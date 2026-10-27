using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>545/548/552的获点入口：只消费已有正式声明，原ApplyStatus行动负责施加Count。</summary>
public static class ArtifactCountEventHooks
{
    private sealed class Subscription
    {
        internal IDisposable Value;
        internal readonly HashSet<long> Seen = new HashSet<long>();
    }
    private static readonly ConditionalWeakTable<StatusInstance, Subscription> Subscriptions
        = new ConditionalWeakTable<StatusInstance, Subscription>();

    internal static void Register()
    {
        BattleSpecialRules.RegisterNumberedCondition("45450201", (argument, context) =>
        {
            ReactionEventData reaction = context?.Declaration?.Get<ReactionEventData>()
                ?? context?.ReactionEvent?.Declaration?.Get<ReactionEventData>();
            if (reaction == null || reaction.Source != context?.Status?.Caster) return false;
            return reaction.Reaction == ReactionType.Bloom || reaction.Reaction == ReactionType.Hyperbloom
                || reaction.Reaction == ReactionType.Burgeon || reaction.Reaction == ReactionType.LunarBloom;
        });
        RegisterHP();
        RegisterBond();
    }

    private static bool HPMatches(BattleEntity owner, BattleDeclaration declaration)
    {
        var change = declaration?.Get<HPChangedEventData>();
        return change != null && change.Subject == owner && change.Before != change.After;
    }

    private static bool BondMatches(BattleEntity owner, BattleDeclaration declaration)
    {
        var change = declaration?.Get<ResourceEventData>();
        return change != null && change.Subject == owner && change.Resource == "BondOfLife"
            && change.Before != change.After;
    }

    private static void RegisterHP()
    {
        const string id = "ST_Artifact_454802";
        BattleSpecialRules.RegisterNumberedCondition("454802", (argument, context) =>
            HPMatches(context?.Status?.Caster, context?.Declaration));
        BattleSpecialRules.RegisterStatusLifecycle(id, (status, host) =>
            Start<HPChangedEventData>(status, host, BattleEventKind.HPChanged, "454802", HPMatches), Stop);
    }

    private static void RegisterBond()
    {
        const string id = "ST_Artifact_455202";
        BattleSpecialRules.RegisterNumberedCondition("455202", (argument, context) =>
            BondMatches(context?.Status?.Caster, context?.Declaration));
        BattleSpecialRules.RegisterStatusLifecycle(id, (status, host) =>
            Start<ResourceEventData>(status, host, BattleEventKind.ResourceChanged, "455202", BondMatches), Stop);
    }

    private static void Start<T>(StatusInstance status, object host, BattleEventKind kind,
        string hookID, Func<BattleEntity, BattleDeclaration, bool> matches) where T : BattleEventData
    {
        BattleEntity owner = RefreshingArtifactCount.RequireOwner(status, host);
        var subscription = new Subscription();
        Subscriptions.Add(status, subscription);
        subscription.Value = BattleEventHub.Subscribe<T>(kind, (header, fact) =>
        {
            BattleDeclaration declaration = BattleEventHub.Current;
            if (!matches(owner, declaration) || !subscription.Seen.Add(header.EventId)) return;
            var dm = DataManager.Instance;
            if (dm == null || !dm.StatusActionDict.TryGetValue(status.StatusID2, out var actions)) return;
            foreach (var action in actions)
            {
                if (action.ActionType != "OnTrigger"
                    || !ScriptHookParser.TryFind(action.ScriptHook, "Hook", out string id)
                    || id?.Trim() != hookID) continue;
                owner.CharacterCtrl.TryExecuteEventStatusAction(status, action, owner, new ScriptHookContext
                {
                    Caster = owner, Target = owner, Status = status, StatusHost = owner,
                    Declaration = declaration
                }, () => status.IsActive && owner.HasStatusInstance(status));
            }
        }, status.ApplyOrder, () => status.IsActive && owner.HasStatusInstance(status));
        // HPChanged一份事实只处理一次，不再从OnDamage/Heal各补一次；纯溢出与契值抵扣没有HP事实。
        // BondOfLife按实际前后值判断；治疗抵扣引起的契值下降合法，零变化请求不获点。
    }

    private static void Stop(StatusInstance status, object host)
    {
        if (!Subscriptions.TryGetValue(status, out var subscription)) return;
        subscription.Value?.Dispose();
        Subscriptions.Remove(status);
    }
}
