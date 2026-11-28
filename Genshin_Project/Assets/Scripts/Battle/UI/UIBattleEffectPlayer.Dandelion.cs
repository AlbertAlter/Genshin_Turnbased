using UnityEngine;

public sealed partial class UIBattleEffectPlayer
{
    private void DrawDandelion(BattleEffectPart part, Matrix4x4 pose, Color color, float alpha, float depthContrast = 0)
    {
        if (fade * alpha < .0001f) return;
        float perspective = settings["Perspective"] / .12f / (settings["CameraDistanceUnits"] * .035f);
        // Split native triangles at the card plane: one winding ribbon can be on both sides of its holder.
        Model(part).Draw(back, pose, range.Center, range.PixelsPerUnit, color, fade * alpha * Mathf.Lerp(1, .65f, depthContrast), 0, perspective, depthSide: 1);
        Model(part).Draw(front, pose, range.Center, range.PixelsPerUnit, color, fade * alpha, 0, perspective, depthSide: -1);
    }
    private void RenderDandelionWindOrbit()
    {
        float width = settings["OrbitWidth"] * settings["RangeScale"] * settings["WidthScale"];
        float cardHeightRatio = range.HalfSize.y * 2 / range.PixelsPerUnit / 3.38f;
        float rise = settings["RiseSpan"] * settings["RangeScale"] * settings["HeightScale"] * cardHeightRatio;
        float depth = settings["DepthRadius"] * settings["RangeScale"];
        float turns = settings["SpiralTurns"], cycle = Elapsed / settings["CycleSeconds"];
        RenderDandelionWind(width, depth, rise);
        int count = Mathf.RoundToInt(settings["SeedCount"]);
        float timing = settings["SeedTimingVariation"], speedVariation = settings["SeedSpeedVariation"], drift = settings["SeedDrift"];
        for (int i = 0; i < count; i++)
        {
            float age = DandelionSpiralMotion.SeedAge(cycle, i, count, seed, timing, speedVariation, out long generation);
            float angle = DandelionSpiralMotion.SeedAngle(age, turns, i, seed, generation, timing, speedVariation);
            var position = DandelionSpiralMotion.SeedPosition(age, angle, width, depth, rise, drift, cycle, i, seed, generation, timing);
            float size = Mathf.Lerp(.8f + DandelionSpiralMotion.Hash(i, 6) * .3f,
                .8f + DandelionSpiralMotion.Noise(i, 6, seed, generation) * .3f, timing) * settings["SeedSize"];
            float phase = cycle * Mathf.PI * 2 * (.45f + .65f * DandelionSpiralMotion.Noise(i, 11, seed))
                + DandelionSpiralMotion.Noise(i, 12, seed, generation) * Mathf.PI * 2;
            var rotation = Quaternion.Euler(Mathf.Sin(phase) * 9, Mathf.Cos(phase) * 7, Mathf.Sin(phase + i * .4f) * 18);
            var pose = Matrix4x4.TRS(position, rotation, Vector3.one * size);
            float alpha = DandelionSpiralMotion.SeedEnvelope(age);
            DrawDandelion(BattleEffectPart.DandelionSeed, pose, tint, alpha * .86f);
            var glow = pose * Matrix4x4.Translate(new Vector3(.012f, .218f, .01f))
                * Matrix4x4.Scale(Vector3.one * settings["GlowSize"]);
            DrawDandelion(BattleEffectPart.DandelionGlow, glow, tint * new Color(.25f, .94f, .72f, 1), alpha * settings["SeedGlow"]);
        }
    }
    private void RenderDandelionWind(float width, float depth, float rise)
    {
        int count = Mathf.RoundToInt(settings["WindCount"]);
        float cycles = Elapsed / settings["WindCycleSeconds"], turns = settings["WindOrbitTurns"];
        float timing = settings["WindTimingVariation"], speedVariation = settings["WindSpeedVariation"], wrap = settings["WindWrapStrength"];
        for (int i = 0; i < count; i++)
        {
            void Strand(float time, float opacity, bool detail)
            {
                var sample = DandelionSpiralMotion.Wind(time, i, count, seed, turns, timing, speedVariation);
                float radius = sample.Radius * (detail ? .94f : 1);
                var pose = Matrix4x4.Translate(new Vector3(0, rise * (sample.Age - .5f), 0))
                    * Matrix4x4.Scale(new Vector3(width * radius, rise / turns * sample.Pitch * (detail ? .7f : 1), depth * radius))
                    * Matrix4x4.Rotate(Quaternion.Euler(0, -sample.Angle * Mathf.Rad2Deg, 0));
                DrawDandelion(BattleEffectPart.DandelionWind, pose, tint, settings["WindOpacity"] * opacity * sample.Alpha, wrap);
            }
            Strand(cycles, 1, false);
            if (wrap > .001f)
            {
                float lag = .035f + .04f * DandelionSpiralMotion.Noise(i, 31, seed);
                Strand(cycles - lag, wrap * .32f, true);
            }
        }
    }
}

