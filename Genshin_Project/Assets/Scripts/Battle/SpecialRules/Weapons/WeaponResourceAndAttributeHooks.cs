using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>资源条件、实时属性换算及按本次声明保存的命中前生命；不在查询中改资源。</summary>
internal static class WeaponResourceAndAttributeHooks
{
    internal static void Register()
    {
        BattleSpecialRules.RegisterNumberedCondition("4316502", (_, c) =>
        {
            var fact = c?.Declaration?.Get<ResourceEventData>();
            return Owns(c?.Status, 3165, 4316502) && fact?.Resource == "BondOfLife"
                && fact.Subject == c.Status.WeaponContext.Equipper && fact.Added > 0d;
        }); // 原ResourceChanged(Equipper)负责派发，只判实际增加，不再另订阅一次。
        BattleSpecialRules.RegisterNumberedCondition("431660102", (_, c) => Owns(c?.Status, 3166, 4316601)
            && WeaponHooks.OwnStatus(c.Status, "ST_Weapon_4316602")?.RuntimeValue >= 2f);
        BattleSpecialRules.RegisterNumberedCondition("4316603", (_, c) =>
            (Owns(c?.Status, 3166, 4316603) || Owns(c?.Status, 3166, 4316604)) && WeaponHooks.IsElemental(c.DamageElement));
        BattleSpecialRules.RegisterNumberedCondition("4316703", (_, c) =>
            (Owns(c?.Status, 3167, 4316703) || Owns(c?.Status, 3167, 4316704))
            && c.Status.WeaponContext.Equipper.CurrentEnergy == 0f);
        BattleSpecialRules.RegisterTargets("Hook(431680302)", (battle, owner) => OtherMembers(battle, owner, ScriptHookContext.Current?.Status));
        BattleSpecialRules.RegisterHitScope(BeginHit);
        BattleSpecialRules.RegisterDeclarationObserver(ObserveHit);
        BattleSpecialRules.RegisterNumberedCondition("4317002", (_, c) =>
        {
            var declaration = c?.DamageEvent?.Declaration ?? c?.Declaration;
            return Owns(c?.Status, 3170, 4317002) && declaration != null && Completed.TryGetValue(declaration, out var fact)
                && fact.Owner == c.Status.WeaponContext.Equipper && fact.Target == c.Target
                && fact.Maximum > 0f && fact.Before < fact.Maximum * .30f;
        });
        BattleSpecialRules.RegisterNumberedCondition("4317505", (_, c) =>
            Owns(c?.Status, 3175, 4317505) && c.Status.WeaponContext.Equipper.GetTotalShieldHP() <= 0f);
        BattleSpecialRules.RegisterStatusLifecycle("ST_Weapon_4317505", (status, host) =>
        {
            var owner = RequireOwner(status, 3175, 4317505);
            if (!ReferenceEquals(owner, host)) throw new InvalidOperationException("苇海无盾生命状态必须挂在装备者身上。");
            ShieldSubscriptions.Add(status, BattleEventHub.Subscribe<ShieldChangedEventData>(BattleEventKind.ShieldChanged,
                (_, fact) => { if (fact.Subject == owner) owner.ClampCurrentHPToMaximum(); }, status.ApplyOrder,
                () => status.IsActive && owner.HasStatusInstance(status) && EquipmentStatusLifetime.CanExecute(status)));
        }, (status, host) =>
        {
            if (ShieldSubscriptions.TryGetValue(status, out var subscription)) { subscription.Dispose(); ShieldSubscriptions.Remove(status); }
            if (host is BattleEntity owner) owner.ClampCurrentHPToMaximum();
        });
        BattleSpecialRules.RegisterActionCounterGroup("ST_Weapon_4317701", action =>
            action?.ActionType == "OnTrigger" && (action.Param1 == "STE_Weapon_431770101" || action.Param1 == "STE_Weapon_431770102")
                ? "FangSharedTwoPerTurn" : null); // 只共用Turn/Pending账本，根Effect额度仍是技能行自己的。
        WeaponEventResponses.Register<ReactionEventData>("ST_Weapon_4317701", 3177, BattleEventKind.ReactionOccurred,
            "431770102", (status, fact) =>
            {
                if (!Owns(status, 3177, 4317701) || !fact.SourceWasPartyMember
                    || fact.SourceSide != status.WeaponContext.Equipper.Side
                    || (fact.Reaction != ReactionType.Burning && fact.Reaction != ReactionType.Burgeon)) return false;
                var team = BattleManager.Instance?.TeamBonuses;
                return team?.Contains(status.WeaponContext.Equipper) == true && team.Contains(fact.Source);
            });
        WeaponEventResponses.Register<DamageEventData>("ST_Weapon_4319202", 3192, BattleEventKind.DamageResolved,
            "4319202", (status, fact) => Owns(status, 3192, 4319202)
                && fact.Source.SourceEntity == status.WeaponContext.Equipper && fact.HitLanded
                && (fact.DamageType == "Skill" || fact.Source.ReactionType == ReactionType.LunarCrystallize));
        // 技能式月结晶是Damage本身的标签；不把公共月笼协奏改为攻击命中。
        BattleSpecialRules.RegisterTargets("Hook(431920305)", (battle, owner) => OtherMembers(battle, owner, ScriptHookContext.Current?.Status, 3192, 4319203));
        BattleSpecialRules.RegisterNumberedCondition("4319205", (_, c) =>
        {
            var status = c?.Status; var owner = status?.WeaponContext?.Equipper;
            if (!Owns(status, 3192, 4319205) && !Owns(status, 3192, 4319206)) return false;
            var parent = WeaponHooks.OwnStatus(status, "ST_Weapon_4319203");
            var team = BattleManager.Instance?.TeamBonuses;
            return status.IsActive && parent != null && parent.BoundStatuses.Contains(status)
                && team?.Contains(owner) == true && team.Resonance.HasEnemyFieldMoonCage;
        });
        BattleSpecialRules.RegisterNumberedCondition("4319603", (_, c) =>
        {
            if (!Owns(c?.Status, 3196, 4319603)) return false;
            var owner = c.Status.WeaponContext.Equipper; float maximum = owner.GetFinalHP();
            return maximum > 0f && owner.CurrentHP < maximum * .5f;
        });
        BattleSpecialRules.RegisterNumberedCondition("4320002", (_, c) => Owns(c?.Status, 3200, 4320002)
            && BondOfLifeStatusHandler.GetValue(c.Status.WeaponContext.Equipper) > 0f);
        BattleSpecialRules.RegisterNumberedCondition("4320003", (_, c) =>
        {
            if (!Owns(c?.Status, 3200, 4320003)) return false;
            var owner = c.Status.WeaponContext.Equipper; float maximum = owner.GetFinalHP();
            return maximum > 0f && BondOfLifeStatusHandler.GetValue(owner) >= maximum * .3f;
        });
        BattleSpecialRules.RegisterNumberedCondition("4320601", (_, c) => Owns(c?.Status, 3206, 4320601) && WeaponHooks.IsElemental(c.DamageElement));
        BattleSpecialRules.RegisterNumberedCondition("4321004", (_, c) => Owns(c?.Status, 3210, 4321004)
            && WeaponHooks.IsElemental(c.DamageElement) && WeaponHooks.OwnStatus(c.Status, "ST_Weapon_4321002")?.RuntimeValue >= 3f);
        BattleSpecialRules.RegisterNumberedCondition("4321302", (_, c) => Owns(c?.Status, 3213, 4321302) && WeaponHooks.IsElemental(c.DamageElement));
    }

