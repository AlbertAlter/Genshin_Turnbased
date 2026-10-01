using System;
using System.Collections.Generic;
using System.Globalization;
using OfficeOpenXml;

public static class EnemyShieldRuleTableLoader
{
    public static readonly string[] ShieldRows =
    {
        "Pyro", "Hydro", "Electro", "Freeze", "Dendro", "Geo"
    };

    public static readonly string[] HitColumns =
    {
        "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Dendro", "Geo", "Physical",
        "Overloaded", "Elemental_Hit", "Hit"
    };

    public static EnemyShieldRuleTable Load(ExcelWorksheet sheet)
    {
        if (sheet == null)
            throw new TableValidationException("[EnemyShield/Type1] 缺少必需工作表");
        if (sheet.Dimension == null)
            throw new TableValidationException("[EnemyShield/Type1] 工作表为空");

        var knownRows = new HashSet<string>(ShieldRows, StringComparer.OrdinalIgnoreCase);
        var knownColumns = new HashSet<string>(HitColumns, StringComparer.OrdinalIgnoreCase);
        var seenColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var columns = new Dictionary<int, string>();

        for (int column = 2; column <= sheet.Dimension.End.Column; column++)
        {
            string hitKind = sheet.Cells[1, column].Text.Trim();
            if (hitKind.Length == 0)
                throw new TableValidationException($"[EnemyShield/Type1] 第 {column} 列表头为空");
            if (!knownColumns.Contains(hitKind))
                throw new TableValidationException($"[EnemyShield/Type1] 未知攻击列：{hitKind}");
            if (!seenColumns.Add(hitKind))
                throw new TableValidationException($"[EnemyShield/Type1] 重复攻击列：{hitKind}");
            columns.Add(column, hitKind);
        }
        foreach (string required in HitColumns)
            if (!seenColumns.Contains(required))
                throw new TableValidationException($"[EnemyShield/Type1] 缺少攻击列：{required}");

        var table = new EnemyShieldRuleTable();
        var seenRows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int row = 2; row <= sheet.Dimension.End.Row; row++)
        {
            string shieldElement = sheet.Cells[row, 1].Text.Trim();
            bool hasRuleCell = false;
            foreach (int column in columns.Keys)
                hasRuleCell |= !string.IsNullOrWhiteSpace(sheet.Cells[row, column].Text);
            if (shieldElement.Length == 0 && !hasRuleCell)
                continue;
            if (shieldElement.Length == 0)
                throw new TableValidationException($"[EnemyShield/Type1] 第 {row} 行缺少盾元素名");
            if (!knownRows.Contains(shieldElement))
                throw new TableValidationException($"[EnemyShield/Type1] 未知盾元素行：{shieldElement}");
            if (!seenRows.Add(shieldElement))
                throw new TableValidationException($"[EnemyShield/Type1] 重复盾元素行：{shieldElement}");

            foreach (KeyValuePair<int, string> column in columns)
            {
                string raw = GetInvariantCellText(sheet.Cells[row, column.Key]);
                EnemyShieldRule rule;
                try
                {
                    rule = EnemyShieldRuleParser.Parse(raw, shieldElement, column.Value);
                }
                catch (Exception exception) when (exception is FormatException || exception is ArgumentException)
                {
                    throw new TableValidationException(
                        $"[EnemyShield/Type1] {shieldElement} x {column.Value} 规则无效：{exception.Message}");
                }
                if (rule.HasEffect)
                    table.Add(rule);
            }
        }

        foreach (string required in ShieldRows)
            if (!seenRows.Contains(required))
                throw new TableValidationException($"[EnemyShield/Type1] 缺少盾元素行：{required}");
        return table;
    }

    private static string GetInvariantCellText(ExcelRange cell)
    {
        object value = cell.Value;
        if (value == null)
            return string.Empty;
        if (value is string text)
            return text.Trim();
        if (value is IFormattable formattable)
            return formattable.ToString(null, CultureInfo.InvariantCulture).Trim();
        return value.ToString().Trim();
    }
}
