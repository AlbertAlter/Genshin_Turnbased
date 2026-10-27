using System.Collections.Generic;

/// <summary>Self单位免费切至前台。只委托统一换人入口，不另发AP或行动声明。</summary>
public static class SwitchInEffect
{
    public static bool Execute(IReadOnlyList<BattleEntity> targets)
    {
        if (targets == null || targets.Count != 1 || BattleExecution.IsCancelled) return false;
        return BattleManager.Instance?.TrySwitchInFromEffect(targets[0]) == true;
    }
}
