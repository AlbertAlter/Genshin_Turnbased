using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>3102：只筛真实DamageType=Heavy；整组伤害完成才清点，不要求Heavy主动行为。</summary>
internal sealed class PlumageWeaponCount : CountStatusHandler
{
    internal const string ID = "ST_Weapon_4310201";
    private static readonly AsyncLocal<Invocation> Current = new AsyncLocal<Invocation>();
    private sealed class Invocation : IDisposable
    {
        internal readonly Invocation Previous;
        internal readonly BattleEntity Owner;
        internal readonly StatusInstance Status;
        internal readonly object Generation;
        // 完整Damage入口登记全部执行身份（含True）；Prepare只准许Heavy参与增伤查询。
        internal readonly Dictionary<long, string> Executions = new Dictionary<long, string>();
        internal readonly Dictionary<long, string> Effects = new Dictionary<long, string>();
        internal bool Damaged, Disposed;
        internal Invocation(BattleEntity owner)
        {
            Previous = Current.Value; Owner = owner;
            Status = owner?.GetStatus(ID); Generation = Status?.CountRuntimeState;
            Current.Value = this;
        }
        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            try
            {
                if (Damaged && Status != null && Status.IsActive && Owner.HasStatusInstance(Status)
                    && EquipmentStatusLifetime.CanExecute(Status) && ReferenceEquals(Status.CountRuntimeState, Generation))
                    Status.RuntimeValue = 0f; // 不销毁管理器，不清付款余数；异常/取消后已提交的伤害同样算数。
            }
            finally { Current.Value = Previous; }
        }
    }
    internal static void Register()
    {
        // SK、普通状态行动均使用真实执行组；跨回合的后续容器不需要继承起手Action/AP。
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => new Invocation(owner));
        BattleSpecialRules.RegisterStatusActionScope(status => new Invocation(status?.Caster));
        BattleSpecialRules.RegisterDamageEffectScope((owner, effect, execution) =>
        {
            var call = Current.Value;
            Invocation fallback = null;
            if (call == null || call.Disposed || call.Owner != owner)
                call = fallback = new Invocation(owner);
            call.Executions[execution] = effect;
            return fallback; // 没有SK/状态行动容器时，以这个完整Damage为组，不在单hit清点。
        }, (_, __) => { });
        BattleSpecialRules.RegisterNumberedCondition("4310201", (_, c) =>
        {
            var call = Current.Value;
            return call != null && !call.Disposed && call.Status != null && c != null
                && c.Status == call.Status && call.Status.IsActive && call.Owner.HasStatusInstance(call.Status)
                && EquipmentStatusLifetime.CanExecute(call.Status)
                && c.Recipient == call.Owner && c.DamageType == "Heavy"
                && call.Effects.TryGetValue(BattleEventHub.CurrentEffectExecutionId, out string effect)
                && effect == c.HitEffectID;
        });
        BattleSpecialRules.RegisterDeclarationObserver(declaration =>
        {
            var call = Current.Value;
            var damage = declaration.Get<DamageEventData>();
            if (call == null || call.Disposed || damage == null || !damage.HitLanded
                || damage.ActualHPDamage <= 0f || damage.Source.SourceEntity != call.Owner
                || damage.DamageType != "Heavy"
                || !call.Executions.TryGetValue(declaration.Header.EffectExecutionId, out string effect)
                || effect != damage.Source.SourceEffectID) return;
            call.Damaged = true; // 仅索引已提交事实；清点只在完整技能作用域释放时发生。True也能记录，但不获取增伤。
        });
    }
    public override void OnCreated(StatusInstance status, object host)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, 3102);
        status.RuntimeValue = 0f; status.CountRuntimeState = new WeaponEventCountState();
        WeaponCountLifetime.Refresh(status, owner, 0, 2);
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        WeaponCountLifetime.RequireApplication(status, host, application, 3102);
        var context = WeaponCountClock.RequireWindowSource(status);
        var payment = context.Declaration?.Get<APSpentEventData>();
        if (payment == null || payment.Subject?.Side != host.Side || payment.Amount <= 0)
            throw new InvalidOperationException("缀花之翎只接受本队正数实际AP付款声明。");
        var state = (WeaponEventCountState)status.CountRuntimeState;
        if (!state.APEvents.Add(context.Declaration.Header.EventId)) return status;
        long total = (long)state.APRemainder + payment.Amount;
        state.APRemainder = (int)(total % 10L);
        status.RuntimeValue = Math.Min(6f, status.RuntimeValue + total / 10L);
        return status;
    }
    public override CountDamageContribution PrepareDamage(StatusInstance status, object host, CountDamageContext context)
    {
        var call = Current.Value;
        if (call != null && call.Owner == host && call.Status == status && !call.Disposed
            && context.Attacker == host && context.DamageType == "Heavy" && context.EffectExecutionID > 0
            && call.Executions.TryGetValue(context.EffectExecutionID, out string effect) && effect == context.EffectID)
            call.Effects[context.EffectExecutionID] = context.EffectID;
        return null; // 不在单hit消费，也不把增伤误放到基础伤害乘区。
    }
    public override void OnRemoved(StatusInstance status, object host) => WeaponCountClock.RemoveState(status);
}

