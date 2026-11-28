using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

/// <summary>Accepted action label recorded on the presentation sequence only; never dispatched to gameplay.</summary>
public sealed class BattleActionPromptData : BattleEventData
{
    public readonly string Name;
    public readonly string SkillId;
    public readonly bool IsStatus;
    public readonly bool FollowsTargetSelection;
    public readonly long SourceEffectExecutionId;
    public BattleActionPromptData(BattleEntity source, string name, bool isStatus, string skillId,
        bool followsTargetSelection = false, long sourceEffectExecutionId = 0)
        : base(BattleEventKind.ActionPrompt, source)
    { Name = name; IsStatus = isStatus; SkillId = skillId; FollowsTargetSelection = followsTargetSelection;
        SourceEffectExecutionId = sourceEffectExecutionId; }
}

/// <summary>Snapshots in actual submission order, including display-only action labels.</summary>
public sealed class BattlePresentationResult
{
    public int Order { get; }
    public BattleEventHeader Header { get; }
    public BattleEventData Data { get; }
    public long RequestID { get; }
    public string EffectID { get; }
    public int SegmentIndex { get; }
    public BattleSide? TargetSide { get; }
    public int TargetSlot { get; }
    public BattleSide? SourceSide { get; }
    public int SourceSlot { get; }
    public BattleEntity SourceEntity { get; }
    // Presentation lanes never replace gameplay execution/hit identities.
    internal long StatusBatchID { get; }
    internal long StatusBatchMemberID { get; }

    private BattlePresentationResult(int order, BattlePresentationResult original)
    {
        Order = order; Header = original.Header; Data = original.Data;
        RequestID = original.RequestID; EffectID = original.EffectID; SegmentIndex = original.SegmentIndex;
        TargetSide = original.TargetSide; TargetSlot = original.TargetSlot;
        SourceSide = original.SourceSide; SourceSlot = original.SourceSlot; SourceEntity = original.SourceEntity;
        StatusBatchID = original.StatusBatchID; StatusBatchMemberID = original.StatusBatchMemberID;
    }
    internal BattlePresentationResult WithOrder(int order) => new BattlePresentationResult(order, this);

    internal BattlePresentationResult(int order, BattleDeclaration declaration, BattlePresentationRequest request)
    {
        Order = order; Header = declaration.Header; Data = declaration.Data;
        StatusBatchID = BattleStatusPresentationBatch.CurrentBatch;
        StatusBatchMemberID = BattleStatusPresentationBatch.CurrentMember;
        RequestID = request?.ID ?? 0;
        EffectID = request?.EffectID ?? string.Empty;
        SourceEntity = request?.Source;
        if (SourceEntity != null) { SourceSide = SourceEntity.Side; SourceSlot = SourceEntity.SlotPosition; }
        SegmentIndex = request?.SegmentIndex ?? 0;
        if (Data is DamageEventData damage)
        {
            EffectID = damage.Source.SourceEffectID ?? EffectID;
            SegmentIndex = damage.HitIndex;
            if (!SourceSide.HasValue && damage.Source.SourceEntity != null)
            { SourceEntity = damage.Source.SourceEntity; SourceSide = damage.Source.SourceSide; SourceSlot = SourceEntity.SlotPosition; }
        }
        else if (Data is HealEventData heal) { EffectID = heal.EffectId; SegmentIndex = heal.SegmentIndex; }
        else if (Data is ReactionEventData reaction) EffectID = reaction.EffectId;
        if (Data is StatusEventData status) { TargetSide = status.HostSide; TargetSlot = status.HostSlot; }
        else if (Data.Subject != null) { TargetSide = Data.Subject.Side; TargetSlot = Data.Subject.SlotPosition; }
    }
}

/// <summary>Explicit phase-batch presentation scope. Business events and execution allowances stay independent.</summary>
internal sealed class BattleStatusPresentationBatch
{
    private sealed class Member : IDisposable
    {
        internal readonly Member Previous = current.Value;
        internal readonly long Batch, Instance;
        internal Member(long batch, long instance) { Batch = batch; Instance = instance; current.Value = this; }
        public void Dispose() => current.Value = Previous;
    }
    private static readonly AsyncLocal<Member> current = new AsyncLocal<Member>();
    private static long nextID;
    private readonly long id;
    internal BattleStatusPresentationBatch(bool parallel) { id = parallel ? Interlocked.Increment(ref nextID) : 0; }
    internal IDisposable Enter(StatusInstance status) => new Member(id, id == 0 ? 0 : status.ApplyOrder);
    internal static long CurrentBatch => current.Value?.Batch ?? 0;
    internal static long CurrentMember => current.Value?.Instance ?? 0;

    internal static long Source(StatusInstance status)
    {
        long source = status?.SourceEffectExecutionId ?? 0;
        // BindStatus children retain their own gameplay source, but share their original application cohort visually.
        for (var parent = status?.BindingParent; parent != null; parent = parent.BindingParent)
            if (parent.SourceEffectExecutionId != 0) source = parent.SourceEffectExecutionId;
        return source;
    }
}

