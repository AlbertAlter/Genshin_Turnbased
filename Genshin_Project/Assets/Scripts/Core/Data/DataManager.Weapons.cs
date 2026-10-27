using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

public partial class DataManager
{
    private static readonly int[] WeaponBreakLevels = { 20, 40, 50, 60, 70, 80 };

    void LoadWeaponAttributes()
    {
        string path = Path.Combine(ChartsPath, "WeaponAttributes.xlsx");
        if (!File.Exists(path)) { Debug.LogWarning("WeaponAttributes.xlsx not found"); return; }
        using var pkg = new ExcelPackage(new FileInfo(path));

        var attributes = pkg.Workbook.Worksheets["Attributes"];
        if (attributes != null)
        {
            for (int row = GetDataStartRow(attributes); row <= GetSheetMaxRow(attributes); row++)
            {
                int weaponID = SafeGetInt(attributes.Cells[row, FindColumnByHeader(attributes, "WeaponID")].Value);
                if (weaponID <= 0) continue;
                WeaponAttributesDict[weaponID] = new WeaponAttributesData
                {
                    WeaponID = weaponID,
                    WeaponName = attributes.Cells[row, FindColumnByHeader(attributes, "WeaponName")].Text,
                    WeaponType = SafeGetInt(attributes.Cells[row, FindColumnByHeader(attributes, "WeaponType")].Value),
                    Star = SafeGetInt(attributes.Cells[row, FindColumnByHeader(attributes, "Star")].Value),
                    SecondaryAttribute = attributes.Cells[row, FindColumnByHeader(attributes, "SecondaryAttribute")].Text,
                    AscensionCurve = SafeGetInt(attributes.Cells[row, FindColumnByHeader(attributes, "AscensionCurve")].Value),
                    BaseATK = SafeGetFloat(attributes.Cells[row, FindColumnByHeader(attributes, "BaseATK")].Value),
                    BaseSecondaryAttribute = SafeGetFloat(attributes.Cells[row, FindColumnByHeader(attributes, "BaseSecondaryAttribute")].Value),
                    Description = attributes.Cells[row, FindColumnByHeader(attributes, "Description")].Text
                };
            }
        }

        var levelBonus = pkg.Workbook.Worksheets["LevelBonus"];
        if (levelBonus != null)
        {
            for (int row = GetDataStartRow(levelBonus); row <= GetSheetMaxRow(levelBonus); row++)
            {
                int weaponID = SafeGetInt(levelBonus.Cells[row, FindColumnByHeader(levelBonus, "WeaponID")].Value);
                if (weaponID <= 0) continue;
                var data = new WeaponLevelBonusData { WeaponID = weaponID };
                for (int index = 0; index < 7; index++)
                {
                    data.ATKPerLevel[index] = SafeGetFloat(levelBonus.Cells[row, FindColumnByHeader(levelBonus, "M" + (index == 0 ? 1 : WeaponBreakLevels[index - 1]))].Value);
                    data.SecondaryPerLevel[index] = SafeGetFloat(levelBonus.Cells[row, FindColumnByHeader(levelBonus, "S" + (index == 0 ? 1 : WeaponBreakLevels[index - 1]))].Value);
                }
                WeaponLevelBonusDict[weaponID] = data;
            }
        }

        var weaponParam = pkg.Workbook.Worksheets["WeaponParam"];
        if (weaponParam != null)
        {
            for (int row = GetDataStartRow(weaponParam); row <= GetSheetMaxRow(weaponParam); row++)
            {
                int weaponID = SafeGetInt(weaponParam.Cells[row, FindColumnByHeader(weaponParam, "WeaponID")].Value);
                int index = SafeGetInt(weaponParam.Cells[row, FindColumnByHeader(weaponParam, "index")].Value);
                if (weaponID <= 0 || index <= 0) continue;
                if (!WeaponParamDict.TryGetValue(weaponID, out var byIndex))
                    WeaponParamDict[weaponID] = byIndex = new Dictionary<int, WeaponParamData>();
                byIndex[index] = new WeaponParamData
                {
                    WeaponID = weaponID, Index = index,
                    Param = SafeGetFloat(weaponParam.Cells[row, FindColumnByHeader(weaponParam, "param")].Value),
                    RefineBonus = SafeGetFloat(weaponParam.Cells[row, FindColumnByHeader(weaponParam, "refinebonus")].Value)
                };
            }
        }

        LoadWeaponAscensionCurve(pkg, "AscensionCurve_3", WeaponAscensionCurve3);
        LoadWeaponAscensionCurve(pkg, "AscensionCurve_4", WeaponAscensionCurve4);
        LoadWeaponAscensionCurve(pkg, "AscensionCurve_5", WeaponAscensionCurve5);
        LogManager.Log(LogCategory.Data, $"WeaponAttributes loaded, {WeaponAttributesDict.Count} weapons");
    }

