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
    /// 同层先统一判定并预留次数，再按施加顺序执行；直接调用返回执行结果，
    /// 收集期间调用返回是否已提交候选。概率条件只在收集确认时求值一次。
    /// </summary>
    public bool TryExecuteEventStatusAction(
        StatusInstance status,
        StatusActionData action,
        object host,
        ScriptHookContext context, Func<bool> isStillValid = null)
    {
        if (status == null || action == null || action.ActionType == "OnDestroy" || action.ActionType == "OnDeath"
            || status.DeathCleanupPending
            || !EquipmentStatusLifetime.CanExecute(status)) return false;
        if (context != null && context.Declaration == null)
            context.Declaration = context.DamageEvent?.Declaration
                ?? BattleEventHub.Find(context.HealEvent) ?? BattleEventHub.Find(context.ReactionEvent)
                ?? BattleEventHub.Current;
        // 事件数据独立留存；实体仍为实时引用，不能快照属性或提前计算响应数值。
        context = context?.Snapshot();
        return BattleResolutionContext.Submit(status.ApplyOrder,
            () => PrepareEventStatusAction(status, action, host, context, isStillValid));
    }

    internal bool TryExecuteRemovedStatusAction(StatusRemovalActions.Request request, StatusActionData action)
        => BattleResolutionContext.Submit(request.Status.ApplyOrder,
            () => PrepareEventStatusAction(request.Status, action, request.Host, request.Context, null, request));

    internal bool TryExecuteDeathStatusAction(StatusDeathActions.Request request, StatusActionData action)
        => BattleResolutionContext.Submit(request.Status.ApplyOrder,
            () => PrepareEventStatusAction(request.Status, action, request.Host, request.Context, null, null, request));

    BattleResolutionContext.PreparedTrigger PrepareEventStatusAction(
        StatusInstance status, StatusActionData action, object host, ScriptHookContext context, Func<bool> isStillValid,
        StatusRemovalActions.Request removal = null, StatusDeathActions.Request death = null)
    {
        using var deathScope = death?.Enter();
        bool Valid() => (death != null ? death.CanExecute
            : (removal != null ? removal.CanExecute : status.IsActive && !status.DeathCleanupPending)
                && EquipmentStatusLifetime.CanExecute(status)) && (isStillValid == null || isStillValid());
        string key = $"{status.GetHashCode()}:{BattleResolutionContext.ActionKey(action)}";
        bool limited = BattleResolutionContext.HasApplicableLimit(action);
        // 未配置新限制的旧行保留重入保护；配置 N 次的行使用整条连锁额度。
        if (!Valid() || (!limited && ExecutingStatusActions.Contains(key))) return null;
        if (StatusActionLimitReached(status, action)
            || BattleResolutionContext.LimitReached(status, action, context)) return null;
        // 治疗候选只能在整段正式提交后确认，不能把预计算数据当作已经发生的事件。
        if (context?.HealEvent != null && !context.HealEvent.IsCommitted) return null;
        if (!ScriptHookEvaluator.Evaluate(action.ScriptHook, context)) return null;
        if (!BattleResolutionContext.TryCollect(status, action, context)) return null;
        StatusActionRuntimeState runtime = GetStatusActionRuntime(status, action);
        runtime.PendingTriggerCount++;
        bool pending = true;
        Action release = () =>
        {
            if (!pending) return;
            pending = false;
            runtime.PendingTriggerCount--;
        };
        return new BattleResolutionContext.PreparedTrigger(async () =>
        {
            using var deathExecution = death?.Enter();
            // 不重复判断触发条件/概率；状态移除后取消请求，释放总次数预留。
            if (!Valid()) return;
            using var triggerDepth = BattleResolutionContext.EnterTrigger();
            bool ownsGuard = ExecutingStatusActions.Add(key);
            try
            {
                await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Status,
                    status.Caster, action.Param1, statusID: status.StatusID2), () =>
                {
                    if (!Valid()) return;
                    release();
                    CountStatusActionTrigger(status, action);
                    if (context != null && !string.IsNullOrEmpty(context.EventHookArgument)
                        && context.EventHookName != null && context.EventHookName.StartsWith("Post")
                        && context.ActionTargetPositions != null)
                        PostDamageHookSystem.SetPositions(context.EventHookArgument,
                            new List<int>(context.ActionTargetPositions));
                    if (death != null) BattleExecution.Enqueue(async () =>
                    {
                        using var dying = death.Enter();
                        if (death.CanExecute) await ExecuteStatusActionEffectsAsync(status, action, host, context);
                    });
                    else if (removal == null) ExecuteStatusActionEffects(status, action, host, context);
                    else BattleExecution.Enqueue(async () =>
                    {
                        if (removal.CanExecute) await ExecuteStatusActionEffectsAsync(status, action, host, context);
                    });
                });
                LogManager.Log(LogCategory.StatusAction,
                    $"事件状态行动执行 {status.StatusID2} {action.ActionType} (hook={action.ScriptHook})");
            }
            finally
            {
                if (ownsGuard) ExecutingStatusActions.Remove(key);
            }
        }, release);
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
        using var resolutionEffect = BattleResolutionContext.BeginEffect();
        if (StatusActionLimitReached(inst, act)) return;
        if (!BattleResolutionContext.TryCollect(inst, act)) return;
        CountStatusActionTrigger(inst, act);
        ExecuteStatusActionEffects(inst, act, caster);
    }
}
