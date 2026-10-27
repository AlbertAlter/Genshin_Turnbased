using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>3060/3085真实击杀，3070正元素接触，3074队伍反应，3086占位敌人行动前。</summary>
internal static class WeaponAuraReactionAndFieldHooks
{
    private const string TalkingStick = "ST_Weapon_4307001", EarthShaker = "ST_Weapon_4307401", HuntField = "ST_Weapon_4308602";
    private sealed class Subscriptions
    {
        internal readonly List<IDisposable> Items = new List<IDisposable>();
    }
    private static readonly ConditionalWeakTable<StatusInstance, Subscriptions> Listeners = new ConditionalWeakTable<StatusInstance, Subscriptions>();

    internal static void Register()
    {
        foreach (int weapon in new[] { 3060, 3085 })
        {
            int registered = weapon;
            string id = ((40000 + weapon) * 100 + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            BattleSpecialRules.RegisterNumberedCondition(id, (_, context) => OwnKill(context, registered));
        }
        BattleSpecialRules.RegisterNumberedCondition("430700101", (_, c) => MatchesContact(c?.Status, c?.Declaration?.Get<ElementContactEventData>(), true)
            || MatchesAura(c?.Status, c?.Declaration?.Get<AuraChangedEventData>(), true));
        BattleSpecialRules.RegisterNumberedCondition("430700102", (_, c) => MatchesContact(c?.Status, c?.Declaration?.Get<ElementContactEventData>(), false)
            || MatchesAura(c?.Status, c?.Declaration?.Get<AuraChangedEventData>(), false));
        BattleSpecialRules.RegisterStatusLifecycle(TalkingStick, StartTalkingStick, Stop);
        WeaponEventResponses.Register<ReactionEventData>(EarthShaker, 3074, BattleEventKind.ReactionOccurred,
            "4307401", MatchesReaction);
        BattleSpecialRules.RegisterNumberedCondition("4308602", (_, c) => MatchesField(c?.Status, c?.StatusHost, c?.Declaration?.Get<ActionEventData>()));
        BattleSpecialRules.RegisterStatusLifecycle(HuntField, StartField, Stop);
    }

    private static BattleEntity Owner(StatusInstance status, string id, int weapon)
    {
        var owner = status?.WeaponContext?.Equipper;
        return status?.StatusID2 == id && status.WeaponContext?.WeaponID == weapon && status.Caster == owner ? owner : null;
    }
    private static bool Live(StatusInstance status, object host)
        => status != null && status.IsActive && StatusHostAccess.Contains(host, status)
            && EquipmentStatusLifetime.CanExecute(status) && BattleManager.Instance?.IsBattleRunning == true
            && !BattleManager.Instance.IsBattleOver;
    private static bool OwnKill(ScriptHookContext context, int weapon)
    {
        var owner = Owner(context?.Status, "ST_Weapon_" + ((40000 + weapon) * 100 + 1), weapon);
        var damage = context?.DamageEvent;
        return owner != null && damage?.CausedDeath == true && damage.ActualHPDamage > 0f
            && damage.Source?.SourceEntity == owner && damage.Target != null && damage.Target.Side != owner.Side;
    }
    private static bool ElementMatches(string element, bool pyro)
        => pyro ? element == "Pyro" : element == "Hydro" || element == "Cryo" || element == "Electro" || element == "Dendro";
    private static bool MatchesContact(StatusInstance status, ElementContactEventData fact, bool pyro)
        => Owner(status, TalkingStick, 3070) is BattleEntity owner && fact?.Subject == owner
            && fact.RequestedAmount > 0f && (fact.CanApplyAura || fact.CanTriggerReaction) && ElementMatches(fact.Element, pyro);
    private static bool MatchesAura(StatusInstance status, AuraChangedEventData fact, bool pyro)
    {
        var owner = Owner(status, TalkingStick, 3070);
        if (owner == null || fact?.Subject != owner) return false;
        // 无ReactionContext的直接ApplyAura（例如无伤害自附着）也有真实刷新事实。
        // 不把衰减、消耗、固有元素状态、冻结显示状态或长期保留的池当新接触。
        foreach (var pool in fact.After)
        {
            if (pool.IsIntrinsic || pool.Kind != ReactionPoolKind.NormalAura || pool.Amount <= 0f || !ElementMatches(pool.Element, pyro)) continue;
            foreach (string refreshed in fact.RefreshedPools)
                if (refreshed == ReactionPoolKind.NormalAura + ":" + pool.Element) return true;
        }
        return false;
    }
    private static void StartTalkingStick(StatusInstance status, object host)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, 3070);
        var subscriptions = new Subscriptions(); Listeners.Add(status, subscriptions);
        void Submit(bool pyro)
            => WeaponEventResponses.Dispatch(status, owner, pyro ? "430700101" : "430700102", BattleEventHub.Current);
        subscriptions.Items.Add(BattleEventHub.Subscribe<ElementContactEventData>(BattleEventKind.ElementContactResolved, (_, fact) =>
        {
            if (MatchesContact(status, fact, true)) Submit(true);
            if (MatchesContact(status, fact, false)) Submit(false);
        }, status.ApplyOrder, () => Live(status, host)));
        subscriptions.Items.Add(BattleEventHub.Subscribe<AuraChangedEventData>(BattleEventKind.AuraChanged, (_, fact) =>
        {
            if (MatchesAura(status, fact, true)) Submit(true);
            if (MatchesAura(status, fact, false)) Submit(false);
        }, status.ApplyOrder, () => Live(status, host)));
        // 接触及其残留刷新同时提交时，原同行Pending/Cooldown预留保证只执行一条效果链。
        // 两个ActionKey各自维护3回合冷却；无额外公共附着事件或私有资源计数。
    }
    private static bool MatchesReaction(StatusInstance status, ReactionEventData fact)
    {
        var owner = Owner(status, EarthShaker, 3074);
        if (owner == null || fact == null || !fact.SourceWasPartyMember || fact.SourceSide != owner.Side) return false;
        foreach (string element in fact.Elements) if (element == "Pyro") return true;
        return false;
    }
    private static bool MatchesField(StatusInstance status, object host, ActionEventData fact)
    {
        var owner = Owner(status, HuntField, 3086);
        return owner != null && host is FieldPosition field && fact?.Kind == BattleEventKind.ActionStarted
            && fact.ActionType == "EnemySkill" && fact.Subject != null && fact.Subject.IsAlive
            && fact.Subject.Side != owner.Side && field.Side == fact.Subject.Side
            && ReferenceEquals(field.Occupant, fact.Subject) && ReferenceEquals(fact.Subject.Position, field);
    }
    private static void StartField(StatusInstance status, object host)
    {
        if (!(host is FieldPosition) || Owner(status, HuntField, 3086)?.CharacterCtrl == null)
            throw new InvalidOperationException("苍翠猎弓风之眼必须保留原装备来源并挂载在场地。");
        var subscriptions = new Subscriptions(); Listeners.Add(status, subscriptions);
        subscriptions.Items.Add(BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted, (_, fact) =>
        {
            if (!MatchesField(status, host, fact) || !DataManager.Instance.StatusActionDict.TryGetValue(HuntField, out var actions)) return;
            var owner = status.WeaponContext.Equipper;
            foreach (var action in actions)
                if (action.ActionType == "OnTrigger" && ScriptHookParser.TryFind(action.ScriptHook, "Hook", out string hook) && hook == "4308602")
                    owner.CharacterCtrl.TryExecuteEventStatusAction(status, action, host, new ScriptHookContext
                    { Caster = owner, Status = status, StatusHost = host, Target = fact.Subject, Declaration = BattleEventHub.Current },
                        () => Live(status, host) && MatchesField(status, host, fact));
        }, status.ApplyOrder, () => Live(status, host)));
    }
    private static void Stop(StatusInstance status, object host)
    {
        if (!Listeners.TryGetValue(status, out var subscriptions)) return;
        foreach (var listener in subscriptions.Items) listener.Dispose();
        Listeners.Remove(status);
    }
}
