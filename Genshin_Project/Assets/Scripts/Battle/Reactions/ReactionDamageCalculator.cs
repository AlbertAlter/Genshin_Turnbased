using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

[Serializable]
public struct ReactionBuffTotals
{
    public float DMGBonus;
    public float BaseDMGBonusFlat;
    // 独立乘区原始汇总，不可塞进DMGBonus后套用其他公式。
    // 普通增幅/激化的BaseDMGBonus进入各自反应精通括号，不作为整段技能增伤。
    public float BaseDMGBonus;
    public float LunarDMGBonus;
    public float StellarDMGBonus;
    public float Elevation;
    public float CritRate;
    public float CritDMG;
    public float EMBonus;
}

/// <summary>
/// 常规元素反应的纯数值计算与反应限定 Buff 收集。
/// 具体反应的元素消耗、范围和状态生命周期由各 ReactionHandler 负责。
/// </summary>
public static class ReactionDamageCalculator
{
    public static float GetLevelCoefficient(int level)
    {
        DataManager dataManager = DataManager.Instance;
        if (dataManager == null)
        {
            LogManager.LogError(LogCategory.Reaction, "ReactionLevelCoefficient 读取失败：DataManager 未初始化");
            return 0f;
        }

        return GetLevelCoefficient(dataManager.ReactionLevelCoefficientDict, level);
    }

    public static float GetLevelCoefficient(IReadOnlyDictionary<int, float> coefficients, int level)
    {
        if (coefficients == null || coefficients.Count == 0)
        {
            LogManager.LogError(LogCategory.Reaction, "ReactionLevelCoefficient 为空");
            return 0f;
        }

        int clampedLevel = Mathf.Clamp(level, 1, 100);
        if (coefficients.TryGetValue(clampedLevel, out float coefficient))
            return coefficient;

        LogManager.LogError(LogCategory.Reaction, $"ReactionLevelCoefficient 缺少等级 {clampedLevel}");
        return 0f;
    }

    public static float AmplifyingEMCoefficient(float totalEM)
    {
        float em = Mathf.Max(0f, totalEM);
        return 2.78f * em / (em + 1400f);
    }

    /// <summary>反应入口读取精通一次；已有来源快照时不重新查询状态或改写快照。</summary>
    public static float GetSourceMastery(ReactionContext context)
    {
        if (context?.SourceSnapshotOverride != null) return context.SourceSnapshotOverride.TotalEM;
        if (context?.SourceEntity == null) return 0f;
        using var damageQuery = DamageQueryContext.Enter(context.SourceEffectID, context.DamageType, context.SourceSkillID);
        var memory = StatusFormulaSnapshot.For(context.SourceEntity);
        if (memory != null) return memory.Attribute("TotalEM",
            memory.HasPlan(context.SourceEffectID, context.AttackElement) ? context.SourceEffectID : "",
            context.AttackElement, context.Target);
        return context.SourceEntity.GetFinalEM(context.SourceEffectID, context.SourceSkillID, -1,
            context.AttackElement, context.Target);
    }

    public static float TransformativeEMCoefficient(float totalEM)
    {
        float em = Mathf.Max(0f, totalEM);
        return 16f * em / (em + 2000f);
    }

    public static float QuickenEMCoefficient(float totalEM)
    {
        float em = Mathf.Max(0f, totalEM);
        return 5f * em / (em + 1200f);
    }

    public static float CalculateCharacterAmplifying(
        float totalDamage,
        float reactionMultiplier,
        float totalEM,
        float baseDamageBonus = 0f)
    {
        return totalDamage * reactionMultiplier * (1f + AmplifyingEMCoefficient(totalEM) + baseDamageBonus);
    }

    public static float CalculateEnemyAmplifying(float totalDamage, float reactionMultiplier)
    {
        return totalDamage * reactionMultiplier;
    }

