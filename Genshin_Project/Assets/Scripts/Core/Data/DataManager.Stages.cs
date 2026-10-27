using System;
using System.Collections.Generic;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

public partial class DataManager
{
    // ================================================================
    //  StageConfig.xlsx
    // ================================================================
    void LoadStageConfig()
    {
        string path = Path.Combine(ChartsPath, "StageConfig.xlsx");
        if (!File.Exists(path)) return;
        using var pkg = new ExcelPackage(new FileInfo(path));
        var sheet = pkg.Workbook.Worksheets[0];
        if (sheet == null) return;
        int startRow = GetDataStartRow(sheet);
        int maxRow = GetSheetMaxRow(sheet);
        for (int row = startRow; row <= maxRow; row++)
        {
            string cell = sheet.Cells[row, FindColumnByHeader(sheet, "StageID")].Text;
            if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(sheet.Cells[row, FindColumnByHeader(sheet, "StageName")].Text)) continue;
            var data = new StageConfigData
            {
                StageID = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "StageID")].Value),
                StageName = sheet.Cells[row, FindColumnByHeader(sheet, "StageName")].Text,
                StageDescription = sheet.Cells[row, FindColumnByHeader(sheet, "StageDescription")].Text,
                EnemyIDs = sheet.Cells[row, FindColumnByHeader(sheet, "EnemyIDs")].Text,
                EnemyCounts = sheet.Cells[row, FindColumnByHeader(sheet, "EnemyCounts")].Text,
                EnemyLevels = sheet.Cells[row, FindColumnByHeader(sheet, "EnemyLevels")].Text,
                AllyIDs = sheet.Cells[row, FindColumnByHeader(sheet, "AllyIDs")].Text,
                AllyCounts = sheet.Cells[row, FindColumnByHeader(sheet, "AllyCounts")].Text,
                AllyLevels = sheet.Cells[row, FindColumnByHeader(sheet, "AllyLevels")].Text,
                WinCondition = sheet.Cells[row, FindColumnByHeader(sheet, "WinCondition")].Text,
                LoseCondition = sheet.Cells[row, FindColumnByHeader(sheet, "LoseCondition")].Text
            };
            if (data.StageID == 0) continue;
            StageConfigDict[data.StageID] = data;
        }
    }
}
