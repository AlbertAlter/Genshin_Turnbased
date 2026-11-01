using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

public enum HitEffectWeapon { Sword = 0, Claymore = 1, Bow = 2, Polearm = 3, Catalyst = 4 }

/// <summary>HitEffects owns impact positions; DamagePopup owns number positions. Runtime reads Data/UI only.</summary>
public sealed class UIHitEffectStyle
{
    public const string FileName = "HitEffects.xlsx";
    public sealed class Profile
    {
        internal readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);
        public string Text(string key) => Values[key];
        public float Number(string key) => float.Parse(Values[key], CultureInfo.InvariantCulture);
        public bool Enabled => Number("Enabled") == 1;
        public float Duration => Mathf.Max(Number("FadeInSeconds") + Number("HoldSeconds") + Number("FadeOutSeconds"),
            Mathf.Max(Number("FlashLifetimeSeconds"), Number("ParticleLifetimeSeconds")));
    }
    public sealed class Particles
    {
        public string Shape, Motion;
        public float CountMultiplier, SizeMultiplier;
    }
    private readonly Profile[,] profiles = new Profile[5, 2];
    private readonly Dictionary<string, Particles> particles = new Dictionary<string, Particles>(StringComparer.Ordinal);
    public Profile Get(HitEffectWeapon weapon, bool heavy) => profiles[(int)weapon, heavy ? 1 : 0];
    public Particles GetParticles(string element) => particles[element];
    public static UIHitEffectStyle Load() => ReadFile(BattleTimelineSettings.RuntimePath(FileName));

    private static readonly string[] NumericKeys = {
        "Enabled", "CenterXMinRatio", "CenterXMaxRatio", "CenterYMinRatio", "CenterYMaxRatio",
        "AngleBaseDegrees", "AngleJitterDegrees", "AlternateAngleDegrees", "HitAngleProgressDegrees",
        "LengthCardWidthRatio", "WidthCardWidthRatio", "DepthScaleMin", "DepthScaleMax", "OpacityMin", "OpacityMax",
        "HitScaleProgress", "FinalHitScale", "MaxScaleMultiplier", "FadeInSeconds", "HoldSeconds", "FadeOutSeconds",
        "FlashCardWidthRatio", "FlashLifetimeSeconds", "ParticleCount", "ParticleSizeCardWidthRatio",
        "ParticleSpeedCardHeightPerSecond", "ParticleLifetimeSeconds", "ParticleSpreadDegrees", "ElementParticlesEnabled" };
    private static readonly HashSet<string> Signed = new HashSet<string>(StringComparer.Ordinal) {
        "AngleBaseDegrees", "AlternateAngleDegrees", "HitAngleProgressDegrees" };
    private static readonly string[] Elements = { "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Geo", "Dendro", "Physical" };
    private static readonly string[] Patterns = { "AlternatingSlash", "CrossSlash", "HeavySlash", "SweepingSlash", "Thrust",
        "SweepingThrust", "ArrowImpact", "ChargedImpact", "ElementImpact", "ElementBurst" };

    public static UIHitEffectStyle ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var package = new ExcelPackage(stream);
        var result = new UIHitEffectStyle();
        string[] sheets = { "单手剑", "双手剑", "弓", "长柄", "法器" };
        for (int weapon = 0; weapon < sheets.Length; weapon++)
        {
            var sheet = package.Workbook.Worksheets[sheets[weapon]];
            if (sheet?.Dimension == null) throw new InvalidDataException("缺少命中特效子表：" + sheets[weapon]);
            var headers = ExcelTableHeaders.For(sheet); headers.Require("Key", "Normal", "Heavy");
            for (int attack = 0; attack < 2; attack++)
            {
                var profile = new Profile();
                for (int row = headers.DataStartRow; row <= sheet.Dimension.End.Row; row++)
                {
                    string key = headers.Cell(row, "Key").Text.Trim();
                    if (key.Length == 0) continue;
                    string value = Convert.ToString(headers.Cell(row, attack == 0 ? "Normal" : "Heavy").Value, CultureInfo.InvariantCulture)?.Trim() ?? "";
                    if (profile.Values.ContainsKey(key)) throw new InvalidDataException("重复命中特效参数：" + key);
                    if (Array.IndexOf(NumericKeys, key) < 0 && key != "Pattern" && key != "DrawLayer" && key != "ColorSource")
                        throw new InvalidDataException("未知命中特效参数：" + key);
                    profile.Values.Add(key, value);
                }
                Validate(profile); result.profiles[weapon, attack] = profile;
            }
        }
        var particleSheet = package.Workbook.Worksheets["元素粒子"];
        if (particleSheet?.Dimension == null) throw new InvalidDataException("缺少元素粒子子表。");
        var columns = ExcelTableHeaders.For(particleSheet);
        columns.Require("Element", "Shape", "CountMultiplier", "SizeMultiplier", "Motion");
        for (int row = columns.DataStartRow; row <= particleSheet.Dimension.End.Row; row++)
        {
            string Cell(string key) => columns.Cell(row, key).Text.Trim();
            string element = Cell("Element"); if (element.Length == 0) continue;
            if (Array.IndexOf(Elements, element) < 0 || result.particles.ContainsKey(element)) throw new InvalidDataException("元素粒子键无效或重复：" + element);
            float Number(string key)
            {
                if (!float.TryParse(Cell(key), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ||
                    float.IsNaN(value) || float.IsInfinity(value) || value < 0) throw new InvalidDataException("元素粒子参数无效：" + key);
                return value;
            }
            string shape = Cell("Shape"), motion = Cell("Motion");
            if (Array.IndexOf(new[] { "Ember", "Droplet", "Arc", "Crystal", "Curl", "Shard", "Leaf", "Spark" }, shape) < 0 ||
                Array.IndexOf(new[] { "BurstRise", "BurstFall", "BranchFlicker", "BurstSpin", "SwirlOut", "DriftOut", "DirectionalBurst" }, motion) < 0)
                throw new InvalidDataException("未知元素粒子形状或运动：" + element);
            result.particles.Add(element, new Particles { Shape = shape, Motion = motion,
                CountMultiplier = Number("CountMultiplier"), SizeMultiplier = Number("SizeMultiplier") });
        }
        foreach (string element in Elements) if (!result.particles.ContainsKey(element)) throw new InvalidDataException("缺少元素粒子：" + element);
        return result;
    }

    private static void Validate(Profile p)
    {
        foreach (string key in NumericKeys)
        {
            if (!p.Values.TryGetValue(key, out string raw) || !float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float n) ||
                float.IsNaN(n) || float.IsInfinity(n) || (!Signed.Contains(key) && n < 0))
                throw new InvalidDataException("缺少或无效的命中特效数字：" + key);
        }
        foreach (string key in new[] { "Pattern", "DrawLayer", "ColorSource" })
            if (!p.Values.ContainsKey(key)) throw new InvalidDataException("缺少命中特效参数：" + key);
        if (Array.IndexOf(Patterns, p.Text("Pattern")) < 0 || (p.Text("DrawLayer") != "Front" && p.Text("DrawLayer") != "Behind") || p.Text("ColorSource") != "Element")
            throw new InvalidDataException("命中特效 Pattern/DrawLayer/ColorSource 不支持。");
        foreach (string pair in new[] { "CenterX", "CenterY", "DepthScale", "Opacity" })
        {
            string suffix = pair.StartsWith("Center", StringComparison.Ordinal) ? "Ratio" : "";
            float min = p.Number(pair + "Min" + suffix), max = p.Number(pair + "Max" + suffix);
            if (min > max || ((pair.StartsWith("Center", StringComparison.Ordinal) || pair == "Opacity") && max > 1) || (pair == "DepthScale" && min <= 0))
                throw new InvalidDataException("命中特效范围无效：" + pair);
        }
        foreach (string key in new[] { "Enabled", "ElementParticlesEnabled" })
            if (p.Number(key) != 0 && p.Number(key) != 1) throw new InvalidDataException(key + " 必须是 0/1。");
        float count = p.Number("ParticleCount");
        if (count != (int)count || count > 256 || p.Number("MaxScaleMultiplier") <= 0 || p.Number("FinalHitScale") <= 0 || p.Duration <= 0)
            throw new InvalidDataException("命中特效数量、比例或时长无效。");
    }
}

