using UnityEngine;
using System.Text.RegularExpressions;

/// <summary>
/// ScriptHook 统一求值入口（术语：只有该特殊处理器返回真值时才触发该行行动）。
/// 全局任何需要向钩子询问布尔值的地方，都调用 Evaluate()；
/// 内部解析钩子文本格式（函数名(参数)），按函数名分派到对应处理器，统一返回布尔值。
/// 当前支持的钩子类型：
///   Check(角色ID_Cn) —— 命座 n 已激活（ConstellationLevel >= n）
///   Check(角色ID_Tn) —— 天赋 n 已激活（角色等级 >= 天赋解锁等级）
///   Hit(效果ID2)   —— 本次命中来源为该效果ID2（Damage 效果有目标即算命中，不看伤害数值）
/// 后续新钩子类型（如"本回合某事件是否广播过"等）在 Switch 中扩展。
/// </summary>
public static class ScriptHookEvaluator
{
    /// <summary>统一求值入口（无命中上下文版）：钩子文本为空返回 true；解析失败/未实现类型返回 false 并警告。</summary>
    public static bool Evaluate(string hook, BattleEntity caster)
    {
        return Evaluate(hook, caster, null);
    }

    /// <summary>统一求值入口（带命中上下文）：hitContext = 最近一次造成命中的效果ID2（供 Hit() 钩子判定）。</summary>
    public static bool Evaluate(string hook, BattleEntity caster, string hitContext)
    {
        if (string.IsNullOrEmpty(hook)) return true;
        string trimmed = hook.Trim();
        int paren = trimmed.IndexOf('(');
        string funcName;
        string argStr = "";
        if (paren <= 0)
        {
            // 裸钩子名（无参数，如 Kaeya_T2）：直接按函数名分发（2026-08-15）
            funcName = trimmed;
        }
        else if (trimmed.EndsWith(")"))
        {
            funcName = trimmed.Substring(0, paren).Trim();
            argStr = trimmed.Substring(paren + 1, trimmed.Length - paren - 2).Trim();
        }
        else
        {
            // 含括号但格式不完整（缺右括号等）：配表笔误，保留警告
            LogManager.LogWarning(LogCategory.ScriptHook, $"无法解析的钩子格式: {hook}，视为不触发");
            return false;
        }

        switch (funcName)
        {
            case "Check":
                return EvalCheck(argStr, caster);
            case "HitLanded":
                return EvalHit(argStr, hitContext, caster);
            case "Kaeya_T2":
                // 元素战技使敌人冻结时回能（2026-08-15）：本次行动目标中任一处于冻结状态即返回真值
                return EvalKaeyaT2(caster);
            // 后续钩子类型在此扩展（如监听本回合某事件是否广播过等）
            default:
                // 未实现的钩子类型：返回非真值（不触发）是钩子接口的正常行为，不再报警告（2026-08-15，如凯亚T2等未实装钩子）
                LogManager.Log(LogCategory.ScriptHook, $"未实现的钩子类型: {funcName}（{hook}），视为不触发");
                return false;
        }
    }

    /// <summary>
    /// Kaeya_T2（元素战技使敌人冻结时额外产生能量）：
    /// 本次主动行为目标（BattleManager.PendingActionTargetPositions = 预解析的总伤害目标位置）中
    /// 任一处于冻结状态（含冻结状态视作冰附着）即返回真值；否则打普通日志并返回 false（不触发）。
    /// </summary>
    static bool EvalKaeyaT2(BattleEntity caster)
    {
        var bm = BattleManager.Instance;
        if (bm == null || bm.PendingActionTargetPositions == null || bm.PendingActionTargetPositions.Count == 0)
        {
            LogManager.Log(LogCategory.ScriptHook, "Kaeya_T2：本次行动无目标，未处于冻结状态，视为不触发");
            return false;
        }
        foreach (var pos in bm.PendingActionTargetPositions)
        {
            var e = bm.GetEntityByPosition(BattleSide.Enemy, pos);
            if (e == null) continue;
            if (e.StatusDict.ContainsKey(ElementReactionManager.FreezeStatusID2))
            {
                LogManager.Log(LogCategory.ScriptHook, $"Kaeya_T2：目标 {e.EntityID}（位置{pos}）处于冻结状态，触发");
                return true;
            }
        }
        LogManager.Log(LogCategory.ScriptHook, "Kaeya_T2：本次行动目标未处于冻结状态，视为不触发");
        return false;
    }

    /// <summary>
    /// Hit(效果ID2)：命中判定——本次命中来源匹配，或本技能序列内命中过该效果
    /// （2026-08-13：T2等插入效果在序列内靠后执行时，第一箭的命中不被后续空ID效果覆盖）。
    /// </summary>
    static bool EvalHit(string argStr, string hitContext, BattleEntity caster)
    {
        bool ok = (!string.IsNullOrEmpty(hitContext) && hitContext == argStr);
        if (!ok && caster != null && caster.CharacterCtrl is CharacterBattleController ctrl)
            ok = ctrl.HasHitEffectThisSequence(argStr);
        LogManager.Log(LogCategory.ScriptHook, $"Hit({argStr}) 最近命中来源=[{hitContext}] 序列内命中={(!ok ? (caster != null && caster.CharacterCtrl is CharacterBattleController c2 ? c2.HasHitEffectThisSequence(argStr) : false) : ok)} → {ok}");
        return ok;
    }

    /// <summary>Check(角色ID_Cn/Tn)：天赋或命座已激活检查。参数格式 "1009_C2" / "1009_T1"。</summary>
    static bool EvalCheck(string argStr, BattleEntity caster)
    {
        var m = Regex.Match(argStr, @"^(\d+)_([CT])(\d+)$");
        if (!m.Success)
        {
            LogManager.LogWarning(LogCategory.ScriptHook, $"Check 参数格式非法: Check({argStr})（应为 角色ID_Cn 或 角色ID_Tn）");
            return false;
        }
        int roleId = int.Parse(m.Groups[1].Value);
        char type = m.Groups[2].Value[0];
        int idx = int.Parse(m.Groups[3].Value);

        if (caster == null || caster.EntityID != roleId)
        {
            // 施放者不匹配：判定返回非真值属正常路径，不报警告（2026-08-15）
            LogManager.Log(LogCategory.ScriptHook, $"Check({argStr})：施放者({caster?.EntityID})不是角色 {roleId}，视为不触发");
            return false;
        }

        if (type == 'C')
        {
            bool ok = caster.ConstellationLevel >= idx;
            LogManager.Log(LogCategory.ScriptHook, $"Check({argStr}) 命座{caster.ConstellationLevel}≥{idx} → {ok}");
            return ok;
        }

        // T：天赋激活 = 角色等级 >= 天赋解锁等级
        var dm = DataManager.Instance;
        if (dm != null && dm.TalentDict.TryGetValue(roleId, out var talents))
        {
            foreach (var t in talents)
            {
                if (t.TalentIndex == idx)
                {
                    bool ok = caster.Level >= t.UnlockAfter;
                    LogManager.Log(LogCategory.ScriptHook, $"Check({argStr}) 等级{caster.Level}≥解锁{t.UnlockAfter} → {ok}");
                    return ok;
                }
            }
        }
        // 未找到天赋定义：判定返回非真值属正常路径，不报警告（2026-08-15）
        LogManager.Log(LogCategory.ScriptHook, $"Check({argStr}) 未找到天赋{idx}定义，视为不触发");
        return false;
    }
}
