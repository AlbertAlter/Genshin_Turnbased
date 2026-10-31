using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>可挂接任意战斗场景的结果播放器。与结算、跳字样式、具体特效组件相互独立。</summary>
[DisallowMultipleComponent]
public sealed class BattlefieldPresentationPlayer : MonoBehaviour, IBattleResultPresentationPlayer
{
    private static BattlefieldPresentationPlayer runtimeOwner;
    private sealed class ViewTicket
    {
        public IBattlePresentationView View;
        public long PlaybackID;
        public Action<Exception> Finish;
    }
    private readonly List<IBattlePresentationView> views = new List<IBattlePresentationView>();
    private readonly List<ViewTicket> tickets = new List<ViewTicket>();
    private BattlePresentationPlayback playback;
    private BattlePresentationConfiguration configuration;

    public static void EnsureOn(GameObject host)
    {
        if (Application.isPlaying && host.GetComponent<BattlefieldPresentationPlayer>() == null)
            host.AddComponent<BattlefieldPresentationPlayer>();
    }

    private void OnEnable()
    {
        if (runtimeOwner != null && runtimeOwner != this)
        {
            Debug.LogError("已有场景表现播放器，不能重复接收同一批战斗结果。", this);
            enabled = false; return;
        }
        runtimeOwner = this;
        playback = new BattlePresentationPlayback(Present, CancelViews);
        ReloadConfiguration();
        // Scenes explicitly opt in to views; do not install unrelated UI modules.
        foreach (var component in GetComponents<MonoBehaviour>())
            if (component.isActiveAndEnabled && component is IBattlePresentationView view) RegisterView(view);
        BattlePresentationResults.Unsequenced += OnUnsequenced;
        BattleEventHub.SessionReset += OnSessionReset;
        BattleExecution.TryInstallDefaultPlayer(this);
    }

    public void ReloadConfiguration()
    {
        // 已排队时间轴不重算，避免热更新改变正在播放的顺序。
        try { configuration = BattlePresentationConfiguration.Load(); }
        catch (Exception error)
        {
            configuration = new BattlePresentationConfiguration();
            Debug.LogError("表现配置未加载，沿用当前即时节奏：" + error.Message, this);
        }
    }

    public void RegisterView(IBattlePresentationView view)
    {
        if (view != null && !views.Contains(view)) views.Add(view);
    }
    public void UnregisterView(IBattlePresentationView view)
    {
        views.Remove(view);
        foreach (var ticket in tickets.ToArray())
            if (ReferenceEquals(ticket.View, view))
            {
                try { view.Cancel(ticket.PlaybackID); }
                catch (Exception error) { Debug.LogException(error); }
                ticket.Finish(null); // 组件卸载不能把整个技能流程永久挂起。
            }
    }

    // 业务先结算；通用模板在结果时间轴中安排视觉前摇和命中，不延迟或重算业务。
    public void Begin(BattlePresentationRequest request, Action reachedResolutionPoint) => reachedResolutionPoint();
    public void ShowResult(BattlePresentationRequest request, Action readyToContinue) => readyToContinue();
    public void Cancel(BattlePresentationRequest request) => CancelResults();

    public Task PlayResultsAsync(BattlePresentationSequence sequence, CancellationToken cancellation)
    {
        if (!isActiveAndEnabled || playback == null) return Task.CompletedTask;
        return playback.Enqueue(BattlePresentationTimeline.Build(sequence, configuration.For), cancellation);
    }
    public void SkipResults() => playback?.Skip();
    public void CancelResults() => playback?.Cancel();

    private void OnUnsequenced(BattlePresentationSequence sequence)
        => BattleExecution.Observe(PlayResultsAsync(sequence, CancellationToken.None));
    private void Update()
    {
        if (runtimeOwner != this) return;
        if (!BattleExecution.IsBusy) BattleExecution.TryInstallDefaultPlayer(this);
        playback?.Advance(Mathf.Max(0, Time.unscaledDeltaTime));
    }
    private void OnSessionReset()
    {
        CancelResults();
        // 重置也清除已发出但仍在飘散的文字，-1表示该场景全部表现。
        CancelViews(-1);
        BattleExecution.TryInstallDefaultPlayer(this);
    }

    private void Present(long id, BattlePresentationCue cue, Action<Exception> completed)
    {
        var targets = views.FindAll(view => !cue.IsAnticipation || view is IBattlePresentationAnticipationView).ToArray();
        if (targets.Length == 0) { completed(null); return; }
        int remaining = targets.Length;
        bool closed = false;
        foreach (var view in targets)
        {
            if (closed) break;
            bool once = false;
            var ticket = new ViewTicket { View = view, PlaybackID = id };
            void Finish(Exception error)
            {
                if (once) return;
                once = true;
                tickets.Remove(ticket);
                if (closed) return;
                if (error != null) { closed = true; completed(error); }
                else if (--remaining == 0) { closed = true; completed(null); }
            }
            ticket.Finish = Finish;
            tickets.Add(ticket);
            try { view.Present(id, cue, Finish); }
            catch (Exception error) { Finish(error); }
        }
    }

    private void CancelViews(long id)
    {
        foreach (var view in views.ToArray())
            try { view.Cancel(id); }
            catch (Exception error) { Debug.LogException(error); }
        foreach (var ticket in tickets.ToArray())
            if (id < 0 || ticket.PlaybackID == id) ticket.Finish(null);
    }
    private void OnDisable()
    {
        BattlePresentationResults.Unsequenced -= OnUnsequenced;
        BattleEventHub.SessionReset -= OnSessionReset;
        BattleExecution.ReleasePlayer(this);
        CancelResults(); CancelViews(-1); views.Clear(); tickets.Clear(); playback = null;
        if (runtimeOwner == this) runtimeOwner = null;
    }
}
