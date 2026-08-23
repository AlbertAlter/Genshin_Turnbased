using System.Collections.Generic;

/// <summary>
/// 凯亚专属钩子（2026-08-19，状态钩子任务）：Kaeya_T2 / Kaeya_C1 / Kaeya_C4。
/// 每个角色最多一个钩子脚本；后续新角色钩子按同模式新增独立脚本，
/// 由 ScriptHookEvaluator 统一分发到对应角色的钩子脚本。
/// </summary>
public static class KaeyaScriptHooks
{
    /// <summary>凯亚全部钩子的统一入口（函数名严格匹配，大小写敏感）。</summary>
    public static bool Evaluate(string functionName, ScriptHookContext context)
    {
        switch (functionName)
        {
            case "Kaeya_T2": return EvalKaeyaT2(context);
            case "Kaeya_C1": return EvalKaeyaC1(context);
            case "Kaeya_C4": return EvalKaeyaC4(context);
            default:
                return false;
        }
    }

    /// <summary>
    /// Kaeya_T2（元素战技使敌人冻结时额外产生能量）：
    /// 本次主动行为目标（ActionTargetPositions，回退到 BattleManager 预解析缓存）中
    /// 任一处于冻结状态即返回真值。
    /// </summary>
    static bool EvalKaeyaT2(ScriptHookContext context)
    {
        IReadOnlyList<int> positions = context != null && context.ActionTargetPositions != null
            ? context.ActionTargetPositions
            : (BattleManager.Instance != null ? BattleManager.Instance.PendingActionTargetPositions : null);
        if (positions == null || positions.Count == 0)
        {
            LogManager.Log(LogCategory.ScriptHook, "Kaeya_T2：本次行动无目标，未处于冻结状态，视为不触发");
            return false;
        }
        foreach (int pos in positions)
        {
            var e = BattleManager.Instance != null
                ? BattleManager.Instance.GetEntityByPosition(BattleSide.Enemy, pos)
                : null;
            if (e == null) continue;
            if (ReactionStateSystem.TryGetEntityState(e, ReactionType.Frozen, out _))
            {
                LogManager.Log(LogCategory.ScriptHook, $"Kaeya_T2：目标 {e.EntityID}（位置{pos}）处于冻结状态，触发");
                return true;
            }
        }
        LogManager.Log(LogCategory.ScriptHook, "Kaeya_T2：本次行动目标未处于冻结状态，视为不触发");
        return false;
    }

    /// <summary>
    /// Kaeya_C1（普攻/重击攻击带冰附着或冻结的敌人时暴击率增加15%）：
    /// 必须在本次伤害和本次元素附着发生前判断——目标攻击前已有冰附着（量&gt;0）或处于冻结状态，二者任一即可。
    /// 攻击本身为冰元素但目标攻击前没有冰附着/冻结，不能享受本次加成；目标为空返回假。
    /// </summary>
    static bool EvalKaeyaC1(ScriptHookContext context)
    {
        BattleEntity target = context != null ? context.Target : null;
        if (target == null)
        {
            LogManager.Log(LogCategory.ScriptHook, "Kaeya_C1：目标为空，视为不触发");
            return false;
        }
        ElementalAura cryo = target.GetAura("Cryo");
        if (cryo != null && cryo.AuraAmount > 0f)
        {
            LogManager.Log(LogCategory.ScriptHook, $"Kaeya_C1：目标 {target.EntityID} 有冰附着({cryo.AuraAmount:F1})，触发");
            return true;
        }
        if (ReactionStateSystem.TryGetEntityState(target, ReactionType.Frozen, out _))
        {
            LogManager.Log(LogCategory.ScriptHook, $"Kaeya_C1：目标 {target.EntityID} 处于冻结状态，触发");
            return true;
        }
        LogManager.Log(LogCategory.ScriptHook, $"Kaeya_C1：目标 {target.EntityID} 无冰附着且未冻结，视为不触发");
        return false;
    }

    /// <summary>
    /// Kaeya_C4（受击后生命值低于最大生命值20%时生成护盾）：
    /// 严格条件：HitLanded &amp;&amp; 实际扣血&gt;0 &amp;&amp; 目标存活 &amp;&amp; HPAfter &lt; 20%最大生命（正好20%不触发）。
    /// 不要求受击前必须高于20%；原本已低于20%再受实际扣血也可触发（仍受 StatusAction.Cooldown=15 限制）。
    /// 目标死亡时不生成护盾；护盾完全吸收、没有实际扣血时不触发。
    /// </summary>
    static bool EvalKaeyaC4(ScriptHookContext context)
    {
        var ev = context != null ? context.DamageEvent : null;
        if (ev == null || !ev.HitLanded)
        {
            LogManager.Log(LogCategory.ScriptHook, "Kaeya_C4：无受击事件或未命中，视为不触发");
            return false;
        }
        if (ev.ActualHPDamage <= 0f)
        {
            LogManager.Log(LogCategory.ScriptHook, "Kaeya_C4：护盾全吸收/未实际扣血，视为不触发");
            return false;
        }
        BattleEntity target = ev.Target;
        if (target == null || !target.IsAlive)
        {
            LogManager.Log(LogCategory.ScriptHook, "Kaeya_C4：目标已死亡，视为不触发");
            return false;
        }
        // 用等价的 5 倍比较避开 0.2f 乘法在 Mono 下把“正好 20%”
        // 舍入成略高于边界，确保 HPAfter == TotalHP / 5 时不触发。
        bool below = ev.HPAfter * 5f < target.TotalHP;
        LogManager.Log(LogCategory.ScriptHook, $"Kaeya_C4：受击后HP {ev.HPAfter:F1}/{target.TotalHP:F1} (20%={target.TotalHP * 0.2f:F1}) → {below}");
        return below;
    }
}
