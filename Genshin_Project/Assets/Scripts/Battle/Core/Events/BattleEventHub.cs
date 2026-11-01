using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

/// <summary>声明种类是代码契约，不由任意配表字符串自动创建。</summary>
public enum BattleEventKind
{
    ActionStarted, ActionCompleted, DamageResolved, HPChanged, HealResolved,
    ReactionOccurred, ResourceChanged, SwitchResolved, ElementContactResolved,
    AuraChanged, StatusAdded, StatusRemoved, ShieldChanged, Death, APSpent, NightSoulBurst, BattleEnded, ShieldGranted,
    MoonCageConcerto
}

/// <summary>胜负已正式确定；不用于手动StopBattle、取消或重置，不借状态OnEnd替代。</summary>
public sealed class BattleEndedEventData : BattleEventData
{
    public bool Victory { get; }
    public BattleEndedEventData(bool victory) : base(BattleEventKind.BattleEnded, null)
    { Victory = victory; }
}

public enum ResourceChangeReason { Gain, Spend, Decay, Clear, HealingAbsorption, Adjustment }

/// <summary>实际AP付款的用途；不由技能等级分类、DamageType或响应时的当前按钮倒推。</summary>
public enum APSpendKind { Unspecified, Skill, Switch, Thaw }

public sealed class APSpentEventData : BattleEventData
{
    private readonly HashSet<BattleEntity> onField = new HashSet<BattleEntity>();
    public int Amount { get; }
    public int? Before { get; }
    public int? After { get; }
    public APSpendKind SpendKind { get; }
    public string SourceSkillID { get; }
    public APSpentEventData(BattleEntity actor, int amount, int? after,
        APSpendKind spendKind = APSpendKind.Unspecified, string sourceSkillID = null) : base(BattleEventKind.APSpent, actor)
    {
        if (spendKind == APSpendKind.Skill && string.IsNullOrWhiteSpace(sourceSkillID))
            throw new ArgumentException("技能AP付款必须保留实际技能ID。", nameof(sourceSkillID));
        SpendKind = spendKind; SourceSkillID = sourceSkillID;
        Amount = amount; After = after; Before = after.HasValue ? after + amount : null;
        var battle = BattleManager.Instance;
        if (battle != null)
        {
            foreach (var ally in battle.Allies)
                if (ally?.Entity != null && ally.IsActive) onField.Add(ally.Entity);
        }
        else if (actor?.CharacterCtrl?.IsActive == true) onField.Add(actor);
    }
    /// <summary>实际付款时的前台快照，不随随后换人或响应执行时的布局变化。</summary>
    public bool WasOnField(BattleEntity actor) => actor != null && onField.Contains(actor);
}

/// <summary>不可变的事件身份；编号与次数额度分开，来源配表ID不是执行编号。</summary>
public sealed class BattleEventHeader
{
    public long SessionId { get; }
    public long EventId { get; }
    public long ResolutionId { get; }
    public long ActionScopeId { get; }
    /// <summary>一次真实技能行动的执行身份；不同于连锁共享的限次ActionScopeId，嵌套行动也各自唯一。</summary>
    public long ActionExecutionId { get; }
    public long EffectScopeId { get; }
    public long EffectExecutionId { get; }
    public long HitId { get; }
    public int TargetIndex { get; }
    public IReadOnlyList<long> CauseEventIds { get; }

    internal BattleEventHeader(long session, long id, long resolution, long action, long effect,
        long execution, long hit, int targetIndex, IEnumerable<long> causes, long actionExecution = 0)
    {
        SessionId = session; EventId = id; ResolutionId = resolution; ActionScopeId = action;
        ActionExecutionId = actionExecution;
        EffectScopeId = effect; EffectExecutionId = execution; HitId = hit; TargetIndex = targetIndex;
        var ids = new List<long>();
        if (causes != null) foreach (long cause in causes) if (cause > 0 && !ids.Contains(cause)) ids.Add(cause);
        CauseEventIds = ids.AsReadOnly();
    }
}

