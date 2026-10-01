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
        var session = new Session { Player = _player };
        _running = session;
        CurrentSession.Value = session;
        try { await RunInline(execute); }
        finally
        {
            CurrentSession.Value = null;
            if (ReferenceEquals(_running, session)) _running = null;
            session.Cancellation.Dispose();
        }
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
        Func<Task> saved = async () =>
        {
            using (context.Enter()) await execute();
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
    public static void ThrowIfCancelled() => CurrentSession.Value?.Cancellation.Token.ThrowIfCancellationRequested();

    public static void SkipPresentations()
    {
        Session session = _running;
        if (session == null) return;
        session.Skip = true;
        session.Gate?.Skip();
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
