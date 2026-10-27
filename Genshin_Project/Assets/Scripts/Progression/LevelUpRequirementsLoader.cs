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
            table.AddCharacter(ReadRequiredInt(sheet, row, "NextLevel"), ReadRequiredInt(sheet, row, "EXPRequired"));
    }

    private static void LoadWeapon(ExcelWorksheet sheet, LevelUpRequirementsTable table)
    {
        ValidateHeaders(sheet, "NextLevel", "EXPRequired_3", "TotalEXP_3", "EXPRequired_4",
            "TotalEXP_4", "EXPRequired_5", "TotalEXP_5");
        for (int row = 2; row <= LastRow(sheet); row++)
        {
            int nextLevel = ReadRequiredInt(sheet, row, "NextLevel");
            table.AddWeapon(3, nextLevel, ReadRequiredInt(sheet, row, "EXPRequired_3"));
            table.AddWeapon(4, nextLevel, ReadRequiredInt(sheet, row, "EXPRequired_4"));
            table.AddWeapon(5, nextLevel, ReadRequiredInt(sheet, row, "EXPRequired_5"));
        }
    }

    private static void LoadArtifact(ExcelWorksheet sheet, LevelUpRequirementsTable table)
    {
        ValidateHeaders(sheet, "NextLevel", "EXPRequired_3", "TotalEXP_3", "EXPRequired_4",
            "TotalEXP_4", "EXPRequired_5", "TotalEXP_5");
        for (int row = 2; row <= LastRow(sheet); row++)
        {
            int nextLevel = ReadRequiredInt(sheet, row, "NextLevel");
            AddOptionalArtifact(sheet, table, row, "EXPRequired_3", 3, nextLevel);
            AddOptionalArtifact(sheet, table, row, "EXPRequired_4", 4, nextLevel);
            AddOptionalArtifact(sheet, table, row, "EXPRequired_5", 5, nextLevel);
        }
    }

    private static void LoadSkill(ExcelWorksheet sheet, LevelUpRequirementsTable table)
    {
        ValidateHeaders(sheet, "NextLevel", "EXPRequired", "EXPType");
        for (int row = 2; row <= LastRow(sheet); row++)
            table.AddSkill(
                ReadRequiredInt(sheet, row, "NextLevel"),
                ReadRequiredInt(sheet, row, "EXPRequired"),
                ReadRequiredInt(sheet, row, "EXPType"));
    }

    private static void AddOptionalArtifact(
        ExcelWorksheet sheet,
        LevelUpRequirementsTable table,
        int row,
        string header,
        int star,
        int nextLevel)
    {
        ExcelRange cell = ExcelTableHeaders.For(sheet).Cell(row, header);
        if (cell.Value == null || string.IsNullOrWhiteSpace(cell.Text))
            return;
        table.AddArtifact(star, nextLevel, ReadRequiredInt(sheet, row, header));
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
        ExcelTableHeaders.For(sheet).Require(expected);
    }

    private static int ReadRequiredInt(ExcelWorksheet sheet, int row, string header)
    {
        ExcelRange cell = ExcelTableHeaders.For(sheet).Cell(row, header);
        object raw = cell.Value;
        double numeric;
        if (raw is int integer) numeric = integer;
        else if (raw is double doubleValue) numeric = doubleValue;
        else if (!double.TryParse(Convert.ToString(raw), out numeric))
            throw new InvalidDataException($"{WorkbookName}/{sheet.Name}!{cell.Address} ({header}) 必须为整数。");

        if (numeric <= 0d || numeric > int.MaxValue || Math.Abs(numeric - Math.Round(numeric)) > 0.000001d)
            throw new InvalidDataException($"{WorkbookName}/{sheet.Name}!{cell.Address} ({header}) 必须为正整数。");
        return (int)Math.Round(numeric);
    }

    private static int LastRow(ExcelWorksheet sheet)
    {
        return sheet.Dimension == null ? 1 : sheet.Dimension.End.Row;
    }
}