public abstract class BattleEventData
{
    public BattleEventKind Kind { get; }
    /// <summary>用于按拥有者/受影响者筛选；归功来源另由各结果保存。</summary>
    public BattleEntity Subject { get; }
    public int SubjectId { get; }
    /// <summary>结果记录时的实际回合/阶段；无战斗管理器时为0，不冒充某个阶段。</summary>
    public int Turn { get; }
    public int Phase { get; }
    public bool SubjectWasOnField { get; }
    protected BattleEventData(BattleEventKind kind, BattleEntity subject)
    {
        Kind = kind; Subject = subject; SubjectId = subject != null ? subject.EntityID : 0;
        Turn = BattleManager.Instance?.TurnCount ?? 0;
        Phase = BattleManager.Instance != null ? (int)BattleManager.Instance.CurrentPhase : 0;
        SubjectWasOnField = subject?.CharacterCtrl?.IsActive == true;
    }
}

public sealed class DamageEventData : BattleEventData
{
    public float TargetMaxHP { get; }
    public int ActiveAllyIdAtHit { get; }
    public bool FromIndependentEnemyStatus { get; }
    public string ReleaseVoiceSkillId { get; }
    public int SourceWeaponType { get; }
    private readonly DamageSourceInfo source;
    public DamageSourceInfo Source => source.Snapshot();
    public bool SourceWasPartyMember { get; }
    public bool SourceWasOnField { get; }
    public float HPBefore { get; }
    public float HPAfter { get; }
    public float RequestedDamage { get; }
    public float ActualHPDamage { get; }
    public float DamageBeforeShield { get; }
    /// <summary>值类型快照；null表示本次伤害没有进入可暴击直伤判定。</summary>
    public CriticalHitResult? Critical { get; }
    public float ShieldAbsorbed { get; }
    public bool IsImmune { get; }
    public float ImmuneDamage { get; }
    public bool HitLanded { get; }
    public bool CausedDeath { get; }
    public int HitIndex { get; }
    public string Element { get; }
    public string DamageType { get; }
    public float ElementAmount { get; }
    public bool IsNightSoulDamage { get; }
    public DamageProperties Properties { get; }
    public bool IsPneumaDamage => Properties.IsPneuma;
    public bool IsOusiaDamage => Properties.IsOusia;
    public bool IsResonanceDamage => Properties.IsResonance;
    public GeoResonanceEmission Resonance { get; }
    public IReadOnlyList<OverallShieldProtection> OverallShieldProtections { get; }
    public DamageEventData(DamageResolvedEvent value) : base(BattleEventKind.DamageResolved, value.Target)
    {
        source = value.Source?.Snapshot() ?? DamageSourceInfo.CreateUnknown();
        if (value.Target != null && value.Target.Side == BattleSide.Ally)
        {
            TargetMaxHP = value.Target.GetFinalHP();
            var active = BattleManager.Instance?.GetActiveAlly()?.Entity;
            ActiveAllyIdAtHit = active != null && active.IsAlive ? active.EntityID : 0;
            var status = BattleResolutionContext.CurrentAction?.IndependentStatus;
            FromIndependentEnemyStatus = status != null && status.HasSourceEntity && status.SourceSide == BattleSide.Enemy;
        }
        SourceWeaponType = source.SourceEntity != null ? source.SourceEntity.WeaponType : -1;
        if (source.SourceKind == ReactionSourceKind.StatusEffect) ReleaseVoiceSkillId = source.SourceStatus?.ReleaseVoiceSkillId;
        SourceWasPartyMember = value.SourceWasPartyMember; SourceWasOnField = value.SourceWasOnField;
        HPBefore = value.HPBefore; HPAfter = value.HPAfter; RequestedDamage = value.RequestedHPDamage;
        ActualHPDamage = value.ActualHPDamage; HitLanded = value.HitLanded; CausedDeath = value.CausedDeath;
        HitIndex = value.HitIndex; DamageBeforeShield = value.DamageBeforeShield;
        Critical = value.Critical;
        ShieldAbsorbed = value.ShieldAbsorbed;
        IsImmune = value.IsImmune; ImmuneDamage = value.ImmuneDamage;
        Element = value.Element; DamageType = value.DamageType;
        ElementAmount = value.ElementAmount; IsNightSoulDamage = value.IsNightSoulDamage;
        Properties = value.Properties;
        Resonance = value.Resonance;
        OverallShieldProtections = value.OverallShieldProtections ?? Array.Empty<OverallShieldProtection>();
    }
}

public sealed class HPChangedEventData : BattleEventData
{
    public float Before { get; }
    public float After { get; }
    public string Reason { get; }
    public HPChangedEventData(BattleEntity target, float before, float after, string reason)
        : base(BattleEventKind.HPChanged, target) { Before = before; After = after; Reason = reason; }
}

