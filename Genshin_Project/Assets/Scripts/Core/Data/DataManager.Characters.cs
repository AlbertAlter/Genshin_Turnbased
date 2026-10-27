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
            string cell = sheet.Cells[row, FindColumnByHeader(sheet, "CharacterID")].Text;
            if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(sheet.Cells[row, FindColumnByHeader(sheet, "Name")].Text)) continue;
            var data = ReadCharacterOverviewRow(sheet, row);
            if (data.CharacterID == 0) continue;
            if (GetCharacterOverview(data.CharacterID) != null)
                throw new TableValidationException($"Characters 第{row}行重复CharacterID：{data.CharacterID}");
            CharacterOverviewList.Add(data);
        }
    }

    static CharacterOverviewData ReadCharacterOverviewRow(ExcelWorksheet sheet, int row)
    {
        string Text(string english, string chinese)
        {
            var headers = ExcelTableHeaders.For(sheet);
            int column = headers.Optional(english);
            if (column < 1) column = headers.Optional(chinese);
            return column > 0 ? sheet.Cells[row, column].Text.Trim() : string.Empty;
        }
        return new CharacterOverviewData
        {
            CharacterID = int.Parse(sheet.Cells[row, FindColumnByHeader(sheet, "CharacterID")].Text.Trim()),
            NameID = Text("NameID", "英文标识"),
            Name = sheet.Cells[row, FindColumnByHeader(sheet, "Name")].Text.Trim(),
            Nation = CharacterMetadataParser.ParseNation(Text("Nation", "国家")),
            Element = CharacterMetadataParser.ParseElement(Text("Element", "元素")),
            WeaponType = CharacterMetadataParser.ParseWeaponType(Text("WeaponType", "武器类型"))
        };
    }

    /// <summary>统一角色身份查询，未配置返回null；国家不由技能、元素或姓名推断。</summary>
    public CharacterOverviewData GetCharacterOverview(int characterID)
        => CharacterOverviewList.Find(character => character.CharacterID == characterID);

    /// <summary>统计完整编队ID，去重但不筛存活/前后台；没有归属数据时明确报错，不当成无归属。</summary>
    public int CountCharactersFromNation(IEnumerable<int> characterIDs, string nation)
    {
        if (characterIDs == null) throw new ArgumentNullException(nameof(characterIDs));
        string expected = CharacterMetadataParser.ParseNation(nation);
        if (string.IsNullOrEmpty(expected)) throw new ArgumentException("必须明确指定统计的国家。", nameof(nation));
        var seen = new HashSet<int>();
        int count = 0;
        foreach (int id in characterIDs)
        {
            if (!seen.Add(id)) continue;
            var metadata = GetCharacterOverview(id);
            if (metadata == null || string.IsNullOrEmpty(metadata.Nation))
                throw new InvalidOperationException("角色缺少正式Nation归属配置：" + id);
            if (metadata.Nation == expected) count++;
        }
        return count;
    }

    void ApplyCharacterOverviewIdentity(CharacterAttributesData attributes)
    {
        var metadata = GetCharacterOverview(attributes.CharacterID);
        if (metadata == null) return; // 兼容独立角色表/内存测试；不伪造不存在的身份。
        attributes.Nation = metadata.Nation;
        if (!string.IsNullOrEmpty(metadata.Element)) attributes.Element = metadata.Element;
        if (metadata.WeaponType >= 0) attributes.WeaponType = metadata.WeaponType;
    }

    static int FindColumnByHeader(ExcelWorksheet sheet, string header)
    {
        return ExcelTableHeaders.For(sheet).Required(header);
    }

    static string ReadOptionalEffectText(ExcelWorksheet sheet, int row, string header)
    {
        int column = ExcelTableHeaders.For(sheet).Optional(header);
        return column > 0 ? sheet.Cells[row, column].Text.Trim() : string.Empty;
    }

    static int ReadExecuteCount(ExcelWorksheet sheet, int row)
    {
        try { return TableValidator.ParseExecuteCount(ReadOptionalEffectText(sheet, row, "ExecuteCount")); }
        catch (TableValidationException error)
        {
            throw new TableValidationException($"{sheet.Name} 第{row}行: {error.Message}");
        }
    }

    static string ReadExecuteCountHandler(ExcelWorksheet sheet, int row)
    {
        try { return TableValidator.ParseExecuteCountHandler(ReadOptionalEffectText(sheet, row, "ExecuteCount")); }
        catch (TableValidationException error)
        {
            throw new TableValidationException($"{sheet.Name} 第{row}行: {error.Message}");
        }
    }

    static string ReadEffectParam2(ExcelWorksheet sheet, int row, string effectType)
    {
        string param = ExcelTableHeaders.For(sheet).Cell(row, "Param2").Text;
        if (effectType != "GainEnergy") return param;
        string mode = ReadOptionalEffectText(sheet, row, "EnergyGainMode");
        if (!string.IsNullOrWhiteSpace(mode) && !string.IsNullOrWhiteSpace(param)
            && TableValidator.NormalizeEnergyMode(mode) != TableValidator.NormalizeEnergyMode(param))
            throw new TableValidationException($"{sheet.Name} 第{row}行: EnergyGainMode 与 Param2 的充能模式冲突");
        return TableValidator.NormalizeEnergyMode(string.IsNullOrWhiteSpace(mode) ? param : mode);
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
            if (fileName.StartsWith("#", StringComparison.Ordinal) ||
                fileName.StartsWith("~$", StringComparison.Ordinal) ||
                fileName.StartsWith("0_", StringComparison.Ordinal))
                continue;
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
                        string cell = attrSheet.Cells[row, FindColumnByHeader(attrSheet, "CharacterID")].Text;
                        if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "NameID")].Text)) continue;
                        var data = new CharacterAttributesData
                        {
                            CharacterID = SafeGetInt(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "CharacterID")].Value),
                            NameID = attrSheet.Cells[row, FindColumnByHeader(attrSheet, "NameID")].Text,
                            Name = attrSheet.Cells[row, FindColumnByHeader(attrSheet, "Name")].Text,
                            Star = SafeGetInt(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "Star")].Value),
                            GrowthCurveID = SafeGetInt(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "GrowthCurveID")].Value),
                            WeaponType = CharacterMetadataParser.ParseWeaponType(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "WeaponType")].Text),
                            Element = CharacterMetadataParser.ParseElement(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "Element")].Text),
                            BaseHP = SafeGetFloat(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "BaseHP")].Value),
                            BaseATK = SafeGetFloat(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "BaseATK")].Value),
                            BaseDEF = SafeGetFloat(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "BaseDEF")].Value),
                            MaxEnergy = SafeGetInt(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "MaxEnergy")].Value),
                            HitWeight = SafeGetFloat(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "HitWeight")].Value),
                            NormalAttackID = attrSheet.Cells[row, FindColumnByHeader(attrSheet, "NormalAttackID")].Text,
                            HeavyAttackID = attrSheet.Cells[row, FindColumnByHeader(attrSheet, "HeavyAttackID")].Text,
                            ElementalSkillID = attrSheet.Cells[row, FindColumnByHeader(attrSheet, "ElementalSkillID")].Text,
                            ElementalBurstID = attrSheet.Cells[row, FindColumnByHeader(attrSheet, "ElementalBurstID")].Text,
                            BaseEM = SafeGetInt(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "BaseEM")].Value),
                            BaseRechargeRate = SafeGetFloat(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "BaseRechargeRate")].Value),
                            CritRate = SafeGetFloat(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "CritRate")].Value),
                            CritDMG = SafeGetFloat(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "CritDMG")].Value),
                            PyroRes = SafeGetFloat(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "PyroRes")].Value),
                            HydroRes = SafeGetFloat(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "HydroRes")].Value),
                            ElectroRes = SafeGetFloat(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "ElectroRes")].Value),
                            CryoRes = SafeGetFloat(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "CryoRes")].Value),
                            AnemoRes = SafeGetFloat(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "AnemoRes")].Value),
                            DendroRes = SafeGetFloat(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "DendroRes")].Value),
                            GeoRes = SafeGetFloat(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "GeoRes")].Value),
                            PhysicalRes = SafeGetFloat(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "PhysicalRes")].Value),
                            PyroDmgBonus = SafeGetFloat(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "PyroDmgBonus")].Value),
                            HydroDmgBonus = SafeGetFloat(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "HydroDmgBonus")].Value),
                            ElectroDmgBonus = SafeGetFloat(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "ElectroDmgBonus")].Value),
                            CryoDmgBonus = SafeGetFloat(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "CryoDmgBonus")].Value),
                            AnemoDmgBonus = SafeGetFloat(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "AnemoDmgBonus")].Value),
                            GeoDmgBonus = SafeGetFloat(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "GeoDmgBonus")].Value),
                            DendroDmgBonus = SafeGetFloat(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "DendroDmgBonus")].Value),
                            PhysicalDmgBonus = SafeGetFloat(attrSheet.Cells[row, FindColumnByHeader(attrSheet, "PhysicalDmgBonus")].Value)
                        };
                        if (data.CharacterID == 0) continue;
                        ApplyCharacterOverviewIdentity(data);
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
                        string cell = ascSheet.Cells[row, FindColumnByHeader(ascSheet, "AscensionLevel")].Text;
                        if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(ascSheet.Cells[row, FindColumnByHeader(ascSheet, "BaseHPFlat")].Text)) continue;
                        var data = new CharacterAscensionData
                        {
                            AscensionLevel = SafeGetInt(ascSheet.Cells[row, FindColumnByHeader(ascSheet, "AscensionLevel")].Value),
                            BaseHPFlat = SafeGetFloat(ascSheet.Cells[row, FindColumnByHeader(ascSheet, "BaseHPFlat")].Value),
                            BaseATKFlat = SafeGetFloat(ascSheet.Cells[row, FindColumnByHeader(ascSheet, "BaseATKFlat")].Value),
                            BaseDEFFlat = SafeGetFloat(ascSheet.Cells[row, FindColumnByHeader(ascSheet, "BaseDEFFlat")].Value),
                            HPBonus = SafeGetFloat(ascSheet.Cells[row, FindColumnByHeader(ascSheet, "HPBonus")].Value),
                            ATKBonus = SafeGetFloat(ascSheet.Cells[row, FindColumnByHeader(ascSheet, "ATKBonus")].Value),
                            DEFBonus = SafeGetFloat(ascSheet.Cells[row, FindColumnByHeader(ascSheet, "DEFBonus")].Value),
                            CritRate = SafeGetFloat(ascSheet.Cells[row, FindColumnByHeader(ascSheet, "CritRate")].Value),
                            CritDMG = SafeGetFloat(ascSheet.Cells[row, FindColumnByHeader(ascSheet, "CritDMG")].Value),
                            EM = SafeGetFloat(ascSheet.Cells[row, FindColumnByHeader(ascSheet, "EM")].Value),
                            EnergyRechargeRate = SafeGetFloat(ascSheet.Cells[row, FindColumnByHeader(ascSheet, "EnergyRechargeRate")].Value),
                            HealBonus = SafeGetFloat(ReadOptionalEffectText(ascSheet, row, "HealBonus")),
                            PyroDmgBonus = SafeGetFloat(ascSheet.Cells[row, FindColumnByHeader(ascSheet, "PyroDmgBonus")].Value),
                            HydroDmgBonus = SafeGetFloat(ascSheet.Cells[row, FindColumnByHeader(ascSheet, "HydroDmgBonus")].Value),
                            ElectroDmgBonus = SafeGetFloat(ascSheet.Cells[row, FindColumnByHeader(ascSheet, "ElectroDmgBonus")].Value),
                            CryoDmgBonus = SafeGetFloat(ascSheet.Cells[row, FindColumnByHeader(ascSheet, "CryoDmgBonus")].Value),
                            AnemoDmgBonus = SafeGetFloat(ascSheet.Cells[row, FindColumnByHeader(ascSheet, "AnemoDmgBonus")].Value),
                            GeoDmgBonus = SafeGetFloat(ascSheet.Cells[row, FindColumnByHeader(ascSheet, "GeoDmgBonus")].Value),
                            DendroDmgBonus = SafeGetFloat(ascSheet.Cells[row, FindColumnByHeader(ascSheet, "DendroDmgBonus")].Value),
                            PhysicalDmgBonus = SafeGetFloat(ascSheet.Cells[row, FindColumnByHeader(ascSheet, "PhysicalDmgBonus")].Value)
                        };
                        if (!CharacterAscensionDict.ContainsKey(workbookCharacterId))
                            CharacterAscensionDict[workbookCharacterId] = new List<CharacterAscensionData>();
                        CharacterAscensionDict[workbookCharacterId].Add(data);
                    }
                }

                var skillSheet = sheets["Skills"];
                if (skillSheet != null)
                {
                    int iconColumn = ExcelTableHeaders.For(skillSheet).Optional("Icon");
                    int descriptionColumn = FindColumnByHeader(skillSheet, "Description");
                    int startRow = GetDataStartRow(skillSheet);
                    int maxRow = GetSheetMaxRow(skillSheet);
                    int charId = 0;
                    for (int row = startRow; row <= maxRow; row++)
                    {
                        string cell = skillSheet.Cells[row, FindColumnByHeader(skillSheet, "SkillID")].Text;
                        if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(skillSheet.Cells[row, FindColumnByHeader(skillSheet, "SkillID2")].Text)) continue;
                        var data = new SkillMainData
                        {
                            SkillID = SafeGetInt(skillSheet.Cells[row, FindColumnByHeader(skillSheet, "SkillID")].Value),
                            SkillID2 = skillSheet.Cells[row, FindColumnByHeader(skillSheet, "SkillID2")].Text,
                            SkillName = skillSheet.Cells[row, FindColumnByHeader(skillSheet, "SkillName")].Text,
                            APCost = SafeGetInt(skillSheet.Cells[row, FindColumnByHeader(skillSheet, "APCost")].Value),
                            Cooldown = SafeGetFloat(skillSheet.Cells[row, FindColumnByHeader(skillSheet, "Cooldown")].Value),
                            EnergyUsed = SafeGetInt(skillSheet.Cells[row, FindColumnByHeader(skillSheet, "EnergyUsed")].Value),
                            MaxCharge = SafeGetInt(skillSheet.Cells[row, FindColumnByHeader(skillSheet, "MaxCharge")].Value),
                            InitialCharge = SafeGetInt(skillSheet.Cells[row, FindColumnByHeader(skillSheet, "InitialCharge")].Value),
                            SkillPhase = SafeGetInt(skillSheet.Cells[row, FindColumnByHeader(skillSheet, "SkillPhase")].Value),
                            ActionType = skillSheet.Cells[row, FindColumnByHeader(skillSheet, "ActionType")].Text,
                            Icon = iconColumn > 0 ? skillSheet.Cells[row, iconColumn].Text.Trim() : string.Empty,
                            Description = skillSheet.Cells[row, descriptionColumn].Text
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
                        string cell = seSheet.Cells[row, FindColumnByHeader(seSheet, "SkillEffectID2")].Text;
                        if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(seSheet.Cells[row, FindColumnByHeader(seSheet, "SkillEffectID")].Text)) continue;
                        var data = new SkillEffectData
                        {
                            AuthoredFields = ReadAuthoredEffectFields(seSheet, row),
                            SkillEffectID = SafeGetInt(seSheet.Cells[row, FindColumnByHeader(seSheet, "SkillEffectID")].Value),
                            SkillEffectID2 = seSheet.Cells[row, FindColumnByHeader(seSheet, "SkillEffectID2")].Text,
                            EffectIndex = SafeGetInt(seSheet.Cells[row, FindColumnByHeader(seSheet, "EffectIndex")].Value),
                            EffectType = seSheet.Cells[row, FindColumnByHeader(seSheet, "EffectType")].Text,
                            Element = seSheet.Cells[row, FindColumnByHeader(seSheet, "Element")].Text,
                            DamageType = seSheet.Cells[row, FindColumnByHeader(seSheet, "DamageType")].Text,
                            Duration = SafeGetInt(seSheet.Cells[row, FindColumnByHeader(seSheet, "Duration")].Value),
                            AddInPhase = SafeGetInt(seSheet.Cells[row, FindColumnByHeader(seSheet, "AddInPhase")].Value),
                            TriggerPhase = SafeGetInt(seSheet.Cells[row, FindColumnByHeader(seSheet, "TriggerPhase")].Value),
                            Param1 = seSheet.Cells[row, FindColumnByHeader(seSheet, "Param1")].Text,
                            Param2 = ReadEffectParam2(seSheet, row, seSheet.Cells[row, FindColumnByHeader(seSheet, "EffectType")].Text),
                            Param3 = seSheet.Cells[row, FindColumnByHeader(seSheet, "Param3")].Text,
                            TargetType = seSheet.Cells[row, FindColumnByHeader(seSheet, "TargetType")].Text,
                            TargetNumber = SafeGetInt(seSheet.Cells[row, FindColumnByHeader(seSheet, "TargetNumber")].Value),
                            TargetConsecutive = SafeGetInt(seSheet.Cells[row, FindColumnByHeader(seSheet, "TargetConsecutive")].Value),
                            TargetConsecutiveSet = !string.IsNullOrEmpty(seSheet.Cells[row, FindColumnByHeader(seSheet, "TargetConsecutive")].Text),
                            TargetOverride = seSheet.Cells[row, FindColumnByHeader(seSheet, "TargetOverride")].Text,
                            ScriptHook = seSheet.Cells[row, FindColumnByHeader(seSheet, "ScriptHook")].Text,
                            DamageHook = ReadOptionalEffectText(seSheet, row, "DamageHook"),
                            ExecuteCount = ReadExecuteCount(seSheet, row),
                            ExecuteCountHandler = ReadExecuteCountHandler(seSheet, row)
                        };
                        string validationSource = $"Characters/{Path.GetFileName(f)}/{seSheet.Name} "
                            + $"(SkillEffectID={data.SkillEffectID}, ID2={data.SkillEffectID2})";
                        TryValidate(validationSource, () => TableValidator.ValidateEffectRow("SkillsEffect", row,
                            data.EffectType, data.Element, data.Duration, data.Param2,
                            data.TargetType, data.TargetNumber, data.TargetConsecutive,
                            data.TargetOverride, isEnemy: false));
                        TryValidate(validationSource, () =>
                        {
                            TableValidator.ValidateDamageProperties("SkillsEffect", row,
                                data.EffectType, data.Element, data.Param1, data.Param2, data.Param3, data.DamageHook);
                            if (data.EffectType == "Damage") data.Element = DamageProperties.NormalizeElement(data.Element);
                        });
                        if (data.EffectType == "ApplyElement")
                            TryValidate(validationSource, () => TableValidator.ValidateApplyElement("SkillsEffect", row,
                                data.Element, data.Param1, data.Param2, data.Param3, data.DamageType, data.DamageHook));
                        if (data.EffectType == "Revive")
                            TryValidate(validationSource, () => TableValidator.ValidateRevive("SkillsEffect", row,
                                data.Param1, data.Param2, data.Param3, data.Element, data.DamageType, data.DamageHook, data.Duration, data.TargetType));
                        if (data.EffectType == "SwitchIn")
                            TryValidate(validationSource, () => TableValidator.ValidateSwitchIn("SkillsEffect", row,
                                data.Param1, data.Param3, data.DamageType, data.DamageHook, data.AddInPhase, data.TriggerPhase));
                        // ChangeControl 专属校验（2026-08-11）：技能效果表允许，填了Duration定时结束，没填=永久
                        if (data.EffectType == "ResetCooldown")
                            TryValidate(validationSource, () => TableValidator.ValidateResetCooldown("SkillsEffect", row, data.Param1));
                        if (data.EffectType == "ReduceCooldown")
                            TryValidate(validationSource, () => TableValidator.ValidateReduceCooldown("SkillsEffect", row, data.Param1, data.Param2));
                        if (data.EffectType == "BondOfLife")
                            TryValidate(validationSource, () => TableValidator.ValidateBondOfLife("SkillsEffect", row, data.Param1));
                        TryValidate(validationSource, () => TableValidator.ValidatePublicCountEffect(
                            "SkillsEffect", row, data.EffectType, data.Param1, data.Param2, data.TargetType));
                        if (data.EffectType == "ChangeControl")
                            TryValidate(validationSource, () => TableValidator.ValidateChangeControl("SkillsEffect", row,
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
                    // Hits fields inherit within the same ParamID group, never across effects.
                    int charId = 0;
                    int skillType = 0;
                    string paramID = string.Empty;
                    for (int row = startRow; row <= maxRow; row++)
                    {
                        string cid = slSheet.Cells[row, FindColumnByHeader(slSheet, "CharacterID")].Text;
                        string stype = slSheet.Cells[row, FindColumnByHeader(slSheet, "SkillType")].Text;
                        string slevel = slSheet.Cells[row, FindColumnByHeader(slSheet, "SkillLevel")].Text;
                        string rowParamID = slSheet.Cells[row, FindColumnByHeader(slSheet, "ParamID")].Text;
                        if (string.IsNullOrEmpty(cid) && string.IsNullOrEmpty(stype)
                            && string.IsNullOrEmpty(slevel) && string.IsNullOrEmpty(rowParamID)) continue;

                        if (!string.IsNullOrEmpty(cid))
                            charId = SafeGetInt(slSheet.Cells[row, FindColumnByHeader(slSheet, "CharacterID")].Value);
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
                            Hits1 = slSheet.Cells[row, FindColumnByHeader(slSheet, "Hits1")].Text,
                            Hits2 = slSheet.Cells[row, FindColumnByHeader(slSheet, "Hits2")].Text,
                            Hits3 = slSheet.Cells[row, FindColumnByHeader(slSheet, "Hits3")].Text,
                            Hits4 = slSheet.Cells[row, FindColumnByHeader(slSheet, "Hits4")].Text,
                            Hits5 = slSheet.Cells[row, FindColumnByHeader(slSheet, "Hits5")].Text,
                            Hits6 = slSheet.Cells[row, FindColumnByHeader(slSheet, "Hits6")].Text,
                            Hits7 = slSheet.Cells[row, FindColumnByHeader(slSheet, "Hits7")].Text
                        };
                        if (!SkillLevelDict.ContainsKey(key)) SkillLevelDict[key] = new Dictionary<int, SkillLevelData>();
                        SkillLevelData previous = null;
                        foreach (var entry in SkillLevelDict[key])
                            if (entry.Key < skillLevel && (previous == null || entry.Key > previous.SkillLevel))
                                previous = entry.Value;
                        if (previous != null)
                        {
                            data.Hits1 = HitDataParser.InheritLevelFields(data.Hits1, previous.Hits1);
                            data.Hits2 = HitDataParser.InheritLevelFields(data.Hits2, previous.Hits2);
                            data.Hits3 = HitDataParser.InheritLevelFields(data.Hits3, previous.Hits3);
                            data.Hits4 = HitDataParser.InheritLevelFields(data.Hits4, previous.Hits4);
                            data.Hits5 = HitDataParser.InheritLevelFields(data.Hits5, previous.Hits5);
                            data.Hits6 = HitDataParser.InheritLevelFields(data.Hits6, previous.Hits6);
                            data.Hits7 = HitDataParser.InheritLevelFields(data.Hits7, previous.Hits7);
                        }
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
                        string cell = conSheet.Cells[row, FindColumnByHeader(conSheet, "CharacterID")].Text;
                        if (!string.IsNullOrEmpty(cell)) charId = SafeGetInt(conSheet.Cells[row, FindColumnByHeader(conSheet, "CharacterID")].Value);
                        if (charId == 0) continue;
                        if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(conSheet.Cells[row, FindColumnByHeader(conSheet, "ConstellationIndex")].Text)) continue;
                        var data = new ConstellationData
                        {
                            CharacterID = charId,
                            ConstellationIndex = SafeGetInt(conSheet.Cells[row, FindColumnByHeader(conSheet, "ConstellationIndex")].Value),
                            ConstellationName = conSheet.Cells[row, FindColumnByHeader(conSheet, "ConstellationName")].Text,
                            Modifiers = conSheet.Cells[row, FindColumnByHeader(conSheet, "Modifiers")].Text,
                            Description = conSheet.Cells[row, FindColumnByHeader(conSheet, "Description")].Text
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
                        string cell = talSheet.Cells[row, FindColumnByHeader(talSheet, "CharacterID")].Text;
                        if (!string.IsNullOrEmpty(cell)) charId = SafeGetInt(talSheet.Cells[row, FindColumnByHeader(talSheet, "CharacterID")].Value);
                        if (charId == 0) continue;
                        if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(talSheet.Cells[row, FindColumnByHeader(talSheet, "TalentIndex")].Text)) continue;
                        var data = new TalentData
                        {
                            CharacterID = charId,
                            TalentIndex = SafeGetInt(talSheet.Cells[row, FindColumnByHeader(talSheet, "TalentIndex")].Value),
                            TalentName = talSheet.Cells[row, FindColumnByHeader(talSheet, "TalentName")].Text,
                            UnlockAfter = SafeGetInt(talSheet.Cells[row, FindColumnByHeader(talSheet, "UnlockAfter")].Value),
                            Modifiers = talSheet.Cells[row, FindColumnByHeader(talSheet, "Modifiers")].Text,
                            Description = talSheet.Cells[row, FindColumnByHeader(talSheet, "Description")].Text
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
                string cell = sheet.Cells[row, FindColumnByHeader(sheet, "Level")].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(sheet.Cells[row, FindColumnByHeader(sheet, "Multiplier")].Text)) continue;
                GrowthCurveDict[curveID][SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "Level")].Value)] = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "Multiplier")].Value);
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
