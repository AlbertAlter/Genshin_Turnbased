using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>正式Dispatch后的表现快照。顺序取实际提交顺序，不按EventId或配表ID重新排序。</summary>
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

    internal BattlePresentationResult(int order, BattleDeclaration declaration, BattlePresentationRequest request)
    {
        Order = order; Header = declaration.Header; Data = declaration.Data;
        RequestID = request?.ID ?? 0;
        EffectID = request?.EffectID ?? string.Empty;
        SegmentIndex = request?.SegmentIndex ?? 0;
        if (Data is DamageEventData damage)
        {
            EffectID = damage.Source.SourceEffectID ?? EffectID;
            SegmentIndex = damage.HitIndex;
        }
        else if (Data is HealEventData heal) { EffectID = heal.EffectId; SegmentIndex = heal.SegmentIndex; }
        else if (Data is ReactionEventData reaction) EffectID = reaction.EffectId;
        if (Data is StatusEventData status) { TargetSide = status.HostSide; TargetSlot = status.HostSlot; }
        else if (Data.Subject != null) { TargetSide = Data.Subject.Side; TargetSlot = Data.Subject.SlotPosition; }
    }
}

/// <summary>一次受控结算的只读结果序列；重复Dispatch不重复收录，Seal后禁止追加。</summary>
public sealed class BattlePresentationSequence
{
    private static long nextID;
    private readonly List<BattlePresentationResult> results = new List<BattlePresentationResult>();
    private readonly HashSet<(long, long)> identities = new HashSet<(long, long)>();
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
