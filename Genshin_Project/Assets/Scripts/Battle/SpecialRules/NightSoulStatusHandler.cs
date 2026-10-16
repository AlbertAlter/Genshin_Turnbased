using System;
using System.Globalization;

public struct NightSoulValueChange
{
    public readonly int Before;
    public readonly int After;
    public NightSoulValueChange(int before, int after) { Before = before; After = after; }
    public int AddedAmount { get { return Math.Max(0, After - Before); } }
    public int RemovedAmount { get { return Math.Max(0, Before - After); } }
}

/// <summary>公共夜魂ID，每个实体独立保存整数余额及本次ApplyStatus指定的上限。</summary>
public sealed class NightSoulStatusHandler : CountStatusHandler
{
    public const int NumericStatusID = 40010;
    public const string StatusID = "ST_NightSoul";
    private sealed class Limits { public int Maximum; }

    public static bool IsStatusReference(string value)
    {
        int id;
        return string.Equals(value?.Trim(), StatusID, StringComparison.Ordinal)
            || (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)
                && id == NumericStatusID);
    }

    public static int ParseMaximum(string value)
    {
        int maximum;
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out maximum) || maximum < 0)
            throw new FormatException("施加夜魂时ApplyStatus.Param2必须填写非负int上限，不接受小数或省略。");
        return maximum;
    }

    public override void OnCreated(StatusInstance status, object host)
    {
        throw new InvalidOperationException("公共夜魂必须通过携带Param2上限的ApplyStatus创建。");
    }

    public override void OnCreated(StatusInstance status, object host, CountStatusApplication application)
    {
        if (!(host is BattleEntity))
            throw new InvalidOperationException("夜魂只能施加到角色/实体本人，不能作为场地数值池。");
        int maximum = ParseMaximum(application?.Param2);
        status.RuntimeIntValue = 0;
        status.CountRuntimeState = new Limits { Maximum = maximum };
        status.StackCount = 1;
        status.IsPermanent = true;
        status.RemainingPhaseCount = 0;
        status.AddInPhase = status.TriggerPhase = 0;
        status.RecordSource(null);
        status.WeaponContext = null;
    }

    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host,
        CountStatusApplication application)
    {
        int maximum = ParseMaximum(application?.Param2);
        Limits limits = GetLimits(status);
        // 调整上限不赠送点数、不清空余额；上限降低时仅截去超额部分。
        status.RuntimeIntValue = Math.Min(status.RuntimeIntValue, maximum);
        limits.Maximum = maximum;
        return status;
    }

    public override void OnRemoved(StatusInstance status, object host)
    {
        status.RuntimeIntValue = 0;
        status.CountRuntimeState = null;
    }

    private static Limits GetLimits(StatusInstance state)
    {
        Limits limits = state?.CountRuntimeState as Limits;
        if (limits == null || !state.HasIntegerRuntimeValue)
            throw new InvalidOperationException("夜魂实例缺少整数余额/上限；请先通过ApplyStatus施加并配置Param2。");
        return limits;
    }

    private static StatusInstance Find(BattleEntity target)
    {
        StatusInstance state;
        return target != null && target.StatusDict.TryGetValue(StatusID, out state)
            && state != null && state.IsActive ? state : null;
    }

    public static int GetValue(BattleEntity target)
    {
        StatusInstance state = Find(target);
        if (state == null) return 0;
        GetLimits(state);
        return state.RuntimeIntValue;
    }

    public static int GetMaximum(BattleEntity target)
    {
        StatusInstance state = Find(target);
        return state == null ? 0 : GetLimits(state).Maximum;
    }

    public static void RequireInitialized(BattleEntity target)
    {
        if (target == null) throw new ArgumentNullException("target");
        GetLimits(Find(target));
    }

    public static NightSoulValueChange ChangeValue(BattleEntity target, int delta)
    {
        RequireInitialized(target);
        StatusInstance state = Find(target);
        int before = state.RuntimeIntValue;
        // 用long承接int相加，避免先溢出再钳制；最终余额仍是int。
        long requested = (long)before + delta;
        int after = (int)Math.Max(0L, Math.Min((long)GetLimits(state).Maximum, requested));
        state.RuntimeIntValue = after;
        // 归零保留已配置的上限，不自动等同于退出加持或触发迸发。
        return new NightSoulValueChange(before, after);
    }

    public static NightSoulValueChange AddValue(BattleEntity target, int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException("amount");
        return ChangeValue(target, amount);
    }

    public static NightSoulValueChange ReduceValue(BattleEntity target, int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException("amount");
        return ChangeValue(target, -amount);
    }

    public static NightSoulValueChange ClearValue(BattleEntity target)
    {
        return ReduceValue(target, GetValue(target));
    }
}
