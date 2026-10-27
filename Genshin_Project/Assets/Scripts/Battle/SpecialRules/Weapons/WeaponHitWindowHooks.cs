using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>3125/3160队友战技、3217逐hit合并事实。仍走统一状态行动入口，不另建响应队列。</summary>
internal static class WeaponHitWindowHooks
{
    internal static void Register()
    {
        WeaponEventResponses.Register<ActionEventData>("ST_Weapon_4312502", 3125, BattleEventKind.ActionStarted,
            "4312502", (status, fact) => status.StatusID2 == "ST_Weapon_4312502" && status.WeaponContext?.WeaponID == 3125
                && status.Caster == status.WeaponContext.Equipper && OtherSkill(status, fact));
        // 复用已有整组收集边界，保存/清符之间不插入由新波乱StatusAdded引发的子响应。
        BattleSpecialRules.RegisterAtomicStatusAction(HaranSigilWeaponCount.ID);
        BattleSpecialRules.RegisterNumberedCondition("4316002", (_, c) => OtherSkill(c?.Status, c?.Declaration));
        BattleSpecialRules.RegisterNumberedCondition("431600202", (_, c) => OwnSkill(c?.Status, c?.Declaration)
            && c.Status.RuntimeValue > 0f);
        BattleSpecialRules.RegisterHitScope(BeginHit);
        BattleSpecialRules.RegisterDeclarationObserver(Observe);
        BattleSpecialRules.RegisterNumberedCondition("4321703", (_, c) => Facts(c) != null);
        BattleSpecialRules.RegisterNumberedCondition("432170302", (_, c) => Facts(c)?.NormalHPDamage == true);
        BattleSpecialRules.RegisterNumberedCondition("432170303", (_, c) => Facts(c)?.Vaporize == true);
    }

    internal static bool OwnSkill(StatusInstance status, BattleDeclaration declaration)
    {
        var action = declaration?.Get<ActionEventData>();
        return declaration?.Kind == BattleEventKind.ActionStarted && action?.ActionType == "Skill"
            && action.Subject == status?.WeaponContext?.Equipper && action.Subject != null;
    }

    internal static bool OtherSkill(StatusInstance status, BattleDeclaration declaration)
        => OtherSkill(status, declaration?.Get<ActionEventData>());

    private static bool OtherSkill(StatusInstance status, ActionEventData action)
    {
        var owner = status?.WeaponContext?.Equipper;
        if (owner == null || action?.Kind != BattleEventKind.ActionStarted || action.ActionType != "Skill"
            || action.Subject == null || action.Subject == owner || action.Subject.Side != owner.Side) return false;
        var battle = BattleManager.Instance;
        if (battle == null || owner.Side != BattleSide.Ally) return false;
        bool hasOwner = false, hasActor = false;
        foreach (var ally in battle.Allies)
        {
            if (ally?.Entity == owner) hasOwner = true;
            if (ally?.Entity == action.Subject) hasActor = true;
        }
        return hasOwner && hasActor;
    }

    internal static void Request(StatusInstance status, BattleEntity owner, string hook,
        BattleDeclaration declaration, Func<bool> current)
    {
        var data = DataManager.Instance;
        if (data == null || !data.StatusActionDict.TryGetValue(status.StatusID2, out var actions)) return;
        foreach (var action in actions)
        {
            if (action.ActionType != "OnTrigger" || !ScriptHookParser.TryFind(action.ScriptHook, "Hook", out string id)
                || id?.Trim() != hook) continue;
            owner.CharacterCtrl.TryExecuteEventStatusAction(status, action, owner, new ScriptHookContext
            { Caster = owner, Status = status, StatusHost = owner, Declaration = declaration }, current);
        }
    }

