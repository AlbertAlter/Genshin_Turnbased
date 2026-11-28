using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>同一设置Prefab的覆盖层入口。留在原场景，关闭只通知原宿主，不重载路线或战斗。</summary>
public sealed class ProjectSettingsOverlay : MonoBehaviour
{
    private Action closed;
    private bool closing;
    public bool IsOpen => !closing && isActiveAndEnabled;

    public static ProjectSettingsOverlay Open(ProjectSettingsView prefab, RectTransform mount, Action onClosed)
    {
        if (prefab == null || mount == null) throw new InvalidOperationException("设置入口缺少公共Prefab或挂点。");
        var root = RoguelikeCampaignScreen.Rect("Settings overlay", mount);
        RoguelikeCampaignScreen.Stretch(root);
        var overlay = root.gameObject.AddComponent<ProjectSettingsOverlay>();
        try
        {
            var blocker = root.gameObject.AddComponent<Image>();
            blocker.color = new Color32(48, 40, 32, 255); blocker.raycastTarget = true;
            var canvas = root.gameObject.AddComponent<Canvas>(); canvas.overrideSorting = true;
            var parent = mount.GetComponentInParent<Canvas>();
            canvas.sortingLayerID = parent.sortingLayerID; canvas.sortingOrder = parent.sortingOrder + 500;
            root.gameObject.AddComponent<GraphicRaycaster>();
            var view = Instantiate(prefab, root, false); view.Bind(root);
            UIPageNavigation.Create(root, overlay.Close);
            overlay.closed = onClosed;
            return overlay;
        }
        catch { Destroy(root.gameObject); throw; }
    }

    public void Close() => Close(true);
    public void Close(bool notify)
    {
        if (closing) return;
        closing = true;
        var callback = closed; closed = null;
        gameObject.SetActive(false); Destroy(gameObject);
        if (notify) callback?.Invoke();
    }
}
