using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>
/// 钩子判断上下文（2026-08-19）：统一携带钩子判断所需的全部信息。
///   - Check            使用 Caster
///   - HitLanded        使用 HitEffectID
///   - Kaeya_C1         使用攻击前的 Target
///   - Kaeya_C4         使用 DamageEvent
///   - Kill             使用 DamageEvent.Source
///   - Kaeya_T2         使用 ActionTargetPositions
/// </summary>
public sealed class ScriptHookContext
{
    private static readonly AsyncLocal<ScriptHookContext> Execution = new AsyncLocal<ScriptHookContext>();
    public static ScriptHookContext Current => Execution.Value;
    /// <summary>本次响应对应的不可变声明，不是全局最近一次事件。</summary>
    public BattleDeclaration Declaration;

    // 每个状态响应保留自己的事件；嵌套响应结束后恢复父事件，不依赖全局“最后命中”。
    public static IDisposable EnterExecution(ScriptHookContext context) => new ExecutionScope(context);

    private sealed class ExecutionScope : IDisposable
    {
        private readonly ScriptHookContext previous;
        private readonly IDisposable declaration;
        public ExecutionScope(ScriptHookContext context)
        {
            previous = Execution.Value;
            Execution.Value = context;
            declaration = BattleEventHub.Enter(context?.Declaration);
        }
        public void Dispose() { declaration.Dispose(); Execution.Value = previous; }
    }

    /// <summary>钩子所属状态/效果的施放者。</summary>
    public BattleEntity Caster;

    /// <summary>判断目标（如 Kaeya_C1 的攻击前目标）。</summary>
    public BattleEntity Target;
    /// <summary>属性加成的实际受益单位；不替换来源Caster或本次攻击Target。</summary>
    public BattleEntity Recipient;

    /// <summary>触发本次判断的状态实例（可为空：效果级钩子）。</summary>
    public StatusInstance Status;

    /// <summary>真实状态宿主（BattleEntity、FieldPosition或BattleTeamStatusHost，可为空）。</summary>
    public object StatusHost;

    /// <summary>最近一次造成命中的效果ID2（HitLanded 判定用）。</summary>
    public string HitEffectID;

    /// <summary>正在执行的状态效果数值ID；区别于引发响应的伤害效果，嵌套效果结束后恢复。</summary>
    public int ExecutingStatusEffectID;

    /// <summary>本次属性查询对应的实际伤害元素；空表示没有伤害上下文，不能以角色固有元素代替。</summary>
    public string DamageElement;
    /// <summary>本次实际DamageType；与倍率所属SkillType、主动行为ActionType独立。</summary>
    public string DamageType;
    /// <summary>属性查询的实际技能身份；状态独立伤害不得借用调用者技能。</summary>
    public string DamageSkillID;
    /// <summary>AP费用查询的实际技能ID；不借用DamageType、ActionType或当前按钮绑定推断。</summary>
    public string APCostSkillID;

    /// <summary>仅反应伤害数值收集入口填写；不能从引发响应的ReactionEvent推断当前伤害域。</summary>
    public ReactionType? DamageReaction;

    /// <summary>本次伤害结算事件（Kaeya_C4 / Kill 判定用）。</summary>
    public DamageResolvedEvent DamageEvent;
    /// <summary>已经提交的生命降低事实；不可由属性查询或预计伤害伪造。</summary>
    public HPDecreaseEvent HPDecreaseEvent;

    /// <summary>本次治疗的前后生命、实际治疗及溢出量；条件确认时必须已经正式提交。</summary>
    public HealResolvedEvent HealEvent;

    /// <summary>引出本次响应的反应事实；后续反应不能覆盖类型、来源效果和元素列表。</summary>
    public ReactionOccurrence ReactionEvent;

    /// <summary>本次 SwitchIn / SwitchOut 声明对应的角色；不同于状态施放者。</summary>
    public BattleEntity SwitchedEntity;

    /// <summary>只复制事件事实与目标列表；Caster/Target/Status 保持实时实体引用。</summary>
    public ScriptHookContext Snapshot()
    {
        var copy = (ScriptHookContext)MemberwiseClone();
        copy.DamageEvent = DamageEvent?.Snapshot();
        if (ReactionEvent != null)
            copy.ReactionEvent = new ReactionOccurrence
            {
                Type = ReactionEvent.Type,
                Declaration = ReactionEvent.Declaration,
                DisplayName = ReactionEvent.DisplayName,
                SourceEntity = ReactionEvent.SourceEntity,
                Target = ReactionEvent.Target,
                SourceEffectID = ReactionEvent.SourceEffectID,
                InvolvedElements = ReactionEvent.InvolvedElements != null
                    ? new List<string>(ReactionEvent.InvolvedElements) : null
            };
        if (ActionTargetPositions != null)
            copy.ActionTargetPositions = new List<int>(ActionTargetPositions).AsReadOnly();
        return copy;
    }

    /// <summary>本次主动行为的目标位置集合（Kaeya_T2 判定用）。</summary>
    public IReadOnlyList<int> ActionTargetPositions;

    /// <summary>当前由哪个事件钩子入口驱动（Pre/Post Allies/Self Damage / Kill）。</summary>
    public string EventHookName;

    /// <summary>当前事件钩子入口的参数；用于并列钩子列表中确认对应事件项。</summary>
    public string EventHookArgument;
}