public readonly struct DandelionWindSample
{
    public readonly float Age, Angle, Radius, Pitch, Alpha;
    public readonly long Generation;
    public DandelionWindSample(float age, float angle, float radius, float pitch, float alpha, long generation)
    { Age = age; Angle = angle; Radius = radius; Pitch = pitch; Alpha = alpha; Generation = generation; }
}

/// <summary>Visual sampling only. At the front (negative Z), motion is left to right, then around the back while rising.</summary>
public static class DandelionSpiralMotion
{
    private const double Tau = System.Math.PI * 2;
    public static float Hash(int index, int channel) => ((index * 73 + channel * 31 + 17) % 997) / 997f;
    // Deterministic visual hashing: sampling/seek never consumes gameplay randomness or advances mutable particle state.
    public static float Noise(int index, int channel, int visualSeed, long generation = 0)
    {
        uint value;
        unchecked
        {
            value = (uint)visualSeed ^ ((uint)index + 1) * 0x9E3779B9u ^ ((uint)channel + 1) * 0x85EBCA6Bu;
            value ^= (uint)generation * 0xC2B2AE35u ^ (uint)((ulong)generation >> 32) * 0x27D4EB2Fu;
            value ^= value >> 16; value *= 0x7FEB352Du; value ^= value >> 15; value *= 0x846CA68Bu; value ^= value >> 16;
        }
        return (value & 0xFFFFFFu) / 16777216f;
    }
    private static double IntegralWave(double cycles, double frequency, double phase)
        => (System.Math.Sin(cycles * Tau * frequency + phase) - System.Math.Sin(phase)) / (Tau * frequency);
    public static int WindSeed(int visualSeed) => visualSeed ^ unchecked((int)0xD1B54A35u);
    public static double WindClock(double cycles, int index, int count, int visualSeed, float timingVariation, float speedVariation)
        => SeedClock(cycles, index, count, WindSeed(visualSeed), timingVariation, speedVariation);
    public static DandelionWindSample Wind(float cycles, int index, int count, int visualSeed, float turns, float timingVariation, float speedVariation)
    {
        int windSeed = WindSeed(visualSeed);
        float age = SeedAge(cycles, index, count, windSeed, timingVariation, speedVariation, out long generation);
        float progress = ForwardProgress(age, index, windSeed, generation, speedVariation);
        float offset = Mathf.Lerp(index * .45f, Noise(index, 26, windSeed, generation) * Mathf.PI * 2, timingVariation);
        float breath = 1 + .06f * timingVariation * (
            .6f * Mathf.Sin(cycles * .93f + Noise(index, 27, windSeed) * Mathf.PI * 2)
            + .4f * Mathf.Sin(cycles * 1.71f + Noise(index, 28, windSeed) * Mathf.PI * 2));
        float radius = Mathf.Lerp(.96f + index * .018f, .88f + .22f * Noise(index, 23, windSeed, generation), timingVariation) * breath;
        float pitch = Mathf.Lerp(1, .72f + .36f * Noise(index, 24, windSeed, generation), timingVariation);
        float alpha = SeedEnvelope(age) * Mathf.Lerp(1, .76f + .24f * Noise(index, 25, windSeed, generation), timingVariation);
        return new DandelionWindSample(age, Angle(progress, turns, offset), radius, pitch, alpha, generation);
    }
    public static double SeedClock(double cycles, int index, int count, int visualSeed, float timingVariation, float speedVariation)
    {
        double phase = (double)index / System.Math.Max(1, count);
        phase += timingVariation * (Noise(index, 1, visualSeed) - phase);
        double rate = 1 + timingVariation * .45 * (Noise(index, 2, visualSeed) * 2 - 1);
        double wave = .65 * IntegralWave(cycles, .19 + .15 * Noise(index, 3, visualSeed), Tau * Noise(index, 4, visualSeed))
            + .35 * IntegralWave(cycles, .57 + .23 * Noise(index, 5, visualSeed), Tau * Noise(index, 7, visualSeed));
        // Positive derivative: rate * (1 +/- speedVariation). Birth intervals vary smoothly rather than forming a queue.
        return phase + rate * (cycles + speedVariation * wave);
    }
    public static float SeedAge(float cycles, int index, int count, int visualSeed, float timingVariation, float speedVariation, out long generation)
    {
        double clock = SeedClock(cycles, index, count, visualSeed, timingVariation, speedVariation);
        generation = (long)System.Math.Floor(clock);
        return (float)(clock - generation);
    }
    public static float SeedAngle(float age, float turns, int index, int visualSeed, long generation, float timingVariation, float speedVariation)
    {
        float progress = ForwardProgress(age, index, visualSeed, generation, speedVariation);
        float offset = Mathf.Lerp(index * .9f, Noise(index, 16, visualSeed, generation) * Mathf.PI * 2, timingVariation);
        return Angle(progress, turns, offset);
    }
    private static float ForwardProgress(float age, int index, int visualSeed, long generation, float speedVariation)
    {
        double wave = .65 * IntegralWave(age, 1, Tau * Noise(index, 14, visualSeed, generation))
            + .35 * IntegralWave(age, 2, Tau * Noise(index, 15, visualSeed, generation));
        return Mathf.Clamp01((float)(age + speedVariation * wave));
    }
    public static Vector3 SeedPosition(float age, float angle, float width, float depth, float rise, float drift, float cycles, int index, int visualSeed, long generation, float timingVariation)
    {
        float amplitude = Mathf.Min(.35f, drift / Mathf.Max(.25f, width));
        float sway = .65f * Mathf.Sin(cycles * (.72f + .8f * Noise(index, 17, visualSeed)) + Noise(index, 18, visualSeed) * Mathf.PI * 2)
            + .35f * Mathf.Sin(cycles * 1.63f + Noise(index, 19, visualSeed) * Mathf.PI * 2);
        float breath = 1 + amplitude * sway;
        float rx = width * Mathf.Lerp(1.04f + Hash(index, 2) * .1f, .95f + .18f * Noise(index, 8, visualSeed, generation), timingVariation) * breath;
        float rz = depth * Mathf.Lerp(1, .85f + .3f * Noise(index, 9, visualSeed, generation), timingVariation) * breath;
        return new Vector3(rx * Mathf.Cos(angle), rise * (age - .5f), -.03f + rz * Mathf.Sin(angle));
    }
    public static float SeedEnvelope(float age)
        => Envelope(age) * Mathf.SmoothStep(0, 1, Mathf.Clamp01(Mathf.Min(age, 1 - age) / .1f));
    public static float Age(float cycles, int index, int count) => Mathf.Repeat(cycles + (float)index / Mathf.Max(1, count), 1);
    public static float Angle(float age, float turns, float offset) => age * turns * Mathf.PI * 2 + Mathf.PI + offset;
    public static float Envelope(float age) => Mathf.Pow(Mathf.Max(0, Mathf.Sin(Mathf.PI * age)), .65f);
    public static Vector3 Position(float age, float turns, float offset, float width, float depth, float rise)
    {
        float angle = Angle(age, turns, offset);
        return new Vector3(width * Mathf.Cos(angle), rise * (age - .5f), -.03f + depth * Mathf.Sin(angle));
    }
}
