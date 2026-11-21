using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>技能与状态说明共用的自适应边框；标题/横线固定，正文独立滚动。</summary>
[DisallowMultipleComponent]
public sealed class BattleSkillInfoView : MonoBehaviour, IPointerClickHandler
{
    public TMP_Text title, level, ap, cooldown, description;
    public ScrollRect scroll;
    public UIAdaptiveFrame frame;
    public RectTransform divider, costs;
    public Vector2 contentPadding = new Vector2(32, 32);
    public float titleTop = 23, headerGap = 8, dividerGap = 13, bodyGap = 28;
    public event Action<string, string> DescriptionLinkClicked;
    private bool statusMode;
    private Vector2 singlePanelCanvasSize;
    private readonly Vector3[] anchorCorners = new Vector3[4];
    private void Awake()
    {
        foreach (var text in new[] { title, level, ap, cooldown, description })
        {
            if (text == null) continue;
            var outline = text.GetComponent<PsdApTextOutline>();
            if (outline != null) outline.enabled = false;
            text.fontStyle = FontStyles.Normal; text.fontWeight = FontWeight.Regular;
        }
    }

    // This handler also consumes non-link panel clicks before they reach the outside-close button.
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || eventData.dragging ||
            !RectTransformUtility.RectangleContainsScreenPoint(scroll.viewport, eventData.position, eventData.pressEventCamera)) return;
        int index = TMP_TextUtilities.FindIntersectingLink(description, eventData.position, eventData.pressEventCamera);
        if (index < 0) return;
        var link = description.textInfo.linkInfo[index];
        string id = link.GetLinkID();
        if (!id.StartsWith("text:", StringComparison.Ordinal)) return;
        DescriptionLinkClicked?.Invoke(id.Substring(5), link.GetLinkText());
        eventData.Use();
    }

    public void Show(string skillName, int skillLevel, int apCost, float cooldownTurns, string body)
    {
        statusMode = false;
        title.richText = true;
        title.text = skillName;
        level.text = "Lv." + skillLevel;
        ap.text = apCost + "AP";
        cooldown.text = cooldownTurns.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "回合";
        SetBody(body, true);
    }

    public void ShowDescription(string name, string body, bool richTitle = false)
    {
        statusMode = true;
        // Only a registered Lang title is rich text; the clicked visible label is otherwise literal.
        title.richText = richTitle;
        title.text = name;
        SetBody(body, false);
    }

    public void ShowStatusInstance(string name, string summary, string body, bool richBody)
    {
        statusMode = true;
        title.richText = false;
        title.text = name;
        level.richText = true;
        level.text = "<size=18>" + summary + "</size>";
        SetBody(body, true, richBody);
    }

    private void SetBody(string body, bool showLevel, bool richBody = true)
    {
        level.gameObject.SetActive(showLevel);
        level.enableWordWrapping = statusMode;
        costs.gameObject.SetActive(!statusMode);
        description.richText = richBody;
        description.raycastTarget = true;
        description.text = body;
        frame.pixelsPerUnitMultiplier = 1f;
        scroll.StopMovement();
    }

    public void Reflow(float width, float maxHeight, bool resetScroll)
    {
        float previous = resetScroll ? 1 : scroll.verticalNormalizedPosition;
        var root = (RectTransform)transform;
        root.localScale = Vector3.one;
        root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        float padding = Mathf.Min(contentPadding.x, width * .2f);
        float textWidth = Mathf.Max(1, width - 2 * padding);
        title.enableWordWrapping = true;
        float titleHeight = Mathf.Max(title.fontSize * 1.2f, title.GetPreferredValues(title.text, textWidth, Mathf.Infinity).y);
        float summaryHeight = statusMode && level.gameObject.activeSelf
            ? level.GetPreferredValues(level.text, textWidth, Mathf.Infinity).y : 0;
        // Keep a readable body even with unusually long translated names or a short Canvas.
        float headerHeight = !statusMode ? 2 * (34 + headerGap) : summaryHeight > 0 ? summaryHeight + headerGap : 0;
        float reserved = headerHeight + dividerGap + divider.rect.height + bodyGap + contentPadding.y + 1;
        titleHeight = Mathf.Min(titleHeight, Mathf.Max(title.fontSize, maxHeight - titleTop - reserved));
        title.overflowMode = TextOverflowModes.Ellipsis;
        Place(title.rectTransform, padding, titleTop, textWidth, titleHeight);
        float bottom = titleTop + titleHeight;
        if (!statusMode)
        {
            float levelWidth = level.GetPreferredValues(level.text, Mathf.Infinity, Mathf.Infinity).x + 4;
            float costWidth = ap.GetPreferredValues(ap.text, Mathf.Infinity, Mathf.Infinity).x
                + cooldown.GetPreferredValues(cooldown.text, Mathf.Infinity, Mathf.Infinity).x + 24 + 8;
            float rowTop = bottom + headerGap;
            Place(level.rectTransform, padding, rowTop, levelWidth, 34);
            if (levelWidth + costWidth + headerGap > textWidth) rowTop += 34 + headerGap;
            Place(costs, width - padding - costWidth, rowTop, costWidth, 34);
            bottom = rowTop + 34;
        }
        else if (summaryHeight > 0)
        {
            Place(level.rectTransform, padding, bottom + headerGap, textWidth, summaryHeight);
            bottom += headerGap + summaryHeight;
        }
        float lineHeight = divider.rect.height;
        Place(divider, padding, bottom + dividerGap, textWidth, lineHeight);
        float bodyTop = Mathf.Min(bottom + dividerGap + lineHeight + bodyGap,
            Mathf.Max(0, maxHeight - contentPadding.y - 1));
        float bodyHeight = Mathf.Max(description.fontSize * 1.2f,
            description.GetPreferredValues(description.text, textWidth, Mathf.Infinity).y + 2);
        float room = Mathf.Max(1, maxHeight - bodyTop - contentPadding.y);
        float viewportHeight = Mathf.Min(bodyHeight, room);
        root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Min(maxHeight, bodyTop + viewportHeight + contentPadding.y));
        Place(scroll.viewport, padding, bodyTop, textWidth, viewportHeight);
        description.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(viewportHeight, bodyHeight));
        scroll.horizontal = false;
        scroll.vertical = bodyHeight > viewportHeight + .5f;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.StopMovement();
        Canvas.ForceUpdateCanvases();
        if (resetScroll) description.rectTransform.anchoredPosition = Vector2.zero;
        scroll.verticalNormalizedPosition = previous;
        description.ForceMeshUpdate();
    }

    internal void LayoutBelowAnchor(RectTransform canvas, RectTransform anchor, BattleInfoPopupStyle style,
        bool contentChanged, bool resetScroll)
    {
        const float gap = 16f;
        Rect bounds = canvas.rect;
        float margin = Mathf.Min(gap, Mathf.Min(bounds.width, bounds.height) * .1f);
        if (contentChanged || singlePanelCanvasSize != bounds.size)
        {
            singlePanelCanvasSize = bounds.size;
            float width = Mathf.Min(style.StatusWidth, Mathf.Max(1, bounds.width - 2 * margin));
            float maxHeight = Mathf.Min(style.StatusMaxHeight, Mathf.Max(1, bounds.height - 2 * margin));
            Reflow(width, maxHeight, resetScroll);
        }

        anchor.GetWorldCorners(anchorCorners);
        Vector2 lower = canvas.InverseTransformPoint(anchorCorners[0]);
        Vector2 upper = canvas.InverseTransformPoint(anchorCorners[2]);
        Vector2 size = ((RectTransform)transform).rect.size;
        float left = (lower.x + upper.x - size.x) * .5f;
        var position = BattleInfoPopupLayout.Place(bounds.xMin + margin, bounds.xMax - margin,
            bounds.yMin + margin, bounds.yMax - margin, left, lower.y - gap, size.x, size.y, 0, 0, 0, false, false);
        PlaceInCanvas(canvas, position.SkillLeft, position.Top);
    }

    internal static RectTransform CreateDismissOverlay(Canvas canvas, string name, UnityEngine.Events.UnityAction dismiss)
    {
        var overlay = (RectTransform)new GameObject(name, typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image), typeof(Button)).transform;
        overlay.SetParent(canvas.transform, false);
        overlay.anchorMin = Vector2.zero;
        overlay.anchorMax = Vector2.one;
        overlay.offsetMin = overlay.offsetMax = Vector2.zero;
        overlay.GetComponent<Image>().color = Color.clear;
        var button = overlay.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(dismiss);
        return overlay;
    }

    internal void PlaceInCanvas(RectTransform canvas, float left, float top)
    {
        var root = (RectTransform)transform;
        Vector2 size = root.rect.size;
        root.position = canvas.TransformPoint(new Vector3(left + size.x * root.pivot.x,
            top - size.y * (1 - root.pivot.y)));
    }

    private static void Place(RectTransform rect, float left, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
    }
}
