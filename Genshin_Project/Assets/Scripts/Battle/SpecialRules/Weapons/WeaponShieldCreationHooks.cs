using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>真实造盾者的原声明桥、治疗具体受术者、动态前台受益资格及尘光的单次攻击快照。</summary>
internal static class WeaponShieldCreationHooks
{
    private const string Parent = "ST_Weapon_4322403", Scalar = "WeaponShieldCreateScalar";
    private sealed class Subscription
    { internal IDisposable Listener; internal readonly HashSet<long> Grants = new HashSet<long>(); }
    private static readonly ConditionalWeakTable<StatusInstance, Subscription> Subscriptions = new ConditionalWeakTable<StatusInstance, Subscription>();
    private static readonly ConditionalWeakTable<StatusInstance, StatusInstance> Parents = new ConditionalWeakTable<StatusInstance, StatusInstance>();
    private sealed class Binding : IDisposable
    {
        internal readonly Binding Previous = Current.Value;
        internal readonly StatusInstance Parent;
        internal Binding(StatusInstance parent) { Parent = parent; Current.Value = this; }
        public void Dispose() => Current.Value = Previous;
    }
    private static readonly AsyncLocal<Binding> Current = new AsyncLocal<Binding>();

    internal static void Register()
    {
        RegisterShield("ST_Weapon_4320302", 3203, "4320302");
        RegisterShield("ST_Weapon_4321301", 3213, "4321301");
        RegisterShield("ST_Weapon_4321802", 3218, "4321802");
        RegisterShield("ST_Weapon_4322402", 3224, "4322402");
        BattleSpecialRules.RegisterNumberedCondition("4321804", (_, c) => Qualifies(c, 3218, "ST_Weapon_4321803", true));
        BattleSpecialRules.RegisterNumberedCondition("4322404", (_, c) => Qualifies(c, 3224, Parent, true));
        BattleSpecialRules.RegisterNumberedCondition("4322405", (_, c) =>
        {
            var recipient = c?.Recipient ?? c?.StatusHost as BattleEntity;
            return Qualifies(c, 3224, Parent, false) && recipient != c.Status.WeaponContext.Equipper
                && BattleManager.Instance.TeamBonuses.HasHexerei
                && DataManager.Instance.TeamBonusRules.Hexerei.CharacterIDs().Contains(recipient.EntityID);
        });
        BattleSpecialRules.RegisterStatusLifecycle(Parent, SaveParent, null);
        BattleSpecialRules.RegisterStatusLifecycle("ST_Weapon_4322404", SaveChild, null);
        BattleSpecialRules.RegisterStatusLifecycle("ST_Weapon_4322405", SaveChild, null);
        BattleSpecialRules.RegisterNumberedCondition("4320202", (_, c) =>
        {
            var owner = c?.Status?.WeaponContext?.Equipper;
            return c?.Status?.StatusID2 == "ST_Weapon_4320202" && c.Status.WeaponContext.WeaponID == 3202
                && c.Status.Caster == owner && Member(owner) && ActiveMember() != null && ActiveMember() != owner;
        });
        BattleSpecialRules.RegisterTargets("Hook(432020301)", (battle, owner) =>
        {
            var result = new List<int>(); var source = ScriptHookContext.Current?.Status;
            var heal = ScriptHookContext.Current?.Declaration?.Get<HealEventData>();
            if (source?.StatusID2 != "ST_Weapon_4320203" || source.WeaponContext?.WeaponID != 3202
                || source.Caster != owner || source.WeaponContext.Equipper != owner || heal?.Source != owner || heal.Requested <= 0f) return result;
            foreach (var entity in new[] { owner, heal.Subject })
                if (Member(entity) && entity.IsAlive && !result.Contains(entity.SlotPosition)
                    && ReferenceEquals(battle.Field.GetSlot(entity.Side, entity.SlotPosition)?.Occupant, entity)) result.Add(entity.SlotPosition);
            return result; // 自疗去重，溢出/抵契仍是合法进行治疗；不向未治疗的其余队员扩散。
        });
        BattleSpecialRules.RegisterNumberedCondition("432220202", (_, c) => c?.Status?.StatusID2 == "ST_Weapon_4322202"
            && c.Status.WeaponContext?.WeaponID == 3222 && c.DamageEvent?.Source?.SourceEntity == c.Status.WeaponContext.Equipper
            && c.Status.Caster == c.Status.WeaponContext.Equipper && c.DamageEvent.ActualHPDamage > 0f
            && c.DamageEvent.Source.ReactionType == ReactionType.LunarBloom);
    }

    private static void RegisterShield(string id, int weapon, string hook)
    {
        BattleSpecialRules.RegisterNumberedCondition(hook, (_, c) => c?.Status?.StatusID2 == id
            && c.Status.WeaponContext?.WeaponID == weapon && Matches(c.Status, c.Declaration?.Get<ShieldGrantedEventData>()));
        BattleSpecialRules.RegisterStatusLifecycle(id, (status, host) =>
        {
            var owner = WeaponCountLifetime.RequireOwner(status, host, weapon); var state = new Subscription(); Subscriptions.Add(status, state);
            state.Listener = BattleEventHub.Subscribe<ShieldGrantedEventData>(BattleEventKind.ShieldGranted, (_, grant) =>
            {
                if (Matches(status, grant) && state.Grants.Add(grant.GrantBatchId))
                    WeaponEventResponses.Dispatch(status, owner, hook, BattleEventHub.Current);
            }, status.ApplyOrder, () => status.IsActive && owner.HasStatusInstance(status) && EquipmentStatusLifetime.CanExecute(status));
        }, (status, host) =>
        { if (Subscriptions.TryGetValue(status, out var state)) { state.Listener.Dispose(); Subscriptions.Remove(status); } });
    }

