using UnityEngine;
using UnityEngine.UI;

/// <summary>Displays the filled part of a burst's element background, independently of its rings and icon.</summary>
public static class BattleUiEnergyFill
{
    public static void Apply(Image image, float energy, float maximum)
    {
        if (image == null) return;
        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Vertical;
        image.fillOrigin = (int)Image.OriginVertical.Bottom;
        image.fillAmount = maximum > 0f ? Mathf.Clamp01(energy / maximum) : 0f;
    }
}