    internal static bool TryResolve(StatusEffectData effect, StatusInstance status, out float value)
    {
        value = 0f;
        if (effect?.EffectType != "Buff" || effect.DamageHook != "Hook(" + effect.StatusEffectID + ")") return false;
        switch (effect.StatusEffectID)
        {
            case 431660401:
                value = Math.Min(RequireOwner(status, 3166, 4316604).GetFinalDEF() / 1000f * WeaponHooks.Param(status, 3),
                    WeaponHooks.Param(status, 4)); return true;
            case 431740201:
                value = RequireOwner(status, 3174, 4317402).GetFinalDEF() * WeaponHooks.Param(status, 2); return true;
            case 431960201: case 431960301:
                value = RequireOwner(status, 3196, effect.StatusEffectID / 100).GetFinalHP()
                    * WeaponHooks.Param(status, effect.StatusEffectID == 431960201 ? 2 : 3); return true;
            case 431970101:
                value = Math.Min(Math.Max(RequireOwner(status, 3197, 4319701).GetFinalRechargeRate() - 1f, 0f)
                    * WeaponHooks.Param(status, 1), WeaponHooks.Param(status, 2)); return true;
            case 431990101:
                value = RequireOwner(status, 3199, 4319901).GetFinalEM() * WeaponHooks.Param(status, 1); return true;
            case 431990301:
                value = RequireOwner(status, 3199, 4319903).GetFinalEM() * WeaponHooks.Param(status, 2) * status.RuntimeValue; return true;
            case 432090201:
                value = RequireOwner(status, 3209, 4320902).GetFinalHP() * WeaponHooks.Param(status, 2); return true;
            case 432130201:
                value = Math.Min(RequireOwner(status, 3213, 4321302).GetFinalHP() / 1000f * WeaponHooks.Param(status, 2),
                    WeaponHooks.Param(status, 3)); return true;
            case 432220301:
                RequireOwner(status, 3222, 4322203);
                value = WeaponHooks.Param(status, 2) * (WeaponHooks.OwnStatus(status, "ST_Weapon_4322204") != null ? 1.5f : 1f); return true;
            case 432220401:
                RequireOwner(status, 3222, 4322204);
                value = WeaponHooks.Param(status, 3) * (WeaponHooks.OwnStatus(status, "ST_Weapon_4322203") != null ? 1.5f : 1f); return true;
            default: return false;
        }
    }