    public static float CalculateCharacterTransformative(
        float levelCoefficient,
        float reactionMultiplier,
        float totalEM,
        float damageBonus,
        float baseDamageBonusFlat,
        float resistanceMultiplier)
    {
        return (levelCoefficient
                * reactionMultiplier
                * (1f + TransformativeEMCoefficient(totalEM) + damageBonus)
                + baseDamageBonusFlat)
            * resistanceMultiplier;
    }

    public static float CalculateEnemyTransformative(
        float levelCoefficient,
        float reactionMultiplier,
        float damageBonus,
        float resistanceMultiplier)
    {
        return levelCoefficient
            * reactionMultiplier
            * (1f + damageBonus)
            * resistanceMultiplier;
    }

    public static float CalculateCharacterQuicken(
        float skillBaseDamage,
        float levelCoefficient,
        ReactionType reactionType,
        float totalEM,
        float damageBonusMultiplier,
        float defenseMultiplier,
        float resistanceMultiplier,
        float baseDamageBonus = 0f)
    {
        float quickenMultiplier;
        if (reactionType == ReactionType.Aggravate)
            quickenMultiplier = 1.15f;
        else if (reactionType == ReactionType.Spread)
            quickenMultiplier = 1.25f;
        else
            return 0f;

        float quickenBaseDamage = quickenMultiplier * levelCoefficient;
        return (skillBaseDamage + quickenBaseDamage * (1f + QuickenEMCoefficient(totalEM) + baseDamageBonus))
            * damageBonusMultiplier
            * defenseMultiplier
            * resistanceMultiplier;
    }

    public static float CalculateEnemyQuicken(
        float skillBaseDamage,
        float levelCoefficient,
        ReactionType reactionType,
        float damageBonusMultiplier,
        float defenseMultiplier,
        float resistanceMultiplier)
    {
        float quickenMultiplier;
        if (reactionType == ReactionType.Aggravate)
            quickenMultiplier = 1.15f;
        else if (reactionType == ReactionType.Spread)
            quickenMultiplier = 1.25f;
        else
            return 0f;

        return (skillBaseDamage + quickenMultiplier * levelCoefficient)
            * damageBonusMultiplier
            * defenseMultiplier
            * resistanceMultiplier;
    }

    /// <summary>仅供我方角色触发结晶时调用；敌方岩攻击不产生结晶盾。</summary>
    public static float CalculateCharacterCrystalShield(float levelCoefficient, float totalEM)
    {
        return levelCoefficient * 4.5f * (1f + TransformativeEMCoefficient(totalEM));
    }

    public static float GetResistanceMultiplier(BattleEntity target, string damageElement)
    {
        if (target == null) return 1f;

        float resistance;
        switch (damageElement)
        {
            case "Pyro": resistance = target.PyroRes; break;
            case "Hydro": resistance = target.HydroRes; break;
            case "Electro": resistance = target.ElectroRes; break;
            case "Cryo": resistance = target.CryoRes; break;
            case "Anemo": resistance = target.AnemoRes; break;
            case "Dendro": resistance = target.DendroRes; break;
            case "Geo": resistance = target.GeoRes; break;
            case "Physical": resistance = target.PhysicalRes; break;
            default: return 1f;
        }

        return GetResistanceMultiplier(
            resistance + target.ResBonus + target.GetStatusResBonus(damageElement));
    }

    public static float GetResistanceMultiplier(float resistance)
    {
        if (resistance <= 0f) return 1f - resistance / 2f;
        if (resistance <= 0.75f) return 1f - resistance;
        return 1f / (1f + 4f * resistance);
    }

