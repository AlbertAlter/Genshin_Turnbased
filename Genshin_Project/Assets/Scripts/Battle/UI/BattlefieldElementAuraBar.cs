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

    public void Initialize()
    {
        if (initialized || icons == null || icons.Length == 0 || icons[0] == null) return;
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

    private void Layout()
    {
        RectTransform first = icons[0].rectTransform;
        for (int i = 1; i < icons.Length; i++)
        {
            RectTransform rect = icons[i].rectTransform;
            rect.anchorMin = first.anchorMin; rect.anchorMax = first.anchorMax; rect.pivot = first.pivot;
            rect.sizeDelta = first.sizeDelta; rect.localScale = first.localScale; rect.localRotation = first.localRotation;
            rect.anchoredPosition = first.anchoredPosition + new Vector2(0, (topAligned ? -1 : 1) * i * (first.rect.height + gap));
        }
    }

    public void Present(IReadOnlyList<string> elements, IReadOnlyDictionary<string, Sprite> sprites)
    {
        Initialize();
        if (!initialized) return;
        Layout();
        for (int i = 0; i < icons.Length; i++)
        {
            Sprite sprite = null;
            bool visible = elements != null && i < elements.Count && sprites.TryGetValue(elements[i], out sprite) && sprite != null;
            icons[i].sprite = visible ? sprite : null;
            icons[i].enabled = visible;
            if (icons[i].gameObject.activeSelf != visible) icons[i].gameObject.SetActive(visible);
        }
    }
}
