using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Element-colored skill, talent and constellation discs shared by character pages.</summary>
public static class UICharacterAbilityIcon
{
    private const string Circle = "art_assets/UI/CharacterList/Shapes/CircleMask.png";
    private const string Details = "art_assets/UI/CharacterList/Details/";

    public static void Create(RectTransform mount, string configuredIcon, string element,
        Action<string, Action<Sprite>> load = null)
    {
        var border = Layer(mount, "Disc border", 0, new Color32(113, 103, 88, 255));
        SetSprite(border, Circle, load);
        var face = Layer(mount, "Disc face", .04f, new Color32(247, 239, 221, 255));
        SetSprite(face, Circle, load);
        if (!string.IsNullOrEmpty(element))
            SetSprite(Layer(face.rectTransform, "Element face", 0, Color.white), Details + "disc" + element + ".png", load);
        if (CharacterSkillIconResolver.TryGetRelativePath(configuredIcon, out string path))
            SetSprite(Layer(mount, "Ability glyph", .08f, Color.white), path, load);
    }

    public static void ApplyElementColor(Image image, string element)
    {
        if (image != null && ElementColorPalette.Load().TryGetColor(element, out var color)) image.color = color;
    }

    private static Image Layer(RectTransform parent, string name, float inset, Color color)
    {
        var rect = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.gameObject.layer = parent.gameObject.layer;
        rect.anchorMin = Vector2.one * inset;
        rect.anchorMax = Vector2.one * (1 - inset);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = rect.GetComponent<Image>();
        image.color = color;
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    private static void SetSprite(Image image, string path, Action<string, Action<Sprite>> load)
    {
        if (load == null) image.gameObject.AddComponent<StreamingSpriteReference>().SetResolvedPath(path);
        else load(path, sprite => { if (image != null) { image.sprite = sprite; image.enabled = sprite != null; } });
    }
}
