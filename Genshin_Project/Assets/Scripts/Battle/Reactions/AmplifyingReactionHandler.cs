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
            float hydroAmount = ElementQuery.GetReactionAmount(context, "Hydro");
            float consumedHydro = Mathf.Min(hydroAmount, attackAmount / 2f);
            float consumedPyro = consumedHydro * 2f;
            if (consumedHydro <= 0f) return false;

            ElementAuraSystem.ConsumeReactionAura(context, "Hydro", consumedHydro);
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
        if (context.DamageComponents?.Properties.UsesSpecialFormula == true)
        {
            resolution.FinalDamage = context.PreReactionDamage;
            return true; // 只提交真实融化/蒸发，不把倍率、精通括号再乘到特殊技能伤害上。
        }
        float baseDamageBonus = sourceIsEnemy ? 0f : context.SourceSnapshotOverride != null
            ? context.SourceSnapshotOverride.BaseDMGBonus
            : ReactionDamageCalculator.CollectReactionBaseDamageBonus(context.SourceEntity, resolution.Type, context.AttackElement);
        if (!sourceIsEnemy) baseDamageBonus += ReactionDamageCalculator.CollectLiveTargetBonuses(
            context.SourceEntity, target, resolution.Type, context.AttackElement).BaseDMGBonus;
        resolution.FinalDamage = sourceIsEnemy
            ? ReactionDamageCalculator.CalculateEnemyAmplifying(
                context.PreReactionDamage,
                reactionMultiplier)
            : ReactionDamageCalculator.CalculateCharacterAmplifying(
                context.PreReactionDamage,
                reactionMultiplier,
                ReactionDamageCalculator.GetSourceMastery(context),
                baseDamageBonus);

        LogManager.Log(
            LogCategory.Reaction,
            $"{resolution.DisplayName}：{context.PreReactionDamage:F1} → {resolution.FinalDamage:F1}" +
            $"（目标{target.EntityID}，攻击量{attackAmount:F1}，残留{resolution.RemainingAttackAmount:F1}）");
        return true;
    }

    private static bool HasAura(ReactionContext context, string element)
    {
        return ElementQuery.ReadReactionAura(context, element)?.IsPresent == true;
    }

    private static bool HasBurningWithExcessDendro(ReactionContext context)
    {
        BattleEntity target = context.Target;
        ElementPoolReading dendro = ElementQuery.ReadAura(target, "Dendro");
        return string.IsNullOrEmpty(context.RestrictedAuraElement)
            && BurningReactionHandler.IsBurning(target)
            && dendro != null
            && dendro.IsPresent;
    }

    private static float GetPyroAmount(ReactionContext context)
        => ElementQuery.GetReactionAmount(context, "Pyro");

    private static void ConsumePyro(ReactionContext context, float amount)
    {
        ElementAuraSystem.ConsumeReactionAura(context, "Pyro", amount);
    }

    private static float GetCryoAmount(ReactionContext context)
        => ElementQuery.GetReactionAmount(context, "Cryo");

    private static void ConsumeCryo(ReactionContext context, float amount)
    {
        ElementAuraSystem.ConsumeReactionAura(context, "Cryo", amount);
    }
}
