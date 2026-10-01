using System.Collections.Generic;
using UnityEngine;

public struct OverloadedReactionResolution
{
    public string DisplayName;
    public float RemainingAttackAmount;
    public List<ReactionDerivedHit> DerivedHits;
}

/// <summary>超载：火雷 1:1 消耗，并对主目标及左右相邻位置产生火元素剧变伤害。</summary>
public static class OverloadedReactionHandler
{
    public static bool CanResolve(ReactionContext context)
        => TryGetReactableAura(context, out _, out _);

    public static bool TryResolve(
        ReactionContext context,
        float levelCoefficient,
        out OverloadedReactionResolution resolution)
    {
        resolution = default;
        if (!TryGetReactableAura(context, out string auraElement, out float auraAmount)) return false;

        float consumed = Mathf.Min(context.AttackAmount, auraAmount);
        if (auraElement == "Pyro")
        {
            if (context.RestrictedAuraKind == ReactionPoolKind.Burning)
                BurningReactionHandler.ConsumeBurningAura(context.Target, consumed);
            else if (context.RestrictedAuraKind == ReactionPoolKind.NormalAura)
                context.Target.ConsumeAura("Pyro", consumed);
            else
                BurningReactionHandler.ConsumePyroIncludingBurning(context.Target, consumed);
        }
        else
            context.Target.ConsumeAura(auraElement, consumed);

        resolution.DisplayName = "超载";
        resolution.RemainingAttackAmount = Mathf.Max(0f, context.AttackAmount - consumed);
        resolution.DerivedHits = BuildDerivedHits(context, levelCoefficient);
        return true;
    }

    private static List<ReactionDerivedHit> BuildDerivedHits(
        ReactionContext context,
        float levelCoefficient)
    {
        var hits = new List<ReactionDerivedHit>();
        ReactionSourceSnapshot snapshot = context.SourceSnapshotOverride != null
            ? context.SourceSnapshotOverride
            : ReactionSourceSnapshot.Capture(context, ReactionType.Overloaded, "Pyro");
        bool sourceIsEnemy = context.SourceKind == ReactionSourceKind.EnemySkill
            || (context.SourceEntity != null
                && context.SourceEntity.Type == BattleEntity.EntityType.Enemy);

        foreach (BattleEntity target in GetTargets(context.Target))
        {
            float resistance = ReactionDamageCalculator.GetResistanceMultiplier(target, "Pyro");
            float damage = sourceIsEnemy
                ? ReactionDamageCalculator.CalculateEnemyTransformative(
                    levelCoefficient, 4f, snapshot.DMGBonus, resistance)
                : ReactionDamageCalculator.CalculateCharacterTransformative(
                    levelCoefficient,
                    4f,
                    snapshot.TotalEM,
                    snapshot.DMGBonus,
                    snapshot.BaseDMGBonusFlat,
                    resistance);

            hits.Add(new ReactionDerivedHit
            {
                Target = target,
                Damage = damage,
                DamageElement = "Pyro",
                PoiseDamage = 100f,
                ElementAmount = 0f,
                // 超载来源声明（2026-08-19）：归属后手施加元素/触发反应的攻击方角色
                Source = DamageSourceInfo.FromReactionSnapshot(snapshot, ReactionType.Overloaded),
                ReactionType = ReactionType.Overloaded
            });
        }
        return hits;
    }

    private static bool TryGetReactableAura(
        ReactionContext context,
        out string auraElement,
        out float auraAmount)
    {
        auraElement = string.Empty;
        auraAmount = 0f;
        if (context == null || context.Target == null || context.AttackAmount <= 0f)
            return false;

        if (context.AttackElement == "Pyro") auraElement = "Electro";
        else if (context.AttackElement == "Electro") auraElement = "Pyro";
        else return false;

        if (auraElement == "Pyro")
        {
            if (context.AllowsReactionPool("Pyro", ReactionPoolKind.NormalAura))
                auraAmount += context.Target.GetAura("Pyro")?.AuraAmount ?? 0f;
            if (context.AllowsReactionPool("Pyro", ReactionPoolKind.Burning))
                auraAmount += BurningReactionHandler.GetBurningAuraAsPyro(context.Target);
        }
        else
            auraAmount = context.AllowsReactionPool(auraElement, ReactionPoolKind.NormalAura)
                ? context.Target.GetAura(auraElement)?.AuraAmount ?? 0f : 0f;
        return auraAmount > 0f;
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
