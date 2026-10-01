using UnityEngine;

public struct BloomReactionResolution
{
    public string DisplayName;
    public float RemainingAttackAmount;
    public BloomCoreInstance CreatedCore;
    public System.Collections.Generic.List<ReactionDerivedHit> DerivedHits;
}

/// <summary>普通绽放：水草按2:1消耗并在敌方对应位置生成一枚草原核。</summary>
public static class BloomReactionHandler
{
    public static bool CanResolve(ReactionContext context)
        => GetOpposingAura(context) != null;

    public static bool TryResolve(
        ReactionContext context,
        float levelCoefficient,
        out BloomReactionResolution resolution)
    {
        resolution = default;
        ElementalAura aura = GetOpposingAura(context);
        if (aura == null) return false;

        float reactionUnits;
        float attackConsumed;
        float auraConsumed;
        if (context.AttackElement == "Hydro")
        {
            reactionUnits = Mathf.Min(context.AttackAmount / 2f, aura.AuraAmount);
            attackConsumed = reactionUnits * 2f;
            auraConsumed = reactionUnits;
        }
        else
        {
            reactionUnits = Mathf.Min(context.AttackAmount, aura.AuraAmount / 2f);
            attackConsumed = reactionUnits;
            auraConsumed = reactionUnits * 2f;
        }
        if (reactionUnits <= 0f) return false;

        context.Target.ConsumeAura(aura.Element, auraConsumed);
        resolution.DisplayName = "绽放";
        resolution.RemainingAttackAmount = Mathf.Max(0f, context.AttackAmount - attackConsumed);
        resolution.CreatedCore = BloomCoreSystem.CreateCore(
            context,
            levelCoefficient,
            out System.Collections.Generic.List<ReactionDerivedHit> overflowHits);
        resolution.DerivedHits = overflowHits;
        return true;
    }

    private static ElementalAura GetOpposingAura(ReactionContext context)
    {
        if (context == null || context.Target == null || context.AttackAmount <= 0f)
            return null;

        string auraElement;
        if (context.AttackElement == "Hydro") auraElement = "Dendro";
        else if (context.AttackElement == "Dendro") auraElement = "Hydro";
        else return null;
        ElementalAura aura = context.Target.GetAura(auraElement);
        return context.AllowsReactionPool(auraElement, ReactionPoolKind.NormalAura)
            && aura != null && aura.AuraAmount > 0f ? aura : null;
    }
}
