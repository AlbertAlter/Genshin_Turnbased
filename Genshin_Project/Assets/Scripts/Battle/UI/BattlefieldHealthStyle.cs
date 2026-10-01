using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>Only the published Assets/StreamingAssets table is consumed by the UI.</summary>
public sealed class BattlefieldHealthStyle
{
    public Color AllyColor, EnemyColor;
    public float Left, Right, Top, Bottom, Duration;
    public static BattlefieldHealthStyle Load()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "Charts", "UI", "BattleField1_health_styles.csv");
        if (!File.Exists(path)) throw new FileNotFoundException("请先使用 UI 配表同步工具发布血条配表。", path);
        var values = File.ReadAllLines(path).Skip(1).Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split(',')).ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim());
        float Number(string key)
        {
            float n = float.Parse(values[key], CultureInfo.InvariantCulture);
            if (float.IsNaN(n) || float.IsInfinity(n) || n < 0) throw new InvalidDataException(key);
            return n;
        }
        Color ReadColor(string key)
        {
            if (!ColorUtility.TryParseHtmlString(values[key], out Color color)) throw new InvalidDataException(key);
            return color;
        }
        return new BattlefieldHealthStyle { AllyColor=ReadColor("AllyFillColor"), EnemyColor=ReadColor("EnemyFillColor"),
            Left=Number("InsetLeft"), Right=Number("InsetRight"), Top=Number("InsetTop"), Bottom=Number("InsetBottom"),
            Duration=Number("TransitionSeconds") };
    }
}
