using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

/// <summary>技能与说明弹窗共用的尺寸表；运行时仅从已发布的 Data/UI 读取。</summary>
public sealed class BattleInfoPopupStyle
{
    public const string FileName = "InfoPopup.xlsx";
    public float SkillWidth { get; private set; } = 479;
    public float SkillMaxHeight { get; private set; } = 479;
    public float StatusWidth { get; private set; } = 300;
    public float StatusMaxHeight { get; private set; } = 479;
    public float PanelGap { get; private set; } = 16;

    public static BattleInfoPopupStyle Load() => CampaignUiTables.Read(ReadPublished);
    private static BattleInfoPopupStyle ReadPublished()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "Data", "UI", FileName);
        // Older published UI packs retain the same dimensions until the new optional UI table is published.
        return File.Exists(path) ? ReadFile(path) : new BattleInfoPopupStyle();
    }

    public static BattleInfoPopupStyle ReadFile(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var book = new ExcelPackage(file);
        var sheet = book.Workbook.Worksheets["Parameters"];
        if (sheet?.Dimension == null) throw new InvalidDataException("InfoPopup.xlsx 缺少 Parameters 子表。");
        var columns = ExcelTableHeaders.For(sheet);
        columns.Require("Key", "Value");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int row = columns.DataStartRow; row <= sheet.Dimension.End.Row; row++)
        {
            string key = columns.Cell(row, "Key").Text.Trim();
            if (key.Length == 0) continue;
            if (values.ContainsKey(key)) throw new InvalidDataException("弹窗尺寸参数重复：" + key);
            values.Add(key, Convert.ToString(columns.Cell(row, "Value").Value, CultureInfo.InvariantCulture)?.Trim() ?? "");
        }
        return Parse(values);
    }

    public static BattleInfoPopupStyle Parse(IReadOnlyDictionary<string, string> values)
    {
        string[] keys = { "SkillWidth", "SkillMaxHeight", "StatusWidth", "StatusMaxHeight", "PanelGap" };
        if (values == null) throw new InvalidDataException("缺少弹窗尺寸参数。");
        foreach (string key in values.Keys)
            if (Array.IndexOf(keys, key) < 0) throw new InvalidDataException("未知弹窗尺寸参数：" + key);
        float Number(string key, float minimum)
        {
            if (!values.TryGetValue(key, out string text) ||
                !float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ||
                float.IsNaN(value) || float.IsInfinity(value) || value < minimum)
                throw new InvalidDataException("弹窗尺寸参数无效：" + key + "，最小值为 " + minimum);
            return value;
        }
        return new BattleInfoPopupStyle
        {
            SkillWidth = Number("SkillWidth", 1), SkillMaxHeight = Number("SkillMaxHeight", 1),
            StatusWidth = Number("StatusWidth", 1), StatusMaxHeight = Number("StatusMaxHeight", 1),
            PanelGap = Number("PanelGap", 0)
        };
    }
}
