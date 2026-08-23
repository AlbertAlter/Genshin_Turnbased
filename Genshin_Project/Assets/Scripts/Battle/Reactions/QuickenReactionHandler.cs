using UnityEngine;

public struct QuickenReactionResolution
{
    public string DisplayName;
    public float RemainingAttackAmount;
    public ReactionStateInstance State;
}

/// <summary>草雷按 1:1 触发原激化；重复触发增加一回合，最多三回合。</summary>
public static class QuickenReactionHandler
{
    private const int InitialDuration = 1;
    private const int MaxDuration = 3;

    public static bool CanResolve(ReactionContext context)
        => GetOpposingAura(context) != null;

    public static bool TryResolve(
        ReactionContext context,
        out QuickenReactionResolution resolution)
    {
        resolution = default;
        ElementalAura aura = GetOpposingAura(context);
        if (aura == null) return false;

        float consumed = Mathf.Min(context.AttackAmount, aura.AuraAmount);
        if (consumed <= 0f) return false;
        context.Target.ConsumeAura(aura.Element, consumed);

        int duration = InitialDuration;
        if (ReactionStateSystem.TryGetEntityState(
                context.Target,
                ReactionType.Quicken,
                out ReactionStateInstance existing))
        {
            duration = Mathf.Min(MaxDuration, existing.RemainingRounds + 1);
        }

        ReactionSourceSnapshot snapshot = ReactionSourceSnapshot.Capture(context);
        ReactionStateInstance state = ReactionStateSystem.SetEntityState(
            context.Target,
            ReactionType.Quicken,
            duration,
            snapshot,
            context.ApplicationPhase);

        resolution = new QuickenReactionResolution
        {
            DisplayName = "原激化",
            RemainingAttackAmount = Mathf.Max(0f, context.AttackAmount - consumed),
            State = state
        };
        return true;
    }

    private static ElementalAura GetOpposingAura(ReactionContext context)
    {
        if (context == null || context.Target == null || context.AttackAmount <= 0f)
            return null;

        string auraElement;
        if (context.AttackElement == "Electro") auraElement = "Dendro";
        else if (context.AttackElement == "Dendro") auraElement = "Electro";
        else return null;

        ElementalAura aura = context.Target.GetAura(auraElement);
        return aura != null && aura.AuraAmount > 0f ? aura : null;
    }
}
