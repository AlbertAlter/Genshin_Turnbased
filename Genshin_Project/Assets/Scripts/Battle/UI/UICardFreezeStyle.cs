using System;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One shared runtime configuration for frozen cards and every preview.</summary>
public sealed class UICardFreezeStyle
{
    public const string FileName = "Freeze.xlsx";
    public float StageFadeSeconds, ShatterSeconds, TravelWidthRatio, FallHeightRatio, RotationDegrees, Opacity;
    public float DetailOpacity, DetailWidthPixels, OutlineOpacity, OutlineWidthPixels, TextureContrast, SurfaceOpacity;
    public float OriginY;
    public int FragmentCount;
    public bool Unscaled;

    public static UICardFreezeStyle Load() => Parse(BattlefieldDamagePopupStyle.ReadParameters(
        Path.Combine(Application.streamingAssetsPath, "Data", "UI", FileName)));

    public static UICardFreezeStyle Parse(IReadOnlyDictionary<string, string> values)
    {
        string[] keys = { "StageFadeSeconds", "ShatterSeconds", "TravelWidthRatio", "FallHeightRatio",
            "RotationDegrees", "Opacity", "OriginY", "FragmentCount", "UseUnscaledTime",
            "DetailOpacity", "DetailWidthPixels", "OutlineOpacity", "OutlineWidthPixels", "TextureContrast", "SurfaceOpacity" };
        if (values == null || values.Count != keys.Length) throw new InvalidDataException("冻结参数缺少或包含未知字段。");
        float N(string key, float min, float max)
        {
            if (!values.TryGetValue(key, out string raw) || !float.TryParse(raw, NumberStyles.Float,
                CultureInfo.InvariantCulture, out float n) || float.IsNaN(n) || float.IsInfinity(n) || n < min || n > max)
                throw new InvalidDataException("冻结参数无效：" + key);
            return n;
        }
        float count = N("FragmentCount", 4, 64), clock = N("UseUnscaledTime", 0, 1);
        if (count != (int)count || (int)count % 4 != 0 || (clock != 0 && clock != 1))
            throw new InvalidDataException("冻结碎片数必须为4的正整数倍，UseUnscaledTime必须为0/1。");
        return new UICardFreezeStyle { StageFadeSeconds = N("StageFadeSeconds", 0, 10),
            ShatterSeconds = N("ShatterSeconds", .01f, 10), TravelWidthRatio = N("TravelWidthRatio", 0, 2),
            FallHeightRatio = N("FallHeightRatio", 0, 2), RotationDegrees = N("RotationDegrees", 0, 360),
            Opacity = N("Opacity", 0, 1), OriginY = N("OriginY", 0, 1), FragmentCount = (int)count, Unscaled = clock == 1,
            DetailOpacity = N("DetailOpacity", 0, 1), DetailWidthPixels = N("DetailWidthPixels", 0, 4),
            OutlineOpacity = N("OutlineOpacity", 0, 1), OutlineWidthPixels = N("OutlineWidthPixels", 0, 8),
            TextureContrast = N("TextureContrast", 0, 8), SurfaceOpacity = N("SurfaceOpacity", 0, 1) };
    }
}
