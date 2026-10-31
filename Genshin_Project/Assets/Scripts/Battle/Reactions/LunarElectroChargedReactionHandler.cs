using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>本次接触真实参与的角色。只保存追溯结果，不在目标循环中提前生成单目标雷暴云。</summary>
public sealed class PendingLunarConduct
{
    internal TeamBonusSystem Team;
    internal BattleEntity Creator, Target;
    internal string SkillID, EffectID;
    internal long HitID, ExecutionID;
    internal int Phase;
    internal readonly List<BattleEntity> Contributors = new List<BattleEntity>();
    internal bool Committed, SuppressImmediateDamage;
}

/// <summary>公共月曜加权伤害的角色属性快照；本次计算不重读角色、Count或装备。</summary>
public sealed class LunarContributionSnapshot
{
    public BattleEntity Source { get; }
    public int SourceEntityID { get; }
    public float LevelCoefficient { get; }
    public float Mastery { get; }
    public ReactionBuffTotals Bonuses { get; }
    public float CritRate { get; }
    public float CritDMG { get; }

    public LunarContributionSnapshot(BattleEntity source, float levelCoefficient, float mastery,
        ReactionBuffTotals bonuses, float critRate, float critDMG)
    {
        Source = source; SourceEntityID = source != null ? source.EntityID : 0;
        LevelCoefficient = levelCoefficient; Mastery = mastery; Bonuses = bonuses;
        CritRate = critRate; CritDMG = critDMG;
    }
}

/// <summary>单次伤害声明的不可变明细；Score已含独立暴击与抗性，未含只在HitBatch结算一次的DMGTaken。</summary>
public sealed class LunarDamageContribution
{
    public BattleEntity Source { get; }
    public int SourceEntityID { get; }
    public CriticalHitResult Critical { get; }
    public float Score => Critical.DamageAfterCritical;
    public float Weight { get; }
    public float WeightedDamage => Score * Weight;
    public LunarDamageContribution(LunarContributionSnapshot snapshot, CriticalHitResult critical, float weight)
    { Source = snapshot.Source; SourceEntityID = snapshot.SourceEntityID; Critical = critical; Weight = weight; }
}

/// <summary>
/// 月感电公共机制：敌方队伍唯一的40026状态拥有属性快照及计时，伤害经原派生HitBatch入口。
/// 生成者是唯一伤害/击杀归属；贡献者只参与评分，不各自产生一份命中或击杀声明。
/// </summary>
public sealed class LunarElectroChargedReactionHandler : IDisposable
{
    public const float Consumption = .4f;
    private readonly TeamBonusSystem team;
    public StatusInstance Cloud { get; private set; }
    public bool IsActive => !team.IsDisposed && Cloud != null && Cloud.IsActive
        && Cloud.RemainingPhaseCount > 0 && team.OpposingHost.Contains(Cloud);
    public IReadOnlyList<LunarContributionSnapshot> Contributors => Runtime?.Contributors
        ?? (IReadOnlyList<LunarContributionSnapshot>)Array.Empty<LunarContributionSnapshot>();

    private sealed class CloudRuntime
    {
        public IReadOnlyList<LunarContributionSnapshot> Contributors;
        public IReadOnlyList<long> Causes;
        public DamageSourceInfo Source;
        public int CreatedTurn;
    }
    private CloudRuntime Runtime => IsActive ? Cloud.CountRuntimeState as CloudRuntime : null;
    internal LunarElectroChargedReactionHandler(TeamBonusSystem team) { this.team = team; }

    /// <summary>规划入口只追溯来源和校验公共定义，不提交云、伤害或随机结果。</summary>
    public static bool TryPrepare(ReactionContext context, ElementPoolReading opposing, out PendingLunarConduct pending)
    {
        pending = null;
        if (opposing == null || LunarReactionConversion.SelectReaction(context, ReactionType.ElectroCharged, opposing.Element)
            != ReactionType.LunarElectroCharged) return false;
        TeamBonusSystem current = BattleManager.Instance?.TeamBonuses;
        if (current == null || current.IsDisposed) return false;
        BattleEntity incoming = context.PreserveIncomingAuraBeforeReaction
            ? ResolveAuraSource(current, ElementQuery.ReadAura(context.Target, context.AttackElement))
            : context.SourceEntity ?? context.SourceSnapshotOverride?.SourceEntity;
        BattleEntity previous = ResolveAuraSource(current, opposing);
        // 特殊残留清算可由风角色发起，但不能把敌人自带的水雷冒充我方附着。
        if (!current.Contains(incoming) && !current.Contains(previous)) return false;
        TeamPublicStatuses.Resolve(TeamPublicStatuses.Thundercloud); // 正式缺表须在消耗元素之前失败。
        pending = new PendingLunarConduct
        {
            Team = current, Creator = context.SourceEntity ?? context.SourceSnapshotOverride?.SourceEntity,
            Target = context.Target, SkillID = context.SourceSkillID, EffectID = context.SourceEffectID,
            HitID = context.LogicalHitID, ExecutionID = context.EffectExecutionID,
            Phase = context.ApplicationPhase > 0 ? context.ApplicationPhase : (int)BattleManager.Instance.CurrentPhase
        };
        if (current.Contains(incoming)) pending.Contributors.Add(incoming);
        if (current.Contains(previous) && !pending.Contributors.Contains(previous)) pending.Contributors.Add(previous);
        return true;
    }

