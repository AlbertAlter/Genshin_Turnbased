using System;
using System.Globalization;

/// <summary>ResetCooldown 的 Param1 语法；按钮类型名忽略大小写，SK_ 字符串 ID 保持精确匹配。</summary>
public static class ResetCooldownTarget
{
    public static bool TryGetButtonIndex(string reference, out int index)
    {
        index = -1;
        if (string.IsNullOrWhiteSpace(reference)) return false;
        switch (reference.Trim().ToLowerInvariant())
        {
            case "normal": index = 0; return true;
            case "heavy": index = 1; return true;
            case "skill": index = 2; return true;
            case "burst": index = 3; return true;
            default: return false;
        }
    }

    public static bool IsValid(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return false;
        int buttonIndex;
        if (TryGetButtonIndex(reference, out buttonIndex)) return true;
        string value = reference.Trim();
        int id;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) return id > 0;
        if (!value.StartsWith("SK_", StringComparison.Ordinal) || value.Length <= 3) return false;
        foreach (char character in value)
            if (char.IsWhiteSpace(character) || character == ';') return false;
        return true;
    }
}

public partial class CharacterBattleController
{
    /// <summary>
    /// 完成指定技能的一轮冷却；类型名按当前按钮绑定解析，数字/字符串 ID 只匹配本角色技能。
    /// 沿用自然冷却结束的次数恢复规则，不补满次数、不改绑定、不解除 Freeze。
    /// </summary>
    public bool ResetSkillCooldown(string reference)
    {
        string skillID;
        if (!TryResolveCooldownSkill(reference, out skillID))
        {
            LogManager.LogWarning(LogCategory.Skill,
                "ResetCooldown 无法解析本角色技能: [" + reference + "]");
            return false;
        }

        CompleteSkillCooldown(skillID);
        // 明确指定被 ChangeControl 换下的技能时，同步其恢复快照，避免返回按钮后旧冷却复活。
        foreach (ControlChangeRecord record in _ccRecords)
        {
            if (record.IsFreeze || record.OriginalSkillID != skillID) continue;
            int remainingCooldown;
            record.OrigCooldownSnapshot = _cooldownRemaining != null
                && _cooldownRemaining.TryGetValue(skillID, out remainingCooldown) ? remainingCooldown : 0;
            int charges;
            if (_currentCharges != null && _currentCharges.TryGetValue(skillID, out charges))
                record.OrigChargeSnapshot = charges;
        }

        LogManager.Log(LogCategory.Skill, "ResetCooldown " + reference + " -> " + skillID);
        if (OnControlChanged != null) OnControlChanged();
        return true;
    }

    void ExecuteResetCooldown(string reference, string effectID)
    {
        if (!ResetSkillCooldown(reference))
            LogManager.LogWarning(LogCategory.Effect, effectID + " ResetCooldown 未执行");
    }

    bool TryResolveCooldownSkill(string reference, out string skillID)
    {
        skillID = null;
        if (_skillById2 == null || !ResetCooldownTarget.IsValid(reference)) return false;
        int buttonIndex;
        if (ResetCooldownTarget.TryGetButtonIndex(reference, out buttonIndex))
        {
            // 不使用角色初始技能，也不追溯当前伤害的来源技能。
            skillID = GetBoundSkillID(buttonIndex);
            return !string.IsNullOrEmpty(skillID) && _skillById2.ContainsKey(skillID);
        }

        string value = reference.Trim();
        if (_skillById2.ContainsKey(value))
        {
            skillID = value;
            return true;
        }
        int numericID;
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out numericID)) return false;
        foreach (var entry in _skillById2)
        {
            if (entry.Value == null || entry.Value.SkillID != numericID) continue;
            skillID = entry.Key;
            return true;
        }
        return false;
    }

    /// <summary>有剩余次数即可继续使用；当前充能计时只在次数耗尽时阻止施放。</summary>
    bool HasSkillUseAvailable(string skillID, SkillMainData skill)
    {
        if (skill == null) return false;
        int value;
        if (skill.MaxCharge > 0)
            return _currentCharges != null && _currentCharges.TryGetValue(skillID, out value) && value > 0;
        return _cooldownRemaining == null || !_cooldownRemaining.TryGetValue(skillID, out value) || value <= 0;
    }

    /// <summary>消耗一次使用次数；后续使用不覆盖已开始的首轮冷却进度。</summary>
    void ConsumeSkillUse(string skillID, SkillMainData skill)
    {
        int current;
        if (skill.MaxCharge > 0 && _currentCharges.TryGetValue(skillID, out current))
            _currentCharges[skillID] = current - 1;
        int cooldown;
        if (skill.Cooldown > 0 && (!_cooldownRemaining.TryGetValue(skillID, out cooldown) || cooldown <= 0))
            _cooldownRemaining[skillID] = (int)Math.Ceiling(skill.Cooldown);
    }

    /// <summary>完成一轮并恢复一次；仍缺次数时，下一轮从完整冷却重新开始，不继承旧进度。</summary>
    bool CompleteSkillCooldown(string skillID)
    {
        int cooldown;
        if (_cooldownRemaining == null || !_cooldownRemaining.TryGetValue(skillID, out cooldown)
            || cooldown <= 0) return false;
        _cooldownRemaining[skillID] = 0;
        int current;
        SkillMainData skill;
        if (_currentCharges != null && _currentCharges.TryGetValue(skillID, out current)
            && _skillById2 != null && _skillById2.TryGetValue(skillID, out skill)
            && skill != null && skill.MaxCharge > 0 && current < skill.MaxCharge)
        {
            _currentCharges[skillID] = current + 1;
            if (current + 1 < skill.MaxCharge && skill.Cooldown > 0)
                _cooldownRemaining[skillID] = (int)Math.Ceiling(skill.Cooldown);
        }
        return true;
    }
}
