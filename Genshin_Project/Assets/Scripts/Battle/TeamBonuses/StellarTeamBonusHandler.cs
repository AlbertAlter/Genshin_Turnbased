using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>只保存已确认的星超导规划；声明记录并提交后才能创建/刷新真实领域。</summary>
public sealed class PendingStellarSuperconduct
{
    internal StellarTeamBonusHandler Owner;
    internal ReactionSourceSnapshot Snapshot;
    internal BattleEntity Creator;
    internal long HitID;
    internal int Phase;
    internal bool Committed;
}

/// <summary>极星辉域是队伍唯一的领域；当前计数与已生效档位分离。</summary>
public sealed class StellarTeamBonusHandler
{
    private sealed class HitFrame
    {
        public long HitID;
        public StellarTeamBonusHandler Owner;
        public float ElementBonus, PhysicalResistanceBonus;
    }
    private sealed class HitScope : IDisposable
    {
        private readonly HitFrame previous;
        private bool disposed;
        public HitScope(HitFrame frame) { previous = CurrentHit.Value; CurrentHit.Value = frame; }
        public void Dispose() { if (!disposed) { disposed = true; CurrentHit.Value = previous; } }
    }
    private static readonly AsyncLocal<HitFrame> CurrentHit = new AsyncLocal<HitFrame>();
    internal static void Register() => BattleSpecialRules.RegisterHitScope(BeginHit);
    private static IDisposable BeginHit(long hitID, BattleEntity source, IEnumerable<BattleEntity> targets)
    {
        var owner = BattleManager.Instance?.TeamBonuses?.Stellar;
        return new HitScope(new HitFrame { HitID = hitID, Owner = owner,
            ElementBonus = owner?.ElementDamageBonus ?? 0f,
            PhysicalResistanceBonus = owner?.PhysicalResistanceBonus ?? 0f });
    }
    private HitFrame ActiveHit => CurrentHit.Value != null && ReferenceEquals(CurrentHit.Value.Owner, this)
        && CurrentHit.Value.HitID != 0 && CurrentHit.Value.HitID == BattleEventHub.CurrentHitId ? CurrentHit.Value : null;
    public float ReadElementDamageBonus() => ActiveHit?.ElementBonus ?? ElementDamageBonus;
    public float ReadPhysicalResistanceBonus() => ActiveHit?.PhysicalResistanceBonus ?? PhysicalResistanceBonus;

    private readonly TeamBonusSystem team;
    private readonly TeamBonusParameters data;
    private readonly HashSet<int> providerIDs;
    private IDisposable damageSubscription;
    private sealed class DomainRuntime
    {
        public readonly HashSet<long> CountedHits = new HashSet<long>();
        public long CreationHit;
        public int Pending, Effective;
        public ReactionSourceSnapshot Snapshot;
    }
    private DomainRuntime Runtime => Domain?.Status?.CountRuntimeState as DomainRuntime;
    public TeamBonusState Domain { get; private set; }
    public ReactionSourceSnapshot SourceSnapshot => Runtime?.Snapshot;
    public int PendingCount => IsActive ? Runtime?.Pending ?? 0 : 0;
    public int EffectiveCount => IsActive ? Runtime?.Effective ?? 0 : 0;
    public bool IsActive => !team.IsDisposed && Domain?.IsActive == true;
    public float ElementDamageBonus => IsActive ? data.Number("ElementDMG" + EffectiveCount) : 0f;
    public float ReactionMultiplier => IsActive ? data.Number("ReactionRate" + EffectiveCount) : data.Enabled ? data.Number("ReactionRate0") : 1f;
    public float PhysicalResistanceBonus => IsActive ? -data.Number("PhysicalResDown") : 0f;
    public StellarTeamBonusHandler(TeamBonusSystem team, TeamBonusParameters data)
    { this.team = team; this.data = data; providerIDs = data.CharacterIDs(); }

    /// <summary>兼容独立规则调用；正式元素入口使用TryPrepare及对应反应声明提交，不在规划时改场面。</summary>
    public bool TryConvertSuperconduct(ReactionContext context)
    {
        if (!TryPrepareSuperconduct(context, out var pending)) return false;
        Commit(pending, null);
        return true;
    }

