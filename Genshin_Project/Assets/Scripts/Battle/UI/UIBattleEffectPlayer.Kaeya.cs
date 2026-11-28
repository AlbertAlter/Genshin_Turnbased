using UnityEngine;

/// <summary>Independent state orbit and single-trigger flight; never creates a gameplay hit.</summary>
public sealed partial class UIBattleEffectPlayer
{
    private readonly float[] kaeyaVacated = { float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity };
    private bool hasKaeyaDeparture;
    private KaeyaIcicleDeparture kaeyaDeparture;
    private void ResetKaeya()
    {
        hasKaeyaDeparture = false;
        for (int i = 0; i < kaeyaVacated.Length; i++) kaeyaVacated[i] = float.NegativeInfinity;
    }
    private Vector3 KaeyaRadii => new Vector3(settings["RadiusX"] * settings["RangeScale"] * settings["WidthScale"],
        settings["TiltHeight"] * settings["RangeScale"] * settings["HeightScale"], settings["DepthRadius"]);
    private float KaeyaSlotOpacity(int slot)
    {
        float age = Elapsed - kaeyaVacated[slot];
        if (age < 0) return 1; // A delayed launch reserves the slot without hiding it early.
        return Mathf.Clamp01((age - settings["SlotReformDelaySeconds"]) / settings["SlotReformSeconds"]);
    }
    public bool TryCaptureKaeyaDeparture(RectTransform actualTarget, out KaeyaIcicleDeparture departure, float launchDelaySeconds = 0)
    {
        departure = default;
        if (!Playing || settings?.Kind != BattleEffectKind.KaeyaGlacialWaltz || actualTarget == null || stoppingTime >= 0) return false;
        Vector2 destination = space.InverseTransformPoint(actualTarget.TransformPoint(actualTarget.rect.center));
        Vector3 local = (destination - range.Center) / range.PixelsPerUnit;
        int count = Mathf.RoundToInt(settings["IcicleCount"]);
        float launchTime = Elapsed + Mathf.Max(0, launchDelaySeconds);
        int slot = KaeyaGlacialWaltzMotion.SelectSlot(launchTime, settings["OrbitSeconds"], count, KaeyaRadii,
            settings["CenterHeight"], local, i => KaeyaSlotOpacity(i) >= .99f);
        if (slot < 0) return false;
        float phase = KaeyaGlacialWaltzMotion.Phase(launchTime, settings["OrbitSeconds"], slot, count);
        departure = new KaeyaIcicleDeparture(range.Center, range.PixelsPerUnit, settings["IcicleSize"],
            KaeyaGlacialWaltzMotion.Orbit(phase, KaeyaRadii, settings["CenterHeight"]), KaeyaGlacialWaltzMotion.Tangent(phase, KaeyaRadii), slot);
        kaeyaVacated[slot] = launchTime;
        Seek(Elapsed);
        return true;
    }
    public void SetKaeyaDeparture(KaeyaIcicleDeparture departure)
    {
        if (settings?.Kind != BattleEffectKind.KaeyaIcicle || !(departure.PixelsPerUnit > 0)) return;
        float waiting = startDelay;
        kaeyaDeparture = departure; hasKaeyaDeparture = true; Seek(Elapsed);
        startDelay = waiting;
        if (waiting > 0) ClearGeometry();
    }
    private void KaeyaDraw(BattleEffectPart part, Vector3 position, Quaternion rotation, Vector3 scale, bool rear,
        Vector2 origin, float units, float opacity = 1, float noise = 0)
    {
        if (opacity <= .0001f || scale.sqrMagnitude <= .00000001f) return;
        Model(part).Draw(rear ? back : front, Matrix4x4.TRS(position, rotation, scale), origin,
            units, tint, fade * opacity, noise, settings["Perspective"]);
    }
    private void RenderKaeyaOrbit()
    {
        int count = Mathf.RoundToInt(settings["IcicleCount"]);
        Vector3 radii = KaeyaRadii;
        for (int slot = 0; slot < count; slot++)
        {
            float phase = KaeyaGlacialWaltzMotion.Phase(Elapsed, settings["OrbitSeconds"], slot, count);
            Vector3 p = KaeyaGlacialWaltzMotion.Orbit(phase, radii, settings["CenterHeight"]);
            Vector3 tangent = KaeyaGlacialWaltzMotion.Tangent(phase, radii);
            float visible = KaeyaSlotOpacity(slot);
            KaeyaDraw(BattleEffectPart.KaeyaIce, p, Quaternion.FromToRotation(Model(BattleEffectPart.KaeyaIce).UpAxis, tangent),
                Vector3.one * settings["IcicleSize"], p.z > 0, range.Center, range.PixelsPerUnit, visible);
            for (int i = 0; i < 6; i++)
            {
                float angle = KaeyaGlacialWaltzMotion.Phase(Elapsed - (2 + i * 1.75f) / 30, settings["OrbitSeconds"], slot, count);
                Vector3 q = KaeyaGlacialWaltzMotion.Orbit(angle, radii, settings["CenterHeight"]);
                float size = .42f + i * .045f;
                KaeyaDraw(BattleEffectPart.KaeyaMist, q, Quaternion.identity, new Vector3(1.1f + i * .09f, .57f + i * .07f, 1) * size,
                    q.z > 0, range.Center, range.PixelsPerUnit, visible * settings["MistOpacity"] * (1 - i * .085f), .65f);
            }
            for (int i = 0; i < 4; i++)
            {
                float angle = KaeyaGlacialWaltzMotion.Phase(Elapsed - (2 + i * 2.5f) / 30, settings["OrbitSeconds"], slot, count);
                Vector3 q = KaeyaGlacialWaltzMotion.Orbit(angle, radii, settings["CenterHeight"]);
                float size = (.035f + i * .007f) * (.65f + .35f * Mathf.Sin(2 * Mathf.PI * Elapsed / 1.2f + i));
                KaeyaDraw(BattleEffectPart.KaeyaGrain, q, Quaternion.Euler(i * 27, i * 41, Elapsed * 30), Vector3.one * size * settings["GrainSize"],
                    q.z > 0, range.Center, range.PixelsPerUnit, visible);
            }
        }
    }
    private bool EnsureKaeyaDeparture()
    {
        if (hasKaeyaDeparture) return true;
        if (caster == null || !BattleEffectRange.TryCalculate(space, new[] { caster }, out var origin, corners, includeInactiveTargets)) return false;
        Vector3 target = (range.Center - origin.Center) / origin.PixelsPerUnit;
        int count = Mathf.RoundToInt(settings["IcicleCount"]);
        int slot = KaeyaGlacialWaltzMotion.SelectSlot(0, settings["OrbitSeconds"], count, KaeyaRadii, settings["CenterHeight"], target);
        if (slot < 0) throw new System.InvalidOperationException("实际目标过近，无法生成沿当前轨道同向弯曲的冰棱弹道。");
        float phase = KaeyaGlacialWaltzMotion.Phase(0, settings["OrbitSeconds"], slot, count);
        kaeyaDeparture = new KaeyaIcicleDeparture(origin.Center, origin.PixelsPerUnit, settings["IcicleSize"],
            KaeyaGlacialWaltzMotion.Orbit(phase, KaeyaRadii, settings["CenterHeight"]), KaeyaGlacialWaltzMotion.Tangent(phase, KaeyaRadii), slot);
        hasKaeyaDeparture = true;
        return true;
    }
    private Vector3 KaeyaFlight(float age, Vector3 target)
    {
        float u = Mathf.Pow(Mathf.Clamp01(age / settings["FlightSeconds"]), 1.6f);
        return KaeyaGlacialWaltzMotion.Curve(kaeyaDeparture.Position, kaeyaDeparture.Tangent, target, settings["ArcBend"], u);
    }
    private void RenderKaeyaIcicle()
    {
        if (!EnsureKaeyaDeparture()) return;
        Vector2 origin = kaeyaDeparture.Origin;
        float units = kaeyaDeparture.PixelsPerUnit;
        Vector3 target = (range.Center - origin) / units;
        float flight = settings["FlightSeconds"];
        if (Elapsed < flight)
        {
            float u = Mathf.Pow(Mathf.Clamp01(Elapsed / flight), 1.6f);
            Vector3 p = KaeyaFlight(Elapsed, target);
            Vector3 direction = KaeyaGlacialWaltzMotion.CurveTangent(kaeyaDeparture.Position, kaeyaDeparture.Tangent, target, settings["ArcBend"], u);
            KaeyaDraw(BattleEffectPart.KaeyaIce, p, Quaternion.FromToRotation(Model(BattleEffectPart.KaeyaIce).UpAxis, direction),
                Vector3.one * kaeyaDeparture.IceSize, p.z > 0, origin, units);
        }
        for (int i = 0; i < 9; i++)
        {
            float age = Elapsed - (.7f + i * .65f) / 30;
            float alpha = Mathf.Clamp01(age * 15) * Mathf.Clamp01((flight + .233333f - Elapsed) / .233333f);
            Vector3 p = KaeyaFlight(age, target);
            KaeyaDraw(BattleEffectPart.KaeyaMist, p, Quaternion.identity, new Vector3(1, .65f, 1) * (.22f + i * .035f),
                p.z > 0, origin, units, alpha * settings["MistOpacity"] * (.95f - i * .062f), .65f);
        }
        for (int i = 0; i < 5; i++)
        {
            if (Elapsed <= i * .5f / 30 || Elapsed >= flight + .1f) continue;
            Vector3 p = KaeyaFlight(Elapsed - (1.2f + i * .75f) / 30, target);
            KaeyaDraw(BattleEffectPart.KaeyaGrain, p, Quaternion.Euler(i * 17, i * 29, Elapsed * 80),
                Vector3.one * (.055f + i * .006f) * settings["GrainSize"], p.z > 0, origin, units);
        }
        float impact = Elapsed - flight;
        if (impact < 0 || impact > settings["ImpactSeconds"]) return;
        var model = Model(BattleEffectPart.KaeyaImpact);
        // Native contact is local frame2; start there when the real timeline confirms contact.
        model.Sample((1 + 25 * Mathf.Clamp01(impact / settings["ImpactSeconds"])) / 26);
        KaeyaDraw(BattleEffectPart.KaeyaImpact, target + Vector3.back * .04f, Quaternion.identity,
            Vector3.one * settings["ImpactSize"], false, origin, units);
    }
}
