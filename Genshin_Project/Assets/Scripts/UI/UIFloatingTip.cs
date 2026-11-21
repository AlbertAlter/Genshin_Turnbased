using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Short, non-interactive feedback above the center of a UI content frame.</summary>
[DisallowMultipleComponent]
public sealed class UIFloatingTip : MonoBehaviour
{
    private const float Duration = 1.65f;
    private const float RiseTime = .32f;
    private const float FadeTime = .30f;
    private RectTransform rect;
    private TextMeshProUGUI label;
    private CanvasGroup group;
    private float elapsed;

    public static UIFloatingTip Create(RectTransform mount, TMP_FontAsset font, DataManager data = null)
    {
        var node = new GameObject("Floating tip", typeof(RectTransform), typeof(LayoutElement),
            typeof(CanvasGroup), typeof(TextMeshProUGUI), typeof(UIFloatingTip));
        node.layer = mount.gameObject.layer;
        var tip = node.GetComponent<UIFloatingTip>();
        tip.rect = (RectTransform)node.transform;
        tip.rect.SetParent(mount, false);
        tip.rect.anchorMin = new Vector2(.12f, .66f);
        tip.rect.anchorMax = new Vector2(.88f, .66f);
        tip.rect.sizeDelta = new Vector2(0f, 120f);
        node.GetComponent<LayoutElement>().ignoreLayout = true;
        tip.group = node.GetComponent<CanvasGroup>();
        tip.group.interactable = tip.group.blocksRaycasts = false;
        tip.label = node.GetComponent<TextMeshProUGUI>();
        if (font != null) tip.label.font = font;
        tip.label.fontSize = 30;
        // The tip still needs to report load failures when data or the palette is unavailable.
        tip.label.color = new Color32(254, 254, 254, 255);
        if (data != null)
        {
            try { tip.label.color = data.ProjectWhite; }
            catch (System.IO.IOException) { }
            catch (System.IO.InvalidDataException) { }
        }
        tip.label.alignment = TextAlignmentOptions.Center;
        tip.label.richText = false;
        tip.label.raycastTarget = false;
        node.SetActive(false);
        return tip;
    }

    public void Show(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) { Hide(); return; }
        elapsed = 0;
        rect.anchoredPosition = Vector2.zero;
        rect.SetAsLastSibling();
        group.alpha = 1;
        label.text = message;
        gameObject.SetActive(true);
    }

    public void Hide() => gameObject.SetActive(false);

    private void Update()
    {
        elapsed += Time.unscaledDeltaTime;
        if (elapsed >= Duration) { Hide(); return; }
        float rise = 1f - Mathf.Clamp01(elapsed / RiseTime);
        rect.anchoredPosition = Vector2.up * (22f * (1f - rise * rise));
        group.alpha = Mathf.Clamp01((Duration - elapsed) / FadeTime);
    }
}
