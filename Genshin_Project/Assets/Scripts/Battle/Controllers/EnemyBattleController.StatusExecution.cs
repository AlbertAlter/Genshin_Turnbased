using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

public partial class EnemyBattleController
{
    public void ApplyInitiateStatuses()
    {
        // Self绑定依赖实际槽位；未落位的InitEnemy不能抢先创建父状态，导致后续入场漏绑子状态。
        if (Entity == null || !Entity.IsAlive || Entity.Position?.Occupant != Entity
            || dm == null || !dm.EnemyMainDict.TryGetValue(EnemyID, out var main)) return;
        foreach (string id in main.InitiateStatuses)
        {
            if (Entity.HasStatus(id)) continue;
            if (!dm.StatusMainDict.TryGetValue(id, out var definition)) throw new InvalidOperationException("敌人初始化状态不存在：" + id);
            Entity.AddStatus(id, Entity, 0, 5, 0, definition);
        }
    }

    private EnemySkillMainData FindEnemySkill(string id)
    {
        foreach (var skill in dm.EnemySkillMainDict.Values)
            if (skill.EnemySkillID2 == id) return skill;
        throw new InvalidOperationException("敌人状态调用的技能不存在：" + id);
    }

    private StatusActionRuntimeState ActionRuntime(StatusInstance status, StatusActionData action)
    {
        if (status.ActionRuntime == null) status.ActionRuntime = new Dictionary<string, StatusActionRuntimeState>();
        string key = BattleSpecialRules.GetActionCounterKey(status, action,
            action.ActionKey ?? (action.StatusID2 + "|" + action.ActionType + "|" + action.Param1 + "|" + action.ScriptHook));
        if (!status.ActionRuntime.TryGetValue(key, out var runtime)) status.ActionRuntime[key] = runtime = new StatusActionRuntimeState();
        int turn = BattleManager.Instance != null ? BattleManager.Instance.TurnCount : 0;
        if (runtime.LastActionTurn != turn) { runtime.LastActionTurn = turn; runtime.TurnTriggerCount = 0; }
        return runtime;
    }

    public bool TryExecuteEventStatusAction(StatusInstance status, StatusActionData action, object host,
        ScriptHookContext context, Func<bool> isStillValid = null)
        => TryExecuteStatusAction(status, action, host, context, isStillValid, null);

    internal bool TryExecuteRemovedStatusAction(StatusRemovalActions.Request request, StatusActionData action)
        => TryExecuteStatusAction(request.Status, action, request.Host, request.Context, null, request);

    internal bool TryExecuteDeathStatusAction(StatusDeathActions.Request request, StatusActionData action)
        => TryExecuteStatusAction(request.Status, action, request.Host, request.Context, null, null, request);

