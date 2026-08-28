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
            string cell = sheet.Cells[row, 1].Text;
            if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(sheet.Cells[row, 2].Text)) continue;
            var data = new StageConfigData
            {
                StageID = SafeGetInt(sheet.Cells[row, 1].Value),
                StageName = sheet.Cells[row, 2].Text,
                StageDescription = sheet.Cells[row, 3].Text,
                EnemyIDs = sheet.Cells[row, 4].Text,
                EnemyCounts = sheet.Cells[row, 5].Text,
                EnemyLevels = sheet.Cells[row, 6].Text,
                AllyIDs = sheet.Cells[row, 7].Text,
                AllyCounts = sheet.Cells[row, 8].Text,
                AllyLevels = sheet.Cells[row, 9].Text,
                WinCondition = sheet.Cells[row, 10].Text,
                LoseCondition = sheet.Cells[row, 11].Text
            };
            if (data.StageID == 0) continue;
            StageConfigDict[data.StageID] = data;
        }
    }
}
