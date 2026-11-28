// 文件路径：Assets/Scripts/Battle/ActionPointManager.cs
using UnityEngine;

/// <summary>
/// 行动点管理器：每回合 100 AP，负责查询、消耗、重置
/// </summary>
public class ActionPointManager
{
    public int MaxAP => 100;
    public int CurrentAP { get; private set; }

    /// <summary>
    /// 新回合开始时调用
    /// </summary>
    public void ResetAP()
    {
        CurrentAP = MaxAP;
        LogManager.Log(LogCategory.AP, $"行动点重置为 {CurrentAP}");
    }

    /// <summary>
    /// 检查是否够支付
    /// </summary>
    public bool CanAfford(int cost) => CurrentAP >= cost;

    /// <summary>
    /// 尝试消耗 AP，成功返回 true
    /// </summary>
    public bool ConsumeAP(int cost)
    {
        if (!CanAfford(cost))
        {
            LogManager.LogWarning(LogCategory.AP, $"行动点不足！需要 {cost}，剩余 {CurrentAP}");
            return false;
        }
        CurrentAP -= cost;
        LogManager.Log(LogCategory.AP, $"消耗 {cost} 点，剩余 {CurrentAP}");
        return true;
    }

    /// <summary>Restore shared AP; optional overflow preserves the amount above MaxAP, bounded by int.MaxValue.</summary>
    public int RecoverAP(int amount, bool allowOverflow = false, BattleEntity actor = null)
    {
        if (amount < 0) throw new System.ArgumentOutOfRangeException(nameof(amount));
        int before = CurrentAP;
        long next = (long)before + amount;
        CurrentAP = allowOverflow ? (int)System.Math.Min(int.MaxValue, next)
            : System.Math.Max(before, (int)System.Math.Min(MaxAP, next));
        var battle = BattleManager.Instance;
        if (CurrentAP != before && battle != null && System.Object.ReferenceEquals(battle.APManager, this))
            BattleEventHub.Publish(new ResourceEventData("AP", "SharedAP", null, before, CurrentAP, amount,
                ResourceChangeReason.Gain, true, actor: actor, teamHost: battle.AllyTeam));
        return CurrentAP - before;
    }
}