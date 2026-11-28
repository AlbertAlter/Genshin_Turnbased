using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OfficeOpenXml;

public sealed class LangEntry
{
    public string Chinese;
    public string English;
    public readonly List<LangInfoAddition> Additions = new List<LangInfoAddition>();
}

/// <summary>说明文案的解锁关联；BaseKey是Lang标识，不引用战斗StatusID。</summary>
public sealed class LangInfoAddition
{
    public string TextKey;
    public int CharacterID;
    public string UnlockType;
    public int UnlockIndex;
}

public static class BattleTipKeys
{
    public const string NoValidTarget = "tip_no_valid_target";
    public const string NoActiveCharacter = "tip_no_active_character";
    public const string TauntTargetRestricted = "tip_taunt_target_restricted";
    public const string ThawInsufficientAP = "tip_thaw_insufficient_ap";
}

public enum UiNameKind
{
    Character,
    Enemy,
    Weapon,
    ArtifactSet,
    Skill,
    Talent,
    Constellation,
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
    private ElementColorPalette LangElementColors => langElementColors ??= ElementColorPalette.Load();
    internal UnityEngine.Color ProjectWhite => LangElementColors.ProjectWhite;

    /// <summary>日志等纯文本入口：替换参数、处理换行，并去除颜色标记。</summary>
    public string GetLangText(string key, bool english = false, params object[] arguments)
        => LangTextFormatter.Format(GetRawLangText(key, english), arguments, context: key);

    /// <summary>TMP富文本入口。显示控件需启用richText；不改变共享语言表原文。</summary>
    public string GetLangRichText(string key, bool english = false, params object[] arguments)
        => LangTextFormatter.Format(GetRawLangText(key, english), arguments, true, ResolveLangColor, key);

