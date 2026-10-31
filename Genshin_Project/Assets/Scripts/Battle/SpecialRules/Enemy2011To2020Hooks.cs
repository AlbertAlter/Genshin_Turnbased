using System;
using System.Collections.Generic;

/// <summary>第二批敌人专用条件、宿主位置和治疗钩子；通用结算仍由公共入口完成。</summary>
public static class Enemy2011To2020Hooks
{
    public const string RainHealHook = "Hook(420140103)";
    public const string Thorns = "ST_DendroShaman_Thorns";
    public const string PillarShield = "ST_GeoShaman_PillarShield";

    internal static void Register()
    {
        BattleSpecialRules.RegisterNumberedCondition("201101", (_, c) => CanCreateField(c?.Caster, "ST_AnemoShaman_Tornado"));
        BattleSpecialRules.RegisterNumberedCondition("201201", (_, c) => CanCreateField(c?.Caster, Thorns));
        BattleSpecialRules.RegisterNumberedCondition("201202", (_, c) => CanCreateField(c?.Caster, "ST_DendroShaman_Field"));
        BattleSpecialRules.RegisterNumberedCondition("201302", (_, c) => CanCreatePillars(BattleManager.Instance, c?.Caster));
        BattleSpecialRules.RegisterNumberedCondition("201402", (_, c) => c?.Caster != null && !c.Caster.HasStatus("ST_HydroShaman_Rain"));
        foreach (string id in new[] { "420110101", "420120101", "420120202", "420120301" })
            BattleSpecialRules.RegisterTargets("Hook(" + id + ")", (battle, _) => FieldOccupantPositions(ScriptHookContext.Current));
        BattleSpecialRules.RegisterTargets("Hook(20130201)", PillarTargets);
        BattleSpecialRules.RegisterNumberedCondition("420120102", (_, c) =>
            c?.Declaration?.Data is ActionEventData action && action.Subject?.Side == BattleSide.Ally
            && c.StatusHost is FieldPosition);
        BattleSpecialRules.RegisterNumberedCondition("420170201", (_, c) => EnteredBurning(c));
        BattleSpecialRules.RegisterStatusLifecycle("ST_DendroSlime_Fear", (status, host) => {
            var battle = BattleManager.Instance;
            // Phase 6内刚进入燃烧的恐惧必须覆盖下一次行动，不能当场计时归零。
            if (battle != null && (int)battle.CurrentPhase == 6)
                status.EarliestTimerTurn = battle.TurnCount + 1;
        }, null);
    }

    public static bool IsGlobalPlayerActionStatus(StatusInstance status)
        => status != null && status.IsActive && status.StatusID2 == Thorns && status.Caster?.EnemyCtrl != null;

    public static bool CanCreateField(BattleEntity caster, string statusID)
    {
        var fields = BattleManager.Instance?.Field?.AllySlots;
        if (caster == null || fields == null) return false;
        foreach (var field in fields)
            foreach (var status in field.StatusList)
                if (status.IsActive && status.StatusID2 == statusID && ReferenceEquals(status.Caster, caster)) return false;
        return true;
    }

    public static List<int> FieldOccupantPositions(ScriptHookContext context)
    {
        var field = context?.StatusHost as FieldPosition;
        return field?.Side == BattleSide.Ally && field.Occupant?.IsAlive == true
            ? new List<int> { field.SlotIndex } : new List<int>();
    }

    public static bool IsPillarTarget(BattleEntity target)
    {
        if (target == null || !target.IsAlive || target.Side != BattleSide.Enemy) return false;
        if (!EnemyTraits.HasType(target, "Minion") || !EnemyTraits.HasFamily(target, "丘丘部族")) return false;
        var shield = target.GetEnemyElementalShield();
        return shield == null || shield.Value <= 0f;
    }

    public static List<int> PillarTargets(BattleManager battle, BattleEntity caster)
    {
        var positions = new List<int>();
        if (battle != null && caster != null)
            foreach (var enemy in battle.Enemies)
                if (IsPillarTarget(enemy?.Entity)) positions.Add(enemy.Entity.SlotPosition);
        return positions;
    }

    public static bool CanCreatePillars(BattleManager battle, BattleEntity caster)
    {
        if (battle == null || caster == null) return false;
        foreach (var enemy in battle.Enemies)
        {
            var target = enemy?.Entity;
            if (target?.IsAlive != true) continue;
            var shield = target.GetEnemyElementalShield();
            if (shield != null && shield.Value > 0f && shield.SourceStatusID2 == PillarShield) return false;
        }
        return PillarTargets(battle, caster).Count > 0;
    }

    public static bool EnteredBurning(ScriptHookContext context)
    {
        var fact = context?.Declaration?.Data as AuraChangedEventData;
        return fact != null && context.Caster != null && ReferenceEquals(fact.Subject, context.Caster)
            && !HasBurning(fact.Before) && HasBurning(fact.After);
    }

    private static bool HasBurning(IReadOnlyList<ElementPoolSnapshot> pools)
    {
        foreach (var pool in pools)
            if (pool.Kind == ReactionPoolKind.Burning && (pool.IsIntrinsic || pool.Amount > 0f)) return true;
        return false;
    }

    public static bool HandlesHealHook(EnemySkillEffectData effect)
        => effect?.EffectType == "Heal" && effect.SkillEffectID == 420140103 && effect.DamageHook == RainHealHook;

    public static float? HealingCeiling(EnemySkillEffectData effect, BattleEntity target)
    {
        if (string.IsNullOrWhiteSpace(effect.DamageHook)) return null;
        if (!HandlesHealHook(effect)) throw new NotSupportedException("敌人治疗钩子尚未实现：" + effect.DamageHook);
        return target.GetFinalHP() * 0.6f;
    }
}
