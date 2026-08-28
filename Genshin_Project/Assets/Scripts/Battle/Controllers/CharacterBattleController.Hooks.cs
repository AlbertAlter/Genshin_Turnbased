using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class CharacterBattleController
{
    private static readonly HashSet<string> ExecutingStatusActions = new HashSet<string>();
    /// <summary>
    /// 事件类状态行动统一执行入口（2026-08-19，状态钩子任务）：
    /// StatusOnHitHookSystem（OnHit）与 KillHookSystem（Kill）共用，触发限制代码不复制。
    /// 固定顺序：
    ///   1. 检查状态和行动是否有效；
    ///   2. 调用 ScriptHookEvaluator.Evaluate（OnHit/Kill 走带上下文接口）；
    ///   3. 检查 MaxTimePerTurn；4. 检查 MaxTimePerLife；5. 检查 Cooldown；
    ///   6. 计数；7. 执行 Param1 中的效果；8. 返回是否真正执行。
    /// </summary>
    public bool TryExecuteEventStatusAction(
        StatusInstance status,
        StatusActionData action,
        object host,
        ScriptHookContext context)
    {
        if (status == null || action == null) return false;
        string actionKey = !string.IsNullOrEmpty(action.ActionKey)
            ? action.ActionKey
            : $"legacy:{action.StatusID2}:{action.ActionType}:{action.Param1}:{action.ScriptHook}";
        string key = $"{status.GetHashCode()}:{actionKey}";
        if (!ExecutingStatusActions.Add(key)) return false;
        try
        {
            if (!ScriptHookEvaluator.Evaluate(action.ScriptHook, context)) return false;
            if (StatusActionLimitReached(status, action)) return false;
            CountStatusActionTrigger(status, action);
            ExecuteStatusActionEffects(status, action, host);
            LogManager.Log(LogCategory.StatusAction, $"事件状态行动执行 {status.StatusID2} {action.ActionType} (hook={action.ScriptHook})");
            return true;
        }
        finally
        {
            ExecutingStatusActions.Remove(key);
        }
    }

/// <summary>状态施加时立刻生效（OnApply）：由状态施放者控制器调用（实体/位置通用）。</summary>
    public void OnStatusApplied(StatusInstance inst, object host)
    {
        if (inst == null) return;
        var dm = DataManager.Instance;
        if (dm == null || !dm.StatusActionDict.TryGetValue(inst.StatusID2, out var actions)) return;
        ExecuteStatusActions(inst, actions, host, "OnApply");
    }

/// <summary>
    /// PreAlliesDamage 钩子触发行执行（2026-08-14，PreDamageHookSystem.TriggerPreDamageHooks 调用）：
    /// 执行该行的 Param1 效果（如凯亚冰棱 SE_Kaeya_BurstTrigger），先于主动行为实行；
    /// 触发前检查次数限制（MaxTimePerTurn 等），触发后计数。
    /// </summary>
    public void ExecutePreDamageHookLine(StatusActionData act, BattleEntity caster)
    {
        if (act == null) return;
        var inst = caster != null ? caster.GetStatus(act.StatusID2) : null;
        if (StatusActionLimitReached(inst, act)) return;
        CountStatusActionTrigger(inst, act);
        ExecuteStatusActionEffects(inst, act, caster);
    }
}