    private sealed class HitFacts
    {
        internal long Hit;
        internal BattleEntity Owner;
        internal StatusInstance Status;
        internal object Generation;
        internal bool NormalHPDamage, Vaporize, Closed;
        internal BattleDeclaration Anchor;
        internal bool Current => Status != null && Status.IsActive && Owner.HasStatusInstance(Status)
            && ReferenceEquals(Status.CountRuntimeState, Generation) && EquipmentStatusLifetime.CanExecute(Status);
    }
    private sealed class HitScope : IDisposable
    {
        private readonly HitFacts previous, facts;
        private bool disposed;
        internal HitScope(HitFacts value) { previous = Current.Value; facts = value; Current.Value = value; }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; Current.Value = previous;
            facts.Closed = true;
            if (!facts.Current || facts.Anchor == null || (!facts.NormalHPDamage && !facts.Vaporize)) return;
            if (!SurfingTimeWeaponCount.ClaimHit(facts.Status, facts.Hit)) return;
            Frozen.Add(facts.Anchor, facts);
            // 外层hit收集尚未结束，按原ApplyOrder提交候选；不会在反应预计算时改层。
            using (BattleEventHub.Enter(facts.Anchor))
                Request(facts.Status, facts.Owner, "4321703", facts.Anchor, () => facts.Current);
        }
    }
    private sealed class EmptyScope : IDisposable { public void Dispose() { } }
    private static readonly AsyncLocal<HitFacts> Current = new AsyncLocal<HitFacts>();
    private static readonly ConditionalWeakTable<BattleDeclaration, HitFacts> Frozen
        = new ConditionalWeakTable<BattleDeclaration, HitFacts>();

    private static IDisposable BeginHit(long hit, BattleEntity owner, IEnumerable<BattleEntity> targets)
    {
        // 同一hit身份的派生分支共享收集；真正嵌套的新hit用自己的帧，不回写父帧。
        if (hit != 0 && Current.Value?.Hit == hit && Current.Value.Owner == owner) return new EmptyScope();
        var status = owner?.GetStatus(SurfingTimeWeaponCount.ID);
        return new HitScope(new HitFacts { Hit = hit, Owner = owner, Status = status,
            Generation = status?.CountRuntimeState });
    }

    private static void Observe(BattleDeclaration declaration)
    {
        var facts = Current.Value;
        if (facts == null || facts.Closed || facts.Hit == 0 || declaration.Header.HitId != facts.Hit || !facts.Current) return;
        var damage = declaration.Get<DamageEventData>();
        var reaction = declaration.Get<ReactionEventData>();
        bool normal = damage != null && damage.Source.SourceEntity == facts.Owner
            && damage.Source.SourceKind != ReactionSourceKind.DerivedReaction && damage.Subject != null
            && damage.Subject.Side != facts.Owner.Side && damage.DamageType == "Normal"
            && damage.HitLanded && damage.ActualHPDamage > 0f;
        bool vaporize = reaction != null && reaction.Source == facts.Owner && reaction.Reaction == ReactionType.Vaporize;
        if (!normal && !vaporize) return;
        facts.NormalHPDamage |= normal; facts.Vaporize |= vaporize;
        facts.Anchor = facts.Anchor ?? declaration;
    }

    private static HitFacts Facts(ScriptHookContext context)
    {
        return context?.Declaration != null && Frozen.TryGetValue(context.Declaration, out var facts)
            && ReferenceEquals(context.Status, facts.Status) && facts.Current ? facts : null;
    }
}

/// <summary>3160波穗：永久0..2；本来源正式战技先转存到波乱，再清零。</summary>
internal sealed class HaranSigilWeaponCount : CountStatusHandler
{
    internal const string ID = "ST_Weapon_4316002";
    private sealed class State : IDisposable
    {
        internal IDisposable Subscription;
        internal readonly HashSet<long> Actions = new HashSet<long>();
        public void Dispose() { Subscription?.Dispose(); Actions.Clear(); }
    }
    public override void OnCreated(StatusInstance status, object host)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, 3160);
        status.RuntimeValue = 0f;
        WeaponCountLifetime.Refresh(status, owner, 0, 0);
        var state = new State(); status.CountRuntimeState = state;
        bool Current() => status.IsActive && owner.HasStatusInstance(status) && EquipmentStatusLifetime.CanExecute(status);
        state.Subscription = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted, (header, action) =>
        {
            var declaration = BattleEventHub.Current;
            if (WeaponHitWindowHooks.OtherSkill(status, declaration))
                WeaponHitWindowHooks.Request(status, owner, "4316002", declaration, Current);
        }, status.ApplyOrder, Current);
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        WeaponCountLifetime.RequireApplication(status, host, application, 3160);
        var context = WeaponCountClock.RequireWindowSource(status);
        var declaration = context.Declaration;
        bool own = WeaponHitWindowHooks.OwnSkill(status, declaration);
        if (!own && !WeaponHitWindowHooks.OtherSkill(status, declaration))
            throw new InvalidOperationException("波穗重施必须来自本场正式战技施放声明。");
        var state = (State)status.CountRuntimeState;
        if (state.Actions.Contains(declaration.Header.EventId)) return status;
        if (own)
        {
            var reward = WeaponHooks.OwnStatus(status, HaranBonusWeaponCount.ID);
            if (reward == null || !(reward.CountRuntimeState is long saved) || saved != declaration.Header.EventId
                || reward.RuntimeValue != status.RuntimeValue)
                throw new InvalidOperationException("波穗必须先成功转存本次份数，再清零。");
        }
        if (state.Actions.Add(declaration.Header.EventId))
            status.RuntimeValue = own ? 0f : Math.Min(2f, status.RuntimeValue + 1f);
        return status;
    }
    public override void OnRemoved(StatusInstance status, object host)
    { (status.CountRuntimeState as State)?.Dispose(); status.CountRuntimeState = null; status.RuntimeValue = 0f; }
}

