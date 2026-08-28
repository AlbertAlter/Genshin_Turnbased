/// <summary>
/// ScriptHook 文本解析（2026-08-19，从 ScriptHookEvaluator 独立）：
/// 支持"函数名(参数)"与裸函数名（无参数，如 Kaeya_T2 / PreAlliesDamage）。
/// 同一字段可用英文分号并列多个钩子；分号只在最外层分隔，括号内分号保留为参数内容。
/// ID大小写严格匹配，只允许去除首尾空格。
/// </summary>
public static class ScriptHookParser
{
    public sealed class HookCall
    {
        public string Text;
        public string FunctionName;
        public string Argument;
    }

    /// <summary>
    /// 解析钩子文本。返回 false = 格式非法（如含括号但缺右括号）。
    /// </summary>
    public static bool TryParse(string hook, out string functionName, out string argument)
    {
        functionName = string.Empty;
        argument = string.Empty;
        if (string.IsNullOrEmpty(hook)) return false;

        string trimmed = hook.Trim();
        int paren = trimmed.IndexOf('(');
        if (paren < 0)
        {
            // 裸函数名（无参数）
            functionName = trimmed;
            return functionName.Length > 0;
        }

        if (!trimmed.EndsWith(")")) return false; // 含括号但格式不完整（缺右括号）：配表笔误

        functionName = trimmed.Substring(0, paren).Trim();
        argument = trimmed.Substring(paren + 1, trimmed.Length - paren - 2).Trim();
        return functionName.Length > 0;
    }

    /// <summary>解析英文分号并列的钩子列表。空字段视为合法空列表。</summary>
    public static bool TryParseAll(string hooks, out System.Collections.Generic.List<HookCall> calls)
    {
        calls = new System.Collections.Generic.List<HookCall>();
        if (string.IsNullOrWhiteSpace(hooks)) return true;

        int depth = 0;
        int start = 0;
        for (int index = 0; index <= hooks.Length; index++)
        {
            bool atEnd = index == hooks.Length;
            char ch = atEnd ? '\0' : hooks[index];
            if (!atEnd)
            {
                if (ch == '(') depth++;
                else if (ch == ')')
                {
                    depth--;
                    if (depth < 0) return false;
                }
            }

            if (!atEnd && (ch != ';' || depth != 0)) continue;
            if (atEnd && depth != 0) return false;

            string part = hooks.Substring(start, index - start).Trim();
            if (part.Length == 0
                || !TryParse(part, out string functionName, out string argument))
                return false;
            calls.Add(new HookCall
            {
                Text = part,
                FunctionName = functionName,
                Argument = argument
            });
            start = index + 1;
        }
        return calls.Count > 0;
    }

    /// <summary>查询并列列表中是否包含指定函数名，并返回该项参数。</summary>
    public static bool TryFind(string hooks, string functionName, out string argument)
    {
        argument = string.Empty;
        if (!TryParseAll(hooks, out var calls)) return false;
        foreach (HookCall call in calls)
        {
            if (call.FunctionName != functionName) continue;
            argument = call.Argument;
            return true;
        }
        return false;
    }
}
