using System.Collections.Generic;

/// <summary>
/// PreAlliesDamage 事件钩子系统（2026-08-15，从 BattleManager 独立）：
/// 我方主动行为（按钮触发技能）造成伤害前，先执行登记的钩子触发行效果（如凯亚凛冽轮舞冰棱）。
/// 登记/注销/触发/查询全部收敛在本类，主程序（BattleManager/CharacterBattleController）只负责调用，不承载钩子逻辑。
/// 登记：状态施加时扫描 StatusAction 行（OnTrigger + ScriptHook=PreAlliesDamage）；
/// 注销：状态消失（到期/移除/重建）时移除；实例数归零才移除条目；
/// 触发：主动行为技能执行开头预解析所有 Damage 目标位置并集（BattleManager.PendingActionTargetPositions，
///      至少一名实体=不会落空）→ 遍历记录表触发触发行。
/// </summary>
public static class PreDamageHookSystem
{
    /// <summary>钩子条目：状态ID2 →（施放者、触发行、实例数）</summary>
    private class PreDamageHookEntry
    {
        public string StatusID2;
        public BattleEntity Caster;
        public StatusActionData TriggerLine;
        public int InstanceCount;
    }

    private static readonly Dictionary<string, PreDamageHookEntry> _preDamageHooks = new Dictionary<string, PreDamageHookEntry>();

    /// <summary>登记钩子状态（AddStatus 成功时调用）：按状态ID2去重，实例数+1。</summary>
    public static void RegisterPreDamageHook(StatusInstance inst)
    {
        if (inst == null || inst.MainData == null) return;
        if (inst.Caster == null || inst.Caster.CharacterCtrl == null) return;
        var dm = DataManager.Instance;
        if (dm == null || !dm.StatusActionDict.TryGetValue(inst.StatusID2, out var actions)) return;

        StatusActionData triggerLine = null;
        foreach (var act in actions)
        {
            if (act.ActionType == "OnTrigger" && act.ScriptHook == "PreAlliesDamage")
            {
                triggerLine = act;
                break;
            }
        }
        if (triggerLine == null) return;

        if (_preDamageHooks.TryGetValue(inst.StatusID2, out var entry))
        {
            entry.InstanceCount++;
        }
        else
        {
            _preDamageHooks[inst.StatusID2] = new PreDamageHookEntry
            {
                StatusID2 = inst.StatusID2,
                Caster = inst.Caster,
                TriggerLine = triggerLine,
                InstanceCount = 1
            };
            LogManager.Log(LogCategory.PreDamageHook, $"登记 {inst.StatusID2}（施放者 {inst.Caster.EntityID}）");
        }
    }

    /// <summary>注销钩子状态（状态消失时调用）：实例数-1，归零移除条目。</summary>
    public static void UnregisterPreDamageHook(string statusID2)
    {
        if (string.IsNullOrEmpty(statusID2) || _preDamageHooks.Count == 0) return;
        if (!_preDamageHooks.TryGetValue(statusID2, out var entry)) return;
        entry.InstanceCount--;
        if (entry.InstanceCount <= 0)
        {
            _preDamageHooks.Remove(statusID2);
            LogManager.Log(LogCategory.PreDamageHook, $"注销 {statusID2}");
        }
    }

    /// <summary>
    /// 触发所有登记的 PreAlliesDamage 钩子（主动行为技能执行开头、预解析完成后调用）。
    /// 先于该次主动行为实行；次数限制（MaxTimePerTurn 等）由施放者控制器执行时检查。
    /// </summary>
    public static void TriggerPreDamageHooks()
    {
        if (_preDamageHooks.Count == 0) return;
        foreach (var kv in _preDamageHooks)
        {
            var entry = kv.Value;
            if (entry.Caster == null || entry.Caster.CharacterCtrl == null) continue;
            var ctrl = entry.Caster.CharacterCtrl;
            LogManager.Log(LogCategory.PreDamageHook, $"触发 {entry.StatusID2}（先于主动行为）");
            ctrl.ExecutePreDamageHookLine(entry.TriggerLine, entry.Caster);
        }
    }

    /// <summary>查询某状态是否已登记 PreAlliesDamage 钩子（TargetOverride 解析时校验用）。</summary>
    public static bool HasPreDamageHook(string statusID2)
    {
        return !string.IsNullOrEmpty(statusID2) && _preDamageHooks.ContainsKey(statusID2);
    }
}
