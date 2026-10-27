using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>512学士：实际能量增加声明→全队共享回合额度→原GainEnergy效果。</summary>
public static class ScholarArtifactHooks
{
    private const string StatusID = "ST_Artifact_451202";
    private sealed class Subscription { internal IDisposable Value; }
    private sealed class TeamLimit
    {
        internal long Session;
        internal readonly Dictionary<BattleSide, int> Turns = new Dictionary<BattleSide, int>();
    }
    private static readonly ConditionalWeakTable<StatusInstance, Subscription> Subscriptions
        = new ConditionalWeakTable<StatusInstance, Subscription>();
    private static readonly ConditionalWeakTable<BattleManager, TeamLimit> Limits
        = new ConditionalWeakTable<BattleManager, TeamLimit>();

    internal static void Register()
    {
        BattleSpecialRules.RegisterStatusLifecycle(StatusID, Start, Stop);
        BattleSpecialRules.RegisterNumberedCondition("451202", (argument, context) =>
            Matches(context?.Status?.Caster ?? context?.Caster, context?.Declaration));
        BattleSpecialRules.RegisterTargets("Hook(45120201)", (battle, owner) =>
        {
            var positions = new List<int>();
            var team = battle?.TeamBonuses;
            if (team == null || team.IsDisposed || !team.Contains(owner)) return positions;
            foreach (BattleEntity member in team.Members)
                if (member != null && member.IsAlive && member.Side == owner.Side
                    && (member.WeaponType == 2 || member.WeaponType == 4)) positions.Add(member.SlotPosition);
            return positions;
        });
    }

    private static bool Matches(BattleEntity owner, BattleDeclaration declaration)
    {
        var battle = BattleManager.Instance;
        var team = battle?.TeamBonuses;
        var resource = declaration?.Get<ResourceEventData>();
        if (owner == null || team == null || team.IsDisposed || !team.Contains(owner)
            || resource == null || resource.Resource != "Energy" || resource.Added <= 0d
            || !team.Contains(resource.Subject) || resource.Subject.Side != owner.Side) return false;
        return !Limits.TryGetValue(battle, out var limit) || limit.Session != declaration.Header.SessionId
            || !limit.Turns.TryGetValue(owner.Side, out int turn) || turn != battle.TurnCount;
    }

    private static void Start(StatusInstance status, object host)
    {
        if (!(host is BattleEntity owner) || owner != status.Caster || owner.CharacterCtrl == null)
            throw new InvalidOperationException("学士必须初始化在穿戴者本体。");
        var subscription = new Subscription();
        Subscriptions.Add(status, subscription);
        subscription.Value = BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged,
            (header, resource) =>
            {
                BattleDeclaration declaration = BattleEventHub.Current;
                if (!Matches(owner, declaration)) return;
                var dm = DataManager.Instance;
                var battle = BattleManager.Instance;
                if (dm == null || !dm.StatusActionDict.TryGetValue(StatusID, out var actions)) return;
                foreach (var action in actions)
                {
                    if (action.ActionType != "OnTrigger"
                        || !ScriptHookParser.TryFind(action.ScriptHook, "Hook", out string id)
                        || id?.Trim() != "451202") continue;
                    if (!owner.CharacterCtrl.TryExecuteEventStatusAction(status, action, owner, new ScriptHookContext
                    {
                        Caster = owner, Target = resource.Subject, Status = status, StatusHost = owner,
                        Declaration = declaration
                    })) continue;
                    // 当前回调已进入正式执行，TryExecute确认并预留原行动后、子效果开始前占用队伍额度。
                    // 同回合所有穿戴者及本次额外回能派生的声明都因此被阻止，不借助全局最近一次事件。
                    TeamLimit limit = Limits.GetValue(battle, key => new TeamLimit());
                    if (limit.Session != header.SessionId) { limit.Session = header.SessionId; limit.Turns.Clear(); }
                    limit.Turns[owner.Side] = battle.TurnCount;
                    break;
                }
            }, status.ApplyOrder, () => status.IsActive && owner.HasStatusInstance(status));
    }

    private static void Stop(StatusInstance status, object host)
    {
        if (!Subscriptions.TryGetValue(status, out var subscription)) return;
        subscription.Value?.Dispose();
        Subscriptions.Remove(status);
    }
}
