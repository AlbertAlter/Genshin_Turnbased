using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

/// <summary>3094/3095专用接入；没有通用Count初值、消费或移除伤害规则。</summary>
internal static class WeaponCompletionHooks
{
    private static ConditionalWeakTable<BattleEntity, Dictionary<string, StatusActionRuntimeState>> Ledgers
        = new ConditionalWeakTable<BattleEntity, Dictionary<string, StatusActionRuntimeState>>();

    internal static void Register()
    {
        BattleSpecialRules.RegisterNumberedCondition("4309402", (_, c) =>
        {
            var status = c?.Status;
            var owner = status?.WeaponContext?.Equipper;
            return status?.StatusID2 == EndOfLineWeaponCount.ID && owner != null && status.IsActive
                && status.RuntimeValue > 0f && owner.HasStatusInstance(status) && EquipmentStatusLifetime.CanExecute(status)
                && c.EventHookName == "PostSelfDamage" && c.HitEffectID != EndOfLineWeaponCount.EffectID
                && c.DamageEvent?.Source?.SourceEntity == owner && c.DamageEvent.HitLanded && HasLivingHitTarget(c, owner);
        });
        BattleSpecialRules.RegisterEntityReset(owner => Ledgers.Remove(owner));
        BattleEventHub.SessionReset += () => Ledgers = new ConditionalWeakTable<BattleEntity, Dictionary<string, StatusActionRuntimeState>>();
    }

    private static bool HasLivingHitTarget(ScriptHookContext context, BattleEntity owner)
    {
        var battle = BattleManager.Instance;
        if (battle == null || context.ActionTargetPositions == null) return false;
        var side = owner.Side == BattleSide.Ally ? BattleSide.Enemy : BattleSide.Ally;
        foreach (int position in context.ActionTargetPositions)
            if (battle.GetEntityByPosition(side, position)?.IsAlive == true) return true;
        return false; // 与原随机目标解析的存活资格一致；空池不先占一次回合触发。
    }

    internal static bool TryGetSharedRuntime(StatusInstance status, StatusActionData action, string actionKey,
        out StatusActionRuntimeState runtime)
    {
        runtime = null;
        if (status?.StatusID2 != EndOfLineWeaponCount.ID || action?.ActionType != "OnTrigger"
            || !ScriptHookParser.TryFind(action.ScriptHook, "PostSelfDamage", out _)) return false;
        var owner = WeaponCountLifetime.RequireOwner(status, status.WeaponContext?.Equipper, 3094);
        string source = status.WeaponContext.SourceInstanceID;
        if (string.IsNullOrEmpty(source)) throw new InvalidOperationException("沿洄回合额度缺少武器来源身份。");
        // 重用公共Pending/Turn/Cooldown账本，只改变这条明确行动的所有权；实例重施/重建都不清额度。
        string key = source + "|" + owner.EquipmentDeathVersion + "|" + actionKey;
        var ledger = Ledgers.GetValue(owner, _ => new Dictionary<string, StatusActionRuntimeState>(StringComparer.Ordinal));
        if (!ledger.TryGetValue(key, out runtime)) ledger.Add(key, runtime = new StatusActionRuntimeState());
        return true;
    }
}

internal sealed class EndOfLineWeaponCount : CountStatusHandler
{
    internal const string ID = "ST_Weapon_4309402";
    internal const string EffectID = "STE_Weapon_430940201";
    private sealed class Use { internal bool Landed; internal int Depth; }
    private sealed class Window
    {
        internal readonly Dictionary<long, Use> Executing = new Dictionary<long, Use>();
        internal readonly HashSet<long> Completed = new HashSet<long>();
    }
    private static void Reset(StatusInstance status, BattleEntity owner)
    {
        status.RuntimeValue = 3f; status.CountRuntimeState = new Window();
        WeaponCountLifetime.Refresh(status, owner, 4, 2);
    }
    public override void OnCreated(StatusInstance status, object host) => Reset(status, WeaponCountLifetime.RequireOwner(status, host, 3094));
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    { Reset(status, WeaponCountLifetime.RequireApplication(status, host, application, 3094)); return status; }
    public override void OnRemoved(StatusInstance status, object host) { status.RuntimeValue = 0f; status.CountRuntimeState = null; }

    private sealed class Completion : IDisposable
    {
        private Action complete;
        internal Completion(Action action) { complete = action; }
        public void Dispose() { var action = complete; complete = null; action?.Invoke(); }
    }
    internal static IDisposable BeginDamage(StatusEffectData effect, StatusInstance status, object host, long execution)
    {
        if (effect?.StatusEffectID2 != EffectID || status?.StatusID2 != ID) return null;
        var owner = WeaponCountLifetime.RequireOwner(status, host, 3094);
        if (execution <= 0) throw new InvalidOperationException("沿洄伤害缺少真实效果执行编号。");
        var window = status.CountRuntimeState as Window;
        if (window == null || !status.IsActive || !owner.HasStatusInstance(status)) return null;
        if (!window.Executing.TryGetValue(execution, out var use)) window.Executing.Add(execution, use = new Use());
        use.Depth++;
        return new Completion(() =>
        {
            if (--use.Depth > 0) return;
            window.Executing.Remove(execution);
            if (!use.Landed || !window.Completed.Add(execution) || !status.IsActive || !owner.HasStatusInstance(status)
                || !ReferenceEquals(status.CountRuntimeState, window)) return;
            status.RuntimeValue = Math.Max(0f, status.RuntimeValue - 1f);
            if (status.RuntimeValue == 0f) owner.RemoveStatusInstance(status, "EndOfLineExhausted");
        });
    }
    public override CountDamageContribution PrepareDamage(StatusInstance status, object host, CountDamageContext context)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, 3094);
        if (context?.Attacker != owner || context.EffectID != EffectID || context.SourceKind != ReactionSourceKind.StatusEffect
            || context.Target == null || context.Target.Side == owner.Side || !status.IsActive
            || !ReferenceEquals(owner.GetStatus(ID), status) || !(status.CountRuntimeState is Window window)
            || !window.Executing.TryGetValue(context.EffectExecutionID, out var use)) return null;
        return new CountDamageContribution(0f, _ => use.Landed = true, null);
    }
}

