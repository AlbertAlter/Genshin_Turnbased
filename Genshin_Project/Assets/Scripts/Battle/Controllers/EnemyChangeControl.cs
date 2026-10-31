using System;

/// <summary>敌人专用ChangeControl：All禁止所有分配，Extra只禁止额外随机分配；两者均保留贡献。</summary>
public static class EnemyChangeControl
{
    public static bool IsFrozen(BattleEntity enemy)
        => HasFreeze(enemy, "All");

    public static bool IsExtraFrozen(BattleEntity enemy)
        => HasFreeze(enemy, "Extra");

    private static bool HasFreeze(BattleEntity enemy, string scope)
    {
        if (enemy == null || DataManager.Instance == null) return false;
        foreach (var status in enemy.GetEffectiveStatusList())
        {
            if (Freezes(status, scope)) return true;
        }
        return false;
    }

    public static bool Freezes(StatusInstance status, string scope = "All")
    {
        if (status?.IsActive != true || status.MainData == null || DataManager.Instance == null) return false;
        foreach (var effect in DataManager.Instance.StatusEffectDict.Values)
            if (effect.StatusEffectID / 100 == status.MainData.StatusID && effect.EffectType == "ChangeControl"
                && effect.Param1 == scope && effect.Param2 == "Freeze") return true;
        return false;
    }
}
