using System.Collections.Generic;

public struct BloomSecondaryReactionResolution
{
    public ReactionType Type;
    public string DisplayName;
    public List<ReactionDerivedHit> DerivedHits;
    public int ConsumedCoreCount;
}

/// <summary>火、雷命中草原核所在位置时的烈绽放与超绽放结算。</summary>
public static class BloomSecondaryReactionHandler
{
    private const float HyperbloomMultiplier = 3f;
    private const float BurgeonMultiplier = 4f;
    private const int MissileCountPerCore = 5;

    public static bool TryResolve(
        ReactionContext context,
        ReactionType sourceReactionType,
        float levelCoefficient,
        out BloomSecondaryReactionResolution resolution)
    {
        FieldPosition position = ResolveEnemyFieldPosition(context);
        return TryResolveAtPosition(
            context,
            position,
            sourceReactionType,
            levelCoefficient,
            out resolution);
    }

    /// <summary>场地攻击可直接调用此入口，因此原位置没有单位时也能引爆草原核。</summary>
    public static bool TryResolveAtPosition(
        ReactionContext context,
        FieldPosition position,
        ReactionType sourceReactionType,
        float levelCoefficient,
        out BloomSecondaryReactionResolution resolution)
    {
        resolution = default;
        if (context == null
            || !context.CanTriggerReaction
            || position == null
            || position.Side != BattleSide.Enemy)
            return false;

        ReactionType type;
        string displayName;
        if (context.AttackElement == "Electro")
        {
            type = ReactionType.Hyperbloom;
            displayName = "超绽放";
        }
        else if (context.AttackElement == "Pyro")
        {
            type = ReactionType.Burgeon;
            displayName = "烈绽放";
        }
        else
        {
            return false;
        }

        List<BloomCoreInstance> cores = BloomCoreSystem.GetTriggerableCores(
            position.SlotIndex,
            context.AttackElement,
            sourceReactionType,
            context.EffectExecutionID);
        if (cores.Count == 0) return false;

        if (levelCoefficient <= 0f)
        {
            levelCoefficient = ReactionDamageCalculator.GetLevelCoefficient(
                context.SourceEntity != null ? context.SourceEntity.Level : 1);
        }

        ReactionSourceSnapshot snapshot = ReactionSourceSnapshot.Capture(
            context,
            type,
            "Dendro");
        snapshot.LevelCoefficient = levelCoefficient;
        var hits = new List<ReactionDerivedHit>();

        foreach (BloomCoreInstance core in cores)
        {
            if (!BloomCoreSystem.ConsumeCore(core)) continue;

            if (type == ReactionType.Hyperbloom)
                AddHyperbloomHits(position, snapshot, levelCoefficient, hits);
            else
                AddBurgeonHits(position, snapshot, levelCoefficient, hits);
        }

        resolution = new BloomSecondaryReactionResolution
        {
            Type = type,
            DisplayName = displayName,
            DerivedHits = hits,
            ConsumedCoreCount = cores.Count
        };
        return true;
    }

    private static void AddHyperbloomHits(
        FieldPosition origin,
        ReactionSourceSnapshot snapshot,
        float levelCoefficient,
        List<ReactionDerivedHit> hits)
    {
        List<BattleEntity> targets = GetTargets(origin);
        if (targets.Count == 0) return;

        for (int count = 0; count < MissileCountPerCore; count++)
        {
            int index = BattleRandom.NextInt(0, targets.Count);
            if (index < 0) index = 0;
            if (index >= targets.Count) index = targets.Count - 1;
            hits.Add(BuildHit(targets[index], snapshot, levelCoefficient, HyperbloomMultiplier, ReactionType.Hyperbloom));
        }
    }

    private static void AddBurgeonHits(
        FieldPosition origin,
        ReactionSourceSnapshot snapshot,
        float levelCoefficient,
        List<ReactionDerivedHit> hits)
    {
        foreach (BattleEntity target in GetTargets(origin))
            hits.Add(BuildHit(target, snapshot, levelCoefficient, BurgeonMultiplier, ReactionType.Burgeon));
    }

    private static ReactionDerivedHit BuildHit(
        BattleEntity target,
        ReactionSourceSnapshot snapshot,
        float levelCoefficient,
        float multiplier,
        ReactionType reactionType)
    {
        float resistance = ReactionDamageCalculator.GetResistanceMultiplier(target, "Dendro");
        bool sourceIsEnemy = snapshot != null
            && (snapshot.SourceKind == ReactionSourceKind.EnemySkill
                || (snapshot.SourceEntity != null
                    && snapshot.SourceEntity.Type == BattleEntity.EntityType.Enemy));
        float damage = sourceIsEnemy
            ? ReactionDamageCalculator.CalculateEnemyTransformative(
                levelCoefficient,
                multiplier,
                snapshot != null ? snapshot.DMGBonus : 0f,
                resistance)
            : ReactionDamageCalculator.CalculateCharacterTransformative(
                levelCoefficient,
                multiplier,
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
            ElementAmount = 0f,
            // 绽放次生来源声明（2026-08-19）：使用创建时的来源快照，保留原始角色归属
            Source = DamageSourceInfo.FromReactionSnapshot(snapshot, reactionType),
            ReactionType = reactionType
        };
    }

    private static List<BattleEntity> GetTargets(FieldPosition origin)
    {
        var targets = new List<BattleEntity>();
        if (origin == null || origin.OwnerField == null) return targets;

        foreach (int slotIndex in BattlePositionSystem.GetAdjacentPositions(
                     BattleSide.Enemy,
                     origin.SlotIndex,
                     1))
        {
            FieldPosition slot = origin.OwnerField.GetSlot(BattleSide.Enemy, slotIndex);
            if (slot != null && slot.IsOccupied && slot.Occupant != null)
                targets.Add(slot.Occupant);
        }
        return targets;
    }

    private static FieldPosition ResolveEnemyFieldPosition(ReactionContext context)
    {
        if (context == null || context.Target == null) return null;
        FieldPosition position = context.Target.Position;
        if (position != null && position.Side == BattleSide.Enemy)
            return position;

        BattleField field = BattleManager.Instance != null ? BattleManager.Instance.Field : null;
        return field != null
            ? field.GetSlot(BattleSide.Enemy, context.Target.SlotPosition)
            : null;
    }
}
