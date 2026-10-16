using System;

/// <summary>已正式提交的一次生命降低事实。不是伤害，不自行派发OnDamage、命中或反应。</summary>
public sealed class HPDecreaseEvent
{
    public BattleEntity Target { get; }
    public float HPBefore { get; }
    public float HPAfter { get; }
    public float Amount => HPBefore - HPAfter;

    public HPDecreaseEvent(BattleEntity target, float before, float after)
    {
        if (target == null || float.IsNaN(before) || float.IsInfinity(before)
            || float.IsNaN(after) || float.IsInfinity(after) || before <= after || after < 0f)
            throw new ArgumentException("生命降低事件需要已经提交的有效前后生命值。");
        Target = target;
        HPBefore = before;
        HPAfter = after;
    }
}