public sealed class HealEventData : BattleEventData
{
    public BattleEntity Source { get; }
    public int SourceId { get; }
    public string EffectId { get; }
    public float Before { get; }
    public float After { get; }
    public float Requested { get; }
    public float Actual { get; }
    public float Overheal { get; }
    public float BondAbsorbed { get; }
    public float BondBefore { get; }
    public float BondAfter { get; }
    public int SegmentIndex { get; }
    public HealEventData(HealResolvedEvent value) : base(BattleEventKind.HealResolved, value.Target)
    {
        Source = value.Source; SourceId = Source != null ? Source.EntityID : 0; EffectId = value.SourceEffectID;
        Before = value.HPBefore; After = value.HPAfter; Requested = value.RequestedAmount;
        Actual = value.ActualAmount; Overheal = value.OverhealAmount; BondAbsorbed = value.BondOfLifeAbsorbedAmount;
        BondBefore = value.BondOfLifeBefore; BondAfter = value.BondOfLifeAfter; SegmentIndex = value.SegmentIndex;
    }
}

public sealed class ReactionEventData : BattleEventData
{
    private readonly HashSet<BattleEntity> onField = new HashSet<BattleEntity>();
    public ReactionType Reaction { get; }
    public BattleEntity Source { get; }
    public int SourceId { get; }
    public string EffectId { get; }
    public IReadOnlyList<string> Elements { get; }
    public string SourceElement { get; }
    public bool SourceHadNightSoulBlessing { get; }
    public bool SourceWasPartyMember { get; }
    public BattleSide SourceSide { get; }
    public ReactionEventData(ReactionOccurrence value) : base(BattleEventKind.ReactionOccurred, value.Target)
    {
        Reaction = value.Type; Source = value.SourceEntity; SourceId = Source != null ? Source.EntityID : 0;
        EffectId = value.SourceEffectID;
        Elements = new List<string>(value.InvolvedElements ?? new List<string>()).AsReadOnly();
        SourceSide = Source != null ? Source.Side : default;
        SourceHadNightSoulBlessing = Source != null && Source.HasStatus("ST_NightSoulBlessing");
        var battle = BattleManager.Instance;
        var team = battle?.TeamBonuses;
        SourceWasPartyMember = team != null && !team.IsDisposed && team.Contains(Source);
        SourceElement = SourceWasPartyMember ? team.ElementOf(Source) : string.Empty;
        if (battle != null)
            foreach (var ally in battle.Allies)
                if (ally?.Entity != null && ally.IsActive) onField.Add(ally.Entity);
    }
    /// <summary>反应真实发生时的完整前台集合，不随之后切人或响应中的布局变化。</summary>
    public bool WasOnField(BattleEntity actor) => actor != null && onField.Contains(actor);
}

/// <summary>double作为公共声明传输值；夜魂存储/计算仍为int，生命之契与当前能量为float。</summary>
public sealed class ResourceEventData : BattleEventData
{
    /// <summary>队伍资源的拥有者；此时Subject为空，不能把触发者伪装成资源拥有者。</summary>
    public TeamBonusSystem TeamOwner { get; }
    public BattleTeamStatusHost TeamHost { get; }
    public BattleEntity Actor { get; }
    public int ActorId { get; }
    public bool ActorWasOnField { get; }
    public string Resource { get; }
    public string PoolId { get; }
    public ResourceChangeReason Reason { get; }
    public bool IsInteger { get; }
    public double Before { get; }
    public double After { get; }
    public double RequestedDelta { get; }
    public double Added => Math.Max(0d, After - Before);
    public double Removed => Math.Max(0d, Before - After);
    public bool WasOnField { get; }
    public ResourceEventData(string resource, string poolId, BattleEntity owner, double before,
        double after, double requestedDelta, ResourceChangeReason reason, bool integer,
        TeamBonusSystem teamOwner = null, BattleEntity actor = null, BattleTeamStatusHost teamHost = null)
        : base(BattleEventKind.ResourceChanged, owner)
    {
        if ((teamOwner != null || teamHost != null) && owner != null)
            throw new ArgumentException("资源拥有者不能同时是队伍与角色。");
        if (teamOwner != null && teamHost != null && !ReferenceEquals(teamOwner.Host, teamHost))
            throw new ArgumentException("资源队伍宿主与TeamOwner不一致。");
        Resource = resource; PoolId = poolId; Before = before; After = after;
        RequestedDelta = requestedDelta; Reason = reason; IsInteger = integer;
        WasOnField = owner != null && owner.CharacterCtrl != null && owner.CharacterCtrl.IsActive;
        TeamOwner = teamOwner; Actor = actor ?? owner; ActorId = Actor != null ? Actor.EntityID : 0;
        TeamHost = teamHost ?? teamOwner?.Host;
        ActorWasOnField = Actor?.CharacterCtrl?.IsActive == true;
    }
}

