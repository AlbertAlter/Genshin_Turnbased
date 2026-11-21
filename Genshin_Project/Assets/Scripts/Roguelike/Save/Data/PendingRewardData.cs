using System;

[Serializable]
public class PendingRewardData
{
    public string RewardId;
}

/// <summary>已完成节点的固定奖励清单。胜利/事件完成时固化，入账与清除此记录同次提交。</summary>
[Serializable]
public sealed class RoguelikePendingNodeReward
{
    public int StageId;
    public string NodeId;
    public System.Collections.Generic.List<RoguelikeItemStackData> Items;
}
