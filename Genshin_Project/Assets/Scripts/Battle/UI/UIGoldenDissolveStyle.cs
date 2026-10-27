using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>Visual settings only. Runtime reads StreamingAssets/Data/UI, never Charts.</summary>
public sealed class UIGoldenDissolveStyle
{
    public const string FileName = "GoldenDissolve.xlsx";
    public float DurationSeconds { get; private set; }
    public bool UseUnscaledTime { get; private set; }
    public float NoiseScale { get; private set; }
    public float EdgeWidth { get; private set; }
    public float EdgeSoftness { get; private set; }
    public Color EdgeColor { get; private set; }
    public Color EdgeHighlight { get; private set; }
    public Vector2 Direction { get; private set; }
    public float DirectionWeight { get; private set; }
    public int FragmentCount { get; private set; }
    public float FragmentLifetimeSeconds { get; private set; }
    public float FragmentSizeMinHeightRatio { get; private set; }
    public float FragmentSizeMaxHeightRatio { get; private set; }
    public float FragmentDriftHeightRatio { get; private set; }
    public float FragmentSpreadHeightRatio { get; private set; }
    public float FragmentRoughness { get; private set; }
    public float FragmentFadeInRatio { get; private set; }
    public float ShadowFadePower { get; private set; }
    public int CaptureHeightPixels { get; private set; }
    public float PreviewCardHeight { get; private set; }
    public float PreviewLoopPauseSeconds { get; private set; }
    public Color PreviewBackgroundColor { get; private set; }
    public float TotalDuration => DurationSeconds + (FragmentCount > 0 ? FragmentLifetimeSeconds : 0);

    private UIGoldenDissolveStyle() { }

    public static UIGoldenDissolveStyle Load()
        => ReadFile(Path.Combine(Application.streamingAssetsPath, "Data", "UI", FileName));

    public static UIGoldenDissolveStyle ReadFile(string path)
        => Parse(BattlefieldDamagePopupStyle.ReadParameters(path));

    public static UIGoldenDissolveStyle Parse(IReadOnlyDictionary<string, string> values)
    {
        string[] keys = { "DurationSeconds", "UseUnscaledTime", "NoiseScale", "EdgeWidth", "EdgeSoftness",
            "EdgeColor", "EdgeHighlight", "DirectionX", "DirectionY", "DirectionWeight",
            "FragmentCount", "FragmentLifetimeSeconds", "FragmentSizeMinHeightRatio", "FragmentSizeMaxHeightRatio",
            "FragmentDriftHeightRatio", "FragmentSpreadHeightRatio", "FragmentRoughness", "FragmentFadeInRatio",
            "ShadowFadePower", "CaptureHeightPixels", "PreviewCardHeight", "PreviewLoopPauseSeconds", "PreviewBackgroundColor" };
        if (values == null || values.Count != keys.Length) throw new InvalidDataException("金色溶解参数缺少或包含未知字段。");
        foreach (string key in keys)
            if (!values.ContainsKey(key)) throw new InvalidDataException("缺少金色溶解参数：" + key);
        float Number(string key, float min, float max)
        {
            if (!float.TryParse(values[key], NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ||
                float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max)
                throw new InvalidDataException("金色溶解参数必须为范围内的有限数：" + key);
            return value;
        }
        Color HtmlColor(string key)
        {
            if (!ColorUtility.TryParseHtmlString(values[key], out Color value))
                throw new InvalidDataException("金色溶解颜色必须为HTML颜色：" + key);
            return value;
        }
        int Integer(string key, int min, int max)
        {
            float value = Number(key, min, max);
            if (value != (int)value) throw new InvalidDataException("金色溶解参数必须为整数：" + key);
            return (int)value;
        }
        float unscaled = Number("UseUnscaledTime", 0, 1);
        if (unscaled != 0 && unscaled != 1) throw new InvalidDataException("UseUnscaledTime必须为0或1。");
        float softness = Number("EdgeSoftness", .001f, 1);
        var result = new UIGoldenDissolveStyle
        {
            DurationSeconds = Number("DurationSeconds", 0, float.MaxValue), UseUnscaledTime = unscaled == 1,
            NoiseScale = Number("NoiseScale", .1f, float.MaxValue), EdgeWidth = Number("EdgeWidth", 0, 1), EdgeSoftness = softness,
            EdgeColor = HtmlColor("EdgeColor"), EdgeHighlight = HtmlColor("EdgeHighlight"),
            Direction = new Vector2(Number("DirectionX", -1, 1), Number("DirectionY", -1, 1)).normalized,
            DirectionWeight = Number("DirectionWeight", 0, 1),
            FragmentCount = Integer("FragmentCount", 0, 4096),
            FragmentLifetimeSeconds = Number("FragmentLifetimeSeconds", 0, float.MaxValue),
            FragmentSizeMinHeightRatio = Number("FragmentSizeMinHeightRatio", .0001f, .25f),
            FragmentSizeMaxHeightRatio = Number("FragmentSizeMaxHeightRatio", .0001f, .25f),
            FragmentDriftHeightRatio = Number("FragmentDriftHeightRatio", 0, 1),
            FragmentSpreadHeightRatio = Number("FragmentSpreadHeightRatio", 0, 1),
            FragmentRoughness = Number("FragmentRoughness", 0, 1),
            FragmentFadeInRatio = Number("FragmentFadeInRatio", 0, .9f),
            ShadowFadePower = Number("ShadowFadePower", .01f, float.MaxValue),
            CaptureHeightPixels = Integer("CaptureHeightPixels", 32, 4096),
            PreviewCardHeight = Number("PreviewCardHeight", 1, 4096),
            PreviewLoopPauseSeconds = Number("PreviewLoopPauseSeconds", 0, float.MaxValue),
            PreviewBackgroundColor = HtmlColor("PreviewBackgroundColor"),
        };
        if (result.FragmentSizeMinHeightRatio > result.FragmentSizeMaxHeightRatio ||
            (result.FragmentCount > 0 && result.FragmentLifetimeSeconds <= 0) || float.IsInfinity(result.TotalDuration))
            throw new InvalidDataException("碎屑尺寸范围、生命周期或总时长无效。");
        return result;
    }
}
