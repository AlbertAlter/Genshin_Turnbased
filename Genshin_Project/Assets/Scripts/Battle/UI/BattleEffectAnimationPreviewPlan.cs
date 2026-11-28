using System;
using System.Collections.Generic;

/// <summary>Read-only authoring schedule from the attack table. Marker samples never execute skills or create business hits.</summary>
public sealed class BattleEffectAnimationPreviewPlan
{
    public readonly BattleEffectKind SelectedKind, ActionKind;
    public readonly BattleEffectKind? StateKind;
    public readonly BattleAttackAnimationSpec Attack;
    public readonly string Element;
    public readonly float[] Hits, Launches;
    public bool HasDamage => Hits.Length > 0;
    public bool FlipCards => ActionKind == BattleEffectKind.VentiUpdraft;
    public static BattleEffectKind? Companion(BattleEffectKind kind)
    {
        switch (kind)
        {
            case BattleEffectKind.AmberArrowRain: return BattleEffectKind.AmberBurningArea;
            case BattleEffectKind.AmberBurningArea: return BattleEffectKind.AmberArrowRain;
            case BattleEffectKind.KaeyaIcicle: return BattleEffectKind.KaeyaGlacialWaltz;
            case BattleEffectKind.KaeyaGlacialWaltz: return BattleEffectKind.KaeyaIcicle;
            case BattleEffectKind.VentiStormPulse: return BattleEffectKind.VentiStormEye;
            case BattleEffectKind.VentiStormEye: return BattleEffectKind.VentiStormPulse;
            default: return null;
        }
    }
    public static bool TargetsCaster(BattleEffectKind kind)
        => kind == BattleEffectKind.KaeyaGlacialWaltz || kind == BattleEffectKind.VentiStormEye;
    public static bool Sliced(BattleEffectKind kind)
        => kind == BattleEffectKind.AmberArrowRain || kind == BattleEffectKind.LisaVioletArc || kind == BattleEffectKind.KaeyaIcicle
            || kind == BattleEffectKind.VentiStormPulse || kind == BattleEffectKind.VentiUpdraft;

    public BattleEffectAnimationPreviewPlan(BattleEffectKind kind, BattleAttackAnimationTable table,
        Func<BattleEffectKind, BattleEffectParameters> settings)
    {
        if (table == null || settings == null) throw new ArgumentNullException();
        SelectedKind = kind;
        var companion = Companion(kind);
        bool state = BattleEffectParameters.IsSustained(kind);
        StateKind = state ? kind : companion;
        ActionKind = state && companion.HasValue ? companion.Value : kind;
        bool damage = !BattleEffectParameters.IsSustained(ActionKind);
        string preferred = kind == BattleEffectKind.VentiStormEye ? "SE_Burst_Venti2"
            : ActionKind == BattleEffectKind.VentiStormPulse ? "STE_Venti_Burst1" : null;
        var spec = preferred != null ? table.Find(preferred) : null;
        if (spec != null && spec.AnimationHook != BattleNativeEffectHooks.Name(ActionKind)) spec = null;
        if (spec == null)
            foreach (var candidate in table.Effects)
                if (candidate.AnimationHook == BattleNativeEffectHooks.Name(ActionKind)) { spec = candidate; break; }
        float contact = BattleEffectTiming.Contact(settings(ActionKind));
        Attack = spec?.WithNativeEffect(ActionKind.ToString(), contact);
        Element = ActionKind == BattleEffectKind.LisaVioletArc ? "Electro"
            : ActionKind == BattleEffectKind.KaeyaIcicle || ActionKind == BattleEffectKind.KaeyaGlacialWaltz ? "Cryo"
            : BattleEffectParameters.IsVenti(ActionKind) || ActionKind == BattleEffectKind.DandelionWindOrbit ? "Anemo" : "Pyro";
        int count = damage ? spec?.Timeline?.MarkerCount ?? 1 : 0;
        Hits = new float[count]; Launches = new float[count];
        for (int i = 0; i < count; i++)
        {
            Hits[i] = spec?.Timeline != null ? (float)spec.Timeline.HitTime(i) : Math.Max(contact, spec?.Windup ?? 0);
            float lead = spec?.Timeline != null ? (float)spec.Timeline.ProjectileSeconds : contact;
            Launches[i] = Math.Max(0, Hits[i] - lead);
        }
    }
}
