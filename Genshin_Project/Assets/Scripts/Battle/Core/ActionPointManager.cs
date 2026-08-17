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
}