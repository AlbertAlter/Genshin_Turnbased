using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>Shield geometry is local to a card, never a replacement for scene card positions.</summary>
public sealed class UIShieldStyle
{
    public const string FileName = "ShieldEffects.xlsx";
    private readonly Dictionary<string, string> values;
    public static readonly string[] ArtKeys = { "MembranePath", "MaskPath", "BorderPath" };
    private static readonly string[] Numbers = {
        "ReferenceWidth", "ReferenceHeight", "MembraneWidth", "MembraneHeight", "MembraneOffsetX", "MembraneOffsetY",
        "MaskWidth", "MaskHeight", "MaskOffsetX", "MaskOffsetY", "TintWidth", "TintHeight", "TintOffsetX", "TintOffsetY",
        "MembraneOpacity", "MembraneFillOpacity", "TintOpacity", "TintFillOpacity", "NeutralTintOpacity",
        "SweepDuration", "SweepInterval", "SweepAngle", "SweepWidth", "SweepSoftness", "SweepOpacity",
        "SweepElementTint", "SweepFadeFraction", "FadeInSeconds", "FadeOutSeconds" };
    private UIShieldStyle(Dictionary<string, string> values) { this.values = values; }
    public string Text(string key) => values[key];
    public float Number(string key) => float.Parse(values[key], CultureInfo.InvariantCulture);
    public Color PsdTint { get { ColorUtility.TryParseHtmlString(Text("PsdTint"), out var color); return color; } }
    public Vector4 Rect(string prefix) => new Vector4(Number(prefix + "OffsetX"), Number(prefix + "OffsetY"),
        Number(prefix + "Width"), Number(prefix + "Height"));
    public IEnumerable<string> ArtPaths { get { foreach (string key in ArtKeys) yield return Text(key); } }
    public static UIShieldStyle Load() => ReadFile(Path.Combine(Application.streamingAssetsPath, "Data", "UI", FileName));
    public static UIShieldStyle ReadFile(string path) => Parse(BattlefieldDamagePopupStyle.ReadParameters(path));

    public static UIShieldStyle Parse(IReadOnlyDictionary<string, string> input)
    {
        var allowed = new HashSet<string>(Numbers, StringComparer.Ordinal) { "PsdTint" };
        foreach (string key in ArtKeys) allowed.Add(key);
        var data = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in input)
        {
            if (!allowed.Contains(pair.Key)) throw new InvalidDataException("未知护盾参数：" + pair.Key);
            data.Add(pair.Key, pair.Value);
        }
        foreach (string key in allowed)
            if (!data.ContainsKey(key)) throw new InvalidDataException("缺少护盾参数：" + key);
        foreach (string key in Numbers)
        {
            if (!float.TryParse(data[key], NumberStyles.Float, CultureInfo.InvariantCulture, out float n) ||
                float.IsNaN(n) || float.IsInfinity(n)) throw new InvalidDataException("护盾参数必须为有限数字：" + key);
            bool signed = key.Contains("Offset") || key == "SweepAngle";
            if (!signed && n < 0) throw new InvalidDataException("护盾参数不能为负：" + key);
            if ((key.Contains("Opacity") || key == "SweepElementTint" || key == "SweepSoftness") && n > 1)
                throw new InvalidDataException("护盾参数必须在 0～1：" + key);
        }
        var result = new UIShieldStyle(data);
        foreach (string key in new[] { "ReferenceWidth", "ReferenceHeight", "MembraneWidth", "MembraneHeight",
            "MaskWidth", "MaskHeight", "TintWidth", "TintHeight", "SweepDuration", "SweepWidth" })
            if (result.Number(key) <= 0) throw new InvalidDataException("护盾尺寸或时长必须大于零：" + key);
        if (Mathf.Abs(result.Number("SweepAngle")) > 80 || result.Number("SweepFadeFraction") > .5f)
            throw new InvalidDataException("扫光角度须在 -80～80 度；淡入淡出比例须在 0～0.5。");
        if (data["PsdTint"].Length != 7 || !ColorUtility.TryParseHtmlString(data["PsdTint"], out _))
            throw new InvalidDataException("PsdTint 必须为 #RRGGBB。");
        foreach (string key in ArtKeys)
        {
            string path = data[key].Replace('\\', '/');
            if (!path.StartsWith("art_assets/", StringComparison.Ordinal) || path.Contains("..") || path.Contains(":") ||
                !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("护盾贴图须为 art_assets 下的相对 PNG 路径：" + key);
        }
        return result;
    }
}
