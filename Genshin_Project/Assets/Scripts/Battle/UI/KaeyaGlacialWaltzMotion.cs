using System;
using UnityEngine;

/// <summary>Display-only orbit/flight math shared by the formal and preview players.</summary>
public static class KaeyaGlacialWaltzMotion
{
    public static float Phase(float seconds, float period, int slot, int count)
        => -Mathf.PI / 3 - 2 * Mathf.PI * seconds / period + 2 * Mathf.PI * slot / count;
    public static Vector3 Orbit(float phase, Vector3 radii, float height)
        => new Vector3(radii.x * Mathf.Cos(phase), height + radii.y * Mathf.Sin(phase), radii.z * Mathf.Sin(phase));
    public static Vector3 Tangent(float phase, Vector3 radii)
        => new Vector3(radii.x * Mathf.Sin(phase), -radii.y * Mathf.Cos(phase), -radii.z * Mathf.Cos(phase));
    public static int SelectSlot(float seconds, float period, int count, Vector3 radii, float height, Vector3 target, Func<int, bool> available = null)
    {
        int chosen = -1; float best = float.NegativeInfinity;
        for (int pass = 0; pass < 2 && chosen < 0; pass++)
            for (int i = 0; i < count; i++)
            {
                if (pass == 0 && available != null && !available(i)) continue;
                float phase = Phase(seconds, period, i, count);
                Vector3 delta = target - Orbit(phase, radii, height), tangent = Tangent(phase, radii);
                float cross = tangent.x * delta.y - tangent.y * delta.x;
                float score = Vector2.Dot(new Vector2(tangent.x, tangent.y).normalized, new Vector2(delta.x, delta.y).normalized);
                if (cross >= -.00001f) continue;
                if (score <= best) continue;
                best = score; chosen = i;
            }
        return chosen;
    }
    public static Vector3 Curve(Vector3 start, Vector3 tangent, Vector3 target, float bend, float u)
    {
        Vector3 handle = start + tangent.normalized * bend * 1.5f;
        return (1 - u) * (1 - u) * start + 2 * (1 - u) * u * handle + u * u * target;
    }
    public static Vector3 CurveTangent(Vector3 start, Vector3 tangent, Vector3 target, float bend, float u)
    {
        Vector3 handle = start + tangent.normalized * bend * 1.5f;
        return 2 * (1 - u) * (handle - start) + 2 * u * (target - handle);
    }
}

public readonly struct KaeyaIcicleDeparture
{
    public readonly Vector2 Origin;
    public readonly float PixelsPerUnit, IceSize;
    public readonly Vector3 Position, Tangent;
    public readonly int Slot;
    public KaeyaIcicleDeparture(Vector2 origin, float units, float size, Vector3 position, Vector3 tangent, int slot)
    { Origin = origin; PixelsPerUnit = units; IceSize = size; Position = position; Tangent = tangent; Slot = slot; }
}
