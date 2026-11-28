using System;
using UnityEngine;

public sealed partial class UIBattleEffectPlayer
{
    private RectTransform defaultBehind, defaultAbove;
    private RectTransform ownedLayerHost, ownedBehind, ownedAbove;

    private void ConfigureEffectLayers()
    {
        if (settings == null) return;
        if (!BattleEffectParameters.UsesHolderLayers(settings.Kind))
        {
            back.SetMount(defaultBehind); front.SetMount(defaultAbove);
            ReleaseOwnedLayers(); return;
        }
        if (targets.Count != 1) throw new ArgumentException("自身环绕特效需要一个实际宿主。");
        var owner = targets[0];
        // The caller supplied this exact card root; no scene search or character-ID inference.
        var card = owner.GetComponent<UICardFrameView>();
        if (card != null && card.BackEffects != null && card.FrontEffects != null)
        {
            back.SetMount(card.BackEffects); front.SetMount(card.FrontEffects);
            ReleaseOwnedLayers(); return;
        }
        // Static/proxy cards still get layers inside their own root, never above all cards.
        if (ownedLayerHost != owner || ownedBehind == null || ownedAbove == null)
        {
            var behind = CreateHolderLayer(owner, true);
            var above = CreateHolderLayer(owner, false);
            back.SetMount(behind); front.SetMount(above);
            ReleaseOwnedLayers();
            ownedLayerHost = owner; ownedBehind = behind; ownedAbove = above;
        }
        else { back.SetMount(ownedBehind); front.SetMount(ownedAbove); }
    }
    private static RectTransform CreateHolderLayer(RectTransform owner, bool behind)
    {
        var layer = new GameObject(behind ? "NativeEffectsBehind" : "NativeEffectsFront", typeof(RectTransform)).GetComponent<RectTransform>();
        layer.SetParent(owner, false);
        layer.anchorMin = Vector2.zero; layer.anchorMax = Vector2.one;
        layer.offsetMin = layer.offsetMax = Vector2.zero;
        if (behind) layer.SetAsFirstSibling(); else layer.SetAsLastSibling();
        return layer;
    }
    private void ReleaseOwnedLayers()
    {
        if (ownedBehind != null) Destroy(ownedBehind.gameObject);
        if (ownedAbove != null) Destroy(ownedAbove.gameObject);
        ownedLayerHost = ownedBehind = ownedAbove = null;
    }
    private void ReleaseEffectLayers()
    { ReleaseOwnedLayers(); defaultBehind = defaultAbove = null; }
}
