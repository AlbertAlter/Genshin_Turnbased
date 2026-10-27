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
        LoadCharacterStatusData();
    }

    // 独立表已随原同步工具递归发布到Data/CharacterStatus；运行时不读取策划源目录。
    void LoadCharacterStatusData()
    {
        string directory = Path.Combine(ChartsPath, "CharacterStatus");
        if (!Directory.Exists(directory)) return; // 兼容只有旧合并表的数据包，不伪造角色归属。
        string[] files = Directory.GetFiles(directory, "*.xlsx");
        Array.Sort(files, StringComparer.Ordinal);
        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (name.StartsWith("#", StringComparison.Ordinal) || name.StartsWith("~$", StringComparison.Ordinal)
                || name.StartsWith("0_", StringComparison.Ordinal)) continue;
            int separator = name.IndexOf('_');
            if (separator <= 0 || !int.TryParse(name.Substring(0, separator), out int characterID) || characterID <= 0)
                throw new TableValidationException("角色状态工作簿名须以角色ID_开头：" + name);
            string source = "CharacterStatus/" + Path.GetFileName(file);
            try
            {
                using var package = new ExcelPackage(new FileInfo(file));
                LoadCharacterStatusWorkbook(package, characterID, source);
            }
            catch (Exception error) { throw new InvalidDataException($"[{source}] {error.Message}", error); }
        }
        LogManager.Log(LogCategory.Data, $"Character statuses loaded, {CharacterStatusIDs.Count} character tables");
    }

    void LoadCharacterStatusWorkbook(ExcelPackage package, int characterID, string source)
    {
        if (characterID <= 0) throw new ArgumentOutOfRangeException(nameof(characterID));
        foreach (string name in new[] { "StatusData_Main", "Status_Effect", "Status_Action", "Status_Attributes" })
            if (package.Workbook.Worksheets[name] == null)
                throw new TableValidationException(source + " 缺少工作表：" + name);
        LoadStatusMainSheet(package.Workbook.Worksheets["StatusData_Main"], characterID: characterID);
        LoadStatusEffectSheet(package.Workbook.Worksheets["Status_Effect"], source + "/Status_Effect", false);
        LoadStatusActionSheet(package.Workbook.Worksheets["Status_Action"], source + "/Status_Action");
        LoadStatusAttributesSheet(package.Workbook.Worksheets["Status_Attributes"]);
    }

    void RegisterCharacterStatus(int characterID, string id)
    {
        foreach (var entry in CharacterStatusIDs)
            if (entry.Value.Contains(id))
                throw new TableValidationException($"角色状态ID重复：{id} 已属于{entry.Key}，不能重复登记到{characterID}");
        if (!CharacterStatusIDs.TryGetValue(characterID, out var ids))
            CharacterStatusIDs.Add(characterID, ids = new HashSet<string>(StringComparer.Ordinal));
        ids.Add(id);
        // 独立正式表覆盖旧合并表的同ID定义，同时撤掉旧行动/效果，不能叠成两套触发。
        if (StatusMainDict.TryGetValue(id, out var previous))
        {
            var stale = new List<string>();
            foreach (var effect in StatusEffectDict)
                if (effect.Value.StatusEffectID / 100 == previous.StatusID) stale.Add(effect.Key);
            foreach (string key in stale) StatusEffectDict.Remove(key);
            StatusActionDict.Remove(id);
            StatusAttributesDict.Remove(id);
        }
    }

    void LoadEnemyStatusData()
    {
        LoadEnemyStatusPackages();
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

    void LoadStatusMainSheet(ExcelWorksheet sheet, Func<int, string> keyForID = null, int characterID = 0)
    {
        if (sheet == null) return;
        for (int row = GetDataStartRow(sheet); row <= GetSheetMaxRow(sheet); row++)
        {
            string id2 = sheet.Cells[row, FindColumnByHeader(sheet, "StatusID2")].Text;
            if (keyForID != null) id2 = id2.Trim();
            if (string.IsNullOrEmpty(id2) && string.IsNullOrEmpty(sheet.Cells[row, FindColumnByHeader(sheet, "StatusID")].Text)) continue;
            if (string.IsNullOrWhiteSpace(id2) && keyForID != null)
            {
                int id = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "StatusID")].Value);
                if (id > 0) id2 = keyForID(id);
            }
            if (string.IsNullOrEmpty(id2)) continue;

            if (characterID > 0) RegisterCharacterStatus(characterID, id2);

            if (keyForID != null && StatusMainDict.ContainsKey(id2))
                throw new InvalidDataException($"[{sheet.Name}] row {row}: duplicate runtime key {id2}");

            StatusMainDict[id2] = new StatusMainData
            {
                StatusID = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "StatusID")].Value),
                StatusID2 = id2,
                StatusName = sheet.Cells[row, FindColumnByHeader(sheet, "StatusName")].Text,
                StatusType = sheet.Cells[row, FindColumnByHeader(sheet, "StatusType")].Text,
                Display = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "Display")].Value),
                Description = sheet.Cells[row, FindColumnByHeader(sheet, "Description")].Text,
                MultiplierPart1 = sheet.Cells[row, FindColumnByHeader(sheet, "MultiplierPart1")].Text,
                MultiplierPart2 = sheet.Cells[row, FindColumnByHeader(sheet, "MultiplierPart2")].Text,
                MultiplierPart3 = sheet.Cells[row, FindColumnByHeader(sheet, "MultiplierPart3")].Text,
                ApplyDamageType = sheet.Cells[row, FindColumnByHeader(sheet, "ApplyDamageType")].Text,
                ApplyReactionType = sheet.Cells[row, FindColumnByHeader(sheet, "ApplyReactionType")].Text,
                ApplyElementType = sheet.Cells[row, FindColumnByHeader(sheet, "ApplyElementType")].Text,
                ApplyString = sheet.Cells[row, FindColumnByHeader(sheet, "ApplyString")].Text,
                ScriptHook = sheet.Cells[row, FindColumnByHeader(sheet, "ScriptHook")].Text,
                MaxCount = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "MaxCount")].Value),
                MaxStack = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "MaxStack")].Value),
                WhenMax = sheet.Cells[row, FindColumnByHeader(sheet, "WhenMax")].Text
            };
        }
    }

    void LoadStatusEffectSheet(ExcelWorksheet sheet, string sourceKey, bool isEnemy,
        Func<int, string> keyForID = null, Func<string, string> resolveStatusKey = null)
    {
        if (sheet == null) return;
        for (int row = GetDataStartRow(sheet); row <= GetSheetMaxRow(sheet); row++)
        {
            string id2 = sheet.Cells[row, FindColumnByHeader(sheet, "StatusEffectID2")].Text;
            if (keyForID != null) id2 = id2.Trim();
            if (string.IsNullOrEmpty(id2) && string.IsNullOrEmpty(sheet.Cells[row, FindColumnByHeader(sheet, "StatusEffectID")].Text)) continue;
            if (string.IsNullOrWhiteSpace(id2) && keyForID != null)
            {
                int id = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "StatusEffectID")].Value);
                if (id > 0) id2 = keyForID(id);
            }
            if (string.IsNullOrEmpty(id2)) continue;

            if (keyForID != null && StatusEffectDict.ContainsKey(id2))
                throw new InvalidDataException($"[{sourceKey}] row {row}: duplicate runtime key {id2}");

            var data = new StatusEffectData
            {
                AuthoredFields = ReadAuthoredEffectFields(sheet, row),
                StatusEffectID = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "StatusEffectID")].Value),
                StatusEffectID2 = id2,
                EffectIndex = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "EffectIndex")].Value),
                EffectType = sheet.Cells[row, FindColumnByHeader(sheet, "EffectType")].Text,
                Element = sheet.Cells[row, FindColumnByHeader(sheet, "Element")].Text,
                DamageType = sheet.Cells[row, FindColumnByHeader(sheet, "DamageType")].Text,
                Duration = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "Duration")].Value),
                AddInPhase = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "AddInPhase")].Value),
                TriggerPhase = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "TriggerPhase")].Value),
                Param1 = sheet.Cells[row, FindColumnByHeader(sheet, "Param1")].Text,
                Param2 = ReadEffectParam2(sheet, row, sheet.Cells[row, FindColumnByHeader(sheet, "EffectType")].Text),
                Param3 = sheet.Cells[row, FindColumnByHeader(sheet, "Param3")].Text,
                TargetType = sheet.Cells[row, FindColumnByHeader(sheet, "TargetType")].Text,
                // TargetSelect原样保留范围语法与纯数字目标编号，由统一目标解析器解释。
                TargetSelect = sheet.Cells[row, FindColumnByHeader(sheet, "TargetSelect")].Text.Trim(),
                TargetConsecutive = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "TargetConsecutive")].Value),
                TargetOverride = sheet.Cells[row, FindColumnByHeader(sheet, "TargetOverride")].Text,
                ScriptHook = sheet.Cells[row, FindColumnByHeader(sheet, "ScriptHook")].Text,
                DamageHook = ReadOptionalEffectText(sheet, row, "DamageHook"),
                ExecuteCount = ReadExecuteCount(sheet, row),
                ExecuteCountHandler = ReadExecuteCountHandler(sheet, row)
            };

            if (resolveStatusKey != null && (data.EffectType == "ApplyStatus"
                || data.EffectType == "BindStatus" || data.EffectType == "RemoveStatus"
                || data.EffectType == "AddTurn"))
                data.Param1 = ResolveArtifactReferenceList(data.Param1, resolveStatusKey);

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
                checkTargetNumber: false, targetSelect: data.TargetSelect, isStatusEffectTable: true));
            TryValidate(sourceKey, () =>
            {
                TableValidator.ValidateDamageProperties(sourceKey, row,
                    data.EffectType, data.Element, data.Param1, data.Param2, data.Param3, data.DamageHook);
                if (data.EffectType == "Damage") data.Element = DamageProperties.NormalizeElement(data.Element);
            });
            if (data.EffectType == "ApplyElement")
                TryValidate(sourceKey, () => TableValidator.ValidateApplyElement(sourceKey, row,
                    data.Element, data.Param1, data.Param2, data.Param3, data.DamageType, data.DamageHook));
            if (data.EffectType == "APCost")
                TryValidate(sourceKey, () => TableValidator.ValidateAPCost(data.Param1, data.Param2));
            if (data.EffectType == "SwitchIn")
                TryValidate(sourceKey, () => TableValidator.ValidateSwitchIn(sourceKey, row,
                    data.Param1, data.Param3, data.DamageType, data.DamageHook, data.AddInPhase, data.TriggerPhase));
            if (data.EffectType == "ResetCooldown")
                TryValidate(sourceKey, () => TableValidator.ValidateResetCooldown(sourceKey, row, data.Param1));
            if (data.EffectType == "ReduceCooldown")
                TryValidate(sourceKey, () => TableValidator.ValidateReduceCooldown(sourceKey, row, data.Param1, data.Param2));
            if (data.EffectType == "BondOfLife")
                TryValidate(sourceKey, () => TableValidator.ValidateBondOfLife(sourceKey, row, data.Param1));
            if (data.EffectType == "Revive")
                TryValidate(sourceKey, () => TableValidator.ValidateRevive(sourceKey, row,
                    data.Param1, data.Param2, data.Param3, data.Element, data.DamageType, data.DamageHook, data.Duration, data.TargetType));
            TryValidate(sourceKey, () => TableValidator.ValidatePublicCountEffect(
                sourceKey, row, data.EffectType, data.Param1, data.Param2, data.TargetType));
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

            if (data.EffectType == "SwitchPattern")
                TryValidate(sourceKey, () => TableValidator.ValidateSwitchPattern(sourceKey, row,
                    data.Param1, data.Param2, data.Param3, data.Element, data.DamageType, data.DamageHook,
                    data.Duration, data.AddInPhase, data.TriggerPhase));

            StatusEffectDict[id2] = data;
        }
    }

    void LoadStatusActionSheet(ExcelWorksheet sheet, string sourceKey)
    {
        if (sheet == null) return;
        for (int row = GetDataStartRow(sheet); row <= GetSheetMaxRow(sheet); row++)
        {
            string id2 = sheet.Cells[row, FindColumnByHeader(sheet, "StatusID2")].Text;
            if (string.IsNullOrEmpty(id2) && string.IsNullOrEmpty(sheet.Cells[row, FindColumnByHeader(sheet, "StatusID")].Text)) continue;
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
            string id2 = sheet.Cells[row, FindColumnByHeader(sheet, "StatusID2")].Text;
            if (string.IsNullOrEmpty(id2) && string.IsNullOrEmpty(sheet.Cells[row, FindColumnByHeader(sheet, "StatusID")].Text)) continue;
            if (string.IsNullOrEmpty(id2)) continue;

            StatusAttributesDict[id2] = new StatusAttributesData
            {
                StatusID = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "StatusID")].Value),
                StatusID2 = id2,
                TotalHP = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "TotalHP")].Value),
                TotalATK = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "TotalATK")].Value),
                TotalDEF = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "TotalDEF")].Value),
                TotalEM = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "TotalEM")].Value),
                TotalRechargeRate = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "TotalRechargeRate")].Value),
                CritRate = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "CritRate")].Value),
                CritDMG = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "CritDMG")].Value),
                BaseDMGBonusFlat = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "BaseDMGBonusFlat")].Value),
                DMGBonus = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "DMGBonus")].Value),
                DEFReduction = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "DEFReduction")].Value),
                DEFIgnored = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "DEFIgnored")].Value),
                ResBonus = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "ResBonus")].Value),
                ShieldStrength = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "ShieldStrength")].Value),
                HealBonus = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "HealBonus")].Value),
                BeHealedBonus = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "BeHealedBonus")].Value),
                Elevation = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "Elevation")].Value),
                BaseDMGBonus = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "BaseDMGBonus")].Value),
                LunarDMGBonus = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "LunarDMGBonus")].Value),
                StellarDMGBonus = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "StellarDMGBonus")].Value)
            };
        }
    }

    // 按表头共用行动解析；Weapon没有StatusID2列，运行时键由武器读取入口生成。
    StatusActionData ReadStatusAction(
        ExcelWorksheet sheet,
        int row,
        bool hasStatusID2,
        string sourceKey)
    {
        var data = new StatusActionData
        {
            StatusID = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "StatusID")].Value),
            StatusID2 = hasStatusID2 ? sheet.Cells[row, FindColumnByHeader(sheet, "StatusID2")].Text : string.Empty,
            ActionType = sheet.Cells[row, FindColumnByHeader(sheet, "ActionType")].Text,
            Param1 = sheet.Cells[row, FindColumnByHeader(sheet, "Param1")].Text,
            Param2 = sheet.Cells[row, FindColumnByHeader(sheet, "Param2")].Text,
            Param3 = sheet.Cells[row, FindColumnByHeader(sheet, "Param3")].Text,
            ScriptHook = sheet.Cells[row, FindColumnByHeader(sheet, "ScriptHook")].Text,
            OnAction = sheet.Cells[row, FindColumnByHeader(sheet, "OnAction")].Text,
            OutgoingHit = sheet.Cells[row, FindColumnByHeader(sheet, "OutgoingHit")].Text,
            OutgoingDamage = sheet.Cells[row, FindColumnByHeader(sheet, "OutgoingDamage")].Text,
            OnHit = sheet.Cells[row, FindColumnByHeader(sheet, "OnHit")].Text,
            OnDamage = sheet.Cells[row, FindColumnByHeader(sheet, "OnDamage")].Text,
            OnHealFrom = sheet.Cells[row, FindColumnByHeader(sheet, "OnHealFrom")].Text,
            WhenHealing = sheet.Cells[row, FindColumnByHeader(sheet, "WhenHealing")].Text,
            APUsed = sheet.Cells[row, FindColumnByHeader(sheet, "APUsed")].Text,
            ReactionTriggered = sheet.Cells[row, FindColumnByHeader(sheet, "ReactionTriggered")].Text,
            Cooldown = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "Cooldown")].Value),
            ExecuteCount = ReadExecuteCount(sheet, row),
            ExecuteCountHandler = ReadExecuteCountHandler(sheet, row),
            ActionKey = $"{sourceKey}:{row}"
        };
        ReadStatusTriggerLimits(sheet, row, data);
        return data;
    }

    void ReadStatusTriggerLimits(ExcelWorksheet sheet, int row, StatusActionData data)
    {
        var headers = ExcelTableHeaders.For(sheet);
        int scopeColumn = headers.Optional("MaxTimePer");
        int countColumn = headers.Optional("MaxTime");
        if (scopeColumn > 0 || countColumn > 0)
        {
            if (scopeColumn < 1 || countColumn < 1)
                throw new TableValidationException($"[{sheet.Name}] MaxTimePer / MaxTime 必须同时存在");
            try { data.SetTriggerLimits(sheet.Cells[row, scopeColumn].Text, sheet.Cells[row, countColumn].Text); }
            catch (TableValidationException error)
            { throw new TableValidationException($"[{sheet.Name}] 第{row}行：{error.Message}"); }
            // 新两列为准，不能在留空时重新叠加旧列中的限制。
            return;
        }
        // 旧工作簿仍可读取，现有运行时测试构造也保留兼容；新策划表不再新增这些列。
        data.MaxTimePerHit = ReadOptionalStatusLimit(sheet, row, "MaxTimePerHit");
        data.MaxTimePerTurn = ReadOptionalStatusLimit(sheet, row, "MaxTimePerTurn");
        data.MaxTimePerLife = ReadOptionalStatusLimit(sheet, row, "MaxTimePerLife");
        data.MaxTimePerEffect = ReadOptionalStatusLimit(sheet, row, "MaxTimePerEffect");
        data.MaxTimePerAction = ReadOptionalStatusLimit(sheet, row, "MaxTimePerAction");
    }
    // Missing columns, empty values and non-positive values mean unlimited.
    int ReadOptionalStatusLimit(ExcelWorksheet sheet, int row, string header)
    {
        int column = ExcelTableHeaders.For(sheet).Optional(header);
        return column > 0 ? Math.Max(0, SafeGetInt(sheet.Cells[row, column].Value)) : 0;
    }
}