    /// <summary>纯规划：真实冰雷接触、存活提供者和实际参与的我方元素；不能用扩散发起者冒充残留来源。</summary>
    public bool TryPrepareSuperconduct(ReactionContext context, out PendingStellarSuperconduct pending)
    {
        pending = null;
        if (!HasLivingProvider() || context == null || !context.CanTriggerReaction || context.AttackAmount <= 0f
            || context.Target == null || context.Target.Side != BattleSide.Enemy
            || (context.AttackElement != "Cryo" && context.AttackElement != "Electro")) return false;
        string other = context.AttackElement == "Cryo" ? "Electro" : "Cryo";
        var normal = ElementQuery.ReadReactionAura(context, other);
        float normalAmount = normal?.AvailableFor(context.AttackAmount) ?? 0f;
        ElementPoolReading frozen = null;
        if (other == "Cryo" && !context.IgnoreFrozenAura && context.AllowsReactionPool("Cryo", ReactionPoolKind.Frozen))
            frozen = ElementQuery.ReadPool(context.Target, "Cryo", ReactionPoolKind.Frozen);
        float frozenAmount = Math.Max(0f, frozen?.Amount ?? 0f);
        if (normalAmount + frozenAmount <= 0f) return false;

        BattleEntity incoming = context.PreserveIncomingAuraBeforeReaction
            ? ReactionSourceAttribution.ResolvePartySource(team, ElementQuery.ReadAura(context.Target, context.AttackElement))
            : context.SourceEntity ?? context.SourceSnapshotOverride?.SourceEntity;
        bool friendly = team.Contains(incoming)
            || normalAmount > 0f && ReactionSourceAttribution.ResolvePartySource(team, normal) != null
            // 普通冰先消耗；未实际用到的冻元素不能提供转化资格。
            || context.AttackAmount > normalAmount && frozenAmount > 0f
                && ReactionSourceAttribution.ResolvePartySource(team, frozen) != null;
        if (!friendly) return false;
        TeamPublicStatuses.Resolve(TeamPublicStatuses.PolarDomain); // 正式缺表必须在消耗元素前报错。
        pending = new PendingStellarSuperconduct { Owner = this,
            Snapshot = ReactionSourceSnapshot.Capture(context),
            Creator = context.SourceEntity ?? context.SourceSnapshotOverride?.SourceEntity,
            HitID = context.LogicalHitID != 0 ? context.LogicalHitID : BattleEventHub.CurrentHitId,
            Phase = context.ApplicationPhase > 0 ? context.ApplicationPhase : team.Phase };
        return true;
    }

    private bool HasLivingProvider()
    {
        if (!data.Enabled || team.IsDisposed) return false;
        foreach (BattleEntity member in team.Members)
            if (member.IsAlive && providerIDs.Contains(member.EntityID)) return true;
        return false;
    }

    internal static void Commit(ReactionOccurrence occurrence)
    {
        var pending = occurrence?.PendingStellarSuperconduct;
        if (pending == null || pending.Committed || pending.Owner.team.IsDisposed) return;
        if (occurrence.Declaration == null) throw new InvalidOperationException("星超导必须先记录真实反应声明再提交领域。");
        pending.Owner.Commit(pending, occurrence.Declaration);
    }

    private void Commit(PendingStellarSuperconduct pending, BattleDeclaration cause)
    {
        if (pending.Committed || team.IsDisposed) return;
        using var resolution = BattleEventHub.BeginResolution();
        using var collection = BattleResolutionContext.BeginCollection();
        using var declaration = BattleEventHub.Enter(cause);
        if (!IsActive)
        {
            damageSubscription?.Dispose();
            Domain = new TeamBonusState(TeamPublicStatuses.PolarDomain, pending.Creator, data.Int("Duration"),
                pending.Phase, team.OpposingHost);
            var runtime = new DomainRuntime { CreationHit = pending.HitID };
            Domain.Status.CountRuntimeState = runtime;
            // 监听者在真实领域创建后才存在，Record时冻结；旧伤害不能让后来领域补领。
            damageSubscription = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,
                (header, hit) => { if (ReferenceEquals(Runtime, runtime)) OnDamage(header, hit); },
                Domain.Status.ApplyOrder, () => IsActive && ReferenceEquals(Runtime, runtime));
        }
        else Domain.Refresh(pending.Creator, data.Int("Duration"), pending.Phase);
        Runtime.Snapshot = pending.Snapshot;
        pending.Committed = true;
        LogManager.Log(LogCategory.TeamBonus, "极星辉域创建/刷新：count=" + PendingCount + " activeTier=" + EffectiveCount);
    }
    public void OnDamage(BattleEventHeader header, DamageEventData hit)
    {
        if (!IsActive || header == null || hit?.Source == null || !team.Contains(hit.Source.SourceEntity) || !hit.HitLanded
            || hit.Subject == null || hit.Subject.Side != BattleSide.Enemy
            || (hit.Element != "Cryo" && hit.Element != "Electro") || hit.ElementAmount <= 0f
            || hit.Source.ReactionType == ReactionType.StellarSuperconduct
            || (Runtime.CreationHit != 0 && header.HitId == Runtime.CreationHit)) return;
        if (header.HitId == 0 || !Runtime.CountedHits.Add(header.HitId)) return;
        Runtime.Pending = System.Math.Min(data.Int("CountCap"), PendingCount + 1);
    }
    public void EnterPhase(int phase)
    {
        Domain?.Tick(phase);
        if (!IsActive)
        { damageSubscription?.Dispose(); damageSubscription = null; return; }
        if (phase != (int)TurnPhase.AllyPreTurn) return;
        Runtime.Effective = PendingCount;
        Runtime.Pending = 0;
        Runtime.CountedHits.Clear();
    }
    public void Dispose()
    {
        damageSubscription?.Dispose(); damageSubscription = null;
        if (Domain?.Status != null) team.OpposingHost.ClearManagedStatuses("PolarDomain");
        Domain = null;
    }
}
