using System;

/// <summary>肉鸽独立入场规则；由战斗适配器在面板/装备初始化完成后、正式开战前调用。</summary>
public static class RoguelikeBattleEntryRules
{
    public static float GetInitialEnergy(float maxEnergy)
    {
        if (float.IsNaN(maxEnergy) || float.IsInfinity(maxEnergy) || maxEnergy < 0f)
            throw new ArgumentOutOfRangeException(nameof(maxEnergy));
        return maxEnergy * 0.5f;
    }

    /// <summary>对本次构建的我方角色调用。不识别任何角色 ID，MaxEnergy=0 时自然初始化为 0。</summary>
    public static void Apply(BattleEntity entity)
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        float energy = GetInitialEnergy(entity.MaxEnergy);
        entity.CurrentHP = entity.TotalHP;
        entity.CurrentEnergy = energy;
    }
}
