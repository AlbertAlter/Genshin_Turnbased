using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

/// <summary>Shared timeline parameters. Child tables live beside this workbook.</summary>
public sealed class BattleTimelineSettings
{
    public const string Folder = "ActionTimeLine";
    public const string FileName = "ActionTimeline.xlsx";
    public IReadOnlyDictionary<string, string> Parameters { get; private set; }
    public float SelectionExitSeconds { get; private set; }
    public static string RuntimePath(string file) => Path.Combine(Application.streamingAssetsPath, "Data", "UI", Folder, file);
    public static BattleTimelineSettings Load() => ReadFile(RuntimePath(FileName));

    public static BattleTimelineSettings ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var book = new ExcelPackage(stream);
        var sheet = book.Workbook.Worksheets["Parameters"] ?? throw new InvalidDataException("时间轴主表缺少 Parameters");
        var h = ExcelTableHeaders.For(sheet);
        h.Require("Key", "Value");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int row = h.DataStartRow; row <= sheet.Dimension.End.Row; row++)
        {
            string key = h.Cell(row, "Key").Text.Trim();
            if (key.Length == 0) continue;
            if (values.ContainsKey(key)) throw new InvalidDataException("时间轴参数重复：" + key);
            values.Add(key, Convert.ToString(h.Cell(row, "Value").Value, CultureInfo.InvariantCulture)?.Trim() ?? "");
        }
        if (!values.TryGetValue("SelectionExitSeconds", out string text) ||
            !float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float duration) ||
            float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0)
            throw new InvalidDataException("SelectionExitSeconds 须为有限非负秒数");
        BattlePresentationConfiguration.Parse(values);
        return new BattleTimelineSettings { Parameters = values, SelectionExitSeconds = duration };
    }
}
