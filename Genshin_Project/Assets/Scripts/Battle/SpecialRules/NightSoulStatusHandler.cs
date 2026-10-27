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

/// <summary>NightSoul复用Count生命周期；每个角色有独立ID、处理器和整数池。</summary>
public class NightSoulStatusHandler : CountStatusHandler
{
    public const string StatusType = "NightSoul";
    public const int BlessingNumericStatusID = 40021;
    public const string BlessingStatusID = "ST_NightSoulBlessing";

    private sealed class Definition
    {
        public readonly int CharacterID, StatusID, Maximum;
        public readonly string Key;
        public Definition(int characterID, int statusID, string name, int maximum)
        {
            CharacterID = characterID; StatusID = statusID;
            Key = "ST_NightSoul_" + name; Maximum = maximum;
        }
    }

    // 身份来自角色目录，基础上限来自角色技能文档；角色规则可替换自己的处理器。
    private static readonly Definition[] Definitions =
    {
        new Definition(1092, 40010, "Kinich", 20),
        new Definition(1093, 40011, "Mualani", 60),
        new Definition(1094, 40012, "Kachina", 60),
        new Definition(1095, 40013, "Xilonen", 90),
        new Definition(1096, 40014, "Ororon", 80),
        new Definition(1097, 40015, "Chasca", 80),
        new Definition(1098, 40016, "Citlali", 100),
        new Definition(1099, 40017, "Mavuika", 80),
        new Definition(1102, 40018, "Varesa", 40),
        new Definition(1103, 40019, "Iansan", 54),
        new Definition(1105, 40020, "Ifa", 80)
    };

    private readonly int defaultMaximum;
    private sealed class Limits { public int Maximum; }

    protected NightSoulStatusHandler(int maximum)
    {
        if (maximum < 0) throw new ArgumentOutOfRangeException("maximum");
        defaultMaximum = maximum;
    }

    internal static void RegisterHandlers()
    {
        foreach (Definition definition in Definitions)
            CountStatusSystem.Register(definition.Key, new NightSoulStatusHandler(definition.Maximum));
    }

    public static bool IsNightSoul(StatusMainData main)
    {
        return main != null && string.Equals(main.StatusType, StatusType, StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveStatusReference(string value)
    {
        int id;
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) return value;
        if (id == BlessingNumericStatusID) return BlessingStatusID;
        foreach (Definition definition in Definitions)
            if (definition.StatusID == id) return definition.Key;
        return value;
    }

    public static bool IsStatusReference(string value)
    {
        string key = ResolveStatusReference(value?.Trim());
        foreach (Definition definition in Definitions)
            if (string.Equals(key, definition.Key, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>角色初始化时先准备0点池，避免首次战技前的被动获点找不到上限。</summary>
    public static void InitializeForCharacter(BattleEntity target, int characterID)
    {
        foreach (Definition definition in Definitions)
        {
            if (definition.CharacterID != characterID) continue;
            StatusMainData main;
            if (DataManager.Instance == null
                || !DataManager.Instance.StatusMainDict.TryGetValue(definition.Key, out main)
                || !IsNightSoul(main))
                throw new InvalidOperationException("角色夜魂主表缺失或类型不是NightSoul：" + definition.Key);
            StatusInstance pool = target.AddStatus(definition.Key, target, 0, 0, 0, main);
            // 新战斗从0开始；普通ApplyStatus重施仍保留余额。
            pool.RuntimeIntValue = 0;
            return;
        }
    }

    public override void OnCreated(StatusInstance status, object host)
    {
        OnCreated(status, host, null);
    }

    public override void OnCreated(StatusInstance status, object host, CountStatusApplication application)
    {
        BattleEntity target = host as BattleEntity;
        if (target == null)
            throw new InvalidOperationException("夜魂只能施加到角色/实体本人，不能作为场地数值池。");
        if (Find(target) != null)
            throw new InvalidOperationException("同一实体不能同时拥有两个夜魂值状态。");
        status.RuntimeIntValue = 0;
        status.CountRuntimeState = new Limits { Maximum = defaultMaximum };
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
        GetLimits(status);
        // 上限由角色处理器管理；重施不重置余额，也不从Param2读取或调整上限。
        return status;
    }

    public override void OnRemoved(StatusInstance status, object host)
    {
        int before = status.RuntimeIntValue;
        status.RuntimeIntValue = 0;
        status.CountRuntimeState = null;
        if (before > 0) BattleEventHub.Publish(new ResourceEventData("NightSoul", status.StatusID2,
            host as BattleEntity, before, 0, -before, ResourceChangeReason.Clear, true));
    }

    private static Limits GetLimits(StatusInstance state)
    {
        Limits limits = state?.CountRuntimeState as Limits;
        if (limits == null || !state.HasIntegerRuntimeValue)
            throw new InvalidOperationException("夜魂实例缺少整数余额/上限；请先初始化对应角色的NightSoul状态。");
        return limits;
    }

    public static StatusInstance Find(BattleEntity target)
    {
        if (target == null) return null;
        StatusInstance found = null;
        foreach (StatusInstance state in target.StatusDict.Values)
        {
            if (state == null || !state.IsActive || !IsNightSoul(state.MainData)) continue;
            if (found != null) throw new InvalidOperationException("同一实体存在多个NightSoul池，无法确定增减目标。");
            found = state;
        }
        return found;
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

    public static NightSoulValueChange ChangeValue(BattleEntity target, int delta, ResourceChangeReason? reason = null)
    {
        RequireInitialized(target);
        StatusInstance state = Find(target);
        int before = state.RuntimeIntValue;
        // 用long承接int相加，避免先溢出再钳制；最终余额仍是int。
        long requested = (long)before + delta;
        int after = (int)Math.Max(0L, Math.Min((long)GetLimits(state).Maximum, requested));
        state.RuntimeIntValue = after;
        if (delta != 0) BattleEventHub.Publish(new ResourceEventData("NightSoul", state.StatusID2,
            target, before, after, delta, reason ?? (delta < 0 ? ResourceChangeReason.Spend : ResourceChangeReason.Gain), true));
        // 归零保留已配置的上限，不自动等同于退出加持或触发迸发。
        return new NightSoulValueChange(before, after);
    }

    public static NightSoulValueChange AddValue(BattleEntity target, int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException("amount");
        return ChangeValue(target, amount);
    }

    public static NightSoulValueChange ReduceValue(BattleEntity target, int amount, ResourceChangeReason reason = ResourceChangeReason.Spend)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException("amount");
        return ChangeValue(target, -amount, reason);
    }

    public static NightSoulValueChange ClearValue(BattleEntity target)
    {
        return ReduceValue(target, GetValue(target), ResourceChangeReason.Clear);
    }
}