/// <summary>Pure visual hash: neither preview nor geometry consumes combat RNG.</summary>
public static class HitEffectMath
{
    public static uint Mix(uint value)
    {
        unchecked { value ^= value >> 16; value *= 0x7FEB352Du; value ^= value >> 15; value *= 0x846CA68Bu; return value ^ (value >> 16); }
    }
    public static float Unit(uint seed, uint channel) => (Mix(seed ^ Mix(channel + 0x51ED270Bu)) >> 8) / 16777216f;
    public static float Range(uint seed, uint channel, float min, float max) => Mathf.Lerp(min, max, Unit(seed, channel));
    public static uint CueSeed(int seed, int hit, int target) => Mix(unchecked((uint)seed) ^ Mix((uint)hit + 1) ^ Mix((uint)target + 0xA341316Cu));
    public static Vector2 Center(UIHitEffectStyle.Profile p, uint seed) => new Vector2(
        Range(seed, 1, p.Number("CenterXMinRatio"), p.Number("CenterXMaxRatio")),
        Range(seed, 2, p.Number("CenterYMinRatio"), p.Number("CenterYMaxRatio")));
    public static float Scale(UIHitEffectStyle.Profile p, uint seed, int hit, int count) => Mathf.Min(p.Number("MaxScaleMultiplier"),
        Range(seed, 3, p.Number("DepthScaleMin"), p.Number("DepthScaleMax")) * (1 + hit * p.Number("HitScaleProgress")) *
        (count > 1 && hit == count - 1 ? p.Number("FinalHitScale") : 1));
    public static bool Critical(uint seed, float rate) => Unit(seed, 0xC817u) < Mathf.Clamp01(rate);
    public static float CueTime(uint seed, int hit, float interval, float jitter, float previous)
        => Mathf.Max(previous, jitter + hit * interval + Range(seed, 0x718u, -jitter, jitter));
}
