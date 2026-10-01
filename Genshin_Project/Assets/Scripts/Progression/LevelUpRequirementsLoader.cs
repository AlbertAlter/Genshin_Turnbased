using System;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

public static class LevelUpRequirementsLoader
{
    public const string WorkbookName = "LevelUpRequirements(In_use).xlsx";

    public static LevelUpRequirementsTable LoadFromStreamingAssets()
    {
        return Load(Path.Combine(Application.streamingAssetsPath, "Data", WorkbookName));
    }

    public static LevelUpRequirementsTable Load(string workbookPath)
    {
        if (string.IsNullOrWhiteSpace(workbookPath))
            throw new ArgumentException("升级经验工作簿路径不能为空。", nameof(workbookPath));
        if (!File.Exists(workbookPath))
            throw new FileNotFoundException("找不到升级经验工作簿。", workbookPath);

        var table = new LevelUpRequirementsTable();
        using (var package = new ExcelPackage(new FileInfo(workbookPath)))
        {
            LoadCharacter(RequireSheet(package, "Character"), table);
            LoadWeapon(RequireSheet(package, "Weapon"), table);
            LoadArtifact(RequireSheet(package, "Artifact"), table);
            LoadSkill(RequireSheet(package, "SkillLevel"), table);
        }
        table.Validate();
        return table;
    }

    private static void LoadCharacter(ExcelWorksheet sheet, LevelUpRequirementsTable table)
    {
        ValidateHeaders(sheet, "NextLevel", "EXPRequired", "TotalEXP");
        for (int row = 2; row <= LastRow(sheet); row++)
            table.AddCharacter(ReadRequiredInt(sheet, row, 1), ReadRequiredInt(sheet, row, 2));
    }

    private static void LoadWeapon(ExcelWorksheet sheet, LevelUpRequirementsTable table)
    {
        ValidateHeaders(sheet, "NextLevel", "EXPRequired_3", "TotalEXP_3", "EXPRequired_4",
            "TotalEXP_4", "EXPRequired_5", "TotalEXP_5");
        for (int row = 2; row <= LastRow(sheet); row++)
        {
            int nextLevel = ReadRequiredInt(sheet, row, 1);
            table.AddWeapon(3, nextLevel, ReadRequiredInt(sheet, row, 2));
            table.AddWeapon(4, nextLevel, ReadRequiredInt(sheet, row, 4));
            table.AddWeapon(5, nextLevel, ReadRequiredInt(sheet, row, 6));
        }
    }

    private static void LoadArtifact(ExcelWorksheet sheet, LevelUpRequirementsTable table)
    {
        ValidateHeaders(sheet, "NextLevel", "EXPRequired_3", "TotalEXP_3", "EXPRequired_4",
            "TotalEXP_4", "EXPRequired_5", "TotalEXP_5");
        for (int row = 2; row <= LastRow(sheet); row++)
        {
            int nextLevel = ReadRequiredInt(sheet, row, 1);
            AddOptionalArtifact(sheet, table, row, 2, 3, nextLevel);
            AddOptionalArtifact(sheet, table, row, 4, 4, nextLevel);
            AddOptionalArtifact(sheet, table, row, 6, 5, nextLevel);
        }
    }

    private static void LoadSkill(ExcelWorksheet sheet, LevelUpRequirementsTable table)
    {
        ValidateHeaders(sheet, "NextLevel", "EXPRequired", "EXPType");
        for (int row = 2; row <= LastRow(sheet); row++)
            table.AddSkill(
                ReadRequiredInt(sheet, row, 1),
                ReadRequiredInt(sheet, row, 2),
                ReadRequiredInt(sheet, row, 3));
    }

    private static void AddOptionalArtifact(
        ExcelWorksheet sheet,
        LevelUpRequirementsTable table,
        int row,
        int column,
        int star,
        int nextLevel)
    {
        if (sheet.Cells[row, column].Value == null || string.IsNullOrWhiteSpace(sheet.Cells[row, column].Text))
            return;
        table.AddArtifact(star, nextLevel, ReadRequiredInt(sheet, row, column));
    }

    private static ExcelWorksheet RequireSheet(ExcelPackage package, string sheetName)
    {
        ExcelWorksheet sheet = package.Workbook.Worksheets[sheetName];
        if (sheet == null)
            throw new InvalidDataException($"{WorkbookName} 缺少工作表 {sheetName}。");
        return sheet;
    }

    private static void ValidateHeaders(ExcelWorksheet sheet, params string[] expected)
    {
        for (int index = 0; index < expected.Length; index++)
        {
            string actual = sheet.Cells[1, index + 1].Text.Trim();
            if (!string.Equals(actual, expected[index], StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"{WorkbookName}/{sheet.Name} 第 {index + 1} 列应为 {expected[index]}，实际为 [{actual}]。");
        }
    }

    private static int ReadRequiredInt(ExcelWorksheet sheet, int row, int column)
    {
        object raw = sheet.Cells[row, column].Value;
        double numeric;
        if (raw is int integer) numeric = integer;
        else if (raw is double doubleValue) numeric = doubleValue;
        else if (!double.TryParse(Convert.ToString(raw), out numeric))
            throw new InvalidDataException($"{WorkbookName}/{sheet.Name}!{sheet.Cells[row, column].Address} 必须为整数。");

        if (numeric <= 0d || numeric > int.MaxValue || Math.Abs(numeric - Math.Round(numeric)) > 0.000001d)
            throw new InvalidDataException($"{WorkbookName}/{sheet.Name}!{sheet.Cells[row, column].Address} 必须为正整数。");
        return (int)Math.Round(numeric);
    }

    private static int LastRow(ExcelWorksheet sheet)
    {
        return sheet.Dimension == null ? 1 : sheet.Dimension.End.Row;
    }
}
