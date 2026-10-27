using System.Collections;
using System.Collections.Generic;

/// <summary>状态由施加者对应的角色/敌人处理器执行，共享计时和正式事件入口。</summary>
public static class StatusActionDispatch
{
    public static bool HasExecutor(BattleEntity caster)
        => caster != null && (caster.CharacterCtrl != null || caster.EnemyCtrl != null);

    public static IEnumerator ExecuteAsync(StatusInstance status, List<StatusActionData> actions, object host, string type)
    {
        if (status.Caster?.CharacterCtrl != null)
            return status.Caster.CharacterCtrl.ExecuteStatusActionsAsync(status, actions, host, type);
        if (status.Caster?.EnemyCtrl != null)
            return status.Caster.EnemyCtrl.ExecuteStatusActionsAsync(status, actions, host, type);
        return Empty();
    }

    public static void Enqueue(StatusInstance status, List<StatusActionData> actions, object host, string type)
    {
        if (status.Caster?.CharacterCtrl != null) status.Caster.CharacterCtrl.ExecuteStatusActions(status, actions, host, type);
        else if (status.Caster?.EnemyCtrl != null)
            foreach (var action in actions)
                if (action.ActionType == type && !ScriptHookEvaluator.IsEventDriven(action.ScriptHook))
                    status.Caster.EnemyCtrl.TryExecuteEventStatusAction(status, action, host,
                        new ScriptHookContext { Caster = status.Caster, Target = host as BattleEntity });
    }
    private static IEnumerator Empty() { yield break; }
}
