using UnityEngine;

public struct AmplifyingReactionResolution
{
    public ReactionType Type;
    public string DisplayName;
    public float FinalDamage;
    public float RemainingAttackAmount;
}

/// <summary>蒸发与融化的双向倍率、元素消耗和攻击元素残留。</summary>
public static class AmplifyingReactionHandler
{
    public static bool TryResolve(
        ReactionContext context,
        out AmplifyingReactionResolution resolution)
    {
        resolution = default;
        if (context == null || context.Target == null || context.AttackAmount <= 0f)
            return false;

        BattleEntity target = context.Target;
        float attackAmount = context.AttackAmount;
        float reactionMultiplier;

        if (context.AttackElement == "Pyro" && HasAura(target, "Hydro"))
        {
            // 蒸发固定水:火=1:2。此处火为攻击元素，水为目标附着。
            float hydroAmount = target.GetAura("Hydro").AuraAmount;
            float consumedHydro = Mathf.Min(hydroAmount, attackAmount / 2f);
            float consumedPyro = consumedHydro * 2f;
            if (consumedHydro <= 0f) return false;

            target.ConsumeAura("Hydro", consumedHydro);
            resolution.Type = ReactionType.Vaporize;
            resolution.DisplayName = "蒸发（火×水）";
            resolution.RemainingAttackAmount = Mathf.Max(0f, attackAmount - consumedPyro);
            reactionMultiplier = 1.5f;
        }
        else if (context.AttackElement == "Hydro"
                 && !HasBurningWithExcessDendro(target)
                 && BurningReactionHandler.GetPyroAmountIncludingBurning(target) > 0f)
        {
            // 蒸发固定水:火=1:2。此处水为攻击元素，火为目标附着。
            float pyroAmount = BurningReactionHandler.GetPyroAmountIncludingBurning(target);
            float consumedHydro = Mathf.Min(attackAmount, pyroAmount / 2f);
            float consumedPyro = consumedHydro * 2f;
            if (consumedHydro <= 0f) return false;

            BurningReactionHandler.ConsumePyroIncludingBurning(target, consumedPyro);
            resolution.Type = ReactionType.Vaporize;
            resolution.DisplayName = "蒸发（水×火）";
            resolution.RemainingAttackAmount = Mathf.Max(0f, attackAmount - consumedHydro);
            reactionMultiplier = 2f;
        }
        else if (context.AttackElement == "Pyro" && GetCryoAmount(context) > 0f)
        {
            // 融化固定火:冰=1:2。冻结运行时状态也视作冰附着。
            float cryoAmount = GetCryoAmount(context);
            float consumedPyro = Mathf.Min(attackAmount, cryoAmount / 2f);
            float consumedCryo = consumedPyro * 2f;
            if (consumedPyro <= 0f) return false;

            ConsumeCryo(context, consumedCryo);
            resolution.Type = ReactionType.Melt;
            resolution.DisplayName = "融化（火×冰）";
            resolution.RemainingAttackAmount = Mathf.Max(0f, attackAmount - consumedPyro);
            reactionMultiplier = 2f;
        }
        else if (context.AttackElement == "Cryo"
                 && BurningReactionHandler.GetPyroAmountIncludingBurning(target) > 0f)
        {
            // 融化固定火:冰=1:2。此处冰为攻击元素，火为目标附着。
            float pyroAmount = BurningReactionHandler.GetPyroAmountIncludingBurning(target);
            float consumedPyro = Mathf.Min(pyroAmount, attackAmount / 2f);
            float consumedCryo = consumedPyro * 2f;
            if (consumedPyro <= 0f) return false;

            BurningReactionHandler.ConsumePyroIncludingBurning(target, consumedPyro);
            resolution.Type = ReactionType.Melt;
            resolution.DisplayName = "融化（冰×火）";
            resolution.RemainingAttackAmount = Mathf.Max(0f, attackAmount - consumedCryo);
            reactionMultiplier = 1.5f;
        }
        else
        {
            return false;
        }

        bool sourceIsEnemy = context.SourceKind == ReactionSourceKind.EnemySkill
            || (context.SourceEntity != null && context.SourceEntity.Type == BattleEntity.EntityType.Enemy);
        resolution.FinalDamage = sourceIsEnemy
            ? ReactionDamageCalculator.CalculateEnemyAmplifying(
                context.PreReactionDamage,
                reactionMultiplier)
            : ReactionDamageCalculator.CalculateCharacterAmplifying(
                context.PreReactionDamage,
                reactionMultiplier,
                context.SourceSnapshotOverride != null
                    ? context.SourceSnapshotOverride.TotalEM
                    : (context.SourceEntity != null ? context.SourceEntity.TotalEM : 0f));

        LogManager.Log(
            LogCategory.Reaction,
            $"{resolution.DisplayName}：{context.PreReactionDamage:F1} → {resolution.FinalDamage:F1}" +
            $"（目标{target.EntityID}，攻击量{attackAmount:F1}，残留{resolution.RemainingAttackAmount:F1}）");
        return true;
    }

    private static bool HasAura(BattleEntity target, string element)
    {
        ElementalAura aura = target.GetAura(element);
        return aura != null && aura.AuraAmount > 0f;
    }

    private static bool HasBurningWithExcessDendro(BattleEntity target)
    {
        ElementalAura dendro = target != null ? target.GetAura("Dendro") : null;
        return BurningReactionHandler.IsBurning(target)
            && dendro != null
            && dendro.AuraAmount > 0f;
    }

    private static float GetCryoAmount(ReactionContext context)
    {
        BattleEntity target = context.Target;
        ElementalAura aura = target.GetAura("Cryo");
        float normalCryo = aura != null ? aura.AuraAmount : 0f;
        return normalCryo + (context.IgnoreFrozenAura ? 0f : FrozenReactionHandler.GetFrozenAuraAsCryo(target));
    }

    private static void ConsumeCryo(ReactionContext context, float amount)
    {
        BattleEntity target = context.Target;
        float remaining = amount;
        ElementalAura aura = target.GetAura("Cryo");
        if (aura != null)
        {
            float consumed = Mathf.Min(aura.AuraAmount, remaining);
            target.ConsumeAura("Cryo", consumed);
            remaining -= consumed;
        }

        if (remaining > 0f && !context.IgnoreFrozenAura)
            FrozenReactionHandler.ConsumeFrozenAura(target, remaining);
    }
}
