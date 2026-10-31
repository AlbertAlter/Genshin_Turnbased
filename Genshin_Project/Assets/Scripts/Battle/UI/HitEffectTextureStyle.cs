using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OfficeOpenXml;
using UnityEngine;

/// <summary>Texture composition only. Hit positions/angle/depth still belong to HitEffects.xlsx.</summary>
public sealed class HitEffectTextureStyle
{
    public enum PlaybackMode { PerTarget, PerHitGroup }
    public const string FileName = "HitEffectTextures.xlsx";
    public const string ArtRoot = "art_assets/UI/SlashComponents/";
    public sealed class Profile
    {
        internal readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);
        public string Text(string key) => Values[key];
        public float Number(string key) => float.Parse(Values[key], CultureInfo.InvariantCulture);
        public PlaybackMode Mode => (PlaybackMode)Enum.Parse(typeof(PlaybackMode), Text("PlaybackMode"));
        public string[] Bodies => new[] { Text("BodyTextureA"), Text("BodyTextureB") }.Where(x => x.Length > 0).ToArray();
        public float Duration(UIHitEffectStyle.Profile impact) => Mathf.Max(impact.Duration,
            Mathf.Max(Number("RevealSeconds"), Number("EraseDelaySeconds") + Number("EraseSeconds")) + Number("SecondSlashDelaySeconds"));
    }
    private readonly Profile[,] profiles = new Profile[5, 2];
    public Profile Get(HitEffectWeapon weapon, bool heavy) => profiles[(int)weapon, heavy ? 1 : 0];
    private static readonly string[] TextKeys = { "BodyTextureA", "BodyTextureB", "GlowTexture", "FlashTexture", "ParticleTexture", "ProjectileTexture", "RevealMode", "PlaybackMode" };
    private static readonly string[] NumberKeys = { "BodyWidthMultiplier", "BodyHeightRatio", "RevealSeconds", "EraseDelaySeconds", "EraseSeconds", "EdgeSoftness",
        "GlowOpacity", "GlowScale", "FlashOpacity", "FlashScale", "CoreWhiteMix", "ParticleOpacity", "ParticleSizeMultiplier",
        "ParticleSpeedMultiplier", "MirrorChance", "StretchMin", "StretchMax", "SecondSlashAngleDegrees", "SecondSlashDelaySeconds",
        "FlightSeconds", "ProjectileWidthRatio", "SourceForwardRatio", "SourceHeightRatio",
        "GroupPaddingCardWidthRatio", "GroupHeightSpanRatio", "GroupMaxHeightCardHeightRatio", "GroupAngleDegrees" };
    public static HitEffectTextureStyle Load() => ReadFile(Path.Combine(Application.streamingAssetsPath, "Data", "UI", FileName));
    public static HitEffectTextureStyle ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var book = new ExcelPackage(stream);
        var result = new HitEffectTextureStyle();
        string[] sheets = { "单手剑", "双手剑", "弓", "长柄", "法器" };
        for (int weapon = 0; weapon < 5; weapon++)
        {
            var sheet = book.Workbook.Worksheets[sheets[weapon]];
            if (sheet?.Dimension == null) throw new InvalidDataException("缺少命中贴图子表：" + sheets[weapon]);
            var headers = ExcelTableHeaders.For(sheet); headers.Require("Key", "Normal", "Heavy");
            for (int attack = 0; attack < 2; attack++)
            {
                var p = new Profile();
                for (int row = headers.DataStartRow; row <= sheet.Dimension.End.Row; row++)
                {
                    string key = headers.Cell(row, "Key").Text.Trim(); if (key.Length == 0) continue;
                    if ((!TextKeys.Contains(key) && !NumberKeys.Contains(key)) || p.Values.ContainsKey(key))
                        throw new InvalidDataException("命中贴图参数未知或重复：" + key);
                    p.Values.Add(key, Convert.ToString(headers.Cell(row, attack == 0 ? "Normal" : "Heavy").Value, CultureInfo.InvariantCulture)?.Trim() ?? "");
                }
                Validate(p); result.profiles[weapon, attack] = p;
            }
        }
        return result;
    }
    private static void Validate(Profile p)
    {
        // Older published workbooks keep their per-target behavior until the new rows are synced.
        var defaults = new Dictionary<string, string> { { "PlaybackMode", "PerTarget" },
            { "GroupPaddingCardWidthRatio", "0.1" }, { "GroupHeightSpanRatio", "0.5" },
            { "GroupMaxHeightCardHeightRatio", "0.75" }, { "GroupAngleDegrees", "0" } };
        foreach (var pair in defaults) if (!p.Values.ContainsKey(pair.Key)) p.Values.Add(pair.Key, pair.Value);
        foreach (string key in TextKeys) if (!p.Values.ContainsKey(key)) throw new InvalidDataException("缺少命中贴图参数：" + key);
        foreach (string key in NumberKeys)
        {
            if (!p.Values.TryGetValue(key, out string raw) || !float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
                || float.IsNaN(v) || float.IsInfinity(v) || (key != "SecondSlashAngleDegrees" && key != "SourceHeightRatio" && key != "GroupAngleDegrees" && v < 0))
                throw new InvalidDataException("无效的命中贴图数字：" + key);
        }
        if (p.Text("RevealMode") != "Sweep" && p.Text("RevealMode") != "Burst") throw new InvalidDataException("RevealMode 只支持 Sweep/Burst。");
        if (p.Text("PlaybackMode") != "PerTarget" && p.Text("PlaybackMode") != "PerHitGroup")
            throw new InvalidDataException("PlaybackMode 只支持 PerTarget / PerHitGroup。");
        if (p.Number("GroupMaxHeightCardHeightRatio") <= 0) throw new InvalidDataException("群体特效厚度上限必须大于0。");
        if (p.Text("BodyTextureA").Length == 0) throw new InvalidDataException("BodyTextureA 不能为空。");
        foreach (string file in p.Bodies.Concat(new[] { p.Text("GlowTexture"), p.Text("FlashTexture"), p.Text("ParticleTexture"), p.Text("ProjectileTexture") }).Where(x => x.Length > 0))
            if (!ValidFileName(file)) throw new InvalidDataException("贴图必须是 SlashComponents 下的 PNG 文件名：" + file);
        foreach (string key in new[] { "GlowOpacity", "FlashOpacity", "ParticleOpacity", "MirrorChance", "CoreWhiteMix", "EdgeSoftness" })
            if (p.Number(key) > 1) throw new InvalidDataException(key + " 必须在 0～1。");
        foreach (string key in new[] { "BodyWidthMultiplier", "BodyHeightRatio", "RevealSeconds", "EraseSeconds", "StretchMin", "StretchMax", "GlowScale" })
            if (p.Number(key) <= 0) throw new InvalidDataException(key + " 必须大于0。");
        if (p.Number("StretchMin") > p.Number("StretchMax")) throw new InvalidDataException("StretchMin 大于 StretchMax。");
        if ((p.Text("ProjectileTexture").Length > 0) != (p.Number("FlightSeconds") > 0)) throw new InvalidDataException("飞行贴图与正数飞行时长必须同时配置。");
    }
    public static bool ValidFileName(string file) => !string.IsNullOrWhiteSpace(file) && file.Trim() == file && file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        && file.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.') && file.IndexOf("..", StringComparison.Ordinal) < 0;
    public IEnumerable<string> Files => profiles.Cast<Profile>().SelectMany(p => p.Bodies.Concat(new[] {
        p.Text("GlowTexture"), p.Text("FlashTexture"), p.Text("ParticleTexture"), p.Text("ProjectileTexture") })).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal);
}

