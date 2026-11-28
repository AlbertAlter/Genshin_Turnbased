using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared screen scaling and content frame for explicitly mounted scene UI.</summary>
[DisallowMultipleComponent]
public sealed class ProjectUiCanvas : MonoBehaviour
{
    public const string PrefabPath = "UI/ProjectCanvas";
    private static readonly Vector2 StandardSize = new Vector2(1920f, 1080f);

    [SerializeField] private CanvasScaler scaler;
    [SerializeField] private RectTransform contentFrame;
    [SerializeField] private GameObject margins;

    public RectTransform ContentFrame => contentFrame;

    // UI-only hosts pass their explicitly authored background camera. A scene imported
    // with an old Rect schema can retain a zero viewport even after its YAML is fixed.
    public static bool EnsureBackgroundViewport(Camera backgroundCamera)
    {
        if (backgroundCamera == null || !backgroundCamera.isActiveAndEnabled) return false;
        Rect viewport = backgroundCamera.rect;
        if (!(viewport.width > 0) || !(viewport.height > 0))
            backgroundCamera.rect = new Rect(0, 0, 1, 1);
        return true;
    }

    public void SetDesignSize(Vector2 size)
    {
        scaler.referenceResolution = size;
        contentFrame.sizeDelta = size;
    }

    public void UseFullScreenAtReferenceHeight()
    {
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;
        Stretch(contentFrame);
        margins.SetActive(false);
    }

    public Canvas CreateContentCanvas(string contentName)
    {
        var content = new GameObject(contentName, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        var rect = (RectTransform)content.transform;
        rect.SetParent(contentFrame, false);
        Stretch(rect);
        var canvas = content.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        return canvas;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    // IMGUI panels use the same centered design rectangle as their UGUI content.
    public static Matrix4x4 GetGuiMatrix(Canvas content, Vector2? fallbackSize = null)
    {
        Vector2 size = content != null ? ((RectTransform)content.transform).rect.size : Vector2.zero;
        if (size.x <= 0f || size.y <= 0f)
            size = fallbackSize ?? StandardSize;

        float scale = Mathf.Min(Screen.width / size.x, Screen.height / size.y);
        var topLeft = new Vector3((Screen.width - size.x * scale) * .5f,
            (Screen.height - size.y * scale) * .5f, 0f);
        return Matrix4x4.TRS(topLeft, Quaternion.identity, new Vector3(scale, scale, 1f));
    }
}
