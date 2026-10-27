using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>3020只订阅已提交的本队主动换人，不重发Switch，也不把免费换人推断成强制换人。</summary>
internal static class WeaponSwitchHooks
{
    private const string ID = "ST_Weapon_4302001";
    private sealed class Subscription { internal IDisposable Value; }
    private static readonly ConditionalWeakTable<StatusInstance, Subscription> Subscriptions = new ConditionalWeakTable<StatusInstance, Subscription>();

    internal static void Register()
    {
        BattleSpecialRules.RegisterStatusLifecycle(ID, Start, Stop);
        BattleSpecialRules.RegisterNumberedCondition("4302001", (_, context) => Eligible(context?.Status, context?.Declaration?.Get<SwitchEventData>()));
        BattleSpecialRules.RegisterTargets("Hook(4302001)", (battle, owner) =>
        {
            var context = ScriptHookContext.Current;
            var change = context?.Declaration?.Get<SwitchEventData>();
            if (owner != context?.Status?.WeaponContext?.Equipper || !Eligible(context?.Status, change)) return new List<int>();
            var target = change.Subject;
            return target.IsAlive && battle?.GetAllyBySlot(target.SlotPosition - 1)?.Entity == target
                ? new List<int> { target.SlotPosition } : new List<int>();
        });
    }

    private static bool Live(StatusInstance status, BattleEntity owner)
        => status.IsActive && owner.HasStatusInstance(status) && EquipmentStatusLifetime.CanExecute(status);

    private static bool Eligible(StatusInstance status, SwitchEventData change)
    {
        var owner = status?.WeaponContext?.Equipper;
        if (status?.StatusID2 != ID || owner == null || status.Caster != owner || status.WeaponContext.WeaponID != 3020
            || owner.Side != BattleSide.Ally || change?.Reason != BattleSwitchReason.Manual
            || change.Outgoing == null || change.Subject == null || change.Outgoing == change.Subject
            || change.Outgoing.Side != owner.Side || change.Subject.Side != owner.Side) return false;
        var battle = BattleManager.Instance;
        if (battle == null) return false;
        foreach (var ally in battle.Allies) if (ally?.Entity == owner) return true;
        return false;
    }

    private static void Start(StatusInstance status, object host)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, 3020);
        var subscription = new Subscription(); Subscriptions.Add(status, subscription);
        subscription.Value = BattleEventHub.Subscribe<SwitchEventData>(BattleEventKind.SwitchResolved, (_, change) =>
        {
            if (!Eligible(status, change) || !DataManager.Instance.StatusActionDict.TryGetValue(ID, out var actions)) return;
            foreach (var action in actions)
                if (action.ActionType == "OnTrigger" && ScriptHookParser.TryFind(action.ScriptHook, "Hook", out var hook) && hook == "4302001")
                    owner.CharacterCtrl.TryExecuteEventStatusAction(status, action, owner, new ScriptHookContext
                    { Caster = owner, Status = status, StatusHost = owner, Target = change.Subject, Declaration = BattleEventHub.Current },
                        () => Live(status, owner));
        }, status.ApplyOrder, () => Live(status, owner));
    }

    private static void Stop(StatusInstance status, object host)
    {
        if (!Subscriptions.TryGetValue(status, out var subscription)) return;
        subscription.Value?.Dispose(); Subscriptions.Remove(status);
    }
}
