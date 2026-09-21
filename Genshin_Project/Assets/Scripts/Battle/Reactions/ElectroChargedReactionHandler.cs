using System.Collections.Generic;
using UnityEngine;

public struct ElectroChargedReactionResolution
{
    public string DisplayName;
    public float RemainingAttackAmount;
    public List<ReactionDerivedHit> DerivedHits;
}

/// <summary>普通感电：水雷即时伤害、双回合结束伤害，以及沿相邻水附着目标传导。</summary>
public static class ElectroChargedReactionHandler
{
    private const float ReactionMultiplier = 9.6f;
    private const float PoiseDamage = 50f;

    public static bool IsElectroCharged(BattleEntity target)
        => target != null
            && ReactionStateSystem.TryGetEntityState(target, ReactionType.ElectroCharged, out _);

    public static bool CanResolve(ReactionContext context)
        => GetOpposingAura(context) != null;

    public static bool TryResolve(
        ReactionContext context,
        float levelCoefficient,
        out ElectroChargedReactionResolution resolution)
    {
        resolution = default;
        ElementalAura aura = GetOpposingAura(context);
        if (aura == null) return false;

        float attackConsumed = Mathf.Min(1f, context.AttackAmount);
        float auraConsumed = Mathf.Min(1f, aura.AuraAmount);
        context.Target.ConsumeAura(aura.Element, auraConsumed);

        float remainingAttack = Mathf.Max(0f, context.AttackAmount - attackConsumed);
        ReactionSourceSnapshot snapshot = ReactionSourceSnapshot.Capture(
            context,
            ReactionType.ElectroCharged,
            "Electro");
        snapshot.LevelCoefficient = levelCoefficient;
        resolution.DisplayName = "感电";
        resolution.RemainingAttackAmount = remainingAttack;
        resolution.DerivedHits = BuildDamageChain(
            context.Target,
            snapshot,
            levelCoefficient);

        if (WillKeepBothAuras(context, aura.Element, remainingAttack))
        {
            ElementalAura remainingAura = context.Target.GetAura(aura.Element);
            float coexistingAmount = remainingAura != null
                ? Mathf.Min(remainingAttack, remainingAura.AuraAmount)
                : 0f;
            ReactionStateSystem.SetEntityState(
                context.Target,
                ReactionType.ElectroCharged,
                1,
                snapshot,
                context.ApplicationPhase,
                false,
                coexistingAmount);
        }
        else
        {
            ReactionStateSystem.RemoveEntityState(
                context.Target,
                ReactionType.ElectroCharged);
        }
        return true;
    }

    /// <summary>在我方及敌方回合结束阶段结算所有感电状态。</summary>
    public static void TickTurnEnd(TurnPhase phase)
    {
        if (phase != TurnPhase.AllyPostTurn && phase != TurnPhase.EnemyPostTurn)
            return;

        var states = new List<ReactionStateInstance>();
        foreach (ReactionStateInstance state in ReactionStateSystem.ActiveStates)
        {
            if (state != null && state.Type == ReactionType.ElectroCharged)
                states.Add(state);
        }
        states.Sort((left, right) => left.ApplyOrder.CompareTo(right.ApplyOrder));

        foreach (ReactionStateInstance state in states)
            TickState(state);
    }

    /// <summary>元素反应或自然衰减移除水雷共存后，立即清理失效的感电状态。</summary>
    public static void CleanupInvalidStates()
    {
        var invalidTargets = new List<BattleEntity>();
        foreach (ReactionStateInstance state in ReactionStateSystem.ActiveStates)
        {
            if (state == null || state.Type != ReactionType.ElectroCharged) continue;
            BattleEntity target = state.Target;
            if (target == null
                || target.GetAura("Hydro") == null
                || target.GetAura("Electro") == null)
                invalidTargets.Add(target);
        }

        foreach (BattleEntity target in invalidTargets)
            ReactionStateSystem.RemoveEntityState(target, ReactionType.ElectroCharged);
    }

