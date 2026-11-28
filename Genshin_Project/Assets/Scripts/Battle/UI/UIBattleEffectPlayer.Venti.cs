using UnityEngine;

public sealed partial class UIBattleEffectPlayer
{
    private float ventiPhase;
    private Color? ventiAbsorbedColor;
    public void SetVentiPhase(float seconds) { ventiPhase = seconds; }
    public void SetVentiAbsorbedColor(Color? color)
    {
        if (ventiAbsorbedColor.Equals(color)) return;
        ventiAbsorbedColor = color;
        if (Playing && startDelay <= 0 && (settings.Kind == BattleEffectKind.VentiStormEye || settings.Kind == BattleEffectKind.VentiStormPulse)) Seek(Elapsed);
    }
    private Vector2 VentiCenter(RectTransform card) => space.InverseTransformPoint(card.TransformPoint(card.rect.center));
    private Vector2 VentiSize(RectTransform card) => new Vector2(
        space.InverseTransformVector(card.TransformVector(Vector3.right * card.rect.width)).magnitude,
        space.InverseTransformVector(card.TransformVector(Vector3.up * card.rect.height)).magnitude) / range.PixelsPerUnit;
    private void DrawVenti(BattleEffectPart part, Vector2 origin, Vector3 position, Quaternion rotation, Vector3 scale, bool rear, float alpha, float noise = .05f, bool fixedAxes = true, float elementShare = 0, bool splitAtCardPlane = false)
    {
        if (fade * alpha < .0001f || Mathf.Min(scale.x, Mathf.Min(scale.y, scale.z)) <= .00001f) return;
        // Flow turns inside a fixed footprint; the ellipse never rotates away from the real target bounds.
        var matrix = fixedAxes ? Matrix4x4.Translate(position) * Matrix4x4.Scale(scale) * Matrix4x4.Rotate(rotation)
            : Matrix4x4.TRS(position, rotation, scale);
        float perspective = settings["Perspective"] / .12f / (settings["CameraDistanceUnits"] * .035f);
        if (splitAtCardPlane)
        {
            Model(part).Draw(back, matrix, origin, range.PixelsPerUnit, tint, fade * alpha, noise, perspective, ventiAbsorbedColor, elementShare, depthSide: 1);
            Model(part).Draw(front, matrix, origin, range.PixelsPerUnit, tint, fade * alpha, noise, perspective, ventiAbsorbedColor, elementShare, depthSide: -1);
        }
        else Model(part).Draw(rear ? back : front, matrix, origin, range.PixelsPerUnit, tint, fade * alpha, noise, perspective, ventiAbsorbedColor, elementShare);
    }
    private void VentiRibbons(Vector2 origin, Vector2 radii, float phase, float alpha)
    {
        int count = Mathf.RoundToInt(settings["RibbonCount"]);
        float cycles = phase / settings["OrbitSeconds"];
        for (int i = 0; i < count; i++)
        {
            float angle = VentiWindMotion.Orbit(cycles, i, settings["OrbitSpeedVariation"]) * 360 + i * 360f / count;
            float depth = Mathf.Sin(angle * Mathf.Deg2Rad);
            var rotation = Quaternion.Euler((i % 3 - 1) * 24, Mathf.Sin(i * 2.4f) * 38, angle);
            float radius = (.85f + .15f * Mathf.Sin(i * 2.39996f)) * VentiWindMotion.Breath(cycles, i, settings["BreathAmount"]);
            DrawVenti(BattleEffectPart.VentiArc, origin, new Vector3(0, 0, depth * settings["DepthRatio"]), rotation,
                new Vector3(radii.x, radii.y, Mathf.Min(radii.x, radii.y)) * radius,
                depth > 0, alpha * (depth > 0 ? .52f : .88f), .08f, elementShare: VentiWindMotion.ConversionShare(i, count));
        }
    }
    private void RenderVentiEye()
    {
        var host = targets[0]; var size = VentiSize(host);
        var radii = new Vector2(size.x * settings["EyeWidthRatio"] * settings["WidthScale"],
            size.y * settings["EyeHeightRatio"] * settings["HeightScale"]) * settings["RangeScale"];
        VentiRibbons(VentiCenter(host), radii, Elapsed, 1);
        VentiParticles(VentiCenter(host), radii, .8f, 1, true);
        VentiAirIntake(VentiCenter(host), radii);
    }
    private Vector3 VentiIntakePoint(float age, int index, Vector2 radii)
    {
        float radius = .045f + .735f * (1 - age * age);
        float angle = index * 2.39996323f + age * settings["IntakeTurns"] * Mathf.PI * 2;
        return new Vector3(Mathf.Cos(angle) * radii.x * radius, Mathf.Sin(angle) * radii.y * radius,
            -.15f - .35f * age + Mathf.Sin(angle) * settings["DepthRatio"] * .2f * (1 - age));
    }
    private void VentiAirIntake(Vector2 origin, Vector2 radii)
    {
        radii *= VentiWindMotion.Breath(Elapsed / settings["OrbitSeconds"], -1, settings["BreathAmount"] * .6f);
        int count = Mathf.RoundToInt(settings["IntakeCount"]);
        float size = settings["IntakeSize"] * Mathf.Min(radii.x, radii.y);
        for (int i = 0; i < count; i++)
        {
            float age = Mathf.Repeat(Elapsed / settings["IntakeSeconds"] + (float)i / count, 1);
            for (int trail = 0; trail < 2; trail++)
            {
                float t = age - trail * .055f; if (t <= 0) continue;
                var p = VentiIntakePoint(t, i, radii);
                var direction = VentiIntakePoint(Mathf.Min(1, t + .02f), i, radii) - p; direction.z = 0;
                float alpha = Mathf.Pow(Mathf.Sin(t * Mathf.PI), .7f) * settings["IntakeOpacity"] * (trail == 0 ? 1 : .3f);
                DrawVenti(BattleEffectPart.VentiFeather, origin, p, Quaternion.FromToRotation(Vector3.up, direction),
                    new Vector3(.26f, 1 - .65f * t, .55f) * size, false, alpha, 0, false, VentiWindMotion.ConversionShare(i, count));
            }
        }
    }
    private void RenderVentiPulse()
    {
        if (caster == null) return;
        var origin = VentiCenter(caster); var size = VentiSize(caster);
        float reachX = size.x, reachY = size.y * .6f;
        foreach (var target in targets)
        {
            var distance = (VentiCenter(target) - origin) / range.PixelsPerUnit; var targetSize = VentiSize(target);
            reachX = Mathf.Max(reachX, Mathf.Abs(distance.x) + targetSize.x * .6f);
            reachY = Mathf.Max(reachY, Mathf.Abs(distance.y) + targetSize.y * .6f);
        }
        float contact = settings["ContactSeconds"], t = Mathf.Clamp01(Elapsed / Duration);
        float growth = Mathf.Clamp01(Elapsed / contact);
        var initial = new Vector2(size.x * settings["EyeWidthRatio"], size.y * settings["EyeHeightRatio"]);
        var radii = Vector2.Lerp(initial, new Vector2(reachX, reachY), Mathf.SmoothStep(0, 1, growth));
        radii *= 1 + Mathf.Max(0, Elapsed - contact) / Mathf.Max(.01f, Duration - contact) * .28f;
        radii = Vector2.Scale(radii, new Vector2(settings["WidthScale"], settings["HeightScale"])) * settings["RangeScale"];
        float alpha = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.15f, 1, t));
        VentiRibbons(origin, radii, ventiPhase + Elapsed, alpha);
        VentiParticles(origin, radii, 1.8f, alpha, false);
    }
    private void RenderVentiUpdraft() => RenderVentiField(false);
    private void RenderVentiWindDomain() => RenderVentiField(true);
    private void RenderVentiField(bool sustained)
    {
        // The battle background is the ground plane; one shared field flows along -Z toward the viewer.
        var radii = range.HalfSize / range.PixelsPerUnit * 1.12f + Vector2.one * .08f;
        radii = Vector2.Scale(radii, new Vector2(settings["WidthScale"], settings["HeightScale"])) * settings["RangeScale"];
        float height = 0; foreach (var target in targets) height += VentiSize(target).y;
        height /= targets.Count;
        float reach = height * settings[sustained ? "DomainHeightRatio" : "UpdraftHeightRatio"];
        float onset = sustained ? 1 : Mathf.Clamp01(Elapsed / settings["ContactSeconds"]);
        float rise = sustained ? 1 : 1 - (1 - onset) * (1 - onset) * (1 - onset);
        float opacity = sustained ? 1 : 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.45f, 1, Elapsed / Duration));
        float groundScale = sustained ? 1 : Mathf.Lerp(.72f, 1, rise);
        DrawVenti(BattleEffectPart.VentiSigil, range.Center, new Vector3(0, 0, .14f), Quaternion.identity,
            new Vector3(radii.x * groundScale, radii.y * groundScale, 1), true, opacity * settings["SigilOpacity"], 0);
        int count = Mathf.RoundToInt(settings["RibbonCount"]);
        for (int i = 0; i < count; i++)
        {
            float age = Mathf.Repeat(Elapsed / settings["FlowSeconds"] + (float)i / count, 1);
            float phase = i * 360f / count + Mathf.Sin(i * 2.4f) * 18 + (sustained ? Elapsed * 360 / settings["OrbitSeconds"] + age * 45 : Elapsed * 55);
            float envelope = sustained ? Mathf.Sin(age * Mathf.PI) : 1;
            DrawVenti(BattleEffectPart.VentiUprush, range.Center, new Vector3(0, 0, .10f - (sustained ? age * reach * .2f : 0)), Quaternion.Euler(0, 0, phase),
                new Vector3(radii.x, radii.y, Mathf.Max(.001f, reach * (sustained ? .35f + .2f * age : rise))),
                false, opacity * envelope * settings["CurlOpacity"], .04f, splitAtCardPlane: true);
        }
        int lines = Mathf.RoundToInt(settings["DetailCount"]);
        for (int i = 0; i < lines; i++)
        {
            float age = Mathf.Repeat(Elapsed / settings["FlowSeconds"] + Hash(i, 13), 1);
            float angle = Hash(i, 12) * Mathf.PI * 2, radius = Mathf.Sqrt((i + .5f) / lines) * .94f;
            float sway = .025f * Mathf.Sin(age * Mathf.PI * 2 + i * 2.39996f);
            float head = Mathf.Min(1, age + settings["LiftLineLengthRatio"] * .25f);
            float tail = Mathf.Max(0, head - settings["LiftLineLengthRatio"]);
            var point = new Vector3(Mathf.Cos(angle) * radii.x * (radius + sway), Mathf.Sin(angle) * radii.y * (radius + sway), .12f - tail * reach);
            var direction = new Vector3(-Mathf.Cos(angle) * .12f, -Mathf.Sin(angle) * .12f, 1);
            DrawVenti(BattleEffectPart.VentiLiftLine, range.Center, point, Quaternion.FromToRotation(Vector3.forward, direction),
                new Vector3(settings["ParticleSize"], settings["ParticleSize"], Mathf.Max(.001f, (head - tail) * reach * rise)),
                false, opacity * rise * Mathf.Pow(Mathf.Sin(age * Mathf.PI), .65f) * .8f, 0, false, splitAtCardPlane: true);
        }
    }
    private void VentiParticles(Vector2 origin, Vector2 radii, float reach, float opacity, bool orbit)
    {
        int count = Mathf.RoundToInt(settings["DetailCount"]);
        for (int i = 0; i < count; i++)
        {
            float age = Mathf.Repeat(Elapsed / settings["FlowSeconds"] + i * .618034f, 1);
            float cycles = orbit ? Elapsed / settings["OrbitSeconds"] : 0;
            float angle = (Hash(i, 2) + (orbit ? VentiWindMotion.Orbit(cycles, i + 31, settings["OrbitSpeedVariation"]) : age * .25f)) * Mathf.PI * 2;
            float radius = (.2f + Hash(i, 3) * .65f) * (orbit ? VentiWindMotion.Breath(cycles, i + 31, settings["BreathAmount"]) : 1);
            var p = new Vector3(Mathf.Cos(angle) * radii.x * radius, Mathf.Sin(angle) * radii.y * radius,
                orbit ? Mathf.Sin(angle) * reach : -.1f - age * reach);
            float alpha = Mathf.Pow(Mathf.Sin(age * Mathf.PI), .65f) * opacity;
            float size = settings["ParticleSize"] * (.7f + age * .6f);
            DrawVenti(BattleEffectPart.VentiFeather, origin, p, Quaternion.Euler(0, 0, angle * Mathf.Rad2Deg),
                new Vector3(.16f, 1.6f, .5f) * size, p.z > 0, alpha * .7f, 0, false,
                settings.Kind == BattleEffectKind.VentiStormEye || settings.Kind == BattleEffectKind.VentiStormPulse ? VentiWindMotion.ConversionShare(i, count) : 0);
        }
    }
}

