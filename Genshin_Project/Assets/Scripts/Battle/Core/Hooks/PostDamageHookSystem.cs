using System.Collections.Generic;

/// <summary>
/// PostAlliesDamage / PostSelfDamage 的最近一次命中位置缓存。
/// 状态行动在一个 Damage 效果完整结算后写入，TargetOverride 随后按钩子参数读取。
/// </summary>
public static class PostDamageHookSystem
{
    private static readonly Dictionary<string, List<int>> PositionsByHookArgument
        = new Dictionary<string, List<int>>();

    public static void SetPositions(string hookArgument, IEnumerable<int> positions)
    {
        if (string.IsNullOrWhiteSpace(hookArgument)) return;
        var result = new List<int>();
        if (positions != null)
        {
            foreach (int position in positions)
                if (position > 0 && !result.Contains(position)) result.Add(position);
        }
        PositionsByHookArgument[hookArgument.Trim()] = result;
    }

    public static List<int> GetPositions(string hookArgument)
    {
        if (string.IsNullOrWhiteSpace(hookArgument)
            || !PositionsByHookArgument.TryGetValue(hookArgument.Trim(), out List<int> positions))
            return new List<int>();
        return new List<int>(positions);
    }

    public static void ClearAll()
    {
        PositionsByHookArgument.Clear();
    }
}
