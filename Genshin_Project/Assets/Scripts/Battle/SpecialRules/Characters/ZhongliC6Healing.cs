using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 六命独立隐藏监听状态的实例记忆。盾只提供已承伤事实，不拥有待治疗记录。
/// 无BindStatus/Count依赖；破盾保留记录，监听状态移除/钟离死亡清空。
/// 每个hit独立保存40%原最终伤害和8%受益者生命上限，行动结束逐条执行表中Heal。
/// </summary>
public static class ZhongliC6Healing
{
    public const string StatusID = "ST_Zhongli_C6Healing";
    public const string JadeShieldID = "ST_Zhongli_JadeShield";
    public const string Trigger = "Zhongli_C6ActionCompleted()";
    public const string HealHook = "Zhongli_C6Heal()";

    private sealed class Record
    {
        internal BattleEntity Recipient;
        internal float BaseAmount, Cap;
    }
    private sealed class PendingAction
    {
        internal BattleEntity Actor;
        internal string Skill;
        internal int Turn;
        internal bool Ready;
        internal readonly List<Record> Records = new List<Record>();
        internal readonly HashSet<long> DamageEvents = new HashSet<long>();
    }
    private sealed class Memory : IDisposable
    {
        internal readonly Dictionary<long, PendingAction> Pending = new Dictionary<long, PendingAction>();
        internal readonly List<IDisposable> Listeners = new List<IDisposable>();
        public void Dispose()
        { foreach (var listener in Listeners) listener.Dispose(); Listeners.Clear(); Pending.Clear(); }
    }

    public static int PendingRecordCount(StatusInstance status)
        => status?.ScriptRuntimeState is Memory memory ? memory.Pending.Values.Sum(action => action.Records.Count) : 0;

    private static bool Active(StatusInstance state) => state?.IsActive == true
        && state.StatusID2 == StatusID && state.Host == state.Caster
        && state.Caster?.EntityID == 1032 && state.Caster.IsAlive && state.Caster.ConstellationLevel >= 6
        && state.Caster.HasStatusInstance(state);

    internal static void Register()
    {
        BattleSpecialRules.RegisterStatusLifecycle(StatusID, Start, Stop);
        BattleSpecialRules.RegisterEntityReset(owner =>
        { foreach (var state in owner.GetStatusInstances(StatusID)) Stop(state, owner); });
        BattleSpecialRules.RegisterBattleReady(battle =>
        {
            foreach (var controller in battle.Allies)
            {
                var owner = controller?.Entity;
                if (owner?.EntityID != 1032 || !owner.IsAlive || owner.ConstellationLevel < 6 || owner.HasStatus(StatusID)) continue;
                if (!DataManager.Instance.StatusMainDict.TryGetValue(StatusID, out var main))
                    throw new InvalidOperationException("六命钟离缺少独立治疗监听状态：" + StatusID);
                var state = owner.AddStatus(StatusID, owner, 0, 2, 0, main);
                controller.RecordCharacterInitializationStatus(state, 0);
            }
        });
        BattleSpecialRules.RegisterCondition("Zhongli_C6ActionCompleted", (argument, context) =>
            string.IsNullOrEmpty(argument) && TryGetReady(context?.Status, context?.Declaration, out _, out _));
        BattleSpecialRules.RegisterStatusHeal(HealHook, Heal);
    }

