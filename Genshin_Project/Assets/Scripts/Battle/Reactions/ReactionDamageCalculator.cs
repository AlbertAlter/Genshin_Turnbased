using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

[Serializable]
public struct ReactionBuffTotals
{
    public float DMGBonus;
    public float BaseDMGBonusFlat;
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
        float totalEM)
    {
        return totalDamage * reactionMultiplier * (1f + AmplifyingEMCoefficient(totalEM));
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
        float criticalMultiplier,
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

        float quickenBaseDamage = quickenMultiplier * levelCoefficient;
        return (skillBaseDamage + quickenBaseDamage * (1f + QuickenEMCoefficient(totalEM)))
            * criticalMultiplier
            * damageBonusMultiplier
            * defenseMultiplier
            * resistanceMultiplier;
    }

    public static float CalculateEnemyQuicken(
        float skillBaseDamage,
        float levelCoefficient,
        ReactionType reactionType,
        float criticalMultiplier,
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
            * criticalMultiplier
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
            default: resistance = target.PhysicalRes; break;
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
        var totals = new ReactionBuffTotals
        {
            DMGBonus = source != null ? source.DMGBonus : 0f,
            BaseDMGBonusFlat = source != null ? source.BaseDMGBonusFlat : 0f
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

    public static ReactionBuffTotals CollectReactionBonuses(
        IEnumerable<StatusInstance> statuses,
        IEnumerable<StatusEffectData> statusEffects,
        ReactionType reactionType,
        string damageElement,
        ReactionBuffTotals initialTotals = default)
    {
        ReactionBuffTotals totals = initialTotals;
        if (statuses == null || statusEffects == null) return totals;

        var effects = new List<StatusEffectData>(statusEffects);
        foreach (StatusInstance instance in statuses)
        {
            StatusMainData mainData = instance != null ? instance.MainData : null;
            if (mainData == null || !instance.IsActive) continue;
            if (!AppliesToReaction(mainData, reactionType, damageElement)) continue;

            string multiplierPart = mainData.GetMultiplierPart();
            if (multiplierPart != "DMGBonus" && multiplierPart != "BaseDMGBonusFlat") continue;

            int count = Mathf.Max(1, instance.StackCount);
            if (mainData.MaxCount > 0) count = Mathf.Min(count, mainData.MaxCount);

            foreach (StatusEffectData effect in effects)
            {
                if (effect == null || effect.StatusEffectID == 0) continue;
                if (effect.StatusEffectID / 100 != mainData.StatusID) continue;
                if (effect.EffectType != "Buff" && effect.EffectType != "Debuff") continue;
                if (!float.TryParse(effect.Param1, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                    continue;

                if (multiplierPart == "DMGBonus") totals.DMGBonus += value * count;
                else totals.BaseDMGBonusFlat += value * count;
            }
        }

        return totals;
    }

    /// <summary>仅收集明确填写了 ApplyReactionType 的反应专属增伤，避免重复计算普通增伤区。</summary>
    public static float CollectExclusiveReactionDamageBonus(
        BattleEntity source,
        ReactionType reactionType,
        string damageElement)
    {
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

        string configuredReaction = GetConfiguredReactionName(reactionType);
        float total = 0f;
        var effects = new List<StatusEffectData>(statusEffects);
        foreach (StatusInstance instance in statuses)
        {
            StatusMainData mainData = instance != null ? instance.MainData : null;
            if (mainData == null || !instance.IsActive) continue;
            if (string.IsNullOrEmpty(mainData.ApplyReactionType)
                || mainData.ApplyReactionType != configuredReaction)
                continue;
            if (!string.IsNullOrEmpty(mainData.ApplyElementType)
                && mainData.ApplyElementType != damageElement)
                continue;
            if (mainData.GetMultiplierPart() != "DMGBonus") continue;

            int count = Mathf.Max(1, instance.StackCount);
            if (mainData.MaxCount > 0) count = Mathf.Min(count, mainData.MaxCount);
            foreach (StatusEffectData effect in effects)
            {
                if (effect == null || effect.StatusEffectID == 0) continue;
                if (effect.StatusEffectID / 100 != mainData.StatusID) continue;
                if (effect.EffectType != "Buff" && effect.EffectType != "Debuff") continue;
                if (float.TryParse(
                        effect.Param1,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out float value))
                {
                    total += value * count;
                }
            }
        }
        return total;
    }

    public static bool AppliesToReaction(
        StatusMainData mainData,
        ReactionType reactionType,
        string damageElement)
    {
        if (mainData == null) return false;

        if (!string.IsNullOrEmpty(mainData.ApplyReactionType)
            && mainData.ApplyReactionType != GetConfiguredReactionName(reactionType))
            return false;

        if (!string.IsNullOrEmpty(mainData.ApplyElementType)
            && mainData.ApplyElementType != damageElement)
            return false;

        return true;
    }

    public static string GetConfiguredReactionName(ReactionType reactionType)
    {
        switch (reactionType)
        {
            case ReactionType.Vaporize: return "蒸发";
            case ReactionType.Melt: return "融化";
            case ReactionType.Overloaded: return "超载";
            case ReactionType.Superconduct: return "超导";
            case ReactionType.ElectroCharged: return "感电";
            case ReactionType.Frozen: return "冻结";
            case ReactionType.Shatter: return "碎冰";
            case ReactionType.Swirl: return "扩散";
            case ReactionType.Crystallize: return "结晶";
            case ReactionType.Burning: return "燃烧";
            case ReactionType.Bloom: return "绽放";
            case ReactionType.Quicken: return "原激化";
            case ReactionType.Hyperbloom: return "超绽放";
            case ReactionType.Burgeon: return "烈绽放";
            case ReactionType.Spread: return "蔓激化";
            case ReactionType.Aggravate: return "超激化";
            default: return string.Empty;
        }
    }
}
