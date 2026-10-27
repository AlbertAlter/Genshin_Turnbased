using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>531：穿戴者真实获得结晶盾，或本人触发月结晶；原行动整组替换全队旧的岩伤/反应元素增伤。</summary>
public static class ArchaicPetraArtifactHooks
{
    public const string StatusID = "ST_Artifact_453102";
    private sealed class Subscription
    {
        internal IDisposable Value;
        internal readonly HashSet<long> Events = new HashSet<long>();
    }
    private static readonly ConditionalWeakTable<StatusInstance, Subscription> Subscriptions
        = new ConditionalWeakTable<StatusInstance, Subscription>();

    internal static void Register()
    {
        BattleSpecialRules.RegisterAtomicStatusAction(StatusID);
        string[] elements = { "Pyro", "Hydro", "Electro", "Cryo" };
        for (int index = 0; index < elements.Length; index++)
        {
            string element = elements[index];
            BattleSpecialRules.RegisterNumberedCondition("4531020" + (index + 1), (argument, context) =>
                context?.Status?.StatusID2 == StatusID && GrantedElement(context.Status.Caster, context.Declaration) == element);
        }
        BattleSpecialRules.RegisterNumberedCondition("45310205", (argument, context) =>
        {
            var reaction = context?.Declaration?.Get<ReactionEventData>()
                ?? context?.ReactionEvent?.Declaration?.Get<ReactionEventData>();
            return context?.Status?.StatusID2 == StatusID && reaction != null
                && reaction.Reaction == ReactionType.LunarCrystallize && reaction.Source == context.Status.Caster;
        });
        BattleSpecialRules.RegisterStatusLifecycle(StatusID, Start, Stop);
    }

    private static string GrantedElement(BattleEntity owner, BattleDeclaration declaration)
    {
        var grant = declaration?.Get<ShieldGrantedEventData>();
        return owner != null && grant?.Subject == owner && grant.Shield.Kind == ShieldKind.Crystallize
            && grant.Shield.Value > 0f && grant.Shield.Duration > 0f ? grant.Shield.Element : string.Empty;
    }

    private static void Start(StatusInstance status, object host)
    {
        BattleEntity owner = RefreshingArtifactCount.RequireOwner(status, host);
        var subscription = new Subscription();
        Subscriptions.Add(status, subscription);
        subscription.Value = BattleEventHub.Subscribe<ShieldGrantedEventData>(BattleEventKind.ShieldGranted,
            (header, grant) =>
            {
                BattleDeclaration declaration = BattleEventHub.Current;
                if (string.IsNullOrEmpty(GrantedElement(owner, declaration)) || !subscription.Events.Add(header.EventId)) return;
                var dm = DataManager.Instance;
                if (dm == null || !dm.StatusActionDict.TryGetValue(StatusID, out var actions)) return;
                foreach (var action in actions)
                {
                    if (action.ActionType != "OnTrigger" || !ScriptHookParser.TryFind(action.ScriptHook, "Hook", out string id)
                        || (id != "45310201" && id != "45310202" && id != "45310203" && id != "45310204")) continue;
                    owner.CharacterCtrl.TryExecuteEventStatusAction(status, action, owner, new ScriptHookContext
                    {
                        Caster = owner, Status = status, StatusHost = owner, Target = owner, Declaration = declaration
                    }, () => status.IsActive && owner.HasStatusInstance(status) && EquipmentStatusLifetime.CanExecute(status));
                }
            }, status.ApplyOrder, () => status.IsActive && owner.HasStatusInstance(status) && EquipmentStatusLifetime.CanExecute(status));
        // 全队获得一批结晶盾时，全部结果先提交；多个穿戴者仍按已有ApplyOrder响应，最后成功的一组覆盖前组。
        // 不在条件钩子里直接删/加Buff，不把“盾仍存在”“盾吸收伤害”当作一次获得。
    }

    private static void Stop(StatusInstance status, object host)
    {
        if (!Subscriptions.TryGetValue(status, out var subscription)) return;
        subscription.Value?.Dispose();
        Subscriptions.Remove(status);
    }
}