    public static ReactionBuffTotals CollectReactionBonuses(
        BattleEntity source,
        ReactionType reactionType,
        string damageElement)
    {
        var memory = StatusFormulaSnapshot.For(source);
        if (memory != null) return memory.ReactionBonuses(reactionType, damageElement);
        var totals = new ReactionBuffTotals
        {
            DMGBonus = source != null ? source.DMGBonus + source.GetTeamDamageBonus(damageElement) : 0f,
            BaseDMGBonusFlat = source != null ? source.BaseDMGBonusFlat : 0f,
            BaseDMGBonus = source != null && reactionType != ReactionType.None ? source.BaseDMGBonus : 0f,
            LunarDMGBonus = source != null && IsLunar(reactionType)
                ? source.LunarDMGBonus + source.GetTeamLunarDamageBonus() : 0f,
            StellarDMGBonus = source != null && ReactionFilterMatcher.IsStellar(reactionType) ? source.StellarDMGBonus : 0f
        };

        DataManager dataManager = DataManager.Instance;
        if (source == null || dataManager == null) return totals;

        return CollectReactionBonuses(
            source.GetEffectiveStatusList(),
            dataManager.StatusEffectDict.Values,
            reactionType,
            damageElement,
            totals);
    }

    public static bool IsLunar(ReactionType type) => ReactionFilterMatcher.IsLunar(type);

    /// <summary>月曜/星烁技能独立乘区，不读取普通DMGBonus，保留本hit目标、技能及效果限定。</summary>
    public static ReactionBuffTotals CollectSpecialSkillBonuses(BattleEntity source, BattleEntity target,
        DamageProperties properties, string element, string effectID, string skillID, string damageType)
    {
        var memory = StatusFormulaSnapshot.For(source, effectID);
        if (memory != null) return memory.ReactionBonuses(properties.Reaction, element, effectID, target, specialSkill: true);
        var totals = new ReactionBuffTotals
        {
            BaseDMGBonus = source.BaseDMGBonus, BaseDMGBonusFlat = source.BaseDMGBonusFlat,
            Elevation = source.Elevation,
            LunarDMGBonus = properties.IsLunar ? source.LunarDMGBonus + source.GetTeamLunarDamageBonus() : 0f,
            StellarDMGBonus = properties.IsStellar ? source.StellarDMGBonus : 0f
        };
        var dm = DataManager.Instance;
        if (dm == null) return totals;
        var query = new ReactionContext
        {
            SourceEntity = source, Target = target, SourceEffectID = effectID, SourceSkillID = skillID,
            AttackElement = element, DamageType = damageType
        };
        return CollectReactionBonuses(source.GetEffectiveStatusList(), dm.StatusEffectDict.Values,
            properties.Reaction, element, totals, null, query);
    }

    /// <summary>雷暴云按生成时反应属性快照，不继承触发技能的Normal/Skill/Burst、专属效果范围或普通DMGBonus。</summary>
    public static ReactionBuffTotals CollectLunarCloudBonuses(BattleEntity source)
        => CollectLunarReactionBonuses(source, ReactionType.LunarElectroCharged, "Electro", TeamPublicStatuses.Thundercloud);

    /// <summary>月曜公共加权伤害共用快照收集；不借用触发技能的标签或元素。</summary>
    public static ReactionBuffTotals CollectLunarReactionBonuses(BattleEntity source,
        ReactionType reaction, string element, string effectID)
    {
        var memory = StatusFormulaSnapshot.For(source);
        if (memory != null) return memory.ReactionBonuses(reaction, element, lunarCloud: true);
        var totals = new ReactionBuffTotals
        {
            BaseDMGBonus = source.BaseDMGBonus, BaseDMGBonusFlat = source.BaseDMGBonusFlat,
            LunarDMGBonus = source.LunarDMGBonus + source.GetTeamLunarDamageBonus(), Elevation = source.Elevation,
            CritRate = source.CritRate + source.WeaponCritRate,
            CritDMG = source.CritDMG + source.WeaponCritDMG
        };
        var dm = DataManager.Instance;
        if (dm == null) return totals;
        return CollectReactionBonuses(source.GetEffectiveStatusList(), dm.StatusEffectDict.Values,
            reaction, element, totals, null, new ReactionContext
            { SourceEntity = source, SourceEffectID = effectID, DamageType = "Reaction", AttackElement = element },
            lunarCloud: true);
    }

