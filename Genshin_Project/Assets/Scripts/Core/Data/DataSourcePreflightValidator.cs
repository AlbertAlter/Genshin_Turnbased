using System;
using System.Collections.Generic;
using System.IO;
using OfficeOpenXml;

/// <summary>统一声明正式工作簿及必需子表。启动只预查路径，打开后在同一工作簿上检查子表。</summary>
public static class DataSourcePreflightValidator
{
    private static readonly Dictionary<string, string[]> RequiredWorkbooks = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["lang.xlsx"] = new[] { "lang" },
        ["Characters.xlsx"] = new[] { "Sheet1" },
        ["Character_Growth_Curve.xlsx"] = new[] { "GrowthCurve_1", "GrowthCurve_2" },
        ["WeaponAttributes.xlsx"] = new[] { "Attributes", "LevelBonus", "WeaponParam", "AscensionCurve_3", "AscensionCurve_4", "AscensionCurve_5" },
        ["StatusData_Weapon.xlsx"] = new[] { "StatusWeapon_Main", "StatusWeapon_Action", "StatusWeapon_Effect", "Weapon_Initiate", "SpecialParam_Desc" },
        ["EnemyAttributes.xlsx"] = new[] { "Coeff", "Base_HP", "Base_ATK1", "Base_ATK2" },
        ["ReactionLevelCoefficient.xlsx"] = new[] { "Sheet1" },
        ["EnemyShield.xlsx"] = new[] { "Type1" },
        ["TeamBonus.xlsx"] = TeamBonusTableLoader.SheetNames
    };

    private static readonly Dictionary<string, string[]> OptionalWorkbooks = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["ElementAuraMotion.xlsx"] = new[] { "Parameters", "说明" },
        ["HealEffects.xlsx"] = new[] { "Parameters", "说明" },
        ["CardBottomStatus.xlsx"] = new[] { "Parameters", "ByStatusID", "说明" },
        ["StatusUI.xlsx"] = new[] { CardFaceStatusTable.IdSheet, CardFaceStatusTable.TypeSheet, "说明" },
        ["LisaConductive.xlsx"] = new[] { "Parameters", "说明" },
        ["CardAppearanceSwitch.xlsx"] = new[] { "Character", "Enemy", "说明" },
        ["lang_StatusIcon.xlsx"] = new[] { "lang_statusiconinfo", "lang_statusicon_weapon", "lang_statusicon_artifact" },
        ["lang_UINames.xlsx"] = new[] { "lang_ui_character", "lang_ui_enemy", "lang_ui_weapon", "lang_ui_artifact",
            "lang_ui_skill", "lang_ui_talent", "lang_ui_constellation" }
    };

    private static readonly string[] StatusSheets = { "StatusData_Main", "Status_Action", "Status_Effect", "Status_Attributes" };

    private static readonly Dictionary<string, string[]> PackageSheets = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Characters"] = new[] { "Attributes", "Skills", "SkillsEffect", "SkillLevel", "Talent", "Constellation", "Ascension" },
        ["CharacterStatus"] = StatusSheets,
        ["Enemies"] = new[] { "Enemy_Main", "EnemySkill_Main", "EnemySkill_Effect", "EnemySkill_Param", "EnemyAI", "EnemyAI_Rule", "BattleInitiate" },
        ["EnemyStatus"] = StatusSheets
    };

    public static IReadOnlyList<string> Validate(string dataRoot) => Validate(dataRoot, checkSheets: true);

    public static IReadOnlyList<string> ValidatePublication(string dataRoot) => Validate(dataRoot, checkSheets: true, checkOtherWorkbooks: true);

    public static IReadOnlyList<string> ValidatePresence(string dataRoot) => Validate(dataRoot, checkSheets: false);

    public static bool IsPublishedWorkbook(string path)
    {
        string name = Path.GetFileName(path);
        return !name.StartsWith("~$", StringComparison.Ordinal)
            && !name.StartsWith("#", StringComparison.Ordinal)
            && !name.StartsWith("0_", StringComparison.Ordinal);
    }

    public static bool UsesEnemyPackages(string dataRoot)
    {
        string root = Path.Combine(dataRoot, "Enemies");
        if (!Directory.Exists(root)) return false;
        foreach (string file in Directory.EnumerateFiles(root, "*.xlsx"))
            if (IsPublishedWorkbook(file)) return true;
        return false;
    }

    internal static void RequireSheets(ExcelPackage package, string source)
    {
        var errors = new List<string>();
        CollectMissingSheets(package, source, errors);
        if (errors.Count > 0) throw new DataLoadException("配表工作簿结构校验失败", errors);
    }

    private static IReadOnlyList<string> Validate(string dataRoot, bool checkSheets, bool checkOtherWorkbooks = false)
    {
        var errors = new List<string>();
        if (!Directory.Exists(dataRoot))
        {
            errors.Add($"数据目录不存在：{dataRoot}");
            return errors;
        }

        var checkedPaths = checkOtherWorkbooks ? new HashSet<string>(StringComparer.Ordinal) : null;
        void Check(string path, string source)
        {
            checkedPaths?.Add(path);
            ValidateWorkbook(path, source, checkSheets, errors);
        }

        foreach (string name in RequiredWorkbooks.Keys)
        {
            string path = name == "lang.xlsx" ? DataManager.GetLangWorkbookPath(dataRoot) : Path.Combine(dataRoot, name);
            Check(path, name);
        }
        if (checkSheets)
        {
            foreach (string path in new[] { DataManager.GetStatusIconWorkbookPath(dataRoot), DataManager.GetUiNamesWorkbookPath(dataRoot) })
                if (File.Exists(path)) Check(path, "lang/" + Path.GetFileName(path));
        }

        foreach (string folder in PackageSheets.Keys)
        {
            string root = Path.Combine(dataRoot, folder);
            if (!Directory.Exists(root))
            {
                errors.Add($"缺少 {folder} 配表目录");
                continue;
            }
            bool found = false;
            foreach (string file in Directory.EnumerateFiles(root, "*.xlsx"))
            {
                if (!IsPublishedWorkbook(file)) continue;
                found = true;
                if (checkSheets) Check(file, folder + "/" + Path.GetFileName(file));
                else break;
            }
            if (!found) errors.Add($"{folder} 目录没有正式工作簿。");
        }
        if (checkedPaths != null)
            foreach (string file in Directory.EnumerateFiles(dataRoot, "*.xlsx", SearchOption.AllDirectories))
                if (!checkedPaths.Contains(file))
                    ValidateWorkbook(file, file.Substring(dataRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), true, errors, false);
        return errors;
    }

    private static void ValidateWorkbook(string path, string source, bool checkSheets, List<string> errors, bool requireKnownSheets = true)
    {
        if (!File.Exists(path))
        {
            errors.Add($"缺少工作簿：{source}");
            return;
        }
        if (!checkSheets) return;
        try
        {
            using var stream = File.OpenRead(path);
            using var package = new ExcelPackage(stream);
            if (package.Workbook.Worksheets.Count == 0) errors.Add($"{source} 没有工作表");
            if (requireKnownSheets) CollectMissingSheets(package, source, errors);
        }
        catch (Exception exception)
        {
            errors.Add($"无法打开 {source}：{exception.Message}");
        }
    }

    public static void CollectMissingSheets(ExcelPackage package, string source, List<string> errors)
    {
        string name = Path.GetFileName(source);
        string folder = Path.GetDirectoryName(source);
        if ((string.IsNullOrEmpty(folder) || !PackageSheets.TryGetValue(folder, out var required))
            && !RequiredWorkbooks.TryGetValue(name, out required)
            && !OptionalWorkbooks.TryGetValue(name, out required)) return;

        foreach (string sheet in required)
            if (package.Workbook.Worksheets[sheet] == null)
                errors.Add($"{source} 缺少工作表：{sheet}");
    }
}
