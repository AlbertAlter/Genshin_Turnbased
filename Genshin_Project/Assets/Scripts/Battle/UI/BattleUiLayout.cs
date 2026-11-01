using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class BattleUiCardBinding
{
    public BattleSide side;
    public int slot;
    public RectTransform root;
    public RectTransform art;
    public RectTransform hitsBehind;
    public RectTransform hitsFront;
    public RectTransform shieldLayer;
    public BattlefieldElementAuraBar auraBar;
}

public interface IBattleUiLayout
{
    Canvas Canvas { get; }
    IReadOnlyList<BattleUiCardBinding> Cards { get; }
    bool TryGetCard(BattleSide side, int slot, out BattleUiCardBinding card);
}

/// <summary>Scene-owned references only. Never positions cards, loads stage data or installs UI features.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Battle UI/Layout")]
public sealed class BattleUiLayout : MonoBehaviour, IBattleUiLayout
{
    public Canvas canvas;
    public BattleUiCardBinding[] cards = Array.Empty<BattleUiCardBinding>();
    public RectTransform groupHitsBehind;
    public RectTransform groupHitsFront;
    public RectTransform projectiles;
    public Canvas Canvas => canvas;
    public IReadOnlyList<BattleUiCardBinding> Cards => cards;

    public float? GetCardScreenX(BattleSide side, int slot)
    {
        if (!TryGetCard(side, slot, out var card)) return null;
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        return RectTransformUtility.WorldToScreenPoint(camera, card.art.TransformPoint(card.art.rect.center)).x;
    }

    public bool TryGetCard(BattleSide side, int slot, out BattleUiCardBinding card)
    {
        foreach (var item in cards)
            if (item != null && item.side == side && item.slot == slot)
            { card = item; return item.root != null && item.art != null; }
        card = null; return false;
    }

    public RectTransform HitLayer(BattleUiCardBinding card, bool behind)
    {
        var layer = behind ? card.hitsBehind : card.hitsFront;
        if (layer != null) return layer;
        layer = Stretch(behind ? "ImpactsBehind" : "ImpactsFront", card.art);
        // Behind must be a sibling beneath the card art, not a child drawn over the image.
        if (behind)
        {
            layer.SetParent(card.art.parent, false);
            layer.anchorMin = card.art.anchorMin; layer.anchorMax = card.art.anchorMax;
            layer.pivot = card.art.pivot; layer.sizeDelta = card.art.sizeDelta;
            layer.localScale = card.art.localScale; layer.localRotation = card.art.localRotation;
            layer.anchoredPosition = card.art.anchoredPosition;
            layer.SetSiblingIndex(card.art.GetSiblingIndex());
        }
        if (behind) card.hitsBehind = layer; else card.hitsFront = layer;
        return layer;
    }

    public RectTransform GroupHitLayer(bool behind)
    {
        var layer = behind ? groupHitsBehind : groupHitsFront;
        if (layer != null || canvas == null) return layer;
        layer = Stretch(behind ? "GroupImpactsBehind" : "GroupImpactsFront", canvas.transform);
        if (behind)
        {
            int index = layer.GetSiblingIndex();
            foreach (var card in cards)
            {
                if (card?.root == null) continue;
                Transform top = card.root;
                while (top.parent != null && top.parent != canvas.transform) top = top.parent;
                if (top.parent == canvas.transform) index = Mathf.Min(index, top.GetSiblingIndex());
            }
            layer.SetSiblingIndex(index);
        }
        if (behind) groupHitsBehind = layer; else groupHitsFront = layer;
        return layer;
    }
    public RectTransform ProjectileLayer()
        => projectiles != null ? projectiles : projectiles = Stretch("Projectiles", canvas.transform);

    private static RectTransform Stretch(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero; return rect;
    }
}
