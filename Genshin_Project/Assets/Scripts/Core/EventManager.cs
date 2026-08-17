// 文件路径：Assets/Scripts/Core/EventManager.cs
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全局事件系统：模块间解耦通信的核心
/// 使用方法：
///   发送方：EventManager.Emit(GameEvent.BattleStart, null);
///   接收方：EventManager.On(GameEvent.BattleStart, (data) => { Debug.Log("战斗开始"); });
///   务必在 OnDestroy / OnDisable 中 Off，避免内存泄漏和空引用。
/// </summary>
public static class EventManager
{
    // 事件类型枚举（后续按需扩充）
    public enum GameEvent
    {
        // 战斗
        BattleStart,            // 战斗开始
        BattleEnd,              // 战斗结束（data: bool? 是否胜利）
        TurnPhaseChanged,       // 回合阶段切换（data: TurnPhase 枚举）
        EntityDamaged,          // 实体受到伤害（data: DamageInfo）
        EntityHealed,           // 实体受到治疗（data: HealInfo）
        EntityDied,             // 实体死亡（data: BattleEntity）
        EnergyChanged,          // 能量变更（data: EnergyData）
        StatusApplied,          // 状态施加（data: StatusInfo）
        StatusRemoved,          // 状态移除（data: StatusInfo）
        ElementalReactionTriggered, // 元素反应触发（data: ReactionInfo）
        BattleLog,  // 战斗日志（data: string）
    }

    // 委托：无参或带一个 object 参数
    private static Dictionary<GameEvent, Action<object>> eventTable = new Dictionary<GameEvent, Action<object>>();

    /// <summary>
    /// 注册事件监听
    /// </summary>
    public static void On(GameEvent eventType, Action<object> callback)
    {
        if (eventTable.ContainsKey(eventType))
            eventTable[eventType] += callback;
        else
            eventTable[eventType] = callback;
    }

    /// <summary>
    /// 注销事件监听
    /// </summary>
    public static void Off(GameEvent eventType, Action<object> callback)
    {
        if (eventTable.ContainsKey(eventType))
        {
            eventTable[eventType] -= callback;
            if (eventTable[eventType] == null)
                eventTable.Remove(eventType);
        }
    }

    /// <summary>
    /// 发送事件（带数据）
    /// </summary>
    public static void Emit(GameEvent eventType, object data = null)
    {
        if (eventTable.TryGetValue(eventType, out var callbacks))
        {
            callbacks?.Invoke(data);
        }
    }

    /// <summary>
    /// 清空所有事件监听（场景切换时可用，正常不需要）
    /// </summary>
    public static void ClearAll()
    {
        eventTable.Clear();
    }
}