using System;
using UnityEngine;

// ============================================================
// 配表数据校验器
// 按《表格术语解释（新）》规则校验关键列，失败直接抛异常
// （DataManager.ReloadAllData 不会吞掉异常，校验失败会回滚整套数据并中断加载，
//   确保配表错误在加载阶段暴露而不是运行期静默出错）。
//
// 程序层面空值=0，所以"空"与"0"不做区分；
// 校验规则里"不能填0"即"空也不能填"。
// ============================================================

/// <summary>配表校验失败异常。DataManager 的 catch 会识别该类型并重新抛出以中断加载。</summary>
public class TableValidationException : System.Exception
{
    public TableValidationException(string message) : base(message) { }
}

public static class TableValidator
{
    public static int ParseExecuteCount(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 1;
        if (int.TryParse(value.Trim(), out int count) && count > 0) return count;
        if (ExecuteCountEvaluator.IsSupportedHandler(value)) return 1;
        throw new TableValidationException($"ExecuteCount 必须为空、正整数或已支持的特殊处理器，当前=[{value}]");
    }

    public static string ParseExecuteCountHandler(string value)
    {
        ParseExecuteCount(value);
        if (string.IsNullOrWhiteSpace(value) || int.TryParse(value.Trim(), out _))
            return string.Empty;
        return value.Trim();
    }

    // 充能模式只接受项目约定的 Based / Flat。
    public static string NormalizeEnergyMode(string value)
    {
        string mode = value?.Trim() ?? string.Empty;
        if (mode == "Based" || mode == "Flat") return mode;
        throw new TableValidationException($"GainEnergy 模式必须为 Based 或 Flat，当前=[{value}]");
    }

    /// <summary>效果行（SkillsEffect / Status_Effect / EnemySkill_Effect 共用）校验。
    /// tableName 用于日志标识；isEnemy 控制 TargetConsecutive 规则；
    /// checkTargetNumber=false 时跳过 TargetNumber 检查（状态效果用 TargetSelect 不用数量）。</summary>
    public static void ValidateEffectRow(string tableName, int row,
        string effectType, string element, int duration, string param2, string targetType,
        int targetNumber, int targetConsecutive, string targetOverride,
        bool isEnemy, bool checkTargetNumber = true)
    {
        string tag = $"[配表校验] {tableName} 第{row}行 ({effectType})";

        // ===== 术语表 L101-L111：各效果类型需要的参数 =====
        // 不需要目标概念的效果（target完全忽略）：
        //   ExecuteSkill/ExecuteEffect —— 只认Param1（L109/L111）
        //   BindStatus/Buff/Debuff —— 状态存在即被动生效（L365）
        //   ChangeControl —— 只改按键绑定，参数只有param1(param2)（L369）
        bool noTarget = effectType == "ExecuteSkill" || effectType == "ExecuteEffect"
                     || effectType == "BindStatus" || effectType == "Buff" || effectType == "Debuff"
                     || effectType == "ChangeControl" || effectType == "APCost"
                     || effectType == "ResetCooldown";
        if (effectType == "ResetCooldown" && isEnemy)
            throw new TableValidationException($"{tag}: ResetCooldown 当前作用于角色技能；敌人没有对应的技能按钮绑定");
        // GainEnergy 的模式仍须合法；只有 Based 读取元素系数。
        // Flat 忽略 Element，允许留空或填 None/none。
        string energyMode = effectType == "GainEnergy" ? NormalizeEnergyMode(param2) : null;
        bool needsElement = effectType == "Damage" || energyMode == "Based";
        // ApplyStatus 的 Duration 留空/0 表示永久；角色、状态和敌人效果共用此规则。
        // 生命周期由 StatusInstance.IsPermanent 控制，不能在读表时拒绝永久状态。

        if (needsElement && string.IsNullOrEmpty(element))
        {
            throw new TableValidationException($"{tag}: Element不能为空/0（{effectType}需要填元素；None=物理也要写None）");
        }

        if (noTarget)
        {
            // 无目标概念的效果：不校验 TargetType/TargetNumber/TargetConsecutive
            return;
        }

        // ===== 有目标概念的效果（Damage/ApplyStatus/RemoveStatus/GainEnergy）=====
        // TargetType：不能填0，除非有 TargetOverride（L131 "除非被override不然不能填0"）
        if (string.IsNullOrEmpty(targetType) && string.IsNullOrEmpty(targetOverride))
        {
            throw new TableValidationException($"{tag}: TargetType不能为空/0（除非填了TargetOverride）");
        }

        // TargetNumber：填0且无override → 报错；有override → 忽略；Self 类型不需要number/consecutive
        //（L131 "若填self也不用识别number和consecutive"；L133 "-1为全部目标，大于0触发选取流程"）
        if (checkTargetNumber && targetType != "Self" && targetNumber == 0 && string.IsNullOrEmpty(targetOverride))
        {
            throw new TableValidationException($"{tag}: TargetNumber不能为0（无TargetOverride时；-1=全部；Self不需要填）");
        }

        // TargetOverride 非空时：忽略 TargetNumber/TargetConsecutive（L139/L141，不额外校验）

        // 敌人技能 TargetConsecutive：只能填2,3,4（L263 "没有选择目标的过程，不能填0和1，要报错"）
        // 例外：TargetType=Self 的效果（如跳舞给自己加buff）不需要选目标，跳过此校验
        if (isEnemy && targetType != "Self" && (targetConsecutive < 2 || targetConsecutive > 4))
        {
            throw new TableValidationException($"{tag}: 敌人技能TargetConsecutive只能填2,3,4（当前{targetConsecutive}）");
        }
    }

