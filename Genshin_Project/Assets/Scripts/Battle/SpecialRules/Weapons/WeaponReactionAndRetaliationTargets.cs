using System;
using System.Collections.Generic;

/// <summary>3141反应固有元素队员，3154本次命中者，3155本次实际伤害来源；不重发原声明。</summary>
internal static class WeaponReactionAndRetaliationTargets
{
    internal static void Register()
    {
        string[] elements = { "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Dendro", "Geo" };
        for (int index = 0; index < elements.Length; index++)
        {
            string element = elements[index];
            string hook = "Hook(" + (431410101 + index) + ")";
            BattleSpecialRules.RegisterTargets(hook, (battle, owner) => ReactionMembers(battle, owner, element));
        }
        BattleSpecialRules.RegisterTargets("Hook(431540301)", (battle, owner) =>
        {
            var context = ScriptHookContext.Current; var damage = context?.DamageEvent;
            if (!Owns(context?.Status, owner, 3154, "ST_Weapon_4315403") || damage?.Source?.SourceEntity != owner
                || !damage.HitLanded || !damage.Source.CountsAsHit || damage.Source.SourceKind == ReactionSourceKind.DerivedReaction
                || (damage.DamageType != "Normal" && damage.DamageType != "Heavy")) return new List<int>();
            return Position(battle, owner, damage.Target);
        });
        BattleSpecialRules.RegisterTargets("Hook(431550202)", (battle, owner) =>
        {
            var context = ScriptHookContext.Current; var damage = context?.DamageEvent; var source = damage?.Source;
            var attacker = source?.SourceEntity;
            if (!Owns(context?.Status, owner, 3155, "ST_Weapon_4315502") || damage?.Target != owner
                || damage.ActualHPDamage <= 0f || source?.HasSourceEntity != true || attacker == null
                || source.SourceEntityID != attacker.EntityID || source.SourceSide != attacker.Side) return new List<int>();
            return Position(battle, owner, attacker); // 无攻击者只取消反击，前一条自疗不受影响。
        });
    }

    private static bool Owns(StatusInstance status, BattleEntity owner, int weapon, string id)
        => owner != null && status?.StatusID2 == id && status.WeaponContext?.WeaponID == weapon
            && status.Caster == owner && status.WeaponContext.Equipper == owner;

    private static List<int> Position(BattleManager battle, BattleEntity owner, BattleEntity target)
        => target != null && target.IsAlive && target.Side != owner.Side && target.SlotPosition > 0
            && ReferenceEquals(battle?.Field?.GetSlot(target.Side, target.SlotPosition)?.Occupant, target)
                ? new List<int> { target.SlotPosition } : new List<int>();

    private static List<int> ReactionMembers(BattleManager battle, BattleEntity owner, string element)
    {
        var context = ScriptHookContext.Current;
        var fact = context?.Declaration?.Get<ReactionEventData>() ?? context?.ReactionEvent?.Declaration?.Get<ReactionEventData>();
        var result = new List<int>(); var team = battle?.TeamBonuses;
        if (!Owns(context?.Status, owner, 3141, "ST_Weapon_4314101") || fact?.Source != owner
            || !fact.SourceWasPartyMember || fact.SourceSide != owner.Side || team?.Contains(owner) != true
            || !ReactionFilterMatcher.Matches("Electro", fact.Reaction, null, fact.Elements)
            || !ReactionFilterMatcher.Matches(element, fact.Reaction, null, fact.Elements)) return result;
        foreach (var member in team.Members)
            if (member != null && member.IsAlive && member.Side == owner.Side && team.ElementOf(member) == element
                && member.SlotPosition > 0 && ReferenceEquals(battle.Field.GetSlot(member.Side, member.SlotPosition)?.Occupant, member))
                result.Add(member.SlotPosition);
        return result; // 已识别但空目标不能回退全队，不按附魔或当前附着挑人。
    }
}
