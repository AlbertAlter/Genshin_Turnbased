using UnityEngine;
using UnityEngine.UI;

/// <summary>The main menu backdrop shared by full-screen pages, without stretching a battle image.</summary>
public static class UIProjectMenuBackground
{
    public const string EmblemPath = "art_assets/Others/PaimonEmblem_ProjectWhite.png";

    public static StreamingSpriteReference Apply(RectTransform target)
    {
        var image = target.GetComponent<Image>();
        if (image == null) image = target.gameObject.AddComponent<Image>();
        image.color = Color.white;
        image.raycastTarget = false;
        var gradient = target.gameObject.AddComponent<UIVerticalGradient>();
        gradient.SetColors(new Color32(87, 77, 68, 255), new Color32(48, 40, 32, 255));
        var clip = new GameObject("Background bounds", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
        clip.SetParent(target, false);
        clip.gameObject.layer = target.gameObject.layer;
        clip.anchorMin = Vector2.zero; clip.anchorMax = Vector2.one;
        clip.offsetMin = clip.offsetMax = Vector2.zero;
        var emblem = new GameObject("Menu emblem", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<RectTransform>();
        emblem.SetParent(clip, false);
        emblem.gameObject.layer = target.gameObject.layer;
        // Sixty percent of the design width, keeping the emblem square at any aspect ratio.
        emblem.anchorMin = new Vector2(.2f, 0);
        emblem.anchorMax = new Vector2(.8f, 1);
        emblem.offsetMin = emblem.offsetMax = Vector2.zero;
        var fit = emblem.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
        fit.aspectRatio = 1;
        var art = emblem.GetComponent<Image>();
        art.color = new Color(1, 1, 1, .20f);
        art.preserveAspect = true;
        art.raycastTarget = false;
        var reference = emblem.gameObject.AddComponent<StreamingSpriteReference>();
        reference.SetResolvedPath(EmblemPath);
        return reference;
    }
}
