using System;
using System.Collections.Generic;

/// <summary>前十个敌人的资格条件和固定位置目标；伤害、状态切换仍由配表执行。</summary>
public static class Enemy2001To2010Hooks
{
    internal static void Register()
    {
        foreach (string id in new[] { "200401", "200402" })
            BattleSpecialRules.RegisterNumberedCondition(id, (_, c) => c?.Caster?.HasStatus("ST_Berserker_TorchLit") == true);
        foreach (string id in new[] { "200403", "200404" })
            BattleSpecialRules.RegisterNumberedCondition(id, (_, c) => c?.Caster?.HasStatus("ST_Berserker_TorchOut") == true);
        BattleSpecialRules.RegisterNumberedCondition("200502", (_, c) => c?.Caster?.EnemyCtrl?.IsLastGuaranteedAction == true);
        foreach (string id in new[] { "200902", "200903" })
            BattleSpecialRules.RegisterNumberedCondition(id, (_, c) => c?.Caster != null && !c.Caster.HasStatus("ST_RockGuard_Shield"));
        foreach (string id in new[] { "201002", "201003" })
            BattleSpecialRules.RegisterNumberedCondition(id, (_, c) => c?.Caster != null && !c.Caster.HasStatus("ST_WoodGuard_Shield"));
        BattleSpecialRules.RegisterNumberedCondition("20069001", (_, c) => c?.Caster != null && !c.Caster.HasStatus("ST_Grenadier_Quenched"));
        BattleSpecialRules.RegisterNumberedCondition("20069002", (_, c) => c?.Caster?.HasStatus("ST_Grenadier_Quenched") == true);
        BattleSpecialRules.RegisterNumberedCondition("420040301", (_, c) => Contact(c, false) && c.Caster.HasStatus("ST_Berserker_TorchLit"));
        BattleSpecialRules.RegisterNumberedCondition("420040302", (_, c) => Contact(c, true) && c.Caster.HasStatus("ST_Berserker_TorchOut"));
        BattleSpecialRules.RegisterNumberedCondition("420060301", (_, c) => Contact(c, false) && c.Caster.HasStatus("ST_Grenadier_Charge"));
        for (int p = 1; p <= 4; p++)
        {
            int position = p;
            BattleSpecialRules.RegisterNumberedCondition("2004010" + p, (_, __) =>
                BattleManager.Instance?.GetEntityByPosition(BattleSide.Ally, position)?.IsAlive == true);
            BattleSpecialRules.RegisterTargets("Hook(2004010" + p + ")", (battle, _) => {
                var target = battle?.GetEntityByPosition(BattleSide.Ally, position);
                return target?.IsAlive == true ? new List<int> { position } : new List<int>();
            });
        }
        BattleSpecialRules.RegisterBattleReady(battle => {
            foreach (var enemy in battle.Enemies) enemy?.ApplyInitiateStatuses();
        });
    }

    private static bool Contact(ScriptHookContext context, bool pyro)
    {
        var contact = context?.Declaration?.Data as ElementContactEventData;
        if (contact == null || context.Caster == null || contact.Subject != context.Caster
            || contact.RequestedAmount <= 0 || !contact.CanApplyAura) return false;
        return pyro ? contact.Element == "Pyro"
            : contact.Element == "Hydro" || contact.Element == "Cryo" || contact.Element == "Electro"
                || contact.Element == "Anemo" || contact.Element == "Geo" || contact.Element == "Dendro";
    }

}
