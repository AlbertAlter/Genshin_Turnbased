using System;

/// <summary>
/// 敌方Shield状态：None使用数值盾公式，其他受支持元素使用固定GU。
/// 本入口只关心 Shield 效果本身，与调用它的 Status_Action.ActionType 无关。
/// </summary>
public static class EnemyShieldEffectSystem
{
    public static void ValidateStatusDefinition(StatusMainData main)
    {
        if (!string.Equals(main?.StatusType, "Shield", StringComparison.OrdinalIgnoreCase) || DataManager.Instance == null) return;
        foreach (var effect in DataManager.Instance.StatusEffectDict.Values)
            if (effect != null && effect.StatusEffectID / 100 == main.StatusID
                && string.Equals(effect.EffectType, "Shield", StringComparison.OrdinalIgnoreCase))
                ValidateDefinition(effect);
    }

    private static void ValidateDefinition(StatusEffectData effect)
    {
        string element = (effect.Element ?? string.Empty).Trim();
        if (string.Equals(element, "None", StringComparison.OrdinalIgnoreCase))
            EnemyHealthShieldFormula.Evaluate(effect.Param1, _ => 1f);
        else
        {
            if (!IsSupportedElement(element)) throw new FormatException("敌人护盾Element须为None（数值盾）或受支持的GU盾元素：" + effect.StatusEffectID2);
            EnemyShieldRuleParser.ParseInitialGauge(effect.Param1);
        }
    }

    /// <summary>敌方Shield状态自身持有盾；无需敌人借角色控制器执行OnApply行动。</summary>
    public static void ApplyStatus(BattleEntity target, StatusInstance status)
    {
        if (target == null || target.Type != BattleEntity.EntityType.Enemy || status?.MainData == null
            || !string.Equals(status.MainData.StatusType, "Shield", StringComparison.OrdinalIgnoreCase)
            || DataManager.Instance == null) return;
        var definitions = new System.Collections.Generic.List<StatusEffectData>();
        foreach (StatusEffectData effect in DataManager.Instance.StatusEffectDict.Values)
            if (effect != null && effect.StatusEffectID / 100 == status.MainData.StatusID
                && string.Equals(effect.EffectType, "Shield", StringComparison.OrdinalIgnoreCase)) definitions.Add(effect);
        definitions.Sort((left, right) => left.StatusEffectID.CompareTo(right.StatusEffectID));
        foreach (StatusEffectData effect in definitions) Apply(target, effect, status);
    }

    public static Shield Apply(
        BattleEntity target,
        StatusEffectData effect,
        StatusInstance sourceStatus)
    {
        if (target == null || target.Type != BattleEntity.EntityType.Enemy
            || effect == null || sourceStatus == null)
            return null;
        ValidateDefinition(effect);

        // 数值盾必须显式填写None；空元素和带元素的数值盾均不支持。
        if (string.Equals((effect.Element ?? string.Empty).Trim(), "None", StringComparison.OrdinalIgnoreCase))
            return ApplyHealthShield(target, effect, sourceStatus);

        float gauge;
        try
        {
            gauge = EnemyShieldRuleParser.ParseInitialGauge(effect.Param1);
        }
        catch (FormatException exception)
        {
            LogManager.LogError(
                LogCategory.Effect,
                $"敌方元素盾 {effect.StatusEffectID2} 的 Param1 无效：{exception.Message}");
            return null;
        }

        string element = (effect.Element ?? string.Empty).Trim();
        if (!IsSupportedElement(element))
        {
            LogManager.LogError(
                LogCategory.Effect,
                $"敌方元素盾 {effect.StatusEffectID2} 的 Element 无对应 EnemyShield.Type1 行：{element}");
            return null;
        }

        foreach (string canonical in ElementQuery.Elements)
            if (string.Equals(element, canonical, StringComparison.OrdinalIgnoreCase)) { element = canonical; break; }
        Shield existing = target.GetEnemyElementalShield();
        if (existing != null && ReferenceEquals(existing.SourceStatus, sourceStatus)
            && existing.Value == gauge && existing.Element == element) return existing;

        Shield shield = target.AddOrReplaceEnemyElementalShield(gauge, element, sourceStatus);
        if (shield != null)
        {
            LogManager.Log(
                LogCategory.Effect,
                $"敌方元素盾 {target.EntityID} = {gauge:F3}U (元素 {element}, 状态 {sourceStatus.StatusID2})");
        }
        return shield;
    }

    private static Shield ApplyHealthShield(BattleEntity target, StatusEffectData effect, StatusInstance status)
    {
        if (!target.HasStatusInstance(status) || !status.IsActive)
            throw new InvalidOperationException("敌人数值盾必须属于受盾敌人身上的有效状态。");
        var existing = target.Shields.Find(s => s != null && s.Kind == ShieldKind.Skill
            && ReferenceEquals(s.SourceStatus, status));
        if (existing != null) return existing; // 同一实例不因重复调用而补满盾量。
        BattleEntity creator = status.Caster ?? target;
        float basis = EnemyHealthShieldFormula.Evaluate(effect.Param1, name => {
            switch (name)
            {
                case "ATK": case "TotalATK": return creator.GetFinalATK(effect.StatusEffectID2, null, -1, "None");
                case "DEF": case "TotalDEF": return creator.GetFinalDEF(effect.StatusEffectID2, null, -1, "None");
                case "MaxHP": case "TotalHP": return creator.GetFinalHP(effect.StatusEffectID2, null, -1, "None");
                case "Target.TotalHP": return target.GetFinalHP(effect.StatusEffectID2, null, -1, "None");
                default: throw new FormatException("未知护盾属性：" + name);
            }
        });
        float amount = BattleEntity.CalculateShieldAmount(basis,
            creator.GetStatusShieldBonus("ShieldBonus", effect.StatusEffectID2, null, "None", target),
            creator.GetStatusShieldBonus("ShieldBonusFlat", effect.StatusEffectID2, null, "None", target));
        target.AddShield(amount, "None", status.RemainingPhaseCount,
            originPhase: status.AddInPhase, sourceStatus: status, creator: creator);
        return target.Shields.Find(s => s != null && s.Kind == ShieldKind.Skill && ReferenceEquals(s.SourceStatus, status));
    }

    private static bool IsSupportedElement(string element)
    {
        return string.Equals(element, "Pyro", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element, "Hydro", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element, "Electro", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element, "Cryo", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element, "Dendro", StringComparison.OrdinalIgnoreCase)
            || string.Equals(element, "Geo", StringComparison.OrdinalIgnoreCase);
    }
}
