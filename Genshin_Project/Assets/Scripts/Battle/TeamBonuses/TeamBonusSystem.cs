using System;
using System.Collections.Generic;

/// <summary>队伍自身拥有的运行时记录，不伪装成任何角色身上的状态。</summary>
public sealed class TeamBonusState
{
    private readonly BattleTeamStatusHost host;
    public StatusInstance Status { get; }
    public string ID { get; }
    private readonly long standaloneOrder;
    private BattleEntity source;
    private int remaining, phase;
    private float value;
    public long ApplyOrder => Status?.ApplyOrder ?? standaloneOrder;
    public BattleEntity Source => Status != null ? Status.Caster : source;
    public int RemainingTurns => Status != null ? Status.RemainingPhaseCount : remaining;
    public int OriginPhase => Status != null ? Status.AddInPhase : phase;
    public float Value { get => Status != null ? Status.RuntimeValue : value;
        set { if (Status != null) Status.RuntimeValue = value; else this.value = value; } }
    public bool IsActive => RemainingTurns > 0 && (Status == null || host.Contains(Status) && Status.IsActive);
    public TeamBonusState(string id, BattleEntity source, int duration, int phase, BattleTeamStatusHost host = null)
    {
        ID = id; this.host = host;
        if (host != null)
            Status = host.AddManagedStatus(id, source, duration, phase, TeamPublicStatuses.Resolve(id));
        else standaloneOrder = ++BattleEntity._applyOrderCounter;
        Refresh(source, duration, phase);
    }
    public void Refresh(BattleEntity source, int duration, int phase)
    {
        if (Status == null) { this.source = source; remaining = Math.Max(0, duration); this.phase = phase; }
        else { Status.RecordSource(source); Status.RemainingPhaseCount = Math.Max(0, duration); Status.AddInPhase = phase; }
    }
    public void Tick(int phase)
    {
        if (!IsActive || phase != OriginPhase) return;
        if (Status == null) remaining--;
        else if (--Status.RemainingPhaseCount <= 0) host.RemoveStatusInstance(Status, "Expired");
    }
}

/// <summary>一场战斗一份。固定规则由各 Handler 负责；表里只配参数和角色/效果身份。</summary>
public sealed class TeamBonusSystem : IDisposable
{
    private readonly List<IDisposable> subscriptions = new List<IDisposable>();
    private readonly List<BattleEntity> members = new List<BattleEntity>();
    private readonly Dictionary<BattleEntity, string> elements = new Dictionary<BattleEntity, string>();
    public IReadOnlyList<BattleEntity> Members => members;
    public BattleTeamStatusHost Host { get; }
    public BattleTeamStatusHost OpposingHost { get; }
    private readonly bool ownsHosts;
    public ElementalResonanceHandler Resonance { get; }
    public MoonsignBonusHandler Moonsign { get; }
    public NightSoulBurstHandler NightSoul { get; }
    public StellarTeamBonusHandler Stellar { get; }
    public VerdantDewPool VerdantDew { get; }
    public LunarElectroChargedReactionHandler Thundercloud { get; }
    public LunarCrystallizeReactionHandler MoonCages { get; }
    public bool HasHexerei { get; }
    public int Turn { get; private set; } = 1;
    public int Phase { get; private set; } = 1;
    public bool IsDisposed { get; private set; }

    public TeamBonusSystem(IEnumerable<BattleEntity> roster, TeamBonusRules rules,
        Func<BattleEntity, string> elementOf, BattleTeamStatusHost host = null, BattleTeamStatusHost opposingHost = null)
    {
        if (rules == null) throw new ArgumentNullException(nameof(rules));
        if ((host == null) != (opposingHost == null)) throw new ArgumentException("双方队伍宿主须一起提供。");
        ownsHosts = host == null;
        Host = host ?? new BattleTeamStatusHost(BattleSide.Ally);
        OpposingHost = opposingHost ?? new BattleTeamStatusHost(BattleSide.Enemy);
        foreach (BattleEntity member in roster)
            if (member != null && member.Side == BattleSide.Ally && !members.Contains(member))
            { members.Add(member); elements.Add(member, elementOf(member) ?? string.Empty); }
        Resonance = new ElementalResonanceHandler(this, rules.Resonance);
        Moonsign = new MoonsignBonusHandler(this, rules.Moonsign);
        NightSoul = new NightSoulBurstHandler(this, rules.NightSoul);
        Stellar = new StellarTeamBonusHandler(this, rules.Stellar);
        VerdantDew = new VerdantDewPool(this);
        Thundercloud = new LunarElectroChargedReactionHandler(this);
        MoonCages = new LunarCrystallizeReactionHandler(this);
        HasHexerei = rules.Hexerei.Enabled && CountMembers(rules.Hexerei.CharacterIDs()) >= rules.Hexerei.Int("RequiredCount");
        Listen<ActionEventData>(BattleEventKind.ActionStarted, Moonsign.OnAction);
        Listen<ReactionEventData>(BattleEventKind.ReactionOccurred, Resonance.OnReaction);
        Listen<DamageEventData>(BattleEventKind.DamageResolved, Resonance.OnDamage);
        Listen<DamageEventData>(BattleEventKind.DamageResolved, NightSoul.OnDamage);
    }

    private void Listen<T>(BattleEventKind kind, Action<BattleEventHeader, T> receive) where T : BattleEventData
    {
        // 和角色状态使用同一个施加序号及声明收集器，不另起一条响应队列。
        subscriptions.Add(BattleEventHub.Subscribe(kind, receive, ++BattleEntity._applyOrderCounter, () => !IsDisposed));
    }
    public bool Contains(BattleEntity actor) => !IsDisposed && actor != null && members.Contains(actor);
    public string ElementOf(BattleEntity actor) => actor != null && elements.TryGetValue(actor, out string value) ? value : string.Empty;
    public int CountMembers(HashSet<int> ids)
    { int count = 0; foreach (BattleEntity member in members) if (ids.Contains(member.EntityID)) count++; return count; }
    public int CountElement(string element)
    { int count = 0; foreach (BattleEntity member in members) if (ElementOf(member) == element) count++; return count; }
    public void EnterPhase(int phase, int turn)
    {
        if (IsDisposed) return;
        Phase = phase; Turn = turn;
        Resonance.EnterPhase(phase);
        Moonsign.EnterPhase(phase);
        Stellar.EnterPhase(phase);
    }
    public bool HasBonus(string id)
    {
        if (IsDisposed) return false;
        switch (id)
        {
            case "MoonsignGleam": return Moonsign.Level >= 1;
            case "MoonsignAscendant": return Moonsign.Level >= 2;
            case "Hexerei": return HasHexerei;
            case "PolarDomain": return Stellar.IsActive;
            default: return Resonance.Has(id);
        }
    }
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        VerdantDew.Reset();
        foreach (IDisposable subscription in subscriptions) subscription.Dispose();
        subscriptions.Clear();
        Resonance.Dispose();
        Stellar.Dispose();
        Thundercloud.Dispose();
        MoonCages.Dispose();
        if (ownsHosts) { Host.Dispose(); OpposingHost.Dispose(); }
    }
}
