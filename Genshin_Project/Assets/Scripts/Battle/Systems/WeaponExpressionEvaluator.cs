using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>武器状态表达式：Index(n) 从状态实例携带的精炼上下文读取。</summary>
public static class WeaponExpressionEvaluator
{
    private static readonly Regex IndexPattern = new Regex(@"Index\(\s*(\d+)\s*\)", RegexOptions.Compiled);

    public static string Expand(string expression, StatusInstance status)
    {
        if (string.IsNullOrEmpty(expression) || status?.WeaponContext == null) return expression;
        return IndexPattern.Replace(expression, match =>
        {
            if (!int.TryParse(match.Groups[1].Value, out int index)) return "0";
            return status.WeaponContext.ResolveParam(index).ToString("R", CultureInfo.InvariantCulture);
        });
    }

    public static bool TryResolveScalar(string expression, StatusInstance status, out float value)
    {
        return float.TryParse(Expand(expression, status), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
