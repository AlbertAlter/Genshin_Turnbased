using System;
using System.Collections.Generic;

/// <summary>3100实际减能、3105真实后台攻击命中、3108实际加能及逐次原受击目标。</summary>
internal static class WeaponEnergyAndOffFieldHooks
{
    internal static void Register()
    {
        WeaponEventResponses.Register<ResourceEventData>("ST_Weapon_4310001", 3100, BattleEventKind.ResourceChanged,
            "4310001", (status, fact) => Energy(status, fact, 3100) && fact.Removed > 0d);
        BattleSpecialRules.RegisterNumberedCondition("4310501", (_, c) => OffFieldHit(c));
        BattleSpecialRules.RegisterNumberedCondition("4310801", (_, c) =>
            Energy(c?.Status, c?.Declaration?.Get<ResourceEventData>(), 3108) && c.Declaration.Get<ResourceEventData>().Added > 0d);
        BattleSpecialRules.RegisterTargets("Hook(431080201)", (battle, owner) =>
        {
            var context = ScriptHookContext.Current; var status = context?.Status; var damage = context?.DamageEvent;
            var target = damage?.Target;
            if (owner == null || status?.StatusID2 != "ST_Weapon_4310802" || status.WeaponContext?.WeaponID != 3108
                || status.Caster != owner || status.WeaponContext.Equipper != owner || damage?.Source?.SourceEntity != owner
                || damage.Source.SourceKind == ReactionSourceKind.DerivedReaction || damage.ActualHPDamage <= 0f
                || (damage.DamageType != "Normal" && damage.DamageType != "Heavy")
                || target == null || !target.IsAlive || target.Side == owner.Side || target.SlotPosition <= 0
                || !ReferenceEquals(battle?.Field?.GetSlot(target.Side, target.SlotPosition)?.Occupant, target)) return new List<int>();
            return new List<int> { target.SlotPosition }; // 已识别但无有效目标时返回空，不随机/回退选敌。
        });
    }
    private static bool Energy(StatusInstance status, ResourceEventData fact, int weapon)
    {
        var owner = status?.WeaponContext?.Equipper;
        return owner != null && status.Caster == owner && status.WeaponContext.WeaponID == weapon
            && status.StatusID2 == "ST_Weapon_" + ((40000 + weapon) * 100 + 1)
            && fact?.Resource == "Energy" && fact.Subject == owner;
    }
    private static bool OffFieldHit(ScriptHookContext context)
    {
        var status = context?.Status; var owner = status?.WeaponContext?.Equipper; var damage = context?.DamageEvent;
        return status?.StatusID2 == "ST_Weapon_4310501" && status.WeaponContext?.WeaponID == 3105
            && owner != null && status.Caster == owner && damage?.Source?.SourceEntity == owner
            && damage.Source.CountsAsHit && damage.Source.SourceKind != ReactionSourceKind.DerivedReaction
            && damage.HitLanded && damage.SourceWasPartyMember && !damage.SourceWasOnField;
    }
}
