using System;
using System.Globalization;

/// <summary>费用=ceil(基础AP*max(0.1,各折扣连乘)+固定增减)，最低0；Free优先。预览和扣费共用。</summary>
public static class APCostResolver
{
    private sealed class Modifiers
    {
        public long Flat;
        public bool Free;
        public readonly System.Collections.Generic.List<(decimal Value, int Count)> Factors
            = new System.Collections.Generic.List<(decimal, int)>();
    }

    /// <summary>只接受带*前缀的非负十进制系数；裸小数不解释为折扣。decimal避免0.1*0.2向上取整多扣1AP。</summary>
    public static bool TryParseMultiplier(string value, out decimal multiplier)
    {
        multiplier = 0m;
        string text = value?.Trim();
        return text != null && text.StartsWith("*", StringComparison.Ordinal)
            && decimal.TryParse(text.Substring(1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out multiplier)
            && multiplier >= 0m;
    }
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

    /// <summary>skillType是本次实际发动的按钮槽位0～3；系统调用传-1，不用倍率SkillType或ActionType猜槽位。</summary>
    public static int ForSkill(BattleEntity actor, SkillMainData skill, int skillType)
    {
        if (skill == null) return 0;
        return Calculate(skill.APCost, ReadModifiers(actor, ResolveSkillOperation(skillType), skill.SkillID2));
    }

    // 卡齐娜的特殊战技在普攻槽时仍吃Normal减费；蓄力起手在重击槽时吃Heavy减费但不因此声明行为。
    private static string ResolveSkillOperation(int skillType) => skillType switch
    {
        0 => "Normal",
        1 => "Heavy",
        2 => "Skill",
        3 => "Burst",
        _ => null
    };

    public static int ForSwitch(BattleEntity outgoing, BattleEntity incoming, int baseCost, bool free)
    {
        if (free) return 0;
        var outgoingModifiers = ReadModifiers(outgoing, "SwitchOut");
        var incomingModifiers = ReadModifiers(incoming, "SwitchIn");
        outgoingModifiers.Free |= incomingModifiers.Free;
        outgoingModifiers.Flat += incomingModifiers.Flat;
        outgoingModifiers.Factors.AddRange(incomingModifiers.Factors);
        // 双方折扣和固定值分别合并，只在完整乘积上取0.1下限，最后统一取整。
        return Calculate(baseCost, outgoingModifiers);
    }

    public static long GetModifier(BattleEntity actor, string operation)
    {
        // 兼容旧查询：这里只返回固定增减，不能用本方法代替完整费用计算。
        return ReadModifiers(actor, operation).Flat;
    }

    private static Modifiers ReadModifiers(BattleEntity actor, string operation, string skillID = null)
    {
        var result = new Modifiers();
        var data = DataManager.Instance;
        if (actor == null || data == null || string.IsNullOrEmpty(operation)) return result;
        result.Flat = BattleManager.Instance?.TeamBonuses?.Resonance.APDelta(actor, operation) ?? 0;
        var used = new System.Collections.Generic.Dictionary<string, int>();
        foreach (var status in actor.GetEffectiveStatusList())
        {
            if (status == null || !status.IsActive || status.MainData == null) continue;
            int count = StatusAggregation.Take(status, used);
            if (count == 0) continue;
            foreach (var effect in data.StatusEffectDict.Values)
            {
                if (effect == null || effect.EffectType != "APCost" || effect.StatusEffectID == 0
                    || effect.StatusEffectID / 100 != status.MainData.StatusID
                    || !MatchesOperation(effect.Param2, operation)) continue;
                if (!ScriptHookEvaluator.Evaluate(effect.ScriptHook, new ScriptHookContext
                    { Caster = status.Caster ?? actor, Status = status, StatusHost = StatusHostAccess.FindHost(status, actor), Target = actor,
                        APCostSkillID = skillID })) continue;
                if (IsFreeValue(effect.Param1))
                {
                    result.Free = true;
                    continue;
                }
                if (TryParseMultiplier(effect.Param1, out decimal multiplier))
                {
                    result.Factors.Add((multiplier, count));
                    continue;
                }
                int delta;
                if (WeaponHooks.TryResolveAPCost(effect, status, out delta)
                    || int.TryParse(effect.Param1, NumberStyles.Integer, CultureInfo.InvariantCulture, out delta))
                    result.Flat += (long)delta * count;
            }
        }
        return result;
    }

    private static int Calculate(int baseCost, Modifiers modifiers)
    {
        if (modifiers.Free) return 0;
        decimal percent = 1m;
        // 0系数同样受最终0.1下限约束；不等同于Free。
        if (modifiers.Factors.Exists(factor => factor.Value == 0m)) percent = 0m;
        else
            foreach (var factor in modifiers.Factors)
            {
                decimal value = factor.Value;
                int count = factor.Count;
                while (count > 0)
                {
                    if ((count & 1) != 0) percent = checked(percent * value);
                    count >>= 1;
                    if (count > 0) value = checked(value * value);
                }
            }
        percent = Math.Max(0.1m, percent);
        if (baseCost > 0 && percent > ((decimal)int.MaxValue - modifiers.Flat) / baseCost) return int.MaxValue;
        decimal cost = baseCost * percent + modifiers.Flat;
        return (int)Math.Max(0m, Math.Min(int.MaxValue, decimal.Ceiling(cost)));
    }
}
