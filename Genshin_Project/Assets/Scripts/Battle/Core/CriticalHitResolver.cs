/// <summary>一次最终暴击判定的完整结果，供伤害结算和日志共用，避免重复抽取。</summary>
public struct CriticalHitResult
{
    public float DamageBeforeCritical;
    public float DamageAfterCritical;
    public float CriticalRate;
    public float Roll;
    public float Multiplier;
    public bool IsCritical;
}

/// <summary>
/// 直伤的最终暴击入口。调用方应先完成基础伤害、增伤、防御、抗性与元素反应计算，
/// 再为每个 hit、每个实际目标分别调用一次，随后才进入护盾与扣血。
/// </summary>
public static class CriticalHitResolver
{
    public static CriticalHitResult Resolve(
        float damageBeforeCritical,
        float criticalRate,
        float criticalDamageBonus)
    {
        float clampedRate = criticalRate;
        if (clampedRate < 0f) clampedRate = 0f;
        if (clampedRate > 1f) clampedRate = 1f;

        float roll = BattleRandom.NextFloat01();
        bool isCritical = roll < clampedRate;
        float multiplier = isCritical ? 1f + criticalDamageBonus : 1f;

        return new CriticalHitResult
        {
            DamageBeforeCritical = damageBeforeCritical,
            DamageAfterCritical = damageBeforeCritical * multiplier,
            CriticalRate = clampedRate,
            Roll = roll,
            Multiplier = multiplier,
            IsCritical = isCritical
        };
    }
}