    private static bool Matches(StatusInstance status, ShieldGrantedEventData grant)
    {
        var owner = status?.WeaponContext?.Equipper;
        return owner != null && status.Caster == owner && grant?.Creator == owner && grant.CreatorId == owner.EntityID
            && grant.CreatorSide == owner.Side && grant.Shield.Value > 0f;
    }

    private static bool Member(BattleEntity entity)
        => entity != null && entity.Side == BattleSide.Ally && BattleManager.Instance?.TeamBonuses?.Contains(entity) == true;
    private static BattleEntity ActiveMember()
    {
        var battle = BattleManager.Instance;
        if (battle != null) foreach (var ally in battle.Allies) if (ally?.Entity?.IsAlive == true && ally.IsActive) return ally.Entity;
        return null;
    }

    private static bool Qualifies(ScriptHookContext c, int weapon, string parentID, bool onField)
    {
        var status = c?.Status; var recipient = c?.Recipient ?? c?.StatusHost as BattleEntity;
        if (status?.WeaponContext?.WeaponID != weapon || !Member(recipient) || !recipient.IsAlive
            || status.Host != recipient || status.Caster != status.WeaponContext.Equipper || !status.IsActive) return false;
        var parent = weapon == 3224 && Parents.TryGetValue(status, out var saved) ? saved : WeaponHooks.OwnStatus(status, parentID);
        if (parent == null || !parent.IsActive || parent.StatusID2 != parentID
            || !parent.BoundStatuses.Contains(status) || !parent.WeaponContext.Equipper.HasStatusInstance(parent)
            || !EquipmentStatusLifetime.CanExecute(status)) return false;
        var active = ActiveMember(); return active != null && (active == recipient) == onField;
    }

    private static void SaveParent(StatusInstance status, object host)
    {
        var c = ScriptHookContext.Current; var owner = status.WeaponContext?.Equipper;
        if (owner == null || host != owner || status.Caster != owner || status.WeaponContext.WeaponID != 3224
            || c?.Status?.StatusID2 != "ST_Weapon_4322402" || c.ExecutingStatusEffectID != 432240201
            || c.Status.Caster != owner || c.Status.WeaponContext?.WeaponID != 3224
            || c.Status.WeaponContext.SourceInstanceID != status.WeaponContext.SourceInstanceID)
            throw new InvalidOperationException("尘光快照必须在本次正式造盾触发的父窗口建立时保存。");
        float value = Math.Min(owner.GetFinalATK("", "", -1, "") * WeaponHooks.Param(status, 2) / 1000f, WeaponHooks.Param(status, 3));
        status.Snapshot = status.Snapshot ?? new Dictionary<string, float>(); status.Snapshot[Scalar] = value;
    }

    internal static IDisposable BeginBinding(StatusInstance parent, StatusEffectData effect)
        => parent?.StatusID2 == Parent && (effect?.StatusEffectID == 432240302 || effect?.StatusEffectID == 432240303)
            ? new Binding(parent) : null;
    internal static bool HasExistingBinding(StatusInstance parent, StatusEffectData effect, BattleEntity recipient)
    {
        if (parent?.StatusID2 != Parent) return false;
        foreach (var child in parent.BoundStatuses)
            if (child.IsActive && child.Host == recipient && child.StatusID2 == effect.Param1 && recipient.HasStatusInstance(child)) return true;
        return false;
    }
    private static void SaveChild(StatusInstance child, object host)
    {
        var parent = Current.Value?.Parent;
        if (parent == null || parent.Snapshot == null || !parent.Snapshot.TryGetValue(Scalar, out float value)
            || child.WeaponContext?.SourceInstanceID != parent.WeaponContext?.SourceInstanceID || child.Caster != parent.Caster)
            throw new InvalidOperationException("尘光子项必须从当前具体父窗口继承攻击快照。");
        child.Snapshot = child.Snapshot ?? new Dictionary<string, float>(); child.Snapshot[Scalar] = value; Parents.Add(child, parent);
    }
    internal static bool HandlesApply(StatusEffectData effect) => effect?.EffectType == "ApplyStatus"
        && effect.StatusEffectID == 432240201 && effect.DamageHook == "Hook(432240201)";
    internal static bool TryResolve(StatusEffectData effect, StatusInstance status, out float value)
    {
        value = 0f;
        if (effect?.EffectType != "Buff" || (effect.StatusEffectID != 432240401 && effect.StatusEffectID != 432240501)
            || effect.DamageHook != "Hook(" + effect.StatusEffectID + ")") return false;
        if (status?.MainData?.StatusID != effect.StatusEffectID / 100 || !Parents.TryGetValue(status, out var parent)
            || !parent.IsActive || parent.Snapshot == null || !parent.Snapshot.TryGetValue(Scalar, out var scalar)) return true;
        value = scalar * (effect.StatusEffectID == 432240501 ? .5f : 1f); return true;
    }
}