/// <summary>3095的终结请求由具体窗口一次性提交；允许正常移除后执行，不复活失效状态。</summary>
internal sealed class KingsSquireWeaponCount : CountStatusHandler
{
    internal const string ID = "ST_Weapon_4309502";
    private sealed class Window { internal bool Claimed; }

    internal sealed class Terminal
    {
        internal readonly StatusInstance Status;
        internal readonly BattleEntity Owner;
        internal readonly List<StatusActionData> Actions;
        internal readonly ScriptHookContext Context;
        internal Terminal(StatusInstance status, BattleEntity owner, List<StatusActionData> actions, ScriptHookContext context)
        { Status = status; Owner = owner; Actions = actions; Context = context; }
        internal bool CanExecute => Owner.IsAlive && EquipmentStatusLifetime.CanExecute(Status)
            && BattleManager.Instance != null && BattleManager.Instance.IsBattleRunning && !BattleManager.Instance.IsBattleOver
            && !BattleExecution.IsCancelled;
    }

    private static void Reset(StatusInstance status, BattleEntity owner)
    {
        status.RuntimeValue = WeaponHooks.Param(status, 1);
        status.CountRuntimeState = new Window();
        WeaponCountLifetime.Refresh(status, owner, 3, 2);
    }
    public override void OnCreated(StatusInstance status, object host) => Reset(status, WeaponCountLifetime.RequireOwner(status, host, 3095));
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    { Reset(status, WeaponCountLifetime.RequireApplication(status, host, application, 3095)); return status; }

    private static Terminal Claim(StatusInstance status, BattleEntity owner, List<StatusActionData> actions,
        ScriptHookContext context)
    {
        if (!(status.CountRuntimeState is Window window) || window.Claimed) return null;
        var terminal = new Terminal(status, owner, new List<StatusActionData>(), context);
        if (!terminal.CanExecute) return null;
        if (actions != null)
            foreach (var action in actions)
                if (action.ActionType == "OnEnd") terminal.Actions.Add(action);
        if (terminal.Actions.Count != 1 || terminal.Actions[0].Param1 != "STE_Weapon_430950202")
            throw new InvalidOperationException("森林教诲缺少唯一的原表终结伤害OnEnd行动。");
        window.Claimed = true;
        return terminal;
    }

    internal static Task EndNaturally(StatusInstance status, object host, List<StatusActionData> actions)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, 3095);
        if (!status.IsActive || !owner.HasStatusInstance(status) || status.IsPermanent || status.RemainingPhaseCount > 0)
            return Task.CompletedTask;
        var context = ScriptHookContext.Current?.Snapshot() ?? new ScriptHookContext();
        context.Caster = owner; context.Status = status; context.StatusHost = owner; context.Target = owner;
        var terminal = Claim(status, owner, actions, context);
        return terminal == null ? Task.CompletedTask : owner.CharacterCtrl.ExecuteCommittedWeaponEnd(terminal);
    }

    public override void OnRemoved(StatusInstance status, object host)
    {
        var owner = WeaponCountLifetime.RequireOwner(status, host, 3095);
        var declaration = BattleEventHub.Current;
        var removed = declaration?.Get<StatusEventData>();
        // 只接受本实例已提交的明确玩法移除；死亡、绑定清理、重置、结束清场都不模拟OnEnd。
        if (removed?.Kind == BattleEventKind.StatusRemoved && removed.InstanceId == status.ApplyOrder && removed.Host == host
            && (removed.Reason == "Removed" || removed.Reason == "Expired" || removed.Reason == "Replaced"))
        {
            var data = DataManager.Instance;
            List<StatusActionData> actions = null;
            if (data != null) data.StatusActionDict.TryGetValue(ID, out actions);
            var terminal = Claim(status, owner, actions, new ScriptHookContext
                { Caster = owner, Status = status, StatusHost = owner, Target = owner, Declaration = declaration });
            if (terminal != null)
                BattleResolutionContext.Submit(status.ApplyOrder, () => !terminal.CanExecute ? null
                    : new BattleResolutionContext.PreparedTrigger(
                        () => owner.CharacterCtrl.ExecuteCommittedWeaponEnd(terminal), null));
        }
        status.RuntimeValue = 0f; status.CountRuntimeState = null;
    }
}
