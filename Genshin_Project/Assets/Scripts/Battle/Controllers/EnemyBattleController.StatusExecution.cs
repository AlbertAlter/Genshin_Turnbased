using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

public partial class EnemyBattleController
{
    private readonly HashSet<(StatusInstance, string)> _executingEnemyStatusActions
        = new HashSet<(StatusInstance, string)>();
    public void ApplyInitiateStatuses()
    {
        // Self绑定依赖实际槽位；未落位的InitEnemy不能抢先创建父状态，导致后续入场漏绑子状态。
        if (Entity == null || !Entity.IsAlive || Entity.Position?.Occupant != Entity
            || dm == null || !dm.EnemyMainDict.TryGetValue(EnemyID, out var main)) return;
        foreach (string id in main.InitiateStatuses) ApplyInitiateStatus(id);
        ApplyHealthPhaseStatuses(CurrentPhaseData);
    }

    private void ApplyHealthPhaseStatuses(EnemyMainData phaseData)
    {
        if (phaseData == null || phaseData.Phase <= 0 || _initializedHealthPhase == phaseData.Phase) return;
        _initializedHealthPhase = phaseData.Phase;
        foreach (string id in phaseData.PhaseInitiateStatuses)
        {
            var status = ApplyInitiateStatus(id);
            if (status?.IsActive == true) TrackPhaseStatus(status);
        }
    }

    private StatusInstance ApplyInitiateStatus(string id)
    {
        if (Entity.HasStatus(id)) return null;
        if (!dm.StatusMainDict.TryGetValue(id, out var definition)) throw new InvalidOperationException("敌人初始化状态不存在：" + id);
        return Entity.AddStatus(id, Entity, 0, 5, 0, definition);
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
        return runtime;
    }

    public bool TryExecuteEventStatusAction(StatusInstance status, StatusActionData action, object host,
        ScriptHookContext context, Func<bool> isStillValid = null, bool joinCurrentExecution = false)
        => TryExecuteStatusAction(status, action, host, context, isStillValid, null, joinCurrentExecution: joinCurrentExecution);

    internal bool TryExecuteRemovedStatusAction(StatusRemovalActions.Request request, StatusActionData action)
        => TryExecuteStatusAction(request.Status, action, request.Host, request.Context, null, request);

    internal bool TryExecuteDeathStatusAction(StatusDeathActions.Request request, StatusActionData action)
        => TryExecuteStatusAction(request.Status, action, request.Host, request.Context, null, null, request);

