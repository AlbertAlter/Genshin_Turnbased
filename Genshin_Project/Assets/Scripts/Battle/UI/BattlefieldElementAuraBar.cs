using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>The first image is the editable PSD anchor. Top rows grow down; bottom rows grow up.</summary>
[DisallowMultipleComponent]
public sealed class BattlefieldElementAuraBar : MonoBehaviour
{
    public BattleSide side;
    public int slot;
    public bool topAligned;
    [Min(0)] public float gap = 4;
    public Image[] icons;
    private bool initialized;
    private Image[] positionedIcons;
    private RectTransform positionedAnchor;
    private readonly List<ElementAuraMotionIcon> motionIcons = new List<ElementAuraMotionIcon>();
    private ElementAuraCollisionFlash collisionFlash;
    public float IconStep => icons != null && icons.Length > 0 && icons[0] != null
        ? icons[0].rectTransform.rect.height + gap : 45;
    private (Vector2 min, Vector2 max, Vector2 pivot, Vector2 size, Vector3 scale,
        Quaternion rotation, Vector2 position, float step) lastLayout;

    public void Initialize()
    {
        if (initialized || icons == null || icons.Length == 0 || icons[0] == null || icons[0].rectTransform == null) return;
        initialized = true;
        var items = new List<Image>(icons);
        // Allocate before target-selection caches the graphics, including initially hidden slots.
        while (items.Count < ElementQuery.Elements.Count)
        {
            var go = new GameObject("ElementAura" + (items.Count + 1), typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = icons[0].gameObject.layer;
            go.transform.SetParent(icons[0].transform.parent, false);
            items.Add(go.GetComponent<Image>());
        }
        icons = items.ToArray();
        foreach (var icon in icons)
        {
            if (icon == null) continue;
            var reference = icon.GetComponent<StreamingSpriteReference>();
            if (reference != null) { reference.loadOnEnable = false; reference.enabled = false; }
            icon.sprite = null;
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            icon.enabled = false;
            icon.gameObject.SetActive(false);
        }
        Layout();
        // 在选择显示缓存Graphic前分配表现池，运行中的高频附着不再创建图形。
        EnsureMotionPool(ElementQuery.Elements.Count * 2 + 1);
        HideMotion();
    }

    private bool Layout()
    {
        if (icons == null || icons.Length == 0 || icons[0] == null) return false;
        RectTransform first = icons[0].rectTransform;
        if (first == null) return false;
        var layout = (min: first.anchorMin, max: first.anchorMax, pivot: first.pivot, size: first.sizeDelta,
            scale: first.localScale, rotation: first.localRotation, position: first.anchoredPosition,
            step: (topAligned ? -1 : 1) * (first.rect.height + gap));
        if (positionedIcons == icons && positionedAnchor == first && lastLayout.Equals(layout)) return true;
        positionedIcons = icons;
        positionedAnchor = first;
        lastLayout = layout;
        for (int i = 1; i < icons.Length; i++)
        {
            if (icons[i] == null) continue;
            RectTransform rect = icons[i].rectTransform;
            if (rect == null) continue;
            rect.anchorMin = layout.min; rect.anchorMax = layout.max; rect.pivot = layout.pivot;
            rect.sizeDelta = layout.size; rect.localScale = layout.scale; rect.localRotation = layout.rotation;
            rect.anchoredPosition = layout.position + Vector2.up * (i * layout.step);
        }
        return true;
    }

    /// <summary>Hide surviving images without initializing or laying out UI during teardown.</summary>
    public void Clear()
    {
        HideMotion();
        if (icons == null) return;
        foreach (var icon in icons)
        {
            if (icon == null) continue;
            icon.sprite = null;
            icon.enabled = false;
            if (icon.gameObject.activeSelf) icon.gameObject.SetActive(false);
        }
    }

    public void Present(IReadOnlyList<string> elements, IReadOnlyDictionary<string, Sprite> sprites, ElementColorPalette palette = null)
    {
        HideMotion();
        if (elements == null || elements.Count == 0 || sprites == null) { Clear(); return; }
        Initialize();
        if (!initialized || !Layout()) { Clear(); return; }
        for (int i = 0; i < icons.Length; i++)
        {
            if (icons[i] == null || icons[i].rectTransform == null) continue;
            Sprite sprite = null;
            bool visible = i < elements.Count && sprites.TryGetValue(elements[i], out sprite) && sprite != null;
            icons[i].sprite = visible ? sprite : null;
            if (visible && palette != null) icons[i].color = palette.GetColor(elements[i]);
            icons[i].enabled = visible;
            if (icons[i].gameObject.activeSelf != visible) icons[i].gameObject.SetActive(visible);
        }
    }

    private void HideMotion()
    {
        foreach (var icon in motionIcons) if (icon != null) icon.gameObject.SetActive(false);
        if (collisionFlash != null) collisionFlash.gameObject.SetActive(false);
    }
    private void EnsureMotionPool(int count)
    {
        while (motionIcons.Count < count)
        {
            var go = new GameObject("ElementAuraMotion", typeof(RectTransform), typeof(CanvasRenderer), typeof(ElementAuraMotionIcon));
            go.layer = icons[0].gameObject.layer; go.transform.SetParent(icons[0].transform.parent, false);
            var graphic = go.GetComponent<ElementAuraMotionIcon>(); graphic.raycastTarget = false;
            motionIcons.Add(graphic);
        }
        if (collisionFlash == null)
        {
            var go = new GameObject("ElementAuraCollision", typeof(RectTransform), typeof(CanvasRenderer), typeof(ElementAuraCollisionFlash));
            go.layer = icons[0].gameObject.layer; go.transform.SetParent(icons[0].transform.parent, false);
            collisionFlash = go.GetComponent<ElementAuraCollisionFlash>(); collisionFlash.raycastTarget = false;
        }
    }
    private void Place(RectTransform rect, float x, float y, float scale)
    {
        var first = icons[0].rectTransform;
        rect.anchorMin = first.anchorMin; rect.anchorMax = first.anchorMax; rect.pivot = first.pivot;
        rect.sizeDelta = first.sizeDelta; rect.localRotation = first.localRotation;
        rect.localScale = first.localScale * scale;
        // 首图标永远保留为可编辑锚点，只移动单独的表现节点。
        rect.anchoredPosition = first.anchoredPosition + new Vector2((side == BattleSide.Ally ? 1 : -1) * x,
            (topAligned ? -1 : 1) * y);
    }
    public void PresentMotion(IReadOnlyList<ElementAuraMotion.Icon> frame, IReadOnlyDictionary<string, Sprite> sprites,
        ElementColorPalette palette, float flash, float collisionY, string flashElement, float brightness)
    {
        Initialize();
        if (!initialized || !Layout() || palette == null) { Clear(); return; }
        foreach (var icon in icons) if (icon != null) { icon.enabled = false; icon.gameObject.SetActive(false); }
        EnsureMotionPool(frame.Count);
        for (int i = 0; i < motionIcons.Count; i++)
        {
            var icon = motionIcons[i]; if (icon == null) continue;
            Sprite sprite = null;
            bool visible = i < frame.Count && frame[i].Opacity > 0 && sprites.TryGetValue(frame[i].Element, out sprite) && sprite != null;
            icon.gameObject.SetActive(visible); if (!visible) continue;
            var pose = frame[i]; Place(icon.rectTransform, pose.X, pose.Y, pose.Scale);
            icon.Set(sprite, palette.GetColor(pose.Element), pose.Glow, pose.Opacity, brightness);
        }
        if (collisionFlash != null)
        {
            collisionFlash.gameObject.SetActive(flash > 0);
            if (flash > 0) { Place(collisionFlash.rectTransform, 4, collisionY, 1); collisionFlash.Set(flash, palette.GetColor(flashElement)); }
        }
    }
    private void OnDestroy()
    {
        foreach (var icon in motionIcons) if (icon != null) Destroy(icon.gameObject);
        motionIcons.Clear(); if (collisionFlash != null) Destroy(collisionFlash.gameObject);
    }
}
