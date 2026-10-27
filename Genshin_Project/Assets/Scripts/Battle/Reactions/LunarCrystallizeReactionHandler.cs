using System;
using System.Collections.Generic;

/// <summary>真实水岩接触时固定来源和触发位置，主hit全部目标提交后才处理场地和协奏。</summary>
public sealed class PendingLunarCrystallize
{
    internal TeamBonusSystem Team;
    internal BattleEntity Actor;
    internal FieldPosition Position;
    internal int Phase;
    internal string SkillID, EffectID;
    internal readonly List<BattleEntity> Sources = new List<BattleEntity>();
    internal bool Committed;
}

/// <summary>一个实际挂在格子上的普通公共状态。不是Count，也不是队伍宿主的重复投影。</summary>
public sealed class MoonCageInstance
{
    public StatusInstance Status { get; }
    public FieldPosition Position { get; }
    public BattleEntity Creator => Status.Caster;
    public bool IsGeoConstruct => true;
    public bool CountsTowardGeoConstructLimit => false;
    public bool HasDealtDamage { get; internal set; }
    public bool IsActive => Status.IsActive && Position.StatusList.Contains(Status);
    internal readonly LunarCrystallizeReactionHandler Owner;
    internal readonly int CreatedTurn;
    internal MoonCageInstance(LunarCrystallizeReactionHandler owner, StatusInstance status, FieldPosition position, int turn)
    { Owner = owner; Status = status; Position = position; CreatedTurn = turn; }
}

/// <summary>协奏时选定的独立月笼攻击快照；没有活目标时Target为空，不伪造一次伤害。</summary>
public sealed class MoonCageAttackSnapshot
{
    public long CageInstanceID { get; }
    public int SlotIndex { get; }
    public BattleEntity Creator { get; }
    public int CreatorID { get; }
    public BattleEntity Target { get; }
    public int TargetID { get; }
    internal MoonCageAttackSnapshot(MoonCageInstance cage, BattleEntity target)
    { CageInstanceID = cage.Status.ApplyOrder; SlotIndex = cage.Position.SlotIndex;
        Creator = cage.Creator; CreatorID = cage.Status.SourceEntityID;
        Target = target; TargetID = target != null ? target.EntityID : 0; }
}

/// <summary>已触发一次协奏，不冒充新的月结晶反应；伤害和击杀另由真实HitBatch声明。</summary>
public sealed class MoonCageConcertoEventData : BattleEventData
{
    public BattleEntity Actor => Subject;
    public IReadOnlyList<BattleEntity> Contributors { get; }
    public IReadOnlyList<MoonCageAttackSnapshot> Attacks { get; }
    internal MoonCageConcertoEventData(BattleEntity actor, IEnumerable<BattleEntity> sources,
        IEnumerable<MoonCageAttackSnapshot> attacks) : base(BattleEventKind.MoonCageConcerto, actor)
    { Contributors = new List<BattleEntity>(sources).AsReadOnly();
        Attacks = new List<MoonCageAttackSnapshot>(attacks).AsReadOnly(); }
}

/// <summary>一场队伍一份月结晶机制；3次非创建反应的来源窗口属于队伍，每个月笼独立存在和计时。</summary>
public sealed class LunarCrystallizeReactionHandler : IDisposable
{
    public const int MaxCages = 3;
    private readonly TeamBonusSystem team;
    private readonly List<MoonCageInstance> cages = new List<MoonCageInstance>();
    private readonly HashSet<BattleEntity> sources = new HashSet<BattleEntity>();
    private readonly List<long> causes = new List<long>();
    public int PendingReactionCount { get; private set; }
    public IReadOnlyList<MoonCageInstance> ActiveCages
        => cages.FindAll(c => !team.IsDisposed && c.IsActive).AsReadOnly();
    internal LunarCrystallizeReactionHandler(TeamBonusSystem team) { this.team = team; }

    internal static void Register() => BattleSpecialRules.RegisterStatusLifecycle(TeamPublicStatuses.MoonCage,
        null, (status, host) =>
        {
            if (status.CountRuntimeState is MoonCageInstance cage) cage.Owner.Removed(cage);
        });

