using System;
using System.Collections.Generic;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

public partial class DataManager
{
    // ================================================================
    //  ReactionLevelCoefficient.xlsx
    // ================================================================
    void LoadReactionLevelCoefficient()
    {
        string path = Path.Combine(ChartsPath, "ReactionLevelCoefficient.xlsx");
        if (!File.Exists(path)) return;
        using var pkg = new ExcelPackage(new FileInfo(path));
        var sheet = pkg.Workbook.Worksheets[0];
        if (sheet == null) return;
        int startRow = GetDataStartRow(sheet);
        int maxRow = GetSheetMaxRow(sheet);
        for (int row = startRow; row <= maxRow; row++)
        {
            string cell = sheet.Cells[row, FindColumnByHeader(sheet, "lv")].Text;
            if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(sheet.Cells[row, FindColumnByHeader(sheet, "coefficient")].Text)) continue;
            ReactionLevelCoefficientDict[SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "lv")].Value)] = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "coefficient")].Value);
        }
    }
}
