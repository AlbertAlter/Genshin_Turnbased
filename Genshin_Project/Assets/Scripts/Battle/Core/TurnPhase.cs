using System;

/// <summary>
/// 回合制状态机的六阶段枚举。
/// 编号与配表《表格术语解释》中 AddInPhase/TriggerPhase 的 1-6 完全对应：
/// 1 = 我方回合开始前
/// 2 = 我方回合（行动阶段，玩家操作/结束回合按键）
/// 3 = 我方回合结束后
/// 4 = 敌方回合开始前
/// 5 = 敌方回合（行动阶段）
/// 6 = 敌方回合结束后
/// </summary>
public enum TurnPhase
{
    AllyPreTurn = 1,     // 我方回合开始前
    AllyAction = 2,      // 我方回合（行动）
    AllyPostTurn = 3,    // 我方回合结束后
    EnemyPreTurn = 4,    // 敌方回合开始前
    EnemyAction = 5,     // 敌方回合（行动）
    EnemyPostTurn = 6    // 敌方回合结束后
}

/// <summary>
/// 阶段工具方法。
/// </summary>
public static class TurnPhaseUtil
{
    /// <summary>
    /// 返回下一阶段（1->2->3->4->5->6->1 循环）。
    /// </summary>
    public static TurnPhase Next(TurnPhase phase)
    {
        int v = (int)phase;
        v = (v % 6) + 1;
        return (TurnPhase)v;
    }
}
