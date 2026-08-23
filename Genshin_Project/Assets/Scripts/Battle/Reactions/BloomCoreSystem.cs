using System;
using System.Collections.Generic;

[Serializable]
public sealed class BloomCoreInstance
{
    public FieldPosition Position;
    public int OriginPhase;
    public long ApplyOrder;
    public long CreatedEffectExecutionID;
    public ReactionSourceSnapshot SourceSnapshot;
    public StatusInstance DisplayStatus;
}

/// <summary>草原核运行时容器：独立管理位置、来源快照、到期爆炸及五枚上限。</summary>
public static class BloomCoreSystem
{
    public const string StatusID = "ST_Bloom";
    public const int MaxCoreCount = 5;

    private static readonly List<BloomCoreInstance> Cores = new List<BloomCoreInstance>();
    public static IReadOnlyList<BloomCoreInstance> ActiveCores => Cores;

    public static BloomCoreInstance CreateCore(
        ReactionContext context,
        float levelCoefficient,
        out List<ReactionDerivedHit> overflowHits)
    {
        overflowHits = new List<ReactionDerivedHit>();
        FieldPosition position = ResolveEnemyFieldPosition(context);
        if (position == null) return null;

        ReactionSourceSnapshot snapshot = ReactionSourceSnapshot.Capture(
            context,
            ReactionType.Bloom,
            "Dendro");
        snapshot.LevelCoefficient = levelCoefficient;
        long applyOrder = ++BattleEntity._applyOrderCounter;
        StatusMainData mainData = GetStatusMainData();
        var display = new StatusInstance
        {
            StatusID2 = StatusID,
            StackCount = 1,
            RemainingPhaseCount = 1,
            AddInPhase = context != null ? context.ApplicationPhase : -1,
            Caster = snapshot.SourceEntity,
            MainData = mainData,
            IsActive = true,
            ApplyOrder = applyOrder
        };
        position.StatusList.Add(display);

        var core = new BloomCoreInstance
        {
            Position = position,
            OriginPhase = context != null && context.ApplicationPhase > 0
                ? context.ApplicationPhase
                : (BattleManager.Instance != null
                    ? (int)BattleManager.Instance.CurrentPhase
                    : -1),
            ApplyOrder = applyOrder,
            CreatedEffectExecutionID = context != null ? context.EffectExecutionID : 0,
            SourceSnapshot = snapshot,
            DisplayStatus = display
        };
        Cores.Add(core);

        if (Cores.Count > MaxCoreCount)
        {
            BloomCoreInstance oldest = Cores[0];
            for (int index = 1; index < Cores.Count; index++)
            {
                if (Cores[index].ApplyOrder < oldest.ApplyOrder)
                    oldest = Cores[index];
            }
            overflowHits = RemoveCoreAndBuildHits(oldest);
        }
        return core;
    }

    /// <summary>草原核创建后下一次进入相同阶段时到期爆炸。</summary>
    public static void TickPhase(int phase)
    {
        var expired = new List<BloomCoreInstance>();
        foreach (BloomCoreInstance core in Cores)
        {
            if (core != null && core.OriginPhase == phase)
                expired.Add(core);
        }
        expired.Sort((left, right) => left.ApplyOrder.CompareTo(right.ApplyOrder));
        foreach (BloomCoreInstance core in expired)
            Detonate(core);
    }

    public static List<ReactionDerivedHit> Detonate(BloomCoreInstance core)
    {
        List<ReactionDerivedHit> hits = RemoveCoreAndBuildHits(core);
        var result = new ReactionResult();
        result.DerivedHits.AddRange(hits);
        ReactionEffectExecutor.ExecuteDerivedHits(result);
        return hits;
    }

    private static List<ReactionDerivedHit> RemoveCoreAndBuildHits(BloomCoreInstance core)
    {
        var hits = new List<ReactionDerivedHit>();
        if (core == null || !Cores.Remove(core)) return hits;
        RemoveDisplay(core);

        ReactionSourceSnapshot snapshot = core.SourceSnapshot;
        float levelCoefficient = snapshot != null && snapshot.LevelCoefficient > 0f
            ? snapshot.LevelCoefficient
            : ReactionDamageCalculator.GetLevelCoefficient(snapshot != null ? snapshot.Level : 1);
        foreach (BattleEntity target in GetExplosionTargets(core.Position))
            hits.Add(BuildHit(target, snapshot, levelCoefficient));
        return hits;
    }