    public static bool TryPrepare(ReactionContext context, string otherElement, out PendingLunarCrystallize pending)
    {
        pending = null;
        if (LunarReactionConversion.SelectReaction(context, ReactionType.Crystallize, otherElement)
            != ReactionType.LunarCrystallize) return false;
        var battle = BattleManager.Instance;
        var current = battle.TeamBonuses;
        FieldPosition position = context.Target.Position
            ?? battle.Field?.GetSlot(BattleSide.Enemy, context.Target.SlotPosition);
        if (position == null || position.Side != BattleSide.Enemy || !ReferenceEquals(position.OwnerField, battle.Field))
            throw new InvalidOperationException("月结晶目标没有本场有效敌方位置，不能消耗元素后生成无位置月笼。");
        TeamPublicStatuses.Resolve(TeamPublicStatuses.MoonCage); // 在消耗水元素之前严格校验正式定义。
        var actor = context.SourceEntity ?? context.SourceSnapshotOverride?.SourceEntity;
        pending = new PendingLunarCrystallize { Team = current, Actor = actor, Position = position,
            SkillID = context.SourceSkillID, EffectID = context.SourceEffectID,
            Phase = context.ApplicationPhase > 0 ? context.ApplicationPhase : (int)battle.CurrentPhase };
        if (current.Contains(actor)) pending.Sources.Add(actor);
        BattleEntity hydro = LunarElectroChargedReactionHandler.ResolveAuraSource(current, ElementQuery.ReadAura(context.Target, "Hydro"));
        if (current.Contains(hydro) && !pending.Sources.Contains(hydro)) pending.Sources.Add(hydro);
        return true;
    }

    internal static List<ReactionDerivedHit> CompleteBatch(IReadOnlyList<ReactionResult> results)
    {
        var hits = new List<ReactionDerivedHit>();
        if (results == null) return hits;
        foreach (var result in results)
        {
            if (result == null) continue;
            foreach (var occurrence in result.TriggeredReactions)
            {
                var pending = occurrence?.PendingLunarCrystallize;
                if (pending == null || pending.Committed || pending.Team.IsDisposed
                    || !ReferenceEquals(BattleManager.Instance?.TeamBonuses, pending.Team)) continue;
                if (occurrence.Declaration == null) throw new InvalidOperationException("月结晶缺少真实反应声明。");
                pending.Committed = true; // 同一发生对象重复通知不再抽生成位置、计数或发动协奏。
                using var cause = BattleEventHub.Enter(occurrence.Declaration);
                using var collection = BattleResolutionContext.BeginCollection();
                hits.AddRange(pending.Team.MoonCages.Commit(pending, occurrence.Declaration));
            }
        }
        return hits;
    }

    private List<ReactionDerivedHit> Commit(PendingLunarCrystallize pending, BattleDeclaration reaction)
    {
        if (TryCreate(pending, reaction)) return new List<ReactionDerivedHit>();
        foreach (var source in pending.Sources) sources.Add(source);
        causes.Add(reaction.Header.EventId);
        if (++PendingReactionCount < 3) return new List<ReactionDerivedHit>();
        // 只在三次反应凑齐时读取属性；之前仅记录角色身份，不缓存面板或一份永久得分。
        var snapshots = LunarWeightedDamageCalculator.Capture(team, sources,
            ReactionType.LunarCrystallize, "Geo", TeamPublicStatuses.MoonCage);
        var windowCauses = causes.ToArray();
        PendingReactionCount = 0; sources.Clear(); causes.Clear(); // 发布前结清窗口，避免子响应重复使用。
        return PlanConcerto(pending, snapshots, windowCauses);
    }

    private bool TryCreate(PendingLunarCrystallize pending, BattleDeclaration reaction)
    {
        if (ActiveCages.Count >= MaxCages) return false;
        FieldPosition chosen = pending.Position;
        if (HasCage(chosen))
        {
            var candidates = new List<FieldPosition>();
            foreach (int slot in BattlePositionSystem.GetAdjacentPositions(BattleSide.Enemy, chosen.SlotIndex, 1))
            {
                var adjacent = chosen.OwnerField.GetSlot(BattleSide.Enemy, slot);
                if (!ReferenceEquals(adjacent, chosen) && adjacent != null && !HasCage(adjacent)) candidates.Add(adjacent);
            }
            if (candidates.Count == 0) return false;
            chosen = candidates[BattleRandom.NextInt(0, candidates.Count)];
        }
        StatusInstance status = chosen.AddManagedStatus(TeamPublicStatuses.MoonCage, pending.Actor, 2,
            pending.Phase, TeamPublicStatuses.Resolve(TeamPublicStatuses.MoonCage), new[] { reaction.Header.EventId });
        var cage = new MoonCageInstance(this, status, chosen, team.Turn);
        status.CountRuntimeState = cage;
        cages.Add(cage);
        team.Resonance.SetHasMoonCage(cage.Creator, true);
        return true;
    }

    public bool HasCage(FieldPosition position)
        => ActiveCages.Count > 0 && cages.Exists(c => c.IsActive && ReferenceEquals(c.Position, position));

