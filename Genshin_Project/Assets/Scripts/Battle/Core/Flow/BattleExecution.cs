using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 主线程分步执行器：默认完整同步完成；只有播放器尚未发出信号时才挂起。
/// 每个子流程拥有自己的队列，保证 A 的子链结清后才执行已预占的 B。
/// AsyncLocal 只携带当前异步流程的规则上下文，不把它暴露给等待期间的 UI/其他调用。
/// </summary>
public static class BattleExecution
{
    /// <summary>重复内容仍属于一次效果；共享反应编号和每效果触发额度。</summary>
    public static async Task RepeatEffect(int count, string damageHook, Func<long, Task> execute,
        BattleEntity owner = null, string effectID = null)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        // DamageHook 与布尔 ScriptHook 不同，未实现的处理器不能静默忽略。
        if (!string.IsNullOrWhiteSpace(damageHook))
            throw new NotSupportedException($"DamageHook 尚未实现处理器：{damageHook}");
        using var effect = BattleResolutionContext.BeginEffect();
        long executionID = ReactionResolver.BeginEffectExecution();
        using var declarationExecution = BattleEventHub.BeginEffectExecution(executionID);
        using (BattleSpecialRules.BeginEffectInvocation(owner, effectID, executionID))
        {
            for (int i = 0; i < count; i++)
            {
                ThrowIfCancelled();
                await execute(executionID);
                await Flush();
            }
        }
        // 整效果收尾沿原队列提交的行动，必须在下一条独立效果开始前结清。
        await Flush();
    }

    private sealed class Pending
    {
        public Func<Task> Execute;
        public Action Cancel;
    }

    private sealed class Frame
    {
        public readonly Queue<Pending> Pending = new Queue<Pending>();
        public void Release()
        {
            while (Pending.Count > 0) Pending.Dequeue().Cancel?.Invoke();
        }
    }

    private sealed class Session
    {
        public readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
        public readonly int ThreadID = Thread.CurrentThread.ManagedThreadId;
        public IBattlePresentationPlayer Player;
        public readonly BattlePresentationSequence Results = new BattlePresentationSequence();
        public readonly List<Action> AfterPresentations = new List<Action>();
        public Gate Gate;
        public bool Skip;
    }

    private sealed class Gate : IDisposable
    {
        private readonly Session _session;
        private readonly BattlePresentationRequest _request;
        private readonly TaskCompletionSource<bool> _impact = new TaskCompletionSource<bool>();
        private readonly TaskCompletionSource<bool> _finished = new TaskCompletionSource<bool>();
        private readonly CancellationTokenRegistration _registration;
        private bool _closed;
        public Task Impact => _impact.Task;
        public Task Finished => _finished.Task;

        public Gate(Session session, BattlePresentationRequest request)
        {
            _session = session;
            _request = request;
            _registration = session.Cancellation.Token.Register(() =>
            {
                _closed = true;
                _impact.TrySetCanceled();
                _finished.TrySetCanceled();
            });
        }
        private bool CanSignal()
        {
            if (_closed || _session.Cancellation.IsCancellationRequested) return false;
            if (Thread.CurrentThread.ManagedThreadId != _session.ThreadID)
                throw new InvalidOperationException("Battle presentation signals must run on the battle thread.");
            return true;
        }
        public void ImpactReached() { if (CanSignal()) _impact.TrySetResult(true); }
        public void FinishedPlaying() { if (CanSignal()) _finished.TrySetResult(true); }
        public void Skip()
        {
            if (!CanSignal()) return;
            _session.Player.Cancel(_request);
            _impact.TrySetResult(true);
            _finished.TrySetResult(true);
        }
        public void Dispose() { _closed = true; _registration.Dispose(); }
    }

    private static readonly AsyncLocal<Session> CurrentSession = new AsyncLocal<Session>();
    private static readonly AsyncLocal<Frame> CurrentFrame = new AsyncLocal<Frame>();
    private static readonly AsyncLocal<BattlePresentationRequest> CurrentPresentation = new AsyncLocal<BattlePresentationRequest>();
    private static Session _running;
    private static IBattlePresentationPlayer _player = new ImmediateBattlePresentationPlayer();
    public static bool IsBusy => _running != null;
    public static bool IsCancelled => CurrentSession.Value?.Cancellation.IsCancellationRequested == true;

    public static void SetPlayer(IBattlePresentationPlayer player)
    {
        if (IsBusy) throw new InvalidOperationException("Cannot replace the presentation player during resolution.");
        _player = player ?? new ImmediateBattlePresentationPlayer();
    }

    /// <summary>场景只接管默认空播放器，不覆盖测试或调用者显式注入的播放器。</summary>
    public static bool TryInstallDefaultPlayer(IBattlePresentationPlayer player)
    {
        if (player == null || IsBusy || !(_player is ImmediateBattlePresentationPlayer)) return false;
        _player = player; return true;
    }

    public static void ReleasePlayer(IBattlePresentationPlayer player)
    {
        if (!ReferenceEquals(_player, player)) return;
        Cancel();
        _player = new ImmediateBattlePresentationPlayer();
    }

    public static Task Run(Func<Task> execute)
    {
        if (CurrentSession.Value != null)
        {
            ThrowIfCancelled();
            return RunInline(execute);
        }
        if (IsBusy) throw new InvalidOperationException("A battle resolution is already running.");
        return RunRoot(execute);
    }

    private static async Task RunRoot(Func<Task> execute)
    {
        var resolution = BattleEventHub.BeginResolution();
        var session = new Session { Player = _player };
        Action[] completions = Array.Empty<Action>();
        _running = session;
        CurrentSession.Value = session;
        try
        {
            await RunInline(execute);
            ThrowIfCancelled();
            session.Results.Seal();
            if (session.Player is IBattleResultPresentationPlayer resultPlayer && !session.Skip)
                await resultPlayer.PlayResultsAsync(session.Results, session.Cancellation.Token);
            ThrowIfCancelled();
            completions = session.AfterPresentations.ToArray();
            session.AfterPresentations.Clear();
        }
        catch
        {
            (session.Player as IBattleResultPresentationPlayer)?.CancelResults();
            throw;
        }
        finally
        {
            CurrentSession.Value = null;
            if (ReferenceEquals(_running, session)) _running = null;
            session.Cancellation.Dispose();
            resolution.Dispose();
        }
        // 完成通知不继承已关闭的结果/声明帧，调用者可以安全切场景或开始下一场。
        foreach (Action completion in completions) completion();
    }

    public static async Task RunInline(Func<Task> execute)
    {
        Frame previous = CurrentFrame.Value;
        var frame = new Frame();
        CurrentFrame.Value = frame;
        try
        {
            ThrowIfCancelled();
            await execute();
            await Flush();
        }
        finally { frame.Release(); CurrentFrame.Value = previous; }
    }

    public static void Enqueue(Func<Task> execute, Action cancelled = null)
    {
        if (IsCancelled) { cancelled?.Invoke(); return; }
        var context = BattleResolutionContext.Capture();
        var declarations = BattleEventHub.Capture();
        var deaths = BattleDeathResolution.Capture();
        Func<Task> saved = async () =>
        {
            using (context.Enter())
            using (deaths.Enter())
            using (declarations.Enter()) await execute();
        };
        if (CurrentFrame.Value != null)
            CurrentFrame.Value.Pending.Enqueue(new Pending { Execute = saved, Cancel = cancelled });
        else
            Observe(Run(saved));
    }

    public static async Task Flush()
    {
        Frame frame = CurrentFrame.Value;
        if (frame == null) return;
        while (frame.Pending.Count > 0)
        {
            ThrowIfCancelled();
            Pending pending = frame.Pending.Dequeue();
            try { await RunInline(pending.Execute); }
            finally { pending.Cancel?.Invoke(); }
        }
    }

    public static async Task Play(BattlePresentationRequest request, Action commit)
    {
        Session session = CurrentSession.Value;
        if (session == null)
        {
            await Run(() => Play(request, commit));
            return;
        }
        await RunInline(async () =>
        {
            using (var gate = new Gate(session, request))
            {
                session.Gate = gate;
                try
                {
                    if (session.Skip) gate.ImpactReached();
                    else session.Player.Begin(request, gate.ImpactReached);
                    await gate.Impact;
                    ThrowIfCancelled();
                    BattlePresentationRequest previous = CurrentPresentation.Value;
                    CurrentPresentation.Value = request;
                    try { commit(); }
                    finally { CurrentPresentation.Value = previous; }
                    ThrowIfCancelled();
                    if (session.Skip) gate.FinishedPlaying();
                    else session.Player.ShowResult(request, gate.FinishedPlaying);
                    await gate.Finished;
                    ThrowIfCancelled();
                }
                catch
                {
                    session.Player.Cancel(request);
                    throw;
                }
                finally { if (ReferenceEquals(session.Gate, gate)) session.Gate = null; }
            }
        });
    }

    public static async Task PlayEffect(BattlePresentationRequest request, Action commit)
    {
        using (BattleResolutionContext.BeginEffect()) await Play(request, commit);
    }

    public static void Record(DamageResolvedEvent result) => CurrentPresentation.Value?.Record(result);
    public static void Record(HealResolvedEvent result) => CurrentPresentation.Value?.Record(result);
    public static void Record(ReactionOccurrence result) => CurrentPresentation.Value?.Record(result);
    /// <summary>仅由正式Dispatch入口调用。保存结果，不产生第二条业务响应队列。</summary>
    internal static void RecordCommittedDeclaration(BattleDeclaration declaration)
    {
        Session session = CurrentSession.Value;
        if (session?.Player is IBattleResultPresentationPlayer)
            session.Results.Record(declaration, CurrentPresentation.Value);
        else BattlePresentationResults.Publish(declaration, CurrentPresentation.Value);
    }
    /// <summary>仅用于外部完成通知；实际业务响应仍走原Flush，不挪到表现结束后执行。</summary>
    public static void AfterPresentations(Action completed)
    {
        if (completed == null) return;
        if (CurrentSession.Value?.Player is IBattleResultPresentationPlayer)
            CurrentSession.Value.AfterPresentations.Add(completed);
        else completed();
    }
    public static void ThrowIfCancelled() => CurrentSession.Value?.Cancellation.Token.ThrowIfCancellationRequested();

    public static void SkipPresentations()
    {
        Session session = _running;
        if (session == null) return;
        session.Skip = true;
        session.Gate?.Skip();
        (session.Player as IBattleResultPresentationPlayer)?.SkipResults();
    }

    public static void Cancel()
    {
        Session session = _running;
        if (session == null) return;
        session.Cancellation.Cancel();
    }

    /// <summary>旧 void 入口：即时完成时保留同步异常；异步期间的异常统一报告。</summary>
    public static void Observe(Task task)
    {
        if (task.IsCompleted) { if (!task.IsCanceled) task.GetAwaiter().GetResult(); return; }
        _ = ObserveLater(task);
    }
    private static async Task ObserveLater(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { }
        catch (Exception exception) { Debug.LogException(exception); }
    }

    public static IEnumerator Wait(Task task)
    {
        while (!task.IsCompleted) yield return null;
        if (!task.IsCanceled) task.GetAwaiter().GetResult();
    }
}
