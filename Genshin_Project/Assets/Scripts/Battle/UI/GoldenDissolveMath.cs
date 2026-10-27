using System;
using UnityEngine;

/// <summary>Display-only, deterministic math shared by card dissolve and fragment births. Never draws BattleRandom.</summary>
public static class GoldenDissolveMath
{
    public readonly struct Fragment
    {
        public readonly Vector2 UV, Drift;
        public readonly float BirthSeconds, Diameter, Rotation;
        public Fragment(Vector2 uv, Vector2 drift, float birth, float diameter, float rotation)
        { UV = uv; Drift = drift; BirthSeconds = birth; Diameter = diameter; Rotation = rotation; }
    }

    public static Vector4 OffsetForSeed(int seed)
    {
        uint a = Mix(unchecked((uint)seed)), b = Mix(a ^ 0x9E3779B9u);
        return new Vector4((a & 65535) / 65535f * 128, (b & 65535) / 65535f * 128, 0, 0);
    }

    public static uint Mix(uint value)
    {
        unchecked { value ^= value >> 16; value *= 0x7FEB352Du; value ^= value >> 15; value *= 0x846CA68Bu; return value ^ (value >> 16); }
    }
    private static float Unit(uint seed) => (Mix(seed) >> 8) / 16777216f;
    private static float Frac(float value) => value - Mathf.Floor(value);
    private static float Hash(float x, float y)
    {
        x = Frac(x * 123.34f); y = Frac(y * 456.21f);
        float dot = x * (x + 45.32f) + y * (y + 45.32f);
        return Frac((x + dot) * (y + dot));
    }
    private static float Noise(float x, float y)
    {
        float ix = Mathf.Floor(x), iy = Mathf.Floor(y), tx = Frac(x), ty = Frac(y);
        tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
        return Mathf.Lerp(Mathf.Lerp(Hash(ix, iy), Hash(ix + 1, iy), tx),
            Mathf.Lerp(Hash(ix, iy + 1), Hash(ix + 1, iy + 1), tx), ty);
    }

    // Keep this scalar field and threshold in sync with Resources/UI/GoldenDissolve.shader.
    public static float BirthProgress(UIGoldenDissolveStyle style, Vector2 uv, float aspect, int seed)
    {
        Vector4 offset = OffsetForSeed(seed);
        float x = uv.x * aspect * style.NoiseScale + offset.x, y = uv.y * style.NoiseScale + offset.y;
        float field = (Noise(x, y) * 4 + Noise(x * 2.03f + 17.1f, y * 2.03f + 17.1f) * 2 +
            Noise(x * 4.07f + 31.7f, y * 4.07f + 31.7f)) / 7;
        Vector2 d = style.Direction;
        float directed = Mathf.Clamp01(.5f + ((uv.x - .5f) * d.x + (uv.y - .5f) * d.y) /
            Mathf.Max(.001f, Mathf.Abs(d.x) + Mathf.Abs(d.y)));
        field = Mathf.Lerp(field, directed, d.sqrMagnitude > 0 ? style.DirectionWeight : 0);
        float low = -style.EdgeWidth - 2 * style.EdgeSoftness, high = 1 + 2 * style.EdgeSoftness;
        return Mathf.Clamp01((field - low) / (high - low));
    }

    public static Fragment[] BuildFragments(UIGoldenDissolveStyle style, Vector2 cardSize, int seed)
    {
        if (style == null) throw new ArgumentNullException(nameof(style));
        if (cardSize.x <= 0 || cardSize.y <= 0) throw new ArgumentOutOfRangeException(nameof(cardSize));
        var result = new Fragment[style.FragmentCount];
        if (result.Length == 0) return result;
        float aspect = cardSize.x / cardSize.y;
        int columns = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(result.Length * aspect)));
        int rows = Mathf.CeilToInt((float)result.Length / columns);
        for (int i = 0; i < result.Length; i++)
        {
            uint key = Mix(unchecked((uint)seed ^ (uint)(i + 1) * 0x9E3779B9u));
            var uv = new Vector2((i % columns + .15f + Unit(key) * .7f) / columns,
                (i / columns + .15f + Unit(key ^ 0xA511E9B3u) * .7f) / rows);
            float diameter = Mathf.Lerp(style.FragmentSizeMinHeightRatio, style.FragmentSizeMaxHeightRatio,
                Unit(key ^ 0x63D83595u)) * cardSize.y;
            var drift = new Vector2((Unit(key ^ 0x9E3779B9u) * 2 - 1) * style.FragmentSpreadHeightRatio,
                style.FragmentDriftHeightRatio + (Unit(key ^ 0xB5297A4Du) * 2 - 1) * style.FragmentSpreadHeightRatio * .25f) * cardSize.y;
            result[i] = new Fragment(uv, drift, BirthProgress(style, uv, aspect, seed) * style.DurationSeconds,
                diameter, Unit(key ^ 0x1B56C4E9u) * Mathf.PI * 2);
        }
        return result;
    }

    public static bool Evaluate(Fragment fragment, UIGoldenDissolveStyle style, float seconds,
        out Vector2 displacement, out float diameter, out float alpha)
    {
        float age = seconds - fragment.BirthSeconds;
        displacement = Vector2.zero; diameter = alpha = 0;
        if (age < 0 || style.FragmentLifetimeSeconds <= 0 || age >= style.FragmentLifetimeSeconds) return false;
        float u = Mathf.Clamp01(age / style.FragmentLifetimeSeconds);
        displacement = fragment.Drift * u;
        diameter = fragment.Diameter * (1 - u);
        float fadeIn = style.FragmentFadeInRatio <= 0 ? 1 : Mathf.SmoothStep(0, 1, u / style.FragmentFadeInRatio);
        alpha = fadeIn * (1 - u * u);
        return alpha > 0 && diameter > 0;
    }

    public static float ShadowAlpha(float bodyProgress, float power) => Mathf.Pow(1 - Mathf.Clamp01(bodyProgress), power);
}