    /// <summary>只供星超导专属伤害查询，不能自动乘到普通冰/雷伤上。</summary>
    public static float GetStellarSuperconductMultiplier() => BattleManager.Instance?.TeamBonuses?.Stellar.ReactionMultiplier ?? 1f;

    public static ReactionBuffTotals CollectReactionBonuses(
        IEnumerable<StatusInstance> statuses,
        IEnumerable<StatusEffectData> statusEffects,
        ReactionType reactionType,
        string damageElement,
        ReactionBuffTotals initialTotals = default)
        => CollectReactionBonuses(statuses, statusEffects, reactionType, damageElement, initialTotals, null);

    /// <summary>只读取BaseDMGBonus，避免为了反应基础加成额外求值无关增伤状态的条件/概率。</summary>
    public static float CollectReactionBaseDamageBonus(BattleEntity source, ReactionType reactionType, string damageElement)
    {
        if (source == null || reactionType == ReactionType.None) return 0f;
        var memory = StatusFormulaSnapshot.For(source);
        if (memory != null) return memory.Raw("BaseDMGBonus") + memory.Bonus("BaseDMGBonus", "", damageElement,
            reaction: reactionType, includeLiveTarget: false);
        float basis = source.BaseDMGBonus;
        var dm = DataManager.Instance;
        if (dm == null) return basis;
        return CollectReactionBonuses(source.GetEffectiveStatusList(), dm.StatusEffectDict.Values,
            reactionType, damageElement, new ReactionBuffTotals { BaseDMGBonus = basis }, "BaseDMGBonus").BaseDMGBonus;
    }

    private static ReactionBuffTotals CollectReactionBonuses(
        IEnumerable<StatusInstance> statuses,
        IEnumerable<StatusEffectData> statusEffects,
        ReactionType reactionType,
        string damageElement,
        ReactionBuffTotals initialTotals,
        string onlyMultiplierPart,
        ReactionContext skillQuery = null, bool lunarCloud = false, bool targetOnly = false)
    {
        ReactionBuffTotals totals = initialTotals;
        if (statuses == null || statusEffects == null) return totals;

        var effects = new List<StatusEffectData>(statusEffects);
        var used = new Dictionary<string, int>();
        foreach (StatusInstance instance in statuses)
        {
            StatusMainData mainData = instance != null ? instance.MainData : null;
            if (mainData == null || !instance.IsActive) continue;
            if (!AppliesToReaction(mainData, reactionType, damageElement)) continue;

            string multiplierPart = mainData.GetMultiplierPart();
            if (skillQuery != null && !targetOnly)
            {
                if (multiplierPart == "DMGBonus") continue;
            }
            // 无技能上下文的派生反应不借用触发技能的Normal/Heavy等身份。
            if (!BattleEntity.StatusAppliesTo(mainData, skillQuery?.SourceEffectID ?? "",
                skillQuery?.SourceSkillID ?? "", skillQuery?.DamageType ?? "Reaction", damageElement)) continue;
            if (onlyMultiplierPart != null && multiplierPart != onlyMultiplierPart) continue;
            if (!MultiplierPartApplies(multiplierPart, reactionType)
                && !(skillQuery != null && multiplierPart == "Elevation")
                && !(lunarCloud && (multiplierPart == "CritRate" || multiplierPart == "CritDMG" || multiplierPart == "EMBonus"))) continue;
            if (!MeetsCondition(mainData.ScriptHook, instance, reactionType, damageElement, skillQuery)) continue;

            int count = StatusAggregation.Take(instance, used);
            if (count == 0) continue;
            bool collected = false;
            foreach (StatusEffectData effect in effects)
            {
                if (effect == null || effect.StatusEffectID == 0) continue;
                if (effect.StatusEffectID / 100 != mainData.StatusID) continue;
                if (effect.EffectType != "Buff" && effect.EffectType != "Debuff") continue;
                if (multiplierPart == "BaseDMGBonusFlat" && BattleSpecialRules.IsPreparedBaseDamageEffect(instance, effect)) continue;
                if (!MeetsCondition(effect.ScriptHook, instance, reactionType, damageElement, skillQuery)) continue;
                if (!WeaponHooks.TryResolveBuffScalar(effect, instance, out float value)
                    && !BattleSpecialRules.TryResolveBuffScalar(effect, instance, out value)
                    && !BuffExpressionEvaluator.TryResolveScalar(effect.Param1, instance, out value))
                    continue;
                value *= instance.HexereiMultiplier;
                collected = true; // 合法的0仍占同ID额度；全部效果不适用时才归还。
                switch (multiplierPart)
                {
                    case "DMGBonus": totals.DMGBonus += value * count; break;
                    case "BaseDMGBonusFlat": totals.BaseDMGBonusFlat += value * count; break;
                    case "BaseDMGBonus": totals.BaseDMGBonus += value * count; break;
                    case "LunarDMGBonus": totals.LunarDMGBonus += value * count; break;
                    case "StellarDMGBonus": totals.StellarDMGBonus += value * count; break;
                    case "Elevation": totals.Elevation += value * count; break;
                    case "CritRate": totals.CritRate += value * count; break;
                    case "CritDMG": totals.CritDMG += value * count; break;
                    case "EMBonus": totals.EMBonus += value * count; break;
                }
            }
            if (!collected) ReleaseAggregationQuota(instance, used, count);
        }

        return totals;
    }

