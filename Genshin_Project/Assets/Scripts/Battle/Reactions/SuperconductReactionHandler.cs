using System.Collections.Generic;
using UnityEngine;

public struct SuperconductReactionResolution
{
    public ReactionType Type;
    public string DisplayName;
    public float RemainingAttackAmount;
    public List<ReactionDerivedHit> DerivedHits;
    public PendingStellarSuperconduct PendingStellarSuperconduct;
}

/// <summary>普通超导：冰雷 1:1 消耗，对主目标及相邻目标造成冰元素剧变伤害并施加减物抗状态。</summary>
public static class SuperconductReactionHandler
{
    private const int StateDuration = 3;

    public static bool CanResolve(ReactionContext context)
        => GetReactableAuraAmount(context) > 0f;

    public static bool TryResolve(
        ReactionContext context,
        float levelCoefficient,
        out SuperconductReactionResolution resolution)
    {
        resolution = default;
        float auraAmount = GetReactableAuraAmount(context);
        if (auraAmount <= 0f) return false;

        float consumed = Mathf.Min(context.AttackAmount, auraAmount);
        PendingStellarSuperconduct pending = null;
        bool stellar = BattleManager.Instance?.TeamBonuses?.Stellar.TryPrepareSuperconduct(context, out pending) == true;
        ConsumeAura(context, consumed);

        resolution.Type = stellar ? ReactionType.StellarSuperconduct : ReactionType.Superconduct;
        if (stellar)
        {
            resolution.DisplayName = "星超导";
            resolution.PendingStellarSuperconduct = pending;
            resolution.RemainingAttackAmount = Mathf.Max(0f, context.AttackAmount - consumed);
            resolution.DerivedHits = new List<ReactionDerivedHit>();
            return true;
        }
        resolution.DisplayName = "超导";
        resolution.RemainingAttackAmount = Mathf.Max(0f, context.AttackAmount - consumed);
        ReactionSourceSnapshot snapshot = ReactionSourceSnapshot.Capture(context);
        resolution.DerivedHits = BuildDerivedHits(context, levelCoefficient, snapshot);
        foreach (ReactionDerivedHit hit in resolution.DerivedHits)
        {
            if (!ReactionStateSystem.TryGetEntityState(
                    hit.Target,
                    ReactionType.Superconduct,
                    out _))
            {
                ReactionStateSystem.SetEntityState(
                    hit.Target,
                    ReactionType.Superconduct,
                    StateDuration,
                    snapshot,
                    context.ApplicationPhase);
            }
        }
        return true;
    }

    private static List<ReactionDerivedHit> BuildDerivedHits(
        ReactionContext context,
        float levelCoefficient,
        ReactionSourceSnapshot snapshot)
    {
        var hits = new List<ReactionDerivedHit>();
        ReactionBuffTotals bonuses = ReactionDamageCalculator.CollectReactionBonuses(
            context.SourceEntity,
            ReactionType.Superconduct,
            "Cryo");
        bool sourceIsEnemy = context.SourceKind == ReactionSourceKind.EnemySkill
            || (context.SourceEntity != null
                && context.SourceEntity.Type == BattleEntity.EntityType.Enemy);

        foreach (BattleEntity target in GetTargets(context.Target))
        {
            float resistance = ReactionDamageCalculator.GetResistanceMultiplier(target, "Cryo");
            float damage = sourceIsEnemy
                ? ReactionDamageCalculator.CalculateEnemyTransformative(
                    levelCoefficient, 1f, bonuses.DMGBonus, resistance)
                : ReactionDamageCalculator.CalculateCharacterTransformative(
                    levelCoefficient,
                    1f,
                    snapshot.TotalEM,
                    bonuses.DMGBonus,
                    bonuses.BaseDMGBonusFlat,
                    resistance);

            hits.Add(new ReactionDerivedHit
            {
                Target = target,
                Damage = damage,
                DamageElement = "Cryo",
                ElementAmount = 0f,
                // 超导来源声明（2026-08-19）：归属触发反应的攻击方角色
                Source = DamageSourceInfo.FromReactionSnapshot(snapshot, ReactionType.Superconduct),
                ReactionType = ReactionType.Superconduct
            });
        }
        return hits;
    }

    private static float GetReactableAuraAmount(ReactionContext context)
    {
        if (context == null || context.Target == null || context.AttackAmount <= 0f)
            return 0f;

        if (context.AttackElement == "Cryo")
            return ElementQuery.GetReactionAmount(context, "Electro");
        if (context.AttackElement != "Electro")
            return 0f;

        return ElementQuery.GetReactionAmount(context, "Cryo");
    }

    private static void ConsumeAura(ReactionContext context, float amount)
    {
        ElementAuraSystem.ConsumeReactionAura(context, context.AttackElement == "Cryo" ? "Electro" : "Cryo", amount);
    }

    private static IEnumerable<BattleEntity> GetTargets(BattleEntity mainTarget)
    {
        int origin = mainTarget.Position != null
            ? mainTarget.Position.SlotIndex
            : mainTarget.SlotPosition;
        BattleSide side = mainTarget.Position != null
            ? mainTarget.Position.Side
            : mainTarget.Side;
        BattleField field = mainTarget.Position != null
            ? mainTarget.Position.OwnerField
            : null;
        if (field == null && BattleManager.Instance != null)
            field = BattleManager.Instance.Field;

        if (field == null || origin <= 0)
        {
            if (mainTarget.IsAlive) yield return mainTarget;
            yield break;
        }

        foreach (int position in BattlePositionSystem.GetAdjacentPositions(side, origin, 1))
        {
            FieldPosition slot = field.GetSlot(side, position);
            if (slot != null && slot.IsOccupied && slot.Occupant != null)
                yield return slot.Occupant;
        }
    }
}
