using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>敌人技能标签及冷却引用；标签大小写敏感，不按显示名推断。</summary>
public static class EnemySkillTags
{
    public static List<string> Parse(string value)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(value)) return result;
        foreach (string part in value.Split(';'))
        {
            string tag = part.Trim();
            if (tag.Length > 0 && !result.Contains(tag)) result.Add(tag);
        }
        return result;
    }

    public static bool TryGetTag(string reference, out string tag)
    {
        tag = null;
        string value = reference?.Trim();
        if (value == null || !value.StartsWith("Tag(", StringComparison.Ordinal) || !value.EndsWith(")", StringComparison.Ordinal)) return false;
        string candidate = value.Substring(4, value.Length - 5).Trim();
        if (candidate.Length == 0 || candidate.IndexOfAny(new[] { ';', '(', ')' }) >= 0) return false;
        tag = candidate;
        return true;
    }

    public static bool IsValidReference(string reference)
    {
        if (TryGetTag(reference, out _)) return true;
        return CooldownTarget.IsValid(reference) && !CooldownTarget.TryGetButtonIndex(reference, out _);
    }

    public static bool Matches(EnemySkillMainData skill, string reference)
    {
        if (skill == null || !IsValidReference(reference)) return false;
        if (TryGetTag(reference, out string tag)) return skill.SkillTags != null && skill.SkillTags.Contains(tag);
        string value = reference.Trim();
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            return skill.EnemySkillID == id;
        return string.Equals(skill.EnemySkillID2, value, StringComparison.Ordinal);
    }
}

public partial class EnemyBattleController
{
    private const string RandomCharacterCooldown = "Random(Skill;Burst)";

    public static bool IsValidCooldownReference(string reference)
        => EnemySkillTags.IsValidReference(reference) || CooldownTarget.TryGetButtonIndex(reference, out _)
            || reference?.Trim() == RandomCharacterCooldown;

    /// <summary>
    /// 修改本敌人当前生命阶段的技能冷却；显式ID或Tag可匹配该阶段的全部模式。
    /// 同标签多技能全部修改一次，最低0；null 直接清零。保留原始CD、模式、使用次数与行动池。
    /// </summary>
    public bool ChangeSkillCooldown(string reference, int? delta)
    {
        if (dm == null || !EnemySkillTags.IsValidReference(reference)) return false;
        dm.EnemyAIRuleDict.TryGetValue(EnemyID, out var rules);
        var matches = new List<int>();
        foreach (var skill in dm.EnemySkillMainDict.Values)
        {
            // 编号归属包含仅由状态调用的技能；AI规则补充无阶段敌人的既有共用技能。
            bool owned = skill.EnemySkillID / 100 == EnemyID
                || (rules != null && rules.Exists(rule => rule != null && rule.SkillID == skill.EnemySkillID));
            if (!owned || !IsCurrentPhaseSkill(skill) || !EnemySkillTags.Matches(skill, reference)) continue;
            _skillCooldownLeft.TryGetValue(skill.EnemySkillID, out int current);
            if ((long)current + delta.GetValueOrDefault() > int.MaxValue) return false; // 本目标整批拒绝，不留下部分修改。
            matches.Add(skill.EnemySkillID);
        }
        matches.Sort();
        foreach (int id in matches)
        {
            _skillCooldownLeft.TryGetValue(id, out int current);
            long next = delta.HasValue ? (long)current + delta.Value : 0;
            if (next <= 0) _skillCooldownLeft.Remove(id);
            else _skillCooldownLeft[id] = (int)next;
            LogManager.Log(LogCategory.Skill, $"敌人{EnemyID} Cooldown {id}: {current}->{Math.Max(0L, next)}");
        }
        return matches.Count > 0;
    }

    private void ExecuteEnemyCooldown(EnemySkillEffectData effect, StatusInstance sourceStatus)
    {
        if (!IsValidCooldownReference(effect.Param1) || !CooldownParameters.TryParse(effect.Param2, out int? delta))
            throw new FormatException("敌人Cooldown需要有效技能引用，Param2为有符号整数或 Reset。");
        var seen = new HashSet<BattleEntity>();
        foreach (var target in ResolveEnemyTargets(effect, sourceStatus))
        {
            // 一次效果对同一单位只改一次；ExecuteCount仍可显式重复整个效果。
            if (target == null || !seen.Add(target)) continue;
            if (target.CharacterCtrl != null)
            {
                string reference = effect.Param1.Trim();
                if (reference == RandomCharacterCooldown)
                    reference = BattleRandom.NextInt(0, 2) == 0 ? "Skill" : "Burst";
                target.CharacterCtrl.ChangeSkillCooldown(reference, delta);
            }
            else if (target.EnemyCtrl != null && !target.EnemyCtrl.ChangeSkillCooldown(effect.Param1, delta))
                LogManager.Log(LogCategory.Skill, $"敌人{target.EntityID} Cooldown跳过：未匹配技能或冷却溢出，引用={effect.Param1}");
        }
    }
}
