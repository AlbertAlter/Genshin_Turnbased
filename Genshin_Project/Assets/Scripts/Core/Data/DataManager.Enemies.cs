using System;
using System.Collections.Generic;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

public partial class DataManager
{
    // ================================================================
    //  EnemyAttributes.xlsx/Coeff：生命、攻击系数及攻击曲线选择的唯一来源
    // ================================================================
    void LoadEnemyAttributes()
    {
        string path = Path.Combine(ChartsPath, "EnemyAttributes.xlsx");
        if (!File.Exists(path)) { Debug.LogWarning("EnemyAttributes.xlsx not found"); return; }
        using var pkg = new ExcelPackage(new FileInfo(path));
        var sheet = pkg.Workbook.Worksheets["Coeff"];
        if (sheet == null) throw new TableValidationException("EnemyAttributes.xlsx缺少Coeff系数表。");
        for (int row = GetDataStartRow(sheet); row <= GetSheetMaxRow(sheet); row++)
        {
            int id = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "EnemyID")].Value);
            if (id <= 0) continue;
            if (EnemyMainDict.ContainsKey(id)) throw new TableValidationException("Coeff重复EnemyID：" + id);
            EnemyMainDict.Add(id, new EnemyMainData {
                EnemyID = id,
                EnemyName = sheet.Cells[row, FindColumnByHeader(sheet, "中文名")].Text,
                EnemyNameID = sheet.Cells[row, FindColumnByHeader(sheet, "English Name")].Text,
                HP_coeff = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "HP_coeff")].Value),
                ATK_coeff = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "ATK_coeff")].Value),
                ATK_Curve = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "ATK_Curve")].Value)
            });
        }
        LoadEnemyIdentityTypes();
    }

    // 技能分表仅补充标识、行动贡献、抗性与增伤，保留Coeff已加载的生命/攻击参数。
    void LoadEnemyAttributeSheet(ExcelWorksheet sheet)
    {
        int startRow = GetDataStartRow(sheet);
        int maxRow = GetSheetMaxRow(sheet);
        for (int row = startRow; row <= maxRow; row++)
        {
            string cell = sheet.Cells[row, FindColumnByHeader(sheet, "EnemyID")].Text;
            if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(sheet.Cells[row, FindColumnByHeader(sheet, "EnemyNameID")].Text)) continue;
            int enemyID = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "EnemyID")].Value);
            if (enemyID == 0) continue;
            if (!EnemyMainDict.TryGetValue(enemyID, out var coefficient))
                throw new TableValidationException($"敌人{enemyID}未在EnemyAttributes.xlsx/Coeff配置属性系数。");
            var data = new EnemyMainData
            {
                EnemyID = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "EnemyID")].Value),
                EnemyNameID = sheet.Cells[row, FindColumnByHeader(sheet, "EnemyNameID")].Text,
                EnemyName = sheet.Cells[row, FindColumnByHeader(sheet, "EnemyName")].Text,
                Threat = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "Threat")].Value),
                ActsGiven = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "ActsGiven")].Value),
                ATK_Curve = coefficient.ATK_Curve,
                HP_coeff = coefficient.HP_coeff,
                ATK_coeff = coefficient.ATK_coeff,
                PhysicalRes = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "PhysicalRes")].Value),
                PyroRes = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "PyroRes")].Value),
                HydroRes = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "HydroRes")].Value),
                ElectroRes = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "ElectroRes")].Value),
                CryoRes = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "CryoRes")].Value),
                AnemoRes = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "AnemoRes")].Value),
                DendroRes = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "DendroRes")].Value),
                GeoRes = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "GeoRes")].Value),
                PhysicalDmgBonus = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "PhysicalDmgBonus")].Value),
                PyroDmgBonus = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "PyroDmgBonus")].Value),
                HydroDmgBonus = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "HydroDmgBonus")].Value),
                ElectroDmgBonus = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "ElectroDmgBonus")].Value),
                CryoDmgBonus = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "CryoDmgBonus")].Value),
                AnemoDmgBonus = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "AnemoDmgBonus")].Value),
                DendroDmgBonus = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "DendroDmgBonus")].Value),
                GeoDmgBonus = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "GeoDmgBonus")].Value)
            };
            if (data.EnemyID == 0) continue;
            EnemyMainDict[data.EnemyID] = data;
        }
    }

    // 敌人身份来自正式Enemy.xlsx，不凭显示名、韧性、Threat或旧属性表顺序推断等级类别。
    void LoadEnemyIdentityTypes()
    {
        string path = Path.Combine(ChartsPath, "Enemy.xlsx");
        if (!File.Exists(path)) return; // 保留未知；需要此信息的技能须明确拒绝缺失分类。
        using var package = new ExcelPackage(new FileInfo(path));
        var sheet = package.Workbook.Worksheets[0];
        if (sheet == null) return;
        var headers = ExcelTableHeaders.For(sheet);
        int idColumn = headers.Optional("EnemyID");
        if (idColumn < 1) idColumn = headers.Optional("顺序ID");
        int typeColumn = headers.Optional("EnemyType");
        if (idColumn < 1 || typeColumn < 1)
            throw new TableValidationException("Enemy.xlsx缺少EnemyID/顺序ID或EnemyType表头。");
        var seen = new HashSet<int>();
        for (int row = GetDataStartRow(sheet); row <= GetSheetMaxRow(sheet); row++)
        {
            int id = SafeGetInt(sheet.Cells[row, idColumn].Value);
            if (id <= 0) continue;
            if (!seen.Add(id)) throw new TableValidationException($"Enemy.xlsx第{row}行重复敌人ID：{id}");
            if (EnemyMainDict.TryGetValue(id, out var enemy))
                enemy.EnemyType = sheet.Cells[row, typeColumn].Text.Trim();
        }
    }

    // ================================================================
    //  EnemyAttributes.xlsx 的 Base_HP / Base_ATK1 / Base_ATK2 曲线
    // ================================================================
    void LoadEnemyCurves()
    {
        const string filename = "EnemyAttributes.xlsx"; // 保留既有Base_HP / Base_ATK1 / Base_ATK2曲线。
        string path = Path.Combine(ChartsPath, filename);
        if (!File.Exists(path)) { Debug.LogWarning(filename + " not found"); return; }
        using var pkg = new ExcelPackage(new FileInfo(path));

        var hpSheet = pkg.Workbook.Worksheets["Base_HP"];
        if (hpSheet != null)
        {
            int startRow = GetDataStartRow(hpSheet);
            int maxRow = GetSheetMaxRow(hpSheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = hpSheet.Cells[row, FindColumnByHeader(hpSheet, "lv")].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(hpSheet.Cells[row, FindColumnByHeader(hpSheet, "hp")].Text)) continue;
                BaseHPDict[SafeGetInt(hpSheet.Cells[row, FindColumnByHeader(hpSheet, "lv")].Value)] = SafeGetFloat(hpSheet.Cells[row, FindColumnByHeader(hpSheet, "hp")].Value);
            }
        }

        var atk1Sheet = pkg.Workbook.Worksheets["Base_ATK1"];
        if (atk1Sheet != null)
        {
            int startRow = GetDataStartRow(atk1Sheet);
            int maxRow = GetSheetMaxRow(atk1Sheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = atk1Sheet.Cells[row, FindColumnByHeader(atk1Sheet, "lv")].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(atk1Sheet.Cells[row, FindColumnByHeader(atk1Sheet, "atk1")].Text)) continue;
                BaseATK1Dict[SafeGetInt(atk1Sheet.Cells[row, FindColumnByHeader(atk1Sheet, "lv")].Value)] = SafeGetFloat(atk1Sheet.Cells[row, FindColumnByHeader(atk1Sheet, "atk1")].Value);
            }
        }

        var atk2Sheet = pkg.Workbook.Worksheets["Base_ATK2"];
        if (atk2Sheet != null)
        {
            int startRow = GetDataStartRow(atk2Sheet);
            int maxRow = GetSheetMaxRow(atk2Sheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = atk2Sheet.Cells[row, FindColumnByHeader(atk2Sheet, "lv")].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(atk2Sheet.Cells[row, FindColumnByHeader(atk2Sheet, "atk2")].Text)) continue;
                BaseATK2Dict[SafeGetInt(atk2Sheet.Cells[row, FindColumnByHeader(atk2Sheet, "lv")].Value)] = SafeGetFloat(atk2Sheet.Cells[row, FindColumnByHeader(atk2Sheet, "atk2")].Value);
            }
        }
    }

    // 查询敌人基础生命（等级无数据返回0）
    public float GetEnemyBaseHP(int level)
    {
        return BaseHPDict.TryGetValue(level, out float v) ? v : 0f;
    }

    // 查询敌人基础攻击（按 Enemy_Main.ATK_Curve 选择曲线1或2；等级无数据返回0）
    public float GetEnemyBaseATK(int curveID, int level)
    {
        if (curveID == 2)
            return BaseATK2Dict.TryGetValue(level, out float v2) ? v2 : 0f;
        return BaseATK1Dict.TryGetValue(level, out float v1) ? v1 : 0f;
    }

    // ================================================================
    //  Enemies分组工作簿的技能、效果、参数与AI
    // ================================================================
    void LoadEnemySkillWorkbook(ExcelPackage pkg)
    {
        // EnemySkill_Main
        var mainSheet = pkg.Workbook.Worksheets["EnemySkill_Main"];
        if (mainSheet != null)
        {
            int startRow = GetDataStartRow(mainSheet);
            int maxRow = GetSheetMaxRow(mainSheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = mainSheet.Cells[row, FindColumnByHeader(mainSheet, "EnemySkillID")].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(mainSheet.Cells[row, FindColumnByHeader(mainSheet, "EnemySkillID2")].Text)) continue;
                var data = new EnemySkillMainData
                {
                    EnemySkillID = SafeGetInt(mainSheet.Cells[row, FindColumnByHeader(mainSheet, "EnemySkillID")].Value),
                    EnemySkillID2 = mainSheet.Cells[row, FindColumnByHeader(mainSheet, "EnemySkillID2")].Text,
                    EnemySkillName = mainSheet.Cells[row, FindColumnByHeader(mainSheet, "EnemySkillName")].Text,
                    Cooldown = SafeGetFloat(mainSheet.Cells[row, FindColumnByHeader(mainSheet, "Cooldown")].Value),
                    UsePerTurn = SafeGetInt(mainSheet.Cells[row, FindColumnByHeader(mainSheet, "UsePerTurn")].Value),
                    HighThreat = SafeGetInt(mainSheet.Cells[row, FindColumnByHeader(mainSheet, "HighThreat")].Value),
                    Description = mainSheet.Cells[row, FindColumnByHeader(mainSheet, "Description")].Text
                };
                if (data.EnemySkillID == 0) continue;
                EnemySkillMainDict[data.EnemySkillID] = data;
            }
        }

        // EnemySkill_Effect
        var effSheet = pkg.Workbook.Worksheets["EnemySkill_Effect"];
        if (effSheet != null)
        {
            int startRow = GetDataStartRow(effSheet);
            int maxRow = GetSheetMaxRow(effSheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = effSheet.Cells[row, FindColumnByHeader(effSheet, "SkillEffectID2")].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(effSheet.Cells[row, FindColumnByHeader(effSheet, "SkillEffectID")].Text)) continue;
                var data = new EnemySkillEffectData
                {
                    SkillEffectID = SafeGetInt(effSheet.Cells[row, FindColumnByHeader(effSheet, "SkillEffectID")].Value),
                    SkillEffectID2 = cell,
                    EffectIndex = SafeGetInt(effSheet.Cells[row, FindColumnByHeader(effSheet, "EffectIndex")].Value),
                    EffectType = effSheet.Cells[row, FindColumnByHeader(effSheet, "EffectType")].Text,
                    Element = effSheet.Cells[row, FindColumnByHeader(effSheet, "Element")].Text,
                    DamageType = effSheet.Cells[row, FindColumnByHeader(effSheet, "DamageType")].Text,
                    Duration = SafeGetInt(effSheet.Cells[row, FindColumnByHeader(effSheet, "Duration")].Value),
                    AddInPhase = SafeGetInt(effSheet.Cells[row, FindColumnByHeader(effSheet, "AddInPhase")].Value),
                    TriggerPhase = SafeGetInt(effSheet.Cells[row, FindColumnByHeader(effSheet, "TriggerPhase")].Value),
                    Param1 = effSheet.Cells[row, FindColumnByHeader(effSheet, "Param1")].Text,
                    Param2 = ReadEffectParam2(effSheet, row, effSheet.Cells[row, FindColumnByHeader(effSheet, "EffectType")].Text),
                    Param3 = effSheet.Cells[row, FindColumnByHeader(effSheet, "Param3")].Text,
                    TargetType = effSheet.Cells[row, FindColumnByHeader(effSheet, "TargetType")].Text,
                    TargetNumber = SafeGetInt(effSheet.Cells[row, FindColumnByHeader(effSheet, "TargetNumber")].Value),
                    TargetConsecutive = SafeGetInt(effSheet.Cells[row, FindColumnByHeader(effSheet, "TargetConsecutive")].Value),
                    TargetOverride = effSheet.Cells[row, FindColumnByHeader(effSheet, "TargetOverride")].Text,
                    ScriptHook = effSheet.Cells[row, FindColumnByHeader(effSheet, "ScriptHook")].Text,
                    DamageHook = ReadOptionalEffectText(effSheet, row, "DamageHook"),
                    ExecuteCount = ReadExecuteCount(effSheet, row),
                    ExecuteCountHandler = ReadExecuteCountHandler(effSheet, row)
                };
                TryValidate("EnemySkill_Effect", () => TableValidator.ValidateEffectRow("EnemySkill_Effect", row,
                    data.EffectType, data.Element, data.Duration, data.Param2,
                    data.TargetType, data.TargetNumber, data.TargetConsecutive,
                    data.TargetOverride, isEnemy: true));
                TryValidate("EnemySkill_Effect", () =>
                {
                    TableValidator.ValidateDamageProperties("EnemySkill_Effect", row,
                        data.EffectType, data.Element, data.Param1, data.Param2, data.Param3, data.DamageHook);
                    if (data.EffectType == "Damage") data.Element = DamageProperties.NormalizeElement(data.Element);
                });
                if (data.EffectType == "ApplyElement")
                    TryValidate("EnemySkill_Effect", () => TableValidator.ValidateApplyElement("EnemySkill_Effect", row,
                        data.Element, data.Param1, data.Param2, data.Param3, data.DamageType, data.DamageHook));
                if (data.EffectType == "BondOfLife")
                    TryValidate("EnemySkill_Effect", () => TableValidator.ValidateBondOfLife("EnemySkill_Effect", row, data.Param1));
                if (data.EffectType == "Revive")
                    TryValidate("EnemySkill_Effect", () => TableValidator.ValidateRevive("EnemySkill_Effect", row,
                        data.Param1, data.Param2, data.Param3, data.Element, data.DamageType, data.DamageHook, data.Duration, data.TargetType));
                TryValidate("EnemySkill_Effect", () => TableValidator.ValidatePublicCountEffect(
                    "EnemySkill_Effect", row, data.EffectType, data.Param1, data.Param2, data.TargetType));
                // 敌人专用模式切换；ChangeControl仅允许在状态表中配置All/Freeze。
                if (data.EffectType == "SwitchPattern")
                    TryValidate("EnemySkill_Effect", () => TableValidator.ValidateSwitchPattern("EnemySkill_Effect", row,
                        data.Param1, data.Param2, data.Param3, data.Element, data.DamageType, data.DamageHook,
                        data.Duration, data.AddInPhase, data.TriggerPhase));
                if (data.EffectType == "ChangeControl")
                    TryValidate("EnemySkill_Effect", () => TableValidator.ValidateChangeControl("EnemySkill_Effect", row,
                        data.Param1, data.Param2, data.Param3, data.Duration,
                        isStatusEffectTable: false, isEnemy: true));
                if (string.IsNullOrEmpty(data.SkillEffectID2)) continue;
                EnemySkillEffectDict[data.SkillEffectID2] = data;
            }
        }

        // EnemySkill_Param
        var paramSheet = pkg.Workbook.Worksheets["EnemySkill_Param"];
        if (paramSheet != null)
        {
            int startRow = GetDataStartRow(paramSheet);
            int maxRow = GetSheetMaxRow(paramSheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = paramSheet.Cells[row, FindColumnByHeader(paramSheet, "SkillEffectID2")].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(paramSheet.Cells[row, FindColumnByHeader(paramSheet, "SkillEffectID")].Text)) continue;
                var data = new EnemySkillParamData
                {
                    SkillEffectID = SafeGetInt(paramSheet.Cells[row, FindColumnByHeader(paramSheet, "SkillEffectID")].Value),
                    SkillEffectID2 = cell,
                    EffectType = paramSheet.Cells[row, FindColumnByHeader(paramSheet, "EffectType")].Text,
                    Hits1 = paramSheet.Cells[row, FindColumnByHeader(paramSheet, "Hits1")].Text,
                    Hits2 = paramSheet.Cells[row, FindColumnByHeader(paramSheet, "Hits2")].Text,
                    Hits3 = paramSheet.Cells[row, FindColumnByHeader(paramSheet, "Hits3")].Text,
                    Hits4 = paramSheet.Cells[row, FindColumnByHeader(paramSheet, "Hits4")].Text,
                    Hits5 = paramSheet.Cells[row, FindColumnByHeader(paramSheet, "Hits5")].Text,
                    Hits6 = paramSheet.Cells[row, FindColumnByHeader(paramSheet, "Hits6")].Text,
                    Hits7 = paramSheet.Cells[row, FindColumnByHeader(paramSheet, "Hits7")].Text,
                    Hits8 = paramSheet.Cells[row, FindColumnByHeader(paramSheet, "Hits8")].Text
                };
                if (string.IsNullOrEmpty(data.SkillEffectID2)) continue;
                EnemySkillParamDict[data.SkillEffectID2] = data;
            }
        }

        // EnemyAI
        var aiSheet = pkg.Workbook.Worksheets["EnemyAI"];
        if (aiSheet != null)
        {
            int startRow = GetDataStartRow(aiSheet);
            int maxRow = GetSheetMaxRow(aiSheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = aiSheet.Cells[row, FindColumnByHeader(aiSheet, "EnemyID")].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(aiSheet.Cells[row, FindColumnByHeader(aiSheet, "EnemyNameID")].Text)) continue;
                var data = new EnemyAIData
                {
                    EnemyID = SafeGetInt(aiSheet.Cells[row, FindColumnByHeader(aiSheet, "EnemyID")].Value),
                    EnemyNameID = aiSheet.Cells[row, FindColumnByHeader(aiSheet, "EnemyNameID")].Text,
                    EnemyName = aiSheet.Cells[row, FindColumnByHeader(aiSheet, "EnemyName")].Text,
                    PatternID = SafeGetInt(aiSheet.Cells[row, FindColumnByHeader(aiSheet, "PatternID")].Value),
                    PatternType = aiSheet.Cells[row, FindColumnByHeader(aiSheet, "PatternType")].Text,
                    NextPatternID = SafeGetInt(aiSheet.Cells[row, FindColumnByHeader(aiSheet, "NextPatternID")].Value),
                    ConditionType = aiSheet.Cells[row, FindColumnByHeader(aiSheet, "ConditionType")].Text,
                    ConditionParam = aiSheet.Cells[row, FindColumnByHeader(aiSheet, "ConditionParam")].Text
                };
                if (data.EnemyID == 0) continue;
                if (!EnemyAIDict.ContainsKey(data.EnemyID)) EnemyAIDict[data.EnemyID] = new List<EnemyAIData>();
                EnemyAIDict[data.EnemyID].Add(data);
            }
        }

        // EnemyAI_Rule（PatternID/PatternType/EnemyID 空值向上沿用）
        var ruleSheet = pkg.Workbook.Worksheets["EnemyAI_Rule"];
        if (ruleSheet != null)
        {
            int startRow = GetDataStartRow(ruleSheet);
            int maxRow = GetSheetMaxRow(ruleSheet);
            int lastPatternID = 0;
            string lastPatternType = "";
            int lastEnemyID = 0;
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = ruleSheet.Cells[row, FindColumnByHeader(ruleSheet, "EnemyID")].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(ruleSheet.Cells[row, FindColumnByHeader(ruleSheet, "PatternID")].Text) && string.IsNullOrEmpty(ruleSheet.Cells[row, FindColumnByHeader(ruleSheet, "SkillID")].Text)) continue;
                int enemyID = 0;
                if (!string.IsNullOrEmpty(cell)) enemyID = SafeGetInt(ruleSheet.Cells[row, FindColumnByHeader(ruleSheet, "EnemyID")].Value);
                string pidCell = ruleSheet.Cells[row, FindColumnByHeader(ruleSheet, "PatternID")].Text;
                string ptypeCell = ruleSheet.Cells[row, FindColumnByHeader(ruleSheet, "PatternType")].Text;
                int patternID = string.IsNullOrEmpty(pidCell) ? lastPatternID : SafeGetInt(ruleSheet.Cells[row, FindColumnByHeader(ruleSheet, "PatternID")].Value);
                string patternType = string.IsNullOrEmpty(ptypeCell) ? lastPatternType : ptypeCell;
                lastPatternID = patternID;
                lastPatternType = patternType;
                if (enemyID != 0) lastEnemyID = enemyID;
                var data = new EnemyAIRuleData
                {
                    EnemyID = enemyID == 0 ? lastEnemyID : enemyID,
                    PatternID = patternID,
                    PatternType = patternType,
                    SkillID = SafeGetInt(ruleSheet.Cells[row, FindColumnByHeader(ruleSheet, "SkillID")].Value),
                    SkillIndex = SafeGetInt(ruleSheet.Cells[row, FindColumnByHeader(ruleSheet, "SkillIndex")].Value),
                    Weight = SafeGetInt(ruleSheet.Cells[row, FindColumnByHeader(ruleSheet, "Weight")].Value),
                    ScriptHook = ReadOptionalEffectText(ruleSheet, row, "ScriptHook")
                };
                if (data.EnemyID == 0) continue;
                if (!EnemyAIRuleDict.ContainsKey(data.EnemyID)) EnemyAIRuleDict[data.EnemyID] = new List<EnemyAIRuleData>();
                TryValidate("EnemyAI_Rule", () =>
                {
                    if (!EnemySkillMainDict.ContainsKey(data.SkillID))
                        throw new TableValidationException($"EnemyAI_Rule 第{row}行 EnemyID={data.EnemyID}, PatternID={data.PatternID}: SkillID={data.SkillID} 未在EnemySkill_Main定义");
                });
                EnemyAIRuleDict[data.EnemyID].Add(data);
            }
        }
    }
}
