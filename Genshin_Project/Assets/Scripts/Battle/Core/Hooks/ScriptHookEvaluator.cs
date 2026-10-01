using System.Text.RegularExpressions;

/// <summary>
/// ScriptHook 统一求值入口（术语：只有该特殊处理器返回真值时才触发该行行动）。
///  - 解析统一走 ScriptHookParser（函数名(参数)，ID大小写严格匹配）
///  - 同一字段可用英文分号并列多个钩子，按填写顺序短路求值，全部为真才返回真
///  - 旧接口 Evaluate(hook, caster) / Evaluate(hook, caster, hitContext) 保留兼容
///  - 新接口 Evaluate(hook, ScriptHookContext) 供事件类钩子（Kill 等）使用
///  - 事件驱动钩子（Pre/Post Damage / Kill(...)）不得在普通六阶段触发集合中执行；
///    各事件系统在对应声明产生时单独驱动。
/// 当前支持的钩子类型：
///   Check(角色ID_Cn/Tn)、HitLanded(效果ID2)、Kaeya_T2、Kaeya_C1、Kaeya_C4、Kill(角色/技能/效果)
/// 后续新钩子类型在 EvaluateParsed 或对应角色钩子脚本中扩展。
/// </summary>
public static class ScriptHookEvaluator
{
    /// <summary>统一求值入口（无命中上下文版）：钩子文本为空返回 true；解析失败/未实现类型返回 false。</summary>
    public static bool Evaluate(string hook, BattleEntity caster)
    {
        return Evaluate(hook, caster, null);
    }

    /// <summary>统一求值入口（带命中上下文）：hitContext = 最近一次造成命中的效果ID2（供 HitLanded() 钩子判定）。</summary>
    public static bool Evaluate(string hook, BattleEntity caster, string hitContext)
    {
        if (string.IsNullOrEmpty(hook)) return true;
        var context = new ScriptHookContext
        {
            Caster = caster,
            HitEffectID = hitContext,
            ActionTargetPositions = BattleManager.Instance != null
                ? BattleManager.Instance.PendingActionTargetPositions
                : null
        };
        return EvaluateAll(hook, context, false);
    }

    /// <summary>
    /// 统一求值入口（带完整上下文版，2026-08-19）：事件类钩子（Kill 等）走此接口。
    /// Kill 在此求值时要求上下文携带 CausedDeath 的 DamageResolvedEvent（由 KillHookSystem 构造）。
    /// </summary>
    public static bool Evaluate(string hook, ScriptHookContext context)
    {
        if (string.IsNullOrEmpty(hook)) return true;
        return EvaluateAll(hook, context, true);
    }

    static bool EvaluateAll(string hooks, ScriptHookContext context, bool allowEventContext)
    {
        if (!ScriptHookParser.TryParseAll(hooks, out var calls))
        {
            LogManager.LogWarning(LogCategory.ScriptHook, $"无法解析的钩子格式: {hooks}，视为不触发");
            return false;
        }
        foreach (ScriptHookParser.HookCall call in calls)
        {
            bool ok;
            if (IsEventDrivenName(call.FunctionName))
            {
                if (!allowEventContext)
                {
                    LogManager.Log(LogCategory.ScriptHook,
                        $"{call.FunctionName} 为事件驱动钩子，普通求值路径视为不触发");
                    return false;
                }
                ok = EvaluateEventHook(call.FunctionName, call.Argument, context);
            }
            else
                ok = EvaluateParsed(call.FunctionName, call.Argument, context);

            if (!ok)
            {
                LogManager.Log(LogCategory.ScriptHook,
                    $"并列钩子未全部通过：{call.Text} 返回假，整组不触发");
                return false;
            }
        }
        return true;
    }

    /// <summary>事件驱动钩子识别：Pre/Post Damage 与 Kill(...) 不得在普通阶段循环中执行。</summary>
    public static bool IsEventDriven(string hook)
    {
        if (string.IsNullOrEmpty(hook)) return false;
        if (!ScriptHookParser.TryParseAll(hook, out var calls)) return false;
        foreach (ScriptHookParser.HookCall call in calls)
            if (IsEventDrivenName(call.FunctionName)) return true;
        return false;
    }

