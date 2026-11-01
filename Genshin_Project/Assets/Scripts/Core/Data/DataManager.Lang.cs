using System;
using System.Collections.Generic;
using System.IO;
using OfficeOpenXml;

public sealed class LangEntry
{
    public string Chinese;
    public string English;
}

public static class BattleTipKeys
{
    public const string NoValidTarget = "tip_no_valid_target";
    public const string NoActiveCharacter = "tip_no_active_character";
    public const string TauntTargetRestricted = "tip_taunt_target_restricted";
    public const string ThawInsufficientAP = "tip_thaw_insufficient_ap";
}

public partial class DataManager
{
    public Dictionary<string, LangEntry> LangDict = new Dictionary<string, LangEntry>(StringComparer.Ordinal);

    /// <summary>原始模板，用于编辑或检查；不替换参数、不剥除标记。</summary>
    public string GetRawLangText(string key, bool english = false)
        => key != null && LangDict.TryGetValue(key, out var entry)
            ? english ? entry.English : entry.Chinese : key ?? string.Empty;

    public string LangHighlightColor { get; set; } = LangTextFormatter.DefaultHighlightColor;
    private ElementColorPalette langElementColors;

    /// <summary>日志等纯文本入口：替换参数、处理换行，并去除颜色标记。</summary>
    public string GetLangText(string key, bool english = false, params object[] arguments)
        => LangTextFormatter.Format(GetRawLangText(key, english), arguments, context: key);

    /// <summary>TMP富文本入口。显示控件需启用richText；不改变共享语言表原文。</summary>
    public string GetLangRichText(string key, bool english = false, params object[] arguments)
        => LangTextFormatter.Format(GetRawLangText(key, english), arguments, true, ResolveLangColor, key);

    /// <summary>同一语法也可用于技能等任意文本，不要求匹配lang字段或注册整段文案。</summary>
    public string FormatLangText(string template, bool richText = false, params object[] arguments)
        => LangTextFormatter.Format(template, arguments, richText, ResolveLangColor);

    private string ResolveLangColor(int id)
    {
        if (id == 0) return LangHighlightColor;
        if (langElementColors == null) langElementColors = ElementColorPalette.Load();
        return "#" + UnityEngine.ColorUtility.ToHtmlStringRGB(langElementColors.GetColor(LangTextFormatter.GetColorElement(id)));
    }

    private void LoadLang()
    {
        langElementColors = null;
        using var package = new ExcelPackage(new FileInfo(Path.Combine(ChartsPath, "lang.xlsx")));
        LoadLangSheet(package.Workbook.Worksheets["lang"]);
    }

    private void LoadLangSheet(ExcelWorksheet sheet)
    {
        if (sheet == null) throw new TableValidationException("lang.xlsx缺少lang子表。");
        int keyColumn = FindColumnByHeader(sheet, "key");
        int chineseColumn = FindColumnByHeader(sheet, "chinese");
        int englishColumn = FindColumnByHeader(sheet, "english");
        for (int row = GetDataStartRow(sheet); row <= GetSheetMaxRow(sheet); row++)
        {
            string key = sheet.Cells[row, keyColumn].Text.Trim();
            string chinese = sheet.Cells[row, chineseColumn].Text;
            string english = sheet.Cells[row, englishColumn].Text;
            if (key.Length == 0 && string.IsNullOrWhiteSpace(chinese) && string.IsNullOrWhiteSpace(english)) continue;
            if (key.Length == 0 || string.IsNullOrWhiteSpace(chinese) || string.IsNullOrWhiteSpace(english))
                throw new TableValidationException($"lang第{row}行须填写key、chinese、english。");
            if (LangDict.ContainsKey(key)) throw new TableValidationException("lang重复key：" + key);
            LangDict.Add(key, new LangEntry { Chinese = chinese, English = english });
        }
    }
}