    /// <summary>供后续超绽放、烈绽放脚本读取同一位置可触发的草原核。</summary>
    public static List<BloomCoreInstance> GetTriggerableCores(
        int enemyPosition,
        string attackElement,
        ReactionType sourceReactionType,
        long effectExecutionID)
    {
        var result = new List<BloomCoreInstance>();
        if (attackElement != "Pyro" && attackElement != "Electro") return result;
        if (sourceReactionType == ReactionType.Overloaded
            || sourceReactionType == ReactionType.ElectroCharged)
            return result;

        foreach (BloomCoreInstance core in Cores)
        {
            if (core == null || core.Position == null) continue;
            if (core.Position.Side != BattleSide.Enemy
                || core.Position.SlotIndex != enemyPosition)
                continue;
            if (effectExecutionID != 0
                && core.CreatedEffectExecutionID == effectExecutionID)
                continue;
            result.Add(core);
        }
        result.Sort((left, right) => left.ApplyOrder.CompareTo(right.ApplyOrder));
        return result;
    }

    /// <summary>由超绽放或烈绽放接管伤害结算时，仅移除草原核及其显示状态。</summary>
    public static bool ConsumeCore(BloomCoreInstance core)
    {
        if (core == null || !Cores.Remove(core)) return false;
        RemoveDisplay(core);
        return true;
    }

    public static void ClearAll()
    {
        foreach (BloomCoreInstance core in Cores)
            RemoveDisplay(core);
        Cores.Clear();
    }

    private static ReactionDerivedHit BuildHit(
        BattleEntity target,
        ReactionSourceSnapshot snapshot,
        float levelCoefficient)
    {
        float resistance = ReactionDamageCalculator.GetResistanceMultiplier(target, "Dendro");
        bool sourceIsEnemy = snapshot != null
            && (snapshot.SourceKind == ReactionSourceKind.EnemySkill
                || (snapshot.SourceEntity != null
                    && snapshot.SourceEntity.Type == BattleEntity.EntityType.Enemy));
        float damage = sourceIsEnemy
            ? ReactionDamageCalculator.CalculateEnemyTransformative(
                levelCoefficient,
                2f,
                snapshot != null ? snapshot.DMGBonus : 0f,
                resistance)
            : ReactionDamageCalculator.CalculateCharacterTransformative(
                levelCoefficient,
                2f,
                snapshot != null ? snapshot.TotalEM : 0f,
                snapshot != null ? snapshot.DMGBonus : 0f,
                snapshot != null ? snapshot.BaseDMGBonusFlat : 0f,
                resistance);
        return new ReactionDerivedHit
        {
            Target = target,
            Damage = damage,
            DamageElement = "Dendro",
            PoiseDamage = 0f,
            ElementAmount = 0f
        };
    }

    private static IEnumerable<BattleEntity> GetExplosionTargets(FieldPosition origin)
    {
        if (origin == null || origin.OwnerField == null) yield break;
        foreach (int position in BattlePositionSystem.GetAdjacentPositions(
                     BattleSide.Enemy,
                     origin.SlotIndex,
                     1))
        {
            FieldPosition slot = origin.OwnerField.GetSlot(BattleSide.Enemy, position);
            if (slot != null && slot.IsOccupied && slot.Occupant != null)
                yield return slot.Occupant;
        }
    }

    private static FieldPosition ResolveEnemyFieldPosition(ReactionContext context)
    {
        if (context == null || context.Target == null) return null;
        BattleField field = context.Target.Position != null
            ? context.Target.Position.OwnerField
            : null;
        if (field == null && BattleManager.Instance != null)
            field = BattleManager.Instance.Field;
        int position = context.Target.Position != null
            ? context.Target.Position.SlotIndex
            : context.Target.SlotPosition;
        return field != null ? field.GetSlot(BattleSide.Enemy, position) : null;
    }

    private static void RemoveDisplay(BloomCoreInstance core)
    {
        if (core?.Position != null && core.DisplayStatus != null)
            core.Position.StatusList.Remove(core.DisplayStatus);
    }

    private static StatusMainData GetStatusMainData()
    {
        if (DataManager.Instance != null
            && DataManager.Instance.StatusMainDict.TryGetValue(StatusID, out StatusMainData configured))
            return configured;
        return new StatusMainData
        {
            StatusID = 40007,
            StatusID2 = StatusID,
            StatusName = "草原核",
            Display = 1
        };
    }
}
