using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public sealed partial class RoguelikeCampaignHost
{
    private UIRewardPopup rewardPopup;
    private Coroutine rewardPresentation;
    private bool rewardsWaiting;

    // This adapter runs only AFTER ReturnToRoute has committed. Showing or closing never changes that transaction.
    private void PresentNodeRewards(IReadOnlyList<RoguelikeItemStackData> granted)
    {
        if (rewardPopup == null || granted == null || granted.Count == 0) return;
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
        rewardsWaiting = true;
        rewardPresentation = StartCoroutine(PresentWhenReady(rewardPopup, display));
    }

    private IEnumerator PresentWhenReady(UIRewardPopup view, UIRewardPopupItem[] display)
    {
        // Route assets and the existing scene transition finish behind this purely visual request.
        while (view != null && loading) yield return null;
        float deadline = Time.realtimeSinceStartup + 25;
        while (view != null && screen != null && !screen.Ready && Time.realtimeSinceStartup < deadline) yield return null;
        rewardsWaiting = false; rewardPresentation = null;
        if (view == null || loaded != home) yield break;
        if (!view.TryShow(display, L("ui_reward_obtained"), L("ui_reward_dismiss_hint")))
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