public sealed class ActionEventData : BattleEventData
{
    public string SkillId { get; }
    public string ActionType { get; }
    public ActionEventData(BattleEntity actor, string skillId, string actionType, bool completed)
        : base(completed ? BattleEventKind.ActionCompleted : BattleEventKind.ActionStarted, actor)
    { SkillId = skillId; ActionType = actionType; }
}

/// <summary>只标记已提交换人的原因；Unknown不推断为主动，免费不等于阵亡强制。</summary>
public enum BattleSwitchReason { Unknown, Manual, DeathForced, SkillAutomatic, InitialEntry, ReturnAutomatic }

public sealed class SwitchEventData : BattleEventData
{
    public BattleEntity Outgoing { get; }
    public int OutgoingId { get; }
    public int IncomingId { get; }
    public BattleSwitchReason Reason { get; }
    public SwitchEventData(BattleEntity outgoing, BattleEntity incoming, BattleSwitchReason reason = BattleSwitchReason.Unknown)
        : base(BattleEventKind.SwitchResolved, incoming)
    { Outgoing = outgoing; OutgoingId = outgoing != null ? outgoing.EntityID : 0; IncomingId = SubjectId; Reason = reason; }
}

public sealed class StatusEventData : BattleEventData
{
    public string StatusId { get; }
    public long InstanceId { get; }
    public object Host { get; }
    public string Reason { get; }
    // 赋予时的显示快照：延迟显示不能反查已被移除/替换的状态，也不能把施加者当成格子宿主。
    public string StatusName { get; }
    public int Display { get; }
    public BattleSide? HostSide { get; }
    public int HostSlot { get; }
    public StatusEventData(StatusInstance status, object host, bool added, string reason)
        : base(added ? BattleEventKind.StatusAdded : BattleEventKind.StatusRemoved,
            host is BattleTeamStatusHost ? null : host as BattleEntity ?? status.Caster)
    {
        StatusId = status.StatusID2; InstanceId = status.ApplyOrder; Host = host; Reason = reason;
        StatusName = status.MainData?.StatusName ?? string.Empty;
        Display = status.MainData?.Display ?? 0;
        if (host is BattleEntity entity) { HostSide = entity.Side; HostSlot = entity.SlotPosition; }
        else if (host is FieldPosition field) { HostSide = field.Side; HostSlot = field.SlotIndex; }
        // 队伍宿主没有个人卡牌，不投影到施加者或每位受益角色。
    }
}

public sealed class DeathEventData : BattleEventData
{
    private readonly DamageSourceInfo source;
    public DamageSourceInfo Source => source.Snapshot();
    public DeathEventData(DamageResolvedEvent damage) : base(BattleEventKind.Death, damage.Target)
    { source = damage.Source?.Snapshot() ?? DamageSourceInfo.CreateUnknown(); }
}

public sealed class BattleDeclaration
{
    public BattleEventHeader Header { get; }
    public BattleEventData Data { get; }
    public BattleEventKind Kind => Data.Kind;
    internal bool Dispatched;
    internal Action CollectResponses;
    internal BattleDeclaration(BattleEventHeader header, BattleEventData data) { Header = header; Data = data; }
    public T Get<T>() where T : BattleEventData => Data as T;
}