    internal static BattleEntity ResolveAuraSource(TeamBonusSystem current, ElementPoolReading aura)
        => ReactionSourceAttribution.ResolvePartySource(current, aura);

    /// <summary>本逻辑hit所有接触完成后，按实际发生顺序整体生成/替换。重复通知不再生成云或抽随机。</summary>
    internal static List<ReactionDerivedHit> CompleteBatch(IReadOnlyList<ReactionResult> results)
    {
        var groups = new List<List<ReactionOccurrence>>();
        if (results == null) return new List<ReactionDerivedHit>();
        foreach (ReactionResult result in results)
        {
            if (result == null) continue;
            foreach (ReactionOccurrence occurrence in result.TriggeredReactions)
            {
                PendingLunarConduct pending = occurrence?.PendingLunarConduct;
                if (pending == null || pending.Committed) continue;
                List<ReactionOccurrence> matching = null;
                foreach (var group in groups)
                {
                    PendingLunarConduct first = group[0].PendingLunarConduct;
                    if (ReferenceEquals(first.Team, pending.Team) && first.HitID == pending.HitID
                        && first.ExecutionID == pending.ExecutionID) { matching = group; break; }
                }
                if (matching == null) { matching = new List<ReactionOccurrence>(); groups.Add(matching); }
                if (!matching.Contains(occurrence)) matching.Add(occurrence);
            }
        }
        var hits = new List<ReactionDerivedHit>();
        foreach (var group in groups)
        {
            PendingLunarConduct pending = group[0].PendingLunarConduct;
            var current = BattleManager.Instance?.TeamBonuses;
            if (!ReferenceEquals(current, pending.Team) || pending.Team.IsDisposed) continue;
            hits.AddRange(current.Thundercloud.Create(group));
        }
        return hits;
    }

    private List<ReactionDerivedHit> Create(List<ReactionOccurrence> occurrences)
    {
        var sources = new HashSet<BattleEntity>();
        var immediateTargets = new List<BattleEntity>();
        var causes = new List<long>();
        foreach (ReactionOccurrence occurrence in occurrences)
        {
            PendingLunarConduct pending = occurrence.PendingLunarConduct;
            foreach (var source in pending.Contributors) sources.Add(source);
            if (!pending.SuppressImmediateDamage && !immediateTargets.Contains(pending.Target)) immediateTargets.Add(pending.Target);
            if (occurrence.Declaration == null) throw new InvalidOperationException("月感电生成雷暴云前必须记录真实反应声明。");
            if (!causes.Contains(occurrence.Declaration.Header.EventId)) causes.Add(occurrence.Declaration.Header.EventId);
        }
        // 编队顺序固定抽取与同分排序，不受目标遍历顺序或HashSet枚举影响。
        var snapshots = LunarWeightedDamageCalculator.Capture(team, sources,
            ReactionType.LunarElectroCharged, "Electro", TeamPublicStatuses.Thundercloud);
        PendingLunarConduct last = occurrences[occurrences.Count - 1].PendingLunarConduct;
        using var resolution = BattleEventHub.BeginResolution();
        using var responses = BattleResolutionContext.BeginCollection();
        using var execution = BattleEventHub.BeginEffectExecution(last.ExecutionID);
        using var hit = BattleEventHub.BeginHit(last.HitID);
        using var cause = BattleEventHub.Enter(occurrences[occurrences.Count - 1].Declaration);
        Cloud = team.OpposingHost.AddManagedStatus(TeamPublicStatuses.Thundercloud, last.Creator, 2,
            last.Phase, TeamPublicStatuses.Resolve(TeamPublicStatuses.Thundercloud), causes);
        var state = new CloudRuntime
        {
            Contributors = snapshots, Causes = causes.AsReadOnly(), CreatedTurn = team.Turn,
            Source = DamageSourceInfo.Create(last.Creator, ReactionSourceKind.DerivedReaction,
                last.SkillID, last.EffectID, ReactionType.LunarElectroCharged, false)
        };
        state.Source.SourceStatus = Cloud;
        state.Source.SourceStatusID = TeamPublicStatuses.Thundercloud;
        state.Source.SourceStatusApplyOrder = Cloud.ApplyOrder;
        Cloud.CountRuntimeState = state; // 普通状态私有运行数据，不是Count数值。
        foreach (var occurrence in occurrences) occurrence.PendingLunarConduct.Committed = true;
        var hits = new List<ReactionDerivedHit>();
        foreach (BattleEntity target in immediateTargets)
            if (target != null) hits.Add(BuildHit(state, target));
        return hits;
    }

