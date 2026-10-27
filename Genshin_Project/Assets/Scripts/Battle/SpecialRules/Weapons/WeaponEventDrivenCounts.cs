using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>显式登记的武器订阅桥。只定向执行原表行动，复用ApplyOrder、原限次和响应队列。</summary>
internal static class WeaponEventResponses
{
    private sealed class Subscription { internal IDisposable Listener; internal readonly HashSet<long> Events = new HashSet<long>(); }
    private static readonly ConditionalWeakTable<StatusInstance, Subscription> Subscriptions = new ConditionalWeakTable<StatusInstance, Subscription>();
    internal static void Register<T>(string statusID, int weapon, BattleEventKind kind, string hook,
        Func<StatusInstance, T, bool> matches) where T : BattleEventData
    {
        BattleSpecialRules.RegisterNumberedCondition(hook, (_, c) =>
        {
            var fact = c?.Declaration?.Get<T>();
            return c?.Status != null && fact != null && matches(c.Status, fact);
        });
        BattleSpecialRules.RegisterStatusLifecycle(statusID, (status, host) =>
        {
            var owner = WeaponCountLifetime.RequireOwner(status, host, weapon);
            var subscription = new Subscription(); Subscriptions.Add(status, subscription);
            subscription.Listener = BattleEventHub.Subscribe<T>(kind, (header, fact) =>
            {
                if (!matches(status, fact) || !subscription.Events.Add(header.EventId)) return;
                Dispatch(status, owner, hook, BattleEventHub.Current);
            }, status.ApplyOrder, () => status.IsActive && owner.HasStatusInstance(status) && EquipmentStatusLifetime.CanExecute(status));
        }, (status, host) =>
        {
            if (!Subscriptions.TryGetValue(status, out var subscription)) return;
            subscription.Listener.Dispose(); Subscriptions.Remove(status);
        });
    }
    internal static void Dispatch(StatusInstance status, BattleEntity owner, string hook, BattleDeclaration declaration)
    {
        if (DataManager.Instance == null || !DataManager.Instance.StatusActionDict.TryGetValue(status.StatusID2, out var actions)) return;
        foreach (var action in actions)
            if (action.ActionType == "OnTrigger" && ScriptHookParser.TryFind(action.ScriptHook, "Hook", out string id) && id == hook)
                owner.CharacterCtrl.TryExecuteEventStatusAction(status, action, owner, new ScriptHookContext
                { Caster = owner, Status = status, StatusHost = owner, Target = declaration?.Data.Subject,
                    Declaration = declaration }, () => status.IsActive && owner.HasStatusInstance(status) && EquipmentStatusLifetime.CanExecute(status));
    }
}

