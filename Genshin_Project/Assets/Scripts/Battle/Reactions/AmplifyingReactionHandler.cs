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

        if (context.AttackElement == "Pyro" && HasAura(context, "Hydro"))
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
                 && !HasBurningWithExcessDendro(context)
                 && GetPyroAmount(context) > 0f)
        {
            // 蒸发固定水:火=1:2。此处水为攻击元素，火为目标附着。
            float pyroAmount = GetPyroAmount(context);
            float consumedHydro = Mathf.Min(attackAmount, pyroAmount / 2f);
            float consumedPyro = consumedHydro * 2f;
            if (consumedHydro <= 0f) return false;

            ConsumePyro(context, consumedPyro);
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
                 && GetPyroAmount(context) > 0f)
        {
            // 融化固定火:冰=1:2。此处冰为攻击元素，火为目标附着。
            float pyroAmount = GetPyroAmount(context);
            float consumedPyro = Mathf.Min(pyroAmount, attackAmount / 2f);
            float consumedCryo = consumedPyro * 2f;
            if (consumedPyro <= 0f) return false;

            ConsumePyro(context, consumedPyro);
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

    private static bool HasAura(ReactionContext context, string element)
    {
        if (!context.AllowsReactionPool(element, ReactionPoolKind.NormalAura)) return false;
        ElementalAura aura = context.Target.GetAura(element);
        return aura != null && aura.AuraAmount > 0f;
    }

    private static bool HasBurningWithExcessDendro(ReactionContext context)
    {
        BattleEntity target = context.Target;
        ElementalAura dendro = target != null ? target.GetAura("Dendro") : null;
        return string.IsNullOrEmpty(context.RestrictedAuraElement)
            && BurningReactionHandler.IsBurning(target)
            && dendro != null
            && dendro.AuraAmount > 0f;
    }

    private static float GetPyroAmount(ReactionContext context)
    {
        float amount = 0f;
        if (context.AllowsReactionPool("Pyro", ReactionPoolKind.NormalAura))
            amount += context.Target.GetAura("Pyro")?.AuraAmount ?? 0f;
        if (context.AllowsReactionPool("Pyro", ReactionPoolKind.Burning))
            amount += BurningReactionHandler.GetBurningAuraAsPyro(context.Target);
        return amount;
    }

    private static void ConsumePyro(ReactionContext context, float amount)
    {
        if (context.RestrictedAuraKind == ReactionPoolKind.Burning)
            BurningReactionHandler.ConsumeBurningAura(context.Target, amount);
        else if (context.RestrictedAuraKind == ReactionPoolKind.NormalAura)
            context.Target.ConsumeAura("Pyro", amount);
        else
            BurningReactionHandler.ConsumePyroIncludingBurning(context.Target, amount);
    }

    private static float GetCryoAmount(ReactionContext context)
    {
        BattleEntity target = context.Target;
        ElementalAura aura = target.GetAura("Cryo");
        float normalCryo = context.AllowsReactionPool("Cryo", ReactionPoolKind.NormalAura) && aura != null
            ? aura.AuraAmount : 0f;
        float frozenCryo = !context.IgnoreFrozenAura
            && context.AllowsReactionPool("Cryo", ReactionPoolKind.Frozen)
            ? FrozenReactionHandler.GetFrozenAuraAsCryo(target) : 0f;
        return normalCryo + frozenCryo;
    }

    private static void ConsumeCryo(ReactionContext context, float amount)
    {
        BattleEntity target = context.Target;
        float remaining = amount;
        ElementalAura aura = target.GetAura("Cryo");
        if (context.AllowsReactionPool("Cryo", ReactionPoolKind.NormalAura) && aura != null)
        {
            float consumed = Mathf.Min(aura.AuraAmount, remaining);
            target.ConsumeAura("Cryo", consumed);
            remaining -= consumed;
        }

        if (remaining > 0f && !context.IgnoreFrozenAura
            && context.AllowsReactionPool("Cryo", ReactionPoolKind.Frozen))
            FrozenReactionHandler.ConsumeFrozenAura(target, remaining);
    }
}
