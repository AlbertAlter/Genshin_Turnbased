using UnityEngine;

public struct FrozenReactionResolution
{
    public string DisplayName;
    public float RemainingAttackAmount;
}

/// <summary>冻结与碎冰：负责水冰消耗、冻结运行时状态及碎冰剧变伤害。</summary>
public static class FrozenReactionHandler
{
    public const int ThawAPCost = 10;

    public static bool IsFrozen(BattleEntity target)
    {
        return target != null
            && ReactionStateSystem.TryGetEntityState(target, ReactionType.Frozen, out _);
    }

    public static float GetFrozenAuraAsCryo(BattleEntity target)
    {
        return ReactionStateSystem.TryGetEntityState(target, ReactionType.Frozen, out var state)
            ? Mathf.Max(0f, state.ReactionElementAmount)
            : 0f;
    }

    public static float ConsumeFrozenAura(BattleEntity target, float amount)
    {
        if (target == null || amount <= 0f
            || !ReactionStateSystem.TryGetEntityState(target, ReactionType.Frozen, out var state))
            return 0f;

        float consumed = Mathf.Min(state.ReactionElementAmount, amount);
        state.ReactionElementAmount = Mathf.Max(0f, state.ReactionElementAmount - consumed);
        state.RemainingRounds = Mathf.CeilToInt(state.ReactionElementAmount);
        if (state.ReactionElementAmount <= 0f)
            ReactionStateSystem.RemoveEntityState(target, ReactionType.Frozen);
        else
            ReactionStateSystem.RefreshDisplay(state);
        return consumed;
    }

    public static bool CanResolveFreeze(ReactionContext context)
    {
        if (context == null || context.Target == null || context.AttackAmount <= 0f) return false;
        if (context.AttackElement == "Hydro")
            return context.Target.GetAura("Cryo")?.AuraAmount > 0f;
        if (context.AttackElement == "Cryo")
            return context.Target.GetAura("Hydro")?.AuraAmount > 0f;
        return false;
    }

    public static bool TryResolveFreeze(
        ReactionContext context,
        out FrozenReactionResolution resolution)
    {
        resolution = default;
        if (!CanResolveFreeze(context)) return false;

        bool wasFrozen = IsFrozen(context.Target);
        float consumed;
        if (context.AttackElement == "Hydro")
        {
            ElementalAura cryo = context.Target.GetAura("Cryo");
            consumed = Mathf.Min(context.AttackAmount, cryo.AuraAmount);
            context.Target.ConsumeAura("Cryo", consumed);
        }
        else
        {
            ElementalAura hydro = context.Target.GetAura("Hydro");
            consumed = Mathf.Min(context.AttackAmount, hydro.AuraAmount);
            context.Target.ConsumeAura("Hydro", consumed);
        }

        resolution.DisplayName = "冻结";
        resolution.RemainingAttackAmount = Mathf.Max(0f, context.AttackAmount - consumed);

        // 已冻结时只进行元素消耗，不覆盖快照、不刷新持续时间。
        if (!wasFrozen && consumed > 0f)
        {
            int duration = Mathf.CeilToInt(consumed);
            ReactionStateSystem.SetEntityState(
                context.Target,
                ReactionType.Frozen,
                duration,
                ReactionSourceSnapshot.Capture(context),
                context.ApplicationPhase,
                true,
                consumed);
        }
        return true;
    }

    public static bool CanShatter(ReactionContext context)
    {
        return context != null
            && context.Target != null
            && context.SourceEntity != null
            && context.PoiseDamage > 0f
            && context.Target.MaxPoise >= 0f
            && IsFrozen(context.Target)
            && context.PoiseDamage >= 100f;
    }

    /// <summary>当前出战的我方角色消耗10 AP主动解除冻结。</summary>
    public static bool TryThaw(BattleEntity target, ActionPointManager actionPoints)
    {
        if (target == null
            || target.Type != BattleEntity.EntityType.Character
            || target.Side != BattleSide.Ally
            || !IsFrozen(target))
            return false;
        if (actionPoints == null || !actionPoints.ConsumeAP(ThawAPCost)) return false;

        ReactionStateSystem.RemoveEntityState(target, ReactionType.Frozen);
        LogManager.Log(LogCategory.Action, $"{target.EntityID} 花费 {ThawAPCost} AP 解除冻结");
        return true;
    }

    public static PendingShatterEffect BuildPendingShatter(
        ReactionContext context,
        float levelCoefficient)
    {
        ReactionBuffTotals bonuses = ReactionDamageCalculator.CollectReactionBonuses(
            context.SourceEntity,
            ReactionType.Shatter,
            "Physical");
        float resistance = ReactionDamageCalculator.GetResistanceMultiplier(
            context.Target,
            "Physical");
        bool sourceIsEnemy = context.SourceKind == ReactionSourceKind.EnemySkill
            || context.SourceEntity.Type == BattleEntity.EntityType.Enemy;
        float damage = sourceIsEnemy
            ? ReactionDamageCalculator.CalculateEnemyTransformative(
                levelCoefficient, 3f, bonuses.DMGBonus, resistance)
            : ReactionDamageCalculator.CalculateCharacterTransformative(
                levelCoefficient,
                3f,
                context.SourceEntity.TotalEM,
                bonuses.DMGBonus,
                bonuses.BaseDMGBonusFlat,
                resistance);

        return new PendingShatterEffect
        {
            Source = context.SourceEntity,
            Target = context.Target,
            SourceEffectID = context.SourceEffectID,
            SourceKind = context.SourceKind,
            SourceSkillID = context.SourceSkillID,
            Damage = damage
        };
    }

}
