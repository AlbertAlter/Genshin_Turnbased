using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

public enum UICardStatusFlow { Buff, Debuff }

/// <summary>Shared animation parameters; callers explicitly choose buff/up or debuff/down.</summary>
public sealed class UICardOrbitArrowStyle
{
    public const string FileName = "CardOrbitArrows.xlsx";
    public int ArrowCount, Chevrons;
    public float TravelSeconds, SpawnStagger, Spread, TravelHeight, Width, Height, Gap, Trail;
    public float GlowScale, GlowOpacity, Opacity, FadeIn, FadeOut, Duration, CoreWhiteMix;
    public Color BuffColor, DebuffColor;
    private Dictionary<string, string> raw;
    public Dictionary<string, float> Numbers()
    {
        var result = new Dictionary<string, float>();
        foreach (var pair in raw)
            if (float.TryParse(pair.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number)) result.Add(pair.Key, number);
        return result;
    }
    public UICardOrbitArrowStyle WithNumbers(IReadOnlyDictionary<string, float> changes)
    {
        var copy = new Dictionary<string, string>(raw);
        foreach (var pair in changes)
        {
            if (!copy.ContainsKey(pair.Key) || pair.Key.EndsWith("Color")) throw new InvalidDataException("未知状态箭头参数：" + pair.Key);
            copy[pair.Key] = pair.Value.ToString("R", CultureInfo.InvariantCulture);
        }
        return Parse(copy);
    }
    public static UICardOrbitArrowStyle Load() => CampaignUiTables.Read(ReadPublished);
    private static UICardOrbitArrowStyle ReadPublished()
        => ReadFile(Path.Combine(Application.streamingAssetsPath, "Data", "UI", "StatusPopUp", FileName));
    public static UICardOrbitArrowStyle ReadFile(string path) => Parse(BattlefieldDamagePopupStyle.ReadParameters(path));
    public static UICardOrbitArrowStyle Parse(IReadOnlyDictionary<string, string> values)
    {
        string[] keys = { "ArrowCount", "ChevronsPerArrow", "TravelSeconds", "SpawnStaggerSeconds", "SpreadWidthRatio",
            "TravelHeightRatio", "ArrowWidthRatio", "ArrowHeightRatio", "ChevronGapRatio", "TrailLengthRatio",
            "GlowScale", "GlowOpacity", "Opacity", "FadeInSeconds", "FadeOutSeconds", "DurationSeconds",
            "CoreWhiteMix", "BuffColor", "DebuffColor" };
        if (values == null || values.Count != keys.Length) throw new InvalidDataException("状态箭头参数数量不正确。");
        foreach (var key in keys) if (!values.ContainsKey(key)) throw new InvalidDataException("缺少状态箭头参数：" + key);
        float N(string key, float min, float max)
        {
            if (!float.TryParse(values[key], NumberStyles.Float, CultureInfo.InvariantCulture, out float n)
                || float.IsNaN(n) || float.IsInfinity(n) || n < min || n > max)
                throw new InvalidDataException("状态箭头参数超出范围：" + key);
            return n;
        }
        int I(string key, int min, int max)
        { float n = N(key, min, max); if (n != (int)n) throw new InvalidDataException(key + " 必须为整数。"); return (int)n; }
        Color C(string key)
        {
            if (values[key].Length != 7 || !values[key].StartsWith("#") || !ColorUtility.TryParseHtmlString(values[key], out var c))
                throw new InvalidDataException(key + " 必须为 #RRGGBB。");
            return c;
        }
        var original = new Dictionary<string, string>();
        foreach (var pair in values) original.Add(pair.Key, pair.Value);
        var style = new UICardOrbitArrowStyle
        {
            raw = original,
            ArrowCount = I("ArrowCount", 1, 12), Chevrons = I("ChevronsPerArrow", 1, 3),
            TravelSeconds = N("TravelSeconds", .1f, 5), SpawnStagger = N("SpawnStaggerSeconds", 0, .5f),
            Spread = N("SpreadWidthRatio", .2f, 2.4f), TravelHeight = N("TravelHeightRatio", .1f, 1.5f),
            Width = N("ArrowWidthRatio", .02f, .5f), Height = N("ArrowHeightRatio", .005f, .15f),
            Gap = N("ChevronGapRatio", .005f, .2f), Trail = N("TrailLengthRatio", 0, .5f),
            GlowScale = N("GlowScale", 1, 4), GlowOpacity = N("GlowOpacity", 0, 1), Opacity = N("Opacity", 0, 1),
            FadeIn = N("FadeInSeconds", 0, 3), FadeOut = N("FadeOutSeconds", 0, 3),
            Duration = N("DurationSeconds", .1f, 5), CoreWhiteMix = N("CoreWhiteMix", 0, 1),
            BuffColor = C("BuffColor"), DebuffColor = C("DebuffColor"),
        };
        return style;
    }
}

public readonly struct UICardOrbitArrowSample
{
    public readonly Vector2 Position;
    public readonly float Phase, Depth, Opacity, Scale;
    public UICardOrbitArrowSample(Vector2 position, float phase, float depth, float opacity, float scale)
    { Position = position; Phase = phase; Depth = depth; Opacity = opacity; Scale = scale; }
}

/// <summary>Deterministic visual motion; never consumes battle randomness.</summary>
public static class UICardOrbitArrowMath
{
    public static UICardOrbitArrowSample Sample(UICardOrbitArrowStyle s, Rect card, int index, float seconds, UICardStatusFlow flow)
    {
        float phase = (seconds - index * s.SpawnStagger) / s.TravelSeconds;
        float seat = s.ArrowCount == 1 ? .5f : (float)index / (s.ArrowCount - 1);
        // Static front/back seats retain card occlusion; each lane moves only vertically, once.
        float depth = index % 2 == 0 ? 1 : -1;
        float vertical = flow == UICardStatusFlow.Buff ? Mathf.Clamp01(phase) : 1 - Mathf.Clamp01(phase);
        var point = new Vector2(card.center.x + (seat - .5f) * card.width * s.Spread,
            card.center.y + (vertical - .5f) * card.height * s.TravelHeight);
        float alpha = Mathf.SmoothStep(0, 1, phase / .15f) * Mathf.SmoothStep(0, 1, (1 - phase) / .15f);
        return new UICardOrbitArrowSample(point, phase, depth, alpha, 1);
    }
    public static float FrontWeight(float depth) => Mathf.SmoothStep(0, 1, (depth + .12f) / .24f);
}