/// <summary>
/// 统一声明中心。只接受已提交事实，不替代业务计算或现有响应队列。
/// 不保存全场历史；结果由发生对象/当前响应持有，不能查询“全局最后一次”。
/// </summary>
public static class BattleEventHub
{
    private sealed class Frame
    {
        internal long Session, Resolution, ActionExecution, Execution, Hit;
        internal int Target;
        internal Frame Copy() => (Frame)MemberwiseClone();
    }
    private sealed class Restore : IDisposable
    {
        private Action restore;
        internal Restore(Action action) { restore = action; }
        public void Dispose() { Action action = restore; restore = null; action?.Invoke(); }
    }
    private sealed class Listener : IDisposable
    {
        internal BattleEventKind Kind;
        internal long Order;
        internal Func<bool> Active;
        internal Action<BattleDeclaration> Receive;
        internal bool Removed;
        public void Dispose() { Removed = true; Listeners.Remove(this); }
    }
    private static readonly AsyncLocal<Frame> FrameSlot = new AsyncLocal<Frame>();
    private static readonly AsyncLocal<BattleDeclaration> EventSlot = new AsyncLocal<BattleDeclaration>();
    private static readonly List<Listener> Listeners = new List<Listener>();
    private static ConditionalWeakTable<object, BattleDeclaration> Recorded = new ConditionalWeakTable<object, BattleDeclaration>();
    private static long session = 1, sequence;
    /// <summary>会话清理后的生命周期通知；表现层在此清空旧显示并重新订阅，不是战斗内事实声明。</summary>
    public static event Action SessionReset;
    public static long NextIdentity() => Interlocked.Increment(ref sequence);
    public static BattleDeclaration Current
    {
        get
        {
            BattleDeclaration value = EventSlot.Value ?? ScriptHookContext.Current?.Declaration;
            return value != null && value.Header.SessionId == session ? value : null;
        }
    }

    public sealed class Snapshot
    {
        private readonly Frame frame;
        private readonly BattleDeclaration declaration;
        internal Snapshot() { frame = FrameSlot.Value; declaration = Current; }
        public IDisposable Enter()
        {
            Frame previous = FrameSlot.Value; BattleDeclaration old = EventSlot.Value;
            if (frame != null && frame.Session == session) FrameSlot.Value = frame;
            EventSlot.Value = declaration != null && declaration.Header.SessionId == session ? declaration : null;
            return new Restore(() => { FrameSlot.Value = previous; EventSlot.Value = old; });
        }
    }
    public static Snapshot Capture() => new Snapshot();

