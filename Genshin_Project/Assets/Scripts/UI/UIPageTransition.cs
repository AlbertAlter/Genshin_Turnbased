using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared unscaled screen cover. The caller decides when destination content is ready.</summary>
public sealed class UIPageTransition : MonoBehaviour
{
    private CanvasGroup group;
    public static UIPageTransition Create(ProjectUiCanvas prefab, Transform owner, bool initiallyCovered = false)
    {
        var canvas = Instantiate(prefab, owner, false);
        canvas.name = "Scene transition";
        canvas.UseFullScreenAtReferenceHeight();
        canvas.GetComponent<Canvas>().sortingOrder = 2000;
        var area = new GameObject("Cover", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(UIPageTransition));
        var rect = (RectTransform)area.transform; rect.SetParent(canvas.transform, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        area.GetComponent<Image>().color = new Color32(49, 40, 33, 255);
        var view = area.GetComponent<UIPageTransition>(); view.group = area.GetComponent<CanvasGroup>();
        view.group.alpha = initiallyCovered ? 1 : 0; view.group.blocksRaycasts = initiallyCovered; return view;
    }
    public IEnumerator Fade(bool covered, float seconds = .16f)
    {
        group.blocksRaycasts = true;
        float from = group.alpha, to = covered ? 1 : 0;
        for (float elapsed = 0; elapsed < seconds; elapsed += Time.unscaledDeltaTime)
        {
            group.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, elapsed / seconds)); yield return null;
        }
        group.alpha = to; group.blocksRaycasts = covered;
    }
}