    private bool TryExecuteStatusAction(StatusInstance status, StatusActionData action, object host,
        ScriptHookContext context, Func<bool> isStillValid, StatusRemovalActions.Request removal, StatusDeathActions.Request death = null)
    {
        if (status == null || action == null || (action.ActionType == "OnDestroy" && removal == null)
            || (action.ActionType == "OnDeath" && death == null)) return false;
        context = context?.Snapshot() ?? new ScriptHookContext();
        context.Caster = status.Caster; context.Status = status; context.StatusHost = host;
        context.Declaration = context.Declaration ?? BattleEventHub.Current;
        return BattleResolutionContext.Submit(status.ApplyOrder, () => {
            using var deathScope = death?.Enter();
            bool Valid() => (death != null ? death.CanExecute : removal != null ? removal.CanExecute
                : status.IsActive && !status.DeathCleanupPending && Entity != null
                // 敌人死亡只清理自身状态；留在其他存活单位上的治疗等状态仍由原来源执行。
                && (Entity.IsAlive || host is FieldPosition || host is BattleEntity recipient && recipient != Entity && recipient.IsAlive)
                && StatusHostAccess.Contains(host, status))
                && (isStillValid == null || isStillValid());
            if (!Valid()) return null;
            var runtime = ActionRuntime(status, action);
            int turn = BattleManager.Instance != null ? BattleManager.Instance.TurnCount : 0;
            int phase = BattleManager.Instance != null ? (int)BattleManager.Instance.CurrentPhase : 0;
            if ((action.MaxTimePerTurn > 0 && runtime.TurnTriggerCount + runtime.PendingTriggerCount >= action.MaxTimePerTurn)
                || (action.MaxTimePerLife > 0 && runtime.LifeTriggerCount + runtime.PendingTriggerCount >= action.MaxTimePerLife)
                || (action.Cooldown > 0 && (runtime.PendingTriggerCount > 0 || runtime.IsCoolingDown(action.Cooldown, turn, phase)))) return null;
            if (!ScriptHookEvaluator.Evaluate(action.ScriptHook, context)
                || !BattleResolutionContext.TryCollect(status, action, context)) return null;
            runtime.PendingTriggerCount++;
            bool pending = true;
            Action release = () => { if (pending) { pending = false; runtime.PendingTriggerCount--; } };
            return new BattleResolutionContext.PreparedTrigger(async () => {
                using var deathExecution = death?.Enter();
                if (!Valid()) return;
                using var trigger = BattleResolutionContext.EnterTrigger();
                using var execution = ScriptHookContext.EnterExecution(context);
                release();
                runtime.TurnTriggerCount++; runtime.LifeTriggerCount++;
                if (action.Cooldown > 0) { runtime.LastTriggerTurn = turn; runtime.LastTriggerPhase = phase; }
                await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Status, Entity,
                    action.Param1, statusID: status.StatusID2), () =>
                {
                    if (Valid()) BattleExecution.Enqueue(async () =>
                    {
                        using var dying = death?.Enter();
                        if (Valid()) await ExecuteEnemyStatusActionEffects(status, action);
                    });
                });
            }, release);
        });
    }

    public IEnumerator ExecuteStatusActionsAsync(StatusInstance status, List<StatusActionData> actions, object host, string actionType)
        => BattleExecution.Wait(BattleExecution.Run(async () => {
            if (actionType == "OnDestroy" || actionType == "OnDeath") return;
            using var naturalEnd = StatusRemovalActions.BeginNaturalEnd(status, actionType);
            using var statusAction = BattleResolutionContext.BeginIndependentStatusAction(status);
            foreach (var action in actions)
            {
                if (action.ActionType != actionType || ScriptHookEvaluator.IsEventDriven(action.ScriptHook)) continue;
                TryExecuteEventStatusAction(status, action, host, new ScriptHookContext { Caster = status.Caster, Target = host as BattleEntity });
                await BattleExecution.Flush();
            }
        }));

    public void OnStatusApplied(StatusInstance status, object host)
    {
        if (dm == null || !dm.StatusActionDict.TryGetValue(status.StatusID2, out var actions)) return;
        foreach (var action in actions)
            if (action.ActionType == "OnApply" && !ScriptHookEvaluator.IsEventDriven(action.ScriptHook))
                TryExecuteEventStatusAction(status, action, host, new ScriptHookContext { Caster = status.Caster, Target = host as BattleEntity });
    }

    private async Task ExecuteEnemyStatusActionEffects(StatusInstance status, StatusActionData action)
    {
        using var statusAction = BattleResolutionContext.BeginIndependentStatusAction(status);
        var previousTargets = _lastEnemyTargetResult;
        _lastEnemyTargetResult = null;
        try
        {
            int repeats = ExecuteCountEvaluator.Evaluate(action.ExecuteCount, action.ExecuteCountHandler, Entity);
            for (int repeat = 0; repeat < repeats; repeat++)
            foreach (string raw in (action.Param1 ?? string.Empty).Split(';'))
            {
                if (status.DeathCleanupPending && action.ActionType != "OnDestroy"
                    && !StatusDeathActions.Allows(status)) return;
                string id = raw.Trim();
                if (id.Length == 0) continue;
                if (id.StartsWith("SK_", StringComparison.Ordinal))
                {
                    var skill = FindEnemySkill(id);
                    await ExecuteEnemySkillAsync(skill, fromStatus: true);
                    continue;
                }
                if (!dm.StatusEffectDict.TryGetValue(id, out var effect)) throw new InvalidOperationException("敌人状态效果不存在：" + id);
                if (effect.EffectType != "ApplyStatus" && effect.EffectType != "RemoveStatus" && effect.EffectType != "SwitchPattern"
                    && effect.EffectType != "Damage" && effect.EffectType != "ApplyElement" && effect.EffectType != "Heal"
                    && effect.EffectType != "Cooldown" && effect.EffectType != "GrantAction" && effect.EffectType != "Count" && effect.EffectType != "Summon" && effect.EffectType != "ClearAura")
                    throw new NotSupportedException("敌人状态主动效果尚不支持：" + effect.EffectType);
                int number = 0;
                if (!string.IsNullOrWhiteSpace(effect.TargetSelect) && !int.TryParse(effect.TargetSelect, out number))
                    throw new NotSupportedException("敌人状态目标仅支持数量和Self：" + effect.TargetSelect);
                await ExecuteEnemyEffectAsync(new EnemySkillEffectData {
                    SkillEffectID = effect.StatusEffectID, SkillEffectID2 = effect.StatusEffectID2,
                    EffectType = effect.EffectType, Element = effect.Element, Duration = effect.Duration,
                    DamageType = effect.DamageType, DamageHook = effect.DamageHook,
                    AddInPhase = effect.AddInPhase, TriggerPhase = effect.TriggerPhase,
                    Param1 = effect.Param1, Param2 = effect.Param2, Param3 = effect.Param3,
                    TargetType = effect.TargetType, TargetNumber = number, TargetConsecutive = effect.TargetConsecutive,
                    TargetOverride = effect.TargetOverride, ScriptHook = effect.ScriptHook,
                    ExecuteCount = effect.ExecuteCount, ExecuteCountHandler = effect.ExecuteCountHandler
                }, status);
            }
        }
        finally { _lastEnemyTargetResult = previousTargets; }
    }
}
