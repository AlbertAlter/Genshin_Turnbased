/// <summary>
/// ScriptHook 文本解析（2026-08-19，从 ScriptHookEvaluator 独立）：
/// 支持"函数名(参数)"与裸函数名（无参数，如 Kaeya_T2 / PreAlliesDamage）。
/// ID大小写严格匹配，只允许去除首尾空格。
/// </summary>
public static class ScriptHookParser
{
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
}
