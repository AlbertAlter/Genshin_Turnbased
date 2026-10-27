using System;
using System.Collections.Generic;
using System.IO;
using OfficeOpenXml;

public partial class DataManager
{
    /// <summary>公共魔导强化白名单；仅登记已接入的Buff状态，不能按角色名字猜测。</summary>
    public HashSet<string> HexereiStatusIDs = new HashSet<string>(StringComparer.Ordinal);

    void LoadHexereiStatusRules()
    {
        string path = Path.Combine(ChartsPath, "Hexerei.xlsx");
        if (!File.Exists(path)) return; // 兼容尚未发布新规则表的旧数据包。
        using var package = new ExcelPackage(new FileInfo(path));
        LoadHexereiStatusSheet(package.Workbook.Worksheets["Status"]);
    }

    internal void LoadHexereiStatusSheet(ExcelWorksheet sheet)
    {
        if (sheet == null) throw new TableValidationException("Hexerei.xlsx 缺少Status子表。");
        var headers = ExcelTableHeaders.For(sheet);
        headers.Require("StatusID", "StatusID2");
        for (int row = headers.DataStartRow; row <= (sheet.Dimension?.End.Row ?? 0); row++)
        {
            string numeric = headers.Cell(row, "StatusID").Text.Trim();
            string id = headers.Cell(row, "StatusID2").Text.Trim();
            if (numeric.Length == 0 && id.Length == 0) continue;
            if (!int.TryParse(numeric, out int number) || !StatusMainDict.TryGetValue(id, out var main)
                || main.StatusID != number || !string.Equals(main.StatusType, "Buff", StringComparison.OrdinalIgnoreCase))
                throw new TableValidationException($"Hexerei/Status第{row}行必须对应已定义的Buff状态：{numeric}/{id}");
            bool characterStatus = false;
            foreach (var ids in CharacterStatusIDs.Values) if (ids.Contains(id)) { characterStatus = true; break; }
            if (!characterStatus)
                throw new TableValidationException($"Hexerei/Status第{row}行不是正式角色状态：{id}");
            if (!HexereiStatusIDs.Add(id)) throw new TableValidationException("魔导状态重复登记：" + id);
        }
    }
}
