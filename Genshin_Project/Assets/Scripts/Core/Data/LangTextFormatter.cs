using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>统一解析参数、换行、颜色与说明Lang链接；参数值只作为文字插入。</summary>
public static class LangTextFormatter
{
    public const string DefaultHighlightColor = "#FFD780";
    private static readonly string[] ColorElements =
        { null, "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Geo", "Dendro", "Physical" };

    public static string GetColorElement(int id)
    {
        if (id < 0 || id >= ColorElements.Length) throw new ArgumentOutOfRangeException(nameof(id));
        return ColorElements[id];
    }

    public static string Format(string template, object[] arguments = null,
        bool richText = false, Func<int, string> color = null, string context = null)
        => FormatContent(template, BindArguments(arguments), richText, color, context, true);

    internal static string FormatCharacterPage(string template, object[] arguments, Func<int, string> color)
        => FormatContent(template, BindArguments(arguments), true, color, null, true, boldStatusHeadings: true);

    private static Func<int, string> BindArguments(object[] arguments)
        => arguments == null ? (Func<int, string>)null : index => index < arguments.Length
            ? Convert.ToString(arguments[index], CultureInfo.InvariantCulture) ?? string.Empty : null;

    internal static string FormatWithArguments(string template, Func<int, string> argument,
        bool richText, Func<int, string> color, string context)
    {
        if (argument == null) return Format(template, richText: richText, color: color, context: context);
        var values = new Dictionary<int, string>();
        return FormatContent(template, index =>
        {
            if (!values.TryGetValue(index, out string value)) values[index] = value = argument(index);
            return value;
        }, richText, color, context, true);
    }

