using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Pure presentation data. Neither this item nor the popup grants inventory or saves progress.</summary>
public sealed class UIRewardPopupItem
{
    public readonly string Name, IconPath, Value;
    public readonly int Rarity;
    public readonly UIItemListIcon.IconKind Kind;

    public UIRewardPopupItem(string name, string iconPath, string value, int rarity,
        UIItemListIcon.IconKind kind = UIItemListIcon.IconKind.Material)
    {
        Name = name ?? ""; IconPath = iconPath; Value = value ?? "";
        Rarity = Mathf.Clamp(rarity, 0, 5); Kind = kind;
    }
}

/// <summary>Reusable obtained-items overlay. Explicit Show/Close commands only; no game service or save dependency.</summary>
[DisallowMultipleComponent]
public sealed class UIRewardPopup : MonoBehaviour
{
    [SerializeField] private RectTransform headingMount, itemsViewport, hintMount;
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private float iconSize = 132, nameHeight = 52, nameFontSize = 23;
    [SerializeField] private Vector2 spacing = new Vector2(24, 24);
    [SerializeField] private Color dimColor = new Color(0.07f, 0.06f, 0.05f, .78f);
    [SerializeField] private Color textColor = new Color32(238, 224, 196, 255);
    [SerializeField] private Color titleColor = new Color32(233, 207, 158, 255);
    [SerializeField] private float fadeSeconds = .18f;

    public bool IsOpen { get; private set; }
    public bool IsReady => IsOpen && prepared;
    public string LastError { get; private set; }
    public event Action Closed;
    public event Action<string> Failed;

    private readonly Dictionary<string, Sprite> images = new Dictionary<string, Sprite>(StringComparer.Ordinal);
    private readonly List<RectTransform> cells = new List<RectTransform>();
    private UIRewardPopupItem[] items;
    private CanvasGroup group;
    private RectTransform content;
    private ScrollRect scroll;
    private Button dismiss;
    private TMP_Text heading, hint;
    private Coroutine request;
    private bool prepared, closing;
    private Vector2 lastViewportSize;
    private static readonly string[] TileArt = { UIItemListIcon.IconMaskPath, UIItemListIcon.ValueBarPath, UIItemListIcon.SelectionOutlinePath };

    private void Awake()
    {
        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0; group.blocksRaycasts = group.interactable = false;
        var shade = gameObject.AddComponent<Image>(); shade.color = dimColor;
        dismiss = gameObject.AddComponent<Button>(); dismiss.targetGraphic = shade;
        dismiss.transition = Selectable.Transition.None;
        dismiss.navigation = new Navigation { mode = Navigation.Mode.None };
        dismiss.onClick.AddListener(Close);
    }

    /// <summary>Returns whether a request was accepted. Busy/disabled/unbound views reject it without any business side effects.</summary>
    public bool TryShow(IReadOnlyList<UIRewardPopupItem> rewards, string title, string closeHint)
    {
        if (!isActiveAndEnabled || IsOpen || font == null || headingMount == null || itemsViewport == null || hintMount == null ||
            rewards == null || rewards.Count == 0) return false;
        foreach (var item in rewards) if (item == null) return false;
        items = new UIRewardPopupItem[rewards.Count];
        for (int i = 0; i < items.Length; i++) items[i] = rewards[i];
        EnsureContent();
        heading.text = title ?? ""; hint.text = closeHint ?? "";
        LastError = null; IsOpen = true; closing = prepared = false;
        group.alpha = 0; group.blocksRaycasts = group.interactable = true;
        transform.SetAsLastSibling();
        request = StartCoroutine(Prepare());
        return true;
    }

    private void EnsureContent()
    {
        if (heading != null) return;
        heading = Label(headingMount, 44, titleColor);
        hint = Label(hintMount, 24, textColor);
        foreach (bool right in new[] { false, true })
        {
            var line = Rect(headingMount, "Title rule");
            line.anchorMin = new Vector2(right ? .63f : .05f, .5f);
            line.anchorMax = new Vector2(right ? .95f : .37f, .5f);
            line.sizeDelta = new Vector2(0, 1.5f);
            var image = line.gameObject.AddComponent<Image>(); image.color = titleColor * new Color(1, 1, 1, .5f); image.raycastTarget = false;
        }
        var surface = itemsViewport.gameObject.AddComponent<Image>(); surface.color = Color.clear;
        itemsViewport.gameObject.AddComponent<RectMask2D>();
        scroll = itemsViewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = itemsViewport; scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        content = Rect(itemsViewport, "Rewards");
        content.anchorMin = content.anchorMax = content.pivot = new Vector2(.5f, 1);
        scroll.content = content;
    }

    private IEnumerator Prepare()
    {
        foreach (string path in TileArt)
        {
            yield return Load(path);
            if (images[path] == null)
            {
                LastError = "Reward popup tile resource is missing: " + path;
                ResetView(); Failed?.Invoke(LastError); yield break;
            }
        }
        foreach (var item in items)
        {
            yield return Load(UIItemListIcon.GetRarityBackdropPath(item.Rarity));
            yield return Load(item.IconPath);
        }
        Canvas.ForceUpdateCanvases();
        LayoutItems(); prepared = true; request = null;
    }