    private static bool Owns(StatusInstance status, int weapon, int id)
        => status?.StatusID2 == "ST_Weapon_" + id && status.MainData?.StatusID == id
            && status.WeaponContext?.WeaponID == weapon && status.WeaponContext.Equipper != null
            && status.Caster == status.WeaponContext.Equipper;

    private static BattleEntity RequireOwner(StatusInstance status, int weapon, int id)
    {
        if (!Owns(status, weapon, id)) throw new InvalidOperationException("武器属性换算缺少正确父状态和来源：" + id);
        return status.WeaponContext.Equipper;
    }

    private static List<int> OtherMembers(BattleManager battle, BattleEntity owner, StatusInstance status, int weapon = 3168, int id = 4316803)
    {
        var result = new List<int>();
        if (battle == null || !Owns(status, weapon, id) || owner != status.WeaponContext.Equipper || owner.Side != BattleSide.Ally) return result;
        bool hasOwner = false;
        foreach (var ally in battle.Allies)
        {
            var member = ally?.Entity;
            if (member == owner) { hasOwner = true; continue; }
            if (member != null && member.IsAlive && member.Side == owner.Side && member.SlotPosition > 0
                && ReferenceEquals(battle.Field.GetSlot(member.Side, member.SlotPosition)?.Occupant, member)) result.Add(member.SlotPosition);
        }
        return hasOwner ? result : new List<int>();
    }

    private sealed class HealthFact
    { internal BattleEntity Owner, Target; internal float Before, Maximum; }
    private sealed class HitFrame : IDisposable
    {
        internal HitFrame Previous;
        internal long Hit;
        internal readonly Dictionary<BattleEntity, HealthFact> Targets = new Dictionary<BattleEntity, HealthFact>();
        public void Dispose() => Current.Value = Previous;
    }
    private static readonly AsyncLocal<HitFrame> Current = new AsyncLocal<HitFrame>();
    private static readonly ConditionalWeakTable<StatusInstance, IDisposable> ShieldSubscriptions = new ConditionalWeakTable<StatusInstance, IDisposable>();
    private static readonly ConditionalWeakTable<BattleDeclaration, HealthFact> Completed = new ConditionalWeakTable<BattleDeclaration, HealthFact>();
    private static IDisposable BeginHit(long hit, BattleEntity owner, IEnumerable<BattleEntity> targets)
    {
        var frame = new HitFrame { Previous = Current.Value, Hit = hit };
        // 未持有狼末监听的来源不保存目标生命；中途才获得监听也不能追溯触发本hit。
        if (Owns(owner?.GetStatus("ST_Weapon_4317002"), 3170, 4317002) && targets != null)
            foreach (var target in targets)
                if (target != null && target.IsAlive && target.Side != owner.Side && !frame.Targets.ContainsKey(target))
                    frame.Targets.Add(target, new HealthFact { Owner = owner, Target = target, Before = target.CurrentHP, Maximum = target.GetFinalHP() });
        Current.Value = frame; return frame;
    }
    private static void ObserveHit(BattleDeclaration declaration)
    {
        var frame = Current.Value; var damage = declaration?.Get<DamageEventData>();
        if (frame == null || frame.Hit == 0 || declaration.Header.HitId != frame.Hit || damage?.Subject == null
            || !damage.HitLanded || !damage.Source.CountsAsHit || damage.Source.SourceKind == ReactionSourceKind.DerivedReaction
            || !frame.Targets.TryGetValue(damage.Subject, out var fact) || damage.Source.SourceEntity != fact.Owner) return;
        Completed.Add(declaration, fact); // 原hit退出后，延迟状态响应仍只读本声明的冻结事实。
    }
}
