using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>柔灯/万世先正式获点再请求独立回能；柔灯/帷间月曜OR事实仍复用原Hit与行动账本。</summary>
internal static class WeaponCountAwardHooks
{
    private sealed class Listeners : IDisposable
    {
        internal readonly List<IDisposable> Items = new List<IDisposable>();
        internal readonly HashSet<long> Events = new HashSet<long>();
        public void Dispose() { foreach (var item in Items) item.Dispose(); }
    }
    private sealed class Award { internal StatusInstance Count; internal float Value; }
    private static readonly ConditionalWeakTable<StatusInstance, Listeners> Subscriptions = new ConditionalWeakTable<StatusInstance, Listeners>();
    private static readonly ConditionalWeakTable<BattleDeclaration, Dictionary<StatusInstance, Award>> Awards
        = new ConditionalWeakTable<BattleDeclaration, Dictionary<StatusInstance, Award>>();

    internal static void Register()
    {
        RegisterOR("ST_Weapon_4320102", 3201, "4320102", Burning);
        RegisterOR("ST_Weapon_4322302", 3223, "4322302", Lunar);
        WeaponEventResponses.Register<HPChangedEventData>("ST_Weapon_4321402", 3214, BattleEventKind.HPChanged,
            "4321402", (status, fact) => fact.Subject == status.WeaponContext?.Equipper && fact.Before != fact.After);
        BattleSpecialRules.RegisterNumberedCondition("432010202", (_, c) => AwardMatches(c, 3201, "ST_Weapon_4320103", 2));
        BattleSpecialRules.RegisterNumberedCondition("432140202", (_, c) => AwardMatches(c, 3214, "ST_Weapon_4321403", 3));
        BattleSpecialRules.RegisterHitScope(BeginHit);
        BattleSpecialRules.RegisterDeclarationObserver(Observe);
        BattleSpecialRules.RegisterStatusLifecycle("ST_Weapon_4322303", null, (status, host) =>
        { if (host is BattleEntity owner) owner.ClampCurrentHPToMaximum(); });
        BattleSpecialRules.RegisterNumberedCondition("4322304", (_, c) => c?.Status?.StatusID2 == "ST_Weapon_4322304"
            && c.Status.WeaponContext?.WeaponID == 3223 && c.DamageReaction.HasValue
            && ReactionDamageCalculator.IsLunar(c.DamageReaction.Value));
    }

