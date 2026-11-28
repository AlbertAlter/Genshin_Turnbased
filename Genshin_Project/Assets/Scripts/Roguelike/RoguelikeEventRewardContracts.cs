using System;

/// <summary>
/// 事件奖励展示扩展入口，在事件胜利及ReturnToRoute原子入账后调用。
/// Rewards已按Rewards子表与真实计数计算并入账；扩展者只展示，不重复发放。
/// 未绑定扩展者时公共宿主在路线显示同一奖励浮层；CompletionId用于识别展示请求。
/// </summary>
public interface IRoguelikeEventRewardEntry
{
    void OnReturnedToRoute(RoguelikeEventRewardContext context);
}

/// <summary>不可变的事件完成身份快照。没有背包引用、预设奖品或可修改的流程快照。</summary>
public sealed class RoguelikeEventRewardContext
{
    public int EnemyKills { get; }
    public System.Collections.Generic.IReadOnlyList<RoguelikeItemStackData> Rewards { get; }
    public string RunId { get; }
    public string EventId { get; }
    public string NodeId { get; }
    public string BattleContentId { get; }
    public string BattleId { get; }
    public string SettlementStepId { get; }
    public int ChapterId { get; }
    public int StageIndex { get; }
    public string CompletionId => RunId + ":" + SettlementStepId;

    private RoguelikeEventRewardContext(RoguelikeFlowContext completed, RoguelikeNodeContentCatalog catalog)
    {
        var node = completed.Flow.Node;
        RunId = completed.RunId; EventId = node.ContentId; NodeId = node.NodeId;
        BattleContentId = node.EventBattleContentId; BattleId = completed.BattleId;
        SettlementStepId = completed.Flow.StepId;
        ChapterId = completed.Snapshot.CurrentChapterId; StageIndex = node.StageIndex;
        EnemyKills = completed.Flow.EventEnemyKills;
        Rewards = (catalog?.EventRewards(EventId, EnemyKills) ?? new System.Collections.Generic.List<RoguelikeItemStackData>()).AsReadOnly();
    }

    /// <summary>在清除当前节点前捕获；调用方只能在ReturnToRoute成功后排入路线通知。</summary>
    public static RoguelikeEventRewardContext Capture(RoguelikeFlowContext completed, RoguelikeNodeContentCatalog catalog = null)
    {
        if (completed?.Snapshot?.Flow?.Step != RoguelikeFlowStep.Settlement ||
            completed.Flow.BattleDefeated ||
            completed.Flow.Node?.Kind != RoguelikeNodeKind.Event ||
            string.IsNullOrWhiteSpace(completed.Flow.Node.EventBattleContentId) ||
            string.IsNullOrWhiteSpace(completed.RunId) || string.IsNullOrWhiteSpace(completed.Flow.StepId))
            return null;
        return new RoguelikeEventRewardContext(completed, catalog);
    }

    public bool MatchesRoute(RoguelikeFlowContext current)
        => current?.Snapshot?.Flow?.Step == RoguelikeFlowStep.Route && current.RunId == RunId &&
            current.Snapshot.CurrentChapterId == ChapterId && current.Snapshot.CurrentNodeId == NodeId &&
            current.Snapshot.ClearedNodeIds.Contains(NodeId);
}
