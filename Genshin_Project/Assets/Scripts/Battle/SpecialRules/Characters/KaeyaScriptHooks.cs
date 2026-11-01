using System;
using System.Threading;

/// <summary>
/// 凯亚专属钩子（2026-08-19，状态钩子任务）：Kaeya_T2 / Kaeya_C1 / Kaeya_C4。
/// 每个角色最多一个钩子脚本；后续新角色钩子按同模式新增独立脚本，
/// 通过 BattleSpecialRules 集中登记，ScriptHookEvaluator 无需知道具体角色。
/// </summary>
public static class KaeyaScriptHooks
{
    private sealed class SkillFrame : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly string SkillID;
        internal bool TriggeredFrozen;
        private readonly SkillFrame previous = CurrentSkill.Value;

        internal SkillFrame(BattleEntity owner, SkillMainData skill)
        {
            Owner = owner;
            SkillID = skill?.SkillID2;
            CurrentSkill.Value = this;
        }

        public void Dispose() => CurrentSkill.Value = previous;
    }

    private static readonly AsyncLocal<SkillFrame> CurrentSkill = new AsyncLocal<SkillFrame>();

    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => new SkillFrame(owner, skill));
        BattleSpecialRules.RegisterDeclarationObserver(RecordFrozen);
        BattleSpecialRules.RegisterCondition("Kaeya_T2", (argument, context) => EvalKaeyaT2(context));
        BattleSpecialRules.RegisterCondition("Kaeya_C1", (argument, context) => EvalKaeyaC1(context));
        BattleSpecialRules.RegisterCondition("Kaeya_C4", (argument, context) => EvalKaeyaC4(context));
    }

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
    /// 只索引原反应入口已经提交的事实，不执行回能、不新增反应声明。
    /// 嵌套技能有独立帧，退出后恢复调用者；不从当前冻结状态反推本次反应。
    /// </summary>
    private static void RecordFrozen(BattleDeclaration declaration)
    {
        var frame = CurrentSkill.Value;
        if (frame?.Owner?.EntityID != 1010 || frame.SkillID != "SK_Skill_Kaeya") return;
        var reaction = declaration?.Get<ReactionEventData>();
        if (reaction?.Reaction == ReactionType.Frozen && reaction.Source == frame.Owner
            && reaction.EffectId == "SE_Skill_Kaeya1" && reaction.Subject != null
            && reaction.Subject.Side != frame.Owner.Side)
            frame.TriggeredFrozen = true;
    }

    /// <summary>本次霜袭实际触发冻结才返回真值；回能量和执行时机仍由原InsertEffect控制。</summary>
    static bool EvalKaeyaT2(ScriptHookContext context)
    {
        var frame = CurrentSkill.Value;
        bool triggered = context?.Caster?.EntityID == 1010 && frame?.Owner == context.Caster
            && frame.SkillID == "SK_Skill_Kaeya" && frame.TriggeredFrozen;
        LogManager.Log(LogCategory.ScriptHook, $"Kaeya_T2：本次霜袭实际触发冻结 → {triggered}");
        return triggered;
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
        ElementPoolReading cryo = ElementQuery.ReadAura(target, "Cryo");
        if (cryo?.IsPresent == true)
        {
            string amount = cryo.IsIntrinsic ? "固有" : cryo.Amount.ToString("F1");
            LogManager.Log(LogCategory.ScriptHook, $"Kaeya_C1：目标 {target.EntityID} 有冰附着({amount})，触发");
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
        float maximum = target.GetFinalHP();
        bool below = ev.HPAfter * 5f < maximum;
        LogManager.Log(LogCategory.ScriptHook, $"Kaeya_C4：受击后HP {ev.HPAfter:F1}/{maximum:F1} (20%={maximum * 0.2f:F1}) → {below}");
        return below;
    }
}
