using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Reusable, pooled status-name overlay. Callers supply committed text and the actual card rectangle.</summary>
[DisallowMultipleComponent]
public sealed class UIStatusNamePopup : MonoBehaviour
{
    public Canvas canvas;
    public TMP_FontAsset font;
    public int sortingOrderOffset = 110;
    public string Error { get; private set; }
    public bool Ready => isActiveAndEnabled && style != null && canvas != null && font != null;
    private sealed class Item
    {
        public RectTransform Rect, Card;
        public TMP_Text Text;
        public CanvasGroup Visibility;
        public UIStatusNameBackdrop Backdrop;
        public UIStatusNamePopupStyle Style;
        public Vector2 Origin;
        public float CardHeight, StartTime;
        public long Playback;
    }
    private UIStatusNamePopupStyle style;
    private RectTransform layer;
    private readonly List<Item> active = new List<Item>();
    private readonly Stack<Item> pool = new Stack<Item>();
    private Material textMaterial;

    private void OnEnable() => ReloadConfiguration();
    public void Bind(Canvas target, TMP_FontAsset typeface, UIStatusNamePopupStyle parameters = null)
    {
        ReleaseLayer(); canvas = target; font = typeface;
        if (parameters != null) { style = parameters; Error = null; }
        else ReloadConfiguration();
    }
    public void ReloadConfiguration()
    {
        Clear(); ReleaseTextMaterial();
        try { style = UIStatusNamePopupStyle.Load(); Error = null; }
        catch (Exception error)
        {
            style = null; Error = error.Message;
            Debug.LogError("状态名称浮层参数未加载：" + Error, this);
        }
    }

    public bool TryShow(string message, RectTransform card, long playbackID = 0)
    {
        if (!Ready || !style.Enabled || string.IsNullOrWhiteSpace(message) || card == null || !card.gameObject.activeInHierarchy
            || !canvas.isActiveAndEnabled) return false;
        EnsureLayer();
        while (active.Count >= style.MaxVisible) Recycle(0);
        Item item = pool.Count > 0 ? pool.Pop() : CreateItem();
        var text = item.Text;
        text.font = font; text.fontSize = style.FontSize; text.fontStyle = FontStyles.Normal; text.fontWeight = FontWeight.Regular;
        text.fontSharedMaterial = TextMaterial();
        text.UpdateMeshPadding();
        text.color = style.Color; text.text = message; text.richText = false;
        text.enableAutoSizing = false; text.enableWordWrapping = true; text.overflowMode = TextOverflowModes.Overflow;
        // TMP lineSpacing is in em-percent units. Match the CSS line-height instead of adding it to the font's native leading.
        text.lineSpacing = 100 * (style.LineHeight - font.faceInfo.lineHeight / Mathf.Max(1, font.faceInfo.pointSize));
        Vector2 preferred = text.GetPreferredValues(message, Mathf.Infinity, Mathf.Infinity);
        Vector2 size = style.Size(preferred.x, 1);
        item.Rect.sizeDelta = size;
        var content = (RectTransform)text.transform;
        content.anchorMin = Vector2.zero; content.anchorMax = Vector2.one;
        content.offsetMin = new Vector2(style.PaddingX, style.PaddingY);
        content.offsetMax = new Vector2(-style.PaddingX, -style.PaddingY);
        text.ForceMeshUpdate(true);
        size = style.Size(preferred.x, text.textInfo.lineCount);
        item.Rect.sizeDelta = size;

        Rect rect = card.rect;
        Vector3 point = card.TransformPoint(new Vector3(rect.center.x, Mathf.Lerp(rect.yMin, rect.yMax, style.AnchorY), 0));
        Vector2 anchor = layer.InverseTransformPoint(point);
        float height = layer.InverseTransformVector(card.TransformVector(Vector3.up * rect.height)).magnitude;
        float? previousTop = null;
        float now = Time.unscaledTime;
        foreach (var previous in active)
        {
            if (previous.Card != card || previous.Rect == null || now - previous.StartTime >= previous.Style.Lifetime) continue;
            float top = previous.Origin.y + Rise(previous, now) + previous.Rect.sizeDelta.y;
            previousTop = previousTop.HasValue ? Mathf.Max(previousTop.Value, top) : top;
        }
        item.Card = card; item.Style = style; item.CardHeight = height; item.Playback = playbackID; item.StartTime = now;
        item.Origin = new Vector2(anchor.x + style.OffsetX, style.Bottom(anchor.y, height, previousTop));
        item.Rect.anchoredPosition = item.Origin; item.Backdrop.Configure(style);
        item.Visibility.alpha = style.Alpha(0); item.Rect.gameObject.SetActive(true); item.Rect.SetAsLastSibling();
        active.Add(item); return true;
    }

