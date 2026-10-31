using System;
using System.Globalization;
using System.Text;

/// <summary>逐段解析任意文本中的参数、换行和颜色标记；参数值只作为文字插入。</summary>
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
    {
        if (string.IsNullOrEmpty(template)) return template ?? string.Empty;
        var result = new StringBuilder(template.Length);
        bool colored = false;
        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            if (c == '\\' && i + 1 < template.Length)
            {
                char next = template[i + 1];
                if (next == 'n' || next == '\\' || next == '#')
                {
                    AppendText(result, next == 'n' ? "\n" : next.ToString(), richText);
                    i++;
                    continue;
                }
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
                if (arguments == null || index >= arguments.Length)
                    throw Error(context, i, "缺少参数{" + index + "}");
                AppendText(result, Convert.ToString(arguments[index], CultureInfo.InvariantCulture) ?? string.Empty, richText);
                i = end;
                continue;
            }
            if (c == '}') throw Error(context, i, "单独的右花括号须写成}}");
            if (c == '#')
            {
                if (colored)
                {
                    if (richText) result.Append("</color>");
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
