using System.Collections.Generic;

/// <summary>第五批敌人的技能资格；倍率、持续时间和状态行动均从配表读取。</summary>
public static class Enemy2041To2050Hooks
{
    public const string HealingBubble = "ST_Hydrogunner_HealingBubble";
    public const string EnhancedHealingBubble = "ST_Hydrogunner_EnhancedHealingBubble";

    internal static void Register()
    {
        BattleSpecialRules.RegisterCondition("HydrogunnerCanHeal", (_, context) =>
            CanCreateHealingBubble(BattleManager.Instance, context?.Caster));
        BattleSpecialRules.RegisterNumberedCondition("204303", (_, context) =>
            context?.Caster?.EnemyCtrl?.IsLastGuaranteedAction == true
            && !context.Caster.HasStatus("ST_Crusher_Charge"));
        BattleSpecialRules.RegisterTargets("PotionFieldOccupant()", (_, __) =>
            Enemy2011To2020Hooks.FieldOccupantPositions(ScriptHookContext.Current));
    }

    public static bool CanCreateHealingBubble(BattleManager battle, BattleEntity caster)
    {
        if (battle == null || caster?.IsOnField != true) return false;
        bool hasRecipient = false;
        foreach (var controller in battle.Enemies)
        {
            var target = controller?.Entity;
            if (target?.IsAlive != true || target.Side != caster.Side) continue;
            // 离场不删除状态，仍阻止同一水铳重放；只有在场且非自身的单位能接受新的水泡。
            if (target != caster && target.IsOnField) hasRecipient = true;
            if (HasOwnHealingBubble(target.StatusDict.Values, caster)) return false;
        }
        return hasRecipient;
    }

    internal static bool HasOwnHealingBubble(IEnumerable<StatusInstance> statuses, BattleEntity caster)
    {
        if (statuses == null || caster == null) return false;
        foreach (var status in statuses)
            if (status != null && status.IsActive && ReferenceEquals(status.Caster, caster)
                && (status.StatusID2 == HealingBubble || status.StatusID2 == EnhancedHealingBubble)) return true;
        return false;
    }
}
