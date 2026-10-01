using System;

/// <summary>
/// 执行 EffectType=Shield 对敌方目标的元素盾创建逻辑。
/// 本入口只关心 Shield 效果本身，与调用它的 Status_Action.ActionType 无关。
/// </summary>
public static class EnemyShieldEffectSystem
{
    public static Shield Apply(
        BattleEntity target,
        StatusEffectData effect,
        StatusInstance sourceStatus)
    {
        if (target == null || target.Type != BattleEntity.EntityType.Enemy
            || effect == null || sourceStatus == null)
            return null;

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

        Shield shield = target.AddOrReplaceEnemyElementalShield(gauge, element, sourceStatus);
        if (shield != null)
        {
            LogManager.Log(
                LogCategory.Effect,
                $"敌方元素盾 {target.EntityID} = {gauge:F3}U (元素 {element}, 状态 {sourceStatus.StatusID2})");
        }
        return shield;
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
