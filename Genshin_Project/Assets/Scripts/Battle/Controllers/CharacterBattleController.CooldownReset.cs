using System;
using System.Globalization;

/// <summary>ResetCooldown / ReduceCooldown 的 Param1 语法；按钮类型名忽略大小写，SK_ 字符串 ID 保持精确匹配。</summary>
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

/// <summary>ReduceCooldown.Param2 只接受非负 int 回合数，不接受空值、小数或公式。</summary>
public static class ReduceCooldownParameters
{
    public static bool TryParseAmount(string value, out int rounds)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out rounds)
            && rounds >= 0;
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
        SyncSelectionPhaseRuntime(skillID);
        SyncCooldownControlSnapshots(skillID);

        LogManager.Log(LogCategory.Skill, "ResetCooldown " + reference + " -> " + skillID);
        if (OnControlChanged != null) OnControlChanged();
        return true;
    }

    /// <summary>
    /// 减少本角色指定技能当前一轮的冷却，下限 0。完成时沿用自然恢复一次的规则；
    /// 超出的减量不带入下一轮充能，不改变按钮绑定，也不解除 Freeze。
    /// 返回 false 表示参数/技能无效；合法的 0 减量或无进行中的冷却是成功的空操作。
    /// </summary>
    public bool ReduceSkillCooldown(string reference, int rounds)
    {
        string skillID;
        if (rounds < 0 || !TryResolveCooldownSkill(reference, out skillID))
        {
            LogManager.LogWarning(LogCategory.Skill,
                "ReduceCooldown 参数或本角色技能无效: [" + reference + "], " + rounds);
            return false;
        }

        int cooldown;
        if (rounds == 0 || _cooldownRemaining == null
            || !_cooldownRemaining.TryGetValue(skillID, out cooldown) || cooldown <= 0)
            return true;

        // 先比较后相减，避免大减量溢出；只完成当前正在进行的一轮。
        if (rounds >= cooldown) CompleteSkillCooldown(skillID);
        else _cooldownRemaining[skillID] = cooldown - rounds;
        SyncSelectionPhaseRuntime(skillID);
        SyncCooldownControlSnapshots(skillID);
        LogManager.Log(LogCategory.Skill,
            "ReduceCooldown " + reference + " -> " + skillID + ", -" + rounds);
        if (OnControlChanged != null) OnControlChanged();
        return true;
    }

    void SyncCooldownControlSnapshots(string skillID)
    {
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
    }

    void ExecuteResetCooldown(string reference, string effectID)
    {
        if (!ResetSkillCooldown(reference))
            LogManager.LogWarning(LogCategory.Effect, effectID + " ResetCooldown 未执行");
    }

    void ExecuteReduceCooldown(string reference, string amount, string effectID)
    {
        int rounds;
        if (!ReduceCooldownParameters.TryParseAmount(amount, out rounds))
        {
            LogManager.LogWarning(LogCategory.Effect,
                effectID + " ReduceCooldown.Param2 必须为非负 int，当前=[" + amount + "]");
            return;
        }
        if (!ReduceSkillCooldown(reference, rounds))
            LogManager.LogWarning(LogCategory.Effect, effectID + " ReduceCooldown 未执行");
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
            _cooldownRemaining[skillID] = BattleSpecialRules.ResolveCastCooldown(Entity, skill, (int)Math.Ceiling(skill.Cooldown));
        SyncSelectionPhaseRuntime(skillID);
    }

    /// <summary>同SkillPhase是同一按钮的选段变体，资源随实际施放档位，不是ChangeControl换下的独立技能。</summary>
    void SyncSelectionPhaseRuntime(string sourceID)
    {
        if (!_skillById2.TryGetValue(sourceID,out var source) || source.SkillPhase<=0) return;
        foreach(var pair in _skillById2)
        {
            if(pair.Key==sourceID || pair.Value.SkillPhase!=source.SkillPhase)continue;
            if(_cooldownRemaining.TryGetValue(sourceID,out int cd))_cooldownRemaining[pair.Key]=cd;
            if(source.MaxCharge>0 && pair.Value.MaxCharge>0 && _currentCharges.TryGetValue(sourceID,out int charge))
                _currentCharges[pair.Key]=Math.Min(charge,pair.Value.MaxCharge);
            SyncCooldownControlSnapshots(pair.Key);
        }
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
        SyncSelectionPhaseRuntime(skillID);
        return true;
    }
}