    public static IDisposable BeginResolution()
    {
        Frame old = FrameSlot.Value;
        if (old == null || old.Session != session)
            FrameSlot.Value = new Frame { Session = session, Resolution = NextIdentity() };
        return new Restore(() => FrameSlot.Value = old);
    }
    public static IDisposable BeginEffectExecution(long execution)
    {
        IDisposable root = BeginResolution(); Frame old = FrameSlot.Value;
        Frame next = old.Copy();
        if (execution != 0 && execution != old.Execution)
        { next.Execution = execution; next.Hit = 0; next.Target = 0; }
        FrameSlot.Value = next;
        return new Restore(() => { FrameSlot.Value = old; root.Dispose(); });
    }
    /// <summary>
    /// 完整技能序列边界，不发布事件、不另建响应队列，也不改变原有连锁限次作用域。
    /// 主动行动取得新身份；非主动技能容器以0隔离，不能借用外层行动的身份。
    /// </summary>
    public static IDisposable BeginActionExecution(bool isAction = true)
    {
        IDisposable root = BeginResolution(); Frame old = FrameSlot.Value;
        Frame next = old.Copy(); next.ActionExecution = isAction ? NextIdentity() : 0;
        next.Execution = 0; next.Hit = 0; next.Target = 0; FrameSlot.Value = next;
        return new Restore(() => { FrameSlot.Value = old; root.Dispose(); });
    }
    public static IDisposable BeginHit(long hit = 0, int targetIndex = 0)
    {
        IDisposable root = BeginResolution(); Frame old = FrameSlot.Value;
        Frame next = old.Copy(); next.Hit = hit != 0 ? hit : NextIdentity(); next.Target = targetIndex; FrameSlot.Value = next;
        return new Restore(() => { FrameSlot.Value = old; root.Dispose(); });
    }
    public static long CurrentHitId => FrameSlot.Value?.Hit ?? 0;
    /// <summary>当前真实执行帧，不取触发当前响应的父声明编号。</summary>
    public static long CurrentEffectExecutionId => FrameSlot.Value?.Execution ?? 0;
    public static IDisposable Enter(BattleDeclaration declaration)
    {
        BattleDeclaration old = EventSlot.Value;
        Frame previous = FrameSlot.Value;
        if (declaration != null && declaration.Header.SessionId == session)
            FrameSlot.Value = new Frame { Session = session, Resolution = declaration.Header.ResolutionId,
                ActionExecution = declaration.Header.ActionExecutionId,
                Execution = declaration.Header.EffectExecutionId, Hit = declaration.Header.HitId,
                Target = declaration.Header.TargetIndex };
        EventSlot.Value = declaration;
        return new Restore(() => { EventSlot.Value = old; FrameSlot.Value = previous; });
    }
    public static BattleDeclaration Find(object occurrence)
    {
        return occurrence != null && Recorded.TryGetValue(occurrence, out BattleDeclaration result) ? result : null;
    }
    public static BattleDeclaration Record(object occurrence, BattleEventData data, long hit = 0,
        int targetIndex = 0, IEnumerable<long> causes = null)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        BattleDeclaration existing = Find(occurrence);
        if (existing != null) return existing;
        using (BeginResolution())
        {
            Frame frame = FrameSlot.Value;
            var ids = new List<long>();
            if (causes != null) ids.AddRange(causes);
            else if (Current != null) ids.Add(Current.Header.EventId);
            var result = new BattleDeclaration(new BattleEventHeader(session, NextIdentity(), frame.Resolution,
                BattleResolutionContext.CurrentAction?.ScopeId ?? 0, BattleResolutionContext.CurrentEffect?.ScopeId ?? 0,
                frame.Execution, hit != 0 ? hit : frame.Hit, targetIndex != 0 ? targetIndex : frame.Target, ids,
                frame.ActionExecution), data);
            // 冻结发生时的订阅者/状态实例。延后Dispatch不能让后来创建的监听者追溯旧事件。
            Listener[] listeners = Listeners.FindAll(listener => listener.Kind == result.Kind && !listener.Removed).ToArray();
            Action hooks = StatusOnHitHookSystem.CaptureDeclaration(result);
            BattleSpecialRules.ObserveDeclaration(result);
            result.CollectResponses = () =>
            {
                foreach (Listener listener in listeners) SubmitListener(listener, result);
                hooks?.Invoke();
            };
            if (occurrence != null) Recorded.Add(occurrence, result);
            return result;
        }
    }
    public static BattleDeclaration Publish(BattleEventData data, object occurrence = null, long hit = 0,
        int targetIndex = 0, IEnumerable<long> causes = null)
    {
        using (BeginResolution())
        {
            BattleDeclaration result = Record(occurrence, data, hit, targetIndex, causes);
            Dispatch(result);
            return result;
        }
    }
    public static void Dispatch(BattleDeclaration result)
    {
        if (result == null || result.Dispatched || result.Header.SessionId != session) return;
        result.Dispatched = true;
        // 只复制已正式提交的结果；Record阶段的治疗计划等不能进入动画队列。
        BattleExecution.RecordCommittedDeclaration(result);
        using (BattleResolutionContext.BeginCollection())
        using (Enter(result))
        {
            Action collect = result.CollectResponses;
            result.CollectResponses = null;
            collect?.Invoke();
        }
    }
    private static void SubmitListener(Listener listener, BattleDeclaration result)
    {
        BattleResolutionContext.Submit(listener.Order, () =>
        {
            if (listener.Removed || (listener.Active != null && !listener.Active())) return null;
            return new BattleResolutionContext.PreparedTrigger(async () =>
            {
                if (listener.Removed || (listener.Active != null && !listener.Active())) return;
                using (BattleResolutionContext.EnterTrigger())
                using (Enter(result))
                {
                    listener.Receive(result);
                    await BattleExecution.Flush();
                }
            }, null);
        });
    }
    public static IDisposable Subscribe<T>(BattleEventKind kind, Action<BattleEventHeader, T> receive,
        long applyOrder, Func<bool> isActive = null) where T : BattleEventData
    {
        if (receive == null) throw new ArgumentNullException(nameof(receive));
        var listener = new Listener { Kind = kind, Order = applyOrder, Active = isActive,
            Receive = result =>
            {
                T data = result.Get<T>();
                if (data == null) throw new InvalidOperationException("声明订阅的数据类型与Kind不匹配。");
                receive(result.Header, data);
            } };
        Listeners.Add(listener);
        return listener;
    }
    public static void ResetSession()
    {
        Interlocked.Increment(ref session);
        Recorded = new ConditionalWeakTable<object, BattleDeclaration>();
        foreach (Listener listener in Listeners) listener.Removed = true;
        Listeners.Clear(); FrameSlot.Value = null; EventSlot.Value = null;
        if (SessionReset != null)
            foreach (Action listener in SessionReset.GetInvocationList())
                try { listener(); }
                catch (Exception error) { UnityEngine.Debug.LogException(error); }
    }
}