    /// <summary>纯公式入口：每贡献者每目标独立暴击后按真实得分排序；权重不归一化。</summary>
    public static float CalculateDamage(IReadOnlyList<LunarContributionSnapshot> sources, float resistance,
        out IReadOnlyList<LunarDamageContribution> contributions, BattleEntity target = null)
        => LunarWeightedDamageCalculator.Calculate(sources, 6f, resistance, out contributions, target);

    private ReactionDerivedHit BuildHit(CloudRuntime state, BattleEntity target)
    {
        float damage = CalculateDamage(state.Contributors,
            ReactionDamageCalculator.GetResistanceMultiplier(target, "Electro"), out var detail, target);
        DamageSourceInfo source = state.Source.Snapshot();
        source.LunarContributions = detail;
        foreach (var contribution in detail)
            LogManager.Log(LogCategory.Crit, $"雷暴云得分 | 生成者={source.SourceEntityID} | 贡献者={contribution.SourceEntityID} "
                + $"| 目标={target.EntityID} | 率={contribution.Critical.CriticalRate:P1} | 随机={contribution.Critical.Roll:F4} "
                + $"| 暴击={contribution.Critical.IsCritical} | 得分={contribution.Score:F3} | 权重={contribution.Weight:F2}");
        return new ReactionDerivedHit { Target = target, Damage = damage, DamageElement = "Electro",
            Properties = new DamageProperties { IsLunar = true, Reaction = ReactionType.LunarElectroCharged },
            Source = source, CauseEventIds = state.Causes, ReactionType = ReactionType.LunarElectroCharged };
    }

    public void TickTurnEnd(TurnPhase phase)
    {
        if (!IsActive || (phase != TurnPhase.AllyPostTurn && phase != TurnPhase.EnemyPostTurn)) return;
        StatusInstance expected = Cloud;
        BattleExecution.Enqueue(() => BattleExecution.PlayEffect(new BattlePresentationRequest(
            BattlePresentationKind.Reaction, expected.Caster, TeamPublicStatuses.Thundercloud,
            reaction: ReactionType.LunarElectroCharged), () =>
        {
            if (!IsActive || !ReferenceEquals(Cloud, expected)) return;
            var battle = BattleManager.Instance;
            if (battle == null || battle.IsBattleOver || !ReferenceEquals(battle.TeamBonuses, team)) return;
            using var resolution = BattleEventHub.BeginResolution();
            using var responses = BattleResolutionContext.BeginCollection();
            using var effect = BattleResolutionContext.BeginEffect();
            CloudRuntime state = Runtime;
            var hits = new ReactionResult();
            foreach (var enemy in battle.Enemies)
            {
                BattleEntity target = enemy != null ? enemy.Entity : null;
                if (target == null || !target.IsOnField || !ElementQuery.HasAura(target, "Hydro") || !ElementQuery.HasAura(target, "Electro")) continue;
                using var elements = ElementEventScope.Begin("LunarConductConsumption", target);
                foreach (long id in state.Causes) elements.AddCause(id);
                if (ElementQuery.ReadIntrinsic(target, "Hydro") == null) target.ConsumeAura("Hydro", Consumption);
                if (ElementQuery.ReadIntrinsic(target, "Electro") == null) target.ConsumeAura("Electro", Consumption);
                ReactionStateSystem.RemoveEntityState(target, ReactionType.ElectroCharged);
                hits.DerivedHits.Add(BuildHit(state, target));
            }
            ReactionEffectExecutor.ExecuteDerivedHits(hits);
        }));
    }

    /// <summary>本阶段反应伤害后、通用状态触发前计时；创建当阶段不立刻扣除第一回合。</summary>
    public void TickDuration(int phase)
    {
        if (!IsActive || phase != Cloud.AddInPhase || Runtime.CreatedTurn == team.Turn) return;
        if (--Cloud.RemainingPhaseCount <= 0) team.OpposingHost.RemoveStatusInstance(Cloud, "Expired");
    }

    public void Dispose()
    { team.OpposingHost.ClearManagedStatuses(TeamPublicStatuses.Thundercloud); Cloud = null; }
}
