using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OfficeOpenXml;

/// <summary>加载前检查必需工作簿和 Sheet。旧版 StageConfig.xlsx 不属于必需数据源。</summary>
public static class DataSourcePreflightValidator
{
    private static readonly string[] CharacterSheets =
    {
        "Attributes", "Skills", "SkillsEffect", "SkillLevel", "Talent", "Constellation", "Ascension"
    };

    public static IReadOnlyList<string> Validate(string dataRoot)
    {
        var errors = new List<string>();

        if (!Directory.Exists(dataRoot))
        {
            errors.Add($"数据目录不存在：{dataRoot}");
            return errors;
        }

        ValidateWorkbook(dataRoot, "Characters.xlsx", errors, "Sheet1");
        ValidateWorkbook(dataRoot, "Character_Growth_Curve.xlsx", errors, "GrowthCurve_1", "GrowthCurve_2");
        ValidateWorkbook(dataRoot, "WeaponAttributes.xlsx", errors,
            "Attributes", "LevelBonus", "WeaponParam", "AscensionCurve_3", "AscensionCurve_4", "AscensionCurve_5");
        ValidateWorkbook(dataRoot, "StatusData.xlsx", errors,
            "StatusData_Main", "Status_Effect", "Status_Action", "Status_Attributes");
        ValidateWorkbook(dataRoot, "StatusData_Enemy.xlsx", errors,
            "StatusData_Main_Enemy", "Status_Effect", "Status_Action_Enemy", "Status_Attributes");
        ValidateWorkbook(dataRoot, "StatusData_Weapon.xlsx", errors,
            "StatusWeapon_Main", "StatusWeapon_Action", "StatusWeapon_Effect", "Weapon_Initiate", "SpecialParam_Desc");
        ValidateWorkbook(dataRoot, "EnemyAttributes.xlsx", errors,
            "Enemy_Main", "Base_HP", "Base_ATK1", "Base_ATK2");
        ValidateWorkbook(dataRoot, "EnemySkill.xlsx", errors,
            "EnemySkill_Main", "EnemySkill_Effect", "EnemySkill_Param", "EnemyAI", "EnemyAI_Rule");
        ValidateWorkbook(dataRoot, "ReactionLevelCoefficient.xlsx", errors, "Sheet1");
        ValidateCharacterWorkbooks(dataRoot, errors);

        return errors;
    }

    private static void ValidateCharacterWorkbooks(string dataRoot, List<string> errors)
    {
        string characterRoot = Path.Combine(dataRoot, "Characters");
        if (!Directory.Exists(characterRoot))
        {
            errors.Add("缺少 Characters 角色配表目录");
            return;
        }

        string[] workbooks = Directory.GetFiles(characterRoot, "*.xlsx")
            .Where(path => !Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal))
            .Where(path => !Path.GetFileNameWithoutExtension(path).StartsWith("0_", StringComparison.Ordinal))
            .ToArray();

        if (workbooks.Length == 0)
        {
            errors.Add("Characters 目录中没有正式角色工作簿");
            return;
        }

        foreach (string workbookPath in workbooks)
            ValidateWorkbookPath(workbookPath, "Characters/" + Path.GetFileName(workbookPath), errors, CharacterSheets);
    }

    private static void ValidateWorkbook(string dataRoot, string relativePath, List<string> errors, params string[] requiredSheets)
    {
        ValidateWorkbookPath(Path.Combine(dataRoot, relativePath), relativePath, errors, requiredSheets);
    }

    private static void ValidateWorkbookPath(string path, string displayName, List<string> errors, params string[] requiredSheets)
    {
        if (!File.Exists(path))
        {
            errors.Add($"缺少工作簿：{displayName}");
            return;
        }

        try
        {
            using var package = new ExcelPackage(new FileInfo(path));
            foreach (string sheetName in requiredSheets)
            {
                if (package.Workbook.Worksheets[sheetName] == null)
                    errors.Add($"{displayName} 缺少工作表：{sheetName}");
            }
        }
        catch (Exception exception)
        {
            errors.Add($"无法打开 {displayName}：{exception.Message}");
        }
    }
}
