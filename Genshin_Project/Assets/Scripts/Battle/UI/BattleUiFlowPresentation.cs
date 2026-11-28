using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>Explicit battle-to-display adapter. Drains finite visuals, holds its own input lock, never resets combat state.</summary>
[DisallowMultipleComponent]
public sealed class BattleUiFlowPresentation : MonoBehaviour, IBattleVisualCompletion
{
    public BattleUiCombatHudSource source;
    public BattleInputController input;
    public RectTransform mount;
    public UIBattleFlowView viewPrefab;
    public UIRewardPopup resultPrefab;
    public bool deferResultToRewards;
    public Transform[] visualRoots;
    private BattleManager battle;
    private UIBattleFlowView view;
    private UIRewardPopup result;
    private readonly List<TaskCompletionSource<bool>> gates = new();
    private int displayedTurn = -1, displayedLimit = -1;
    private int displayedWave = -1, displayedWaves = -1, displayedEnemies = -1;
    private bool displayedExpired;
    private int unblockFrame = -1;
    private bool displayedEnglish, warned;
    public bool HasPendingVisuals => gates.Count > 0 || view != null && view.HasPendingVisuals;

    public void BindBattle(BattleManager value)
    {
        if (battle == value) return;
        if (battle != null) { battle.FlowPresentation.Requested -= Present; battle.OnBattleEnded -= Ended; }
        Clear(); battle = value; displayedTurn = -1;
        if (isActiveAndEnabled && battle != null) { battle.FlowPresentation.Requested += Present; battle.OnBattleEnded += Ended; }
    }
    private bool EnsureView()
    {
        if (view != null) return true;
        var data = DataManager.Instance;
        if (mount == null || viewPrefab == null || data == null || !data.IsLoaded)
        {
            if (!warned) { warned = true; Debug.LogWarning("战斗流程显示缺少显式布局、Prefab或Lang，跳过可选提示。", this); }
            return false;
        }
        view = Instantiate(viewPrefab, mount, false);
        view.Bind((key, english, args) => data.GetLangText(key, english, args), true);
        view.English = ProjectUserSettings.English(); return true;
    }
    private Task Present(BattleFlowNotice notice, CancellationToken cancellation)
    {
        if (!isActiveAndEnabled || cancellation.IsCancellationRequested || !EnsureView()) return Task.CompletedTask;
        if (notice.Kind == BattleFlowNoticeKind.BattleStart && result != null) { Destroy(result.gameObject); result = null; }
        var gate = new TaskCompletionSource<bool>(); gates.Add(gate);
        input?.SetUiInputBlocked(this, true);
        StartCoroutine(Play(notice, cancellation, gate)); return gate.Task;
    }
    private IEnumerator Play(BattleFlowNotice notice, CancellationToken cancellation, TaskCompletionSource<bool> gate)
    {
        try
        {
            if (notice.Kind != BattleFlowNoticeKind.BattleStart)
            {
                var tails = (visualRoots ?? Array.Empty<Transform>()).Where(root => root != null)
                    .SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true)).Distinct()
                    .Where(component => component != this && component != view && component is IBattleVisualCompletion).ToArray();
                while (!cancellation.IsCancellationRequested && (BattlePause.IsPaused || tails.Any(component =>
                    component != null && component.isActiveAndEnabled && ((IBattleVisualCompletion)component).HasPendingVisuals))) yield return null;
            }
            if (cancellation.IsCancellationRequested || view == null) yield break;
            view.English = ProjectUserSettings.English();
            var playing = view.PlayAsync(notice, cancellation);
            while (!playing.IsCompleted && !cancellation.IsCancellationRequested) yield return null;
            if (playing.IsFaulted) Debug.LogException(playing.Exception, this);
            if (!cancellation.IsCancellationRequested) gate.TrySetResult(true);
        }
        finally
        {
            gates.Remove(gate);
            if (gates.Count == 0) unblockFrame = Time.frameCount + 1;
            if (!gate.Task.IsCompleted) gate.TrySetCanceled();
        }
    }
    private void LateUpdate()
    {
        if (unblockFrame >= 0 && Time.frameCount >= unblockFrame && gates.Count == 0)
        { unblockFrame = -1; input?.SetUiInputBlocked(this, false); }
        if (source?.battle != null && source.battle != battle) BindBattle(source.battle);
        if (battle == null || !EnsureView()) return;
        int turn = battle.IsBattleRunning ? battle.TurnCount : 0;
        bool english = ProjectUserSettings.English(); view.English = english;
        if (turn != displayedTurn || displayedEnglish != english || displayedLimit != battle.MaxRounds || displayedExpired != battle.EndedByRoundLimit)
        {
            displayedTurn = turn; displayedEnglish = english; displayedLimit = battle.MaxRounds; displayedExpired = battle.EndedByRoundLimit;
            view.SetTurn(turn, displayedLimit, displayedExpired);
        }
        if (displayedWave != battle.CurrentWave || displayedWaves != battle.WaveCount || displayedEnemies != battle.RemainingEnemyCount)
        {
            displayedWave = battle.CurrentWave; displayedWaves = battle.WaveCount; displayedEnemies = battle.RemainingEnemyCount;
            view.SetEnemies(displayedWave, displayedWaves, displayedEnemies);
        }
    }
    private void Ended(bool victory)
    {
        if (deferResultToRewards || resultPrefab == null || mount == null) return;
        StartCoroutine(ShowResult(victory, battle.FlowPresentation.Generation));
    }
    private IEnumerator ShowResult(bool victory, int generation)
    {
        var tails = (visualRoots ?? Array.Empty<Transform>()).Where(root => root != null)
            .SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true)).Distinct()
            .Where(component => component != this && component is IBattleVisualCompletion).ToArray();
        while (battle != null && battle.FlowPresentation.Generation == generation && (BattleExecution.IsBusy || tails.Any(component => component != null && component.isActiveAndEnabled
            && ((IBattleVisualCompletion)component).HasPendingVisuals))) yield return null;
        if (battle == null || battle.FlowPresentation.Generation != generation || !battle.IsBattleOver || deferResultToRewards) yield break;
        if (result == null) result = Instantiate(resultPrefab, mount, false);
        var data = DataManager.Instance; bool en = ProjectUserSettings.English();
        result.TryShowBattleResult(Array.Empty<UIRewardPopupItem>(), data.GetLangText(victory ? "ui_battle_flow_victory" : "ui_battle_flow_defeat", en),
            data.GetLangText(battle.EndedByRoundLimit ? "ui_battle_flow_limit_reached" : "ui_battle_flow_turn", en, battle.TurnCount),
            data.GetLangText("ui_reward_dismiss_hint", en));
    }
    private void Clear()
    {
        StopAllCoroutines();
        var old = gates.ToArray(); gates.Clear();
        if (view != null) { view.Clear(); view.SetTurn(0); }
        if (result != null) { Destroy(result.gameObject); result = null; }
        input?.SetUiInputBlocked(this, false);
        foreach (var gate in old) gate.TrySetCanceled();
    }
    private void OnEnable() { var old = battle; battle = null; BindBattle(old ?? source?.battle); }
    private void OnDisable()
    {
        if (battle != null) { battle.FlowPresentation.Requested -= Present; battle.OnBattleEnded -= Ended; }
        Clear(); displayedTurn = -1;
    }
    private void OnDestroy() { if (view != null) Destroy(view.gameObject); }
}
