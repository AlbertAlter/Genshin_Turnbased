using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>状态由施加者对应的角色/敌人处理器执行，共享计时和正式事件入口。</summary>
public static class StatusActionDispatch
{
    internal static bool TryPrepare(StatusInstance status, StatusActionData action, ScriptHookContext context,
        StatusActionRuntimeState runtime, bool continueStatusCall = false)
    {
        var battle = BattleManager.Instance;
        int turn = battle != null ? battle.TurnCount : 0;
        int phase = battle != null ? (int)battle.CurrentPhase : 0;
        if (runtime.LimitReached(action, turn, phase)
            || BattleResolutionContext.LimitReached(status, action, context, continueStatusCall)) return false;
        return ScriptHookEvaluator.Evaluate(action.ScriptHook, context)
            && BattleResolutionContext.TryCollect(status, action, context, continueStatusCall);
    }

    public static bool HasExecutor(BattleEntity caster)
        => caster != null && (caster.CharacterCtrl != null || caster.EnemyCtrl != null);

    public static IEnumerator ExecuteAsync(StatusInstance status, List<StatusActionData> actions, object host, string type)
        => ExecuteAsync(status, actions, host, type, null);

    internal static IEnumerator ExecuteAsync(StatusInstance status, List<StatusActionData> actions, object host, string type,
        BattleStatusPromptOccurrence prompts)
        => BattleExecution.Wait(HasExecutor(status.Caster)
            ? BattleExecution.Run(() => ExecuteTask(status, actions, host, type, prompts)) : Task.CompletedTask);

    internal static Task ExecuteTask(StatusInstance status, List<StatusActionData> actions, object host, string type,
        BattleStatusPromptOccurrence prompts)
    {
        if (status.Caster?.CharacterCtrl != null)
            return status.Caster.CharacterCtrl.ExecuteStatusActionsTask(status, actions, host, type, prompts);
        if (status.Caster?.EnemyCtrl != null)
            return status.Caster.EnemyCtrl.ExecuteStatusActionsTask(status, actions, host, type, prompts);
        return Task.CompletedTask;
    }

    public static void Enqueue(StatusInstance status, List<StatusActionData> actions, object host, string type)
    {
        if (status.Caster?.CharacterCtrl != null) status.Caster.CharacterCtrl.ExecuteStatusActions(status, actions, host, type);
        else if (status.Caster?.EnemyCtrl != null)
            status.Caster.EnemyCtrl.ExecuteStatusActions(status, actions, host, type);
    }
}