    private List<ReactionDerivedHit> PlanConcerto(PendingLunarCrystallize trigger,
        IReadOnlyList<LunarContributionSnapshot> snapshots, IReadOnlyList<long> windowCauses)
    {
        var active = ActiveCages;
        var attacks = new List<MoonCageAttackSnapshot>();
        foreach (var cage in active)
        {
            var targets = new List<BattleEntity>();
            foreach (int slot in BattlePositionSystem.GetAdjacentPositions(BattleSide.Enemy, cage.Position.SlotIndex, 1))
            {
                var field = cage.Position.OwnerField.GetSlot(BattleSide.Enemy, slot);
                if (field?.IsOccupied == true && field.Occupant.IsAlive) targets.Add(field.Occupant);
            }
            attacks.Add(new MoonCageAttackSnapshot(cage,
                targets.Count > 0 ? targets[BattleRandom.NextInt(0, targets.Count)] : null));
        }
        var actors = new List<BattleEntity>();
        foreach (var snapshot in snapshots) actors.Add(snapshot.Source);
        var declaration = BattleEventHub.Publish(new MoonCageConcertoEventData(trigger.Actor, actors, attacks), causes: windowCauses);
        var hits = new List<ReactionDerivedHit>();
        for (int index = 0; index < active.Count; index++)
        {
            MoonCageInstance cage = active[index];
            BattleEntity target = attacks[index].Target;
            if (target == null) continue; // 无目标不抽暴击，不记造成伤害，也不续存月笼。
            float damage = LunarWeightedDamageCalculator.Calculate(snapshots, 1.6f,
                ReactionDamageCalculator.GetResistanceMultiplier(target, "Geo"), out var detail, target, ReactionType.LunarCrystallize);
            var source = DamageSourceInfo.Create(trigger.Actor, ReactionSourceKind.DerivedReaction,
                trigger.SkillID, trigger.EffectID, ReactionType.LunarCrystallize, false);
            source.SourceStatus = cage.Status; source.SourceStatusID = TeamPublicStatuses.MoonCage;
            source.SourceStatusApplyOrder = cage.Status.ApplyOrder; source.LunarContributions = detail;
            long hitID = BattleEventHub.NextIdentity();
            foreach (var contribution in detail)
                LogManager.Log(LogCategory.Crit, $"月笼协奏 hit#{hitID} | 归属={source.SourceEntityID} | 月笼格={cage.Position.SlotIndex} "
                    + $"| 目标={target.EntityID} | 贡献者={contribution.SourceEntityID} | 率={contribution.Critical.CriticalRate:P1} "
                    + $"| 随机={contribution.Critical.Roll:F4} | 暴击={contribution.Critical.IsCritical} "
                    + $"| 得分={contribution.Score:F3} | 权重={contribution.Weight:F2}");
            hits.Add(new ReactionDerivedHit { RuntimeHitID = hitID, Target = target,
                Damage = damage, DamageElement = "Geo", Properties = new DamageProperties
                { IsLunar = true, Reaction = ReactionType.LunarCrystallize },
                ReactionType = ReactionType.LunarCrystallize, Source = source,
                CauseEventIds = new[] { declaration.Header.EventId }, OnResolved = result => MarkDamage(cage, result) });
        }
        return hits;
    }

    private static void MarkDamage(MoonCageInstance cage, HitResolution result)
    {
        var damage = result?.DamageEvent;
        if (!cage.IsActive || damage == null || (damage.ActualHPDamage <= 0f && damage.ShieldAbsorbed <= 0f)) return;
        cage.HasDealtDamage = true;
        cage.Status.IsPermanent = true;
        cage.Status.RemainingPhaseCount = 0; // 按原文：2t内从未造成伤害则消失；造成过则一直存在。
    }

    public void TickDuration(int phase)
    {
        foreach (var cage in ActiveCages)
            if (!cage.HasDealtDamage && phase == cage.Status.AddInPhase && cage.CreatedTurn != team.Turn
                && --cage.Status.RemainingPhaseCount <= 0)
                cage.Position.RemoveStatusInstance(cage.Status, "Expired");
    }

    private void Removed(MoonCageInstance cage)
    {
        cages.Remove(cage);
        team.Resonance.SetHasMoonCage(cage.Creator,
            cages.Exists(other => other.IsActive && ReferenceEquals(other.Creator, cage.Creator)));
    }

    public void Dispose()
    {
        // 会话销毁不伪造战斗内消耗/到期，也不执行OnEnd。
        foreach (var cage in cages)
        {
            cage.Status.IsActive = false; cage.Position.StatusList.Remove(cage.Status);
            team.Resonance.SetHasMoonCage(cage.Creator, false);
        }
        cages.Clear(); PendingReactionCount = 0; sources.Clear(); causes.Clear();
    }
}