    public static void ValidateResetCooldown(string tableName, int row, string param1)
    {
        if (!ResetCooldownTarget.IsValid(param1))
            throw new TableValidationException($"[配表校验] {tableName} 第{row}行 (ResetCooldown): Param1 必须为正整数技能ID、SK_字符串ID或Normal/Heavy/Skill/Burst，当前=[{param1}]");
    }

    public static void ValidateBondOfLife(string tableName, int row, string param1)
    {
        if (!BondOfLifeEffect.IsValidExpression(param1))
            throw new TableValidationException($"[配表校验] {tableName} 第{row}行 (BondOfLife): Param1 必须为有限正负数或系数*变量。系数可用武器Index(n)；变量支持TotalHP/CurrentHP/TotalATK/TotalDEF/TotalEM/CurrentEnergy/MaxEnergy/BondOfLife，可加Target.前缀，当前=[{param1}]");
    }

    public static void ValidatePublicCountEffect(string tableName, int row, string effectType,
        string param1, string param2, string targetType)
    {
        bool appliesNightSoul = effectType == "ApplyStatus" && NightSoulStatusHandler.IsStatusReference(param1);
        if (effectType == "NightSoul" && !NightSoulEffect.IsValidAmount(param1))
            throw new TableValidationException($"[配表校验] {tableName} 第{row}行 (NightSoul): Param1必须为有符号int增减量，当前=[{param1}]");
        if ((effectType == "NightSoul" || appliesNightSoul) && !string.IsNullOrEmpty(targetType)
            && targetType.EndsWith("Field", System.StringComparison.Ordinal))
            throw new TableValidationException($"[配表校验] {tableName} 第{row}行: 公共夜魂只允许实体目标，不能施加到场地。");
        if (appliesNightSoul)
        {
            try { NightSoulStatusHandler.ParseMaximum(param2); }
            catch (System.FormatException exception)
            {
                throw new TableValidationException($"[配表校验] {tableName} 第{row}行: {exception.Message}");
            }
        }
    }

    public static void ValidateAPCost(string param1, string param2)
    {
        if (!int.TryParse(param1, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out _) && !APCostResolver.IsFreeValue(param1))
            throw new TableValidationException("APCost 的 Param1 必须为整数增减量或Free（强制免除该操作AP费用）");
        if (string.IsNullOrWhiteSpace(param2))
            throw new TableValidationException("APCost 的 Param2 必须填写适用操作");
        foreach (string operation in param2.Split(';'))
            if (!APCostResolver.IsOperation(operation.Trim()))
                throw new TableValidationException($"APCost 操作无效：[{operation}]；支持 Normal/Heavy/Skill/Burst/SwitchIn/SwitchOut，多个用分号分隔");
    }

    /// <summary>Enemy_Main 行校验。Poise 不能填0（-1=无法被削韧，正数=韧性值）。</summary>
    public static void ValidateEnemyMainRow(int row, int enemyID, float poise)
    {
        if (poise == 0f)
        {
            throw new TableValidationException($"[配表校验] Enemy_Main 第{row}行 (EnemyID={enemyID}): Poise不能为0（-1=无法被削韧）");
        }
    }

    /// <summary>Skills 行校验。MaxCharge=0合法（无此概念）；InitialCharge 无额外限制。</summary>
    public static void ValidateSkillRow(string tableName, int row, int skillID)
    {
        // 目前 Skills 表无硬性0限制（MaxCharge 0=无此概念，合法）
        // 预留扩展位：将来如有新规则在此添加
    }

    /// <summary>
    /// ChangeControl 专属校验（术语 L369-L375）：
    ///  - Param1 必填 ∈ {Normal,Heavy,Skill,Burst,All}
    ///  - Param2 必填（Freeze 或新技能 SkillID2）
    ///  - All 只能配 Freeze
    ///  - 替换型（Param2≠Freeze）Param3 必填 ∈ {0,1}（0=换上不重置冷却和次数，1=重置冷却并读InitialCharge为次数）
    ///  - 状态效果表：Duration 不填（跟状态 duration 走）；技能效果表：填了定时结束，没填=永久
    ///  - 敌人技能效果表禁止（敌人没有按键绑定）
    /// </summary>
    public static void ValidateChangeControl(string tableName, int row,
        string param1, string param2, string param3, int duration, bool isStatusEffectTable, bool isEnemy)
    {
        string tag = $"[配表校验] {tableName} 第{row}行 (ChangeControl)";

        if (isEnemy)
        {
            throw new TableValidationException($"{tag}: 敌人没有按键绑定，ChangeControl 禁止出现在敌人技能效果表");
        }

        if (string.IsNullOrEmpty(param1) ||
            (param1 != "Normal" && param1 != "Heavy" && param1 != "Skill" && param1 != "Burst" && param1 != "All"))
        {
            throw new TableValidationException($"{tag}: Param1 必填且只能为 Normal/Heavy/Skill/Burst/All，当前=[{param1}]");
        }

        if (string.IsNullOrEmpty(param2))
        {
            throw new TableValidationException($"{tag}: Param2 必填（Freeze 或新技能 SkillID2）");
        }

        if (param1 == "All" && param2 != "Freeze")
        {
            throw new TableValidationException($"{tag}: All 只能配 Freeze（不能替换技能）");
        }

        if (param2 != "Freeze" && param3 != "0" && param3 != "1")
        {
            throw new TableValidationException($"{tag}: 替换型（Param2≠Freeze）Param3 必填 0 或 1（0=换上不重置冷却和次数，1=重置冷却并读InitialCharge为次数），当前=[{param3}]");
        }

        if (isStatusEffectTable && duration != 0)
        {
            throw new TableValidationException($"{tag}: 状态效果表的 ChangeControl 不填 Duration（跟状态 duration 走），当前 Duration={duration}");
        }
    }
}
