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
        => ElementQuery.GetAmount(target, "Pyro", ReactionPoolKind.Burning);

    public static float ConsumeBurningAura(BattleEntity target, float amount)
    {
        using var declarations = ElementEventScope.Begin("ReactionConsumption", target);
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
        => ElementQuery.GetReactionAmount(target, "Pyro");

    /// <summary>先消耗普通过量火，再消耗燃元素。</summary>
    public static float ConsumePyroIncludingBurning(BattleEntity target, float amount)
    {
        using var declarations = ElementEventScope.Begin("ReactionConsumption", target);
        if (target == null || amount <= 0f) return 0f;

        float remaining = amount;
        float consumedTotal = 0f;
        ElementPoolReading pyro = ElementQuery.ReadNormal(target, "Pyro");
        if (pyro != null)
        {
            float consumed = Mathf.Min(pyro.Amount, remaining);
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
        ElementPoolReading aura = GetOpposingNormalAura(context);
        if (aura == null) return false;

        float consumed = aura.AvailableFor(context.AttackAmount);
        ElementAuraSystem.ConsumeReactionAura(context, aura.Element, consumed);
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

        foreach (ReactionStateInstance state in states)
            BattleExecution.Enqueue(async () =>
            {
                bool previous = _isTickingTurnEnd;
                _isTickingTurnEnd = true;
                try
                {
                    await BattleExecution.PlayEffect(new BattlePresentationRequest(
                        BattlePresentationKind.Reaction, state.SourceSnapshot?.SourceEntity,
                        state.SourceSnapshot?.SourceEffectID, targets: new[] { state.Target }, reaction: ReactionType.Burning), () =>
                    {
                        if (ReactionStateSystem.TryGetEntityState(state.Target, ReactionType.Burning, out var current)
                            && object.ReferenceEquals(current, state)) ExecuteTick(state, phase);
                    });
                }
                finally { _isTickingTurnEnd = previous; }
            });
    }

    private static void ExecuteTick(ReactionStateInstance state, TurnPhase phase)
    {
        using var resolutionEffect = BattleResolutionContext.BeginEffect();
        using var hitResponses = BattleResolutionContext.BeginCollection();
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

        DMGTakenSnapshot taken = target.PrepareDMGTaken(
            snapshot != null ? snapshot.SourceEffectID : string.Empty,
            snapshot != null ? snapshot.SourceSkillID : string.Empty, "Reaction", "Pyro");
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
            AttackAmount = target.GetEnemyElementalShield()?.Element == "Pyro" ? 0f : 1f,
            PreReactionDamage = damage,
            DamageType = "Reaction",
            SkipImmediateAuraDecay = true
        });

        HitResolution resolution;
        using (var batch = new HitBatch())
        {
            resolution = batch.Resolve(new HitRequest
            {
                Target = target,
                DamageBeforeShield = result.FinalDamage,
                EnemyShieldHit = result.EnemyShieldHit,
                Element = "Pyro",
                DamageType = "Reaction",
                DMGTaken = taken,
                // 燃烧保留触发者/阵营，但它是反应伤害，只产生 OnDamage。
                Source = DamageSourceInfo.FromReactionSnapshot(snapshot, ReactionType.Burning),
                HitIndex = 0,
                TargetIndex = 0
            });
            batch.Flush();
        }
        ReactionEffectExecutor.FinalizePrimaryHit(
            result,
            resolution != null && resolution.DamageEvent != null
                ? resolution.DamageEvent.ActualHPDamage
                : 0f);
        ReactionEffectExecutor.ExecuteDerivedHits(result);
    }

    private static ElementPoolReading GetOpposingNormalAura(ReactionContext context)
    {
        if (context == null || context.Target == null || context.AttackAmount <= 0f)
            return null;

        string auraElement;
        if (context.AttackElement == "Pyro") auraElement = "Dendro";
        else if (context.AttackElement == "Dendro") auraElement = "Pyro";
        else return null;

        ElementPoolReading aura = ElementQuery.ReadReactionAura(context, auraElement);
        return aura?.IsPresent == true ? aura : null;
    }
}
