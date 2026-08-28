using System;
using System.Collections.Generic;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

public partial class DataManager
{
    // ================================================================
    //  EnemyAttributes.xlsx 的 Enemy_Main
    // ================================================================
    void LoadEnemyAttributes()
    {
        string path = Path.Combine(ChartsPath, "EnemyAttributes.xlsx");
        if (!File.Exists(path)) { Debug.LogWarning("EnemyAttributes.xlsx not found"); return; }
        using var pkg = new ExcelPackage(new FileInfo(path));
        var sheet = pkg.Workbook.Worksheets["Enemy_Main"];
        if (sheet == null) return;
        int startRow = GetDataStartRow(sheet);
        int maxRow = GetSheetMaxRow(sheet);
        for (int row = startRow; row <= maxRow; row++)
        {
            string cell = sheet.Cells[row, 1].Text;
            if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(sheet.Cells[row, 2].Text)) continue;
            var data = new EnemyMainData
            {
                EnemyID = SafeGetInt(sheet.Cells[row, 1].Value),
                EnemyNameID = sheet.Cells[row, 2].Text,
                EnemyName = sheet.Cells[row, 3].Text,
                Threat = SafeGetInt(sheet.Cells[row, 4].Value),
                ActsGiven = SafeGetInt(sheet.Cells[row, 5].Value),
                Poise = SafeGetFloat(sheet.Cells[row, 6].Value),
                ATK_Curve = SafeGetInt(sheet.Cells[row, 7].Value),
                HP_coeff = SafeGetFloat(sheet.Cells[row, 8].Value),
                ATK_coeff = SafeGetFloat(sheet.Cells[row, 9].Value),
                PhysicalRes = SafeGetFloat(sheet.Cells[row, 10].Value),
                PyroRes = SafeGetFloat(sheet.Cells[row, 11].Value),
                HydroRes = SafeGetFloat(sheet.Cells[row, 12].Value),
                ElectroRes = SafeGetFloat(sheet.Cells[row, 13].Value),
                CryoRes = SafeGetFloat(sheet.Cells[row, 14].Value),
                AnemoRes = SafeGetFloat(sheet.Cells[row, 15].Value),
                DendroRes = SafeGetFloat(sheet.Cells[row, 16].Value),
                GeoRes = SafeGetFloat(sheet.Cells[row, 17].Value),
                PhysicalDmgBonus = SafeGetFloat(sheet.Cells[row, 18].Value),
                PyroDmgBonus = SafeGetFloat(sheet.Cells[row, 19].Value),
                HydroDmgBonus = SafeGetFloat(sheet.Cells[row, 20].Value),
                ElectroDmgBonus = SafeGetFloat(sheet.Cells[row, 21].Value),
                CryoDmgBonus = SafeGetFloat(sheet.Cells[row, 22].Value),
                AnemoDmgBonus = SafeGetFloat(sheet.Cells[row, 23].Value),
                DendroDmgBonus = SafeGetFloat(sheet.Cells[row, 24].Value),
                GeoDmgBonus = SafeGetFloat(sheet.Cells[row, 25].Value)
            };
            TryValidate("Enemy_Main", () => TableValidator.ValidateEnemyMainRow(row, data.EnemyID, data.Poise));
            if (data.EnemyID == 0) continue;
            EnemyMainDict[data.EnemyID] = data;
        }
    }

    // ================================================================
    //  EnemyAttributes.xlsx 的 Base_HP / Base_ATK1 / Base_ATK2 曲线
    // ================================================================
    void LoadEnemyCurves()
    {
        string path = Path.Combine(ChartsPath, "EnemyAttributes.xlsx");
        if (!File.Exists(path)) { Debug.LogWarning("EnemyAttributes.xlsx not found"); return; }
        using var pkg = new ExcelPackage(new FileInfo(path));

        var hpSheet = pkg.Workbook.Worksheets["Base_HP"];
        if (hpSheet != null)
        {
            int startRow = GetDataStartRow(hpSheet);
            int maxRow = GetSheetMaxRow(hpSheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = hpSheet.Cells[row, 1].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(hpSheet.Cells[row, 2].Text)) continue;
                BaseHPDict[SafeGetInt(hpSheet.Cells[row, 1].Value)] = SafeGetFloat(hpSheet.Cells[row, 2].Value);
            }
        }

        var atk1Sheet = pkg.Workbook.Worksheets["Base_ATK1"];
        if (atk1Sheet != null)
        {
            int startRow = GetDataStartRow(atk1Sheet);
            int maxRow = GetSheetMaxRow(atk1Sheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = atk1Sheet.Cells[row, 1].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(atk1Sheet.Cells[row, 2].Text)) continue;
                BaseATK1Dict[SafeGetInt(atk1Sheet.Cells[row, 1].Value)] = SafeGetFloat(atk1Sheet.Cells[row, 2].Value);
            }
        }

        var atk2Sheet = pkg.Workbook.Worksheets["Base_ATK2"];
        if (atk2Sheet != null)
        {
            int startRow = GetDataStartRow(atk2Sheet);
            int maxRow = GetSheetMaxRow(atk2Sheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = atk2Sheet.Cells[row, 1].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(atk2Sheet.Cells[row, 2].Text)) continue;
                BaseATK2Dict[SafeGetInt(atk2Sheet.Cells[row, 1].Value)] = SafeGetFloat(atk2Sheet.Cells[row, 2].Value);
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
    //  EnemySkill.xlsx 5 sheets
    // ================================================================
    void LoadEnemySkill()
    {
        string path = Path.Combine(ChartsPath, "EnemySkill.xlsx");
        if (!File.Exists(path)) { Debug.LogWarning("EnemySkill.xlsx not found"); return; }
        using var pkg = new ExcelPackage(new FileInfo(path));

        // EnemySkill_Main
        var mainSheet = pkg.Workbook.Worksheets["EnemySkill_Main"];
        if (mainSheet != null)
        {
            int startRow = GetDataStartRow(mainSheet);
            int maxRow = GetSheetMaxRow(mainSheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = mainSheet.Cells[row, 1].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(mainSheet.Cells[row, 2].Text)) continue;
                var data = new EnemySkillMainData
                {
                    EnemySkillID = SafeGetInt(mainSheet.Cells[row, 1].Value),
                    EnemySkillID2 = mainSheet.Cells[row, 2].Text,
                    EnemySkillName = mainSheet.Cells[row, 3].Text,
                    Cooldown = SafeGetFloat(mainSheet.Cells[row, 4].Value),
                    UsePerTurn = SafeGetInt(mainSheet.Cells[row, 5].Value),
                    HighThreat = SafeGetInt(mainSheet.Cells[row, 6].Value),
                    Description = mainSheet.Cells[row, 7].Text
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
                string cell = effSheet.Cells[row, 2].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(effSheet.Cells[row, 1].Text)) continue;
                var data = new EnemySkillEffectData
                {
                    SkillEffectID = SafeGetInt(effSheet.Cells[row, 1].Value),
                    SkillEffectID2 = cell,
                    EffectIndex = SafeGetInt(effSheet.Cells[row, 3].Value),
                    EffectType = effSheet.Cells[row, 4].Text,
                    Element = effSheet.Cells[row, 5].Text,
                    DamageType = effSheet.Cells[row, 6].Text,
                    Duration = SafeGetInt(effSheet.Cells[row, 7].Value),
                    AddInPhase = SafeGetInt(effSheet.Cells[row, 8].Value),
                    TriggerPhase = SafeGetInt(effSheet.Cells[row, 9].Value),
                    Param1 = effSheet.Cells[row, 10].Text,
                    Param2 = effSheet.Cells[row, 11].Text,
                    Param3 = effSheet.Cells[row, 12].Text,
                    TargetType = effSheet.Cells[row, 13].Text,
                    TargetNumber = SafeGetInt(effSheet.Cells[row, 14].Value),
                    TargetConsecutive = SafeGetInt(effSheet.Cells[row, 15].Value),
                    TargetOverride = effSheet.Cells[row, 16].Text,
                    ScriptHook = effSheet.Cells[row, 17].Text
                };
                TryValidate("EnemySkill_Effect", () => TableValidator.ValidateEffectRow("EnemySkill_Effect", row,
                    data.EffectType, data.Element, data.Duration, data.Param2,
                    data.TargetType, data.TargetNumber, data.TargetConsecutive,
                    data.TargetOverride, isEnemy: true));
                // ChangeControl 专属校验（2026-08-11）：敌人没有按键绑定，禁止使用
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
                string cell = paramSheet.Cells[row, 2].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(paramSheet.Cells[row, 1].Text)) continue;
                var data = new EnemySkillParamData
                {
                    SkillEffectID = SafeGetInt(paramSheet.Cells[row, 1].Value),
                    SkillEffectID2 = cell,
                    EffectType = paramSheet.Cells[row, 3].Text,
                    Hits1 = paramSheet.Cells[row, 4].Text,
                    Hits2 = paramSheet.Cells[row, 5].Text,
                    Hits3 = paramSheet.Cells[row, 6].Text,
                    Hits4 = paramSheet.Cells[row, 7].Text,
                    Hits5 = paramSheet.Cells[row, 8].Text,
                    Hits6 = paramSheet.Cells[row, 9].Text,
                    Hits7 = paramSheet.Cells[row, 10].Text,
                    Hits8 = paramSheet.Cells[row, 11].Text
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
                string cell = aiSheet.Cells[row, 1].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(aiSheet.Cells[row, 2].Text)) continue;
                var data = new EnemyAIData
                {
                    EnemyID = SafeGetInt(aiSheet.Cells[row, 1].Value),
                    EnemyNameID = aiSheet.Cells[row, 2].Text,
                    EnemyName = aiSheet.Cells[row, 3].Text,
                    PatternID = SafeGetInt(aiSheet.Cells[row, 4].Value),
                    PatternType = aiSheet.Cells[row, 5].Text,
                    NextPatternID = SafeGetInt(aiSheet.Cells[row, 6].Value),
                    ConditionType = aiSheet.Cells[row, 7].Text,
                    ConditionParam = aiSheet.Cells[row, 8].Text
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
                string cell = ruleSheet.Cells[row, 1].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(ruleSheet.Cells[row, 2].Text) && string.IsNullOrEmpty(ruleSheet.Cells[row, 4].Text)) continue;
                int enemyID = 0;
                if (!string.IsNullOrEmpty(cell)) enemyID = SafeGetInt(ruleSheet.Cells[row, 1].Value);
                string pidCell = ruleSheet.Cells[row, 2].Text;
                string ptypeCell = ruleSheet.Cells[row, 3].Text;
                int patternID = string.IsNullOrEmpty(pidCell) ? lastPatternID : SafeGetInt(ruleSheet.Cells[row, 2].Value);
                string patternType = string.IsNullOrEmpty(ptypeCell) ? lastPatternType : ptypeCell;
                lastPatternID = patternID;
                lastPatternType = patternType;
                if (enemyID != 0) lastEnemyID = enemyID;
                var data = new EnemyAIRuleData
                {
                    EnemyID = enemyID == 0 ? lastEnemyID : enemyID,
                    PatternID = patternID,
                    PatternType = patternType,
                    SkillID = SafeGetInt(ruleSheet.Cells[row, 4].Value),
                    SkillIndex = SafeGetInt(ruleSheet.Cells[row, 5].Value),
                    Weight = SafeGetInt(ruleSheet.Cells[row, 6].Value)
                };
                if (data.EnemyID == 0) continue;
                if (!EnemyAIRuleDict.ContainsKey(data.EnemyID)) EnemyAIRuleDict[data.EnemyID] = new List<EnemyAIRuleData>();
                EnemyAIRuleDict[data.EnemyID].Add(data);
            }
        }
    }
}
