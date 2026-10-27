using System;
using System.Collections.Generic;

/// <summary>
/// 单个目标、单次命中的承伤区快照。先读取受击前的状态与钩子，
/// 反应确定后仅筛选数值，不重新求值钩子，也不改变 Count。
/// </summary>
public sealed class DMGTakenSnapshot
{
    private readonly KeyValuePair<string, float>[] _contributions;

    internal DMGTakenSnapshot(List<KeyValuePair<string, float>> contributions)
    {
        _contributions = contributions.ToArray();
    }

    public float GetMultiplier(ReactionType reactionType)
    {
        float total = 0f;
        foreach (var contribution in _contributions)
        {
            string filter = contribution.Key;
            bool matches = string.IsNullOrWhiteSpace(filter);
            if (!matches)
            {
                // All仍限定真实反应，不把普通无反应伤害算进反应专属承伤。
                matches = reactionType != ReactionType.None && Enum.IsDefined(typeof(ReactionType), reactionType)
                    && ReactionFilterMatcher.Matches(filter, reactionType, reactionType.ToString());
            }
            if (matches) total += contribution.Value;
        }
        // 负值可表达承伤降低，但不能把伤害变成治疗。
        return float.IsNaN(total) || float.IsInfinity(total) ? 1f : Math.Max(0f, 1f + total);
    }
}

public partial class BattleEntity
{
    /// <summary>
    /// 只汇总受击方及其所在位置的 DMGTaken；来源方的 DMGBonus 是另外一个乘区。
    /// damageType 必须传实际效果标签，空标签不冒充 Skill/Normal 等伤害。
    /// </summary>
    public DMGTakenSnapshot PrepareDMGTaken(string effectID, string skillID, string damageType, string element)
    {
        var contributions = new List<KeyValuePair<string, float>>();
        var dm = DataManager.Instance;
        if (dm == null || dm.StatusEffectDict == null) return new DMGTakenSnapshot(contributions);

        var used = new Dictionary<string, int>();
        foreach (StatusInstance status in GetEffectiveStatusList())
        {
            StatusMainData main = status?.MainData;
            if (main == null || !status.IsActive || main.GetMultiplierPart() != "DMGTaken") continue;
            if (!MatchesDMGTakenFilter(main.ApplyString, effectID, skillID)) continue;
            if (!MatchesDMGTakenFilter(main.ApplyDamageType, damageType)) continue;
            if (!MatchesDMGTakenFilter(main.ApplyElementType, element)) continue;

            var context = new ScriptHookContext
            {
                Caster = status.Caster,
                Target = this,
                Status = status,
                Recipient = this,
                StatusHost = StatusHostAccess.FindHost(status, this),
                HitEffectID = effectID,
                DamageSkillID = skillID,
                DamageType = damageType,
                DamageElement = element
            };
            if (!ScriptHookEvaluator.Evaluate(main.ScriptHook, context)) continue;
            int count = StatusAggregation.Take(status, used);
            if (count == 0) continue;

            float value = 0f;
            bool collected = false;
            foreach (StatusEffectData effect in dm.StatusEffectDict.Values)
            {
                if (effect == null || effect.StatusEffectID / 100 != main.StatusID
                    || (effect.EffectType != "Buff" && effect.EffectType != "Debuff")) continue;
                if (!ScriptHookEvaluator.Evaluate(effect.ScriptHook, context)) continue;
                if (!HexereiStatusEnhancement.TryResolveScalar(effect, status, out float scalar)) continue;
                if (float.IsNaN(scalar) || float.IsInfinity(scalar)) continue;
                value += scalar * count;
                collected = true; // 合法的0也占额度；全部条件/解析失败才退还。
            }
            if (collected) contributions.Add(new KeyValuePair<string, float>(main.ApplyReactionType, value));
            else if (main.MaxCount > 0)
            {
                string id = status.StatusID2 ?? main.StatusID.ToString();
                used[id] -= count;
                if (used[id] == 0) used.Remove(id);
            }
        }
        return new DMGTakenSnapshot(contributions);
    }

    private static bool MatchesDMGTakenFilter(string configured, params string[] actualValues)
    {
        if (string.IsNullOrWhiteSpace(configured)) return true;
        foreach (string raw in configured.Split(';'))
        {
            string token = raw.Trim();
            if (token.Length == 0) continue;
            if (string.Equals(token, "All", StringComparison.OrdinalIgnoreCase)) return true;
            foreach (string actual in actualValues)
                if (!string.IsNullOrWhiteSpace(actual)
                    && string.Equals(token, actual.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