    void LoadWeaponAscensionCurve(ExcelPackage pkg, string sheetName, Dictionary<int, float> target)
    {
        var sheet = pkg.Workbook.Worksheets[sheetName];
        if (sheet == null) return;
        for (int row = GetDataStartRow(sheet); row <= GetSheetMaxRow(sheet); row++)
        {
            int level = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "Level")].Value);
            if (level > 0) target[level] = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "BaseWeaponATKFlat")].Value);
        }
    }

    void LoadWeaponStatusData()
    {
        string path = Path.Combine(ChartsPath, "StatusData_Weapon.xlsx");
        if (!File.Exists(path)) { Debug.LogWarning("StatusData_Weapon.xlsx not found"); return; }
        using var pkg = new ExcelPackage(new FileInfo(path));

        var main = pkg.Workbook.Worksheets["StatusWeapon_Main"];
        if (main != null)
        {
            for (int row = GetDataStartRow(main); row <= GetSheetMaxRow(main); row++)
            {
                int id = SafeGetInt(main.Cells[row, FindColumnByHeader(main, "StatusID")].Value);
                if (id <= 0) continue;
                string id2 = WeaponStatusKey(id);
                if (StatusMainDict.ContainsKey(id2))
                    throw new InvalidDataException($"[StatusWeapon_Main] row {row}: duplicate runtime key {id2}");
                StatusMainDict[id2] = new StatusMainData
                {
                    StatusID = id, StatusID2 = id2,
                    StatusName = main.Cells[row, FindColumnByHeader(main, "StatusName")].Text, StatusType = main.Cells[row, FindColumnByHeader(main, "StatusType")].Text,
                    Display = SafeGetInt(main.Cells[row, FindColumnByHeader(main, "Display")].Value), Description = main.Cells[row, FindColumnByHeader(main, "Description")].Text,
                    MultiplierPart1 = main.Cells[row, FindColumnByHeader(main, "MultiplierPart1")].Text, MultiplierPart2 = main.Cells[row, FindColumnByHeader(main, "MultiplierPart2")].Text,
                    MultiplierPart3 = main.Cells[row, FindColumnByHeader(main, "MultiplierPart3")].Text, ApplyDamageType = main.Cells[row, FindColumnByHeader(main, "ApplyDamageType")].Text,
                    ApplyReactionType = main.Cells[row, FindColumnByHeader(main, "ApplyReactionType")].Text, ApplyElementType = main.Cells[row, FindColumnByHeader(main, "ApplyElementType")].Text,
                    ApplyString = main.Cells[row, FindColumnByHeader(main, "ApplyString")].Text, ScriptHook = main.Cells[row, FindColumnByHeader(main, "ScriptHook")].Text,
                    MaxCount = SafeGetInt(main.Cells[row, FindColumnByHeader(main, "MaxCount")].Value), MaxStack = SafeGetInt(main.Cells[row, FindColumnByHeader(main, "MaxStack")].Value),
                    WhenMax = main.Cells[row, FindColumnByHeader(main, "WhenMax")].Text
                };
            }
        }

        var effects = pkg.Workbook.Worksheets["StatusWeapon_Effect"];
        if (effects != null)
        {
            for (int row = GetDataStartRow(effects); row <= GetSheetMaxRow(effects); row++)
            {
                int id = SafeGetInt(effects.Cells[row, FindColumnByHeader(effects, "StatusEffectID")].Value);
                if (id <= 0) continue;
                string id2 = WeaponEffectKey(id);
                if (StatusEffectDict.ContainsKey(id2))
                    throw new InvalidDataException($"[StatusWeapon_Effect] row {row}: duplicate runtime key {id2}");
                string effectType = effects.Cells[row, FindColumnByHeader(effects, "EffectType")].Text;
                TryValidate("StatusWeapon_Effect", () => TableValidator.ValidateDamageProperties("StatusWeapon_Effect", row,
                    effectType, effects.Cells[row, FindColumnByHeader(effects, "Element")].Text,
                    effects.Cells[row, FindColumnByHeader(effects, "Param1")].Text,
                    effects.Cells[row, FindColumnByHeader(effects, "Param2")].Text,
                    effects.Cells[row, FindColumnByHeader(effects, "Param3")].Text,
                    ReadOptionalEffectText(effects, row, "DamageHook")));
                if (effectType == "APCost")
                    TryValidate("StatusWeapon_Effect", () => TableValidator.ValidateAPCost(
                        effects.Cells[row, FindColumnByHeader(effects, "Param1")].Text,
                        effects.Cells[row, FindColumnByHeader(effects, "Param2")].Text,
                        id, ReadOptionalEffectText(effects, row, "DamageHook")));
                if (effectType == "ApplyElement")
                {
                    TryValidate("StatusWeapon_Effect", () => TableValidator.ValidateEffectRow("StatusWeapon_Effect", row,
                        effectType, effects.Cells[row, FindColumnByHeader(effects, "Element")].Text,
                        SafeGetInt(effects.Cells[row, FindColumnByHeader(effects, "Duration")].Value),
                        effects.Cells[row, FindColumnByHeader(effects, "Param2")].Text,
                        effects.Cells[row, FindColumnByHeader(effects, "TargetType")].Text, 0,
                        SafeGetInt(effects.Cells[row, FindColumnByHeader(effects, "TargetConsecutive")].Value),
                        effects.Cells[row, FindColumnByHeader(effects, "TargetOverride")].Text,
                        false, checkTargetNumber: false,
                        targetSelect: effects.Cells[row, FindColumnByHeader(effects, "TargetSelect")].Text, isStatusEffectTable: true));
                    TryValidate("StatusWeapon_Effect", () => TableValidator.ValidateApplyElement("StatusWeapon_Effect", row,
                        effects.Cells[row, FindColumnByHeader(effects, "Element")].Text,
                        effects.Cells[row, FindColumnByHeader(effects, "Param1")].Text,
                        effects.Cells[row, FindColumnByHeader(effects, "Param2")].Text,
                        effects.Cells[row, FindColumnByHeader(effects, "Param3")].Text,
                        effects.Cells[row, FindColumnByHeader(effects, "DamageType")].Text,
                        ReadOptionalEffectText(effects, row, "DamageHook")));
                }
                if (effectType == "SwitchIn")
                {
                    TryValidate("StatusWeapon_Effect", () => TableValidator.ValidateEffectRow("StatusWeapon_Effect", row,
                        effectType, effects.Cells[row, FindColumnByHeader(effects, "Element")].Text,
                        SafeGetInt(effects.Cells[row, FindColumnByHeader(effects, "Duration")].Value),
                        effects.Cells[row, FindColumnByHeader(effects, "Param2")].Text,
                        effects.Cells[row, FindColumnByHeader(effects, "TargetType")].Text, 0,
                        SafeGetInt(effects.Cells[row, FindColumnByHeader(effects, "TargetConsecutive")].Value),
                        effects.Cells[row, FindColumnByHeader(effects, "TargetOverride")].Text,
                        false, checkTargetNumber: false,
                        targetSelect: effects.Cells[row, FindColumnByHeader(effects, "TargetSelect")].Text, isStatusEffectTable: true));
                    TryValidate("StatusWeapon_Effect", () => TableValidator.ValidateSwitchIn("StatusWeapon_Effect", row,
                        effects.Cells[row, FindColumnByHeader(effects, "Param1")].Text,
                        effects.Cells[row, FindColumnByHeader(effects, "Param3")].Text,
                        effects.Cells[row, FindColumnByHeader(effects, "DamageType")].Text,
                        ReadOptionalEffectText(effects, row, "DamageHook"),
                        SafeGetInt(effects.Cells[row, FindColumnByHeader(effects, "AddInPhase")].Value),
                        SafeGetInt(effects.Cells[row, FindColumnByHeader(effects, "TriggerPhase")].Value)));
                }
                if (effectType == "ResetCooldown")
                    TryValidate("StatusWeapon_Effect", () => TableValidator.ValidateResetCooldown(
                        "StatusWeapon_Effect", row, effects.Cells[row, FindColumnByHeader(effects, "Param1")].Text));
                if (effectType == "ReduceCooldown")
                    TryValidate("StatusWeapon_Effect", () => TableValidator.ValidateReduceCooldown(
                        "StatusWeapon_Effect", row,
                        effects.Cells[row, FindColumnByHeader(effects, "Param1")].Text,
                        effects.Cells[row, FindColumnByHeader(effects, "Param2")].Text));
                if (effectType == "BondOfLife")
                    TryValidate("StatusWeapon_Effect", () => TableValidator.ValidateBondOfLife(
                        "StatusWeapon_Effect", row, effects.Cells[row, FindColumnByHeader(effects, "Param1")].Text));
                TryValidate("StatusWeapon_Effect", () => TableValidator.ValidatePublicCountEffect(
                    "StatusWeapon_Effect", row, effectType,
                    effects.Cells[row, FindColumnByHeader(effects, "Param1")].Text,
                    effects.Cells[row, FindColumnByHeader(effects, "Param2")].Text,
                    effects.Cells[row, FindColumnByHeader(effects, "TargetType")].Text));
                StatusEffectDict[id2] = new StatusEffectData
                {
                    AuthoredFields = ReadAuthoredEffectFields(effects, row),
                    StatusEffectID = id, StatusEffectID2 = id2,
                    EffectIndex = SafeGetInt(effects.Cells[row, FindColumnByHeader(effects, "EffectIndex")].Value), EffectType = effectType,
                    Element = effects.Cells[row, FindColumnByHeader(effects, "Element")].Text, DamageType = effects.Cells[row, FindColumnByHeader(effects, "DamageType")].Text,
                    Duration = SafeGetInt(effects.Cells[row, FindColumnByHeader(effects, "Duration")].Value), AddInPhase = SafeGetInt(effects.Cells[row, FindColumnByHeader(effects, "AddInPhase")].Value),
                    TriggerPhase = SafeGetInt(effects.Cells[row, FindColumnByHeader(effects, "TriggerPhase")].Value),
                    Param1 = NormalizeWeaponEffectParam(effectType, effects.Cells[row, FindColumnByHeader(effects, "Param1")].Text),
                    Param2 = ReadEffectParam2(effects, row, effects.Cells[row, FindColumnByHeader(effects, "EffectType")].Text), Param3 = effects.Cells[row, FindColumnByHeader(effects, "Param3")].Text,
                    TargetType = effects.Cells[row, FindColumnByHeader(effects, "TargetType")].Text, TargetSelect = effects.Cells[row, FindColumnByHeader(effects, "TargetSelect")].Text,
                    TargetConsecutive = SafeGetInt(effects.Cells[row, FindColumnByHeader(effects, "TargetConsecutive")].Value),
                    TargetOverride = effects.Cells[row, FindColumnByHeader(effects, "TargetOverride")].Text, ScriptHook = effects.Cells[row, FindColumnByHeader(effects, "ScriptHook")].Text,
                    DamageHook = ReadOptionalEffectText(effects, row, "DamageHook"),
                    ExecuteCount = ReadExecuteCount(effects, row),
                    ExecuteCountHandler = ReadExecuteCountHandler(effects, row)
                };
            }
        }

        var actions = pkg.Workbook.Worksheets["StatusWeapon_Action"];
        if (actions != null)
        {
            for (int row = GetDataStartRow(actions); row <= GetSheetMaxRow(actions); row++)
            {
                int id = SafeGetInt(actions.Cells[row, FindColumnByHeader(actions, "StatusID")].Value);
                if (id <= 0) continue;
                string id2 = WeaponStatusKey(id);
                var action = ReadStatusAction(actions, row, false, "StatusData_Weapon/StatusWeapon_Action");
                action.StatusID = id; action.StatusID2 = id2;
                action.Param1 = NormalizeWeaponEffectList(action.Param1);
                if (!StatusActionDict.TryGetValue(id2, out var list))
                    StatusActionDict[id2] = list = new List<StatusActionData>();
                list.Add(action);
            }
        }

        var initiate = pkg.Workbook.Worksheets["Weapon_Initiate"];
        if (initiate != null)
        {
            var initiateColumns = ExcelTableHeaders.For(initiate).NumberedColumns("InitiateStatus");
            for (int row = GetDataStartRow(initiate); row <= GetSheetMaxRow(initiate); row++)
            {
                int weaponID = SafeGetInt(initiate.Cells[row, FindColumnByHeader(initiate, "WeaponID")].Value);
                if (weaponID <= 0) continue;
                var statuses = new List<string>();
                foreach (int col in initiateColumns)
                {
                    int statusID = SafeGetInt(initiate.Cells[row, col].Value);
                    if (statusID > 0) statuses.Add(WeaponStatusKey(statusID));
                }
                WeaponInitiateStatusDict[weaponID] = statuses;
            }
        }
        LogManager.Log(LogCategory.Data, $"Weapon statuses loaded, {WeaponInitiateStatusDict.Count} initiate rows");
    }

    public static string WeaponStatusKey(int statusID) => $"ST_Weapon_{statusID}";
    public static string WeaponEffectKey(int effectID) => $"STE_Weapon_{effectID}";

    static string NormalizeWeaponEffectParam(string effectType, string value)
    {
        if (effectType != "ApplyStatus" && effectType != "BindStatus" && effectType != "RemoveStatus") return value;
        value = CountStatusSystem.ResolvePublicStatusReference(value);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
            ? WeaponStatusKey(id) : value;
    }

    static string NormalizeWeaponEffectList(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        string[] parts = value.Split(';');
        for (int index = 0; index < parts.Length; index++)
        {
            string part = parts[index].Trim();
            parts[index] = int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                ? WeaponEffectKey(id) : part;
        }
        return string.Join(";", parts);
    }

    public float ResolveWeaponParam(int weaponID, int index, int refinement)
    {
        if (!WeaponParamDict.TryGetValue(weaponID, out var byIndex) || !byIndex.TryGetValue(index, out var data)) return 0f;
        int rank = Mathf.Clamp(refinement, 1, 5);
        return data.Param + (rank - 1) * data.RefineBonus;
    }

    public WeaponRuntimeContext CreateWeaponContext(int weaponID, int refinement, BattleEntity equipper)
    {
        var context = new WeaponRuntimeContext
        {
            WeaponID = weaponID, Refinement = Mathf.Clamp(refinement, 1, 5), Equipper = equipper,
            SourceInstanceID = $"Weapon:{weaponID}:{Guid.NewGuid():N}"
        };
        if (WeaponParamDict.TryGetValue(weaponID, out var byIndex))
            foreach (int index in byIndex.Keys)
                context.ResolvedParams[index] = ResolveWeaponParam(weaponID, index, context.Refinement);
        return context;
    }

    public WeaponPanel GetWeaponPanel(int weaponID, int level, int ascension)
    {
        if (!WeaponAttributesDict.TryGetValue(weaponID, out var attr)) return null;
        int clampedLevel = Mathf.Clamp(level, 1, 90);
        float attack = attr.BaseATK, secondary = attr.BaseSecondaryAttribute;
        if (WeaponLevelBonusDict.TryGetValue(weaponID, out var bonus))
        {
            for (int gainedLevel = 2; gainedLevel <= clampedLevel; gainedLevel++)
            {
                int band = WeaponLevelBand(gainedLevel);
                attack += bonus.ATKPerLevel[band];
                secondary += bonus.SecondaryPerLevel[band];
            }
        }
        Dictionary<int, float> curve = attr.AscensionCurve == 5 ? WeaponAscensionCurve5
            : attr.AscensionCurve == 4 ? WeaponAscensionCurve4 : WeaponAscensionCurve3;
        int count = Mathf.Clamp(ascension, 0, WeaponBreakLevels.Length);
        for (int index = 0; index < count; index++)
            if (clampedLevel >= WeaponBreakLevels[index] && curve.TryGetValue(WeaponBreakLevels[index], out float flat)) attack += flat;
        return new WeaponPanel { BaseATK = attack, SecondaryAttribute = attr.SecondaryAttribute, SecondaryValue = secondary };
    }

    static int WeaponLevelBand(int level)
    {
        if (level <= 20) return 0; if (level <= 40) return 1; if (level <= 50) return 2;
        if (level <= 60) return 3; if (level <= 70) return 4; if (level <= 80) return 5; return 6;
    }
}