/// <summary>3191：三种余额分别恢复；真实全队付款等比例扣除，查询只读独立池。</summary>
internal sealed class TalesWeaponCount : CountStatusHandler
{
    internal const string ID = "ST_Weapon_4319101";
    private sealed class State : WeaponEventCountState
    { internal float Skill, Burst; internal readonly HashSet<long> DamageEvents = new HashSet<long>(); }
    private static bool Hexerei(StatusInstance status)
        => ScriptHookEvaluator.Evaluate("TeamBonus(Hexerei)", new ScriptHookContext { Caster = status.Caster, Status = status, StatusHost = status.Host });
    internal static void Register()
    {
        BattleSpecialRules.RegisterNumberedCondition("431910104", (_, c) =>
        {
            var damage = c?.DamageEvent; var owner = c?.Status?.WeaponContext?.Equipper;
            return owner != null && damage?.Source?.SourceEntity == owner && damage.Source.SourceKind != ReactionSourceKind.DerivedReaction
                && damage.ActualHPDamage > 0f && (damage.DamageType == "Normal" || damage.DamageType == "Skill" || damage.DamageType == "Burst")
                && !Hexerei(c.Status);
        });
    }
    internal static bool TryResolve(StatusEffectData effect, StatusInstance status, out float value)
    {
        value = 0f;
        if (effect.EffectType != "Buff" || (effect.StatusEffectID != 431910201 && effect.StatusEffectID != 431910301)
            || effect.DamageHook != "Hook(" + effect.StatusEffectID + ")") return false;
        var parent = WeaponHooks.OwnStatus(status, ID);
        if (parent?.CountRuntimeState is State state) value = effect.StatusEffectID == 431910201 ? state.Skill : state.Burst;
        return true; // 合法0不回退成Index(1)。
    }
    public override void OnCreated(StatusInstance status, object host)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, 3191);
        float maximum = WeaponHooks.Param(status, 1);
        var state = new State { Skill = maximum, Burst = maximum }; status.CountRuntimeState = state; status.RuntimeValue = maximum;
        WeaponCountLifetime.Refresh(status, owner, 0, 2);
        state.Listen<APSpentEventData>(status, owner, BattleEventKind.APSpent, (header, payment) =>
        {
            if (payment.Subject?.Side != owner.Side || payment.Amount <= 0 || !state.APEvents.Add(header.EventId)) return;
            float loss = payment.Amount * WeaponHooks.Param(status, 2);
            status.RuntimeValue = Math.Max(0f, status.RuntimeValue - loss);
            state.Skill = Math.Max(0f, state.Skill - loss); state.Burst = Math.Max(0f, state.Burst - loss);
        });
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        WeaponCountLifetime.RequireApplication(status, host, application, 3191);
        var context = WeaponCountClock.RequireWindowSource(status); var hit = context.DamageEvent;
        if (hit?.Source?.SourceEntity != host || hit.Source.SourceKind == ReactionSourceKind.DerivedReaction || !hit.HitLanded
            || context.Declaration == null) throw new InvalidOperationException("黎明回点必须来自本人正式攻击事实。");
        var state = (State)status.CountRuntimeState;
        if (!state.DamageEvents.Add(context.Declaration.Header.EventId)) return status;
        float maximum = WeaponHooks.Param(status, 1);
        if (Hexerei(status))
        {
            float gain = WeaponHooks.Param(status, 4);
            status.RuntimeValue = Math.Min(maximum, status.RuntimeValue + gain);
            state.Skill = Math.Min(maximum, state.Skill + gain); state.Burst = Math.Min(maximum, state.Burst + gain);
        }
        else
        {
            if (hit.ActualHPDamage <= 0f) throw new InvalidOperationException("非魔导黎明恢复需要实际生命伤害。");
            float gain = WeaponHooks.Param(status, 3);
            switch (hit.DamageType)
            {
                case "Normal": status.RuntimeValue = Math.Min(maximum, status.RuntimeValue + gain); break;
                case "Skill": state.Skill = Math.Min(maximum, state.Skill + gain); break;
                case "Burst": state.Burst = Math.Min(maximum, state.Burst + gain); break;
                default: throw new InvalidOperationException("非魔导黎明只恢复Normal/Skill/Burst各自余额。");
            }
        }
        return status;
    }
    public override void OnRemoved(StatusInstance status, object host) => WeaponCountClock.RemoveState(status);
}

/// <summary>3215：每个独立HPChanged增加1，满3定向建立唯一真实减费子项。</summary>
internal sealed class CashflowWeaponCount : CountStatusHandler
{
    internal const string ID = "ST_Weapon_4321503";
    private sealed class State { internal readonly HashSet<long> Events = new HashSet<long>(); }
    internal static void Register()
    {
        WeaponEventResponses.Register<HPChangedEventData>("ST_Weapon_4321502", 3215, BattleEventKind.HPChanged, "4321502",
            (status, hp) => hp.Subject == status.WeaponContext?.Equipper && hp.Before != hp.After);
        BattleSpecialRules.RegisterNumberedCondition("432150304", (_, c) => c?.Status?.StatusID2 == ID
            && c.Status.IsActive && c.Status.RuntimeValue >= 3f && EquipmentStatusLifetime.CanExecute(c.Status));
    }
    private static void Gain(StatusInstance status, BattleEntity owner)
    {
        var context = ScriptHookContext.Current;
        var hp = context?.Declaration?.Get<HPChangedEventData>();
        if (hp?.Subject != owner || hp.Before == hp.After) throw new InvalidOperationException("金流获点需要本装备者实际HP变化声明。");
        var state = (State)status.CountRuntimeState;
        if (!state.Events.Add(context.Declaration.Header.EventId)) return;
        status.RuntimeValue = Math.Min(3f, status.RuntimeValue + 1f);
        WeaponCountLifetime.Refresh(status, owner, 1, hp.Phase);
        if (status.RuntimeValue < 3f) return;
        foreach (var child in status.BoundStatuses)
            if (child.StatusID2 == "ST_Weapon_4321506" && child.IsActive && owner.HasStatusInstance(child)) return;
        StatusBindingSystem.ApplyBinding(status, owner, 432150304);
    }
    public override void OnCreated(StatusInstance status, object host)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, 3215);
        status.CountRuntimeState = new State(); status.RuntimeValue = 0f; Gain(status, owner);
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    { Gain(status, WeaponCountLifetime.RequireApplication(status, host, application, 3215)); return status; }
    public override void OnRemoved(StatusInstance status, object host) { status.RuntimeValue = 0f; status.CountRuntimeState = null; }
}