    private static string FormatContent(string template, Func<int, string> argument,
        bool richText, Func<int, string> color, string context, bool allowLinks, bool boldStatusHeadings = false)
    {
        if (string.IsNullOrEmpty(template)) return template ?? string.Empty;
        var result = new StringBuilder(template.Length);
        bool colored = false;
        int lineStart = 0;
        int headingMarker = -1;
        int headingOutputStart = 0;
        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            if (c == '\r' || c == '\n') lineStart = i + 1;
            if (c == '\\' && i + 1 < template.Length)
            {
                char next = template[i + 1];
                if (next == 'n' || next == '\\' || next == '#' || next == '[' || next == ']' || next == '|')
                {
                    AppendText(result, next == 'n' ? "\n" : next.ToString(), richText);
                    if (next == 'n') lineStart = i + 2;
                    i++;
                    continue;
                }
            }
            if (c == '[' && template.Length - i >= 6 && string.CompareOrdinal(template, i, "[text:", 0, 6) == 0)
            {
                if (!allowLinks) throw Error(context, i, "说明链接不支持嵌套");
                int separator = template.IndexOf('|', i + 6);
                int end = FindLinkEnd(template, i + 6);
                if (separator < 0 || end < 0 || separator >= end)
                    throw Error(context, i, "说明链接须写成[text:lang_key|显示文字]");
                string key = template.Substring(i + 6, separator - i - 6);
                if (!IsLinkKey(key)) throw Error(context, i, "说明链接目标须为合法Lang key");
                string label = template.Substring(separator + 1, end - separator - 1);
                if (string.IsNullOrWhiteSpace(label)) throw Error(context, i, "说明链接缺少显示文字");
                string display = FormatContent(label, argument, richText, color, context, false);
                if (richText) result.Append("<link=\"text:").Append(key).Append("\"><u>");
                result.Append(display);
                if (richText) result.Append("</u></link>");
                i = end;
                continue;
            }
            if ((c == '{' || c == '}') && i + 1 < template.Length && template[i + 1] == c)
            {
                AppendText(result, c.ToString(), richText);
                i++;
                continue;
            }
            if (c == '{')
            {
                int end = i + 1;
                while (end < template.Length && template[end] >= '0' && template[end] <= '9') end++;
                if (end == i + 1 || end >= template.Length || template[end] != '}'
                    || !int.TryParse(template.Substring(i + 1, end - i - 1), NumberStyles.None,
                        CultureInfo.InvariantCulture, out int index))
                    throw Error(context, i, "参数须使用{0}、{1}等非负整数占位符");
                string value = argument?.Invoke(index);
                if (value == null) throw Error(context, i, "缺少参数{" + index + "}");
                AppendText(result, value, richText);
                i = end;
                continue;
            }
            if (c == '}') throw Error(context, i, "单独的右花括号须写成}}");
            if (c == '#')
            {
                if (colored)
                {
                    if (richText) result.Append("</color>");
                    if (headingMarker >= lineStart && SkipLineWhitespace(template, headingMarker + 2) < i
                        && HasHeadingBody(template, i + 1))
                        result.Insert(headingOutputStart, "<b>").Append("</b>");
                    headingMarker = -1;
                    colored = false;
                }
                else
                {
                    if (i + 1 >= template.Length || template[i + 1] < '0' || template[i + 1] > '8')
                        throw Error(context, i, "颜色编号须为单个数字0—8；普通井号须写成\\#");
                    int id = template[++i] - '0';
                    if (richText)
                    {
                        string hex = color?.Invoke(id);
                        if (!IsRgbColor(hex)) throw Error(context, i, "颜色" + id + "未提供有效的#RRGGBB色值");
                        if (boldStatusHeadings && id >= 1 && SkipLineWhitespace(template, lineStart) == i - 1)
                        {
                            headingMarker = i - 1;
                            headingOutputStart = result.Length;
                        }
                        result.Append("<color=").Append(hex).Append('>');
                    }
                    colored = true;
                }
                continue;
            }
            AppendText(result, c.ToString(), richText);
        }
        if (colored) throw Error(context, template.Length, "颜色标记缺少结束的#");
        return result.ToString();
    }

    private static bool HasHeadingBody(string template, int afterHeading)
    {
        int endOfLine = SkipLineWhitespace(template, afterHeading);
        int lineBreakLength = GetLineBreakLength(template, endOfLine);
        if (lineBreakLength == 0) return false;
        int bodyStart = SkipLineWhitespace(template, endOfLine + lineBreakLength);
        return bodyStart < template.Length && GetLineBreakLength(template, bodyStart) == 0;
    }

    private static int SkipLineWhitespace(string template, int index)
    {
        while (index < template.Length && char.IsWhiteSpace(template[index])
            && template[index] != '\r' && template[index] != '\n') index++;
        return index;
    }

    private static int GetLineBreakLength(string template, int index)
    {
        if (index >= template.Length) return 0;
        char current = template[index];
        char next = index + 1 < template.Length ? template[index + 1] : '\0';
        if (current == '\r') return next == '\n' ? 2 : 1;
        if (current == '\n') return 1;
        return current == '\\' && next == 'n' ? 2 : 0;
    }

    private static bool IsLinkKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        foreach (char c in key)
            if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9')
                && c != '_' && c != '-' && c != '.') return false;
        return true;
    }

    private static int FindLinkEnd(string template, int start)
    {
        for (int i = start; i < template.Length; i++)
        {
            if (template[i] == '\\' && i + 1 < template.Length) { i++; continue; }
            if (template[i] == ']') return i;
        }
        return -1;
    }

    private static bool IsRgbColor(string value)
    {
        if (value == null || value.Length != 7 || value[0] != '#') return false;
        for (int i = 1; i < value.Length; i++)
            if (!Uri.IsHexDigit(value[i])) return false;
        return true;
    }

    private static void AppendText(StringBuilder output, string value, bool richText)
    {
        // TMP原生标签只由本解析器产生；模板和参数内的<按普通字符显示。
        output.Append(richText ? value.Replace("<", "<noparse><</noparse>") : value);
    }

    private static FormatException Error(string context, int position, string message)
        => new FormatException("Lang文本" + (string.IsNullOrEmpty(context) ? "" : "[" + context + "]")
            + "第" + (position + 1) + "字符：" + message);
}
