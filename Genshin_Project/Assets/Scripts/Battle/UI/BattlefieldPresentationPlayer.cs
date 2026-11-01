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
    [Tooltip("留空使用 Resources/UI/DefaultSkillTimeline。命中时点沿用运行时 Excel。")]
    public BattleSkillTimelineTemplate skillTimeline;
    public BattlefieldSelectionPresentation selectionPresentation;
    private int playbackGeneration;
    public bool VoicesEnabled => skillTimeline == null || (skillTimeline.tracks & BattleSkillTracks.Voice) != 0;
    public BattleActionStyle ActionStyle => configuration?.ActionStyle;

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
        if (skillTimeline == null) skillTimeline = Resources.Load<BattleSkillTimelineTemplate>(BattleSkillTimelineTemplate.DefaultResource);
        if (skillTimeline == null) Debug.LogError("缺少默认技能时间轴模板，临时使用公共默认轨道。", this);
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

    public async Task PlayResultsAsync(BattlePresentationSequence sequence, CancellationToken cancellation)
    {
        if (!isActiveAndEnabled || playback == null) return;
        int generation = playbackGeneration;
        if (selectionPresentation != null)
            await selectionPresentation.WaitForSelectionExit(configuration.SelectionExitSeconds, cancellation);
        cancellation.ThrowIfCancellationRequested();
        if (generation != playbackGeneration || !isActiveAndEnabled || playback == null) return;
        var cues = skillTimeline != null ? skillTimeline.Build(sequence, configuration.For)
            : BattleSkillTimeline.Build(sequence, configuration.For, BattleSkillTracks.All, BattleVoiceAnchor.CastStart);
        bool voicePrepared = false;
        if (VoicesEnabled)
            foreach (var view in views)
                if (view is ICharacterVoiceSequenceView voice && (!(view is MonoBehaviour component) || component != null && component.isActiveAndEnabled))
                {
                    voicePrepared = true;
                    voice.PrepareVoices(cues);
                }
        if (!voicePrepared && LogManager.IsEnabled(LogCategory.Voice))
            foreach (var cue in cues)
                if (cue.SkillMarker == BattleSkillMarker.CastStart && cue.Result.TargetSide == BattleSide.Ally && cue.Result.Data is ActionEventData action)
                    LogManager.Log(LogCategory.Voice, $"跳过 {action.SubjectId} {action.SkillId}，" +
                        (VoicesEnabled ? "场景没有启用并注册的角色语音组件。" : "时间轴关闭了语音轨道。"));
        await playback.Enqueue(cues, cancellation);
    }
    public void SkipResults()
    {
        playbackGeneration++;
        selectionPresentation?.SkipSelectionExit();
        playback?.Skip();
    }
    public void CancelResults()
    {
        playbackGeneration++;
        selectionPresentation?.SkipSelectionExit();
        playback?.Cancel();
    }

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
        var targets = views.FindAll(view => BattleSkillTimeline.Receives(view, cue)
            && (!(view is MonoBehaviour component) || component != null && component.isActiveAndEnabled)).ToArray();
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
