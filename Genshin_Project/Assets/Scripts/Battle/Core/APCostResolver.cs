using System;
using System.Globalization;

/// <summary>APCost 被动费用修正。Free优先于所有数值增减；只查询状态，供 UI 和正式扣费共用。</summary>
public static class APCostResolver
{
    public static bool IsOperation(string operation)
    {
        return operation == "Normal" || operation == "Heavy" || operation == "Skill"
            || operation == "Burst" || operation == "SwitchIn" || operation == "SwitchOut";
    }

    public static bool IsFreeValue(string value)
    {
        return string.Equals(value == null ? null : value.Trim(), "Free", StringComparison.OrdinalIgnoreCase);
    }

    public static bool MatchesOperation(string configured, string operation)
    {
        if (string.IsNullOrWhiteSpace(configured) || string.IsNullOrEmpty(operation)) return false;
        foreach (string token in configured.Split(';'))
            if (token.Trim() == operation) return true;
        return false;
    }

    public static int ForSkill(BattleEntity actor, SkillMainData skill)
    {
        if (skill == null) return 0;
        bool free;
        long modifier = ReadModifier(actor, skill.ActionType, out free);
        return free ? 0 : Clamp((long)skill.APCost + modifier);
    }

    public static int ForSwitch(BattleEntity outgoing, BattleEntity incoming, int baseCost, bool free)
    {
        if (free) return 0;
        bool outgoingFree, incomingFree;
        long outgoingModifier = ReadModifier(outgoing, "SwitchOut", out outgoingFree);
        long incomingModifier = ReadModifier(incoming, "SwitchIn", out incomingFree);
        if (outgoingFree || incomingFree) return 0;
        // 双方先相加，最后统一截断，不能先截断某一方的减费。
        return Clamp((long)baseCost + outgoingModifier + incomingModifier);
    }

    public static long GetModifier(BattleEntity actor, string operation)
    {
        bool free;
        return ReadModifier(actor, operation, out free);
    }

    private static long ReadModifier(BattleEntity actor, string operation, out bool free)
    {
        free = false;
        var data = DataManager.Instance;
        if (actor == null || data == null || string.IsNullOrEmpty(operation)) return 0;
        long total = 0;
        foreach (var status in actor.GetEffectiveStatusList())
        {
            if (status == null || !status.IsActive || status.MainData == null) continue;
            int count = Math.Max(1, status.StackCount);
            if (status.MainData.MaxCount > 0) count = Math.Min(count, status.MainData.MaxCount);
            foreach (var effect in data.StatusEffectDict.Values)
            {
                if (effect == null || effect.EffectType != "APCost" || effect.StatusEffectID == 0
                    || effect.StatusEffectID / 100 != status.MainData.StatusID
                    || !MatchesOperation(effect.Param2, operation)) continue;
                if (IsFreeValue(effect.Param1))
                {
                    free = true;
                    continue;
                }
                int delta;
                if (int.TryParse(effect.Param1, NumberStyles.Integer, CultureInfo.InvariantCulture, out delta))
                    total += (long)delta * count;
            }
        }
        return total;
    }

    private static int Clamp(long cost)
    {
        return (int)Math.Max(0L, Math.Min(int.MaxValue, cost));
    }
}
