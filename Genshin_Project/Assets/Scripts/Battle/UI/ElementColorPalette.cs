using System;
using System.Collections.Generic;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

/// <summary>元素特效与元素字体的统一主体色来源；不保存另一份硬编码色号。</summary>
public sealed class ElementColorPalette
{
    public const string FileName = "ElementColors.xlsx";
    public const string ElementIconFolder = "art_assets/ElementSVG/";
    private static readonly string[] Keys = { "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Geo", "Dendro", "Physical", "Healing" };
    private readonly Dictionary<string, Color> colors;
    private readonly Dictionary<ReactionType, string> reactionColors = new Dictionary<ReactionType, string>();
    private readonly Dictionary<string, string> auraIcons = new Dictionary<string, string>(StringComparer.Ordinal);
    public IEnumerable<string> AuraIconPaths => auraIcons.Values;
    public bool TryGetAuraIconPath(string element, out string path) => auraIcons.TryGetValue(element, out path);

    /// <summary>全部元素字形集中在ElementSVG；Unity读取同目录的白色PNG，网页直接读取SVG。</summary>
    public static string ElementIconPath(string element)
    {
        if (element == "Physical" || element == "Healing" || Array.IndexOf(Keys, element) < 0)
            throw new ArgumentException("没有该元素的图标：" + element, nameof(element));
        return ElementIconFolder + "Element_" + element + ".png";
    }

    public static string ElementSvgPath(string element)
        => Path.ChangeExtension(ElementIconPath(element), ".svg");

    private ElementColorPalette(Dictionary<string, Color> colors) { this.colors = colors; }

    /// <summary>Project white and Physical share one authoritative table entry.</summary>
    public Color ProjectWhite => colors["Physical"];
    /// <summary>治疗绿独立于草元素色；治疗跳字和治疗光芒共用此项。</summary>
    public Color HealingGreen => colors["Healing"];

    public static ElementColorPalette Load() => CampaignUiTables.Read(ReadPublished);
    private static ElementColorPalette ReadPublished()
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
            // Rows without an element key are personal notes/swatches, not palette entries.
            if (key.Length == 0) continue;
            string color = headers.Cell(row, "Color").Text.Trim();
            entries.Add(new KeyValuePair<string, string>(key, color));
        }
        var palette = Parse(entries);
        // 与角色页复用既有字形，不能把状态图标Components当作全项目元素图标来源。
        foreach (string key in Keys)
            if (key != "Physical" && key != "Healing") palette.auraIcons.Add(key, ElementIconPath(key));
        var reactions = package.Workbook.Worksheets["Reactions"];
        if (reactions?.Dimension != null)
        {
            var reactionHeaders = ExcelTableHeaders.For(reactions); reactionHeaders.Require("Reaction", "Color");
            var values = new List<KeyValuePair<string, string>>();
            for (int row = reactionHeaders.DataStartRow; row <= reactions.Dimension.End.Row; row++)
            {
                string key = reactionHeaders.Cell(row, "Reaction").Text.Trim();
                if (key.Length > 0) values.Add(new KeyValuePair<string, string>(key, reactionHeaders.Cell(row, "Color").Text.Trim()));
            }
            palette.ReadReactionColors(values);
        }
        return palette;
    }

    public void ReadReactionColors(IEnumerable<KeyValuePair<string, string>> entries)
    {
        var parsed = new Dictionary<ReactionType, string>();
        foreach (var entry in entries)
        {
            if (!Enum.TryParse(entry.Key, out ReactionType reaction) || !Enum.IsDefined(typeof(ReactionType), reaction) || reaction == ReactionType.None)
                throw new InvalidDataException("未知反应颜色键：" + entry.Key);
            string value = (entry.Value ?? "").Trim();
            if (parsed.ContainsKey(reaction)) throw new InvalidDataException("重复反应颜色：" + entry.Key);
            if (value != "Trigger" && (value.Length != 7 || value[0] != '#' || !ColorUtility.TryParseHtmlString(value, out _)))
                throw new InvalidDataException("反应颜色须为Trigger或#RRGGBB：" + entry.Key);
            parsed.Add(reaction, value);
        }
        foreach (ReactionType reaction in Enum.GetValues(typeof(ReactionType)))
            if (reaction != ReactionType.None && !parsed.ContainsKey(reaction)) throw new InvalidDataException("缺少反应颜色：" + reaction);
        reactionColors.Clear(); foreach (var entry in parsed) reactionColors.Add(entry.Key, entry.Value);
    }

    public bool TryGetReactionColor(ReactionType reaction, string triggerElement, out Color color)
    {
        color = default;
        if (!reactionColors.TryGetValue(reaction, out string value)) return false;
        if (value == "Trigger" && string.IsNullOrWhiteSpace(triggerElement)) return false;
        return value == "Trigger" ? TryGetColor(triggerElement, out color) : ColorUtility.TryParseHtmlString(value, out color);
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
