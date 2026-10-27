using System;
using System.Collections.Generic;

/// <summary>
/// Immune状态的被动伤害免疫。元素完全来自所属Immune效果的Element，不按状态ID、固有元素或抗性推断。
/// 正式伤害入口每目标调用一次；条件钩子可能取随机，UI只读取已提交结果的IsImmune，不重新判定。
/// </summary>
public static class DamageImmunity
{
    public static bool IsImmuneType(string value)
        => string.Equals(value?.Trim(), "Immune", StringComparison.OrdinalIgnoreCase);

    public static string[] ParseElements(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new TableValidationException("Immune的Element必须明确填写免疫元素，不能留空或默认全部。");
        var elements = new List<string>();
        foreach (string part in value.Split(';'))
        {
            string token = NormalizeElement(part);
            if (token == null)
                throw new TableValidationException("Immune的Element仅支持七元素、Physical（物理）或None（直接伤害），英文分号并列；不支持空分段、All或True：" + value);
            if (!elements.Contains(token)) elements.Add(token);
        }
        return elements.ToArray();
    }

    private static string NormalizeElement(string value)
    {
        string token = value?.Trim();
        foreach (string element in ElementQuery.Elements)
            if (string.Equals(token, element, StringComparison.OrdinalIgnoreCase)) return element;
        if (string.Equals(token, "Physical", StringComparison.OrdinalIgnoreCase)) return "Physical";
        if (string.Equals(token, "None", StringComparison.OrdinalIgnoreCase)) return "None";
        return null;
    }

    public static bool Resolve(BattleEntity target, string element, string effectID)
    {
        if (target == null || !target.IsAlive || DamageProperties.IsTrue(element)) return false;
        string actual = NormalizeElement(element);
        DataManager data = DataManager.Instance;
        if (actual == null || data == null) return false;
        foreach (StatusInstance status in target.GetEffectiveStatusList())
        {
            if (status == null || !status.IsActive || !IsImmuneType(status.MainData?.StatusType)) continue;
            var effects = new List<StatusEffectData>();
            foreach (StatusEffectData effect in data.StatusEffectDict.Values)
                if (effect != null && IsImmuneType(effect.EffectType)
                    && effect.StatusEffectID / 100 == status.MainData.StatusID
                    && Array.IndexOf(ParseElements(effect.Element), actual) >= 0) effects.Add(effect);
            if (effects.Count == 0) continue;
            effects.Sort((left, right) => left.StatusEffectID.CompareTo(right.StatusEffectID));
            var context = new ScriptHookContext
            {
                Caster = status.Caster, Target = target, Recipient = target, Status = status,
                StatusHost = StatusHostAccess.FindHost(status, target), HitEffectID = effectID
            };
            if (!ScriptHookEvaluator.Evaluate(status.MainData.ScriptHook, context)) continue;
            foreach (StatusEffectData effect in effects)
                if (ScriptHookEvaluator.Evaluate(effect.ScriptHook, context)) return true;
        }
        return false;
    }

    /// <summary>整套配表加载后校验归属；运行时不把缺效果的Immune解释成全免疫。</summary>
    public static void ValidateDefinitions(DataManager data)
    {
        foreach (SkillEffectData effect in data.SkillEffectDict.Values)
            if (effect != null && IsImmuneType(effect.EffectType))
                throw new TableValidationException("Immune是状态被动效果，技能须通过ApplyStatus施加：" + effect.SkillEffectID2);
        foreach (EnemySkillEffectData effect in data.EnemySkillEffectDict.Values)
            if (effect != null && IsImmuneType(effect.EffectType))
                throw new TableValidationException("Immune是状态被动效果，敌人技能须通过ApplyStatus施加：" + effect.SkillEffectID2);

        var configured = new HashSet<int>();
        foreach (StatusEffectData effect in data.StatusEffectDict.Values)
        {
            if (effect == null || !IsImmuneType(effect.EffectType)) continue;
            ValidateEffect(effect);
            bool found = false;
            foreach (StatusMainData main in data.StatusMainDict.Values)
                if (main != null && main.StatusID == effect.StatusEffectID / 100 && IsImmuneType(main.StatusType))
                { found = true; configured.Add(main.StatusID); break; }
            if (!found)
                throw new TableValidationException("Immune效果必须属于StatusType=Immune的状态：" + effect.StatusEffectID2);
        }
        foreach (StatusMainData main in data.StatusMainDict.Values)
            if (main != null && IsImmuneType(main.StatusType)
                && (!configured.Contains(main.StatusID) || !string.IsNullOrEmpty(main.GetMultiplierPart())))
                throw new TableValidationException("Immune状态须配置至少一条Immune效果，MultiplierPart留空：" + main.StatusID2);
    }

    public static void ValidateEffect(StatusEffectData effect)
    {
        if (effect.StatusEffectID <= 0)
            throw new TableValidationException("Immune效果须填写正整数StatusEffectID，以关联所属状态：" + effect.StatusEffectID2);
        ParseElements(effect.Element);
        if (!string.IsNullOrWhiteSpace(effect.Param1) || !string.IsNullOrWhiteSpace(effect.Param2)
            || !string.IsNullOrWhiteSpace(effect.Param3) || !string.IsNullOrWhiteSpace(effect.TargetType)
            || !string.IsNullOrWhiteSpace(effect.TargetSelect) || !string.IsNullOrWhiteSpace(effect.TargetOverride)
            || effect.TargetConsecutive != 0 || effect.Duration != 0 || effect.AddInPhase != 0 || effect.TriggerPhase != 0
            || !string.IsNullOrWhiteSpace(effect.DamageType) || !string.IsNullOrWhiteSpace(effect.DamageHook)
            || effect.ExecuteCount != 1 || !string.IsNullOrWhiteSpace(effect.ExecuteCountHandler))
            throw new TableValidationException("Immune是随状态存在的被动效果，仅填写Element及可选ScriptHook，不使用Param、目标、伤害、重复执行或计时字段：" + effect.StatusEffectID2);
    }
}
