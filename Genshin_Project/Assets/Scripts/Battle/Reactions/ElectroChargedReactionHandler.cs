using System.Collections.Generic;
using UnityEngine;

public struct ElectroChargedReactionResolution
{
    public ReactionType Type;
    public PendingLunarConduct PendingLunarConduct;
    public string DisplayName;
    public float RemainingAttackAmount;
    public List<ReactionDerivedHit> DerivedHits;
}

/// <summary>普通感电：水雷即时伤害、双回合结束伤害，以及沿相邻水附着目标传导。</summary>
public static class ElectroChargedReactionHandler
{
    private const float ReactionMultiplier = 9.6f;

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
        ElementPoolReading aura = GetOpposingAura(context);
        if (aura == null) return false;

        if (LunarElectroChargedReactionHandler.TryPrepare(context, aura, out var lunar))
        {
            ElementAuraSystem.ConsumeReactionAura(context, aura.Element, aura.AvailableFor(LunarElectroChargedReactionHandler.Consumption));
            ReactionStateSystem.RemoveEntityState(context.Target, ReactionType.ElectroCharged);
            resolution.Type = ReactionType.LunarElectroCharged;
            resolution.DisplayName = "月感电";
            resolution.RemainingAttackAmount = Mathf.Max(0f, context.AttackAmount - LunarElectroChargedReactionHandler.Consumption);
            resolution.PendingLunarConduct = lunar;
            resolution.DerivedHits = new List<ReactionDerivedHit>();
            return true;
        }

        float attackConsumed = Mathf.Min(1f, context.AttackAmount);
        float auraConsumed = aura.AvailableFor(1f);
        ElementAuraSystem.ConsumeReactionAura(context, aura.Element, auraConsumed);

        float remainingAttack = Mathf.Max(0f, context.AttackAmount - attackConsumed);
        ReactionSourceSnapshot snapshot = ReactionSourceSnapshot.Capture(
            context,
            ReactionType.ElectroCharged,
            "Electro");
        snapshot.LevelCoefficient = levelCoefficient;
        resolution.Type = ReactionType.ElectroCharged;
        resolution.DisplayName = "感电";
        resolution.RemainingAttackAmount = remainingAttack;
        resolution.DerivedHits = BuildDamageChain(
            context.Target,
            snapshot,
            levelCoefficient);

