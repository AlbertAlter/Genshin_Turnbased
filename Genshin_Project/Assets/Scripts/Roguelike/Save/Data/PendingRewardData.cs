using System;

[Serializable]
public class PendingRewardData
{
    public string RewardId;
}

/// <summary>节点首次战斗结束或事件完成时固化的固定奖励。入账、领奖记录和清除此清单同次提交。</summary>
[Serializable]
public sealed class RoguelikePendingNodeReward
{
    public int StageId;
    public string NodeId;
    public System.Collections.Generic.List<RoguelikeItemStackData> Items;
}