    private static void RegisterOR(string id, int weapon, string hook, Func<StatusInstance, BattleDeclaration, bool> matches)
    {
        BattleSpecialRules.RegisterNumberedCondition(hook, (_, c) => c?.Status?.StatusID2 == id
            && c.Status.WeaponContext?.WeaponID == weapon && c.Declaration != null && matches(c.Status, c.Declaration));
        BattleSpecialRules.RegisterStatusLifecycle(id, (status, host) =>
        {
            var owner = WeaponCountLifetime.RequireOwner(status, host, weapon); var state = new Listeners(); Subscriptions.Add(status, state);
            bool Current() => status.IsActive && owner.HasStatusInstance(status) && EquipmentStatusLifetime.CanExecute(status);
            void Receive(BattleEventHeader header)
            {
                var declaration = BattleEventHub.Current;
                if (matches(status, declaration) && state.Events.Add(header.EventId)) WeaponEventResponses.Dispatch(status, owner, hook, declaration);
            }
            state.Items.Add(BattleEventHub.Subscribe<ReactionEventData>(BattleEventKind.ReactionOccurred, (header, _) => Receive(header), status.ApplyOrder, Current));
            state.Items.Add(BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (header, _) => Receive(header), status.ApplyOrder, Current));
        }, (status, host) =>
        { if (Subscriptions.TryGetValue(status, out var state)) { state.Dispose(); Subscriptions.Remove(status); } });
    }

    private static bool Burning(StatusInstance status, BattleDeclaration declaration)
    {
        var owner = status.WeaponContext?.Equipper;
        if (owner == null || status.Caster != owner) return false;
        var reaction = declaration.Get<ReactionEventData>();
        if (reaction != null) return reaction.Source == owner && reaction.Reaction == ReactionType.Burning;
        var damage = declaration.Get<DamageEventData>();
        return damage != null && damage.Source.SourceEntity == owner && damage.Source.CountsAsHit
            && damage.Source.SourceKind != ReactionSourceKind.DerivedReaction && damage.Element == "Dendro"
            && damage.ActualHPDamage > 0f && OldBurning.TryGetValue(declaration, out var saved) && saved.Owner == owner;
    }

    private static bool Lunar(StatusInstance status, BattleDeclaration declaration)
    {
        var owner = status.WeaponContext?.Equipper;
        if (owner == null || status.Caster != owner) return false;
        var reaction = declaration.Get<ReactionEventData>();
        if (reaction != null) return reaction.Source == owner && ReactionDamageCalculator.IsLunar(reaction.Reaction);
        var damage = declaration.Get<DamageEventData>();
        return damage != null && damage.Source.SourceEntity == owner && damage.ActualHPDamage > 0f
            && ReactionDamageCalculator.IsLunar(damage.Source.ReactionType);
    }

    internal static void Awarded(StatusInstance count, BattleEntity owner, int weapon, int maximum)
    {
        if (count.RuntimeValue < maximum) return;
        var context = ScriptHookContext.Current; var listener = context?.Status; var declaration = context?.Declaration;
        string expected = weapon == 3201 ? "ST_Weapon_4320102" : "ST_Weapon_4321402";
        if (listener?.StatusID2 != expected || listener.Caster != owner || listener.WeaponContext?.Equipper != owner
            || listener.WeaponContext.SourceInstanceID != count.WeaponContext.SourceInstanceID || declaration == null)
            throw new InvalidOperationException("满额回能必须由本来源监听的本次正式获点驱动。");
        Awards.GetOrCreateValue(declaration)[listener] = new Award { Count = count, Value = count.RuntimeValue };
        WeaponHitWindowHooks.Request(listener, owner, weapon == 3201 ? "432010202" : "432140202", declaration,
            () => listener.IsActive && owner.HasStatusInstance(listener) && EquipmentStatusLifetime.CanExecute(listener));
    }

    private static bool AwardMatches(ScriptHookContext c, int weapon, string countID, int minimum)
        => c?.Status?.WeaponContext?.WeaponID == weapon && c.Declaration != null && Awards.TryGetValue(c.Declaration, out var awards)
            && awards.TryGetValue(c.Status, out var award) && award.Count.StatusID2 == countID
            && award.Count.WeaponContext.SourceInstanceID == c.Status.WeaponContext.SourceInstanceID && award.Value >= minimum;

    private sealed class BurnFrame : IDisposable
    {
        internal BurnFrame Previous; internal BattleEntity Owner; internal long Hit;
        internal readonly HashSet<BattleEntity> Targets = new HashSet<BattleEntity>();
        public void Dispose() => Current.Value = Previous;
    }
    private sealed class BurnFact { internal BattleEntity Owner; }
    private static readonly AsyncLocal<BurnFrame> Current = new AsyncLocal<BurnFrame>();
    private static readonly ConditionalWeakTable<BattleDeclaration, BurnFact> OldBurning = new ConditionalWeakTable<BattleDeclaration, BurnFact>();
    private static IDisposable BeginHit(long id, BattleEntity owner, IEnumerable<BattleEntity> targets)
    {
        var frame = new BurnFrame { Previous = Current.Value, Owner = owner, Hit = id };
        if (owner?.GetStatus("ST_Weapon_4320102") != null && targets != null)
            foreach (var target in targets) if (BurningReactionHandler.IsBurning(target)) frame.Targets.Add(target);
        Current.Value = frame; return frame;
    }
    private static void Observe(BattleDeclaration declaration)
    {
        var frame = Current.Value; var damage = declaration.Get<DamageEventData>();
        if (frame != null && frame.Hit != 0 && declaration.Header.HitId == frame.Hit && damage?.Subject != null
            && damage.Source.SourceEntity == frame.Owner && frame.Targets.Contains(damage.Subject))
            OldBurning.Add(declaration, new BurnFact { Owner = frame.Owner });
    }
}

/// <summary>两种武器分别定义1/2/3份及1/2回合；满额刷新完成后才定向请求原回能行。</summary>
internal sealed class WeaponAwardCount : CountStatusHandler
{
    private readonly int weapon, maximum, duration;
    internal WeaponAwardCount(int weapon, int maximum, int duration) { this.weapon = weapon; this.maximum = maximum; this.duration = duration; }
    public override void OnCreated(StatusInstance status, object host)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, weapon); status.RuntimeValue = 1f;
        WeaponCountLifetime.Refresh(status, owner, duration, 0);
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        WeaponCountLifetime.RequireApplication(status, host, application, weapon);
        status.RuntimeValue = Math.Min(maximum, status.RuntimeValue + 1f);
        WeaponCountLifetime.Refresh(status, host, duration, 0); WeaponCountAwardHooks.Awarded(status, host, weapon, maximum); return status;
    }
    public override void OnRemoved(StatusInstance status, object host) => status.RuntimeValue = 0f;
}
