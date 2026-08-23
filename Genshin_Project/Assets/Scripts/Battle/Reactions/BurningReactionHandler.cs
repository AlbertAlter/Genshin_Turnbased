using System.Collections.Generic;
using UnityEngine;

public struct BurningReactionResolution
{
    public string DisplayName;
    public float RemainingAttackAmount;
}

/// <summary>燃烧：保存精确燃元素量，在双方回合结束造成火伤并施加1单位火附着。</summary>
public static class BurningReactionHandler
{
    private const float ReactionMultiplier = 2f;
    private static bool _isTickingTurnEnd;

    public static bool IsBurning(BattleEntity target)
        => target != null
            && ReactionStateSystem.TryGetEntityState(target, ReactionType.Burning, out _);

    public static float GetBurningAuraAsPyro(BattleEntity target)
    {
        return ReactionStateSystem.TryGetEntityState(target, ReactionType.Burning, out var state)
            ? Mathf.Max(0f, state.ReactionElementAmount)
            : 0f;
    }

    public static float ConsumeBurningAura(BattleEntity target, float amount)
    {
        if (target == null || amount <= 0f
            || !ReactionStateSystem.TryGetEntityState(target, ReactionType.Burning, out var state))
            return 0f;

        float consumed = Mathf.Min(state.ReactionElementAmount, amount);
        state.ReactionElementAmount = Mathf.Max(0f, state.ReactionElementAmount - consumed);
        state.RemainingRounds = Mathf.CeilToInt(state.ReactionElementAmount);
        if (state.ReactionElementAmount <= 0f)
            ReactionStateSystem.RemoveEntityState(target, ReactionType.Burning);
        else
            ReactionStateSystem.RefreshDisplay(state);
        return consumed;
    }

    public static float GetPyroAmountIncludingBurning(BattleEntity target)
    {
        if (target == null) return 0f;
        return (target.GetAura("Pyro")?.AuraAmount ?? 0f) + GetBurningAuraAsPyro(target);
    }

    /// <summary>先消耗普通过量火，再消耗燃元素。</summary>
    public static float ConsumePyroIncludingBurning(BattleEntity target, float amount)
    {
        if (target == null || amount <= 0f) return 0f;

        float remaining = amount;
        float consumedTotal = 0f;
        ElementalAura pyro = target.GetAura("Pyro");
        if (pyro != null)
        {
            float consumed = Mathf.Min(pyro.AuraAmount, remaining);
            target.ConsumeAura("Pyro", consumed);
            consumedTotal += consumed;
            remaining -= consumed;
        }
        if (remaining > 0f)
            consumedTotal += ConsumeBurningAura(target, remaining);
        return consumedTotal;
    }

    public static bool CanResolve(ReactionContext context)
        => GetOpposingNormalAura(context) != null;

    public static bool TryResolve(
        ReactionContext context,
        float levelCoefficient,
        out BurningReactionResolution resolution)
    {
        resolution = default;
        ElementalAura aura = GetOpposingNormalAura(context);
        if (aura == null) return false;

        float consumed = Mathf.Min(context.AttackAmount, aura.AuraAmount);
        context.Target.ConsumeAura(aura.Element, consumed);
        float totalBurningAmount = consumed;
        if (ReactionStateSystem.TryGetEntityState(
                context.Target,
                ReactionType.Burning,
                out ReactionStateInstance existing))
            totalBurningAmount += existing.ReactionElementAmount;

        ReactionSourceSnapshot snapshot = ReactionSourceSnapshot.Capture(
            context,
            ReactionType.Burning,
            "Pyro");
        snapshot.LevelCoefficient = levelCoefficient;
        ReactionStateSystem.SetEntityState(
            context.Target,
            ReactionType.Burning,
            Mathf.CeilToInt(totalBurningAmount),
            snapshot,
            context.ApplicationPhase,
            true,
            totalBurningAmount,
            _isTickingTurnEnd);

        resolution.DisplayName = "燃烧";
        resolution.RemainingAttackAmount = Mathf.Max(0f, context.AttackAmount - consumed);
        return true;
    }

