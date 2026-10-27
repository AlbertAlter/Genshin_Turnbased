using System;

/// <summary>敌人专用ChangeControl。冻结普通行动分配，不改变共享池的已有贡献；状态结束恢复资格。</summary>
public static class EnemyChangeControl
{
    public static bool IsFrozen(BattleEntity enemy)
    {
        if (enemy == null || DataManager.Instance == null) return false;
        foreach (var status in enemy.GetEffectiveStatusList())
        {
            if (Freezes(status)) return true;
        }
        return false;
    }

    public static bool Freezes(StatusInstance status)
    {
        if (status?.IsActive != true || status.MainData == null || DataManager.Instance == null) return false;
        foreach (var effect in DataManager.Instance.StatusEffectDict.Values)
            if (effect.StatusEffectID / 100 == status.MainData.StatusID && effect.EffectType == "ChangeControl"
                && effect.Param1 == "All" && effect.Param2 == "Freeze") return true;
        return false;
    }
}
