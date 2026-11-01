using System;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One shared runtime configuration for frozen cards and every preview.</summary>
public sealed class UICardFreezeStyle
{
    public const string FileName = "Freeze.xlsx";
    public float CrackRevealSeconds, ShatterSeconds, TravelWidthRatio, FallHeightRatio, RotationDegrees;
    public float CrackRevealFeatherPixels, SecondRevealEasePower;
    public float OriginY;
    public int FragmentCount;
    public bool Unscaled;

    public static UICardFreezeStyle Load() => Parse(BattlefieldDamagePopupStyle.ReadParameters(
        Path.Combine(Application.streamingAssetsPath, "Data", "UI", FileName)));

    public static UICardFreezeStyle Parse(IReadOnlyDictionary<string, string> values)
    {
        string[] keys = { "CrackRevealSeconds", "ShatterSeconds", "TravelWidthRatio", "FallHeightRatio",
            "RotationDegrees", "OriginY", "FragmentCount", "UseUnscaledTime", "CrackRevealFeatherPixels", "SecondRevealEasePower" };
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
        return new UICardFreezeStyle { CrackRevealSeconds = N("CrackRevealSeconds", 0, 10),
            ShatterSeconds = N("ShatterSeconds", .01f, 10), TravelWidthRatio = N("TravelWidthRatio", 0, 2),
            FallHeightRatio = N("FallHeightRatio", 0, 2), RotationDegrees = N("RotationDegrees", 0, 360),
            OriginY = N("OriginY", 0, 1), FragmentCount = (int)count, Unscaled = clock == 1,
            CrackRevealFeatherPixels = N("CrackRevealFeatherPixels", 0, 8),
            SecondRevealEasePower = N("SecondRevealEasePower", 1, 4) };
    }
}