/// <summary>Visual-only, smooth irregular motion; absolute sampling keeps pause/seek and eye-to-pulse phase coherent.</summary>
internal static class VentiWindMotion
{
    // Interleaved strands keep both hues all around the eye. An odd final strand shares both colours equally.
    public static float ConversionShare(int index, int count) => count % 2 != 0 && index == count - 1 ? .5f : index % 2;
    private const double Tau = System.Math.PI * 2;
    private static double Phase(int strand, int channel)
    {
        uint value = unchecked((uint)(strand + 73) * 747796405u + (uint)channel * 2891336453u);
        value = unchecked((value ^ (value >> 16)) * 2246822519u); value ^= value >> 13;
        return (value & 0xFFFFFFu) / 16777216.0 * Tau;
    }
    public static float Orbit(float cycles, int strand, float variation)
    {
        // Integral of three bounded, independently phased speed waves: d(orbit)/d(cycles) stays within 1 +/- variation.
        double offset = 0;
        for (int harmonic = 1; harmonic <= 3; harmonic++)
        {
            double phase = Phase(strand, harmonic), weight = harmonic == 1 ? .5 : harmonic == 2 ? .32 : .18;
            offset += weight * (System.Math.Sin(cycles * Tau * harmonic + phase) - System.Math.Sin(phase)) / (Tau * harmonic);
        }
        return (float)(cycles + variation * offset);
    }
    public static float Breath(float cycles, int strand, float amount)
        => (float)(1 + amount * (.65 * System.Math.Sin(cycles * System.Math.PI)
            + .35 * System.Math.Sin(cycles * System.Math.PI * 3 + Phase(strand, 4))));
}