/// <summary>3145：资格永久存0/1，连续后台满1回合获得，连续前台满2回合失效。</summary>
internal sealed class SacrificialJadeWeaponCount : CountStatusHandler
{
    internal const string ID = "ST_Weapon_4314501";
    private static void SetField(StatusInstance status, BattleEntity owner, WeaponEventCountState state, bool onField, long time)
    {
        state.OnField = onField;
        state.NextTick = onField ? (status.RuntimeValue > 0f ? time + 12L : -1L)
            : status.RuntimeValue > 0f ? -1L : time + 6L;
        WeaponCountClock.Schedule(status, owner, state.NextTick);
    }
    public override void OnCreated(StatusInstance status, object host)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, 3145);
        var state = new WeaponEventCountState(); status.CountRuntimeState = state; status.RuntimeValue = 0f;
        WeaponCountClock.Schedule(status, owner, -1L);
        state.WhenBattleReady = battle =>
        {
            if (state.LayoutReady) return;
            state.LayoutReady = true;
            SetField(status, owner, state, owner.CharacterCtrl.IsActive, WeaponCountClock.Now());
        };
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        WeaponCountLifetime.RequireApplication(status, host, application, 3145);
        var context = WeaponCountClock.RequireWindowSource(status);
        var change = context.Declaration?.Get<SwitchEventData>();
        var state = (WeaponEventCountState)status.CountRuntimeState;
        if (!state.LayoutReady || change == null || (change.Subject != host && change.Outgoing != host))
            throw new InvalidOperationException("玉珑资格重施必须来自完整编队后的本装备者正式换人事实。");
        if (state.SwitchEvents.Add(context.Declaration.Header.EventId))
            SetField(status, host, state, change.Subject == host, WeaponCountClock.At(change.Turn, change.Phase));
        return status;
    }
    public override void OnItsTurn(StatusInstance status, object host)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, 3145);
        var state = (WeaponEventCountState)status.CountRuntimeState;
        if (!state.LayoutReady || state.NextTick < 0L || (int)BattleManager.Instance.CurrentPhase != status.TriggerPhase
            || WeaponCountClock.Now() < state.NextTick) return;
        status.RuntimeValue = state.OnField ? 0f : 1f;
        state.NextTick = -1L;
        WeaponCountClock.Schedule(status, owner, state.NextTick);
        if (state.OnField) owner.ClampCurrentHPToMaximum();
    }
    public override void OnRemoved(StatusInstance status, object host)
    {
        WeaponCountClock.RemoveState(status);
        if (host is BattleEntity owner) owner.ClampCurrentHPToMaximum();
    }
}

