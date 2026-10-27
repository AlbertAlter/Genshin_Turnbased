using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>状态图标悬停说明。打开期间持续读取动态描述，不把结果写回共享主表。</summary>
public sealed class BattleStatusDescriptionView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private StatusInstance status;
    private object host;
    private bool hovering;
    private RectTransform popup;
    private TMP_Text description;
    private Canvas canvas;
    private Vector2 pointerPosition;
    private Camera pointerCamera;

    public void Bind(StatusInstance value, object statusHost)
    {
        // 图标池排序/复用后，不把原来悬停的状态说明偷偷改成另一份状态。
        if (!ReferenceEquals(status, value) || !ReferenceEquals(host, statusHost)) Hide();
        status = value;
        host = statusHost;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!CanShow()) return;
        pointerPosition = eventData.position;
        pointerCamera = eventData.enterEventCamera;
        hovering = true;
        EnsurePopup();
        RefreshDescription();
    }

    public void OnPointerExit(PointerEventData eventData) => Hide();
    private void OnDisable() => Hide();
    private void OnDestroy() { if (popup != null) Destroy(popup.gameObject); }
    private void LateUpdate() { if (hovering) RefreshDescription(); }

    private bool CanShow()
        => isActiveAndEnabled && status != null && status.IsActive && status.MainData?.Display == 1;

    private void Hide()
    {
        hovering = false;
        if (popup != null) popup.gameObject.SetActive(false);
    }

    private void EnsurePopup()
    {
        if (popup != null) return;
        var parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas == null) return;
        canvas = parentCanvas.rootCanvas;
        var panel = new GameObject("StatusDescription", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        popup = panel.GetComponent<RectTransform>();
        popup.SetParent(canvas.transform, false);
        popup.anchorMin = popup.anchorMax = Vector2.zero;
        popup.pivot = new Vector2(0f, 1f);
        var background = panel.GetComponent<Image>();
        background.color = new Color(0.08f, 0.08f, 0.08f, 0.95f);
        background.raycastTarget = false;
        var group = panel.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        var textObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(popup, false);
        description = textObject.GetComponent<TextMeshProUGUI>();
        var owner = GetComponentInParent<UIBattleController>();
        var reference = owner != null ? owner.GetComponentInChildren<TMP_Text>(true) : null;
        if (reference != null && reference.font != null) description.font = reference.font;
        description.fontSize = 18f;
        description.color = Color.white;
        description.richText = false;
        description.raycastTarget = false;
        description.enableWordWrapping = true;
        description.alignment = TextAlignmentOptions.TopLeft;
        var textRect = description.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 10f);
        textRect.offsetMax = new Vector2(-12f, -10f);
    }

    private void RefreshDescription()
    {
        if (!CanShow()) { Hide(); return; }
        if (popup == null || description == null || canvas == null) return;
        string lifetime = status.IsPermanent ? "持续存在" : $"剩余{status.RemainingPhaseCount}回合";
        string content = $"{status.MainData.StatusName}\n{status.GetDescription(host)}\n{lifetime}";
        if (description.text != content) description.text = content;
        var canvasRect = (RectTransform)canvas.transform;
        float width = Mathf.Min(360f, Mathf.Max(24f, canvasRect.rect.width));
        float height = description.GetPreferredValues(content, Mathf.Max(1f, width - 24f), Mathf.Infinity).y + 20f;
        popup.sizeDelta = new Vector2(width, height);
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, pointerPosition, pointerCamera, out Vector2 local))
        {
            float x = local.x - canvasRect.rect.xMin + 16f;
            float y = local.y - canvasRect.rect.yMin - 16f;
            popup.anchoredPosition = new Vector2(
                Mathf.Clamp(x, 0f, Mathf.Max(0f, canvasRect.rect.width - width)),
                Mathf.Clamp(y, Mathf.Min(height, canvasRect.rect.height), canvasRect.rect.height));
        }
        popup.SetAsLastSibling();
        popup.gameObject.SetActive(true);
    }
}
