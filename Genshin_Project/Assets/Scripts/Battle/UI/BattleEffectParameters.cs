using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

public enum BattleEffectKind { ExplosionLight, Explosion, ExplosionLarge, AmberArrowRain, AmberBurningArea, LisaVioletArc, KaeyaGlacialWaltz, KaeyaIcicle, VentiStormEye, VentiStormPulse, VentiUpdraft, VentiWindDomain, DandelionWindOrbit }

/// <summary>Timing inside the authored VFX, independent of the attack timeline's hit schedule.</summary>
public static class BattleEffectTiming
{
    public static float Contact(BattleEffectParameters settings)
        => settings.Kind == BattleEffectKind.AmberArrowRain || settings.Kind == BattleEffectKind.KaeyaIcicle ? settings["FlightSeconds"]
        : settings.Kind == BattleEffectKind.LisaVioletArc ? settings["StrikeDelaySeconds"] + settings["StrikeSeconds"] / 31
        : settings.Kind == BattleEffectKind.VentiStormPulse || settings.Kind == BattleEffectKind.VentiUpdraft ? settings["ContactSeconds"] : 0;
}

/// <summary>One real hit within a committed execution; decorative density never creates damage hits.</summary>
public sealed class BattleEffectHitSlice
{
    public readonly int Index, Count;
    public BattleEffectHitSlice(int index, int count)
    {
        if (count < 1 || index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        Index = index; Count = count;
    }
    public int DecorativeCount(int total) => Math.Max(1, total / Count + (Index < total % Count ? 1 : 0));
}

/// <summary>Keep an authored VFX before contact while the shared timeline is behind a causal barrier.</summary>
public sealed class BattleEffectContactGate
{
    public float Contact { get; }
    public bool Waiting { get; private set; } = true;
    public BattleEffectContactGate(float contact)
    {
        if (float.IsNaN(contact) || float.IsInfinity(contact) || contact < 0) throw new ArgumentOutOfRangeException(nameof(contact));
        Contact = contact;
    }
    public float Limit(float proposed) => Waiting ? Math.Min(proposed, Math.Max(0, Contact - .0001f)) : proposed;
    public void Confirm() => Waiting = false;
}

/// <summary>One authored effect. Targets, seed and playback controls are request data, never parameter rows.</summary>
public sealed class BattleEffectParameter
{
    public readonly string Key, Label, Unit;
    public readonly float Default, Min, Max;
    public BattleEffectParameter(string key, string label, float value, float min, float max, string unit = "ratio")
    { Key = key; Label = label; Default = value; Min = min; Max = max; Unit = unit; }
}

public sealed partial class BattleEffectParameters
{
    public const string Folder = "UE";
    public readonly BattleEffectKind Kind;
    private readonly Dictionary<string, float> values;
    private static BattleEffectParameter P(string key, string label, float value, float min, float max, string unit = "ratio")
        => new BattleEffectParameter(key, label, value, min, max, unit);
    public static readonly BattleEffectKind[] Kinds = (BattleEffectKind[])Enum.GetValues(typeof(BattleEffectKind));
    public static string Label(BattleEffectKind kind) => BattleEffectPreviewCatalog.Get(kind).Label;
    public static bool IsExplosion(BattleEffectKind kind) => kind <= BattleEffectKind.ExplosionLarge;
    public static bool IsSustained(BattleEffectKind kind) => kind == BattleEffectKind.AmberBurningArea || kind == BattleEffectKind.KaeyaGlacialWaltz || kind == BattleEffectKind.VentiStormEye || kind == BattleEffectKind.VentiWindDomain || kind == BattleEffectKind.DandelionWindOrbit;
    public static bool UsesHolderLayers(BattleEffectKind kind) => kind == BattleEffectKind.KaeyaGlacialWaltz || kind == BattleEffectKind.VentiStormEye || kind == BattleEffectKind.DandelionWindOrbit;
    public static bool IsVenti(BattleEffectKind kind) => kind == BattleEffectKind.VentiStormEye || kind == BattleEffectKind.VentiStormPulse || kind == BattleEffectKind.VentiUpdraft || kind == BattleEffectKind.VentiWindDomain;
    public static string FileName(BattleEffectKind kind) => kind + ".xlsx";
    public static string RuntimePath(BattleEffectKind kind) => Path.Combine(Application.streamingAssetsPath, "Data", Folder, FileName(kind));
    public static IReadOnlyList<BattleEffectParameter> Fields(BattleEffectKind kind)
    {
        float duration = kind == BattleEffectKind.ExplosionLight ? .8f : kind == BattleEffectKind.Explosion ? 1.3f : kind == BattleEffectKind.ExplosionLarge ? 1.7f : kind == BattleEffectKind.AmberBurningArea ? 4 : 2;
        if (kind == BattleEffectKind.KaeyaGlacialWaltz) duration = 2.4f;
        if (kind == BattleEffectKind.KaeyaIcicle) duration = 1.3333333f;
        if (kind == BattleEffectKind.DandelionWindOrbit) duration = 4.8f;
        if (IsVenti(kind)) duration = kind == BattleEffectKind.VentiStormEye ? 2.4f : kind == BattleEffectKind.VentiWindDomain ? 7.2f : kind == BattleEffectKind.VentiUpdraft ? 1.15f : .75f;
        var result = new List<BattleEffectParameter>
        {
            P("DurationSeconds", "单段时长（秒）", duration, .1f, 15, "seconds"),
            P("RangeScale", "范围倍率", 1, .25f, 3), P("WidthScale", "横向范围倍率", 1, .25f, 3),
            P("HeightScale", "纵向范围倍率", 1, .25f, 3), P("Opacity", "整体透明度", 1, 0, 1),
            P("Intensity", "发光强度", 1.4f, .1f, 6), P("Perspective", "景深透视强度", .12f, 0, .7f),
            P("FadeInSeconds", "出现淡入（秒）", .03f, 0, 2, "seconds"),
            P("FadeOutSeconds", "停止淡出（秒）", .18f, 0, 2, "seconds"),
            P("ColorRed", "乘色 · 红", 1, 0, 2), P("ColorGreen", "乘色 · 绿", 1, 0, 2), P("ColorBlue", "乘色 · 蓝", 1, 0, 2)
        };
        if (kind == BattleEffectKind.DandelionWindOrbit) result.AddRange(DandelionFields());
        else if (IsVenti(kind)) result.AddRange(VentiFields(kind));
        else if (IsExplosion(kind)) result.AddRange(new[]
        {
            P("ParticleDensity", "火团与火星密度", 1, .25f, 2), P("FireSize", "单簇火焰尺寸", 1.35f, .25f, 3),
            P("LowerFill", "下半部火团填充", 1, 0, 2), P("RingSize", "冲击环范围", 1.12f, .25f, 2),
            P("SmokeOpacity", "烟尘透明度", .65f, 0, 1), P("SparkSize", "火星尺寸", 1, .25f, 3)
        });
        else if (kind == BattleEffectKind.AmberArrowRain) result.AddRange(new[]
        {
            P("ArrowsPerTarget", "每目标箭支数", 4, 1, 10, "count"),
            P("FlightSeconds", "箭支飞行（秒）", .217f, .05f, 2, "seconds"),
            P("LaunchStaggerSeconds", "发射间隔（秒）", .075f, .01f, .5f, "seconds"),
            P("PitchDegrees", "入射向上偏转（度）", 12, 0, 35, "degrees"),
            P("TravelUnits", "前景飞行距离", 8.5f, 2, 15, "units"),
            P("ArrowSize", "箭支尺寸", .65f, .15f, 2), P("ImpactSize", "接触爆焰尺寸", .7f, .1f, 2)
        });
        else if (kind == BattleEffectKind.AmberBurningArea) result.AddRange(new[]
        {
            P("FlamesPerTarget", "每目标火簇数量", 2, 1, 4, "count"),
            P("FlameCycleSeconds", "单簇更新周期（秒）", 1.33f, .3f, 5, "seconds"),
            P("FlameSize", "单簇火焰尺寸", .55f, .1f, 2), P("ScatterInset", "分布半径比例", .88f, .2f, 1),
            P("BoundaryOpacity", "焦痕与边界透明度", .7f, 0, 1), P("EmberCount", "上浮余烬数量", 4, 0, 20, "count")
        });
        else if (kind == BattleEffectKind.KaeyaGlacialWaltz || kind == BattleEffectKind.KaeyaIcicle) result.AddRange(new[]
        {
            P("IcicleCount", "环绕冰棱数量", 3, 3, 4, "count"),
            P("OrbitSeconds", "顺时针环绕周期（秒）", 2.4f, 1, 5, "seconds"),
            P("RadiusX", "环绕水平半径", 1.34f, .5f, 3, "units"),
            P("DepthRadius", "环绕景深半径", .9f, .2f, 2, "units"),
            P("TiltHeight", "环绕纵向半径", .4f, .1f, 1.5f, "units"),
            P("CenterHeight", "环绕中心高度", -.25f, -1, 1, "units"),
            P("IcicleSize", "冰棱尺寸", 1, .2f, 2),
            P("MistOpacity", "冰雾浓度", 1, 0, 2),
            P("GrainSize", "细碎冰屑尺寸", 1, .2f, 3),
            P("ArcBend", "同向发射弧线长度", 1.65f, .2f, 4, "units"),
            P("FlightSeconds", "冰棱飞行（秒）", .5f, .1f, 2, "seconds"),
            P("ImpactSeconds", "命中碎冰时长（秒）", .8333333f, .2f, 3, "seconds"),
            P("ImpactSize", "命中碎冰范围", 1, .2f, 3),
            P("SlotReformDelaySeconds", "离轨槽空缺（秒）", .2666667f, 0, 2, "seconds"),
            P("SlotReformSeconds", "补回冰棱淡入（秒）", .2666667f, .01f, 2, "seconds")
        });
        else result.AddRange(new[]
        {
            P("CastSeconds", "向上放电时长（秒）", .6f, .1f, 2, "seconds"),
            P("StrikeDelaySeconds", "落雷开始（秒）", .6f, 0, 3, "seconds"),
            P("StrikeSeconds", "落雷片段时长（秒）", 1.067f, .2f, 3, "seconds"),
            P("StrikeHeight", "落雷高度倍率", 1, .25f, 3), P("StrikeWidth", "雷击宽度倍率", 1, .25f, 3),
            P("CastOffsetX", "固定发射点 · 卡宽横移", -.25f, -1, 1), P("CastOffsetY", "固定发射点 · 卡高纵移", .25f, -1, 1)
        });
        return result;
    }
    private BattleEffectParameters(BattleEffectKind kind, Dictionary<string, float> values) { Kind = kind; this.values = values; }
    public float this[string key] => values[key];
    public Dictionary<string, float> Values() => new Dictionary<string, float>(values, StringComparer.Ordinal);
    public BattleEffectParameters Copy() => new BattleEffectParameters(Kind, Values());
    public static BattleEffectParameters Defaults(BattleEffectKind kind)
    { var result = new Dictionary<string, float>(StringComparer.Ordinal); foreach (var p in Fields(kind)) result.Add(p.Key, p.Default); return new BattleEffectParameters(kind, result); }
    public static BattleEffectParameters Load(BattleEffectKind kind) => ReadFile(kind, RuntimePath(kind));
    public static BattleEffectParameters ReadFile(BattleEffectKind kind, string path)
    {
        var raw = BattlefieldDamagePopupStyle.ReadParameters(path);
        if (raw.Count != Fields(kind).Count) throw new InvalidDataException(FileName(kind) + " 含有缺失或未知参数。");
        var result = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var field in Fields(kind))
        {
            if (!raw.TryGetValue(field.Key, out string text) || !float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                throw new InvalidDataException(FileName(kind) + " 参数缺失或非数字：" + field.Key);
            result.Add(field.Key, value);
        }
        return Parse(kind, result);
    }
    public static BattleEffectParameters Parse(BattleEffectKind kind, IReadOnlyDictionary<string, float> raw)
    {
        var result = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var field in Fields(kind))
        {
            if (!raw.TryGetValue(field.Key, out float value) || float.IsNaN(value) || float.IsInfinity(value) || value < field.Min || value > field.Max)
                throw new InvalidDataException("特效参数超出范围：" + field.Key);
            if (field.Unit == "count" && value != (int)value) throw new InvalidDataException("特效数量参数需要整数：" + field.Key);
            result.Add(field.Key, value);
        }
        return new BattleEffectParameters(kind, result);
    }
}