/// <summary>3205：主窗与独立1回合增益分开，只延长同批实际施加且尚有效的具体实例。</summary>
internal sealed class AbsolutionWeaponCount : CountStatusHandler
{
    internal const string ID = "ST_Weapon_4320502";
    private sealed class State : WeaponEventCountState
    { internal long StartEvent; internal StatusInstance Normal, Skill; }
    internal static void Register()
    {
        foreach (string id in new[] { "ST_Weapon_4320503", "ST_Weapon_4320504" })
            BattleSpecialRules.RegisterStatusLifecycle(id, (child, host) =>
            {
                var parent = WeaponHooks.OwnStatus(child, ID);
                var context = ScriptHookContext.Current;
                if (!(parent?.CountRuntimeState is State state) || context?.Declaration?.Header.EventId != state.StartEvent) return;
                if (child.StatusID2 == "ST_Weapon_4320503" && context.ExecutingStatusEffectID == 432050102) state.Normal = child;
                if (child.StatusID2 == "ST_Weapon_4320504" && context.ExecutingStatusEffectID == 432050103) state.Skill = child;
            }, null);
    }
    private static void Clear(BattleEntity owner, State state)
    {
        foreach (var child in new[] { state.Normal, state.Skill })
            if (child != null && child.IsActive && owner.HasStatusInstance(child)) owner.RemoveStatusInstance(child, "BoundRemoved");
        state.Normal = state.Skill = null;
    }
    private static void Extend(BattleEntity owner, StatusInstance child)
    {
        if (child != null && child.IsActive && owner.HasStatusInstance(child) && child.RemainingPhaseCount > 0)
            StatusBindingSystem.ExtendDuration(child, 1);
    }
    private static void Reset(StatusInstance status, BattleEntity owner)
    {
        if (status.CountRuntimeState is State old) { old.Dispose(); Clear(owner, old); }
        var action = ScriptHookContext.Current?.Declaration;
        if (action?.Get<ActionEventData>()?.ActionType != "Skill" || action.Data.Subject != owner)
            throw new InvalidOperationException("纷争之途必须由本人正式战技施放建立。");
        var state = new State { StartEvent = action.Header.EventId }; status.CountRuntimeState = state; status.RuntimeValue = 0f;
        WeaponCountLifetime.Refresh(status, owner, 4, 0);
        state.Listen<SwitchEventData>(status, owner, BattleEventKind.SwitchResolved, (_, change) =>
        { if (change.Outgoing == owner) Clear(owner, state); });
        state.Listen<DamageEventData>(status, owner, BattleEventKind.DamageResolved, (header, hit) =>
        {
            if (hit.Source.SourceEntity != owner || hit.Source.SourceKind == ReactionSourceKind.DerivedReaction || !hit.HitLanded
                || (hit.DamageType != "Normal" && hit.DamageType != "Heavy" && hit.DamageType != "Skill" && hit.DamageType != "Burst")) return;
            if (header.HitId <= 0L) throw new InvalidOperationException("纷争计数缺少真实HitId。");
            if (!state.Hits.Add(header.HitId)) return;
            if (hit.DamageType == "Normal" || hit.DamageType == "Heavy")
            {
                status.RuntimeValue += 1f;
                if (status.RuntimeValue == 2f) { status.RuntimeValue = 0f; Extend(owner, state.Skill); }
            }
            else Extend(owner, state.Normal);
        });
    }
    public override void OnCreated(StatusInstance status, object host) => Reset(status, WeaponCountLifetime.RequireOwner(status, host, 3205));
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    { Reset(status, WeaponCountLifetime.RequireApplication(status, host, application, 3205)); return status; }
    public override void OnRemoved(StatusInstance status, object host)
    {
        if (status.CountRuntimeState is State state && host is BattleEntity owner) { state.Dispose(); Clear(owner, state); }
        status.RuntimeValue = 0f; status.CountRuntimeState = null;
    }
}

