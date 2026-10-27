using System;

/// <summary>一次数值操作的结果，不持久保存历史，也不自行触发角色或武器收益。</summary>
public struct BondOfLifeValueChange
{
    public readonly float Before;
    public readonly float After;

    public BondOfLifeValueChange(float before, float after)
    {
        Before = before;
        After = after;
    }

    public float AddedAmount { get { return Math.Max(0f, After - Before); } }
    public float RemovedAmount { get { return Math.Max(0f, Before - After); } }
    public bool BecameEmpty { get { return Before > 0f && After <= 0f; } }
}

/// <summary>
/// 生命之契的公共数值载体。和脚本控制的反应显示状态一样，直接维护实体上的唯一状态，
/// 不登记普通状态行动或回合计时，也不把生命之契视为元素反应。
/// 仅记录一个剩余绝对值；施加量、上限、治疗抵扣与收益时机由具体调用方决定。
/// </summary>
public sealed class BondOfLifeStatusHandler : CountStatusHandler
{
    public const int NumericStatusID = 40009;
    public const string StatusID = "ST_BondOfLife";

    public override void OnCreated(StatusInstance status, object host)
    {
        status.RuntimeValue = 0f;
        PrepareCarrier(status);
    }

    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host,
        CountStatusApplication application)
    {
        // 普通ApplyStatus不能覆盖已有公共余额；增减仍只走BondOfLife效果/接口。
        return status;
    }

    public override void OnRemoved(StatusInstance status, object host)
    {
        float before = status.RuntimeValue;
        status.RuntimeValue = 0f;
        if (before > 0f) BattleEventHub.Publish(new ResourceEventData("BondOfLife", StatusID,
            host as BattleEntity, before, 0, -before, ResourceChangeReason.Clear, false));
    }

    public static float GetValue(BattleEntity target)
    {
        StatusInstance state;
        if (target == null || !target.StatusDict.TryGetValue(StatusID, out state)
            || state == null || !state.IsActive) return 0f;
        return state.RuntimeValue;
    }

    /// <summary>比例只是当前剩余绝对值除以当前生命上限，不另外保存或随上限变化改写数值。</summary>
    public static float GetHPRatio(BattleEntity target)
    {
        return target != null && target.GetFinalHP() > 0f ? GetValue(target) / target.GetFinalHP() : 0f;
    }

    private static BondOfLifeValueChange SetValue(BattleEntity target, float value, float requestedDelta, ResourceChangeReason reason)
    {
        using var collection = BattleResolutionContext.BeginCollection();
        if (target == null) throw new ArgumentNullException("target");
        ValidateAmount(value);
        float before = GetValue(target);
        if (value == 0f)
        {
            StatusInstance removed;
            if (target.StatusDict.TryGetValue(StatusID, out removed) && removed != null)
                removed.RuntimeValue = 0f;
            target.RemoveStatus(StatusID);
            if (requestedDelta != 0f) BattleEventHub.Publish(new ResourceEventData("BondOfLife", StatusID,
                target, before, 0, requestedDelta, reason, false));
            return new BondOfLifeValueChange(before, 0f);
        }

        StatusInstance state;
        bool created = false;
        if (!target.StatusDict.TryGetValue(StatusID, out state) || state == null)
        {
            state = new StatusInstance
            {
                StatusID2 = StatusID,
                ApplyOrder = ++BattleEntity._applyOrderCounter
            };
            target.StatusDict[StatusID] = state;
            created = true;
        }

        state.RuntimeValue = value;
        PrepareCarrier(state);
        if (created) BattleEventHub.Publish(new StatusEventData(state, target, true, "Applied"));
        if (requestedDelta != 0f) BattleEventHub.Publish(new ResourceEventData("BondOfLife", StatusID,
            target, before, value, requestedDelta, reason, false));
        return new BondOfLifeValueChange(before, value);
    }

    private static void PrepareCarrier(StatusInstance state)
    {
        state.StackCount = 1;
        state.IsActive = true;
        state.IsPermanent = true;
        state.RemainingPhaseCount = 0;
        state.AddInPhase = 0;
        state.TriggerPhase = 0;
        // 公共池不属于最后一次加值的武器；来源与收益记录放在各来源自己的状态/钩子中。
        state.Caster = null;
        state.HasSourceEntity = false;
        state.SourceEntityID = 0;
        state.SourceSide = default(BattleSide);
        state.WeaponContext = null;
        state.MainData = GetMainData();
    }

    /// <summary>统一增减入口；不同来源共用原数值相加，不覆盖、不按层数计数，也不设公共比例上限。</summary>
    public static BondOfLifeValueChange ChangeValue(BattleEntity target, float delta, ResourceChangeReason? reason = null)
    {
        if (float.IsNaN(delta) || float.IsInfinity(delta))
            throw new ArgumentOutOfRangeException("delta", "生命之契增减量必须为有限数值。");
        return SetValue(target, Math.Max(0f, GetValue(target) + delta), delta,
            reason ?? (delta < 0f ? ResourceChangeReason.Spend : ResourceChangeReason.Gain));
    }

    /// <summary>在原值上增加；角色或技能专属的获取限制先由对应调用方处理。</summary>
    public static BondOfLifeValueChange AddValue(BattleEntity target, float amount)
    {
        ValidateAmount(amount);
        return ChangeValue(target, amount);
    }

    /// <summary>最多减少当前剩余值；返回的 RemovedAmount 是本次实际减少量，不是请求量或累计量。</summary>
    public static BondOfLifeValueChange ReduceValue(BattleEntity target, float amount, ResourceChangeReason reason = ResourceChangeReason.Spend)
    {
        ValidateAmount(amount);
        return ChangeValue(target, -amount, reason);
    }

    public static BondOfLifeValueChange ClearValue(BattleEntity target)
    {
        return SetValue(target, 0f, -GetValue(target), ResourceChangeReason.Clear);
    }

    private static void ValidateAmount(float amount)
    {
        if (amount < 0f || float.IsNaN(amount) || float.IsInfinity(amount))
            throw new ArgumentOutOfRangeException("amount", "生命之契数值必须为有限的非负数。");
    }

    private static StatusMainData GetMainData()
    {
        StatusMainData main;
        DataManager data = DataManager.Instance;
        if (data != null && data.StatusMainDict.TryGetValue(StatusID, out main)) return main;
        return new StatusMainData
        {
            StatusID = NumericStatusID,
            StatusID2 = StatusID,
            StatusName = "生命之契",
            StatusType = "Count",
            Display = 1,
            MaxCount = 1,
            MaxStack = 1,
            Description = "记录当前生命之契剩余绝对值，由特殊代码管理；归零移除，不按回合衰减。"
        };
    }
}
