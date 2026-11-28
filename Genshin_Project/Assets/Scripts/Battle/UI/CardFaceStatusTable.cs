using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

/// <summary>Card-face presentation configuration. Never adds fields to or changes a status instance.</summary>
public sealed class CardFaceStatusTable
{
    public const string FileName = "StatusUI.xlsx";
    public const string IdSheet = "ByStatusID", TypeSheet = "ByStatusType";
    public sealed class Rule
    {
        public readonly string Key, UIType;
        public readonly bool Enabled, ByID;
        public readonly int Layer;
        public Rule(string key, string uiType, bool enabled, int layer, bool byID)
        { Key = key; UIType = uiType; Enabled = enabled; Layer = layer; ByID = byID; }
    }
    private readonly Dictionary<int, Rule> byID = new Dictionary<int, Rule>();
    private readonly Dictionary<string, Rule> byType = new Dictionary<string, Rule>(StringComparer.OrdinalIgnoreCase);

    public static CardFaceStatusTable ReadPublished() => ReadFile(Path.Combine(
        Application.streamingAssetsPath, "Data", "UI", CardTargetIndicatorTable.Folder, FileName));

    public static CardFaceStatusTable ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var package = new ExcelPackage(stream);
        var table = new CardFaceStatusTable();
        table.ReadSheet(package.Workbook.Worksheets[IdSheet], true);
        table.ReadSheet(package.Workbook.Worksheets[TypeSheet], false);
        return table;
    }

    private void ReadSheet(ExcelWorksheet sheet, bool id)
    {
        string keyName = id ? "StatusID" : "StatusType";
        if (sheet == null) throw new InvalidDataException("卡面 UI 缺少子表：" + (id ? IdSheet : TypeSheet));
        var h = ExcelTableHeaders.For(sheet);
        h.Require(keyName, "UIType", "Enabled", "Layer");
        for (int row = h.DataStartRow; row <= (sheet.Dimension?.End.Row ?? 0); row++)
        {
            string key = h.Cell(row, keyName).Text.Trim();
            if (key.Length == 0) continue;
            string ui = h.Cell(row, "UIType").Text.Trim();
            if (ui.Length == 0 || ui.Length > 64) throw Error("UIType 需要填写展示类型");
            foreach (char c in ui)
                if (!char.IsLetterOrDigit(c) && c != '_') throw Error("UIType 只允许字母、数字和下划线");
            if (!int.TryParse(h.Cell(row, "Enabled").Text, out int enabled) || enabled < 0 || enabled > 1)
                throw Error("Enabled 只能填 0 或 1");
            if (!int.TryParse(h.Cell(row, "Layer").Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int layer)
                || layer < -10000 || layer > 10000) throw Error("Layer 须为 -10000～10000 的整数");
            var rule = new Rule(key, ui, enabled == 1, layer, id);
            if (id)
            {
                if (!int.TryParse(key, out int statusID) || statusID <= 0) throw Error("StatusID 须为正整数主键");
                if (byID.ContainsKey(statusID)) throw Error("重复 StatusID：" + key);
                byID.Add(statusID, rule);
            }
            else
            {
                if (byType.ContainsKey(key)) throw Error("重复 StatusType：" + key);
                byType.Add(key, rule);
            }
            InvalidDataException Error(string message) => new InvalidDataException($"卡面 UI [{sheet.Name}] 第{row}行：{message}。");
        }
    }

    /// <summary>
    /// Both tables contribute. Different UIType values coexist; the same UIType is drawn once.
    /// An ID rule overrides the same UIType from that instance's type rule, including Enabled=0.
    /// Other active instances can still keep that visual alive. Display and Count are deliberately ignored.
    /// </summary>
    public void Resolve(IEnumerable<StatusInstance> statuses, IDictionary<string, Rule> result)
    {
        result.Clear();
        if (statuses == null) return;
        foreach (var status in statuses)
        {
            if (status == null || !status.IsActive || status.MainData == null) continue;
            byID.TryGetValue(status.MainData.StatusID, out var specific);
            byType.TryGetValue(status.MainData.StatusType ?? "", out var common);
            if (common != null && (specific == null || specific.UIType != common.UIType)) Add(common);
            if (specific != null) Add(specific);
        }
        void Add(Rule candidate)
        {
            if (!candidate.Enabled) return;
            if (!result.TryGetValue(candidate.UIType, out var old)
                || (candidate.ByID && !old.ByID)
                || (candidate.ByID == old.ByID && (candidate.Layer > old.Layer
                    || (candidate.Layer == old.Layer && string.CompareOrdinal(candidate.Key, old.Key) < 0))))
                result[candidate.UIType] = candidate;
        }
    }
}