    public static void TickTurnEnd(TurnPhase phase)
    {
        if (phase != TurnPhase.AllyPostTurn && phase != TurnPhase.EnemyPostTurn)
            return;

        var states = new List<ReactionStateInstance>();
        foreach (ReactionStateInstance state in ReactionStateSystem.ActiveStates)
        {
            if (state != null && state.Type == ReactionType.Burning)
                states.Add(state);
        }
        states.Sort((left, right) => left.ApplyOrder.CompareTo(right.ApplyOrder));

        _isTickingTurnEnd = true;
        try
        {
            foreach (ReactionStateInstance state in states)
            {
                if (!ReactionStateSystem.TryGetEntityState(
                        state.Target,
                        ReactionType.Burning,
                        out ReactionStateInstance current)
                    || !object.ReferenceEquals(current, state))
                    continue;
                ExecuteTick(state, phase);
            }
        }
        finally
        {
            _isTickingTurnEnd = false;
        }
    }

    private static void ExecuteTick(ReactionStateInstance state, TurnPhase phase)
    {
        BattleEntity target = state.Target;
        if (target == null || !target.IsAlive)
        {
            ReactionStateSystem.RemoveEntityState(target, ReactionType.Burning);
            return;
        }

        ReactionSourceSnapshot snapshot = state.SourceSnapshot;
        float levelCoefficient = snapshot != null && snapshot.LevelCoefficient > 0f
            ? snapshot.LevelCoefficient
            : ReactionDamageCalculator.GetLevelCoefficient(snapshot != null ? snapshot.Level : 1);
        float resistance = ReactionDamageCalculator.GetResistanceMultiplier(target, "Pyro");
        bool sourceIsEnemy = snapshot != null
            && (snapshot.SourceKind == ReactionSourceKind.EnemySkill
                || (snapshot.SourceEntity != null
                    && snapshot.SourceEntity.Type == BattleEntity.EntityType.Enemy));
        float damage = sourceIsEnemy
            ? ReactionDamageCalculator.CalculateEnemyTransformative(
                levelCoefficient,
                ReactionMultiplier,
                snapshot != null ? snapshot.DMGBonus : 0f,
                resistance)
            : ReactionDamageCalculator.CalculateCharacterTransformative(
                levelCoefficient,
                ReactionMultiplier,
                snapshot != null ? snapshot.TotalEM : 0f,
                snapshot != null ? snapshot.DMGBonus : 0f,
                snapshot != null ? snapshot.BaseDMGBonusFlat : 0f,
                resistance);

        ReactionResult result = ReactionResolver.Resolve(new ReactionContext
        {
            SourceEntity = snapshot != null ? snapshot.SourceEntity : null,
            SourceKind = snapshot != null ? snapshot.SourceKind : ReactionSourceKind.DerivedReaction,
            SourceSkillID = snapshot != null ? snapshot.SourceSkillID : string.Empty,
            SourceEffectID = snapshot != null ? snapshot.SourceEffectID : string.Empty,
            SourceSnapshotOverride = snapshot,
            Target = target,
            EffectExecutionID = ReactionResolver.BeginEffectExecution(),
            ApplicationPhase = (int)phase,
            AttackElement = "Pyro",
            AttackAmount = 1f,
            PreReactionDamage = damage,
            DamageType = "Reaction",
            PoiseDamage = 0f,
            SkipImmediateAuraDecay = true
        });

        float finalDamage = target.AbsorbDamageWithShield(result.FinalDamage, "Pyro");
        // 带来源扣血（2026-08-19）：燃烧持续伤害使用创建燃烧状态时保存的来源快照
        target.TakeDamage(finalDamage, DamageSourceInfo.FromReactionSnapshot(snapshot, ReactionType.Burning));
        ReactionEffectExecutor.FinalizePrimaryHit(result, finalDamage);
        ReactionEffectExecutor.ExecuteDerivedHits(result);
    }

    private static ElementalAura GetOpposingNormalAura(ReactionContext context)
    {
        if (context == null || context.Target == null || context.AttackAmount <= 0f)
            return null;

        string auraElement;
        if (context.AttackElement == "Pyro") auraElement = "Dendro";
        else if (context.AttackElement == "Dendro") auraElement = "Pyro";
        else return null;

        ElementalAura aura = context.Target.GetAura(auraElement);
        return aura != null && aura.AuraAmount > 0f ? aura : null;
    }
}