        if (WillKeepBothAuras(context, aura.Element, remainingAttack))
        {
            ElementPoolReading remainingAura = ReadConductingAura(context.Target, aura.Element);
            float coexistingAmount = remainingAura?.AvailableFor(remainingAttack) ?? 0f;
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
            BattleExecution.Enqueue(() => BattleExecution.PlayEffect(new BattlePresentationRequest(
                BattlePresentationKind.Reaction, state.SourceSnapshot?.SourceEntity,
                state.SourceSnapshot?.SourceEffectID, targets: new[] { state.Target }, reaction: ReactionType.ElectroCharged), () =>
            {
                if (ReactionStateSystem.TryGetEntityState(state.Target, ReactionType.ElectroCharged, out var current)
                    && object.ReferenceEquals(current, state)) TickState(state);
            }));
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
                || ReadConductingAura(target, "Hydro")?.IsPresent != true
                || ReadConductingAura(target, "Electro")?.IsPresent != true)
                invalidTargets.Add(target);
        }

        foreach (BattleEntity target in invalidTargets)
            ReactionStateSystem.RemoveEntityState(target, ReactionType.ElectroCharged);
    }

    private static void TickState(ReactionStateInstance state)
    {
        using var resolutionEffect = BattleResolutionContext.BeginEffect();
        BattleEntity target = state.Target;
        ElementPoolReading hydro = ReadConductingAura(target, "Hydro");
        ElementPoolReading electro = ReadConductingAura(target, "Electro");
        if (target == null || hydro == null || electro == null)
        {
            ReactionStateSystem.RemoveEntityState(target, ReactionType.ElectroCharged);
            return;
        }

        EnemyShieldHitResult hydroShield = ConsumeConductingAura(target, hydro);
        EnemyShieldHitResult electroShield = ConsumeConductingAura(target, electro);

        ReactionSourceSnapshot snapshot = state.SourceSnapshot;
        float levelCoefficient = snapshot != null && snapshot.LevelCoefficient > 0f
            ? snapshot.LevelCoefficient
            : ReactionDamageCalculator.GetLevelCoefficient(snapshot != null ? snapshot.Level : 1);
        var result = new ReactionResult { EnemyShieldHit = hydroShield ?? electroShield };
        result.DerivedHits.AddRange(BuildDamageChain(
            target,
            snapshot,
            levelCoefficient));
        ReactionEffectExecutor.ExecuteDerivedHits(result);

        hydro = ReadConductingAura(target, "Hydro");
        electro = ReadConductingAura(target, "Electro");
        if (hydro == null || electro == null)
            ReactionStateSystem.RemoveEntityState(target, ReactionType.ElectroCharged);
        else
        {
            // 只记录有限共存量供显示。双方都固有时没有有限量，存在性仍由两种真实状态判断，不能填1模拟无限池。
            state.ReactionElementAmount = hydro.IsIntrinsic
                ? (electro.IsIntrinsic ? 0f : electro.Amount)
                : electro.IsIntrinsic ? hydro.Amount : Mathf.Min(hydro.Amount, electro.Amount);
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
        var shieldHits = new Dictionary<BattleEntity, EnemyShieldHitResult>();
        if (mainTarget == null) return hits;

        visited.Add(mainTarget);
        queue.Enqueue(mainTarget);

        while (queue.Count > 0)
        {
            BattleEntity target = queue.Dequeue();
            ReactionDerivedHit hit = BuildHit(target, snapshot, levelCoefficient);
            if (shieldHits.TryGetValue(target, out var shieldHit)) hit.EnemyShieldHit = shieldHit;
            hits.Add(hit);

            foreach (BattleEntity adjacent in GetAdjacentOccupiedTargets(target))
            {
                if (adjacent == null || visited.Contains(adjacent)) continue;
                ElementPoolReading hydro = ReadConductingAura(adjacent, "Hydro");
                if (hydro?.IsPresent != true) continue;

                visited.Add(adjacent);
                EnemyShieldHitResult adjacentShield = ConsumeConductingAura(adjacent, hydro);
                if (adjacentShield != null) shieldHits[adjacent] = adjacentShield;
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
                (snapshot != null ? snapshot.DMGBonus : 0f) + ReactionDamageCalculator.CollectLiveTargetBonuses(
                    snapshot?.SourceEntity, target, ReactionType.ElectroCharged, "Electro").DMGBonus,
                snapshot != null ? snapshot.BaseDMGBonusFlat : 0f,
                resistance);

        return new ReactionDerivedHit
        {
            Target = target,
            Damage = damage,
            DamageElement = "Electro",
            ElementAmount = 0f,
            // 感电来源声明（2026-08-19）：使用创建感电状态时保存的来源快照，保留原始角色归属
            Source = DamageSourceInfo.FromReactionSnapshot(snapshot, ReactionType.ElectroCharged),
            ReactionType = ReactionType.ElectroCharged
        };
    }

    private static ElementPoolReading GetOpposingAura(ReactionContext context)
    {
        if (context == null || context.Target == null || context.AttackAmount <= 0f)
            return null;

        string auraElement;
        if (context.AttackElement == "Hydro") auraElement = "Electro";
        else if (context.AttackElement == "Electro") auraElement = "Hydro";
        else return null;

        ElementPoolReading aura = ElementQuery.ReadReactionAura(context, auraElement);
        return aura?.IsPresent == true ? aura : null;
    }

    private static bool WillKeepBothAuras(
        ReactionContext context,
        string opposingElement,
        float remainingAttack)
    {
        ElementPoolReading opposingAura = ReadConductingAura(context.Target, opposingElement);
        return (remainingAttack > 0f || ElementQuery.ReadIntrinsic(context.Target, context.AttackElement) != null)
            && opposingAura != null
            && opposingAura.IsPresent;
    }

    private static ElementPoolReading ReadConductingAura(BattleEntity target, string element)
        => ElementQuery.ReadShield(target, element) ?? ElementQuery.ReadAura(target, element);

    private static EnemyShieldHitResult ConsumeConductingAura(BattleEntity target, ElementPoolReading aura)
    {
        if (aura.Kind == ReactionPoolKind.EnemyShield)
            return EnemyElementShieldSystem.ResolveHit(target, aura.Element == "Hydro" ? "Electro" : "Hydro", 1f);
        if (!aura.IsIntrinsic) target.ConsumeAura(aura.Element, aura.AvailableFor(1f));
        return null;
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
            if (slot != null && slot.Occupant?.IsOnField == true)
                yield return slot.Occupant;
        }
    }
}
