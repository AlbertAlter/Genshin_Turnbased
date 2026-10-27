using System;
using System.Collections.Generic;
using System.Globalization;

public enum CountChangeOperation { Add, Full, Clear }

/// <summary>Count效果的数值请求；不携带重新施加、计时或绑定参数。</summary>
public sealed class CountChangeRequest
{
    public CountChangeOperation Operation { get; }
    public double Delta { get; }
    public BattleEntity Actor { get; }
    public StatusInstance SourceStatus { get; }
    public string EffectID { get; }
    private readonly int? integerDelta;

    private CountChangeRequest(CountChangeOperation operation, double delta, int? integer,
        BattleEntity actor, StatusInstance sourceStatus, string effectID)
    {
        Operation = operation; Delta = delta; integerDelta = integer;
        Actor = actor; SourceStatus = sourceStatus; EffectID = effectID;
    }

    public static CountChangeRequest Parse(string value, BattleEntity actor = null,
        StatusInstance sourceStatus = null, string effectID = null)
    {
        string token = value?.Trim();
        if (string.Equals(token, "full", StringComparison.OrdinalIgnoreCase))
            return new CountChangeRequest(CountChangeOperation.Full, 0d, null, actor, sourceStatus, effectID);
        if (string.Equals(token, "clear", StringComparison.OrdinalIgnoreCase))
            return new CountChangeRequest(CountChangeOperation.Clear, 0d, null, actor, sourceStatus, effectID);
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double delta)
            || double.IsNaN(delta) || double.IsInfinity(delta) || Math.Abs(delta) > float.MaxValue)
            throw new FormatException("Count.Param2须为有限增减数、full或clear：[" + value + "]");
        int? integer = int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
            ? number : (int?)null;
        return new CountChangeRequest(CountChangeOperation.Add, delta, integer, actor, sourceStatus, effectID);
    }

    public ResourceChangeReason Reason => Operation == CountChangeOperation.Clear
        ? ResourceChangeReason.Clear : Delta < 0d ? ResourceChangeReason.Spend : ResourceChangeReason.Gain;

    public void ValidateNumber(bool integer)
    {
        if (integer && Operation == CountChangeOperation.Add && !integerDelta.HasValue)
            throw new FormatException("整数Count的Param2须为有符号int，不接受小数、公式或溢出数值。");
    }

    public int IntegerAfter(int before, int minimum, int maximum)
    {
        ValidateNumber(true);
        if (minimum > maximum) throw new ArgumentException("Count上下限倒置。");
        if (Operation == CountChangeOperation.Full) return maximum;
        if (Operation == CountChangeOperation.Clear) return Math.Max(minimum, Math.Min(maximum, 0));
        // 先以long相加再裁切，不能发生int溢出，也不能经float损失逐点精度。
        return (int)Math.Max(minimum, Math.Min((long)maximum, (long)before + integerDelta.Value));
    }

    public float FloatAfter(float before, float minimum, float maximum)
    {
        if (float.IsNaN(before) || float.IsInfinity(before)
            || float.IsNaN(minimum) || float.IsInfinity(minimum)
            || float.IsNaN(maximum) || float.IsInfinity(maximum) || minimum > maximum)
            throw new ArgumentException("Count余额及上下限须为有效有限数。");
        if (Operation == CountChangeOperation.Full) return maximum;
        if (Operation == CountChangeOperation.Clear) return Math.Max(minimum, Math.Min(maximum, 0f));
        return (float)Math.Max(minimum, Math.Min(maximum, (double)before + Delta));
    }

    public double RequestedDelta(double before, double after)
        => Operation == CountChangeOperation.Add ? Delta : after - before;

    /// <summary>简单Count可复用；归零保留实例。自行声明的专用消费/溢出路径不能再调用本方法重复声明。</summary>
    public void ApplyInteger(StatusInstance status, BattleEntity host, int minimum, int maximum)
    {
        int before = status.RuntimeIntValue;
        int after = IntegerAfter(before, minimum, maximum);
        status.RuntimeIntValue = after;
        Publish(status, host, before, after, true);
    }

    public void ApplyFloat(StatusInstance status, BattleEntity host, float minimum, float maximum)
    {
        float before = status.RuntimeValue;
        float after = FloatAfter(before, minimum, maximum);
        status.RuntimeValue = after;
        Publish(status, host, before, after, false);
    }

    private void Publish(StatusInstance status, BattleEntity host, double before, double after, bool integer)
    {
        if (before == after) return;
        BattleEventHub.Publish(new ResourceEventData("Count", status.StatusID2, host, before, after,
            RequestedDelta(before, after), Reason, integer, actor: Actor));
    }
}

/// <summary>
/// Param1=Count状态数字ID或ID2；Param2=增减数/full/clear。
/// 只修改选中实体上已存在的唯一实例；不创建、不重施、不调用OnApply。
/// 专用处理器须显式接入；夜魂及未适配的复杂Count不走通用数值修改。
/// </summary>
public static class CountEffect
{
    private sealed class Prepared
    {
        internal BattleEntity Host;
        internal StatusInstance Status;
        internal CountStatusHandler Handler;
    }

    public static void ValidateTargetType(string targetType)
    {
        if (BattleTargetResolver.IsTeamTarget(targetType)
            || (!string.IsNullOrEmpty(targetType) && targetType.EndsWith("Field", StringComparison.Ordinal)))
            throw new NotSupportedException("Count效果当前只修改实体上的计数，未开放场地或队伍Count。");
    }