/// <summary>仅3159/3184/3185/3189明确规则：逐来源独立期限，RuntimeValue保存最终一档属性比例。</summary>
internal sealed class WeaponQualificationCount : CountStatusHandler
{
    private readonly int weaponID;
    internal WeaponQualificationCount(int weapon)
    {
        if (weapon != 3159 && weapon != 3184 && weapon != 3185 && weapon != 3189) throw new ArgumentOutOfRangeException(nameof(weapon));
        weaponID = weapon;
    }
    private sealed class State : WeaponEventCountState
    {
        internal bool Energy;
        internal int Count;
        internal long CritUntil = -1L;
        internal readonly Dictionary<string, long> Deadlines = new Dictionary<string, long>(StringComparer.Ordinal);
        internal readonly HashSet<long> Events = new HashSet<long>();
    }
    internal static void Register()
    {
        BattleSpecialRules.RegisterNumberedCondition("431590202", (_, c) =>
        {
            var damage = c?.DamageEvent;
            return damage?.Source?.SourceEntity == c?.Status?.WeaponContext?.Equipper
                && damage?.Source?.SourceKind != ReactionSourceKind.DerivedReaction && damage?.DamageType == "Normal"
                && damage.ActualHPDamage > 0f && WeaponHooks.IsElemental(damage.Element);
        });
        foreach (string id in new[] { "431590204", "431840204" })
            BattleSpecialRules.RegisterNumberedCondition(id, (_, c) =>
            {
                var resource = c?.Declaration?.Get<ResourceEventData>();
                return resource != null && resource.Subject == c.Status?.WeaponContext?.Equipper
                    && resource.Resource == "Energy" && resource.Before != resource.After;
            });
        BattleSpecialRules.RegisterNumberedCondition("431890104", (_, c) =>
        {
            var resource = c?.Declaration?.Get<ResourceEventData>();
            return resource != null && resource.Subject == c.Status?.WeaponContext?.Equipper
                && resource.Resource == "BondOfLife" && resource.After > resource.Before;
        });
        BattleSpecialRules.RegisterNumberedCondition("4318902", (_, c) =>
        {
            var parent = WeaponHooks.OwnStatus(c?.Status, "ST_Weapon_4318901");
            return parent?.CountRuntimeState is State state && (state.Count == 3 || state.CritUntil >= 0L);
        });
    }
    private static void Recalculate(StatusInstance status, BattleEntity owner, State state)
    {
        int count = state.Deadlines.Count + (state.Energy ? 1 : 0);
        bool heartstrings = status.WeaponContext.WeaponID == 3189;
        float before = status.RuntimeValue;
        if (heartstrings)
        {
            if (count == 3) state.CritUntil = -1L;
            else if (state.Count == 3) state.CritUntil = WeaponCountClock.Now() + 6L;
        }
        state.Count = count;
        status.RuntimeValue = count == 0 ? 0f : WeaponHooks.Param(status, count + (heartstrings ? 0 : 1));
        long next = -1L;
        foreach (long deadline in state.Deadlines.Values) if (next < 0L || deadline < next) next = deadline;
        if (state.CritUntil >= 0L && (next < 0L || state.CritUntil < next)) next = state.CritUntil;
        WeaponCountClock.Schedule(status, owner, next);
        if (heartstrings && before > status.RuntimeValue) owner.ClampCurrentHPToMaximum();
    }
    public override void OnCreated(StatusInstance status, object host)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, weaponID);
        var state = new State(); status.CountRuntimeState = state; status.RuntimeValue = 0f;
        WeaponCountClock.Schedule(status, owner, -1L);
        state.WhenBattleReady = battle =>
        {
            if (state.LayoutReady) return;
            state.LayoutReady = true;
            state.Energy = (weaponID == 3159 || weaponID == 3184) && owner.MaxEnergy > 0 && owner.CurrentEnergy < owner.MaxEnergy;
            Recalculate(status, owner, state);
        };
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        WeaponCountLifetime.RequireApplication(status, host, application, weaponID);
        var context = WeaponCountClock.RequireWindowSource(status);
        var declaration = context.Declaration;
        var state = (State)status.CountRuntimeState;
        if (!state.LayoutReady || declaration == null) throw new InvalidOperationException("武器独立资格需要真实编队及声明上下文。");
        var damage = declaration.Get<DamageEventData>();
        var action = declaration.Get<ActionEventData>();
        var resource = declaration.Get<ResourceEventData>();
        var heal = declaration.Get<HealEventData>();
        string source = null; long deadline = -1L;
        if (weaponID == 3189)
        {
            if (action?.Kind == BattleEventKind.ActionStarted && action.Subject == host && action.ActionType == "Skill")
            { source = "Skill"; deadline = WeaponCountClock.At(action.Turn, action.Phase) + 36L; }
            else if (resource?.Subject == host && resource.Resource == "BondOfLife" && resource.Added > 0d)
            { source = "BondOfLife"; deadline = WeaponCountClock.At(resource.Turn, resource.Phase) + 36L; }
            else if (heal?.Source == host)
            { source = "Healing"; deadline = WeaponCountClock.At(heal.Turn, heal.Phase) + 30L; }
            else throw new InvalidOperationException("疗护只接受本人Skill施放、实际契值增加或本人正式进行治疗。");
        }
        else if (damage != null)
        {
            bool own = damage.Source.SourceEntity == host && damage.Source.SourceKind != ReactionSourceKind.DerivedReaction && damage.HitLanded;
            if (weaponID == 3185)
            {
                if (!own || (damage.DamageType != "Normal" && damage.DamageType != "Heavy"
                    && damage.DamageType != "Skill" && damage.DamageType != "Burst"))
                    throw new InvalidOperationException("冬极只接受本人四种实际DamageType的攻击命中。");
                source = damage.DamageType; deadline = WeaponCountClock.At(damage.Turn, damage.Phase) + 18L;
            }
            else
            {
                if (!own || damage.DamageType != "Normal" || damage.ActualHPDamage <= 0f
                    || (weaponID == 3159 && !WeaponHooks.IsElemental(damage.Element)))
                    throw new InvalidOperationException("雾切/飞雷普攻资格必须由本次实际普攻生命伤害取得；雾切还须为七元素。");
                source = "Normal"; deadline = WeaponCountClock.At(damage.Turn, damage.Phase) + 6L;
            }
        }
        else if (action != null && weaponID != 3185)
        {
            string required = weaponID == 3159 ? "Burst" : "Skill";
            if (action.Kind != BattleEventKind.ActionStarted || action.Subject != host || action.ActionType != required)
                throw new InvalidOperationException("武器施放资格必须来自本人正式对应ActionStarted。");
            source = required; deadline = WeaponCountClock.At(action.Turn, action.Phase) + 18L;
        }
        else if (resource != null && weaponID != 3185)
        {
            if (resource.Subject != host || resource.Resource != "Energy" || resource.Before == resource.After)
                throw new InvalidOperationException("能量资格只读取本人Energy实际变化。");
        }
        else throw new InvalidOperationException("武器独立资格没有匹配当前声明的规则。");
        if (!state.Events.Add(declaration.Header.EventId)) return status;
        if (source != null) state.Deadlines[source] = deadline;
        else state.Energy = host.MaxEnergy > 0 && resource.After < host.MaxEnergy;
        Recalculate(status, host, state);
        return status;
    }
    public override void OnItsTurn(StatusInstance status, object host)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, weaponID);
        var state = (State)status.CountRuntimeState;
        if (!state.LayoutReady || (int)BattleManager.Instance.CurrentPhase != status.TriggerPhase) return;
        long now = WeaponCountClock.Now();
        if (state.CritUntil >= 0L && state.CritUntil <= now) state.CritUntil = -1L;
        foreach (string source in new List<string>(state.Deadlines.Keys))
            if (state.Deadlines[source] <= now) state.Deadlines.Remove(source);
        Recalculate(status, owner, state);
    }
    public override void OnRemoved(StatusInstance status, object host)
    {
        WeaponCountClock.RemoveState(status);
        if (weaponID == 3189 && host is BattleEntity owner) owner.ClampCurrentHPToMaximum();
    }
}

