using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

/// <summary>六命只缓存本窗口真实死亡；完成行为或独立状态链后走原状态行动执行Revive。</summary>
public static class BarbaraRevivalHooks
{
    public const string StatusID = "ST_Barbara_C6";
    private sealed class Budget { internal int Session; internal bool Used; }
    private sealed class Memory
    {
        internal StatusInstance Status;
        internal BattleEntity Owner, PendingTarget;
        internal long PendingDeathVersion;
        internal Budget Budget;
        internal IDisposable Listener;
    }
    private static readonly ConditionalWeakTable<BattleEntity, Budget> Budgets = new ConditionalWeakTable<BattleEntity, Budget>();
    private static readonly ConditionalWeakTable<StatusInstance, Memory> States = new ConditionalWeakTable<StatusInstance, Memory>();

    internal static void Register()
    {
        BattleSpecialRules.RegisterStatusLifecycle(StatusID, Start, Stop);
        BattleSpecialRules.RegisterCondition("Barbara_C6", (argument, context) =>
            string.IsNullOrEmpty(argument) && context?.Status != null
            && States.TryGetValue(context.Status, out var memory) && Available(memory)
            && memory.PendingTarget != null && memory.PendingTarget == context.Target && context.Target.IsDead
            && context.Target.EquipmentDeathVersion == memory.PendingDeathVersion
            && context.Declaration?.Kind == BattleEventKind.Death);
        BattleSpecialRules.RegisterTargets("Barbara_C6Target()", (battle, owner) =>
        {
            var context = ScriptHookContext.Current;
            var target = context?.Target;
            return battle != null && context?.Status != null
                && States.TryGetValue(context.Status, out var memory) && Available(memory)
                && memory.Owner == owner && memory.PendingTarget == target && target != null && target.IsDead
                && target.EquipmentDeathVersion == memory.PendingDeathVersion
                && battle.GetEntityByPosition(target.Side, target.SlotPosition) == target
                    ? new List<int> { target.SlotPosition } : new List<int>();
        });
    }

    private static bool Available(Memory memory)
        => memory.Status.IsActive && memory.Owner.IsAlive && memory.Owner.CharacterCtrl != null && memory.Owner.ConstellationLevel >= 6
            && memory.Owner.HasStatusInstance(memory.Status) && !memory.Budget.Used
            && memory.Budget.Session == BattleDeathResolution.SessionVersion;

    private static void Start(StatusInstance status, object host)
    {
        if (!(host is BattleEntity owner) || owner != status.Caster || owner.EntityID != 1014)
            throw new InvalidOperationException("芭芭拉六命控制状态必须初始化在本人身上。");
        var budget = Budgets.GetValue(owner, _ => new Budget { Session = BattleDeathResolution.SessionVersion });
        if (budget.Session != BattleDeathResolution.SessionVersion)
        { budget.Session = BattleDeathResolution.SessionVersion; budget.Used = false; }
        var memory = new Memory { Owner = owner, Status = status, Budget = budget };
        States.Add(status, memory);
        memory.Listener = BattleEventHub.Subscribe<DeathEventData>(BattleEventKind.Death, (header, fact) =>
        {
            var target = fact.Subject;
            if (target == null || target == owner || target.Side != owner.Side) return;
            BattleDeathResolution.RequestRevival(status, BattleEventHub.Current, request => Resolve(memory, request));
        }, status.ApplyOrder, () => Available(memory));
    }

    private static async Task Resolve(Memory memory, BattleDeathResolution.Revival request)
    {
        if (!Available(memory) || memory.Owner.EquipmentDeathVersion != request.SourceDeathVersion) return;
        var data = DataManager.Instance;
        if (data == null || !data.StatusActionDict.TryGetValue(StatusID, out var actions)) return;
        var action = actions.Find(row => row.ActionType == "OnTrigger"
            && ScriptHookParser.TryFind(row.ScriptHook, "Barbara_C6", out _));
        if (action == null) return;
        var candidates = new List<BattleDeathResolution.Death>();
        long first = long.MaxValue;
        var battle = BattleManager.Instance;
        foreach (var death in request.Candidates)
        {
            var target = death.Target;
            if (target == null || target.IsAlive || target == memory.Owner || target.Side != memory.Owner.Side
                || target.EquipmentDeathVersion != death.TargetDeathVersion
                || death.Declaration == null || battle == null
                || battle.GetEntityByPosition(target.Side, target.SlotPosition) != target) continue;
            if (death.BatchOrder < first) { first = death.BatchOrder; candidates.Clear(); }
            if (death.BatchOrder == first) candidates.Add(death);
        }
        if (candidates.Count == 0) return;
        // 同批次是真正同时倒下；不能让目标遍历顺序冒充先后。先稳定排列，再只抽一次统一随机。
        candidates.Sort((a, b) => a.Target.SlotPosition.CompareTo(b.Target.SlotPosition));
        var chosen = candidates[candidates.Count == 1 ? 0 : BattleRandom.NextInt(0, candidates.Count)];
        request.SelectedTarget = chosen.Target;
        memory.PendingTarget = chosen.Target;
        memory.PendingDeathVersion = chosen.Target.EquipmentDeathVersion;
        int attemptedSession = BattleDeathResolution.SessionVersion;
        try
        {
            using var rules = chosen.Rules.Enter();
            using var cause = BattleEventHub.Enter(chosen.Declaration);
            memory.Owner.CharacterCtrl.TryExecuteEventStatusAction(memory.Status, action, memory.Owner,
                new ScriptHookContext { Caster = memory.Owner, Status = memory.Status, StatusHost = memory.Owner,
                    Target = chosen.Target, Declaration = chosen.Declaration }, () => Available(memory) && chosen.Target.IsDead
                        && chosen.Target.EquipmentDeathVersion == chosen.TargetDeathVersion
                        && memory.Owner.EquipmentDeathVersion == request.SourceDeathVersion);
            await BattleExecution.Flush();
        }
        finally
        {
            // 已提交成功即消耗；后续HP响应报错/取消也不能把成功的复活免费退回。旧场收尾不污染新场预算。
            if (request.Succeeded && memory.Budget.Session == attemptedSession) memory.Budget.Used = true;
            memory.PendingTarget = null; memory.PendingDeathVersion = 0;
        }
    }

    private static void Stop(StatusInstance status, object host)
    {
        if (!States.TryGetValue(status, out var memory)) return;
        memory.Listener?.Dispose(); States.Remove(status);
        // 预算属于本场的角色，不随普通状态替换重新获得一次。
    }
}