/// <summary>Pure cosmetic samples. Never consumes BattleRandom and never moves card layout.</summary>
public static class HitTextureMath
{
    public static int Variant(uint seed, int count) => count <= 1 ? 0 : Math.Min(count - 1, (int)(HitEffectMath.Unit(seed, 700) * count));
    public static float Reveal(float age, float duration) => Mathf.Clamp01(age / Mathf.Max(.001f, duration));
    public static float Erase(float age, float delay, float duration) => Mathf.Clamp01((age - delay) / Mathf.Max(.001f, duration));
    public static Vector2 Flight(Vector2 start, Vector2 end, float age, float duration) => Vector2.Lerp(start, end, Reveal(age, duration));
    public static RectInt AlphaBounds(Color32[] pixels, int width, int height, byte threshold = 8)
    {
        if (pixels == null || width <= 0 || height <= 0 || pixels.Length != width * height) throw new ArgumentException("无效的贴图像素。");
        int left = width, bottom = height, right = -1, top = -1;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            if (pixels[y * width + x].a >= threshold) { left = Math.Min(left, x); bottom = Math.Min(bottom, y); right = Math.Max(right, x); top = Math.Max(top, y); }
        return right < 0 ? new RectInt(0, 0, width, height) : new RectInt(left, bottom, right - left + 1, top - bottom + 1);
    }
}
