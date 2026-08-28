using System;
using System.Collections.Generic;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

public partial class DataManager
{
    // 角色、敌人、全局三份 Status 工作簿结构一致，只允许 Sheet 名不同。
    void LoadStatusData()
    {
        LoadStatusWorkbook(
            "StatusData.xlsx",
            "StatusData_Main",
            "Status_Action",
            "Status_Effect",
            "Status_Attributes",
            false,
            "StatusData");
    }

    void LoadEnemyStatusData()
    {
        LoadStatusWorkbook(
            "StatusData_Enemy.xlsx",
            "StatusData_Main_Enemy",
            "Status_Action_Enemy",
            "Status_Effect",
            "Status_Attributes",
            true,
            "EnemyStatusData");
    }

    void LoadOverallStatusData()
    {
        LoadStatusWorkbook(
            "StatusData_Overall.xlsx",
            "StatusData_Overall_Main",
            "StatusAction_Overall",
            "StatusEffect_Overall",
            "StatusAttributes_Overall",
            false,
            "OverallStatusData");
    }

    void LoadStatusWorkbook(
        string fileName,
        string mainSheetName,
        string actionSheetName,
        string effectSheetName,
        string attributesSheetName,
        bool isEnemy,
        string logName)
    {
        string path = Path.Combine(ChartsPath, fileName);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"{fileName} not found");
            return;
        }

        using var pkg = new ExcelPackage(new FileInfo(path));
        LoadStatusMainSheet(pkg.Workbook.Worksheets[mainSheetName]);
        LoadStatusEffectSheet(
            pkg.Workbook.Worksheets[effectSheetName],
            $"{fileName}/{effectSheetName}",
            isEnemy);
        LoadStatusActionSheet(
            pkg.Workbook.Worksheets[actionSheetName],
            $"{fileName}/{actionSheetName}");
        LoadStatusAttributesSheet(pkg.Workbook.Worksheets[attributesSheetName]);

        LogManager.Log(LogCategory.Data, $"{logName} loaded, {StatusMainDict.Count} statuses");
    }

    void LoadStatusMainSheet(ExcelWorksheet sheet)
    {
        if (sheet == null) return;
        for (int row = GetDataStartRow(sheet); row <= GetSheetMaxRow(sheet); row++)
        {
            string id2 = sheet.Cells[row, 2].Text;
            if (string.IsNullOrEmpty(id2) && string.IsNullOrEmpty(sheet.Cells[row, 1].Text)) continue;
            if (string.IsNullOrEmpty(id2)) continue;

            StatusMainDict[id2] = new StatusMainData
            {
                StatusID = SafeGetInt(sheet.Cells[row, 1].Value),
                StatusID2 = id2,
                StatusName = sheet.Cells[row, 3].Text,
                StatusType = sheet.Cells[row, 4].Text,
                Display = SafeGetInt(sheet.Cells[row, 5].Value),
                Description = sheet.Cells[row, 6].Text,
                MultiplierPart1 = sheet.Cells[row, 7].Text,
                MultiplierPart2 = sheet.Cells[row, 8].Text,
                MultiplierPart3 = sheet.Cells[row, 9].Text,
                ApplyDamageType = sheet.Cells[row, 10].Text,
                ApplyReactionType = sheet.Cells[row, 11].Text,
                ApplyElementType = sheet.Cells[row, 12].Text,
                ApplyString = sheet.Cells[row, 13].Text,
                ScriptHook = sheet.Cells[row, 14].Text,
                MaxCount = SafeGetInt(sheet.Cells[row, 15].Value),
                MaxStack = SafeGetInt(sheet.Cells[row, 16].Value),
                WhenMax = sheet.Cells[row, 17].Text
            };
        }
    }

    void LoadStatusEffectSheet(ExcelWorksheet sheet, string sourceKey, bool isEnemy)
    {
        if (sheet == null) return;
        for (int row = GetDataStartRow(sheet); row <= GetSheetMaxRow(sheet); row++)
        {
            string id2 = sheet.Cells[row, 2].Text;
            if (string.IsNullOrEmpty(id2) && string.IsNullOrEmpty(sheet.Cells[row, 1].Text)) continue;
            if (string.IsNullOrEmpty(id2)) continue;

            var data = new StatusEffectData
            {
                StatusEffectID = SafeGetInt(sheet.Cells[row, 1].Value),
                StatusEffectID2 = id2,
                EffectIndex = SafeGetInt(sheet.Cells[row, 3].Value),
                EffectType = sheet.Cells[row, 4].Text,
                Element = sheet.Cells[row, 5].Text,
                DamageType = sheet.Cells[row, 6].Text,
                Duration = SafeGetInt(sheet.Cells[row, 7].Value),
                AddInPhase = SafeGetInt(sheet.Cells[row, 8].Value),
                TriggerPhase = SafeGetInt(sheet.Cells[row, 9].Value),
                Param1 = sheet.Cells[row, 10].Text,
                Param2 = sheet.Cells[row, 11].Text,
                Param3 = sheet.Cells[row, 12].Text,
                TargetType = sheet.Cells[row, 13].Text,
                // 第14列原样保留范围语法与纯数字目标编号，由统一目标解析器解释。
                TargetSelect = sheet.Cells[row, 14].Text.Trim(),
                TargetConsecutive = SafeGetInt(sheet.Cells[row, 15].Value),
                TargetOverride = sheet.Cells[row, 16].Text,
                ScriptHook = sheet.Cells[row, 17].Text
            };

            TryValidate(sourceKey, () => TableValidator.ValidateEffectRow(
                sourceKey,
                row,
                data.EffectType,
                data.Element,
                data.Duration,
                data.Param2,
                data.TargetType,
                0,
                data.TargetConsecutive,
                data.TargetOverride,
                isEnemy,
                checkTargetNumber: false));
            if (data.EffectType == "ChangeControl")
            {
                TryValidate(sourceKey, () => TableValidator.ValidateChangeControl(
                    sourceKey,
                    row,
                    data.Param1,
                    data.Param2,
                    data.Param3,
                    data.Duration,
                    isStatusEffectTable: true,
                    isEnemy: isEnemy));
            }

            StatusEffectDict[id2] = data;
        }
    }

    void LoadStatusActionSheet(ExcelWorksheet sheet, string sourceKey)
    {
        if (sheet == null) return;
        for (int row = GetDataStartRow(sheet); row <= GetSheetMaxRow(sheet); row++)
        {
            string id2 = sheet.Cells[row, 2].Text;
            if (string.IsNullOrEmpty(id2) && string.IsNullOrEmpty(sheet.Cells[row, 1].Text)) continue;
            if (string.IsNullOrEmpty(id2)) continue;

            StatusActionData data = ReadStatusAction(sheet, row, true, sourceKey);
            if (!StatusActionDict.TryGetValue(id2, out List<StatusActionData> actions))
                StatusActionDict[id2] = actions = new List<StatusActionData>();
            actions.Add(data);
        }
    }

    void LoadStatusAttributesSheet(ExcelWorksheet sheet)
    {
        if (sheet == null) return;
        for (int row = GetDataStartRow(sheet); row <= GetSheetMaxRow(sheet); row++)
        {
            string id2 = sheet.Cells[row, 2].Text;
            if (string.IsNullOrEmpty(id2) && string.IsNullOrEmpty(sheet.Cells[row, 1].Text)) continue;
            if (string.IsNullOrEmpty(id2)) continue;

            StatusAttributesDict[id2] = new StatusAttributesData
            {
                StatusID = SafeGetInt(sheet.Cells[row, 1].Value),
                StatusID2 = id2,
                TotalHP = SafeGetInt(sheet.Cells[row, 3].Value),
                TotalATK = SafeGetInt(sheet.Cells[row, 4].Value),
                TotalDEF = SafeGetInt(sheet.Cells[row, 5].Value),
                TotalEM = SafeGetInt(sheet.Cells[row, 6].Value),
                TotalRechargeRate = SafeGetInt(sheet.Cells[row, 7].Value),
                CritRate = SafeGetInt(sheet.Cells[row, 8].Value),
                CritDMG = SafeGetInt(sheet.Cells[row, 9].Value),
                BaseDMGBonusFlat = SafeGetInt(sheet.Cells[row, 10].Value),
                DMGBonus = SafeGetInt(sheet.Cells[row, 11].Value),
                DEFReduction = SafeGetInt(sheet.Cells[row, 12].Value),
                DEFIgnored = SafeGetInt(sheet.Cells[row, 13].Value),
                ResBonus = SafeGetInt(sheet.Cells[row, 14].Value),
                ShieldStrength = SafeGetInt(sheet.Cells[row, 15].Value),
                HealBonus = SafeGetInt(sheet.Cells[row, 16].Value),
                BeHealedBonus = SafeGetInt(sheet.Cells[row, 17].Value),
                Elevation = SafeGetInt(sheet.Cells[row, 18].Value),
                BaseDMGBonus = SafeGetInt(sheet.Cells[row, 19].Value),
                LunarDMGBonus = SafeGetInt(sheet.Cells[row, 20].Value),
                StellarDMGBonus = SafeGetInt(sheet.Cells[row, 21].Value)
            };
        }
    }

    // Character/Enemy/Overall 均为 19 列；Weapon 缺 StatusID2，使用 offset=0 复用同一行解析。
    StatusActionData ReadStatusAction(
        ExcelWorksheet sheet,
        int row,
        bool hasStatusID2,
        string sourceKey)
    {
        int offset = hasStatusID2 ? 1 : 0;
        return new StatusActionData
        {
            StatusID = SafeGetInt(sheet.Cells[row, 1].Value),
            StatusID2 = hasStatusID2 ? sheet.Cells[row, 2].Text : string.Empty,
            ActionType = sheet.Cells[row, 2 + offset].Text,
            Param1 = sheet.Cells[row, 3 + offset].Text,
            Param2 = sheet.Cells[row, 4 + offset].Text,
            Param3 = sheet.Cells[row, 5 + offset].Text,
            ScriptHook = sheet.Cells[row, 6 + offset].Text,
            OnAction = sheet.Cells[row, 7 + offset].Text,
            OutgoingHit = sheet.Cells[row, 8 + offset].Text,
            OutgoingDamage = sheet.Cells[row, 9 + offset].Text,
            OnHit = sheet.Cells[row, 10 + offset].Text,
            OnDamage = sheet.Cells[row, 11 + offset].Text,
            OnHealFrom = sheet.Cells[row, 12 + offset].Text,
            WhenHealing = sheet.Cells[row, 13 + offset].Text,
            APUsed = sheet.Cells[row, 14 + offset].Text,
            ReactionTriggered = sheet.Cells[row, 15 + offset].Text,
            MaxTimePerTurn = SafeGetInt(sheet.Cells[row, 16 + offset].Value),
            MaxTimePerLife = SafeGetInt(sheet.Cells[row, 17 + offset].Value),
            Cooldown = SafeGetInt(sheet.Cells[row, 18 + offset].Value),
            ActionKey = $"{sourceKey}:{row}"
        };
    }
}
