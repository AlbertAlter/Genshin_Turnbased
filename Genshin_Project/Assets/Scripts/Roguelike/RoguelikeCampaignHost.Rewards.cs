using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

public sealed partial class RoguelikeCampaignHost
{
    private UIRewardPopup rewardPopup;
    private Coroutine rewardPresentation;
    private bool rewardsWaiting;

    private void BeginBattleRewards()
    {
        if (rewardPresentation != null) return;
        screen.Clear(); rewardsWaiting = true;
        rewardPresentation = StartCoroutine(PresentBattleRewards());
    }
    private IEnumerator PresentBattleRewards()
    {
        yield return null;
        var runtime = RoguelikeBattleSceneEntry.Current?.runtime;
        var player = runtime != null ? runtime.GetComponent<BattlefieldPresentationPlayer>() : null;
        var tails = runtime != null ? runtime.GetComponents<MonoBehaviour>() : Array.Empty<MonoBehaviour>();
        // Ended already follows resolution presentations; also wait for finite fire-and-forget tails.
        while (loading || BattleExecution.IsBusy || player != null && player.HasPendingResults ||
            tails.Any(view => view != null && view.isActiveAndEnabled && view is IBattleVisualCompletion tail && tail.HasPendingVisuals))
            yield return null;
        RoguelikeSaveResult saved;
        RoguelikeFlowContext completed;
        UIRewardPopupItem[] display = Array.Empty<UIRewardPopupItem>();
        try
        {
            completed = Read();
            var granted = completed.Snapshot.PendingNodeReward?.Items;
            if (granted != null && granted.Count > 0) display = BuildRewardItems(granted);
            saved = Game.ReturnToRoute(completed.Flow.StepId);
        }
        catch (Exception error)
        {
            rewardPresentation = null; rewardsWaiting = false;
            screen.Notice(error.Message); screen.Choice(L("btn_main_retry_save"), BeginBattleRewards); yield break;
        }
        if (!saved.Succeeded)
        {
            rewardPresentation = null; rewardsWaiting = false;
            screen.Notice(saved.Message);
            screen.Choice(L("btn_main_retry_save"), BeginBattleRewards);
            screen.Choice(L("btn_inventory"), () => Roster(RosterPage.Bag));
            yield break;
        }
        // 事件的额外奖励只登记返回路线后的入口；本场普通战斗奖励仍在战斗场景内显示。
        QueueEventRewardForRoute(completed);
        if (rewardPopup != null)
        {
            if (rewardPopup.TryShowBattleResult(display, L(completed.Flow.BattleDefeated ? "ui_battle_flow_defeat" : "ui_battle_flow_victory"), BattleResultDetail(runtime), L("ui_reward_dismiss_hint")))
            {
                while (rewardPopup != null && rewardPopup.IsOpen && !rewardPopup.IsReady) yield return null;
                var voice = runtime != null ? runtime.GetComponent<CharacterBattleVoice>() : null;
                if (!completed.Flow.BattleDefeated && rewardPopup != null && rewardPopup.IsReady) voice?.PlayRewardVoice();
                while (rewardPopup != null && rewardPopup.IsOpen || voice != null && voice.IsRewardVoicePlaying) yield return null;
            }
            else RewardPresentationFailed("Reward popup did not accept the display request.");
        }
        rewardPresentation = null; rewardsWaiting = false; BattleControls();
    }

    private string BattleResultDetail(BattleSceneRuntime runtime)
    {
        var manager = runtime != null ? runtime.GetComponent<BattleManager>() : null;
        if (manager == null) return "";
        return DataManager.Instance.GetLangText(manager.EndedByRoundLimit ? "ui_battle_flow_limit_reached" : "ui_battle_flow_turn", english, manager.TurnCount);
    }
    private UIRewardPopupItem[] BuildRewardItems(IReadOnlyList<RoguelikeItemStackData> granted)
    {
        var data = DataManager.Instance;
        var display = new UIRewardPopupItem[granted.Count];
        for (int i = 0; i < granted.Count; i++)
        {
            var stack = granted[i];
            if (!Game.Items.TryGetItem(stack.ItemId, out var item) || item == null)
                throw new InvalidOperationException("Reward display item is missing from the bound catalog: " + stack.ItemId);
            string nameKey = "item_" + item.ItemId + "_name";
            string name = data.LangDict.ContainsKey(nameKey) ? L(nameKey) : item.Name;
            var kind = item.ItemType == RoguelikeItemType.Weapon ? UIItemListIcon.IconKind.Weapon :
                item.ItemType == RoguelikeItemType.Artifact ? UIItemListIcon.IconKind.Artifact : UIItemListIcon.IconKind.Material;
            display[i] = new UIRewardPopupItem(name, item.Icon, stack.Quantity.ToString("N0", CultureInfo.InvariantCulture), item.Rarity, kind);
        }
        return display;
    }

    // This adapter runs only AFTER ReturnToRoute has committed. Showing or closing never changes that transaction.
    private void PresentNodeRewards(IReadOnlyList<RoguelikeItemStackData> granted, bool? defeated = null)
    {
        if (rewardPopup == null || (granted == null || granted.Count == 0) && !defeated.HasValue) return;
        var display = granted == null ? Array.Empty<UIRewardPopupItem>() : BuildRewardItems(granted);
        rewardsWaiting = true;
        rewardPresentation = StartCoroutine(PresentWhenReady(rewardPopup, display, defeated));
    }

    private IEnumerator PresentWhenReady(UIRewardPopup view, UIRewardPopupItem[] display, bool? defeated)
    {
        // Route assets and the existing scene transition finish behind this purely visual request.
        while (view != null && loading) yield return null;
        float deadline = Time.realtimeSinceStartup + 25;
        while (view != null && screen != null && !screen.Ready && Time.realtimeSinceStartup < deadline) yield return null;
        rewardsWaiting = false; rewardPresentation = null;
        if (view == null || loaded != home) yield break;
        bool shown = defeated.HasValue
            ? view.TryShowBattleResult(display, L(defeated.Value ? "ui_battle_flow_defeat" : "ui_battle_flow_victory"), "", L("ui_reward_dismiss_hint"))
            : view.TryShow(display, L("ui_reward_obtained"), L("ui_reward_dismiss_hint"));
        if (!shown)
            RewardPresentationFailed("Reward popup did not accept the display request.");
    }

    private void RewardPresentationFailed(string message)
    {
        Debug.LogWarning(message, this); screen?.Notice(message);
    }

    private void ReleaseRewardPresentation()
    {
        if (rewardPresentation != null) StopCoroutine(rewardPresentation);
        rewardPresentation = null; rewardsWaiting = false;
        if (rewardPopup != null) rewardPopup.Failed -= RewardPresentationFailed;
        rewardPopup = null;
    }
}
