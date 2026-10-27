using System;
using System.Collections.Generic;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

/// <summary>元素特效与元素字体的统一主体色来源；不保存另一份硬编码色号。</summary>
public sealed class ElementColorPalette
{
    public const string FileName = "ElementColors.xlsx";
    private static readonly string[] Keys = { "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Geo", "Dendro", "Physical" };
    private readonly Dictionary<string, Color> colors;

    private ElementColorPalette(Dictionary<string, Color> colors) { this.colors = colors; }

    public static ElementColorPalette Load()
        => ReadFile(Path.Combine(Application.streamingAssetsPath, "Data", "UI", FileName));

    /// <summary>同步工具可校验显式源文件；正式运行入口只用 Load。</summary>
    public static ElementColorPalette ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var package = new ExcelPackage(stream);
        var sheet = package.Workbook.Worksheets["Colors"];
        if (sheet?.Dimension == null) throw new InvalidDataException("元素颜色表缺少 Colors 子表。");
        var headers = ExcelTableHeaders.For(sheet);
        headers.Require("Element", "Color");
        var entries = new List<KeyValuePair<string, string>>();
        for (int row = headers.DataStartRow; row <= sheet.Dimension.End.Row; row++)
        {
            string key = headers.Cell(row, "Element").Text.Trim();
            string color = headers.Cell(row, "Color").Text.Trim();
            if (key.Length == 0 && color.Length == 0) continue;
            entries.Add(new KeyValuePair<string, string>(key, color));
        }
        return Parse(entries);
    }

    public static ElementColorPalette Parse(IEnumerable<KeyValuePair<string, string>> entries)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));
        var colors = new Dictionary<string, Color>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            string key = (entry.Key ?? string.Empty).Trim();
            string value = (entry.Value ?? string.Empty).Trim();
            if (Array.IndexOf(Keys, key) < 0) throw new InvalidDataException("元素颜色键不合法：" + key);
            if (colors.ContainsKey(key)) throw new InvalidDataException("元素颜色键重复：" + key);
            // 主体 RGB 只在这里配置；透明度由具体字体/特效参数控制，避免多处重复乘Alpha。
            if (value.Length != 7 || value[0] != '#' || !ColorUtility.TryParseHtmlString(value, out var color))
                throw new InvalidDataException("元素主体色必须使用 #RRGGBB：" + key);
            colors.Add(key, color);
        }
        foreach (string key in Keys)
            if (!colors.ContainsKey(key)) throw new InvalidDataException("元素颜色缺少：" + key);
        return new ElementColorPalette(colors);
    }

    /// <summary>使用已结算伤害的元素，不按角色、武器或受击者固有元素推断。未知元素明确返回 false。</summary>
    public bool TryGetColor(string element, out Color color)
    {
        string key = (element ?? string.Empty).Trim();
        // 无元素伤害使用物理展示色；不改变战斗事件本身的元素定义。
        if (key.Length == 0 || key == "None") key = "Physical";
        return colors.TryGetValue(key, out color);
    }

    public Color GetColor(string element)
    {
        if (!TryGetColor(element, out var color)) throw new KeyNotFoundException("元素展示色未定义：" + element);
        return color;
    }
}
