using System.Collections;
using System.Collections.Generic;

/// <summary>登记状态所在的阶段，按施加顺序执行阶段行动、计时和自然到期。</summary>
internal sealed class BattleStatusPhaseScheduler
{
    private readonly Dictionary<int, List<(StatusInstance inst, object host)>> _statusByPhase
        = new Dictionary<int, List<(StatusInstance inst, object host)>>();
    private readonly Dictionary<int, List<(StatusInstance inst, object host)>> _triggerByPhase
        = new Dictionary<int, List<(StatusInstance inst, object host)>>();

    /// <summary>
    /// 状态施加登记：按 AddInPhase 放进计时桶；若该状态有 OnItsTurn 行动行且 TriggerPhase>0，
    /// 再按 TriggerPhase 放进触发桶。桶内追加顺序 = 施加顺序 = 大顺序。
    /// </summary>
    internal void Register(StatusInstance inst, object host)
    {
        if (inst == null) return;
        if (inst.RemainingPhaseCount <= 0)
            inst.IsPermanent = true;
        var item = (inst, host);

        if (!_statusByPhase.TryGetValue(inst.AddInPhase, out var tickList))
        {
            tickList = new List<(StatusInstance, object)>();
            _statusByPhase[inst.AddInPhase] = tickList;
        }
        if (!tickList.Exists(entry => entry.inst == inst))
            tickList.Add(item);

        if (inst.TriggerPhase > 0)
        {
            var actions = GetStatusActions(inst.StatusID2);
            bool hasOnItsTurn = false;
            if (actions != null)
            {
                foreach (var act in actions)
                    if (act.ActionType == "OnItsTurn") { hasOnItsTurn = true; break; }
            }
            if (hasOnItsTurn)
            {
                if (!_triggerByPhase.TryGetValue(inst.TriggerPhase, out var trigList))
                {
                    trigList = new List<(StatusInstance, object)>();
                    _triggerByPhase[inst.TriggerPhase] = trigList;
                }
                if (!trigList.Exists(entry => entry.inst == inst))
                    trigList.Add(item);
            }
            else
            {
                LogManager.LogWarning(LogCategory.Status,
                    $"{inst.StatusID2} 配置了 TriggerPhase={inst.TriggerPhase}，但没有 OnItsTurn 行动；阶段不会调用 OnTrigger。");
            }
        }
    }

    /// <summary>状态注销（到期/移除/战斗结束）：从计时桶和触发桶移除（按实例引用，幂等）。</summary>
    internal void Unregister(StatusInstance inst)
    {
        if (inst == null) return;
        if (_statusByPhase.TryGetValue(inst.AddInPhase, out var tickList))
            tickList.RemoveAll(entry => entry.inst == inst);
        if (inst.TriggerPhase > 0 && _triggerByPhase.TryGetValue(inst.TriggerPhase, out var trigList))
            trigList.RemoveAll(entry => entry.inst == inst);
    }

    internal void Clear()
    {
        _statusByPhase.Clear();
        _triggerByPhase.Clear();
    }

    internal IEnumerator ExecutePhase(TurnPhase phase, int turn)
    {
        var prompts = new BattleStatusPromptOccurrence();
        yield return ExecuteTriggerSet(phase, prompts);
        yield return ExecuteTimerSet(phase, turn, prompts);
    }

    /// <summary>
    /// 触发集合（2026-08-12 以状态为中心）：
    /// 状态施加时已按 TriggerPhase 登记到触发桶，这里直接取本阶段桶，
    /// 按施加顺序（大顺序）逐个执行 OnItsTurn，不调用事件行动 OnTrigger。
    /// 结算顺序：触发集合整体在计时器集合之前。
    /// </summary>
    private IEnumerator ExecuteTriggerSet(TurnPhase phase, BattleStatusPromptOccurrence prompts)
    {
        if (!_triggerByPhase.TryGetValue((int)phase, out var list) || list.Count == 0) yield break;
        var scheduled = new List<(StatusInstance inst, object host)>(list);
        scheduled.Sort((left, right) => left.inst.ApplyOrder.CompareTo(right.inst.ApplyOrder));
        foreach (var item in scheduled)
        {
            if (!StatusHostAccess.Contains(item.host, item.inst)) continue;
            var actions = GetStatusActions(item.inst.StatusID2);
            if (actions == null) continue;
            if (StatusActionDispatch.HasExecutor(item.inst.Caster))
            {
                LogManager.Log(LogCategory.Status, $"OnItsTurn {item.inst.StatusID2} on {item.host} at phase {(int)phase}");
                yield return StatusActionDispatch.ExecuteAsync(item.inst, actions, item.host, "OnItsTurn", prompts);
            }
        }
    }