    /// <summary>目标标记与本次消费增益在实际反应目标上读取，不写入持续反应/加权来源快照。</summary>
    public static ReactionBuffTotals CollectLiveTargetBonuses(BattleEntity source, BattleEntity target,
        ReactionType reaction, string element, bool includeCrit = false)
    {
        var dm = DataManager.Instance;
        if (source == null || target == null || dm == null) return default;
        var statuses = new List<StatusInstance>();
        foreach (var status in source.GetEffectiveStatusList())
            if (StatusSnapshotConditions.IsLiveTargetStatus(status)) statuses.Add(status);
        return CollectReactionBonuses(statuses, dm.StatusEffectDict.Values, reaction, element, default, null,
            new ReactionContext { SourceEntity = source, Target = target, DamageType = "Reaction", AttackElement = element },
            lunarCloud: includeCrit, targetOnly: true);
    }

    /// <summary>仅收集明确填写了 ApplyReactionType 的反应专属增伤，避免重复计算普通增伤区。</summary>
    public static float CollectExclusiveReactionDamageBonus(
        BattleEntity source,
        ReactionType reactionType,
        string damageElement)
    {
        var memory = StatusFormulaSnapshot.For(source);
        if (memory != null) return memory.Bonus("DMGBonus", "", damageElement, reaction: reactionType, exclusive: true);
        DataManager dataManager = DataManager.Instance;
        if (source == null || dataManager == null) return 0f;

        return CollectExclusiveReactionDamageBonus(
            source.GetEffectiveStatusList(),
            dataManager.StatusEffectDict.Values,
            reactionType,
            damageElement);
    }