/// <summary>一次阶段或一次真实事件内已经显示过的状态批次。仅记提示，独立于结算会话。</summary>
internal sealed class BattleStatusPromptOccurrence
{
    private static readonly ConditionalWeakTable<object, BattleStatusPromptOccurrence> events = new();
    private readonly HashSet<(long sourceEffect, string actionType, string statusID)> announced = new();

    internal static BattleStatusPromptOccurrence ForEvent(object fact) => events.GetValue(fact, _ => new BattleStatusPromptOccurrence());
    internal bool TryAnnounce(long source, string actionType, string statusID) => announced.Add((source, actionType, statusID));
}

/// <summary>一次受控结算的只读结果序列；重复Dispatch不重复收录，Seal后禁止追加。</summary>
public sealed class BattlePresentationSequence
{
    private static long nextID;
    private readonly List<BattlePresentationResult> results = new List<BattlePresentationResult>();
    private readonly HashSet<(long, long)> identities = new HashSet<(long, long)>();
    private readonly HashSet<(long, long)> announcedStatuses = new HashSet<(long, long)>();
    internal void RecordPrompt(BattleEntity actor, string name, StatusInstance status, string skillId = null,
        bool followsTargetSelection = false, string actionType = null, ScriptHookContext context = null)
    {
        if (actor == null || string.IsNullOrWhiteSpace(name)) return;
        long source = BattleStatusPresentationBatch.Source(status);
        if (source != 0)
        {
            var occurrence = context?.ActionPrompts;
            if (occurrence == null)
            {
                object fact = actionType == "OnTrigger" ? BattleResolutionContext.StatusActionOccurrence(context) : null;
                occurrence = BattleStatusPromptOccurrence.ForEvent(fact ?? (object)BattleResolutionContext.CurrentChain ?? this);
            }
            if (!occurrence.TryAnnounce(source, actionType, status?.StatusID2)) return;
        }
        var header = BattleEventHub.PresentationHeader();
        if (source == 0 && status != null && !announcedStatuses.Add((status.ApplyOrder, header.ChainScopeId))) return;
        Record(new BattleDeclaration(header, new BattleActionPromptData(actor, name, status != null, skillId,
            followsTargetSelection, source)));
    }
    public long ID { get; } = Interlocked.Increment(ref nextID);
    public IReadOnlyList<BattlePresentationResult> Results => results.AsReadOnly();
    public bool IsSealed { get; private set; }

    internal void Record(BattleDeclaration declaration, BattlePresentationRequest request = null)
    {
        if (IsSealed) throw new InvalidOperationException("表现序列已关闭。");
        if (!identities.Add((declaration.Header.SessionId, declaration.Header.EventId))) return;
        results.Add(new BattlePresentationResult(results.Count, declaration, request));
    }
    internal void Seal() => IsSealed = true;

    internal static BattlePresentationSequence ForTimeline(IEnumerable<BattlePresentationResult> records)
    {
        var sequence = new BattlePresentationSequence();
        sequence.results.AddRange(records);
        sequence.Seal();
        return sequence;
    }

    /// <summary>合并同帧的独立只读结果，保留所有身份和提交时快照，不重新分发业务事件。</summary>
    internal static BattlePresentationSequence Combine(IReadOnlyList<BattlePresentationSequence> sequences)
    {
        var combined = new BattlePresentationSequence();
        foreach (var sequence in sequences)
        {
            if (!sequence.IsSealed) throw new InvalidOperationException("不能合并尚未关闭的表现序列。");
            foreach (var result in sequence.Results)
                if (combined.identities.Add((result.Header.SessionId, result.Header.EventId)))
                    combined.results.Add(result.WithOrder(combined.results.Count));
        }
        combined.Seal(); return combined;
    }
}

/// <summary>可选扩展。旧的逐步播放器仍可工作；结果播放器只消费结算完成的只读序列。</summary>
public interface IBattleResultPresentationPlayer : IBattlePresentationPlayer
{
    Task PlayResultsAsync(BattlePresentationSequence sequence, CancellationToken cancellation);
    void SkipResults();
    void CancelResults();
}

/// <summary>没有受控执行帧的正式结果也走同一表现入口，不再由跳字直接监听战斗响应。</summary>
public static class BattlePresentationResults
{
    public static event Action<BattlePresentationSequence> Unsequenced;
    internal static void Publish(BattleDeclaration declaration, BattlePresentationRequest request)
    {
        var sequence = new BattlePresentationSequence();
        sequence.Record(declaration, request); sequence.Seal();
        if (Unsequenced == null) return;
        foreach (Action<BattlePresentationSequence> listener in Unsequenced.GetInvocationList())
            try { listener(sequence); }
            catch (Exception error) { UnityEngine.Debug.LogException(error); }
    }
}