    /// <summary>界面名称按数值ID查找。天赋和命座另传配表序号，圣遗物使用套装ID。</summary>
    public string GetUiName(UiNameKind kind, int id, string fallbackName, bool english = false, int index = 0)
    {
        string key = kind switch
        {
            UiNameKind.Character => FormattableString.Invariant($"character_{id}_name"),
            UiNameKind.Enemy => FormattableString.Invariant($"enemy_{id}_name"),
            UiNameKind.Weapon => FormattableString.Invariant($"weapon_{id}_name"),
            UiNameKind.ArtifactSet => FormattableString.Invariant($"artifact_{id}_name"),
            UiNameKind.Skill => FormattableString.Invariant($"skill_{id}_name"),
            UiNameKind.Talent => FormattableString.Invariant($"character_{id}_talent_{index}_name"),
            UiNameKind.Constellation => FormattableString.Invariant($"character_{id}_constellation_{index}_name"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        return LangDict.TryGetValue(key, out var entry)
            ? english ? entry.English : entry.Chinese
            : fallbackName ?? string.Empty;
    }

    /// <summary>实际生命阶段覆盖Boss通用文案；没有Boss名称时沿用敌人界面名称。</summary>
    public BossUiText GetBossUiText(int enemyID, int phase, string fallbackName, bool english = false)
        => BossUiText.Resolve(LangDict, enemyID, phase,
            GetUiName(UiNameKind.Enemy, enemyID, fallbackName, english), english);

    internal string GetStatusIconDescriptionText(StatusInstance status, object host, bool english = false)
    {
        string key = "status_icon_" + status.StatusID2 + "_desc";
        if (!LangDict.TryGetValue(key, out var entry)) return null;
        string template = english ? entry.English : entry.Chinese;
        var arguments = template.IndexOf('{') < 0 ? null : StatusDescriptionResolver.GetIconArguments(status, host, english);
        return LangTextFormatter.FormatWithArguments(template, arguments, true, ResolveLangColor, key);
    }

    /// <summary>同一语法也可用于技能等任意文本，不要求匹配lang字段或注册整段文案。</summary>
    public string FormatLangText(string template, bool richText = false, params object[] arguments)
        => LangTextFormatter.Format(template, arguments, richText, ResolveLangColor);

    /// <summary>战斗内角色预览和战斗外角色页共用的富文本入口。传入原始Lang模板和展示参数。</summary>
    public string FormatCharacterPageDescription(string template, params object[] arguments)
        => LangTextFormatter.FormatCharacterPage(template, arguments, ResolveLangColor);

    /// <summary>界面按实际技能编号读取独立Lang文案，原技能表Description继续保留阅读用途。</summary>
    public string GetSkillDescriptionText(int skillId, string fallbackDescription,
        bool richText = false, bool english = false, params object[] arguments)
    {
        string key = "skill_" + skillId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "_desc";
        string source = LangDict.ContainsKey(key) ? key : fallbackDescription;
        return richText ? GetLangRichText(source, english, arguments) : GetLangText(source, english, arguments);
    }

    /// <summary>基础说明始终在首段；各补充只求值一次，激活项优先，组内保留配表顺序。</summary>
    public string GetInfoDescriptionText(string baseKey, string fallback, int characterID,
        Func<LangInfoAddition, bool> isActive, bool richText = true, bool english = false,
        string inactiveColor = "#808080")
    {
        LangEntry entry = null;
        bool found = baseKey != null && LangDict.TryGetValue(baseKey, out entry);
        string template = found ? english ? entry.English : entry.Chinese : fallback ?? string.Empty;
        var output = new StringBuilder(LangTextFormatter.Format(template, richText: richText,
            color: ResolveLangColor, context: baseKey));
        if (!found) return output.ToString();
        var additions = entry.Additions.Where(a => a.CharacterID == characterID)
            .Select(a => (Addition: a, Active: isActive?.Invoke(a) == true))
            .OrderByDescending(a => a.Active); // LINQ的稳定排序保留相同激活状态的原始行序。
        foreach (var item in additions)
        {
            var text = LangDict[item.Addition.TextKey];
            string raw = english ? text.English : text.Chinese;
            bool dimmed = richText && !item.Active;
            output.Append("\n\n");
            if (dimmed) output.Append("<color=").Append(inactiveColor).Append('>');
            // 未激活的整项统一灰色，包括原先的元素色、状态名和链接显示文字。
            output.Append(LangTextFormatter.Format(raw, richText: richText,
                color: dimmed ? _ => inactiveColor : ResolveLangColor, context: item.Addition.TextKey));
            if (dimmed) output.Append("</color>");
        }
        return output.ToString();
    }

    private string ResolveLangColor(int id)
    {
        if (id == 0) return LangHighlightColor;
        return "#" + UnityEngine.ColorUtility.ToHtmlStringRGB(LangElementColors.GetColor(LangTextFormatter.GetColorElement(id)));
    }

    internal static string GetLangWorkbookPath(string dataRoot)
    {
        string organized = Path.Combine(dataRoot, "lang", "lang.xlsx");
        return File.Exists(organized) ? organized : Path.Combine(dataRoot, "lang.xlsx");
    }

    internal static string GetStatusIconWorkbookPath(string dataRoot)
        => Path.Combine(dataRoot, "lang", "lang_StatusIcon.xlsx");

    internal static string GetUiNamesWorkbookPath(string dataRoot)
        => Path.Combine(dataRoot, "lang", "lang_UINames.xlsx");

    private void LoadLang()
    {
        langElementColors = null;
        using (var package = OpenDataWorkbook(GetLangWorkbookPath(ChartsPath)))
        {
            LoadLangWorkbook(package, "lang.xlsx");
            LoadInfoAdditions(package.Workbook.Worksheets["InfoAdditions"]);
        }

        LoadOptionalLangWorkbook(GetStatusIconWorkbookPath(ChartsPath));
        LoadOptionalLangWorkbook(GetUiNamesWorkbookPath(ChartsPath));
    }

    private void LoadOptionalLangWorkbook(string path)
    {
        if (!File.Exists(path)) return;
        using var package = OpenDataWorkbook(path, "lang/" + Path.GetFileName(path));
        LoadLangWorkbook(package, Path.GetFileName(path));
    }

    private void LoadLangWorkbook(ExcelPackage workbook, string fileName)
    {
        foreach (var sheet in workbook.Workbook.Worksheets)
        {
            if (!string.Equals(sheet.Name, "lang", StringComparison.OrdinalIgnoreCase)
                && !sheet.Name.StartsWith("lang_", StringComparison.OrdinalIgnoreCase)) continue;
            // 新建但尚未填写的分类子表不提供词条，也不影响已有语言数据。
            if (sheet.Dimension == null) continue;
            LoadLangSheet(sheet, fileName);
        }
    }

    private void LoadInfoAdditions(ExcelWorksheet sheet)
    {
        if (sheet?.Dimension == null) return;
        int baseColumn = FindColumnByHeader(sheet, "BaseKey");
        int textColumn = FindColumnByHeader(sheet, "TextKey");
        int characterColumn = FindColumnByHeader(sheet, "CharacterID");
        int typeColumn = FindColumnByHeader(sheet, "UnlockType");
        int indexColumn = FindColumnByHeader(sheet, "UnlockIndex");
        var registered = new HashSet<string>(StringComparer.Ordinal);
        for (int row = GetDataStartRow(sheet); row <= GetSheetMaxRow(sheet); row++)
        {
            string key = sheet.Cells[row, baseColumn].Text.Trim();
            string textKey = sheet.Cells[row, textColumn].Text.Trim();
            if (key.Length == 0 && textKey.Length == 0) continue;
            string type = sheet.Cells[row, typeColumn].Text.Trim();
            if (!int.TryParse(sheet.Cells[row, characterColumn].Text.Trim(), out int character)
                || !int.TryParse(sheet.Cells[row, indexColumn].Text.Trim(), out int index))
                throw new TableValidationException($"lang.xlsx/InfoAdditions第{row}行CharacterID和UnlockIndex须为整数。");
            if (!LangDict.TryGetValue(key, out var entry) || !LangDict.ContainsKey(textKey))
                throw new TableValidationException($"lang.xlsx/InfoAdditions第{row}行基础或补充文案key不存在。");
            if (!registered.Add(textKey))
                throw new TableValidationException("说明补充key重复关联：" + textKey);
            bool valid = type == "Talent" && index > 0 || type == "Constellation" && index >= 1 && index <= 6
                || (type == "Hexerei" || type == "MoonsignGleam" || type == "MoonsignFull") && index >= 0;
            if (character <= 0 || !valid)
                throw new TableValidationException($"lang.xlsx/InfoAdditions第{row}行角色或解锁条件无效。");
            entry.Additions.Add(new LangInfoAddition
            { TextKey = textKey, CharacterID = character, UnlockType = type, UnlockIndex = index });
        }
    }

    private void LoadLangSheet(ExcelWorksheet sheet, string fileName)
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
            // 某些阶段原作没有副标题；显式空条目用于关闭该Boss的通用副标题。
            bool emptyBossSubtitle = string.Equals(sheet.Name, "lang_ui_boss", StringComparison.Ordinal)
                && key.StartsWith("boss_", StringComparison.Ordinal) && key.EndsWith("_subtitle", StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(chinese) && string.IsNullOrWhiteSpace(english);
            if (key.Length == 0 || !emptyBossSubtitle && (string.IsNullOrWhiteSpace(chinese) || string.IsNullOrWhiteSpace(english)))
                throw new TableValidationException($"{fileName}/{sheet.Name}第{row}行须填写key、chinese、english。");
            if (LangDict.ContainsKey(key)) throw new TableValidationException($"{fileName}/{sheet.Name}重复key：" + key);
            LangDict.Add(key, new LangEntry { Chinese = chinese, English = english });
        }
    }
}