    public static float CollectExclusiveReactionDamageBonus(
        IEnumerable<StatusInstance> statuses,
        IEnumerable<StatusEffectData> statusEffects,
        ReactionType reactionType,
        string damageElement)
    {
        if (statuses == null || statusEffects == null) return 0f;

        float total = 0f;
        var effects = new List<StatusEffectData>(statusEffects);
        var used = new Dictionary<string, int>();
        foreach (StatusInstance instance in statuses)
        {
            StatusMainData mainData = instance != null ? instance.MainData : null;
            if (mainData == null || !instance.IsActive) continue;
            if (string.IsNullOrWhiteSpace(mainData.ApplyReactionType)
                || !AppliesToReaction(mainData, reactionType, damageElement))
                continue;
            if (mainData.GetMultiplierPart() != "DMGBonus") continue;
            if (!MeetsStatusCondition(instance, reactionType, damageElement)) continue;

            int count = StatusAggregation.Take(instance, used);
            if (count == 0) continue;
            bool collected = false;
            foreach (StatusEffectData effect in effects)
            {
                if (effect == null || effect.StatusEffectID == 0) continue;
                if (effect.StatusEffectID / 100 != mainData.StatusID) continue;
                if (effect.EffectType != "Buff" && effect.EffectType != "Debuff") continue;
                if (!MeetsCondition(effect.ScriptHook, instance, reactionType, damageElement)) continue;
                if (BuffExpressionEvaluator.TryResolveScalar(effect.Param1, instance, out float value))
                {
                    value *= instance.HexereiMultiplier;
                    collected = true;
                    total += value * count;
                }
            }
            if (!collected) ReleaseAggregationQuota(instance, used, count);
        }
        return total;
    }

    private static bool MeetsStatusCondition(StatusInstance status, ReactionType reaction, string element)
    {
        // 反应收集也必须筛选隐藏候选，并且先筛选再消耗同ID的MaxCount额度。
        // 每次实际收集每实例只判断一次；未提供的命中目标/事件不从全局“最近一次”补凑。
        return MeetsCondition(status.MainData.ScriptHook, status, reaction, element);
    }

    private static bool MeetsCondition(string hook, StatusInstance status, ReactionType reaction, string element,
        ReactionContext skillQuery = null)
        => ScriptHookEvaluator.Evaluate(hook, new ScriptHookContext
        {
            Caster = status.Caster, Status = status, DamageReaction = reaction, DamageElement = element,
            Recipient = skillQuery?.SourceEntity, StatusHost = StatusHostAccess.FindHost(status, skillQuery?.SourceEntity),
            Target = skillQuery?.Target, HitEffectID = skillQuery?.SourceEffectID,
            DamageType = skillQuery?.DamageType, DamageSkillID = skillQuery?.SourceSkillID
        });

    private static bool MultiplierPartApplies(string part, ReactionType reaction)
    {
        switch (part)
        {
            case "DMGBonus":
            case "BaseDMGBonusFlat": return true;
            case "BaseDMGBonus": return reaction != ReactionType.None;
            case "LunarDMGBonus": return IsLunar(reaction);
            case "StellarDMGBonus": return ReactionFilterMatcher.IsStellar(reaction);
            default: return false;
        }
    }

    private static void ReleaseAggregationQuota(StatusInstance status, Dictionary<string, int> used, int count)
    {
        if (status.MainData.MaxCount <= 0) return;
        string id = status.StatusID2 ?? status.MainData.StatusID.ToString();
        used[id] -= count;
        if (used[id] == 0) used.Remove(id);
        // 只回退本次加总的局部额度，不写状态、Count或行动次数，也不重求条件/概率。
    }

    public static bool AppliesToReaction(
        StatusMainData mainData,
        ReactionType reactionType,
        string damageElement)
    {
        if (mainData == null) return false;

        if (!string.IsNullOrEmpty(mainData.ApplyReactionType)
            && !ReactionFilterMatcher.Matches(
                mainData.ApplyReactionType, reactionType, null, damageElement))
            return false;

        if (!string.IsNullOrWhiteSpace(mainData.ApplyElementType))
        {
            bool matched = false;
            foreach (string configured in mainData.ApplyElementType.Split(';'))
                if (string.Equals(configured.Trim(), "All", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(configured.Trim(), damageElement, StringComparison.OrdinalIgnoreCase))
                { matched = true; break; }
            if (!matched) return false;
        }

        return true;
    }

    public static string GetConfiguredReactionName(ReactionType reactionType)
    {
        return ReactionFilterMatcher.GetConfiguredReactionName(reactionType);
    }
}
