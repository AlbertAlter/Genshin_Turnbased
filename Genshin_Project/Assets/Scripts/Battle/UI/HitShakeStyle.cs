using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

/// <summary>Control-click shake profiles only. Attack and hit motions use AttackAnimations.xlsx.</summary>
public sealed class HitShakeStyle
{
    public const string FileName = "HitShake.xlsx";
    public sealed class Profile
    {
        public string ID, Space;
        public float Duration, X, Y, Z, Rotation, Frequency, Decay, MaxTranslation, MaxRotation;
    }
    private readonly Dictionary<string, Profile> profiles = new Dictionary<string, Profile>(StringComparer.Ordinal);
    public static Profile AttackScreen(BattleAttackAnimationSpec spec)
    {
        if (spec == null || !spec.HasScreenShake) return null;
        float amplitude = spec.ScreenShakeAmplitude;
        return new Profile { ID = "AttackScreen/" + spec.EffectID, Space = "Screen", Duration = 0.2f,
            X = spec.ScreenShakeAxes.Contains("X") ? amplitude : 0, Y = spec.ScreenShakeAxes.Contains("Y") ? amplitude : 0,
            Z = spec.ScreenShakeAxes.Contains("Z") ? amplitude : 0, Frequency = 25, Decay = 2, MaxTranslation = 0.3f };
    }
    public Profile Get(string id) => id == "None" ? null : profiles[id];
    public static HitShakeStyle Load() => CampaignUiTables.Read(ReadPublished);
    private static HitShakeStyle ReadPublished() => ReadFile(BattleTimelineSettings.RuntimePath(FileName));

    public static HitShakeStyle ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var package = new ExcelPackage(stream);
        var sheet = package.Workbook.Worksheets["Profiles"];
        if (sheet?.Dimension == null) throw new InvalidDataException("HitShake 缺少 Profiles 子表。");
        var h = ExcelTableHeaders.For(sheet);
        h.Require("Key");
        var style = new HitShakeStyle();
        foreach (var column in h.Columns)
        {
            string id = column.Key;
            if (id == "Key" || id == "Unit" || id == "Description") continue;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int row = h.DataStartRow; row <= sheet.Dimension.End.Row; row++)
            {
                string key = h.Cell(row, "Key").Text.Trim(); if (key.Length == 0) continue;
                if (values.ContainsKey(key)) throw new InvalidDataException("HitShake 重复参数：" + key);
                values.Add(key, Convert.ToString(sheet.Cells[row, column.Value].Value, CultureInfo.InvariantCulture)?.Trim() ?? "");
            }
            float Number(string key)
            {
                if (!values.TryGetValue(key, out string raw) || !float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float n) || float.IsNaN(n) || float.IsInfinity(n) || n < 0)
                    throw new InvalidDataException("HitShake 数字无效：" + id + "/" + key);
                return n;
            }
            var allowed = new HashSet<string> { "Space", "DurationSeconds", "AmplitudeXRatio", "AmplitudeYRatio", "RotationDegrees", "FrequencyHz", "DecayPower", "MaxTranslationRatio", "MaxRotationDegrees" };
            foreach (string key in values.Keys) if (!allowed.Contains(key)) throw new InvalidDataException("HitShake 未知参数：" + key);
            if (!values.TryGetValue("Space", out string space)) throw new InvalidDataException("HitShake 缺少 Space。");
            var p = new Profile { ID = id, Space = space, Duration = Number("DurationSeconds"),
                X = Number("AmplitudeXRatio"), Y = Number("AmplitudeYRatio"), Rotation = Number("RotationDegrees"),
                Frequency = Number("FrequencyHz"), Decay = Number("DecayPower"), MaxTranslation = Number("MaxTranslationRatio"), MaxRotation = Number("MaxRotationDegrees") };
            if (id == "None" || style.profiles.ContainsKey(id) || (p.Space != "Card" && p.Space != "Screen") || p.Duration <= 0 || p.Decay <= 0 || p.Frequency <= 0)
                throw new InvalidDataException("HitShake 档位无效或重复：" + id);
            style.profiles.Add(id, p);
        }
        return style;
    }
}

public struct HitShakePose { public Vector2 Offset; public float Rotation, ScaleDelta; }

/// <summary>Pure damped visual oscillation. No gameplay RNG; no accumulated transform writes.</summary>
public static class HitShakeMath
{
    public static HitShakePose Sample(HitShakeStyle.Profile p, Vector2 size, uint seed, float age)
    {
        if (age < 0 || age >= p.Duration) return default;
        float envelope = Mathf.Pow(1 - age / p.Duration, p.Decay);
        float wave = age * p.Frequency * 2 * Mathf.PI;
        float Osc(uint channel) => Mathf.Cos(wave * (channel == 2 ? 1.19f : 1) + HitEffectMath.Unit(seed, channel + 900) * 2 * Mathf.PI);
        return Clamp(new HitShakePose { Offset = new Vector2(Osc(1) * p.X * size.x, Osc(2) * p.Y * size.y) * envelope,
            Rotation = Osc(3) * p.Rotation * envelope, ScaleDelta = Osc(4) * p.Z * envelope }, size, p.MaxTranslation, p.MaxRotation);
    }
    public static HitShakePose Clamp(HitShakePose pose, Vector2 size, float translationLimit, float rotationLimit)
    {
        pose.Offset = Vector2.ClampMagnitude(pose.Offset, Mathf.Min(size.x, size.y) * translationLimit);
        pose.Rotation = Mathf.Clamp(pose.Rotation, -rotationLimit, rotationLimit);
        pose.ScaleDelta = Mathf.Clamp(pose.ScaleDelta, -0.2f, 0.2f); return pose;
    }
}
