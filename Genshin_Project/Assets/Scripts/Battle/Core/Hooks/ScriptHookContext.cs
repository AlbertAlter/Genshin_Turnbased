using System.Collections.Generic;

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
    /// <summary>钩子所属状态/效果的施放者。</summary>
    public BattleEntity Caster;

    /// <summary>判断目标（如 Kaeya_C1 的攻击前目标）。</summary>
    public BattleEntity Target;

    /// <summary>触发本次判断的状态实例（可为空：效果级钩子）。</summary>
    public StatusInstance Status;

    /// <summary>状态宿主（BattleEntity 或 FieldPosition，可为空）。</summary>
    public object StatusHost;

    /// <summary>最近一次造成命中的效果ID2（HitLanded 判定用）。</summary>
    public string HitEffectID;

    /// <summary>本次伤害结算事件（Kaeya_C4 / Kill 判定用）。</summary>
    public DamageResolvedEvent DamageEvent;

    /// <summary>本次治疗的前后生命、实际治疗及溢出量；条件确认时必须已经正式提交。</summary>
    public HealResolvedEvent HealEvent;

    /// <summary>引出本次响应的反应事实；后续反应不能覆盖类型、来源效果和元素列表。</summary>
    public ReactionOccurrence ReactionEvent;

    /// <summary>只复制事件事实与目标列表；Caster/Target/Status 保持实时实体引用。</summary>
    public ScriptHookContext Snapshot()
    {
        var copy = (ScriptHookContext)MemberwiseClone();
        copy.DamageEvent = DamageEvent?.Snapshot();
        if (ReactionEvent != null)
            copy.ReactionEvent = new ReactionOccurrence
            {
                Type = ReactionEvent.Type,
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
