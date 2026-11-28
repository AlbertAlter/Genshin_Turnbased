using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>Shared card-face targeting parameters. Layout stays with the explicitly bound card.</summary>
public sealed class CardTargetIndicatorTable
{
    public const string Folder = "CardFaceUI";
    public const string FileName = "Targeted.xlsx";
    private readonly CardTargetIndicatorStyle parameters;
    private CardTargetIndicatorTable(CardTargetIndicatorStyle value) => parameters = value;

    public static CardTargetIndicatorTable ReadPublished() => ReadFile(
        Path.Combine(Application.streamingAssetsPath, "Data", "UI", Folder, FileName));
    public static CardTargetIndicatorTable ReadFile(string path)
        => Parse(BattlefieldDamagePopupStyle.ReadParameters(path));

    public CardTargetIndicatorStyle ApplyTo(CardTargetIndicatorStyle layout)
    {
        if (layout == null) throw new ArgumentNullException(nameof(layout));
        var result = parameters.Copy();
        result.referenceSize = layout.referenceSize;
        result.centerOffset = layout.centerOffset;
        result.referenceCardWidth = layout.referenceCardWidth;
        return result;
    }

    public static Dictionary<string, float> Values(CardTargetIndicatorStyle s)
    {
        if (s == null) throw new ArgumentNullException(nameof(s));
        var values = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            ["EnterSeconds"] = s.enterSeconds, ["SettleSeconds"] = s.settleSeconds,
            ["ExitSeconds"] = s.exitSeconds, ["TipTravel"] = s.tipTravel,
            ["TipOvershoot"] = s.tipOvershoot, ["RingStartScale"] = s.ringStartScale,
            ["SizeMultiplier"] = s.sizeMultiplier, ["IdleMotion"] = s.idleMotion ? 1 : 0,
            ["IdleCycleSeconds"] = s.idleCycleSeconds, ["IdleScaleAmount"] = s.idleScaleAmount,
            ["IdleGlowDip"] = s.idleGlowDip, ["RotationDegreesPerSecond"] = s.ringDegreesPerSecond,
            ["OuterGlow"] = s.outerGlow ? 1 : 0, ["GlowSize"] = s.glowSize,
            ["GlowSpread"] = s.glowSpread, ["GlowRed"] = s.glowColor.r,
            ["GlowGreen"] = s.glowColor.g, ["GlowBlue"] = s.glowColor.b, ["GlowOpacity"] = s.glowColor.a
        };
        var text = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in values) text.Add(pair.Key, pair.Value.ToString("R", CultureInfo.InvariantCulture));
        Parse(text); // Reject invalid tuning before either workbook is changed.
        return values;
    }

    public static CardTargetIndicatorTable Parse(IReadOnlyDictionary<string, string> values)
    {
        if (values == null || values.Count != 19)
            throw new InvalidDataException("卡面准心的 Parameters 需要19项参数。");
        float N(string key, float min, float max)
        {
            if (!values.TryGetValue(key, out string raw) || !float.TryParse(raw, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float n) || float.IsNaN(n) || float.IsInfinity(n) || n < min || n > max)
                throw new InvalidDataException($"卡面准心参数 {key} 须为 {min}～{max} 的有限数字。");
            return n;
        }
        bool B(string key)
        {
            float n = N(key, 0, 1);
            if (n != 0 && n != 1) throw new InvalidDataException("卡面准心开关只能填0或1：" + key);
            return n == 1;
        }
        return new CardTargetIndicatorTable(new CardTargetIndicatorStyle
        {
            enterSeconds = N("EnterSeconds", 0, 10), settleSeconds = N("SettleSeconds", 0, 10),
            exitSeconds = N("ExitSeconds", 0, 10), tipTravel = N("TipTravel", 0, 100),
            tipOvershoot = N("TipOvershoot", 0, 100), ringStartScale = N("RingStartScale", 1, 1.2f),
            sizeMultiplier = N("SizeMultiplier", .25f, 1.5f), idleMotion = B("IdleMotion"),
            idleCycleSeconds = N("IdleCycleSeconds", .1f, 60), idleScaleAmount = N("IdleScaleAmount", 0, .1f),
            idleGlowDip = N("IdleGlowDip", 0, 1), ringDegreesPerSecond = N("RotationDegreesPerSecond", -360, 360),
            outerGlow = B("OuterGlow"), glowSize = N("GlowSize", 0, 120), glowSpread = N("GlowSpread", 0, 100),
            glowColor = new Color(N("GlowRed", 0, 1), N("GlowGreen", 0, 1), N("GlowBlue", 0, 1), N("GlowOpacity", 0, 1))
        });
    }
}
