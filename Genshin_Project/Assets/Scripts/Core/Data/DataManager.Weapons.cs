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
                int weaponID = SafeGetInt(attributes.Cells[row, 1].Value);
                if (weaponID <= 0) continue;
                WeaponAttributesDict[weaponID] = new WeaponAttributesData
                {
                    WeaponID = weaponID,
                    WeaponName = attributes.Cells[row, 2].Text,
                    WeaponType = SafeGetInt(attributes.Cells[row, 3].Value),
                    Star = SafeGetInt(attributes.Cells[row, 4].Value),
                    SecondaryAttribute = attributes.Cells[row, 5].Text,
                    AscensionCurve = SafeGetInt(attributes.Cells[row, 6].Value),
                    BaseATK = SafeGetFloat(attributes.Cells[row, 7].Value),
                    BaseSecondaryAttribute = SafeGetFloat(attributes.Cells[row, 8].Value),
                    Description = attributes.Cells[row, 9].Text
                };
            }
        }

        var levelBonus = pkg.Workbook.Worksheets["LevelBonus"];
        if (levelBonus != null)
        {
            for (int row = GetDataStartRow(levelBonus); row <= GetSheetMaxRow(levelBonus); row++)
            {
                int weaponID = SafeGetInt(levelBonus.Cells[row, 1].Value);
                if (weaponID <= 0) continue;
                var data = new WeaponLevelBonusData { WeaponID = weaponID };
                for (int index = 0; index < 7; index++)
                {
                    data.ATKPerLevel[index] = SafeGetFloat(levelBonus.Cells[row, 2 + index].Value);
                    data.SecondaryPerLevel[index] = SafeGetFloat(levelBonus.Cells[row, 9 + index].Value);
                }
                WeaponLevelBonusDict[weaponID] = data;
            }
        }

        var weaponParam = pkg.Workbook.Worksheets["WeaponParam"];
        if (weaponParam != null)
        {
            for (int row = GetDataStartRow(weaponParam); row <= GetSheetMaxRow(weaponParam); row++)
            {
                int weaponID = SafeGetInt(weaponParam.Cells[row, 1].Value);
                int index = SafeGetInt(weaponParam.Cells[row, 2].Value);
                if (weaponID <= 0 || index <= 0) continue;
                if (!WeaponParamDict.TryGetValue(weaponID, out var byIndex))
                    WeaponParamDict[weaponID] = byIndex = new Dictionary<int, WeaponParamData>();
                byIndex[index] = new WeaponParamData
                {
                    WeaponID = weaponID, Index = index,
                    Param = SafeGetFloat(weaponParam.Cells[row, 3].Value),
                    RefineBonus = SafeGetFloat(weaponParam.Cells[row, 4].Value)
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
            int level = SafeGetInt(sheet.Cells[row, 1].Value);
            if (level > 0) target[level] = SafeGetFloat(sheet.Cells[row, 2].Value);
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
                int id = SafeGetInt(main.Cells[row, 1].Value);
                if (id <= 0) continue;
                string id2 = WeaponStatusKey(id);
                if (StatusMainDict.ContainsKey(id2))
                    throw new InvalidDataException($"[StatusWeapon_Main] row {row}: duplicate runtime key {id2}");
                StatusMainDict[id2] = new StatusMainData
                {
                    StatusID = id, StatusID2 = id2,
                    StatusName = main.Cells[row, 2].Text, StatusType = main.Cells[row, 3].Text,
                    Display = SafeGetInt(main.Cells[row, 4].Value), Description = main.Cells[row, 5].Text,
                    MultiplierPart1 = main.Cells[row, 6].Text, MultiplierPart2 = main.Cells[row, 7].Text,
                    MultiplierPart3 = main.Cells[row, 8].Text, ApplyDamageType = main.Cells[row, 9].Text,
                    ApplyReactionType = main.Cells[row, 10].Text, ApplyElementType = main.Cells[row, 11].Text,
                    ApplyString = main.Cells[row, 12].Text, ScriptHook = main.Cells[row, 13].Text,
                    MaxCount = SafeGetInt(main.Cells[row, 14].Value), MaxStack = SafeGetInt(main.Cells[row, 15].Value),
                    WhenMax = main.Cells[row, 16].Text
                };
            }
        }

        var effects = pkg.Workbook.Worksheets["StatusWeapon_Effect"];
        if (effects != null)
        {
            for (int row = GetDataStartRow(effects); row <= GetSheetMaxRow(effects); row++)
            {
                int id = SafeGetInt(effects.Cells[row, 1].Value);
                if (id <= 0) continue;
                string id2 = WeaponEffectKey(id);
                if (StatusEffectDict.ContainsKey(id2))
                    throw new InvalidDataException($"[StatusWeapon_Effect] row {row}: duplicate runtime key {id2}");
                string effectType = effects.Cells[row, 3].Text;
                StatusEffectDict[id2] = new StatusEffectData
                {
                    StatusEffectID = id, StatusEffectID2 = id2,
                    EffectIndex = SafeGetInt(effects.Cells[row, 2].Value), EffectType = effectType,
                    Element = effects.Cells[row, 4].Text, DamageType = effects.Cells[row, 5].Text,
                    Duration = SafeGetInt(effects.Cells[row, 6].Value), AddInPhase = SafeGetInt(effects.Cells[row, 7].Value),
                    TriggerPhase = SafeGetInt(effects.Cells[row, 8].Value),
                    Param1 = NormalizeWeaponEffectParam(effectType, effects.Cells[row, 9].Text),
                    Param2 = effects.Cells[row, 10].Text, Param3 = effects.Cells[row, 11].Text,
                    TargetType = effects.Cells[row, 12].Text, TargetSelect = effects.Cells[row, 13].Text,
                    TargetConsecutive = SafeGetInt(effects.Cells[row, 14].Value),
                    TargetOverride = effects.Cells[row, 15].Text, ScriptHook = effects.Cells[row, 16].Text
                };
            }
        }

        var actions = pkg.Workbook.Worksheets["StatusWeapon_Action"];
        if (actions != null)
        {
            for (int row = GetDataStartRow(actions); row <= GetSheetMaxRow(actions); row++)
            {
                int id = SafeGetInt(actions.Cells[row, 1].Value);
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
            for (int row = GetDataStartRow(initiate); row <= GetSheetMaxRow(initiate); row++)
            {
                int weaponID = SafeGetInt(initiate.Cells[row, 1].Value);
                if (weaponID <= 0) continue;
                var statuses = new List<string>();
                for (int col = 2; col <= 6; col++)
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
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
            ? WeaponStatusKey(id) : value;
    }

    static string NormalizeWeaponEffectList(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        string[] parts = value.Split(',');
        for (int index = 0; index < parts.Length; index++)
        {
            string part = parts[index].Trim();
            parts[index] = int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                ? WeaponEffectKey(id) : part;
        }
        return string.Join(",", parts);
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
