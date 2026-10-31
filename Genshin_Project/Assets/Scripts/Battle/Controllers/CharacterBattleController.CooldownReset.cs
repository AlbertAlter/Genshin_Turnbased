using System;
using System.Globalization;

/// <summary>ResetCooldown / Cooldown 的 Param1 语法；按钮类型名忽略大小写，SK_ 字符串 ID 保持精确匹配。</summary>
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

/// <summary>Cooldown.Param2 为有符号 int 增减量：正数增加、负数减少、0 不变；不接受空值、小数或公式。</summary>
public static class CooldownParameters
{
    public static bool TryParseAmount(string value, out int rounds)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out rounds);
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
    /// 增减本角色指定技能当前一轮的冷却，下限 0。正数延长，负数缩短，完成时沿用自然恢复一次的规则；
    /// 超出的减量不带入下一轮充能，不改变按钮绑定，也不解除 Freeze。
    /// 普通技能（MaxCharge&lt;=0）允许正数从0开启冷却；充能技能满次数时将最后一次转回充能，否则调整当前轮。
    /// 返回 false 表示技能无效或增加后超出 int 范围；0增量以及对零冷却的负增量均为空操作。
    /// 满次数开启充能时可用次数减一，已有充能时不再扣次数；不重新调用施放冷却修正（例如重云二命）。
    /// </summary>
    public bool ChangeSkillCooldown(string reference, int rounds)
    {
        string skillID;
        if (!TryResolveCooldownSkill(reference, out skillID))
        {
            LogManager.LogWarning(LogCategory.Skill,
                "Cooldown 本角色技能无效: [" + reference + "], " + rounds);
            return false;
        }

        if (rounds == 0 || _cooldownRemaining == null) return true;
        _cooldownRemaining.TryGetValue(skillID, out int cooldown);
        bool reopenLastCharge = false;
        if (cooldown <= 0)
        {
            if (rounds < 0) return true;
            var skill = _skillById2[skillID];
            if (skill.MaxCharge > 0)
            {
                // 满次数时只把最后一次转回充能；缺次数却无计时的异常/特殊状态不在此补造计时。
                if (_currentCharges == null || !_currentCharges.TryGetValue(skillID, out int charges)
                    || charges != skill.MaxCharge) return true;
                reopenLastCharge = true;
            }
            cooldown = 0;
        }

        // 使用 long，包含 int.MinValue 在内的负量均不会取反溢出；只完成当前这一轮。
        long next = (long)cooldown + rounds;
        if (next > int.MaxValue)
        {
            LogManager.LogWarning(LogCategory.Skill, "Cooldown 增加后超出 int 范围: " + skillID);
            return false;
        }
        if (next <= 0) CompleteSkillCooldown(skillID);
        else
        {
            if (reopenLastCharge) _currentCharges[skillID]--;
            _cooldownRemaining[skillID] = (int)next;
        }
        SyncSelectionPhaseRuntime(skillID);
        SyncCooldownControlSnapshots(skillID);
        LogManager.Log(LogCategory.Skill,
            "Cooldown " + reference + " -> " + skillID + ", delta=" + rounds);
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

    void ExecuteCooldown(string reference, string amount, string effectID)
    {
        int rounds;
        if (!CooldownParameters.TryParseAmount(amount, out rounds))
        {
            LogManager.LogWarning(LogCategory.Effect,
                effectID + " Cooldown.Param2 必须为有符号 int 增减量，当前=[" + amount + "]");
            return;
        }
        if (!ChangeSkillCooldown(reference, rounds))
            LogManager.LogWarning(LogCategory.Effect, effectID + " Cooldown 未执行");
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