/// <summary>3178：2回合窗内4个合格逻辑hit只延长一次1回合；0是合法进度，不是失效。</summary>
internal sealed class BlazingSunsWeaponCount : CountStatusHandler
{
    internal const string ID = "ST_Weapon_4317802";
    private static void Reset(StatusInstance status, BattleEntity owner)
    {
        (status.CountRuntimeState as WeaponEventCountState)?.Dispose();
        var state = new WeaponEventCountState(); status.CountRuntimeState = state; status.RuntimeValue = 0f;
        WeaponCountLifetime.Refresh(status, owner, 2, 0);
        state.Listen<DamageEventData>(status, owner, BattleEventKind.DamageResolved, (header, damage) =>
        {
            if (status.RuntimeValue >= 4f || damage.Source.SourceEntity != owner
                || damage.Source.SourceKind == ReactionSourceKind.DerivedReaction || !damage.HitLanded
                || damage.ActualHPDamage <= 0f || !WeaponHooks.IsElemental(damage.Element)
                || (damage.DamageType != "Normal" && damage.DamageType != "Heavy")) return;
            if (header.HitId <= 0L) throw new InvalidOperationException("焚光进度缺少真实逻辑hit身份。");
            if (!state.Hits.Add(header.HitId)) return;
            status.RuntimeValue += 1f;
            if (status.RuntimeValue == 4f) StatusBindingSystem.ExtendDuration(status, 1);
        });
    }
    public override void OnCreated(StatusInstance status, object host) => Reset(status, WeaponCountLifetime.RequireOwner(status, host, 3178));
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    { Reset(status, WeaponCountLifetime.RequireApplication(status, host, application, 3178)); return status; }
    public override void OnRemoved(StatusInstance status, object host) => WeaponCountClock.RemoveState(status);
}