    private static void Start(StatusInstance state, object host)
    {
        if (!(host is BattleEntity owner) || owner != state.Caster || owner.EntityID != 1032)
            throw new InvalidOperationException("钟离六命治疗监听状态必须独立挂在钟离本人。");
        var memory = new Memory(); state.ScriptRuntimeState = memory;
        memory.Listeners.Add(BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted, (header, action) =>
        {
            if (header.ActionExecutionId == 0 || action.ActionType != "EnemySkill"
                || action.Subject == null || action.Subject.Side == owner.Side) return;
            int turn = BattleManager.Instance?.TurnCount ?? 0;
            // 未正常完成的行动不会得到治疗；旧回合记录不继续占内存，也不移交给后来的行动。
            foreach (long id in memory.Pending.Where(pair => pair.Value.Turn != turn).Select(pair => pair.Key).ToArray())
                memory.Pending.Remove(id);
            memory.Pending[header.ActionExecutionId] = new PendingAction
                { Actor = action.Subject, Skill = action.SkillId, Turn = turn };
        }, state.ApplyOrder, () => Active(state)));
        memory.Listeners.Add(BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (header, damage) =>
        {
            if (!memory.Pending.TryGetValue(header.ActionExecutionId, out var pending) || pending.Ready
                || damage.Subject == null || damage.Subject.Side != owner.Side || damage.HPAfter <= 0
                || !memory.Pending[header.ActionExecutionId].DamageEvents.Add(header.EventId)) return;
            foreach (var protection in damage.OverallShieldProtections)
            {
                if (protection.Owner != owner || protection.SourceStatus?.StatusID2 != JadeShieldID
                    || protection.DamageBeforeShield <= 0f) continue;
                float cap = Mathf.Max(0f, protection.RecipientMaxHP * .08f);
                pending.Records.Add(new Record { Recipient = damage.Subject,
                    BaseAmount = Mathf.Min(.4f * protection.DamageBeforeShield, cap), Cap = cap });
                break; // 同一来源玉璋盾每个hit目标只记一次，不随其他来源场地盾增加次数。
            }
        }, state.ApplyOrder, () => Active(state)));
        memory.Listeners.Add(BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted, (header, action) =>
        {
            if (!memory.Pending.TryGetValue(header.ActionExecutionId, out var pending) || pending.Ready
                || action.ActionType != "EnemySkill" || action.Subject != pending.Actor || action.SkillId != pending.Skill) return;
            if (pending.Records.Count == 0) { memory.Pending.Remove(header.ActionExecutionId); return; }
            pending.Ready = true;
            var dm = DataManager.Instance;
            if (dm == null || !dm.StatusActionDict.TryGetValue(StatusID, out var actions))
                throw new InvalidOperationException("钟离六命监听状态缺少OnTrigger治疗行动。");
            foreach (var row in actions)
                if (row.ActionType == "OnTrigger" && row.ScriptHook == Trigger)
                    owner.CharacterCtrl.TryExecuteEventStatusAction(state, row, host, new ScriptHookContext
                    { Caster = owner, Status = state, StatusHost = host, Target = owner, Declaration = BattleEventHub.Current },
                        () => Active(state));
        }, state.ApplyOrder, () => Active(state)));
        memory.Listeners.Add(BattleEventHub.Subscribe<BattleEndedEventData>(BattleEventKind.BattleEnded,
            (_, __) => memory.Pending.Clear(), state.ApplyOrder));
    }

    private static bool TryGetReady(StatusInstance state, BattleDeclaration declaration,
        out Memory memory, out PendingAction pending)
    {
        memory = state?.ScriptRuntimeState as Memory; pending = null;
        return Active(state) && memory != null && declaration?.Kind == BattleEventKind.ActionCompleted
            && memory.Pending.TryGetValue(declaration.Header.ActionExecutionId, out pending) && pending.Ready;
    }

    private static async Task Heal(StatusHealExecution execution)
    {
        var declaration = ScriptHookContext.Current?.Declaration;
        if (!TryGetReady(execution.Status, declaration, out var memory, out var pending)) return;
        memory.Pending.Remove(declaration.Header.ActionExecutionId); // 先取走，嵌套声明不能重复领取。
        foreach (var record in pending.Records)
        {
            if (!Active(execution.Status)) return;
            if (record.Recipient?.IsAlive != true || record.Recipient.Side != execution.Status.Caster.Side) continue;
            await execution.ExecuteDefault(new List<BattleEntity> { record.Recipient },
                record.BaseAmount.ToString("R", CultureInfo.InvariantCulture), record.Cap);
        }
    }

    private static void Stop(StatusInstance state, object host)
    {
        (state.ScriptRuntimeState as Memory)?.Dispose();
        state.ScriptRuntimeState = null;
    }
}