    private bool TryExecuteStatusAction(StatusInstance status, StatusActionData action, object host,
        ScriptHookContext context, Func<bool> isStillValid, StatusRemovalActions.Request removal, StatusDeathActions.Request death = null,
        bool joinCurrentExecution = false)
    {
        if (status == null || action == null || (action.ActionType == "OnDestroy" && removal == null)
            || (action.ActionType == "OnDeath" && death == null)) return false;
        context = context?.SnapshotForStatusAction(status, action) ?? new ScriptHookContext();
        context.Caster = status.Caster; context.Status = status; context.StatusHost = host;
        context.Declaration = context.Declaration ?? BattleEventHub.Current;
        if (!BattleResolutionContext.CanTriggerStatusAction(action, context)) return false;
        return BattleResolutionContext.Submit(status.ApplyOrder, () => {
            using var deathScope = death?.Enter();
            bool Valid() => (BattleManager.Instance?.IsBattleOver != true || context?.Declaration?.Kind == BattleEventKind.BattleEnded)
                && (death != null ? death.CanExecute : removal != null ? removal.CanExecute
                : status.IsActive && !status.DeathCleanupPending && Entity != null
                // 敌人死亡只清理自身状态；留在其他存活单位上的治疗等状态仍由原来源执行。
                && (Entity.IsAlive || host is FieldPosition || host is BattleEntity recipient && recipient != Entity && recipient.IsAlive)
                && StatusHostAccess.Contains(host, status))
                && (isStillValid == null || isStillValid());
            if (!Valid()) return null;
            var guardKey = (status, BattleResolutionContext.ActionKey(action));
            if (!BattleResolutionContext.HasApplicableLimit(action) && _executingEnemyStatusActions.Contains(guardKey)) return null;
            var runtime = ActionRuntime(status, action);
            if (!StatusActionDispatch.TryPrepare(status, action, context, runtime, continueStatusCall: joinCurrentExecution))
                return null;
            runtime.PendingTriggerCount++;
            bool pending = true;
            Action release = () => { if (pending) { pending = false; runtime.PendingTriggerCount--; } };
            return new BattleResolutionContext.PreparedTrigger(async () => {
                using var deathExecution = death?.Enter();
                if (!Valid()) return;
                using var trigger = BattleResolutionContext.EnterTrigger();
                using var execution = ScriptHookContext.EnterExecution(context);
                bool ownsGuard = _executingEnemyStatusActions.Add(guardKey);
                try
                {
                    await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Status, Entity,
                        action.Param1, statusID: status.StatusID2), () =>
                    {
                        if (!Valid() || (joinCurrentExecution
                            && !BattleResolutionContext.CommitContinuedStatusAction(status, action))) return;
                        release();
                        var battle = BattleManager.Instance;
                        runtime.RecordTrigger(action, battle != null ? battle.TurnCount : 0,
                            battle != null ? (int)battle.CurrentPhase : 0);
                        BattleExecution.Enqueue(async () =>
                        {
                            using var dying = death?.Enter();
                            if (Valid()) await ExecuteEnemyStatusActionEffects(status, action, joinCurrentExecution, context);
                        });
                    });
                }
                finally { if (ownsGuard) _executingEnemyStatusActions.Remove(guardKey); }
            }, release).WithStatus(joinCurrentExecution ? null : status,
                (object)removal ?? death ?? BattleResolutionContext.StatusActionOccurrence(context),
                () => { using var dying = death?.Enter(); return Valid(); });
        });
    }

    public void ExecuteStatusActions(StatusInstance status, List<StatusActionData> actions, object host, string actionType)
        => BattleExecution.Enqueue(() => ExecuteStatusActionsTask(status, actions, host, actionType));

    public IEnumerator ExecuteStatusActionsAsync(StatusInstance status, List<StatusActionData> actions, object host, string actionType)
        => BattleExecution.Wait(BattleExecution.Run(() => ExecuteStatusActionsTask(status, actions, host, actionType)));

    public Task ExecuteStatusActionsTask(StatusInstance status, List<StatusActionData> actions, object host, string actionType)
        => ExecuteStatusActionsTask(status, actions, host, actionType, null);

    internal async Task ExecuteStatusActionsTask(StatusInstance status, List<StatusActionData> actions, object host, string actionType,
        BattleStatusPromptOccurrence prompts)
    {
        if (actionType == "OnDestroy" || actionType == "OnDeath") return;
        if (status == null || actions == null || !actions.Exists(action => action.ActionType == actionType
            && !ScriptHookEvaluator.IsEventDriven(action.ScriptHook))) return;
        using var naturalEnd = StatusRemovalActions.BeginNaturalEnd(status, actionType);
        using var statusAction = BattleResolutionContext.BeginStatusExecution(status, startExecution: false);
        foreach (var action in actions)
        {
            if (action.ActionType != actionType || ScriptHookEvaluator.IsEventDriven(action.ScriptHook)) continue;
            TryExecuteEventStatusAction(status, action, host, new ScriptHookContext {
                Caster = status.Caster, Target = host as BattleEntity, ActionPrompts = prompts }, joinCurrentExecution: true);
            await BattleExecution.Flush();
        }
        statusAction.Complete();
    }

    public void OnStatusApplied(StatusInstance status, object host)
    {
        if (dm == null || !dm.StatusActionDict.TryGetValue(status.StatusID2, out var actions)) return;
        ExecuteStatusActions(status, actions, host, "OnApply");
    }

    private async Task ExecuteEnemyStatusActionEffects(StatusInstance status, StatusActionData action, bool joinCurrentExecution,
        ScriptHookContext context)
    {
        using var statusAction = BattleResolutionContext.BeginStatusExecution(status, joinCurrentExecution,
            joinDispatchedInvocation: !joinCurrentExecution);
        var previousTargets = _lastEnemyTargetResult;
        _lastEnemyTargetResult = null;
        try
        {
            int repeats = ExecuteCountEvaluator.Evaluate(action.ExecuteCount, action.ExecuteCountHandler, Entity);
            if (repeats > 0 && !string.IsNullOrWhiteSpace(action.Param1)) BattleExecution.RecordStatusPrompt(status, action.ActionType, context);
            for (int repeat = 0; repeat < repeats; repeat++)
            foreach (string raw in (action.Param1 ?? string.Empty).Split(';'))
            {
                if (BattleExecution.GameplayEffectsStopped) return;
                if (status.DeathCleanupPending && action.ActionType != "OnDestroy"
                    && !StatusDeathActions.Allows(status)) return;
                if (status.Host is BattleEntity withdrawnHost && withdrawnHost.HasWithdrawn && action.ActionType != "OnDestroy") return;
                string id = raw.Trim();
                if (id.Length == 0) continue;
                BattleResolutionContext.ContinueStatusExecution(status);
                await BattleResolutionContext.FlushExecutionStart();
                if (status.EnemyHealthPhase > 0 && status.EnemyHealthPhase != HealthPhases.Current
                    && action.ActionType != "OnDestroy") return;
                if (id.StartsWith("SK_", StringComparison.Ordinal))
                {
                    var skill = FindEnemySkill(id);
                    await ExecuteEnemySkillAsync(skill, fromStatus: true);
                    continue;
                }
                if (!dm.StatusEffectDict.TryGetValue(id, out var effect)) throw new InvalidOperationException("敌人状态效果不存在：" + id);
                if (effect.EffectType != "ApplyStatus" && effect.EffectType != "RemoveStatus" && effect.EffectType != "SwitchPattern"
                    && effect.EffectType != "Damage" && effect.EffectType != "ApplyElement" && effect.EffectType != "Heal"
                    && effect.EffectType != "Cooldown" && effect.EffectType != "DrainEnergy" && effect.EffectType != "GrantAction" && effect.EffectType != "Count" && effect.EffectType != "Summon" && effect.EffectType != "ClearAura" && effect.EffectType != "Withdrawn")
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
            await BattleExecution.Flush();
            statusAction.Complete();
        }
        finally { _lastEnemyTargetResult = previousTargets; }
    }
}