    private IEnumerator Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || images.ContainsKey(path)) yield break;
        Sprite loaded = null;
        yield return StreamingAssetSpriteLoader.Load(path, value => loaded = value, message => Debug.LogWarning(message, this));
        images[path] = loaded;
    }
    private Sprite ImageFor(string path) => !string.IsNullOrWhiteSpace(path) && images.TryGetValue(path, out var image) ? image : null;

    private void LayoutItems()
    {
        ClearCells();
        float width = Mathf.Max(1, itemsViewport.rect.width), height = Mathf.Max(1, itemsViewport.rect.height);
        float size = Mathf.Min(Mathf.Max(1, iconSize), width), gapX = Mathf.Max(0, spacing.x), gapY = Mathf.Max(0, spacing.y);
        int columns = Mathf.Min(items.Length, Mathf.Max(1, Mathf.FloorToInt((width + gapX) / (size + gapX))));
        int rows = (items.Length + columns - 1) / columns;
        float cellHeight = UIItemListIcon.GetCellSize(size, compact: true).y + nameHeight;
        float total = rows * cellHeight + (rows - 1) * gapY;
        float top = Mathf.Max(0, (height - total) * .5f);
        content.sizeDelta = new Vector2(width, Mathf.Max(height, total));
        content.anchoredPosition = Vector2.zero; scroll.velocity = Vector2.zero;
        for (int i = 0; i < items.Length; i++)
        {
            int row = i / columns, column = i % columns;
            int inRow = Mathf.Min(columns, items.Length - row * columns);
            float rowWidth = inRow * size + (inRow - 1) * gapX;
            var cell = Rect(content, "Reward " + i); cells.Add(cell);
            // Only item cells consume taps; empty space in the viewport can still dismiss the overlay.
            var face = cell.gameObject.AddComponent<Image>(); face.color = Color.clear;
            var blocker = cell.gameObject.AddComponent<Button>(); blocker.targetGraphic = face;
            blocker.transition = Selectable.Transition.None; blocker.navigation = new Navigation { mode = Navigation.Mode.None };
            cell.anchorMin = cell.anchorMax = cell.pivot = new Vector2(.5f, 1);
            cell.sizeDelta = new Vector2(size, cellHeight);
            cell.anchoredPosition = new Vector2(-rowWidth * .5f + size * .5f + column * (size + gapX), -top - row * (cellHeight + gapY));
            var item = items[i];
            var tile = UIItemListIcon.Create(cell, size, font, images[TileArt[0]], images[TileArt[1]], images[TileArt[2]],
                compact: true, tileMask: images[TileArt[0]]);
            var tileRect = (RectTransform)tile.transform;
            tileRect.anchorMin = tileRect.anchorMax = tileRect.pivot = new Vector2(.5f, 1);
            tileRect.anchoredPosition = Vector2.zero;
            tile.Bind(ImageFor(item.IconPath), ImageFor(UIItemListIcon.GetRarityBackdropPath(item.Rarity)), item.Value,
                kind: item.Kind, rarity: item.Rarity);
            var name = Rect(cell, "Name");
            name.anchorMin = new Vector2(0, 0); name.anchorMax = new Vector2(1, 0); name.pivot = new Vector2(.5f, 0);
            name.sizeDelta = new Vector2(0, nameHeight);
            var caption = Label(name, nameFontSize, textColor); caption.text = item.Name;
            caption.overflowMode = TextOverflowModes.Ellipsis;
        }
        lastViewportSize = itemsViewport.rect.size;
    }

    public bool TryNavigateBack()
    {
        if (!IsOpen) return false;
        Close(); return true;
    }
    public void Close()
    {
        if (!IsOpen || closing) return;
        if (request != null) { StopCoroutine(request); request = null; }
        closing = true;
    }
    private void Update()
    {
        if (!IsOpen) return;
        if (prepared && !closing && lastViewportSize != itemsViewport.rect.size) LayoutItems();
        float target = closing ? 0 : prepared ? 1 : 0;
        group.alpha = Mathf.MoveTowards(group.alpha, target, fadeSeconds <= 0 ? 1 : Time.unscaledDeltaTime / fadeSeconds);
        if (closing && group.alpha <= 0)
        {
            ResetView(); Closed?.Invoke();
        }
    }
    private void ResetView()
    {
        IsOpen = prepared = closing = false; request = null;
        if (group != null) { group.alpha = 0; group.blocksRaycasts = group.interactable = false; }
        ClearCells(); items = null;
        foreach (var image in images.Values) if (image != null) { Destroy(image.texture); Destroy(image); }
        images.Clear();
    }
    private void ClearCells()
    {
        foreach (var cell in cells) if (cell != null) { cell.gameObject.SetActive(false); Destroy(cell.gameObject); }
        cells.Clear();
    }
    private void OnDisable()
    {
        if (request != null) StopCoroutine(request);
        ResetView();
    }
    private void OnDestroy() { if (dismiss != null) dismiss.onClick.RemoveListener(Close); }
    private static RectTransform Rect(Transform parent, string name)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false); rect.gameObject.layer = parent.gameObject.layer; return rect;
    }
    private TMP_Text Label(RectTransform rect, float size, Color color)
    {
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.fontSize = size; text.color = color;
        text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false; text.richText = false;
        text.enableWordWrapping = true; return text;
    }
}
