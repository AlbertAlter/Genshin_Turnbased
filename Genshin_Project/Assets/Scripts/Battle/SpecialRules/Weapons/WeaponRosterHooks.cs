using System;
using System.Collections.Generic;

/// <summary>完整编队条件和动态属性换算；只读正式角色身份/当前上限，不建立Count或缓存最后目标。</summary>
internal static class WeaponRosterHooks
{
    internal static void Register()
    {
        BattleSpecialRules.RegisterBattleReady(battle =>
        {
            foreach (var ally in battle.Allies)
            {
                var parent = ally?.Entity?.GetStatus("ST_Weapon_4321101");
                if (parent?.IsActive == true && parent.WeaponContext?.WeaponID == 3211 && EquipmentStatusLifetime.CanExecute(parent))
                    StatusBindingSystem.ApplyBindings(parent, ally.Entity);
            }
        }); // 装备初始化时编队可能尚未齐全，开场只补缺失的具体绑定。
        BattleSpecialRules.RegisterNumberedCondition("4321103", (_, c) => c?.Status?.StatusID2 == "ST_Weapon_4321103"
            && ElementCount(c.Status, 3211, true, false, true) > 0);
        BattleSpecialRules.RegisterNumberedCondition("4321104", (_, c) => c?.Status?.StatusID2 == "ST_Weapon_4321104"
            && WeaponHooks.IsElemental(c.DamageElement) && ElementCount(c.Status, 3211, false, false, true) > 0);
        BattleSpecialRules.RegisterNumberedCondition("4319003", (_, c) =>
            (c?.Status?.StatusID2 == "ST_Weapon_4319003" || c?.Status?.StatusID2 == "ST_Weapon_4319004")
            && ElementCount(c.Status, 3190, false) >= 1);
        BattleSpecialRules.RegisterStatusLifecycle("ST_Weapon_4315002", null, (status, host) =>
        {
            if (host is BattleEntity owner && status.WeaponContext?.Equipper == owner)
                owner.ClampCurrentHPToMaximum(); // 真实移除后才裁切；查询不改HP、不伪造治疗。
        });
        BattleSpecialRules.RegisterNumberedCondition("4310102", (_, c) => ChainCount(c?.Status) >= 3);
        BattleSpecialRules.RegisterNumberedCondition("4312001", (_, c) =>
        {
            var elements = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in Roster(c?.Status, 3120))
            {
                string element = Metadata(member).Element;
                if (WeaponHooks.IsElemental(element)) elements.Add(element);
            }
            return elements.Count >= 3;
        });
    }

    internal static bool HasExistingBinding(StatusInstance parent, StatusEffectData effect, BattleEntity target)
    {
        if (parent?.StatusID2 != "ST_Weapon_4321101" || effect.StatusEffectID != 432110101) return false;
        foreach (var child in parent.BoundStatuses)
            if (child.IsActive && child.Host == target && child.StatusID2 == effect.Param1 && target.HasStatusInstance(child)) return true;
        return false;
    }

    private static BattleEntity Owner(StatusInstance status, int weapon)
    {
        var owner = status?.WeaponContext?.Equipper;
        if (owner == null || status.Caster != owner || status.WeaponContext.WeaponID != weapon)
            throw new InvalidOperationException("武器属性换算缺少本装备者来源：" + status?.StatusID2);
        return owner;
    }

    private static List<BattleEntity> Roster(StatusInstance status, int weapon)
    {
        var owner = Owner(status, weapon);
        var battle = BattleManager.Instance;
        if (owner.Side != BattleSide.Ally || battle == null)
            throw new InvalidOperationException("武器编队查询需要完整我方编队。");
        bool includesOwner = false;
        var ids = new HashSet<int>();
        var members = new List<BattleEntity>();
        foreach (var controller in battle.Allies)
        {
            var member = controller?.Entity;
            if (member == null) continue;
            if (ReferenceEquals(member, owner)) includesOwner = true;
            if (ids.Add(member.EntityID)) members.Add(member);
        }
        if (!includesOwner) throw new InvalidOperationException("武器装备者尚未加入本场完整编队。");
        return members; // 阵亡、后台不代表离开编队；不读取Field或当前攻击元素。
    }

    private static CharacterOverviewData Metadata(BattleEntity member)
        => DataManager.Instance?.GetCharacterOverview(member.EntityID)
            ?? throw new InvalidOperationException("角色缺少正式身份配置：" + member.EntityID);

    private static int ChainCount(StatusInstance status)
    {
        var members = Roster(status, 3101);
        string ownElement = Metadata(Owner(status, 3101)).Element;
        if (!WeaponHooks.IsElemental(ownElement))
            throw new InvalidOperationException("碎链装备者缺少有效固有元素。");
        int count = 0;
        foreach (var member in members)
        {
            var identity = Metadata(member);
            // 已知异元素即属于并集；无须猜测该人的缺失地区。
            bool different = WeaponHooks.IsElemental(identity.Element) && identity.Element != ownElement;
            if (different || identity.Nation == "纳塔") count++;
            else if (string.IsNullOrEmpty(identity.Nation) || string.IsNullOrEmpty(identity.Element))
                throw new InvalidOperationException("碎链编队成员缺少国别/固有元素：" + member.EntityID);
        }
        return count;
    }

    private static int ElementCount(StatusInstance status, int weapon, bool same, bool includeOwner = true, bool skipInvalid = false)
    {
        var owner = Owner(status, weapon); string ownElement = DataManager.Instance?.GetCharacterOverview(owner.EntityID)?.Element;
        if (skipInvalid && !WeaponHooks.IsElemental(ownElement)) return 0;
        if (!WeaponHooks.IsElemental(ownElement)) throw new InvalidOperationException("武器编队统计缺少装备者固有元素：" + owner.EntityID);
        int count = 0;
        foreach (var member in Roster(status, weapon))
        {
            if (!includeOwner && member == owner) continue;
            string element = DataManager.Instance?.GetCharacterOverview(member.EntityID)?.Element;
            if (skipInvalid && !WeaponHooks.IsElemental(element)) continue;
            if (!WeaponHooks.IsElemental(element)) throw new InvalidOperationException("武器编队统计缺少成员固有元素：" + member.EntityID);
            if ((element == ownElement) == same) count++;
        }
        return count; // 每名角色计一次，阵亡/后台仍在编队；不数元素种类或当前附魔。
    }

    internal static bool TryResolve(StatusEffectData effect, StatusInstance status, out float value)
    {
        value = 0f;
        if (effect?.EffectType != "Buff" || effect.DamageHook != "Hook(" + effect.StatusEffectID + ")") return false;
        if (effect.StatusEffectID != 431010101 && effect.StatusEffectID != 430660101 && effect.StatusEffectID != 430920101
            && effect.StatusEffectID != 431170101 && effect.StatusEffectID != 431480201 && effect.StatusEffectID != 431500201
            && effect.StatusEffectID != 431880201 && effect.StatusEffectID != 431880301
            && effect.StatusEffectID != 431900301 && effect.StatusEffectID != 431900401
            && effect.StatusEffectID != 432110301 && effect.StatusEffectID != 432110401) return false;
        if (status?.MainData?.StatusID != effect.StatusEffectID / 100)
            throw new InvalidOperationException("武器换算参数与父状态不匹配：" + effect.StatusEffectID);
        switch (effect.StatusEffectID)
        {
            case 431010101:
                Owner(status, 3101);
                value = ChainCount(status) * WeaponHooks.Param(status, 1);
                return true;
            case 430660101: case 430920101: case 431170101:
            {
                int weapon = effect.StatusEffectID / 10000 - 40000;
                double sum = 0d;
                foreach (var member in Roster(status, weapon)) sum += member.MaxEnergy;
                value = (float)Math.Min(sum * WeaponHooks.Param(status, 1), WeaponHooks.Param(status, 2));
                return true;
            }
            case 431480201:
                value = Math.Min(Owner(status, 3148).GetFinalHP() / 1000f * WeaponHooks.Param(status, 1),
                    WeaponHooks.Param(status, 2));
                return true;
            case 431500201:
            {
                int count = 0;
                foreach (var member in Roster(status, 3150))
                    if (Metadata(member).Element == "Hydro") count++;
                value = WeaponHooks.Param(status, 1) + Math.Min(count * WeaponHooks.Param(status, 2), WeaponHooks.Param(status, 3));
                return true;
            }
            case 431880201: case 431880301:
            {
                int count = Math.Min(3, ElementCount(status, 3188, effect.StatusEffectID == 431880201));
                value = count == 0 ? 0f : WeaponHooks.Param(status, (effect.StatusEffectID == 431880201 ? 1 : 4) + count);
                return true;
            }
            case 431900301: case 431900401:
            {
                int count = ElementCount(status, 3190, false);
                value = count == 0 ? 0f : WeaponHooks.Param(status, (effect.StatusEffectID == 431900301 ? 2 : 4) + (count >= 2 ? 1 : 0));
                return true;
            }
            case 432110301: case 432110401:
                value = Math.Min(3, ElementCount(status, 3211, effect.StatusEffectID == 432110301, false, true))
                    * WeaponHooks.Param(status, effect.StatusEffectID == 432110301 ? 2 : 3); return true;
            default: return false;
        }
    }
}