/// <summary>3221：两个个人普通Buff独立计时；最新真实触发接管整组奖励，失效不回退旧来源。</summary>
internal sealed class NightweaverWeaponCount : CountStatusHandler
{
    internal const string ID = "ST_Weapon_4322101";
    private sealed class State : WeaponEventCountState { internal readonly HashSet<long> Triggers = new HashSet<long>(); }
    private static bool HasBoth(StatusInstance status)
        => EquipmentStatusLifetime.CanExecute(status) && WeaponHooks.OwnStatus(status, "ST_Weapon_4322102") != null
            && WeaponHooks.OwnStatus(status, "ST_Weapon_4322103") != null;
    private static bool Skill(ScriptHookContext c)
    {
        var owner = c?.Status?.WeaponContext?.Equipper; var hit = c?.DamageEvent;
        return owner != null && hit?.Source?.SourceEntity == owner && hit.Source.SourceKind != ReactionSourceKind.DerivedReaction
            && hit.ActualHPDamage > 0f && hit.DamageType == "Skill" && (hit.Element == "Hydro" || hit.Element == "Dendro");
    }
    private static bool Lunar(StatusInstance status, ReactionEventData reaction)
        => reaction != null && reaction.Reaction == ReactionType.LunarBloom && reaction.SourceWasPartyMember
            && reaction.SourceSide == status.WeaponContext.Equipper.Side;
    internal static void Register()
    {
        BattleSpecialRules.RegisterNumberedCondition("432210101", (_, c) => Skill(c));
        BattleSpecialRules.RegisterNumberedCondition("432210103", (_, c) => c?.Status != null
            && Lunar(c.Status, c.Declaration?.Get<ReactionEventData>()));
        BattleSpecialRules.RegisterNumberedCondition("432210105", (_, c) =>
        {
            var parent = WeaponHooks.OwnStatus(c?.Status, ID);
            return parent != null && parent.RuntimeValue == 1f && HasBoth(parent);
        });
    }
    private static void Revoke(StatusInstance status)
    { status.RuntimeValue = 0f; StatusBindingSystem.RemoveBindings(status); }
    public override void OnCreated(StatusInstance status, object host)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, 3221);
        var state = new State(); status.CountRuntimeState = state; status.RuntimeValue = 0f;
        WeaponCountLifetime.Refresh(status, owner, 0, 2);
        state.Listen<ReactionEventData>(status, owner, BattleEventKind.ReactionOccurred, (header, reaction) =>
        {
            if (Lunar(status, reaction)) WeaponEventResponses.Dispatch(status, owner, "432210103", BattleEventHub.Current);
        });
        state.Listen<StatusEventData>(status, owner, BattleEventKind.StatusRemoved, (_, removed) =>
        {
            if (removed.Host == owner && (removed.StatusId == "ST_Weapon_4322102" || removed.StatusId == "ST_Weapon_4322103")
                && status.RuntimeValue == 1f && !HasBoth(status)) Revoke(status);
        });
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        WeaponCountLifetime.RequireApplication(status, host, application, 3221);
        var context = WeaponCountClock.RequireWindowSource(status);
        if (context.Declaration == null || (!Skill(context) && !Lunar(status, context.Declaration.Get<ReactionEventData>())))
            throw new InvalidOperationException("纺夜认领必须来自本人水草战技生命伤害或我方真实月绽放触发。");
        var state = (State)status.CountRuntimeState;
        if (!state.Triggers.Add(context.Declaration.Header.EventId) || !HasBoth(status)) return status;
        var battle = BattleManager.Instance;
        if (battle == null) throw new InvalidOperationException("纺夜队伍奖励缺少实际编队。");
        foreach (var ally in battle.Allies)
            if (ally?.Entity != null)
                foreach (var old in ally.Entity.GetStatusInstances(ID)) Revoke(old);
        status.RuntimeValue = 1f;
        StatusBindingSystem.ApplyBindings(status, host);
        return status;
    }
    public override void OnRemoved(StatusInstance status, object host)
    { Revoke(status); WeaponCountClock.RemoveState(status); }
}
