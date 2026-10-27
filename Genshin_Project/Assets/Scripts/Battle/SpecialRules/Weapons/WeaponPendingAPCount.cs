using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>
/// 3012/3181专用：装备者实际AP累计，供下一组Normal/Heavy读取。
/// 不依赖技能阶段、主动行为身份或蓄力来源；其他正式行为清掉此前累计及该行为已支付的AP。
/// </summary>
internal sealed class WeaponPendingAPCount : CountStatusHandler
{
    internal const string Slingshot = "ST_Weapon_4301203", Amos = "ST_Weapon_4318103";
    private readonly int weapon;
    internal WeaponPendingAPCount(int weaponID) { weapon = weaponID; }
    private sealed class State : IDisposable
    {
        internal float Reserved;
        internal object Generation = new object();
        internal readonly List<IDisposable> Subscriptions = new List<IDisposable>();
        internal readonly HashSet<long> Events = new HashSet<long>();
        internal readonly List<BattleEventData> Facts = new List<BattleEventData>();
        public void Dispose() { foreach (var item in Subscriptions) item.Dispose(); Subscriptions.Clear(); Events.Clear(); Facts.Clear(); }
    }
    private sealed class Use
    {
        internal StatusInstance Status;
        internal State State;
        internal object Generation;
        internal float AP;
        internal bool Landed;
    }
    private sealed class Group : IDisposable
    {
        internal readonly Group Previous;
        internal readonly BattleEntity Owner;
        internal readonly Dictionary<long, string> Executions = new Dictionary<long, string>();
        internal readonly Dictionary<StatusInstance, Use> Uses = new Dictionary<StatusInstance, Use>();
        internal readonly HashSet<StatusInstance> Landed = new HashSet<StatusInstance>();
        internal bool Closed;
        internal Group(BattleEntity owner) { Previous = Current.Value; Owner = owner; Current.Value = this; }
        public void Dispose()
        {
            if (Closed) return;
            try
            {
                // True等不进入普通增伤Prepare的命中，仅在结束边界补消费准备；Record观察器只记录身份。
                foreach (var count in Landed)
                {
                    var use = Reserve(this, count);
                    if (use != null) use.Landed = true;
                }
                Closed = true;
                foreach (var use in Uses.Values)
                {
                    if (!Valid(use, Owner)) continue;
                    Drain(use.Status, use.State);
                    if (!ReferenceEquals(use.State.Generation, use.Generation)) continue;
                    use.State.Reserved = Math.Max(0f, use.State.Reserved - use.AP);
                    // 整组结束才消费；组内新支付但未被本组使用的AP保留给后续输出。
                    if (use.Landed) use.Status.RuntimeValue = Math.Max(0f, use.Status.RuntimeValue - use.AP);
                }
            }
            finally { Closed = true; Current.Value = Previous; }
        }
    }
    private static readonly AsyncLocal<Group> Current = new AsyncLocal<Group>();
    private static bool Attack(string type) => type == "Normal" || type == "Heavy";
    private static bool Valid(Use use, BattleEntity owner)
        => owner != null && use.Status.IsActive && owner.HasStatusInstance(use.Status)
            && EquipmentStatusLifetime.CanExecute(use.Status) && ReferenceEquals(use.Status.CountRuntimeState, use.State);
    private static Use Reserve(Group group, StatusInstance status)
    {
        if (group.Uses.TryGetValue(status, out var existing)) return existing;
        if (!(status.CountRuntimeState is State state)) return null;
        Drain(status, state); // 正式伤害准备前提交已有付款事实，属性查询本身不补记或改Count。
        var use = new Use { Status = status, State = state, Generation = state.Generation,
            AP = Math.Max(0f, status.RuntimeValue - state.Reserved) };
        state.Reserved += use.AP; // 只预留，Count在整组实际命中完成前仍保留原值。
        group.Uses.Add(status, use);
        return use;
    }
    private static void Clear(StatusInstance status, State state)
    { status.RuntimeValue = 0f; state.Reserved = 0f; state.Generation = new object(); }

