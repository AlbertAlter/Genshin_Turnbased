using System;

/// <summary>执行次数可由正整数或已登记的非负整数处理器决定；处理器支持Name与Name()两种无参写法。</summary>
public static class ExecuteCountEvaluator
{
    public static bool IsSupportedHandler(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        string text = value.Trim();
        if (!ScriptHookParser.TryParse(text, out string functionName, out string argument))
            return false;
        return BattleSpecialRules.HasCountHandler(functionName) && string.IsNullOrEmpty(argument);
    }

    public static int Evaluate(int fixedCount, string handler, BattleEntity caster)
    {
        if (string.IsNullOrWhiteSpace(handler))
        {
            if (fixedCount < 1) throw new ArgumentOutOfRangeException(nameof(fixedCount));
            return fixedCount;
        }
        if (!IsSupportedHandler(handler))
            throw new NotSupportedException($"ExecuteCount 尚未实现处理器：{handler}");

        ScriptHookParser.TryParse(handler.Trim(), out string functionName, out _);
        int count = BattleSpecialRules.EvaluateCount(functionName, caster);
        if (count < 0)
            throw new InvalidOperationException($"ExecuteCount 处理器返回负数：{handler} -> {count}");
        LogManager.Log(LogCategory.Effect, $"ExecuteCount {handler} -> {count}");
        return count;
    }
}
