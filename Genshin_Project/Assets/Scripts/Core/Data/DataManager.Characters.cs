using System;
using System.Collections.Generic;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

public partial class DataManager
{
    // ================================================================
    //  Characters.xlsx 的 Sheet1
    // ================================================================
    void LoadCharacterOverview()
    {
        string path = Path.Combine(ChartsPath, "Characters.xlsx");
        if (!File.Exists(path)) { Debug.LogWarning("StreamingAssets/Data/Characters.xlsx not found"); return; }
        using var pkg = new ExcelPackage(new FileInfo(path));
        var sheet = pkg.Workbook.Worksheets[0];
        if (sheet == null) return;
        int startRow = GetDataStartRow(sheet);
        int maxRow = GetSheetMaxRow(sheet);
        for (int row = startRow; row <= maxRow; row++)
        {
            string cell = sheet.Cells[row, 1].Text;
            if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(sheet.Cells[row, 2].Text)) continue;
            var data = new CharacterOverviewData
            {
                CharacterID = SafeGetInt(sheet.Cells[row, 1].Value),
                Name = sheet.Cells[row, 2].Text
            };
            if (data.CharacterID == 0) continue;
            CharacterOverviewList.Add(data);
        }
    }

    // ================================================================
    //  Characters/{id}_{name}.xlsx 全部 sheets
    //  跳过 0_ 开头的模板文件；跳过 CharacterID 为 0 的空行
    // ================================================================
    void LoadCharacterSheets()
    {
        string charDir = Path.Combine(ChartsPath, "Characters");
        if (!Directory.Exists(charDir)) { Debug.LogWarning("StreamingAssets/Data/Characters/ not found"); return; }
        var files = Directory.GetFiles(charDir, "*.xlsx");
        foreach (string f in files)
        {
            string fileName = Path.GetFileNameWithoutExtension(f);
            if (fileName.StartsWith("0_")) continue; // 跳过模板
            try
            {
                using var pkg = new ExcelPackage(new FileInfo(f));
                var sheets = pkg.Workbook.Worksheets;
                if (sheets.Count == 0) continue;

                // 每个角色工作簿只描述一个角色。Ascension 表本身没有 CharacterID 列，
                // 因此角色 ID 必须从同一工作簿的 Attributes 表取得。
                int workbookCharacterId = 0;

                var attrSheet = sheets["Attributes"];
                if (attrSheet != null)
                {
                    int startRow = GetDataStartRow(attrSheet);
                    int maxRow = GetSheetMaxRow(attrSheet);
                    for (int row = startRow; row <= maxRow; row++)
                    {
                        string cell = attrSheet.Cells[row, 1].Text;
                        if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(attrSheet.Cells[row, 2].Text)) continue;
                        var data = new CharacterAttributesData
                        {
                            CharacterID = SafeGetInt(attrSheet.Cells[row, 1].Value),
                            NameID = attrSheet.Cells[row, 2].Text,
                            Name = attrSheet.Cells[row, 3].Text,
                            Star = SafeGetInt(attrSheet.Cells[row, 4].Value),
                            GrowthCurveID = SafeGetInt(attrSheet.Cells[row, 5].Value),
                            WeaponType = SafeGetInt(attrSheet.Cells[row, 6].Value),
                            Element = attrSheet.Cells[row, 7].Text,
                            BaseHP = SafeGetFloat(attrSheet.Cells[row, 8].Value),
                            BaseATK = SafeGetFloat(attrSheet.Cells[row, 9].Value),
                            BaseDEF = SafeGetFloat(attrSheet.Cells[row, 10].Value),
                            MaxEnergy = SafeGetInt(attrSheet.Cells[row, 11].Value),
                            Poise = SafeGetFloat(attrSheet.Cells[row, 12].Value),
                            HitWeight = SafeGetFloat(attrSheet.Cells[row, 13].Value),
                            NormalAttackID = attrSheet.Cells[row, 14].Text,
                            HeavyAttackID = attrSheet.Cells[row, 15].Text,
                            ElementalSkillID = attrSheet.Cells[row, 16].Text,
                            ElementalBurstID = attrSheet.Cells[row, 17].Text,
                            BaseEM = SafeGetInt(attrSheet.Cells[row, 18].Value),
                            BaseRechargeRate = SafeGetFloat(attrSheet.Cells[row, 19].Value),
                            CritRate = SafeGetFloat(attrSheet.Cells[row, 20].Value),
                            CritDMG = SafeGetFloat(attrSheet.Cells[row, 21].Value),
                            PyroRes = SafeGetFloat(attrSheet.Cells[row, 22].Value),
                            HydroRes = SafeGetFloat(attrSheet.Cells[row, 23].Value),
                            ElectroRes = SafeGetFloat(attrSheet.Cells[row, 24].Value),
                            CryoRes = SafeGetFloat(attrSheet.Cells[row, 25].Value),
                            AnemoRes = SafeGetFloat(attrSheet.Cells[row, 26].Value),
                            DendroRes = SafeGetFloat(attrSheet.Cells[row, 27].Value),
                            GeoRes = SafeGetFloat(attrSheet.Cells[row, 28].Value),
                            PhysicalRes = SafeGetFloat(attrSheet.Cells[row, 29].Value),
                            PyroDmgBonus = SafeGetFloat(attrSheet.Cells[row, 30].Value),
                            HydroDmgBonus = SafeGetFloat(attrSheet.Cells[row, 31].Value),
                            ElectroDmgBonus = SafeGetFloat(attrSheet.Cells[row, 32].Value),
                            CryoDmgBonus = SafeGetFloat(attrSheet.Cells[row, 33].Value),
                            AnemoDmgBonus = SafeGetFloat(attrSheet.Cells[row, 34].Value),
                            GeoDmgBonus = SafeGetFloat(attrSheet.Cells[row, 35].Value),
                            DendroDmgBonus = SafeGetFloat(attrSheet.Cells[row, 36].Value),
                            PhysicalDmgBonus = SafeGetFloat(attrSheet.Cells[row, 37].Value)
                        };
                        if (data.CharacterID == 0) continue;
                        if (workbookCharacterId != 0 && workbookCharacterId != data.CharacterID)
                            throw new InvalidDataException($"角色表 {fileName} 的 Attributes 中出现了多个 CharacterID");
                        workbookCharacterId = data.CharacterID;
                        CharacterAttributesDict[data.CharacterID] = data;
                    }
                }

                var ascSheet = sheets["Ascension"];
                if (ascSheet != null)
                {
                    if (workbookCharacterId == 0)
                        throw new InvalidDataException($"角色表 {fileName} 无法从 Attributes 取得 CharacterID");

                    int startRow = GetDataStartRow(ascSheet);
                    int maxRow = GetSheetMaxRow(ascSheet);
                    for (int row = startRow; row <= maxRow; row++)
                    {
                        string cell = ascSheet.Cells[row, 1].Text;
                        if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(ascSheet.Cells[row, 2].Text)) continue;
                        var data = new CharacterAscensionData
                        {
                            AscensionLevel = SafeGetInt(ascSheet.Cells[row, 1].Value),
                            BaseHPFlat = SafeGetFloat(ascSheet.Cells[row, 2].Value),
                            BaseATKFlat = SafeGetFloat(ascSheet.Cells[row, 3].Value),
                            BaseDEFFlat = SafeGetFloat(ascSheet.Cells[row, 4].Value),
                            HPBonus = SafeGetFloat(ascSheet.Cells[row, 5].Value),
                            ATKBonus = SafeGetFloat(ascSheet.Cells[row, 6].Value),
                            DEFBonus = SafeGetFloat(ascSheet.Cells[row, 7].Value),
                            CritRate = SafeGetFloat(ascSheet.Cells[row, 8].Value),
                            CritDMG = SafeGetFloat(ascSheet.Cells[row, 9].Value),
                            EM = SafeGetFloat(ascSheet.Cells[row, 10].Value),
                            EnergyRechargeRate = SafeGetFloat(ascSheet.Cells[row, 11].Value),
                            PyroDmgBonus = SafeGetFloat(ascSheet.Cells[row, 12].Value),
                            HydroDmgBonus = SafeGetFloat(ascSheet.Cells[row, 13].Value),
                            ElectroDmgBonus = SafeGetFloat(ascSheet.Cells[row, 14].Value),
                            CryoDmgBonus = SafeGetFloat(ascSheet.Cells[row, 15].Value),
                            AnemoDmgBonus = SafeGetFloat(ascSheet.Cells[row, 16].Value),
                            GeoDmgBonus = SafeGetFloat(ascSheet.Cells[row, 17].Value),
                            DendroDmgBonus = SafeGetFloat(ascSheet.Cells[row, 18].Value),
                            PhysicalDmgBonus = SafeGetFloat(ascSheet.Cells[row, 19].Value)
                        };
                        if (!CharacterAscensionDict.ContainsKey(workbookCharacterId))
                            CharacterAscensionDict[workbookCharacterId] = new List<CharacterAscensionData>();
                        CharacterAscensionDict[workbookCharacterId].Add(data);
                    }
                }

                var skillSheet = sheets["Skills"];
                if (skillSheet != null)
                {
                    int startRow = GetDataStartRow(skillSheet);
                    int maxRow = GetSheetMaxRow(skillSheet);
                    int charId = 0;
                    for (int row = startRow; row <= maxRow; row++)
                    {
                        string cell = skillSheet.Cells[row, 1].Text;
                        if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(skillSheet.Cells[row, 2].Text)) continue;
                        var data = new SkillMainData
                        {
                            SkillID = SafeGetInt(skillSheet.Cells[row, 1].Value),
                            SkillID2 = skillSheet.Cells[row, 2].Text,
                            SkillName = skillSheet.Cells[row, 3].Text,
                            APCost = SafeGetInt(skillSheet.Cells[row, 4].Value),
                            Cooldown = SafeGetFloat(skillSheet.Cells[row, 5].Value),
                            EnergyUsed = SafeGetInt(skillSheet.Cells[row, 6].Value),
                            MaxCharge = SafeGetInt(skillSheet.Cells[row, 7].Value),
                            InitialCharge = SafeGetInt(skillSheet.Cells[row, 8].Value),
                            SkillPhase = SafeGetInt(skillSheet.Cells[row, 9].Value),
                            ActionType = skillSheet.Cells[row, 10].Text,
                            Description = skillSheet.Cells[row, 11].Text
                        };
                        // 角色ID = SkillID / 100（SkillID规则：角色ID + 从01开始的两位序号，如 1009 -> 100901）
                        if (charId == 0 && data.SkillID != 0) charId = data.SkillID / 100;
                        if (charId == 0) continue;
                        if (!CharacterSkillDict.ContainsKey(charId)) CharacterSkillDict[charId] = new List<SkillMainData>();
                        CharacterSkillDict[charId].Add(data);
                        if (!string.IsNullOrEmpty(data.SkillID2)) SkillStringToInt[data.SkillID2] = data.SkillID;
                    }
                }

                var seSheet = sheets["SkillsEffect"];
                if (seSheet != null)
                {
                    int startRow = GetDataStartRow(seSheet);
                    int maxRow = GetSheetMaxRow(seSheet);
                    for (int row = startRow; row <= maxRow; row++)
                    {
                        string cell = seSheet.Cells[row, 2].Text;
                        if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(seSheet.Cells[row, 1].Text)) continue;
                        var data = new SkillEffectData
                        {
                            SkillEffectID = SafeGetInt(seSheet.Cells[row, 1].Value),
                            SkillEffectID2 = seSheet.Cells[row, 2].Text,
                            EffectIndex = SafeGetInt(seSheet.Cells[row, 3].Value),
                            EffectType = seSheet.Cells[row, 4].Text,
                            Element = seSheet.Cells[row, 5].Text,
                            DamageType = seSheet.Cells[row, 6].Text,
                            Duration = SafeGetInt(seSheet.Cells[row, 7].Value),
                            AddInPhase = SafeGetInt(seSheet.Cells[row, 8].Value),
                            TriggerPhase = SafeGetInt(seSheet.Cells[row, 9].Value),
                            Param1 = seSheet.Cells[row, 10].Text,
                            Param2 = seSheet.Cells[row, 11].Text,
                            Param3 = seSheet.Cells[row, 12].Text,
                            TargetType = seSheet.Cells[row, 13].Text,
                            TargetNumber = SafeGetInt(seSheet.Cells[row, 14].Value),
                            TargetConsecutive = SafeGetInt(seSheet.Cells[row, 15].Value),
                            TargetConsecutiveSet = !string.IsNullOrEmpty(seSheet.Cells[row, 15].Text),
                            TargetOverride = seSheet.Cells[row, 16].Text,
                            ScriptHook = seSheet.Cells[row, 17].Text
                        };
                        TryValidate("SkillsEffect", () => TableValidator.ValidateEffectRow("SkillsEffect", row,
                            data.EffectType, data.Element, data.Duration, data.Param2,
                            data.TargetType, data.TargetNumber, data.TargetConsecutive,
                            data.TargetOverride, isEnemy: false));
                        // ChangeControl 专属校验（2026-08-11）：技能效果表允许，填了Duration定时结束，没填=永久
                        if (data.EffectType == "ChangeControl")
                            TryValidate("SkillsEffect", () => TableValidator.ValidateChangeControl("SkillsEffect", row,
                                data.Param1, data.Param2, data.Param3, data.Duration,
                                isStatusEffectTable: false, isEnemy: false));
                        if (string.IsNullOrEmpty(data.SkillEffectID2)) continue;
                        SkillEffectDict[data.SkillEffectID2] = data;
                    }
                }

                var slSheet = sheets["SkillLevel"];
                if (slSheet != null)
                {
                    int startRow = GetDataStartRow(slSheet);
                    int maxRow = GetSheetMaxRow(slSheet);
                    // SkillLevel 是分组子表：CharacterID、SkillType、ParamID 留空时继承上方最近的非空值。
                    // Hits1~Hits7 不继承；它们仍由战斗层按当前等级/1级倍率的既有规则合并。
                    int charId = 0;
                    int skillType = 0;
                    string paramID = string.Empty;
                    for (int row = startRow; row <= maxRow; row++)
                    {
                        string cid = slSheet.Cells[row, 1].Text;
                        string stype = slSheet.Cells[row, 2].Text;
                        string slevel = slSheet.Cells[row, 3].Text;
                        string rowParamID = slSheet.Cells[row, 4].Text;
                        if (string.IsNullOrEmpty(cid) && string.IsNullOrEmpty(stype)
                            && string.IsNullOrEmpty(slevel) && string.IsNullOrEmpty(rowParamID)) continue;

                        if (!string.IsNullOrEmpty(cid))
                            charId = SafeGetInt(slSheet.Cells[row, 1].Value);
                        if (!string.IsNullOrEmpty(stype))
                        {
                            if (!int.TryParse(stype, out skillType))
                            {
                                switch (stype.Trim())
                                {
                                    case "Normal": skillType = 0; break;
                                    case "Heavy": skillType = 1; break;
                                    case "Skill": skillType = 2; break;
                                    case "Burst": skillType = 3; break;
                                }
                            }
                        }
                        if (!string.IsNullOrEmpty(rowParamID))
                            paramID = rowParamID;
                        if (charId == 0 || string.IsNullOrEmpty(paramID)) continue;

                        int skillLevel = 0;
                        int.TryParse(slevel, out skillLevel);
                        if (skillLevel <= 0) continue;

                        // ParamID 必须参与分组；同一 SkillType 可以有多套等级参数（如凯亚普通冰棱/C6冰棱）。
                        string key = $"{charId}_{skillType}_{paramID}";
                        var data = new SkillLevelData
                        {
                            CharacterID = charId,
                            SkillType = skillType,
                            SkillLevel = skillLevel,
                            ParamID = paramID,
                            Hits1 = slSheet.Cells[row, 5].Text,
                            Hits2 = slSheet.Cells[row, 6].Text,
                            Hits3 = slSheet.Cells[row, 7].Text,
                            Hits4 = slSheet.Cells[row, 8].Text,
                            Hits5 = slSheet.Cells[row, 9].Text,
                            Hits6 = slSheet.Cells[row, 10].Text,
                            Hits7 = slSheet.Cells[row, 11].Text
                        };
                        if (!SkillLevelDict.ContainsKey(key)) SkillLevelDict[key] = new Dictionary<int, SkillLevelData>();
                        SkillLevelDict[key][skillLevel] = data;
                    }
                }

                var conSheet = sheets["Constellation"];
                if (conSheet != null)
                {
                    int startRow = GetDataStartRow(conSheet);
                    int maxRow = GetSheetMaxRow(conSheet);
                    int charId = 0;
                    for (int row = startRow; row <= maxRow; row++)
                    {
                        string cell = conSheet.Cells[row, 1].Text;
                        if (!string.IsNullOrEmpty(cell)) charId = SafeGetInt(conSheet.Cells[row, 1].Value);
                        if (charId == 0) continue;
                        if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(conSheet.Cells[row, 2].Text)) continue;
                        var data = new ConstellationData
                        {
                            CharacterID = charId,
                            ConstellationIndex = SafeGetInt(conSheet.Cells[row, 2].Value),
                            ConstellationName = conSheet.Cells[row, 3].Text,
                            Modifiers = conSheet.Cells[row, 4].Text,
                            Description = conSheet.Cells[row, 5].Text
                        };
                        if (!ConstellationDict.ContainsKey(charId)) ConstellationDict[charId] = new List<ConstellationData>();
                        ConstellationDict[charId].Add(data);
                    }
                }

                var talSheet = sheets["Talent"];
                if (talSheet != null)
                {
                    int startRow = GetDataStartRow(talSheet);
                    int maxRow = GetSheetMaxRow(talSheet);
                    int charId = 0;
                    for (int row = startRow; row <= maxRow; row++)
                    {
                        string cell = talSheet.Cells[row, 1].Text;
                        if (!string.IsNullOrEmpty(cell)) charId = SafeGetInt(talSheet.Cells[row, 1].Value);
                        if (charId == 0) continue;
                        if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(talSheet.Cells[row, 2].Text)) continue;
                        var data = new TalentData
                        {
                            CharacterID = charId,
                            TalentIndex = SafeGetInt(talSheet.Cells[row, 2].Value),
                            TalentName = talSheet.Cells[row, 3].Text,
                            UnlockAfter = SafeGetInt(talSheet.Cells[row, 4].Value),
                            Modifiers = talSheet.Cells[row, 5].Text,
                            Description = talSheet.Cells[row, 6].Text
                        };
                        if (!TalentDict.ContainsKey(charId)) TalentDict[charId] = new List<TalentData>();
                        TalentDict[charId].Add(data);
                    }
                }
            }
            catch (TableValidationException)
            {
                throw; // 配表校验失败：直接中断加载，不吞异常
            }
            catch (Exception e)
            {
                _loadErrors.Add($"[Characters/{Path.GetFileName(f)}] {e.Message}");
            }
        }
    }

    // ================================================================
    //  Character_Growth_Curve.xlsx（多sheet：GrowthCurve_1 / GrowthCurve_2）
    //  按 sheet 序号作为曲线ID（第1张=1，第2张=2，依此类推）
    // ================================================================
    void LoadGrowthCurve()
    {
        string path = Path.Combine(ChartsPath, "Character_Growth_Curve.xlsx");
        if (!File.Exists(path)) return;
        using var pkg = new ExcelPackage(new FileInfo(path));
        for (int s = 0; s < pkg.Workbook.Worksheets.Count; s++)
        {
            var sheet = pkg.Workbook.Worksheets[s];
            if (sheet == null) continue;
            int curveID = s + 1; // 曲线ID从1开始
            if (!GrowthCurveDict.ContainsKey(curveID)) GrowthCurveDict[curveID] = new Dictionary<int, float>();
            int startRow = GetDataStartRow(sheet);
            int maxRow = GetSheetMaxRow(sheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = sheet.Cells[row, 1].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(sheet.Cells[row, 2].Text)) continue;
                GrowthCurveDict[curveID][SafeGetInt(sheet.Cells[row, 1].Value)] = SafeGetFloat(sheet.Cells[row, 2].Value);
            }
        }
    }

    // 查询指定曲线在指定等级的成长倍率；曲线不存在或等级不存在返回1f
    public float GetGrowthCurve(int curveID, int level)
    {
        if (GrowthCurveDict.TryGetValue(curveID, out var levelMap))
        {
            if (levelMap.TryGetValue(level, out float mult)) return mult;
        }
        return 1f;
    }
}
