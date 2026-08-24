using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 日志分类（2026-08-14）：集中式日志管理。
/// 各脚本通过 LogManager 发日志，统一前缀/格式，便于分类过滤与开关控制。
/// 现有日志种类已全部整理进来，标签与原先完全一致（如 [Turn]/[Damage]），替换后日志格式不变。
/// </summary>
public enum LogCategory{
    // 战斗流程
    Turn,        // [Turn] 阶段切换
    AP,          // [AP] 行动点
    Battle,      // [战斗] 胜负判定
    Ally,        // [Ally] 我方角色分配
    // 技能/伤害
    Skill,       // [Skill] 技能施放
    Damage,      // [Damage] 技能伤害
    StatusDamage,// [StatusDamage] 状态伤害
    Crit,        // [暴击] 暴击判定
    Reaction,    // [反应] 元素反应
    Splash,      // [Splash] 溅射
    GainEnergy,  // [GainEnergy] 能量获得
    StatusEnergy,// [StatusEnergy] 状态能量
    DmgBonus,    // [DmgBonus] 伤害加成
    GetHitData,  // [GetHitData] 伤害数据解析
    Effect,      // [Effect] 效果执行
    // 状态
    Status,      // [Status] 状态通用
    StatusAction,// [StatusAction] 状态行动
    StatusEffect,// [StatusEffect] 状态效果
    ApplyStatus, // [ApplyStatus] 状态施加
    RemoveStatus,// [RemoveStatus] 状态移除
    Bind,        // [Bind] 子状态绑定
    AddTurn,     // [AddTurn] 回合延长
    ChangeControl,// [ChangeControl] 按键绑定修改
    // 选择/操作
    Select,      // [Select] 目标选择
    Action,      // [Action] 技能入口
    // 敌人
    Enemy,       // [Enemy] 敌人行动/伤害
    AI,          // [AI调试] 敌人AI
    // 界面
    UI,          // [UI] 界面
    // 构建/钩子
    Build,       // [Build] 天赋/命座/指令应用
    ScriptHook,  // [ScriptHook] 钩子判定
    PreDamageHook,// [PreDamageHook] 主动伤害钩子
    // 数据
    Data         // [DataManager] 数据加载/校验
}

/// <summary>
/// 集中式日志管理器（2026-08-14）。
/// 用法：LogManager.Log(LogCategory.Damage, $"{Entity.EntityID}攻击...");
/// </summary>
public static class LogManager{
    private static readonly HashSet<LogCategory> VerboseCategories = new HashSet<LogCategory>
    {
        LogCategory.AI,
        LogCategory.DmgBonus,
        LogCategory.GetHitData
    };

    private static readonly HashSet<LogCategory> DisabledCategories = new HashSet<LogCategory>();

    public static bool EnableInfoLogs { get; set; } = true;
    public static bool EnableVerboseLogs { get; set; } = false;

    static readonly Dictionary<LogCategory, string> Tags = new Dictionary<LogCategory, string>{
        { LogCategory.Turn,          "[Turn] " },
        { LogCategory.AP,            "[AP] " },
        { LogCategory.Battle,        "[战斗] " },
        { LogCategory.Ally,          "[Ally] " },
        { LogCategory.Skill,         "[Skill] " },
        { LogCategory.Damage,        "[Damage] " },
        { LogCategory.StatusDamage,  "[StatusDamage] " },
        { LogCategory.Crit,          "[暴击] " },
        { LogCategory.Reaction,      "[反应] " },
        { LogCategory.Splash,        "[Splash] " },
        { LogCategory.GainEnergy,    "[GainEnergy] " },
        { LogCategory.StatusEnergy,  "[StatusEnergy] " },
        { LogCategory.DmgBonus,      "[DmgBonus] " },
        { LogCategory.GetHitData,    "[GetHitData] " },
        { LogCategory.Effect,        "[Effect] " },
        { LogCategory.Status,        "[Status] " },
        { LogCategory.StatusAction,  "[StatusAction] " },
        { LogCategory.StatusEffect,  "[StatusEffect] " },
        { LogCategory.ApplyStatus,   "[ApplyStatus] " },
        { LogCategory.RemoveStatus,  "[RemoveStatus] " },
        { LogCategory.Bind,          "[Bind] " },
        { LogCategory.AddTurn,       "[AddTurn] " },
        { LogCategory.ChangeControl, "[ChangeControl] " },
        { LogCategory.Select,        "[Select] " },
        { LogCategory.Action,        "[Action] " },
        { LogCategory.Enemy,         "[Enemy] " },
        { LogCategory.AI,            "[AI调试] " },
        { LogCategory.UI,            "[UI] " },
        { LogCategory.Build,         "[Build] " },
        { LogCategory.ScriptHook,    "[ScriptHook] " },
        { LogCategory.PreDamageHook, "[PreDamageHook] " },
        { LogCategory.Data,          "[DataManager] " },
    };

    public static bool IsEnabled(LogCategory category)
    {
        return EnableInfoLogs
            && !DisabledCategories.Contains(category)
            && (EnableVerboseLogs || !VerboseCategories.Contains(category));
    }

    public static void SetCategoryEnabled(LogCategory category, bool enabled)
    {
        if (enabled) DisabledCategories.Remove(category);
        else DisabledCategories.Add(category);
    }

    public static void Log(LogCategory category, string msg)
    {
        if (!IsEnabled(category)) return;
        Debug.Log(Tags[category] + msg);
    }

    public static void LogWarning(LogCategory category, string msg)
    {
        Debug.LogWarning(Tags[category] + msg);
    }

    public static void LogError(LogCategory category, string msg)
    {
        Debug.LogError(Tags[category] + msg);
    }
}
