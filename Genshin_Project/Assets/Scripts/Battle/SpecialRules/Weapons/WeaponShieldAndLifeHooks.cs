using System;
using System.Collections.Generic;

/// <summary>护盾武器的独立Count参数、3157/3161生命换算、3163真实HP变化、3164已提交前台岩伤。查询不改状态。</summary>
internal static class WeaponShieldAndLifeHooks
{
    internal static void Register()
    {
        BattleSpecialRules.RegisterNumberedCondition("431610202", (_, context) =>
        {
            var status = context?.Status;
            return Owns(status, 3161, 4316102)
                && WeaponHooks.OwnStatus(status, "ST_Weapon_4316103")?.RuntimeValue >= 3f;
        });
        BattleSpecialRules.RegisterTargets("Hook(431610204)", (battle, owner) =>
        {
            var result = new List<int>(); var status = ScriptHookContext.Current?.Status;
            var count = WeaponHooks.OwnStatus(status, "ST_Weapon_4316103");
            if (battle == null || !Owns(status, 3161, 4316102) || owner != status.WeaponContext.Equipper
                || !PartyMember(owner, owner) || count == null || count.RuntimeValue < 3f) return result;
            foreach (var ally in battle.Allies)
            {
                var member = ally?.Entity;
                if (member == null || member == owner || !member.IsAlive || member.Side != owner.Side
                    || member.SlotPosition <= 0
                    || !ReferenceEquals(battle.Field.GetSlot(member.Side, member.SlotPosition)?.Occupant, member)) continue;
                result.Add(member.SlotPosition);
            }
            return result; // 无有效受益者返回已识别空目标，不能回退到包含装备者的全队。
        });
        WeaponEventResponses.Register<HPChangedEventData>("ST_Weapon_4316301", 3163, BattleEventKind.HPChanged,
            "4316301", (status, fact) => Owns(status, 3163, 4316301)
                && fact.Subject == status.WeaponContext.Equipper && fact.Before != fact.After);
        WeaponEventResponses.Register<HPChangedEventData>("ST_Weapon_4316302", 3163, BattleEventKind.HPChanged,
            "4316302", (status, fact) => Owns(status, 3163, 4316302)
                && fact.Before != fact.After && OtherPartyMember(status.WeaponContext.Equipper, fact.Subject));
        // 只有实际HP超过新的上限时才会产生MaxHPAdjustment，不把上限提高伪装成治疗。
        BattleSpecialRules.RegisterStatusLifecycle("ST_Weapon_4316304", null, (status, host) =>
        {
            if (host is BattleEntity owner) owner.ClampCurrentHPToMaximum();
        });
        WeaponEventResponses.Register<DamageEventData>("ST_Weapon_4316404", 3164, BattleEventKind.DamageResolved,
            "4316404", (status, fact) =>
            {
                if (!Owns(status, 3164, 4316404) || fact.Element != "Geo" || fact.ActualHPDamage <= 0f
                    || !fact.SourceWasPartyMember || !fact.SourceWasOnField) return false;
                var source = fact.Source;
                return source.HasSourceEntity && source.SourceEntity != null
                    && source.SourceEntityID == source.SourceEntity.EntityID
                    && source.SourceSide == status.WeaponContext.Equipper.Side
                    && PartyMember(status.WeaponContext.Equipper, source.SourceEntity);
            });
    }

    internal static bool TryResolve(StatusEffectData effect, StatusInstance status, out float value)
    {
        value = 0f;
        if (effect?.EffectType != "Buff" || effect.DamageHook != "Hook(" + effect.StatusEffectID + ")") return false;
        switch (effect.StatusEffectID)
        {
            case 431560301: case 431720301: case 431950301: case 432080301:
            {
                // 仅共享只读算法。各武器仍使用自己的Count/参数/来源，尘世Hit/1不改成其他三把的Effect/1。
                var owner = RequireOwner(status, effect.StatusEffectID / 10000 - 40000, effect.StatusEffectID / 100);
                value = Count(status, 5) * WeaponHooks.Param(status, 2) * (owner.GetTotalShieldHP() > 0f ? 2f : 1f);
                return true;
            }
            case 431570201:
                value = RequireOwner(status, 3157, 4315702).GetFinalHP() * WeaponHooks.Param(status, 2);
                return true;
            case 431610301:
                value = RequireOwner(status, 3161, 4316103).GetFinalHP() * WeaponHooks.Param(status, 2) * Count(status, 3);
                return true;
            case 431610401:
                value = RequireOwner(status, 3161, 4316104).GetFinalHP() * WeaponHooks.Param(status, 3);
                return true;
            default: return false;
        }
    }

    private static bool Owns(StatusInstance status, int weapon, int id)
        => status?.MainData?.StatusID == id && status.StatusID2 == "ST_Weapon_" + id
            && status.WeaponContext?.WeaponID == weapon && status.WeaponContext.Equipper != null
            && status.Caster == status.WeaponContext.Equipper;

    private static BattleEntity RequireOwner(StatusInstance status, int weapon, int id)
    {
        if (!Owns(status, weapon, id))
            throw new InvalidOperationException("武器生命/护盾参数必须保留对应父状态及原装备者上下文：" + id);
        return status.WeaponContext.Equipper;
    }

    private static float Count(StatusInstance status, int maximum)
    {
        float count = status.RuntimeValue;
        if (float.IsNaN(count) || float.IsInfinity(count) || count < 0f || count > maximum || count != (int)count)
            throw new InvalidOperationException("武器增益份数必须为0至上限的整数：" + status.StatusID2);
        return count;
    }

    private static bool OtherPartyMember(BattleEntity owner, BattleEntity member)
        => member != owner && PartyMember(owner, member);

    private static bool PartyMember(BattleEntity owner, BattleEntity member)
    {
        var battle = BattleManager.Instance;
        if (battle == null || owner?.Side != BattleSide.Ally || member == null || member.Side != owner.Side) return false;
        bool hasOwner = false, hasMember = false;
        foreach (var ally in battle.Allies)
        {
            if (ally?.Entity == owner) hasOwner = true;
            if (ally?.Entity == member) hasMember = true;
        }
        return hasOwner && hasMember;
    }
}