    private static bool IsEventDrivenName(string funcName)
    {
        return funcName == "PreAlliesDamage"
            || funcName == "PreSelfDamage"
            || funcName == "PostAlliesDamage"
            || funcName == "PostSelfDamage"
            || funcName == "Kill";
    }

    static bool EvaluateEventHook(string funcName, string argStr, ScriptHookContext context)
    {
        bool eventMatches = context != null && context.EventHookName == funcName;
        if (!eventMatches) return false;

        if (funcName == "Kill")
        {
            bool ok = context.DamageEvent != null
                && context.DamageEvent.CausedDeath
                && KillHookSystem.MatchesKillArgument(argStr, context.DamageEvent.Source);
            LogManager.Log(LogCategory.ScriptHook, $"Kill({argStr}) → {ok}");
            return ok;
        }

        bool argumentMatches = string.IsNullOrEmpty(argStr)
            || argStr == context.EventHookArgument;
        LogManager.Log(LogCategory.ScriptHook,
            $"{funcName}({argStr}) 事件上下文参数=[{context.EventHookArgument}] → {argumentMatches}");
        return argumentMatches;
    }

    /// <summary>按函数名分发：通用钩子直接处理，角色专属钩子分派到对应角色钩子脚本。</summary>
    private static bool EvaluateParsed(string funcName, string argStr, ScriptHookContext context)
    {
        switch (funcName)
        {
            case "Check":
                return EvalCheck(argStr, context != null ? context.Caster : null);
            case "HitLanded":
                return EvalHit(argStr, context);
            case "Hook":
                return EvalWeaponHook(argStr, context);
            case "Kaeya_T2":
            case "Kaeya_C1":
            case "Kaeya_C4":
                return KaeyaScriptHooks.Evaluate(funcName, context);
            default:
                // 未实现的钩子类型：返回非真值（不触发）是钩子接口的正常行为
                LogManager.Log(LogCategory.ScriptHook, $"未实现的钩子类型: {funcName}（{funcName}({argStr})），视为不触发");
                return false;
        }
    }

    static bool EvalWeaponHook(string argStr, ScriptHookContext context)
    {
        BattleEntity owner = context?.Status?.WeaponContext?.Equipper ?? context?.Caster;
        switch (argStr)
        {
            case "4300201":
            {
                BattleEntity target = context?.Target;
                return target != null
                    && (target.GetAura("Hydro") != null
                        || target.GetAura("Cryo") != null
                        || FrozenReactionHandler.IsFrozen(target));
            }
            case "4300301":
                return owner != null && owner.TotalHP > 0f && owner.CurrentHP / owner.TotalHP > 0.9f;
            case "4300501":
            {
                float roll = BattleRandom.NextFloat01();
                bool triggered = roll < 0.5f;
                LogManager.Log(LogCategory.ScriptHook,
                    $"Hook(4300501) 独立概率判定：率=50.0% 随机={roll:F4} → {(triggered ? "触发" : "不触发")}");
                return triggered;
            }
            default:
                LogManager.Log(LogCategory.ScriptHook, $"未实现的武器 Hook: Hook({argStr})，视为不触发");
                return false;
        }
    }

    /// <summary>
    /// HitLanded(效果ID2)：命中判定——本次命中来源匹配，或本技能序列内命中过该效果
    /// （2026-08-13：T2等插入效果在序列内靠后执行时，第一箭的命中不被后续空ID效果覆盖）。
    /// </summary>
    static bool EvalHit(string argStr, ScriptHookContext context)
    {
        string hitContext = context != null ? context.HitEffectID : null;
        BattleEntity caster = context != null ? context.Caster : null;
        bool ok = !string.IsNullOrEmpty(hitContext) && hitContext == argStr;
        bool sequenceHit = false;
        if (!ok && caster != null && caster.CharacterCtrl is CharacterBattleController ctrl)
            sequenceHit = ctrl.HasHitEffectThisSequence(argStr);
        ok = ok || sequenceHit;
        LogManager.Log(LogCategory.ScriptHook, $"HitLanded({argStr}) 最近命中来源=[{hitContext}] 序列内命中={sequenceHit} → {ok}");
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
            // 施放者不匹配：判定返回非真值属正常路径，不报警告
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
        // 未找到天赋定义：判定返回非真值属正常路径，不报警告
        LogManager.Log(LogCategory.ScriptHook, $"Check({argStr}) 未找到天赋{idx}定义，视为不触发");
        return false;
    }
}