internal sealed class HaranBonusWeaponCount : CountStatusHandler
{
    internal const string ID = "ST_Weapon_4316003";
    private static void Save(StatusInstance status, BattleEntity owner)
    {
        var context = ScriptHookContext.Current;
        var sigils = WeaponHooks.OwnStatus(status, HaranSigilWeaponCount.ID);
        if (context == null || sigils == null || context.Status != sigils || !WeaponHitWindowHooks.OwnSkill(sigils, context.Declaration)
            || sigils.RuntimeValue < 1f || sigils.RuntimeValue > 2f || sigils.RuntimeValue != (int)sigils.RuntimeValue)
            throw new InvalidOperationException("波乱必须在清零前转存本来源实际1或2枚波穗。");
        status.RuntimeValue = sigils.RuntimeValue;
        status.CountRuntimeState = context.Declaration.Header.EventId;
        WeaponCountLifetime.Refresh(status, owner, 2, context.Declaration.Data.Phase);
    }
    public override void OnCreated(StatusInstance status, object host) => Save(status, WeaponCountLifetime.RequireOwner(status, host, 3160));
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    { Save(status, WeaponCountLifetime.RequireApplication(status, host, application, 3160)); return status; }
    public override void OnRemoved(StatusInstance status, object host) { status.CountRuntimeState = null; status.RuntimeValue = 0f; }
}

/// <summary>3217炽夏：Skill重置4/4回合，伤害后专用效果扣/加各1而不续期；0也保留窗口。</summary>
internal sealed class SurfingTimeWeaponCount : CountStatusHandler
{
    internal const string ID = "ST_Weapon_4321703";
    private sealed class Generation { internal readonly HashSet<long> Hits = new HashSet<long>(); }
    internal static bool ClaimHit(StatusInstance status, long hit)
        => status.CountRuntimeState is Generation generation && generation.Hits.Add(hit);
    private static void Reset(StatusInstance status, BattleEntity owner)
    {
        status.RuntimeValue = 4f; status.CountRuntimeState = new Generation();
        WeaponCountLifetime.Refresh(status, owner, 4, 0);
    }
    public override void OnCreated(StatusInstance status, object host) => Reset(status, WeaponCountLifetime.RequireOwner(status, host, 3217));
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        WeaponCountLifetime.RequireApplication(status, host, application, 3217);
        var context = ScriptHookContext.Current;
        switch (context?.ExecutingStatusEffectID ?? 0)
        {
            case 432170201: Reset(status, host); break;
            case 432170302:
            case 432170303:
                if (context.Status != status || !status.IsActive || !host.HasStatusInstance(status))
                    throw new InvalidOperationException("炽夏扣加必须来自仍有效的本次窗口。");
                status.RuntimeValue = context.ExecutingStatusEffectID == 432170302
                    ? Math.Max(0f, status.RuntimeValue - 1f) : Math.Min(4f, status.RuntimeValue + 1f);
                break;
            default: throw new InvalidOperationException("炽夏只接受开窗、扣层、回层三个专用效果。");
        }
        return status;
    }
    public override void OnRemoved(StatusInstance status, object host) { status.RuntimeValue = 0f; status.CountRuntimeState = null; }
}

/// <summary>3217常驻HP百分比：开场使用角色生命基础，与突破/武器副属性/圣遗物加成相加。</summary>
internal static class SurfingTimeInitialHP
{
    private const string ID = "ST_Weapon_4321701";
    private sealed class Applied { internal float Value, Percent; }
    private static readonly ConditionalWeakTable<BattleEntity, Applied> Values = new ConditionalWeakTable<BattleEntity, Applied>();
    internal static void Register()
    {
        BattleSpecialRules.RegisterBattleReady(battle =>
        {
            foreach (var ally in battle.Allies)
            {
                var owner = ally?.Entity; var status = owner?.GetStatus(ID);
                if (status == null || !status.IsActive || !EquipmentStatusLifetime.CanExecute(status)) continue;
                WeaponCountLifetime.RequireOwner(status, owner, 3217);
                var applied = Values.GetValue(owner, _ => new Applied());
                float percent = WeaponHooks.Param(status, 1);
                float value = owner.CharacterCtrl.GetHPBeforePercentBonuses() * percent;
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f) throw new InvalidOperationException("冲浪生命加成必须为有限非负值。");
                float delta = value - applied.Value;
                status.HPBonusIncludedInPanel = true;
                owner.BakedHPBonus += percent - applied.Percent; applied.Percent = percent;
                owner.TotalHP += delta; applied.Value = value;
                // 当前HP由完整开战初始化的统一收尾写入。
            }
        });
        BattleSpecialRules.RegisterStatusLifecycle(ID, null, (status, host) => { if (host is BattleEntity owner) Reset(owner); });
        BattleSpecialRules.RegisterEntityReset(Reset);
    }
    private static void Reset(BattleEntity owner)
    {
        if (!Values.TryGetValue(owner, out var applied)) return;
        float previousHP = owner.CurrentHP;
        owner.TotalHP = Math.Max(0f, owner.TotalHP - applied.Value);
        owner.BakedHPBonus -= applied.Percent;
        owner.CurrentHP = Math.Min(owner.CurrentHP, owner.GetFinalHP());
        Values.Remove(owner);
        var battle = BattleManager.Instance;
        if (battle != null && battle.IsBattleRunning && !battle.IsBattleOver && previousHP != owner.CurrentHP)
            BattleEventHub.Publish(new HPChangedEventData(owner, previousHP, owner.CurrentHP, "MaxHPAdjustment"));
    }
}
