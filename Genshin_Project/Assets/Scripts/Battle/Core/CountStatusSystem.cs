using System;
using System.Collections.Generic;

/// <summary>重新施加时的原始请求；如何刷新数值和生命周期由对应 Count 自己决定。</summary>
public sealed class CountStatusApplication
{
    public BattleEntity Caster;
    public int Duration;
    public int AddInPhase;
    public int TriggerPhase;
    public StatusMainData MainData;
    public WeaponRuntimeContext WeaponContext;
    public string Param2; // 专用Count自行解释；夜魂使用本次ApplyStatus填写的整数上限。

    public StatusInstance ApplyTo(BattleEntity target)
    {
        return target.AddStatus(MainData.StatusID2, Caster, Duration, AddInPhase,
            TriggerPhase, MainData, WeaponContext, Param2);
    }
}

/// <summary>一次真实直伤请求。只在正式结算中使用，不在属性、UI 或日志查询中执行。</summary>
public sealed class CountDamageContext
{
    public BattleEntity Attacker;
    public BattleEntity Target;
    public ReactionSourceKind SourceKind;
    public string EffectID;
    public string SkillID;
    public string DamageType;
    public string Element;
    public int SkillType;
    public long EffectExecutionID;
    public int HitIndex;
    public int TargetIndex;
}

/// <summary>Count 只有存储和回调入口；没有统一初值、上限、扣除、刷新或归零移除规则。</summary>
public abstract class CountStatusHandler
{
    public abstract void OnCreated(StatusInstance status, object host);
    public virtual void OnCreated(StatusInstance status, object host, CountStatusApplication application)
    {
        OnCreated(status, host);
    }
    public abstract StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application);
    public virtual void OnRemoved(StatusInstance status, object host) { }
    public virtual void OnItsTurn(StatusInstance status, object host) { }
    public virtual CountDamageContribution PrepareDamage(StatusInstance status, object host, CountDamageContext context)
    {
        return null;
    }
}

/// <summary>处理器登记不保存角色计数；余额属于各自StatusInstance的浮点或整数运行时数值。</summary>
public static class CountStatusSystem
{
    private static readonly Dictionary<string, CountStatusHandler> Handlers
        = new Dictionary<string, CountStatusHandler>(StringComparer.Ordinal);

    static CountStatusSystem()
    {
        Register(BondOfLifeStatusHandler.StatusID, new BondOfLifeStatusHandler());
        Register(NightSoulStatusHandler.StatusID, new NightSoulStatusHandler());
        Register(SturdyBoneCountHandler.StatusID, new SturdyBoneCountHandler());
        Register(HuskCountHandler.StatusID, new HuskCountHandler());
        Register(CrimsonWitchCountHandler.StatusID, new CrimsonWitchCountHandler());
        Register(PaleFlameCountHandler.StatusID, new PaleFlameCountHandler());
        Register(VermillionCountHandler.StatusID, new VermillionCountHandler());
    }

    internal static void Register(string statusID, CountStatusHandler handler)
    {
        Handlers.Add(statusID, handler);
    }

    public static bool IsRegistered(string statusID)
    {
        return statusID != null && Handlers.ContainsKey(statusID);
    }

    public static string ResolvePublicStatusReference(string value)
    {
        int id;
        if (int.TryParse(value, out id))
        {
            if (id == BondOfLifeStatusHandler.NumericStatusID) return BondOfLifeStatusHandler.StatusID;
            if (id == NightSoulStatusHandler.NumericStatusID) return NightSoulStatusHandler.StatusID;
        }
        return value;
    }

    public static bool IsCount(StatusMainData main)
    {
        return main != null && string.Equals(main.StatusType, "Count", StringComparison.OrdinalIgnoreCase);
    }

    private static CountStatusHandler GetHandler(StatusMainData main)
    {
        CountStatusHandler handler;
        if (!Handlers.TryGetValue(main.StatusID2, out handler))
            throw new NotSupportedException("Count 状态尚未登记专用处理器：" + main.StatusID2);
        return handler;
    }

    public static void Validate(StatusMainData main)
    {
        if (IsCount(main)) GetHandler(main);
    }

    public static void OnCreated(StatusInstance status, object host, CountStatusApplication application = null)
    {
        if (status != null && IsCount(status.MainData)) GetHandler(status.MainData).OnCreated(status, host, application);
    }

