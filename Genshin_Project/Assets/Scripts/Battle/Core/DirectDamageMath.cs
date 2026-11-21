using UnityEngine;

internal static class DirectDamageMath
{
    internal static float CharacterDefenseMultiplier(BattleEntity target, float sourceLevel, float reduction, float ignored)
    {
        if (target.Type == BattleEntity.EntityType.Enemy)
        {
            float attacker = sourceLevel + 100f;
            float defender = (target.Level + 100f) * (1f - reduction) * (1f - ignored);
            return attacker / (attacker + defender);
        }

        float effectiveDef = target.GetFinalDEF() * (1f - reduction) * (1f - ignored);
        if (effectiveDef < 0f) effectiveDef = 0f;
        float multiplier = 1f - effectiveDef / (effectiveDef + 5f * sourceLevel + 500f);
        return Mathf.Clamp(multiplier, 0.1f, 1f);
    }

    internal static float EnemyDefenseMultiplier(float targetDef, float sourceLevel)
    {
        float multiplier = 1f - targetDef / (targetDef + 5f * sourceLevel + 500f);
        return multiplier < 0f ? 0f : multiplier;
    }
}