    private static void Index(BattleEntity owner, BattleDeclaration declaration)
    {
        if (owner == null) return;
        foreach (string id in new[] { Slingshot, Amos })
        {
            var status = owner.GetStatus(id);
            if (status?.CountRuntimeState is State state && status.IsActive
                && EquipmentStatusLifetime.CanExecute(status) && state.Events.Add(declaration.Header.EventId))
                state.Facts.Add(declaration.Data); // 观察器只存已提交事实，不在Record处修改Count或发新声明。
        }
    }
    private static void Drain(StatusInstance status, State state)
    {
        foreach (var fact in state.Facts)
            if (fact is APSpentEventData payment) status.RuntimeValue += payment.Amount;
            else Clear(status, state);
        state.Facts.Clear(); // 私有事实索引，不是另一条响应队列。
    }

    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => new Group(owner));
        BattleSpecialRules.RegisterStatusActionScope(status => new Group(status?.Caster));
        BattleSpecialRules.RegisterDamageEffectScope((owner, effect, execution) =>
        {
            Group fallback = null;
            var group = Current.Value;
            if (group == null || group.Closed || group.Owner != owner) group = fallback = new Group(owner);
            group.Executions[execution] = effect;
            return fallback;
        }, (_, __) => { });
        BattleSpecialRules.RegisterNumberedCondition("4301201", (_, c) => Read(c, out float ap) && ap <= 15f);
        BattleSpecialRules.RegisterNumberedCondition("4301202", (_, c) => Read(c, out float ap) && ap > 15f);
        BattleSpecialRules.RegisterNumberedCondition("4318102", (hook, c) => Read(c, out _));
        BattleSpecialRules.RegisterDeclarationObserver(declaration =>
        {
            if (declaration.Get<APSpentEventData>() is APSpentEventData payment)
            {
                if (payment.Amount > 0) Index(payment.Subject, declaration);
                return;
            }
            if (declaration.Kind == BattleEventKind.ActionStarted && declaration.Get<ActionEventData>() is ActionEventData action)
            {
                if (!Attack(action.ActionType)) Index(action.Subject, declaration);
                return;
            }
            if (declaration.Get<SwitchEventData>() is SwitchEventData change)
            {
                Index(change.Outgoing, declaration); Index(change.Subject, declaration);
                return;
            }
            var group = Current.Value;
            var damage = declaration.Get<DamageEventData>();
            if (group == null || group.Closed || damage == null || !damage.HitLanded
                || !Attack(damage.DamageType) || damage.Source.SourceEntity != group.Owner
                || damage.Source.SourceKind == ReactionSourceKind.DerivedReaction
                || damage.Subject == null || damage.Subject.Side == group.Owner.Side
                || !group.Executions.TryGetValue(declaration.Header.EffectExecutionId, out string effect)
                || effect != damage.Source.SourceEffectID) return;
            // 普通伤害已经在PrepareDamage预留；True不吃增伤，但也能标记本组确实命中。
            foreach (string id in new[] { Slingshot, Amos })
            {
                var count = group.Owner.GetStatus(id);
                if (count == null || !count.IsActive || !EquipmentStatusLifetime.CanExecute(count)) continue;
                group.Landed.Add(count);
            }
        });
    }
    private static string Counter(StatusInstance buff)
        => buff?.WeaponContext?.WeaponID == 3012 ? Slingshot : buff?.WeaponContext?.WeaponID == 3181 ? Amos : null;
    private static bool Read(ScriptHookContext c, out float ap)
        => Read(c?.Status, c?.HitEffectID, c?.DamageType, out ap);
    private static bool Read(StatusInstance buff, string effect, string type, out float ap)
    {
        ap = 0f;
        var group = Current.Value;
        var owner = buff?.WeaponContext?.Equipper;
        if (group == null || group.Closed || owner == null || group.Owner != owner || buff.Caster != owner
            || !group.Executions.TryGetValue(BattleEventHub.CurrentEffectExecutionId, out string actual)) return false;
        if (effect != null && effect != actual) return false;
        if (!Attack(type ?? DamageQueryContext.ResolveType(actual))) return false;
        string counterID = Counter(buff);
        if (counterID == null) return false;
        var count = WeaponHooks.OwnStatus(buff, counterID);
        if (count == null || !group.Uses.TryGetValue(count, out var use) || !Valid(use, owner)) return false;
        ap = use.AP;
        return true;
    }
    internal static bool TryResolve(StatusEffectData effect, StatusInstance status, out float value)
    {
        value = 0f;
        if (effect?.StatusEffectID != 431810201 || effect.EffectType != "Buff"
            || effect.DamageHook != "Hook(431810201)") return false;
        if (Read(status, null, null, out float ap)) value = Math.Min(8f, (float)Math.Floor(ap / 5f)) * WeaponHooks.Param(status, 2);
        return true; // 合法0不回退成Index(2)常量。
    }
    public override void OnCreated(StatusInstance status, object host)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, weapon);
        status.RuntimeValue = 0f;
        var state = new State(); status.CountRuntimeState = state;
        WeaponCountLifetime.Refresh(status, owner, 0, 2); // 永久，不配置TriggerPhase，不随任何阶段衰减。
        bool CurrentSource() => status.IsActive && owner.HasStatusInstance(status) && EquipmentStatusLifetime.CanExecute(status);
        state.Subscriptions.Add(BattleEventHub.Subscribe<APSpentEventData>(BattleEventKind.APSpent, (header, payment) =>
        {
            if (payment.Subject == owner) Drain(status, state);
        }, status.ApplyOrder, CurrentSource));
        state.Subscriptions.Add(BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted, (header, action) =>
        {
            if (action.Subject != owner || Attack(action.ActionType)) return;
            // 原执行器先提交付款再声明行为；此处连同该其他行为自身的费用一起清掉。
            Drain(status, state);
        }, status.ApplyOrder, CurrentSource));
        state.Subscriptions.Add(BattleEventHub.Subscribe<SwitchEventData>(BattleEventKind.SwitchResolved, (header, change) =>
        {
            if (change.Outgoing == owner || change.Subject == owner) Drain(status, state);
        }, status.ApplyOrder, CurrentSource));
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        WeaponCountLifetime.RequireApplication(status, host, application, weapon);
        return status; // 重复初始化不清累计、不重复订阅。
    }
    public override CountDamageContribution PrepareDamage(StatusInstance status, object host, CountDamageContext c)
    {
        var group = Current.Value;
        if (group != null && !group.Closed && group.Owner == host && c.Attacker == host && Attack(c.DamageType)
            && c.Target.Side != c.Attacker.Side && c.SourceKind != ReactionSourceKind.DerivedReaction
            && group.Executions.TryGetValue(c.EffectExecutionID, out string effect) && effect == c.EffectID)
            Reserve(group, status);
        return null; // 仅准备只读资格，增伤仍由原Buff及DMGBonus聚合。
    }
    public override void OnRemoved(StatusInstance status, object host)
    {
        if (status.CountRuntimeState is State state) state.Dispose();
        status.RuntimeValue = 0f; status.CountRuntimeState = null;
    }
}