    public static StatusMainData ResolveDefinition(DataManager data, string reference)
    {
        if (data == null) throw new InvalidOperationException("Count效果缺少DataManager。");
        string key = reference?.Trim();
        if (string.IsNullOrEmpty(key)) throw new FormatException("Count.Param1必须填写Count状态ID。");
        StatusMainData result = null;
        if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int numeric))
        {
            if (numeric <= 0) throw new FormatException("Count数字状态ID必须为正整数：" + key);
            foreach (var main in data.StatusMainDict.Values)
                if (main != null && main.StatusID == numeric)
                {
                    if (result != null && result != main)
                        throw new InvalidOperationException("Count数字状态ID不唯一：" + key);
                    result = main;
                }
        }
        else data.StatusMainDict.TryGetValue(key, out result);
        if (result == null) throw new FormatException("Count状态ID不存在：" + key);
        if (!string.Equals(result.StatusType, "Count", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Count.Param1必须指向Count类型状态：" + key);
        return result;
    }

    public static void Execute(string reference, string value, BattleEntity actor,
        IEnumerable<BattleEntity> targets, string effectID = null, StatusInstance sourceStatus = null,
        string targetType = null)
    {
        ValidateTargetType(targetType);
        var request = CountChangeRequest.Parse(value, actor, sourceStatus, effectID);
        StatusMainData main = ResolveDefinition(DataManager.Instance, reference);
        CountStatusHandler handler = CountStatusSystem.RequireEffectHandler(main);
        request.ValidateNumber(handler.EffectUsesIntegerValue);
        var prepared = new List<Prepared>();
        if (targets == null) return;
        // 保留随机模式3返回的重复目标：选中同一人两次，就执行两次数值操作。
        // 整批先校验，避免后面的整数小数错误导致前半批已提交。
        foreach (var target in targets)
        {
            if (target == null || !target.IsAlive) continue;
            StatusInstance selected = null;
            foreach (var status in target.GetStatusInstances(main.StatusID2))
            {
                if (status == null || !status.IsActive) continue;
                if (selected != null)
                    throw new InvalidOperationException("Count效果无法定位同一实体上的多份同ID实例：" + main.StatusID2);
                selected = status;
            }
            if (selected == null || !EquipmentStatusLifetime.CanExecute(selected)) continue;
            if (!string.Equals(selected.MainData?.StatusType, "Count", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("目标实例并非Count状态：" + main.StatusID2);
            handler.ValidateEffectChange(selected, target, request);
            prepared.Add(new Prepared { Host = target, Status = selected, Handler = handler });
        }
        using var resolution = BattleEventHub.BeginResolution();
        using var collection = BattleResolutionContext.BeginCollection();
        foreach (var item in prepared)
        {
            // 绑定本次解析的实际实例；不能在旧实例耗尽后扣到同ID新一轮状态上。
            if (!item.Host.IsAlive || !item.Status.IsActive || !item.Host.HasStatusInstance(item.Status)
                || !EquipmentStatusLifetime.CanExecute(item.Status)) continue;
            double before = ReadValue(item.Status);
            item.Handler.ChangeFromEffect(item.Status, item.Host, request);
            LogManager.Log(LogCategory.Effect, "Count " + effectID + " -> " + item.Host.EntityID
                + " " + main.StatusID2 + " " + before + " -> " + ReadValue(item.Status));
        }
    }

    private static double ReadValue(StatusInstance status)
        => status.HasIntegerRuntimeValue ? (double)status.RuntimeIntValue : status.RuntimeValue;

    public static void ValidateDefinitions(DataManager data)
    {
        foreach (var effect in data.SkillEffectDict.Values)
            if (effect?.EffectType == "Count") ValidateRow(data, effect.Param1, effect.Param2,
                effect.TargetType, effect.Element, effect.Param3, effect.DamageType, effect.DamageHook,
                effect.Duration, effect.AddInPhase, effect.TriggerPhase, effect.SkillEffectID2);
        foreach (var effect in data.StatusEffectDict.Values)
            if (effect?.EffectType == "Count") ValidateRow(data, effect.Param1, effect.Param2,
                effect.TargetType, effect.Element, effect.Param3, effect.DamageType, effect.DamageHook,
                effect.Duration, effect.AddInPhase, effect.TriggerPhase, effect.StatusEffectID2);
        foreach (var effect in data.EnemySkillEffectDict.Values)
            if (effect?.EffectType == "Count") ValidateRow(data, effect.Param1, effect.Param2,
                effect.TargetType, effect.Element, effect.Param3, effect.DamageType, effect.DamageHook,
                effect.Duration, effect.AddInPhase, effect.TriggerPhase, effect.SkillEffectID2);
    }

    private static void ValidateRow(DataManager data, string reference, string value, string targetType,
        string element, string param3, string damageType, string damageHook, int duration,
        int addPhase, int triggerPhase, string effectID)
    {
        try
        {
            ValidateTargetType(targetType);
            var request = CountChangeRequest.Parse(value);
            var handler = CountStatusSystem.RequireEffectHandler(ResolveDefinition(data, reference));
            request.ValidateNumber(handler.EffectUsesIntegerValue);
            if (!string.IsNullOrWhiteSpace(element) || !string.IsNullOrWhiteSpace(param3)
                || !string.IsNullOrWhiteSpace(damageType) || !string.IsNullOrWhiteSpace(damageHook)
                || duration != 0 || addPhase != 0 || triggerPhase != 0)
                throw new FormatException("Count的Element/Param3/DamageType/DamageHook及持续/阶段字段必须留空。");
        }
        catch (Exception error) when (error is FormatException || error is InvalidOperationException
            || error is NotSupportedException)
        { throw new TableValidationException("[Count效果 " + effectID + "] " + error.Message); }
    }
}
