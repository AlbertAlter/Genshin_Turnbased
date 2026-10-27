using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

/// <summary>554烬城、558穹境：读取本次反应声明，不用最近反应、伤害元素或响应时状态倒推。</summary>
public static class ArtifactReactionHooks
{
    private const string Cinder = "ST_Artifact_455402";
    private const string Moon = "ST_Artifact_455802";
    private sealed class Subscription
    {
        internal IDisposable Value;
        internal readonly HashSet<long> Events = new HashSet<long>();
    }
    private static readonly ConditionalWeakTable<StatusInstance, Subscription> Subscriptions
        = new ConditionalWeakTable<StatusInstance, Subscription>();

    internal static void Register()
    {
        BattleSpecialRules.RegisterNumberedCondition("455402", (argument, context) => MatchesCinder(context));
        string[] elements = { "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Geo", "Dendro" };
        for (int index = 0; index < 14; index++)
        {
            string element = elements[index % 7];
            bool blessed = index >= 7;
            BattleSpecialRules.RegisterNumberedCondition((45540201 + index).ToString(CultureInfo.InvariantCulture),
                (argument, context) =>
                {
                    ReactionEventData reaction = Reaction(context);
                    return MatchesCinder(context) && Contains(reaction.Elements, element)
                        && (!blessed || reaction.SourceHadNightSoulBlessing);
                });
        }
        BattleSpecialRules.RegisterNumberedCondition("455802", (argument, context) =>
            MatchesMoon(context?.Status?.Caster ?? context?.Caster, Reaction(context)));
        BattleSpecialRules.RegisterStatusLifecycle(Moon, StartMoon, StopMoon);
    }

    private static ReactionEventData Reaction(ScriptHookContext context)
        => context?.Declaration?.Get<ReactionEventData>() ?? context?.ReactionEvent?.Declaration?.Get<ReactionEventData>();

    private static bool MatchesCinder(ScriptHookContext context)
    {
        var reaction = Reaction(context);
        return context?.Status?.StatusID2 == Cinder && reaction != null && reaction.Reaction != ReactionType.None
            && reaction.Source == context.Status.Caster && reaction.SourceWasPartyMember
            && ArtifactConditionHooks.IsElement(reaction.SourceElement) && Contains(reaction.Elements, reaction.SourceElement);
    }

    private static bool Contains(IReadOnlyList<string> elements, string element)
    {
        if (elements != null) foreach (string candidate in elements) if (candidate == element) return true;
        return false;
    }

    private static bool MatchesMoon(BattleEntity owner, ReactionEventData reaction)
        => owner != null && reaction != null && reaction.SourceWasPartyMember && reaction.SourceSide == owner.Side
            && ReactionDamageCalculator.IsLunar(reaction.Reaction) && reaction.WasOnField(owner);

    private static void StartMoon(StatusInstance status, object host)
    {
        BattleEntity owner = RefreshingArtifactCount.RequireOwner(status, host);
        var subscription = new Subscription();
        Subscriptions.Add(status, subscription);
        subscription.Value = BattleEventHub.Subscribe<ReactionEventData>(BattleEventKind.ReactionOccurred, (header, reaction) =>
        {
            if (!MatchesMoon(owner, reaction) || !subscription.Events.Add(header.EventId)) return;
            var dm = DataManager.Instance;
            if (dm == null || !dm.StatusActionDict.TryGetValue(Moon, out var actions)) return;
            foreach (var action in actions)
            {
                if (action.ActionType != "OnTrigger" || !ScriptHookParser.TryFind(action.ScriptHook, "Hook", out string id)
                    || id?.Trim() != "455802") continue;
                owner.CharacterCtrl.TryExecuteEventStatusAction(status, action, owner, new ScriptHookContext
                {
                    Caster = owner, Status = status, StatusHost = owner, Target = reaction.Subject,
                    Declaration = BattleEventHub.Current
                }, () => status.IsActive && owner.HasStatusInstance(status) && EquipmentStatusLifetime.CanExecute(status));
            }
        }, status.ApplyOrder, () => status.IsActive && owner.HasStatusInstance(status) && EquipmentStatusLifetime.CanExecute(status));
    }

    private static void StopMoon(StatusInstance status, object host)
    {
        if (!Subscriptions.TryGetValue(status, out var subscription)) return;
        subscription.Value?.Dispose();
        Subscriptions.Remove(status);
    }
}
