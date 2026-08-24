using UnityEngine;

public struct CrystallizeReactionResolution
{
    public string DisplayName;
    public string ReactedElement;
    public float RemainingAttackAmount;
    public float ShieldValue;
    public bool GrantsPartyShield;
}

/// <summary>Ordinary Geo crystallize and enemy Geo neutralization.</summary>
public static class CrystallizeReactionHandler
{
    private const float Epsilon = 0.0001f;

    public static bool CanResolve(ReactionContext context)
    {
        return TrySelectReactedElement(context, out _, out _);
    }

    public static bool TryResolve(
        ReactionContext context,
        out CrystallizeReactionResolution resolution)
    {
        resolution = default;
        if (!TrySelectReactedElement(context, out string reactedElement, out float availableAmount))
            return false;

        float otherConsumed = Mathf.Min(availableAmount, context.AttackAmount / 2f);
        if (otherConsumed <= Epsilon) return false;

        ConsumeReactedElement(context, reactedElement, otherConsumed);

        ReactionSourceSnapshot snapshot = ReactionSourceSnapshot.Capture(context);
        bool sourceIsEnemy = IsEnemySource(context, snapshot);
        bool sourceIsConfirmedAlly = !sourceIsEnemy && IsConfirmedAllySource(context, snapshot);
        float shieldValue = 0f;
        if (sourceIsConfirmedAlly)
        {
            int sourceLevel = snapshot != null && snapshot.Level > 0
                ? snapshot.Level
                : (context.SourceEntity != null ? context.SourceEntity.Level : 1);
            float levelCoefficient = snapshot != null && snapshot.LevelCoefficient > 0f
                ? snapshot.LevelCoefficient
                : ReactionDamageCalculator.GetLevelCoefficient(sourceLevel);
            float totalEM = snapshot != null ? Mathf.Max(0f, snapshot.TotalEM) : 0f;
            shieldValue = Mathf.Max(0f,
                ReactionDamageCalculator.CalculateCharacterCrystalShield(levelCoefficient, totalEM));

            BattleManager battle = BattleManager.Instance;
            if (battle == null)
            {
                LogManager.LogWarning(
                    LogCategory.Reaction,
                    "结晶反应已完成，但当前没有 BattleManager，无法为队伍施加结晶盾。");
            }
            else
            {
                CrystallizeShieldSystem.ApplyPartyShield(
                    battle,
                    reactedElement,
                    shieldValue,
                    context.ApplicationPhase);
            }
        }

        resolution = new CrystallizeReactionResolution
        {
            DisplayName = sourceIsConfirmedAlly ? "结晶" : null,
            ReactedElement = reactedElement,
            RemainingAttackAmount = 0f,
            ShieldValue = shieldValue,
            GrantsPartyShield = sourceIsConfirmedAlly
        };
        return true;
    }

    private static bool TrySelectReactedElement(
        ReactionContext context,
        out string reactedElement,
        out float availableAmount)
    {
        reactedElement = null;
        availableAmount = 0f;
        if (context == null
            || !context.CanTriggerReaction
            || context.Target == null
            || !context.Target.IsAlive
            || context.AttackElement != "Geo"
            || context.AttackAmount <= Epsilon)
            return false;

        float pyro = BurningReactionHandler.GetPyroAmountIncludingBurning(context.Target);
        if (pyro > Epsilon)
        {
            reactedElement = "Pyro";
            availableAmount = pyro;
            return true;
        }

        float hydro = context.Target.GetAura("Hydro")?.AuraAmount ?? 0f;
        if (hydro > Epsilon)
        {
            reactedElement = "Hydro";
            availableAmount = hydro;
            return true;
        }

        float electro = context.Target.GetAura("Electro")?.AuraAmount ?? 0f;
        if (electro > Epsilon)
        {
            reactedElement = "Electro";
            availableAmount = electro;
            return true;
        }

        float normalCryo = context.Target.GetAura("Cryo")?.AuraAmount ?? 0f;
        float frozenCryo = context.IgnoreFrozenAura
            ? 0f
            : FrozenReactionHandler.GetFrozenAuraAsCryo(context.Target);
        float cryo = Mathf.Max(0f, normalCryo) + Mathf.Max(0f, frozenCryo);
        if (cryo <= Epsilon) return false;

        reactedElement = "Cryo";
        availableAmount = cryo;
        return true;
    }

    private static void ConsumeReactedElement(
        ReactionContext context,
        string reactedElement,
        float amount)
    {
        if (reactedElement == "Pyro")
        {
            BurningReactionHandler.ConsumePyroIncludingBurning(context.Target, amount);
            return;
        }

        if (reactedElement == "Hydro" || reactedElement == "Electro")
        {
            context.Target.ConsumeAura(reactedElement, amount);
            return;
        }

        float remaining = amount;
        ElementalAura normalCryo = context.Target.GetAura("Cryo");
        if (normalCryo != null)
        {
            float normalConsumed = Mathf.Min(normalCryo.AuraAmount, remaining);
            context.Target.ConsumeAura("Cryo", normalConsumed);
            remaining -= normalConsumed;
        }
        if (remaining > Epsilon && !context.IgnoreFrozenAura)
            FrozenReactionHandler.ConsumeFrozenAura(context.Target, remaining);
    }

    private static bool IsEnemySource(ReactionContext context, ReactionSourceSnapshot snapshot)
    {
        return context.SourceKind == ReactionSourceKind.EnemySkill
            || (context.SourceEntity != null
                && (context.SourceEntity.Type == BattleEntity.EntityType.Enemy
                    || context.SourceEntity.Side == BattleSide.Enemy))
            || (snapshot != null
                && (snapshot.SourceKind == ReactionSourceKind.EnemySkill
                    || snapshot.SourceSide == BattleSide.Enemy
                    || (snapshot.SourceEntity != null
                        && snapshot.SourceEntity.Type == BattleEntity.EntityType.Enemy)));
    }

    private static bool IsConfirmedAllySource(
        ReactionContext context,
        ReactionSourceSnapshot snapshot)
    {
        if (context.SourceEntity != null)
            return context.SourceEntity.Type == BattleEntity.EntityType.Character
                && context.SourceEntity.Side == BattleSide.Ally;

        return context.SourceSnapshotOverride != null
            && snapshot != null
            && snapshot.SourceSide == BattleSide.Ally
            && (snapshot.SourceEntity != null || snapshot.SourceEntityID != 0);
    }
}