/// <summary>Only actual supplied rectangles determine coverage. One target stays centered; groups use their bounds midline.</summary>
public readonly struct BattleEffectRange
{
    public readonly Vector2 Center, HalfSize;
    public readonly float PixelsPerUnit;
    public readonly int Count;
    public BattleEffectRange(Vector2 center, Vector2 half, float units, int count) { Center = center; HalfSize = half; PixelsPerUnit = units; Count = count; }
    public static BattleEffectRange FromBounds(Rect bounds, float averageCardWidth, int count)
    {
        if (count < 1 || count > 5 || !(bounds.width > 0) || !(bounds.height > 0) || !(averageCardWidth > 0) ||
            float.IsNaN(bounds.x) || float.IsNaN(bounds.y) || float.IsInfinity(bounds.x) || float.IsInfinity(bounds.y) ||
            float.IsInfinity(bounds.width) || float.IsInfinity(bounds.height) || float.IsInfinity(averageCardWidth))
            throw new ArgumentException("特效范围需要1～5个有效卡位的有限矩形。");
        return new BattleEffectRange(bounds.center, bounds.size * .5f, averageCardWidth / 1.97f, count);
    }
    public static bool TryCalculate(RectTransform space, IReadOnlyList<RectTransform> targets, out BattleEffectRange range, Vector3[] cornerBuffer = null, bool includeInactive = false)
    {
        range = default;
        if (space == null || targets == null || targets.Count < 1 || targets.Count > 5) return false;
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        float widths = 0;
        var corners = cornerBuffer ?? new Vector3[4];
        for (int index = 0; index < targets.Count; index++)
        {
            var target = targets[index];
            if (target == null || (!includeInactive && !target.gameObject.activeInHierarchy)) return false;
            for (int before = 0; before < index; before++) if (targets[before] == target) return false;
            target.GetWorldCorners(corners);
            Vector2 lo = new Vector2(float.PositiveInfinity, float.PositiveInfinity), hi = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var corner in corners) { Vector2 p = space.InverseTransformPoint(corner); lo = Vector2.Min(lo, p); hi = Vector2.Max(hi, p); }
            min = Vector2.Min(min, lo); max = Vector2.Max(max, hi); widths += hi.x - lo.x;
        }
        if (!(widths > 0)) return false;
        range = FromBounds(new Rect(min, max - min), widths / targets.Count, targets.Count);
        return true;
    }
}