    /// <summary>
    /// 计时器集合：状态 AddInPhase 等于当前阶段时，剩余回合-1；
    /// 归零后触发 OnEnd 并移除该状态。
    /// 2026-08-12 改造：以状态为中心——不管施加给敌方/我方/位置，只要是本阶段要结算的状态，
    /// 全部收进一个大顺序（ApplyOrder 全局施加顺序）；未指定 TriggerPhase 的状态在此执行 OnItsTurn，
    /// 到期状态按同样顺序统一执行 OnEnd 并移除。
    /// </summary>
    private IEnumerator ExecuteTimerSet(TurnPhase phase, int turn, BattleStatusPromptOccurrence prompts)
    {
        // 直接取本阶段登记的状态列表（施加时已按 AddInPhase 登记，列表顺序 = 施加顺序 = 大顺序）
        if (!_statusByPhase.TryGetValue((int)phase, out var list) || list.Count == 0) yield break;

        // 本阶段统一 -1 回合 + OnItsTurn（严格按大顺序），收集到期
        var expired = new List<(StatusInstance inst, object host)>();
        var scheduled = new List<(StatusInstance inst, object host)>(list);
        scheduled.Sort((left, right) => left.inst.ApplyOrder.CompareTo(right.inst.ApplyOrder));
        foreach (var item in scheduled)
        {
            if (!StatusHostAccess.Contains(item.host, item.inst)) continue;
            if (turn < item.inst.EarliestTimerTurn) continue;
            if (!item.inst.IsPermanent)
                item.inst.RemainingPhaseCount--;
            // 显式 TriggerPhase 已在触发集合执行 OnItsTurn，不能在计时阶段再执行一次。
            // 未配置触发阶段的旧状态仍在 AddInPhase 执行自己的回合行动。
            if (item.inst.TriggerPhase <= 0 && StatusActionDispatch.HasExecutor(item.inst.Caster))
            {
                var acts = GetStatusActions(item.inst.StatusID2);
                if (acts != null)
                {
                    yield return StatusActionDispatch.ExecuteAsync(item.inst, acts, item.host, "OnItsTurn", prompts);
                }
            }
            if (!item.inst.IsPermanent && item.inst.RemainingPhaseCount <= 0)
                expired.Add(item);
        }

        // 到期统一处理（同样按大顺序）：OnEnd → 子状态清理 → 移除 + 注销
        if (expired.Count == 0) yield break;
        var removedNames = new List<string>();
        var batches = new Dictionary<long, List<(StatusInstance inst, object host, List<StatusActionData> actions)>>();
        foreach (var item in expired)
        {
            var status = item.inst;
            long source = status.SourceEffectExecutionId;
            if (source == 0) continue;
            var actions = GetStatusActions(status.StatusID2);
            if (actions == null || !actions.Exists(action => action.ActionType == "OnEnd")) continue;
            if (!batches.TryGetValue(source, out var members))
                batches.Add(source, members = new List<(StatusInstance, object, List<StatusActionData>)>());
            members.Add((status, item.host, actions));
        }
        foreach (var item in expired)
        {
            if (!IsDue(item)) continue;
            LogManager.Log(LogCategory.Status, $"{item.inst.StatusID2} on {item.host} expired at phase {(int)phase}");
            var actions = GetStatusActions(item.inst.StatusID2);
            long source = item.inst.SourceEffectExecutionId;
            if (batches.TryGetValue(source, out var members) && actions != null && actions.Exists(action => action.ActionType == "OnEnd"))
            {
                batches.Remove(source);
                yield return BattleExecution.Wait(StatusExpiryBatch.Run(members, member => RemoveExpired(member, removedNames), prompts));
                continue;
            }
            if (actions != null && StatusActionDispatch.HasExecutor(item.inst.Caster))
                yield return StatusActionDispatch.ExecuteAsync(item.inst, actions, item.host, "OnEnd", prompts);
            RemoveExpired(item, removedNames);
        }
        if (removedNames.Count > 0)
            LogManager.Log(LogCategory.Status, $"消失状态清单 (phase {(int)phase}): {string.Join(", ", removedNames)}");
    }

    internal static bool IsDue((StatusInstance inst, object host) item)
        => item.inst.IsActive && !item.inst.IsPermanent && item.inst.RemainingPhaseCount <= 0
            && StatusHostAccess.Contains(item.host, item.inst);

    private void RemoveExpired((StatusInstance inst, object host) item, List<string> removedNames)
    {
        if (!IsDue(item)) return;
        if (StatusHostAccess.Remove(item.host, item.inst, "Expired")) removedNames.Add(item.inst.StatusID2);
        Unregister(item.inst);
    }

    private static List<StatusActionData> GetStatusActions(string statusID2)
    {
        var dm = DataManager.Instance;
        if (dm == null) return null;
        dm.StatusActionDict.TryGetValue(statusID2, out var actions);
        return actions;
    }
}
