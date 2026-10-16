using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>NightSoul.Param1为有符号int增减量；不解析float、不取整、不改变上限。</summary>
public static class NightSoulEffect
{
    public static bool IsValidAmount(string expression)
    {
        int value;
        return int.TryParse(expression, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public static void Execute(string expression, IEnumerable<BattleEntity> targets, string effectID = null)
    {
        int delta;
        if (!int.TryParse(expression, NumberStyles.Integer, CultureInfo.InvariantCulture, out delta))
            throw new FormatException("NightSoul.Param1必须是有符号int增减量，不接受小数或公式：[" + expression + "]");
        if (targets == null) return;
        var seen = new HashSet<BattleEntity>();
        var prepared = new List<BattleEntity>();
        foreach (BattleEntity target in targets)
        {
            if (target == null || !target.IsAlive || !seen.Add(target)) continue;
            // 先确认整批目标都已配置上限，再提交，避免半批成功。
            NightSoulStatusHandler.RequireInitialized(target);
            prepared.Add(target);
        }
        foreach (BattleEntity target in prepared)
        {
            NightSoulValueChange change = NightSoulStatusHandler.ChangeValue(target, delta);
            LogManager.Log(LogCategory.Effect, "NightSoul " + effectID + " -> " + target.EntityID
                + " " + change.Before + " -> " + change.After);
        }
    }
}
