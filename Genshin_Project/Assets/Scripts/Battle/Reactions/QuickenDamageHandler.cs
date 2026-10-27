public struct QuickenDamageResolution
{
    public ReactionType Type;
    public string DisplayName;
    public float FinalDamage;
}

/// <summary>原激化目标受到有元素量的雷/草命中时，改用超激化或蔓激化公式。</summary>
public static class QuickenDamageHandler
{
    public static bool CanResolve(ReactionContext context)
    {
        return context != null
            && context.CanTriggerReaction
            && context.Target != null
            && context.AttackAmount > 0f
            && context.DamageComponents != null
            && (context.AttackElement == "Electro" || context.AttackElement == "Dendro")
            && ReactionStateSystem.TryGetEntityState(
                context.Target,
                ReactionType.Quicken,
                out _);
    }

    public static bool TryResolve(
        ReactionContext context,
        float levelCoefficient,
        out QuickenDamageResolution resolution)
    {
        resolution = default;
        if (!CanResolve(context)) return false;

        ReactionType type;
        string displayName;
        if (context.AttackElement == "Electro")
        {
            type = ReactionType.Aggravate;
            displayName = "超激化";
        }
        else if (context.AttackElement == "Dendro")
        {
            type = ReactionType.Spread;
            displayName = "蔓激化";
        }
        else
        {
            return false;
        }

        ReactionDamageComponents components = context.DamageComponents;
        if (components.Properties.UsesSpecialFormula)
        {
            resolution = new QuickenDamageResolution
            { Type = type, DisplayName = displayName, FinalDamage = context.PreReactionDamage };
            return true; // 保留真实反应事实，但不使用普通激化公式改写特殊技能伤害。
        }
        float reactionDamageBonus = ReactionDamageCalculator.CollectExclusiveReactionDamageBonus(
            context.SourceEntity,
            type,
            context.AttackElement);
        float damageBonusMultiplier = components.DamageBonusMultiplier + reactionDamageBonus;
        bool sourceIsEnemy = context.SourceKind == ReactionSourceKind.EnemySkill
            || (context.SourceEntity != null
                && context.SourceEntity.Type == BattleEntity.EntityType.Enemy);
        float baseDamageBonus = sourceIsEnemy ? 0f : context.SourceSnapshotOverride != null
            ? context.SourceSnapshotOverride.BaseDMGBonus
            : ReactionDamageCalculator.CollectReactionBaseDamageBonus(context.SourceEntity, type, context.AttackElement);

        float finalDamage = sourceIsEnemy
            ? ReactionDamageCalculator.CalculateEnemyQuicken(
                components.SkillBaseDamage,
                levelCoefficient,
                type,
                damageBonusMultiplier,
                components.DefenseMultiplier,
                components.ResistanceMultiplier)
            : ReactionDamageCalculator.CalculateCharacterQuicken(
                components.SkillBaseDamage,
                levelCoefficient,
                type,
                ReactionDamageCalculator.GetSourceMastery(context),
                damageBonusMultiplier,
                components.DefenseMultiplier,
                components.ResistanceMultiplier,
                baseDamageBonus);

        resolution = new QuickenDamageResolution
        {
            Type = type,
            DisplayName = displayName,
            FinalDamage = finalDamage
        };
        return true;
    }
}
