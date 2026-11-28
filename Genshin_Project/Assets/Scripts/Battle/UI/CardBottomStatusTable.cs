using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

/// <summary>Independent coloured object strip. Status ID chooses appearance, not gameplay or status-bar visibility.</summary>
public sealed class CardBottomStatusTable
{
    public const string RelativePath = "UI/CardBottomStatus/CardBottomStatus.xlsx";
    public enum NumberMode { Count, Instances, Stacks, Value }
    public sealed class Rule
    {
        public int StatusID;
        public string Icon;
        public NumberMode CountMode;
        public bool ShowCount;
        public string CountHook;
        public bool Pinned;
        public string MainColor;
    }
    public readonly Dictionary<int, Rule> Rules = new Dictionary<int, Rule>();
    public int MaxVisible;
    public float IconSizeRatio, SpacingRatio, EdgeGapRatio, NumberSizeRatio;
    public static CardBottomStatusTable Load() => CampaignUiTables.Read(() => ReadFile(Path.Combine(Application.streamingAssetsPath, "Data", RelativePath)));
    public static CardBottomStatusTable ReadFile(string path)
    {
        var p = BattlefieldDamagePopupStyle.ReadParameters(path);
        if (p.Count != 5) throw new InvalidDataException("卡底状态 Parameters 必须包含五项参数。");
        float Number(string key, float min, float max)
        {
            if (!p.TryGetValue(key, out string value) || !float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float n) ||
                float.IsNaN(n) || float.IsInfinity(n) || n < min || n > max) throw new InvalidDataException("卡底状态参数无效：" + key);
            return n;
        }
        var table = new CardBottomStatusTable { MaxVisible = (int)Number("MaxVisible", 4, 4),
            IconSizeRatio = Number("IconSizeRatio", .02f, .3f), SpacingRatio = Number("SpacingRatio", 0, .1f),
            EdgeGapRatio = Number("EdgeGapRatio", 0, .2f), NumberSizeRatio = Number("NumberSizeRatio", .1f, 1) };
        using var book = new ExcelPackage(new FileInfo(path));
        var sheet = book.Workbook.Worksheets["ByStatusID"] ?? throw new InvalidDataException("卡底状态缺少 ByStatusID。");
        var h = ExcelTableHeaders.For(sheet); h.Require("StatusID", "Icon", "ShowCount", "Enabled");
        int modeColumn = h.Optional("CountMode");
        int countHookColumn = h.Optional("CountHook");
        int pinnedColumn = h.Optional("Pinned");
        int mainColorColumn = h.Optional("MainColor");
        var seen = new HashSet<int>();
        for (int row = h.DataStartRow; row <= (sheet.Dimension?.End.Row ?? 0); row++)
        {
            string idText = h.Cell(row, "StatusID").Text.Trim(); if (idText.Length == 0) continue;
            if (!int.TryParse(idText, out int id) || id <= 0 || !seen.Add(id)) throw new InvalidDataException("卡底状态 ID 无效或重复：" + idText);
            int Flag(string key)
            { if (!int.TryParse(h.Cell(row, key).Text, out int flag) || flag < 0 || flag > 1) throw new InvalidDataException("卡底状态 " + key + " 须为0/1。"); return flag; }
            int enabled = Flag("Enabled"), show = Flag("ShowCount");
            string icon = h.Cell(row, "Icon").Text.Trim();
            if (Path.GetFileName(icon) != icon || !icon.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || icon.Contains(".."))
                throw new InvalidDataException("卡底图标只能填写本目录 PNG 文件名：" + icon);
            string modeText = modeColumn > 0 ? sheet.Cells[row, modeColumn].Text.Trim() : string.Empty;
            NumberMode mode = NumberMode.Count;
            if (modeText.Length > 0 && (!Enum.TryParse(modeText, out mode) || !Enum.IsDefined(typeof(NumberMode), mode)))
                throw new InvalidDataException("未知卡底状态计数模式：" + modeText);
            string countHook = countHookColumn > 0 ? sheet.Cells[row, countHookColumn].Text.Trim() : string.Empty;
            CardBottomStatusCountHooks.Validate(countHook);
            string pinnedText = pinnedColumn > 0 ? sheet.Cells[row, pinnedColumn].Text.Trim() : string.Empty;
            int pinned = 0;
            if (pinnedText.Length > 0 && (!int.TryParse(pinnedText, out pinned) || pinned < 0 || pinned > 1))
                throw new InvalidDataException("卡底状态 Pinned 须为0/1，留空按0读取。");
            string mainColor = mainColorColumn > 0 ? sheet.Cells[row, mainColorColumn].Text.Trim() : string.Empty;
            UIIconMainColor.Validate(mainColor);
            if (enabled == 1) table.Rules.Add(id, new Rule { StatusID = id, Icon = "art_assets/UI/CardBottomStatus/" + icon, CountMode = mode,
                ShowCount = show == 1, CountHook = countHook, Pinned = pinned == 1, MainColor = mainColor });
        }
        return table;
    }
    public struct Display
    {
        public Rule Rule;
        public StatusInstance Status; // 实际最新实例，供公共状态详情读取；聚合数字不伪造一份状态。
        public double Count;
        public long Newest;
    }
    /// <summary>Read Count by default; other sources must be explicit. Zero hides the icon, never removes a status.</summary>
    public void Resolve(IEnumerable<StatusInstance> statuses, List<Display> output, Dictionary<int, int> indices,
        FieldPosition position = null, BattleEntity occupant = null)
    {
        output.Clear(); indices.Clear();
        foreach (var status in statuses)
        {
            if (status == null || !status.IsActive || status.MainData == null || !Rules.TryGetValue(status.MainData.StatusID, out var rule)) continue;
            double count = 0;
            if (string.IsNullOrEmpty(rule.CountHook))
            {
                if (rule.CountMode == NumberMode.Count && !string.Equals(status.MainData.StatusType, "Count", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"卡底状态{rule.StatusID}不是Count，请显式配置CountMode或CountHook。");
                count = rule.CountMode == NumberMode.Instances ? 1 : rule.CountMode == NumberMode.Stacks ? Math.Max(0, status.StackCount)
                    : status.HasIntegerRuntimeValue ? (double)status.RuntimeIntValue : status.RuntimeValue;
            }
            if (!indices.TryGetValue(rule.StatusID, out int index))
            { indices.Add(rule.StatusID, output.Count); output.Add(new Display { Rule = rule, Status = status, Count = count, Newest = status.ApplyOrder }); }
            else
            {
                var item = output[index]; item.Count += count;
                if (status.ApplyOrder > item.Newest) { item.Newest = status.ApplyOrder; item.Status = status; }
                output[index] = item;
            }
        }
        // A hook runs once for the aggregated icon, not once per contributing status instance.
        for (int i = 0; i < output.Count; i++)
        {
            var item = output[i];
            if (!string.IsNullOrEmpty(item.Rule.CountHook))
                item.Count = CardBottomStatusCountHooks.Read(item.Rule.CountHook,
                    new CardBottomStatusCountHooks.Context(item.Rule.StatusID, position, occupant, item.Status));
            if (double.IsNaN(item.Count) || double.IsInfinity(item.Count) || item.Count < 0)
                throw new InvalidDataException($"卡底状态{item.Rule.StatusID}计数必须为有限非负数。");
            output[i] = item;
        }
        output.RemoveAll(item => item.Count == 0);
        output.Sort((a, b) => {
            int pinned = b.Rule.Pinned.CompareTo(a.Rule.Pinned);
            if (pinned != 0) return pinned;
            int order = b.Newest.CompareTo(a.Newest);
            return order != 0 ? order : a.Rule.StatusID.CompareTo(b.Rule.StatusID);
        });
        indices.Clear();
        for (int i = 0; i < output.Count; i++) indices.Add(output[i].Rule.StatusID, i);
    }
}