    private static void TickState(ReactionStateInstance state)
    {
        BattleEntity target = state.Target;
        ElementalAura hydro = target != null ? target.GetAura("Hydro") : null;
        ElementalAura electro = target != null ? target.GetAura("Electro") : null;
        if (target == null || hydro == null || electro == null)
        {
            ReactionStateSystem.RemoveEntityState(target, ReactionType.ElectroCharged);
            return;
        }

        target.ConsumeAura("Hydro", Mathf.Min(1f, hydro.AuraAmount));
        target.ConsumeAura("Electro", Mathf.Min(1f, electro.AuraAmount));

        ReactionSourceSnapshot snapshot = state.SourceSnapshot;
        float levelCoefficient = snapshot != null && snapshot.LevelCoefficient > 0f
            ? snapshot.LevelCoefficient
            : ReactionDamageCalculator.GetLevelCoefficient(snapshot != null ? snapshot.Level : 1);
        var result = new ReactionResult();
        result.DerivedHits.AddRange(BuildDamageChain(
            target,
            snapshot,
            levelCoefficient));
        ReactionEffectExecutor.ExecuteDerivedHits(result);

        hydro = target.GetAura("Hydro");
        electro = target.GetAura("Electro");
        if (hydro == null || electro == null)
            ReactionStateSystem.RemoveEntityState(target, ReactionType.ElectroCharged);
        else
        {
            state.ReactionElementAmount = Mathf.Min(hydro.AuraAmount, electro.AuraAmount);
            ReactionStateSystem.RefreshDisplay(state);
        }
    }

    private static List<ReactionDerivedHit> BuildDamageChain(
        BattleEntity mainTarget,
        ReactionSourceSnapshot snapshot,
        float levelCoefficient)
    {
        var hits = new List<ReactionDerivedHit>();
        var visited = new HashSet<BattleEntity>();
        var queue = new Queue<BattleEntity>();
        if (mainTarget == null) return hits;

        visited.Add(mainTarget);
        queue.Enqueue(mainTarget);

        while (queue.Count > 0)
        {
            BattleEntity target = queue.Dequeue();
            hits.Add(BuildHit(target, snapshot, levelCoefficient));

            foreach (BattleEntity adjacent in GetAdjacentOccupiedTargets(target))
            {
                if (adjacent == null || visited.Contains(adjacent)) continue;
                ElementalAura hydro = adjacent.GetAura("Hydro");
                if (hydro == null || hydro.AuraAmount <= 0f) continue;

                visited.Add(adjacent);
                adjacent.ConsumeAura("Hydro", Mathf.Min(1f, hydro.AuraAmount));
                queue.Enqueue(adjacent);
            }
        }
        return hits;
    }

    private static ReactionDerivedHit BuildHit(
        BattleEntity target,
        ReactionSourceSnapshot snapshot,
        float levelCoefficient)
    {
        float resistance = ReactionDamageCalculator.GetResistanceMultiplier(target, "Electro");
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

        return new ReactionDerivedHit
        {
            Target = target,
            Damage = damage,
            DamageElement = "Electro",
            PoiseDamage = PoiseDamage,
            ElementAmount = 0f,
            // 感电来源声明（2026-08-19）：使用创建感电状态时保存的来源快照，保留原始角色归属
            Source = DamageSourceInfo.FromReactionSnapshot(snapshot, ReactionType.ElectroCharged),
            ReactionType = ReactionType.ElectroCharged
        };
    }

    private static ElementalAura GetOpposingAura(ReactionContext context)
    {
        if (context == null || context.Target == null || context.AttackAmount <= 0f)
            return null;

        string auraElement;
        if (context.AttackElement == "Hydro") auraElement = "Electro";
        else if (context.AttackElement == "Electro") auraElement = "Hydro";
        else return null;

        ElementalAura aura = context.Target.GetAura(auraElement);
        return context.AllowsReactionPool(auraElement, ReactionPoolKind.NormalAura)
            && aura != null && aura.AuraAmount > 0f ? aura : null;
    }

    private static bool WillKeepBothAuras(
        ReactionContext context,
        string opposingElement,
        float remainingAttack)
    {
        ElementalAura opposingAura = context.Target.GetAura(opposingElement);
        return remainingAttack > 0f
            && opposingAura != null
            && opposingAura.AuraAmount > 0f;
    }

    private static IEnumerable<BattleEntity> GetAdjacentOccupiedTargets(BattleEntity target)
    {
        int origin = target.Position != null ? target.Position.SlotIndex : target.SlotPosition;
        BattleSide side = target.Position != null ? target.Position.Side : target.Side;
        BattleField field = target.Position != null ? target.Position.OwnerField : null;
        if (field == null && BattleManager.Instance != null)
            field = BattleManager.Instance.Field;
        if (field == null || origin <= 0) yield break;

        foreach (int position in BattlePositionSystem.GetAdjacentPositions(side, origin, 1))
        {
            FieldPosition slot = field.GetSlot(side, position);
            if (slot != null && slot.IsOccupied && slot.Occupant != null)
                yield return slot.Occupant;
        }
    }
}
