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
        if (icons == null) return;
        foreach (var icon in icons)
        {
            if (icon == null) continue;
            icon.sprite = null;
            icon.enabled = false;
            if (icon.gameObject.activeSelf) icon.gameObject.SetActive(false);
        }
    }

    public void Present(IReadOnlyList<string> elements, IReadOnlyDictionary<string, Sprite> sprites)
    {
        if (elements == null || elements.Count == 0 || sprites == null) { Clear(); return; }
        Initialize();
        if (!initialized || !Layout()) { Clear(); return; }
        for (int i = 0; i < icons.Length; i++)
        {
            if (icons[i] == null || icons[i].rectTransform == null) continue;
            Sprite sprite = null;
            bool visible = i < elements.Count && sprites.TryGetValue(elements[i], out sprite) && sprite != null;
            icons[i].sprite = visible ? sprite : null;
            icons[i].enabled = visible;
            if (icons[i].gameObject.activeSelf != visible) icons[i].gameObject.SetActive(visible);
        }
    }
}
