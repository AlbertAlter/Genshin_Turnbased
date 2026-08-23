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
        float reactionDamageBonus = ReactionDamageCalculator.CollectExclusiveReactionDamageBonus(
            context.SourceEntity,
            type,
            context.AttackElement);
        float damageBonusMultiplier = components.DamageBonusMultiplier + reactionDamageBonus;
        bool sourceIsEnemy = context.SourceKind == ReactionSourceKind.EnemySkill
            || (context.SourceEntity != null
                && context.SourceEntity.Type == BattleEntity.EntityType.Enemy);

        float finalDamage = sourceIsEnemy
            ? ReactionDamageCalculator.CalculateEnemyQuicken(
                components.SkillBaseDamage,
                levelCoefficient,
                type,
                components.CriticalMultiplier,
                damageBonusMultiplier,
                components.DefenseMultiplier,
                components.ResistanceMultiplier)
            : ReactionDamageCalculator.CalculateCharacterQuicken(
                components.SkillBaseDamage,
                levelCoefficient,
                type,
                context.SourceEntity != null ? context.SourceEntity.TotalEM : 0f,
                components.CriticalMultiplier,
                damageBonusMultiplier,
                components.DefenseMultiplier,
                components.ResistanceMultiplier);

        resolution = new QuickenDamageResolution
        {
            Type = type,
            DisplayName = displayName,
            FinalDamage = finalDamage
        };
        return true;
    }
}
