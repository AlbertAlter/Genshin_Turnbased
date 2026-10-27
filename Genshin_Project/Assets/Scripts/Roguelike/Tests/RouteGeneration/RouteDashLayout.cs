using System.Collections.Generic;
using UnityEngine;

namespace Roguelike.Tests.RouteGeneration
{
    public struct RouteDash
    {
        public float Start, Length, Width, Angle, Offset;
        public int Index;
    }

    /// <summary>Stable visual noise only: never draws from the gameplay random stream.</summary>
    public static class RouteDashLayout
    {
        public static uint EdgeSeed(int seed, int from, int to) => Hash(Hash((uint)seed ^ (uint)from) ^ (uint)to);
        private static uint Hash(uint value)
        {
            unchecked
            {
                value ^= value >> 16; value *= 0x7feb352du;
                value ^= value >> 15; value *= 0x846ca68bu;
                return value ^ (value >> 16);
            }
        }
        public static float Noise(uint seed, int index, int channel)
        {
            uint hash;
            unchecked { hash = Hash(seed ^ ((uint)index * 0x9e3779b9u) ^ ((uint)channel * 0x85ebca6bu)); }
            return (hash & 0x00ffffffu) / 16777215f * 2f - 1f;
        }

        public static List<RouteDash> Build(float length, RouteMapStyle style, uint seed)
        {
            var result = new List<RouteDash>();
            if (length <= 0f) return result;
            // Bound mesh size even if an artist accidentally chooses microscopic dash lengths.
            int count = Mathf.Clamp(Mathf.FloorToInt((length + style.GapLength) /
                (style.DashLength + style.GapLength)), 1, 512);
            var lengths = new float[count];
            var gaps = new float[count];
            float totalLength, totalGap;
            while (true)
            {
                totalLength = 0; totalGap = 0;
                for (int i = 0; i < count; i++)
                {
                    lengths[i] = style.DashLength * (1f + Noise(seed, i, 1) * style.LengthVariation);
                    gaps[i] = style.GapLength * (1f + Noise(seed, i, 2) * style.GapVariation);
                    totalLength += lengths[i];
                    if (i < count - 1) totalGap += gaps[i];
                }
                float needed = style.FitMode == RouteLineFitMode.CenterPattern ? totalLength + totalGap : totalLength;
                if (count == 1 || needed < length - 0.001f) break;
                count--;
            }
            float gapScale = style.FitMode == RouteLineFitMode.AdjustGap && count > 1
                ? (length - totalLength) / totalGap : 1f;
            float cursor = style.FitMode == RouteLineFitMode.CenterPattern
                ? Mathf.Max(0f, (length - totalLength - totalGap) * 0.5f) : 0f;
            if (count == 1)
            {
                lengths[0] = Mathf.Min(lengths[0], length);
                cursor = (length - lengths[0]) * 0.5f;
            }
            for (int i = 0; i < count; i++)
            {
                result.Add(new RouteDash
                {
                    Start = cursor, Length = lengths[i], Index = i,
                    Width = style.Width * (1f + Noise(seed, i, 3) * style.WidthVariation),
                    Angle = Noise(seed, i, 4) * style.AngleVariation,
                    Offset = Noise(seed, i, 5) * style.OffsetVariation
                });
                cursor += lengths[i] + gaps[i] * gapScale;
            }
            return result;
        }
    }
}