    private static float Rise(Item item, float now)
        => item.CardHeight * item.Style.RiseHeightRatio * item.Style.RiseProgress(now - item.StartTime);
    private void LateUpdate()
    {
        float now = Time.unscaledTime;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var item = active[i];
            float age = now - item.StartTime;
            if (item.Rect == null || item.Card == null || !item.Card.gameObject.activeInHierarchy || age >= item.Style.Lifetime)
            { Recycle(i); continue; }
            item.Rect.anchoredPosition = item.Origin + Vector2.up * Rise(item, now);
            item.Visibility.alpha = item.Style.Alpha(age);
        }
    }

    public void Cancel(long playbackID)
    {
        for (int i = active.Count - 1; i >= 0; i--)
            if (playbackID < 0 || active[i].Playback == playbackID) Recycle(i);
    }
    public void Clear() => Cancel(-1);
    private void Recycle(int index)
    {
        var item = active[index]; active.RemoveAt(index);
        if (item.Rect == null) return;
        item.Rect.gameObject.SetActive(false); item.Card = null; pool.Push(item);
    }
    private void EnsureLayer()
    {
        if (layer != null) return;
        layer = Node("StatusNamePopupLayer", canvas.transform);
        Stretch(layer);
        var overlay = layer.gameObject.AddComponent<Canvas>(); overlay.overrideSorting = true;
        overlay.sortingLayerID = canvas.sortingLayerID; overlay.sortingOrder = canvas.sortingOrder + sortingOrderOffset;
    }
    private Item CreateItem()
    {
        var rect = Node("Status name", layer);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.pivot = new Vector2(.5f, 0);
        var visibility = rect.gameObject.AddComponent<CanvasGroup>(); visibility.blocksRaycasts = visibility.interactable = false;
        var background = Node("Fading gold background", rect); Stretch(background);
        var backdrop = background.gameObject.AddComponent<UIStatusNameBackdrop>();
        var content = Node("Status text", rect);
        var text = content.gameObject.AddComponent<TextMeshProUGUI>();
        text.raycastTarget = false; text.maskable = false; text.alignment = TextAlignmentOptions.Center;
        return new Item { Rect = rect, Text = text, Backdrop = backdrop, Visibility = visibility };
    }
    private Material TextMaterial()
    {
        if (textMaterial != null) return textMaterial;
        var source = font.material;
        if (source == null || !source.HasProperty(ShaderUtilities.ID_UnderlayColor)) return source;
        textMaterial = new Material(source) { name = "Status name shadow", hideFlags = HideFlags.HideAndDontSave };
        textMaterial.EnableKeyword("UNDERLAY_ON");
        textMaterial.SetColor(ShaderUtilities.ID_UnderlayColor, new Color32(36, 27, 22, 128));
        float scale = Mathf.Max(.001f, source.GetFloat(ShaderUtilities.ID_GradientScale) * style.FontSize / Mathf.Max(1, font.faceInfo.pointSize));
        textMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0);
        textMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -1 / scale);
        textMaterial.SetFloat(ShaderUtilities.ID_UnderlaySoftness, Mathf.Clamp01(2 / scale));
        return textMaterial;
    }
    private static RectTransform Node(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.gameObject.layer = parent.gameObject.layer; return rect;
    }
    private static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    private void ReleaseTextMaterial()
    { if (textMaterial != null) Destroy(textMaterial); textMaterial = null; }
    private void ReleaseLayer()
    {
        Clear(); pool.Clear(); ReleaseTextMaterial();
        if (layer != null) { layer.gameObject.SetActive(false); Destroy(layer.gameObject); }
        layer = null;
    }
    private void OnDisable() => ReleaseLayer();
    private void OnDestroy() => ReleaseLayer();
}
