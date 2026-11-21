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

    internal BattlePresentationResult(int order, BattleDeclaration declaration, BattlePresentationRequest request)
    {
        Order = order; Header = declaration.Header; Data = declaration.Data;
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

/// <summary>一次阶段或一次真实事件内已经显示过的状态批次。仅记提示，独立于结算会话。</summary>
internal sealed class BattleStatusPromptOccurrence
{
    private static readonly ConditionalWeakTable<object, BattleStatusPromptOccurrence> events = new();
    private readonly HashSet<(long sourceEffect, string actionType)> announced = new();

    internal static BattleStatusPromptOccurrence ForEvent(object fact) => events.GetValue(fact, _ => new BattleStatusPromptOccurrence());
    internal bool TryAnnounce(long source, string actionType) => announced.Add((source, actionType));
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
        long source = status?.SourceEffectExecutionId ?? 0;
        if (source != 0)
        {
            var occurrence = context?.ActionPrompts;
            if (occurrence == null)
            {
                object fact = actionType == "OnTrigger" ? BattleResolutionContext.StatusActionOccurrence(context) : null;
                occurrence = BattleStatusPromptOccurrence.ForEvent(fact ?? (object)BattleResolutionContext.CurrentChain ?? this);
            }
            if (!occurrence.TryAnnounce(source, actionType)) return;
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