    public static StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        return GetHandler(status.MainData).OnReapplied(status, host, application);
    }

    public static void OnRemoved(StatusInstance status, object host)
    {
        if (status != null && IsCount(status.MainData)) GetHandler(status.MainData).OnRemoved(status, host);
    }

    public static void OnItsTurn(StatusInstance status, object host)
    {
        if (status != null && status.IsActive && IsCount(status.MainData))
            GetHandler(status.MainData).OnItsTurn(status, host);
    }

    internal static void PrepareDamage(CountDamageContext context, List<CountDamageContribution> contributions)
    {
        if (context == null || context.Attacker == null || context.Target == null || !context.Target.IsAlive) return;
        var statuses = new List<KeyValuePair<StatusInstance, object>>();
        var seen = new HashSet<StatusInstance>();
        AddEntity(context.Attacker, statuses, seen);
        BattleManager battle = BattleManager.Instance;
        if (battle != null)
        {
            foreach (CharacterBattleController ally in battle.Allies)
                if (ally != null) AddEntity(ally.Entity, statuses, seen);
            foreach (EnemyBattleController enemy in battle.Enemies)
                if (enemy != null) AddEntity(enemy.Entity, statuses, seen);
            if (battle.Field != null)
            {
                foreach (FieldPosition field in battle.Field.AllySlots) AddField(field, statuses, seen);
                foreach (FieldPosition field in battle.Field.EnemySlots) AddField(field, statuses, seen);
            }
        }
        statuses.Sort((left, right) => left.Key.ApplyOrder.CompareTo(right.Key.ApplyOrder));
        foreach (var pair in statuses)
        {
            CountDamageContribution contribution = GetHandler(pair.Key.MainData).PrepareDamage(pair.Key, pair.Value, context);
            if (contribution != null) contributions.Add(contribution);
        }
    }

    private static void AddEntity(BattleEntity entity, List<KeyValuePair<StatusInstance, object>> list, HashSet<StatusInstance> seen)
    {
        if (entity == null) return;
        foreach (StatusInstance status in entity.GetStatusList()) Add(status, entity, list, seen);
        AddField(entity.Position, list, seen);
    }

    private static void AddField(FieldPosition field, List<KeyValuePair<StatusInstance, object>> list, HashSet<StatusInstance> seen)
    {
        if (field == null) return;
        foreach (StatusInstance status in field.StatusList) Add(status, field, list, seen);
    }

    private static void Add(StatusInstance status, object host, List<KeyValuePair<StatusInstance, object>> list, HashSet<StatusInstance> seen)
    {
        if (status != null && status.IsActive && IsCount(status.MainData) && seen.Add(status))
            list.Add(new KeyValuePair<StatusInstance, object>(status, host));
    }
}

/// <summary>专用处理器提供的单次贡献；取消与命中后处理同样由专用处理器定义。</summary>
public sealed class CountDamageContribution
{
    public readonly float BaseDamageBonus;
    private Action<DamageResolvedEvent> _committed;
    private Action _cancelled;
    private DamageResolvedEvent _result;
    private bool _resolved;

    public CountDamageContribution(float baseDamageBonus, Action<DamageResolvedEvent> committed, Action cancelled)
    {
        BaseDamageBonus = baseDamageBonus;
        _committed = committed;
        _cancelled = cancelled;
    }

    public void Resolve(DamageResolvedEvent result)
    {
        if (_resolved) return;
        _resolved = true;
        if (result != null && result.HitLanded) _result = result;
        else Cancel();
    }

    internal void Cancel()
    {
        if (_result != null) return;
        Action cancelled = _cancelled;
        _cancelled = null;
        _committed = null;
        _resolved = true;
        if (cancelled != null) cancelled();
    }

    internal void Finish()
    {
        Action<DamageResolvedEvent> committed = _committed;
        _committed = null;
        _cancelled = null;
        if (_result != null && committed != null) committed(_result);
    }
}

public sealed class CountDamagePlan
{
    internal readonly List<CountDamageContribution> Contributions = new List<CountDamageContribution>();
    public float BaseDamageBonus
    {
        get
        {
            float total = 0f;
            foreach (CountDamageContribution item in Contributions) total += item.BaseDamageBonus;
            return total;
        }
    }
    public void Resolve(DamageResolvedEvent result)
    {
        foreach (CountDamageContribution item in Contributions) item.Resolve(result);
    }
}

/// <summary>本 hit 结束时先归还取消请求，再完成命中回调；不另建事件队列或改变现有响应顺序。</summary>
public sealed class CountDamageBatch : IDisposable
{
    private readonly List<CountDamagePlan> _plans = new List<CountDamagePlan>();
    private bool _disposed;

    public CountDamagePlan Prepare(CountDamageContext context)
    {
        if (_disposed) throw new ObjectDisposedException("CountDamageBatch");
        var plan = new CountDamagePlan();
        _plans.Add(plan);
        CountStatusSystem.PrepareDamage(context, plan.Contributions);
        return plan;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (CountDamagePlan plan in _plans)
            foreach (CountDamageContribution item in plan.Contributions) item.Cancel();
        foreach (CountDamagePlan plan in _plans)
            foreach (CountDamageContribution item in plan.Contributions) item.Finish();
    }
}

/// <summary>基础伤害加算通过主命中的乘区；溅射不能继承主目标已经消耗的 Count 贡献。</summary>
public static class CountDamageMath
{
    public static float AmplifyingFactor(ReactionResult reaction, float preReactionDamage)
    {
        if (preReactionDamage > 0f && (reaction.PrimaryDamageReactionType == ReactionType.Vaporize
            || reaction.PrimaryDamageReactionType == ReactionType.Melt))
            return reaction.FinalDamage / preReactionDamage;
        return 1f;
    }

    public static float AfterReaction(float flatBonus, ReactionDamageComponents components,
        ReactionResult reaction, float preReactionDamage)
    {
        return flatBonus * components.DamageBonusMultiplier * components.DefenseMultiplier
            * components.ResistanceMultiplier * AmplifyingFactor(reaction, preReactionDamage);
    }
}
