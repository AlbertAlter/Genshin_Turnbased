using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class CharacterBattleController
{
    // ================================================================
    //  状态行动执行入口（BattleManager 在 OnTrigger/OnEnd 时调用）
    //  把状态行动的参数（效果ID列表）转成效果执行
    // ================================================================
    /// <summary>
    /// 状态行动触发限制（2026-08-14，Status_Action 新列）：
    /// MaxTimePerTurn（每回合最大次数）/ MaxTimePerLife（全场最大次数）/ Cooldown（从触发阶段起算的冷却回合）。
    /// 返回 true = 已达上限/冷却中，不应触发。
    /// </summary>
    StatusActionRuntimeState GetStatusActionRuntime(StatusInstance inst, StatusActionData act)
    {
        if (inst == null || act == null) return null;
        if (inst.ActionRuntime == null)
            inst.ActionRuntime = new Dictionary<string, StatusActionRuntimeState>();
        string key = !string.IsNullOrEmpty(act.ActionKey)
            ? act.ActionKey
            : $"{act.StatusID2}|{act.ActionType}|{act.Param1}|{act.ScriptHook}";
        key = BattleSpecialRules.GetActionCounterKey(inst, act, key);
        if (WeaponCompletionHooks.TryGetSharedRuntime(inst, act, key, out var shared))
        {
            inst.ActionRuntime[key] = shared;
            return shared;
        }
        if (!inst.ActionRuntime.TryGetValue(key, out StatusActionRuntimeState state))
        {
            state = new StatusActionRuntimeState();
            inst.ActionRuntime[key] = state;
        }
        return state;
    }

    bool StatusActionLimitReached(StatusInstance inst, StatusActionData act)
    {
        if (inst == null || act == null) return false;
        if (act.MaxTimePerTurn <= 0 && act.MaxTimePerLife <= 0 && act.Cooldown <= 0) return false;
        StatusActionRuntimeState state = GetStatusActionRuntime(inst, act);
        if (state == null) return false;
        var bm = BattleManager.Instance;
        int turn = bm != null ? bm.TurnCount : 0;
        int phase = bm != null ? (int)bm.CurrentPhase : 0;
        // 懒重置：新回合时清零本回合计数
        if (state.LastActionTurn != turn)
        {
            state.LastActionTurn = turn;
            state.TurnTriggerCount = 0;
        }
        if (act.MaxTimePerTurn > 0 && state.TurnTriggerCount + state.PendingTriggerCount >= act.MaxTimePerTurn)
            return true;
        if (act.MaxTimePerLife > 0 && state.LifeTriggerCount + state.PendingTriggerCount >= act.MaxTimePerLife)
            return true;
        if (act.Cooldown > 0 && (state.PendingTriggerCount > 0
            || state.IsCoolingDown(act.Cooldown, turn, phase)))
            return true;
        return false;
    }

    /// <summary>状态行动触发后计数（MaxTimePerTurn/MaxTimePerLife/Cooldown）。</summary>
    void CountStatusActionTrigger(StatusInstance inst, StatusActionData act)
    {
        if (inst == null || act == null) return;
        StatusActionRuntimeState state = GetStatusActionRuntime(inst, act);
        if (state == null) return;
        var bm = BattleManager.Instance;
        int turn = bm != null ? bm.TurnCount : 0;
        int phase = bm != null ? (int)bm.CurrentPhase : 0;
        if (state.LastActionTurn != turn)
        {
            state.LastActionTurn = turn;
            state.TurnTriggerCount = 0;
        }
        // 两个字段记录实际触发次数；MaxTimePerTurn / MaxTimePerLife 只决定
        // 是否设上限，不能决定是否记账。无上限或仅配置 Cooldown 的行动
        // 在真正执行后同样必须留下计数。
        state.TurnTriggerCount++;
        state.LifeTriggerCount++;
        if (act.Cooldown > 0)
        {
            state.LastTriggerTurn = turn;
            state.LastTriggerPhase = phase;
        }

        // ActionRuntime 负责逐行动限制；StatusInstance 上的旧字段仍是状态级公开汇总，
        // 供界面、调试与既有调用方读取，不能在迁移后停止记账。
        if (inst.LastActionTurn != turn)
        {
            inst.LastActionTurn = turn;
            inst.TurnTriggerCount = 0;
        }
        inst.TurnTriggerCount++;
        inst.LifeTriggerCount++;
        if (act.Cooldown > 0)
        {
            inst.LastTriggerTurn = turn;
            inst.LastTriggerPhase = phase;
        }
    }

    public void ExecuteStatusActions(StatusInstance inst, List<StatusActionData> actions, object host, string actionType)
        => BattleExecution.Enqueue(() => ExecuteStatusActionsTask(inst, actions, host, actionType));

    public async Task ExecuteStatusActionsTask(StatusInstance inst, List<StatusActionData> actions, object host, string actionType)
    {
        if (actionType == "OnDestroy" || actionType == "OnDeath") return; // 仅接受对应真实事件。
        using var naturalEnd = StatusRemovalActions.BeginNaturalEnd(inst, actionType);
        using var statusAction = BattleResolutionContext.BeginIndependentStatusAction(inst);
        await BattleDeathResolution.RunAsync(() => ExecuteStatusActionsCoreAsync(inst, actions, host, actionType));
    }

    private async Task ExecuteStatusActionsCoreAsync(StatusInstance inst, List<StatusActionData> actions, object host, string actionType)
    {
        if (actionType == "OnEnd" && inst?.StatusID2 == KingsSquireWeaponCount.ID)
        {
            await KingsSquireWeaponCount.EndNaturally(inst, host, actions);
            return;
        }
        bool countTurnHandled = false;
        foreach (var act in actions)
        {
            if (inst == null || !inst.IsActive || inst.DeathCleanupPending || !EquipmentStatusLifetime.CanExecute(inst)) break;
            if (act.ActionType != actionType) continue;
            // 事件驱动钩子行（ScriptHook=PreAlliesDamage / Kill(...)）：不参与普通触发（阶段/回合等），由各自钩子机制单独触发（2026-08-19）
            if (ScriptHookEvaluator.IsEventDriven(act.ScriptHook)) continue;
            // ScriptHook：只有该特殊处理器返回真值时才触发该行行动（统一求值入口 ScriptHookEvaluator）
            var actionContext = ScriptHookContext.Current?.Snapshot() ?? new ScriptHookContext();
            actionContext.Caster = inst.Caster;
            actionContext.Status = inst;
            actionContext.StatusHost = host;
            actionContext.Target = host as BattleEntity;
            if (!ScriptHookEvaluator.Evaluate(act.ScriptHook, actionContext)) continue;
            // 触发限制：每回合/全场次数上限、行动冷却（2026-08-14）
            if (StatusActionLimitReached(inst, act)) continue;
            if (!BattleResolutionContext.TryCollect(inst, act, actionContext)) continue;
            await BattleExecution.Play(new BattlePresentationRequest(
                BattlePresentationKind.Status, inst?.Caster, act.Param1, statusID: inst?.StatusID2), () =>
            {
                if (!inst.IsActive || inst.DeathCleanupPending || !EquipmentStatusLifetime.CanExecute(inst)) return;
                // Count 在自己的首条有效 OnItsTurn 行动处结算一次，不增加阶段。
                if (actionType == "OnItsTurn" && !countTurnHandled)
                {
                    countTurnHandled = true;
                    CountStatusSystem.OnItsTurn(inst, host);
                }
                CountStatusActionTrigger(inst, act);
                ExecuteStatusActionEffects(inst, act, host, actionContext);
            });
        }
    }

    /// <summary>
    /// 阶段协程与直接入口共用同一状态执行流程。默认播放器立即完成；
    /// 仅在真实表现尚未完成时等待，不再由 Display 字段决定固定延迟。
    /// </summary>
    public System.Collections.IEnumerator ExecuteStatusActionsAsync(StatusInstance inst, List<StatusActionData> actions, object host, string actionType)
        => BattleExecution.Wait(BattleExecution.Run(() => ExecuteStatusActionsTask(inst, actions, host, actionType)));

    /// <summary>仅接受3095处理器已一次性确认的终结请求，不把失效状态重新激活。</summary>
    internal async Task ExecuteCommittedWeaponEnd(KingsSquireWeaponCount.Terminal terminal)
    {
        if (!terminal.CanExecute) return;
        using var trigger = BattleResolutionContext.EnterTrigger();
        using var context = ScriptHookContext.EnterExecution(terminal.Context);
        foreach (var action in terminal.Actions)
        {
            if (!terminal.CanExecute) return;
            if (ScriptHookEvaluator.IsEventDriven(action.ScriptHook)
                || !ScriptHookEvaluator.Evaluate(action.ScriptHook, terminal.Context)
                || StatusActionLimitReached(terminal.Status, action)
                || !BattleResolutionContext.TryCollect(terminal.Status, action, terminal.Context)) continue;
            await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Status,
                terminal.Owner, action.Param1, statusID: terminal.Status.StatusID2), () =>
            {
                if (!terminal.CanExecute) return;
                CountStatusActionTrigger(terminal.Status, action);
                ExecuteStatusActionEffects(terminal.Status, action, terminal.Owner, terminal.Context);
            });
        }
    }

    /// <summary>
    /// 预解析状态行动的效果目标并快照（结果先记着）：
    /// 遍历行动的效果（Param1 分号分隔），对 Damage 类效果调用 ResolveTargets（此刻目标/标记还在），
    /// 结果存入快照表；延迟结束后 ExecuteDamage 直接用快照，不重新解析。
    /// </summary>
    void PreResolveStatusActionTargets(StatusActionData act, object host)
    {
        if (string.IsNullOrEmpty(act.Param1)) return;
        var dm = DataManager.Instance;
        if (dm == null) return;
        foreach (var tid in act.Param1.Split(';'))
        {
            var t = tid.Trim();
            if (t.StartsWith("SK_"))
            {
                if (!_skillEffects.TryGetValue(t, out var effects)) continue;
                foreach (var eff in effects)
                    PreResolveStatusEffectTarget(eff);
            }
            else
            {
                if (dm.SkillEffectDict.TryGetValue(t, out var eff))
                    PreResolveStatusEffectTarget(eff);
            }
        }
    }

    void PreResolveStatusEffectTarget(SkillEffectData eff)
    {
        if (eff == null || string.IsNullOrEmpty(eff.SkillEffectID2) || eff.EffectType != "Damage") return;
        var targets = ResolveTargets(eff);
        _statusTargetSnapshot[eff.SkillEffectID2] = new List<BattleEntity>(targets);
    }

    /// <summary>有快照用快照（用后即清），否则正常解析。延迟执行的效果用触发时的目标，不受延迟期间状态变化影响。</summary>
    List<BattleEntity> TakeStatusTargetSnapshot(SkillEffectData eff)
    {
        if (eff != null && !string.IsNullOrEmpty(eff.SkillEffectID2) && _statusTargetSnapshot.TryGetValue(eff.SkillEffectID2, out var snap))
        {
            _statusTargetSnapshot.Remove(eff.SkillEffectID2);
            return new List<BattleEntity>(snap);
        }
        return ResolveTargets(eff);
    }

    void ExecuteStatusActionEffects(StatusInstance sourceStatus, StatusActionData act, object host,
        ScriptHookContext context = null)
        => BattleExecution.Enqueue(() => ExecuteStatusActionEffectsAsync(sourceStatus, act, host, context));

    async Task ExecuteStatusActionEffectsAsync(StatusInstance sourceStatus, StatusActionData act, object host,
        ScriptHookContext context)
    {
        using var statusAction = BattleResolutionContext.BeginIndependentStatusAction(sourceStatus);
        await BattleDeathResolution.RunAsync(() => ExecuteStatusActionEffectsCoreAsync(sourceStatus, act, host, context));
    }

    async Task ExecuteStatusActionEffectsCoreAsync(StatusInstance sourceStatus, StatusActionData act, object host,
        ScriptHookContext context)
    {
        if (sourceStatus?.DeathCleanupPending == true && act.ActionType != "OnDestroy"
            && !StatusDeathActions.Allows(sourceStatus)) return;
        if (!EquipmentStatusLifetime.CanExecute(sourceStatus)) return;
        using var triggerContext = ScriptHookContext.EnterExecution(context);
        using var actionGroup = BattleSpecialRules.BeginStatusAction(sourceStatus);
        if (string.IsNullOrEmpty(act.Param1)) return;
        int count = ExecuteCountEvaluator.Evaluate(act.ExecuteCount, act.ExecuteCountHandler,
            sourceStatus?.Caster ?? Entity);
        for (int repeat = 0; repeat < count; repeat++)
            await ExecuteStatusActionEffectsOnceAsync(sourceStatus, act, host);
    }

    async Task ExecuteStatusActionEffectsOnceAsync(StatusInstance sourceStatus, StatusActionData act, object host)
    {
        var dm = DataManager.Instance;
        BeginStatusTargetSequence();

        // Param1 是效果ID2列表（分号分隔），如 "STE_Bunny2;STE_Bunny4"
        // 特殊：以 SK_ 开头的是技能ID（如蓄力箭 SK_HeavyAttack_Amber），执行技能效果序列
        string[] ids = act.Param1.Split(';');
        foreach (var id in ids)
        {
            if (sourceStatus?.DeathCleanupPending == true && act.ActionType != "OnDestroy"
                && !StatusDeathActions.Allows(sourceStatus)) return;
            if (!EquipmentStatusLifetime.CanExecute(sourceStatus)) return;
            string tid = id.Trim();
            if (string.IsNullOrEmpty(tid)) continue;

            if (tid.StartsWith("SK_"))
            {
                // Skill 容器调用：完整执行其资源/冷却/次数/效果规则；是否属于主动行为只看被调用 Skill 的 ActionType。
                if (_skillById2.ContainsKey(tid))
                {
                    await ExecuteSkillFromStatusAsync(tid, GetSkillTypeForExecution(tid, 1), sourceStatus);
                    LogManager.Log(LogCategory.StatusAction, $"ExecuteSkill {tid} via status");
                }
                else
                {
                    LogManager.LogWarning(LogCategory.StatusAction, $"技能不存在: {tid}");
                }
                continue;
            }

            if (!dm.StatusEffectDict.TryGetValue(tid, out var statusEff))
            {
                // SE_ 开头 = 技能效果（如凯亚冰棱 SE_Kaeya_BurstTrigger），2026-08-14 新增支持
                if (dm.SkillEffectDict.TryGetValue(tid, out var skillEff))
                {
                    await ExecuteEffectFromStatusAsync(skillEff, GetEffectSkillType(tid, 1), sourceStatus);
                }
                else
                {
                    LogManager.LogWarning(LogCategory.StatusAction, $"状态效果不存在: {tid}");
                }
                continue;
            }
            await ExecuteStatusEffectAsync(statusEff, sourceStatus, host);
        }
    }

    async Task ExecuteSkillFromStatusAsync(string skillID2, int skillType, StatusInstance sourceStatus)
    {
        StatusInstance previous = _currentEffectSourceStatus;
        _currentEffectSourceStatus = sourceStatus;
        try
        {
            await ExecuteSkillByIDAsync(skillID2, skillType, false);
        }
        finally
        {
            _currentEffectSourceStatus = previous;
        }
    }

    void ExecuteEffectFromStatus(SkillEffectData effect, int skillType, StatusInstance sourceStatus)
        => BattleExecution.Enqueue(() => ExecuteEffectFromStatusAsync(effect, skillType, sourceStatus));

    async Task ExecuteEffectFromStatusAsync(SkillEffectData effect, int skillType, StatusInstance sourceStatus)
    {
        using var snapshotIsolation = StatusFormulaSnapshot.Suspend();
        StatusInstance previous = _currentEffectSourceStatus;
        _currentEffectSourceStatus = sourceStatus;
        try
        {
            await ExecuteEffectAsync(effect, skillType);
        }
        finally
        {
            _currentEffectSourceStatus = previous;
        }
    }

    Task ExecuteStatusEffectAsync(StatusEffectData statusEff, StatusInstance sourceStatus, object host)
    {
        int count = ExecuteCountEvaluator.Evaluate(statusEff.ExecuteCount,
            statusEff.ExecuteCountHandler, sourceStatus?.Caster ?? Entity);
        bool handlesHook = (statusEff.EffectType == "Damage"
            && (BattleSpecialRules.HasStatusDamageHandler(statusEff.DamageHook) || BattleSpecialRules.HasFixedStatusAmount(statusEff.DamageHook)))
            || (statusEff.EffectType == "Heal" && BattleSpecialRules.HasStatusHealHandler(statusEff.DamageHook))
            || WeaponHooks.HandlesEffectHook(statusEff);
        string repeatHook = handlesHook ? null : statusEff.DamageHook;
        return count == 0 ? Task.CompletedTask : BattleExecution.RepeatEffect(count, repeatHook,
            executionID => ExecuteStatusEffectOnceAsync(statusEff, sourceStatus, host, executionID),
            sourceStatus?.Caster ?? Entity, statusEff.StatusEffectID2);
    }

    async Task ExecuteStatusEffectOnceAsync(StatusEffectData statusEff, StatusInstance sourceStatus, object host, long executionID)
    {
        if (!EquipmentStatusLifetime.CanExecute(sourceStatus)) return;
        if (!WeaponHooks.CanExecuteWindowEffect(statusEff, sourceStatus)) return;
        using var resolutionEffect = BattleResolutionContext.BeginEffect();
        BattleExecution.ThrowIfCancelled();
        var hookContext = ScriptHookContext.Current?.Snapshot() ?? new ScriptHookContext();
        hookContext.Caster = sourceStatus?.Caster ?? Entity;
        hookContext.Status = sourceStatus;
        hookContext.StatusHost = host;
        hookContext.Target = hookContext.Target ?? host as BattleEntity;
        hookContext.HitEffectID = hookContext.HitEffectID ?? LastHitEffectID2;
        hookContext.ExecutingStatusEffectID = statusEff.StatusEffectID;
        if (!ScriptHookEvaluator.Evaluate(statusEff.ScriptHook, hookContext)) return;
        using var effectContext = ScriptHookContext.EnterExecution(hookContext);
        using var formulaSnapshot = StatusFormulaSnapshot.Enter(sourceStatus, statusEff.StatusEffectID2);
        // 父声明只提供因果；进入子效果后恢复实际执行身份，不能把追加伤害记成源攻击。
        using var actualExecution = BattleEventHub.BeginEffectExecution(executionID);
        if (statusEff.EffectType == "Damage") await ExecuteStatusDamageAsync(statusEff, sourceStatus, host, executionID);
        else if (statusEff.EffectType == "Heal") await ExecuteStatusHealAsync(statusEff, sourceStatus, host);
        else await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Effect,
            sourceStatus?.Caster ?? Entity, statusEff.StatusEffectID2, statusID: sourceStatus?.StatusID2),
            () => { if (EquipmentStatusLifetime.CanExecute(sourceStatus)) ExecuteStatusEffectImmediate(statusEff, sourceStatus, host); });
        await BattleExecution.Flush();
    }

    async Task ExecuteStatusDamageAsync(StatusEffectData statusEff, StatusInstance sourceStatus, object host, long effectExecutionID)
    {
        if (DamageProperties.Parse(statusEff.Element, statusEff.Param3).IsResonance
            && await GeoConstructStatusSystem.TryExecuteAsync(sourceStatus, sourceStatus?.Caster ?? Entity,
                async targets =>
                {
                    long nodeExecution = BattleEventHub.NextIdentity();
                    using var execution = BattleEventHub.BeginEffectExecution(nodeExecution);
                    using var nodeCompletion = BattleSpecialRules.BeginDamageEffect(sourceStatus?.Caster ?? Entity,
                        statusEff.StatusEffectID2, nodeExecution);
                    await ExecuteStatusDamageCoreAsync(statusEff, sourceStatus, host, nodeExecution, targets);
                })) return;
        using var damageQuery = DamageQueryContext.Enter(statusEff.StatusEffectID2, statusEff.DamageType);
        using var completion = BattleSpecialRules.BeginDamageEffect(sourceStatus?.Caster ?? Entity, statusEff.StatusEffectID2, effectExecutionID);
        DamageProperties.Parse(statusEff.Element, statusEff.Param3).RequireImplementedFormula();
        if (DamageProperties.IsFixed(statusEff.Element))
        {
            TableValidator.ValidateDamageProperties("Status_Effect", 0, "Damage", statusEff.Element,
                statusEff.Param1, statusEff.Param2, statusEff.Param3, statusEff.DamageHook);
            await ExecuteTrueStatusDamageAsync(statusEff, sourceStatus, host);
            return;
        }
        if (BattleSpecialRules.HasStatusDamageHandler(statusEff.DamageHook))
        {
            var execution = new StatusDamageExecution
            {
                Effect = statusEff, Status = sourceStatus, Host = host,
                Targets = ResolveStatusTargets(statusEff, sourceStatus, host), ExecutionID = effectExecutionID
            };
            if (execution.Targets == null) return;
            // 特殊处理器回到普通结算时复用已经解析的目标，不再次随机选敌。
            // 弹射处理器可在下一跳前明确更新自己的Targets，仍走同一效果执行编号。
            execution.ExecuteDefault = prepared => ExecuteStatusDamageCoreAsync(prepared, sourceStatus, host, effectExecutionID, execution.Targets);
            await BattleSpecialRules.ExecuteStatusDamage(execution);
            return;
        }
        await ExecuteStatusDamageCoreAsync(statusEff, sourceStatus, host, effectExecutionID);
    }

    async Task ExecuteStatusDamageCoreAsync(StatusEffectData authoredEffect, StatusInstance sourceStatus, object host, long effectExecutionID,
        List<BattleEntity> preparedTargets = null)
    {
        var statusEff = authoredEffect;
        // 特殊状态可传入运行时改写后的效果，不能沿用外层占位定义。
        using var damageQuery = DamageQueryContext.Enter(statusEff.StatusEffectID2, statusEff.DamageType);
        // 状态伤害（兔兔伯爵爆炸 STE_Bunny2 等）：从 SkillType=2 查等级，多段 Hits1-7 逐段结算
        var hits = GetHitDataFromStatusEffectList(statusEff);
        if (hits.Count == 0 || (hits.Count == 1 && hits[0].Multiplier <= 0)) return;
        using var weaponCompletion = EndOfLineWeaponCount.BeginDamage(statusEff, sourceStatus, host, effectExecutionID);
        int stSkillType = GetEffectSkillType(statusEff.StatusEffectID2, 2);
        string resolvedParam = WeaponExpressionEvaluator.Expand(statusEff.Param1, sourceStatus);
        string[] directHits = statusEff.Param1 != null && statusEff.Param1.Contains("Index(")
            ? null : GetDirectHitExpressions(statusEff.Param1);
        var targets = preparedTargets ?? ResolveStatusTargets(statusEff, sourceStatus, host);
        if (targets == null) return;

        for (int hitIndex = 0; hitIndex < hits.Count; hitIndex++)
        {
            var enchant = EnchantStatusSystem.Resolve(sourceStatus?.Caster ?? Entity, authoredEffect.DamageType, authoredEffect.Element);
            statusEff = enchant.HasValue ? authoredEffect.WithDamageElement(enchant.Value.Element) : authoredEffect;
            if (enchant.HasValue) hits[hitIndex] = new HitData(hits[hitIndex].Multiplier, enchant.Value.ElementAmount);
            float baseValue = directHits != null || StatusFormulaSnapshot.For(Entity, statusEff.StatusEffectID2) != null
                ? 0f : ResolveBaseValue(resolvedParam, statusEff.StatusEffectID2, stSkillType, statusEff.Element);
            if (statusEff.Element == "Geo" && targets.Count > 1)
                targets = targets.OrderBy(target => target != null ? target.SlotPosition : int.MaxValue).ToList();
            if (statusEff.Element == "Anemo")
            {
                await ExecuteAnemoStatusDamageAsync(statusEff, sourceStatus, new List<HitData> { hits[hitIndex] }, stSkillType,
                    baseValue, targets, effectExecutionID, directHits == null ? null : new[] { directHits[hitIndex] }, hitIndex);
                continue;
            }
            await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Hit, sourceStatus?.Caster ?? Entity, statusEff.StatusEffectID2, hitIndex + 1, targets), () =>
            {
                if (!EquipmentStatusLifetime.CanExecute(sourceStatus)) return;
                using var hitResponses = BattleResolutionContext.BeginCollection();
                using var countDamage = new CountDamageBatch();
                HitData hit = hits[hitIndex];
                var hitTargets = DamageHitTargets.Expand(targets, statusEff.Param2, statusEff.Element,
                    statusEff.TargetType == "Self" ? sourceStatus?.Host as BattleEntity : null);
                long logicalHitID = BattleEventHub.NextIdentity();
                using var hitSnapshot = BattleSpecialRules.BeginHit(logicalHitID, sourceStatus?.Caster ?? Entity,
                    hitTargets.Select(entry => entry.Target));
                var reactions = new List<ReactionResult>();
                var resolutions = new List<HitResolution>();
                using (var batch = new HitBatch(logicalHitID))
                {
                    for (int targetIndex = 0; targetIndex < hitTargets.Count; targetIndex++)
                    {
                        var entry = hitTargets[targetIndex];
                        BattleEntity target = entry.Target;
                        using var snapshotTarget = StatusFormulaSnapshot.EnterTarget(target);
                        HitData targetHit = entry.Scale(BattleSpecialRules.ModifyStatusHit(statusEff.DamageHook,
                            sourceStatus?.Caster ?? Entity, target, hit, GetEffectiveSkillLevel(stSkillType)));
                        int resolvedTargetIndex = batch.Results.Count;
                        float hitBaseValue = directHits != null
                            ? ResolveDirectHitBase(directHits[hitIndex], statusEff.StatusEffectID2, stSkillType, statusEff.Element)
                            : StatusFormulaSnapshot.For(Entity, statusEff.StatusEffectID2) != null
                                ? ResolveBaseValue(resolvedParam, statusEff.StatusEffectID2, stSkillType, statusEff.Element) : baseValue;
                        hitBaseValue = SnowTombedWeaponHooks.ResolveBase(statusEff, sourceStatus, target, stSkillType, hitBaseValue);
                        CountDamagePlan countPlan = countDamage.Prepare(new CountDamageContext
                        {
                            Attacker = sourceStatus?.Caster ?? Entity, Target = target, SourceKind = ReactionSourceKind.StatusEffect,
                            EffectID = statusEff.StatusEffectID2, SkillID = string.Empty, DamageType = statusEff.DamageType,
                            Element = statusEff.Element, SkillType = stSkillType, EffectExecutionID = effectExecutionID,
                            HitIndex = hitIndex + 1, TargetIndex = resolvedTargetIndex + 1
                        });
                        float damage = CalculateStatusDamage(
                            hitBaseValue,
                            targetHit,
                            statusEff,
                            target,
                            stSkillType,
                            out ReactionDamageComponents damageComponents,
                            countPlan.BaseDamageBonus);
                        DMGTakenSnapshot taken = target.PrepareDMGTaken(
                            statusEff.StatusEffectID2, string.Empty, statusEff.DamageType, statusEff.Element);
                        ReactionResult reaction = ReactionResolver.Resolve(new ReactionContext
                        {
                            SourceEntity = sourceStatus != null && sourceStatus.Caster != null
                                ? sourceStatus.Caster
                                : Entity,
                            EquipmentSource = sourceStatus?.EquipmentSource,
                            Target = target,
                            SourceKind = ReactionSourceKind.StatusEffect,
                            SourceEffectID = statusEff.StatusEffectID2,
                            EffectExecutionID = effectExecutionID,
                            LogicalHitID = batch.RuntimeHitID,
                            TargetIndex = resolvedTargetIndex + 1,
                            AttackElement = statusEff.Element,
                            AttackAmount = targetHit.ElementAura,
                            PreReactionDamage = damage,
                            DamageComponents = damageComponents,
                            DamageType = statusEff.DamageType,
                        });
                        StatusOnHitHookSystem.NotifyReactions(reaction.TriggeredReactions);
                        if (reaction.HasReaction)
                            LogManager.Log(LogCategory.StatusDamage,
                                $"{statusEff.StatusEffectID2} -> {target.EntityID} 触发{reaction.DisplayNameSummary}");
                        CriticalHitResult critical = ResolveFinalCriticalDamage(
                            reaction.FinalDamage,
                            target,
                            statusEff.StatusEffectID2,
                            string.Empty,
                            stSkillType,
                            statusEff.Element,
                            hitIndex + 1,
                            entry.IsSplash ? "状态溅射" : "状态直伤", WeaponHooks.ForceCritical(statusEff), reaction.PrimaryDamageReactionType);
                        reactions.Add(reaction);
                        resolutions.Add(batch.Resolve(new HitRequest
                        {
                            Properties = DamageProperties.Parse(statusEff.Element, statusEff.Param3),
                            Target = target,
                            DamageBeforeShield = critical.DamageAfterCritical,
                            Critical = critical,
                            Element = statusEff.Element,
                            ElementAmount = targetHit.ElementAura,
                            DamageType = statusEff.DamageType,
                            EnemyShieldHit = reaction.EnemyShieldHit,
                            DMGTaken = taken,
                            Source = CreateStatusDamageSource(
                                sourceStatus,
                                statusEff.StatusEffectID2,
                                reaction.PrimaryDamageReactionType),
                            HitIndex = hitIndex,
                            TargetIndex = resolvedTargetIndex,
                            CountPlan = countPlan
                        }));
                    }
                    batch.Flush();
                }
                ReactionResult lunar = ReactionEffectExecutor.PrepareLunarBatch(reactions);
                for (int index = 0; index < reactions.Count; index++)
                {
                    HitResolution resolution = resolutions[index];
                    float actualHPDamage = resolution?.DamageEvent != null
                        ? resolution.DamageEvent.ActualHPDamage
                        : 0f;
                    ReactionEffectExecutor.FinalizePrimaryHit(reactions[index], actualHPDamage);
                    ReactionEffectExecutor.ExecuteDerivedHits(reactions[index]);
                    LogManager.Log(LogCategory.StatusDamage,
                        $"hit#{hitIndex + 1} {statusEff.StatusEffectID2} -> " +
                        $"{resolution.Request.Target.EntityID} : {resolution.DamageAfterShield:F1}");
                }
                ReactionEffectExecutor.ExecuteDerivedHits(lunar);
            });
        }

    }

    async Task ExecuteStatusHealAsync(StatusEffectData statusEff, StatusInstance sourceStatus, object host)
    {
        if (BattleSpecialRules.HasStatusHealHandler(statusEff.DamageHook))
        {
            await BattleSpecialRules.ExecuteStatusHeal(new StatusHealExecution
            {
                Effect = statusEff, Status = sourceStatus, Host = host,
                ExecuteDefault = (targets, formula, cap) => ExecuteHealingSegmentsAsync(targets,
                    statusEff.StatusEffectID2, formula, GetEffectSkillType(statusEff.StatusEffectID2, 2),
                    statusEff.Element, sourceStatus?.Caster ?? Entity, sourceStatus, cap)
            });
            return;
        }
        // 治疗（2026-08-14）：Param1=治疗量公式，目标=TargetType解析
        var healTargets = ResolveStatusTargets(statusEff, sourceStatus, host);
        if (healTargets == null) return;
        await ExecuteHealingSegmentsAsync(healTargets, statusEff.StatusEffectID2, WeaponHooks.HealingFormula(statusEff, sourceStatus),
            GetEffectSkillType(statusEff.StatusEffectID2, 2), statusEff.Element,
            sourceStatus?.Caster ?? Entity, sourceStatus);

    }

    void ExecuteStatusEffectImmediate(StatusEffectData statusEff, StatusInstance sourceStatus, object host)
    {
        using var resolutionEffect = BattleResolutionContext.BeginEffect();
        switch (statusEff.EffectType)
        {
            case "SwitchIn":
                SwitchInEffect.Execute(ResolveStatusTargets(statusEff, sourceStatus, host));
                break;
            case "ResetCooldown":
                ExecuteResetCooldown(statusEff.Param1, statusEff.StatusEffectID2);
                break;
            case "Cooldown":
                ExecuteCooldown(statusEff.Param1, statusEff.Param2, statusEff.StatusEffectID2);
                break;
            case "BondOfLife":
                BondOfLifeEffect.Execute(statusEff.Param1, sourceStatus?.Caster ?? Entity,
                    ResolveStatusTargets(statusEff, sourceStatus, host), sourceStatus,
                    statusEff.StatusEffectID2, GetEffectSkillType(statusEff.StatusEffectID2, 2), statusEff.Element);
                break;
            case "NightSoul":
                NightSoulEffect.Execute(statusEff.Param1,
                    ResolveStatusTargets(statusEff, sourceStatus, host), statusEff.StatusEffectID2);
                break;
            case "Count":
                CountEffect.Execute(statusEff.Param1, statusEff.Param2, sourceStatus?.Caster ?? Entity,
                    ResolveStatusTargets(statusEff, sourceStatus, host), statusEff.StatusEffectID2,
                    sourceStatus, statusEff.TargetType);
                break;
            case "ApplyElement":
                ApplyElementEffect.Execute(statusEff.Element, statusEff.Param1, sourceStatus?.Caster ?? Entity,
                    ResolveStatusTargets(statusEff, sourceStatus, host), ReactionSourceKind.StatusEffect,
                    string.Empty, statusEff.StatusEffectID2, BattleEventHub.CurrentEffectExecutionId, sourceStatus);
                break;
            case "ApplyStatus":
            {
                if (string.IsNullOrEmpty(statusEff.Param1)) break;
                string appliedStatusID = CountStatusSystem.ResolvePublicStatusReference(statusEff.Param1);
                var dm = DataManager.Instance;
                if (dm == null || !dm.StatusMainDict.TryGetValue(appliedStatusID, out var main))
                {
                    LogManager.LogWarning(LogCategory.ApplyStatus, $"状态不存在: {statusEff.Param1}");
                    break;
                }
                if (BattleTargetResolver.IsTeamTarget(statusEff.TargetType))
                {
                    foreach (var team in BattleTargetResolver.GetTeams(BattleManager.Instance,
                        ResolveStatusTargetResult(statusEff, sourceStatus, host)))
                        team.AddStatus(appliedStatusID, sourceStatus?.Caster ?? Entity,
                            statusEff.Duration, statusEff.AddInPhase, statusEff.TriggerPhase,
                            main, sourceStatus?.WeaponContext, statusEff.Param2, sourceStatus?.EquipmentSource,
                            enchantElement: statusEff.Element, enchantOverride: statusEff.Param3);
                    break;
                }
                if (!string.IsNullOrEmpty(statusEff.TargetType)
                    && statusEff.TargetType.EndsWith("Field", StringComparison.Ordinal))
                {
                    foreach (var field in ResolveStatusTargetFields(statusEff, sourceStatus, host))
                        field.AddStatus(appliedStatusID, sourceStatus?.Caster ?? Entity,
                            statusEff.Duration, statusEff.AddInPhase, statusEff.TriggerPhase,
                            main, sourceStatus?.WeaponContext, statusEff.Param2, equipmentSource: sourceStatus?.EquipmentSource,
                            enchantElement: statusEff.Element, enchantOverride: statusEff.Param3);
                    break;
                }
                var targets = ResolveStatusTargets(statusEff, sourceStatus, host);
                foreach (BattleEntity target in targets)
                {
                    if (target == null || !target.IsAlive) continue;
                    target.AddStatus(appliedStatusID,
                        sourceStatus?.Caster ?? Entity,
                        statusEff.Duration,
                        statusEff.AddInPhase,
                        statusEff.TriggerPhase,
                        main,
                        sourceStatus?.WeaponContext, statusEff.Param2, sourceStatus?.EquipmentSource,
                        enchantElement: statusEff.Element, enchantOverride: statusEff.Param3);
                }
                break;
            }

            case "GainEnergy":
            {
                ExecuteGainEnergy(statusEff, sourceStatus, host);
                break;
            }

            case "RemoveStatus":
            {
                if (BattleTargetResolver.IsTeamTarget(statusEff.TargetType))
                {
                    foreach (var team in BattleTargetResolver.GetTeams(BattleManager.Instance,
                        ResolveStatusTargetResult(statusEff, sourceStatus, host))) team.RemoveStatus(statusEff.Param1);
                    break;
                }
                if (!string.IsNullOrEmpty(statusEff.TargetType)
                    && statusEff.TargetType.EndsWith("Field", StringComparison.Ordinal))
                {
                    foreach (var field in ResolveStatusTargetFields(statusEff, sourceStatus, host))
                        RemoveStatusFromFieldSlot(statusEff.Param1, field);
                    break;
                }
                var targets = ResolveStatusTargets(statusEff, sourceStatus, host);
                if (targets == null) return;
                foreach (var target in targets)
                {
                    if (MonaScriptHooks.TryRemove(statusEff, sourceStatus, target)) continue;
                    if (WeaponStackTransferHooks.TryRemove(statusEff, sourceStatus, target)) continue;
                    if (statusEff.StatusEffectID == 430420301)
                    {
                        var weapon = sourceStatus?.WeaponContext;
                        if (weapon?.WeaponID != 3042 || sourceStatus.StatusID2 != "ST_Weapon_4304203"
                            || sourceStatus.Caster != weapon.Equipper || target != weapon.Equipper)
                            throw new InvalidOperationException("纸伞死亡解除冷却必须保留原装备者及武器来源。");
                        foreach (var cooldown in target.GetStatusInstances(statusEff.Param1))
                            if (cooldown.Caster == sourceStatus.Caster
                                && cooldown.WeaponContext?.SourceInstanceID == sourceStatus.WeaponContext.SourceInstanceID)
                                target.RemoveStatusInstance(cooldown);
                    }
                    else target.RemoveStatus(statusEff.Param1, -1);
                }
                break;
            }

            case "ExecuteEffect":
            {
                // 状态触发执行指定技能效果（箭雨 STE_Amber_ArrowRain -> SE_Burst_Amber1）
                if (string.IsNullOrEmpty(statusEff.Param1))
                {
                    LogManager.Log(LogCategory.StatusEffect, $"{statusEff.StatusEffectID2} ExecuteEffect 缺 Param1");
                    break;
                }
                if (!_effectById.TryGetValue(statusEff.Param1, out var targetEff))
                {
                    LogManager.LogWarning(LogCategory.StatusEffect, $"{statusEff.StatusEffectID2} ExecuteEffect 找不到技能效果 {statusEff.Param1}");
                    break;
                }
                var calledEffect = CreateCalledEffect(targetEff, statusEff);
                bool overridesTargets = HasAuthoredCallField(statusEff, "TargetType")
                    || HasAuthoredCallField(statusEff, "TargetSelect")
                    || HasAuthoredCallField(statusEff, "TargetConsecutive")
                    || HasAuthoredCallField(statusEff, "TargetOverride");
                if (!overridesTargets)
                {
                    ExecuteEffectFromStatus(calledEffect, GetEffectSkillType(calledEffect.SkillEffectID2, 3), sourceStatus);
                    break;
                }
                // 位置持续效果以 0,0 指向自身格时，空格属于合法的“本次无命中”。
                // 例如箭雨会覆盖连续场地，其中没有单位的格子应直接跳过，不作为目标解析错误报警。
                if (host is FieldPosition originField
                    && string.Equals(statusEff.TargetSelect?.Replace(" ", ""), "0,0", StringComparison.Ordinal)
                    && (originField.Occupant?.IsOnField != true))
                {
                    break;
                }
                // 先按状态效果自己的 TargetSelect/TargetOverride 解析位置，再把该位置交给被调用技能效果。
                var targetRequest = new StatusEffectData
                {
                    StatusEffectID2 = statusEff.StatusEffectID2,
                    TargetType = calledEffect.TargetType,
                    TargetSelect = statusEff.TargetSelect,
                    TargetOverride = calledEffect.TargetOverride,
                    TargetConsecutive = calledEffect.TargetConsecutive
                };
                var statusTargets = ResolveStatusTargetResult(targetRequest, sourceStatus, host, calledEffect.TargetNumber);
                if (!statusTargets.IsValid) break;
                var calledPositions = new List<int>(statusTargets.Positions);
                // 状态宿主范围已经解析成实际位置，子效果不能再次解释偏移或重新随机。
                calledEffect.TargetOverride = null;
                calledEffect.TargetConsecutive = 0;
                bool sameSide = statusTargets.Side == Entity.Side;
                calledEffect.TargetType = statusTargets.Domain == BattleTargetDomain.Team
                    ? (sameSide ? "AllyTeam" : "EnemyTeam") : statusTargets.Domain == BattleTargetDomain.Field
                    ? (sameSide ? "AllyField" : "EnemyField") : (sameSide ? "Allies" : "Enemy");
                // 目标设置与还原必须跟随排队任务，覆盖整个异步执行区间。
                BattleExecution.Enqueue(async () =>
                {
                    List<int> savedForced = new List<int>(ForcedTargetPositions);
                    var savedPreviousSkillTarget = _lastSkillTargetResult;
                    var savedPreviousPositions = new List<int>(_lastTargetPositions);
                    ForcedTargetPositions.Clear();
                    ForcedTargetPositions.AddRange(calledPositions);
                    _lastSkillTargetResult = null;
                    _lastTargetPositions.Clear();
                    try
                    {
                        await ExecuteEffectFromStatusAsync(calledEffect,
                            GetEffectSkillType(calledEffect.SkillEffectID2, 3), sourceStatus);
                    }
                    finally
                    {
                        ForcedTargetPositions.Clear();
                        ForcedTargetPositions.AddRange(savedForced);
                        _lastSkillTargetResult = savedPreviousSkillTarget;
                        _lastTargetPositions.Clear();
                        _lastTargetPositions.AddRange(savedPreviousPositions);
                    }
                    LogManager.Log(LogCategory.StatusAction, $"ExecuteEffect {statusEff.Param1} via status (positions={string.Join(",", calledPositions)})");
                });
                break;
            }

            case "BindStatus":
            {
                // BindStatus 是被动效果（2026-08-06）：
                // 术语表原文"只要该效果来源的状态存在，其目标就一直存在子状态"，
                // 绑定动作在 StatusBindingSystem（状态施加成功时）已按统一目标解析完成，
                // 这里只做确认日志，避免走到 default 报"未支持类型"。
                LogManager.Log(LogCategory.StatusEffect, $"{statusEff.StatusEffectID2} BindStatus -> {statusEff.Param1} (已由施加时自动绑定)");
                break;
            }

            case "Revive":
            {
                ReviveEffect.Execute(statusEff.Param1, sourceStatus?.Caster ?? Entity,
                    ResolveStatusTargets(statusEff, sourceStatus, host), sourceStatus);
                break;
            }

            case "AddTurn":
            {
                // 延长状态回合（凯亚 C2 击杀延长：Param1 逗号分隔多个状态ID，存在的都 +Param2）
                ExecuteAddTurn(statusEff, host);
                break;
            }

            case "Shield":
            {
                // 敌方None为数值盾公式；其他敌方元素盾仍使用12U等固定GU。
                var shieldTargets = ResolveStatusTargets(statusEff, sourceStatus, host);
                LogManager.Log(LogCategory.Effect, $"[Shield诊断] {statusEff.StatusEffectID2}: 目标数={(shieldTargets != null ? shieldTargets.Count : -1)} host={host} 来源状态={sourceStatus?.StatusID2}");
                if (shieldTargets == null) return;
                float shieldDur = statusEff.Duration > 0 ? statusEff.Duration : 999;
                long shieldGrantBatch = BattleEventHub.NextIdentity();
                foreach (var t in shieldTargets)
                {
                    if (t == null || !t.IsAlive) continue;
                    if (t.Type == BattleEntity.EntityType.Enemy && !OverallShieldSystem.IsOverall(sourceStatus?.MainData))
                    {
                        EnemyShieldEffectSystem.Apply(t, statusEff, sourceStatus);
                    }
                    else
                    {
                        // 每名受盾者单独判断目标条件；来源加成只在生成时结算，旧盾不随Buff改变。
                        float ordinaryShieldAmount = ResolveShieldAmount(
                            WeaponHooks.ShieldFormula(statusEff, sourceStatus),
                            statusEff.StatusEffectID2,
                            GetEffectSkillType(statusEff.StatusEffectID2, 2),
                            statusEff.Element, t);
                        t.AddShield(ordinaryShieldAmount, statusEff.Element, shieldDur, sourceStatus: sourceStatus,
                            creator: sourceStatus?.Caster ?? Entity, grantBatchId: shieldGrantBatch);
                        LogManager.Log(LogCategory.Effect,
                            $"护盾 {t.EntityID} +{ordinaryShieldAmount:F1} (元素 {statusEff.Element})");
                    }
                }
                break;
            }

            default:
                if (DamageImmunity.IsImmuneType(statusEff.EffectType))
                    break; // 被动免疫由HitBatch读取，不因Action调用添加、切换或重复执行。
                if (statusEff.EffectType == "APCost")
                    break; // 被动费用修正由 APCostResolver 查询，调用不额外扣费。
                if (statusEff.EffectType == "Buff" || statusEff.EffectType == "Debuff")
                {
                    // Buff/Debuff 为被动数值加成：属性聚合（GetStatusATKBonus/CritRate）直接读配表，无需执行
                    break;
                }
                LogManager.Log(LogCategory.StatusEffect, $"未支持类型 {statusEff.EffectType}");
                break;
        }
    }

    /// <summary>
    /// AddTurn：延长目标状态回合（2026-08-14）。
    /// Param1 = 状态ID2（分号分隔多个，存在的都 +N），Param2 = 增加回合数（空默认 +1）。
    /// host 为实体时查实体状态；为位置时查位置状态；找不到对应状态则静默跳过。
    /// </summary>
    void ExecuteAddTurn(StatusEffectData statusEff, object host)
    {
        if (host == null || statusEff == null || string.IsNullOrEmpty(statusEff.Param1)) return;
        int add = 1;
        if (!string.IsNullOrEmpty(statusEff.Param2) && !int.TryParse(statusEff.Param2.Trim(), out add))
            add = 1;

        foreach (var sid in statusEff.Param1.Split(';'))
        {
            string tid = sid.Trim();
            if (tid.Length == 0) continue;

            if (host is BattleEntity entity)
            {
                foreach (var inst in entity.GetStatusInstances(tid))
                {
                    StatusBindingSystem.ExtendDuration(inst, add);
                    LogManager.Log(LogCategory.AddTurn, $"{tid} +{add}t -> {inst.RemainingPhaseCount}");
                }
            }
            else if (host is FieldPosition slot)
            {
                foreach (var inst in slot.StatusList)
                {
                    if (inst.StatusID2 == tid)
                    {
                        StatusBindingSystem.ExtendDuration(inst, add);
                        LogManager.Log(LogCategory.AddTurn, $"{tid} +{add}t -> {inst.RemainingPhaseCount} ({slot})");
                    }
                }
            }
            else if (host is BattleTeamStatusHost team)
                foreach (var inst in team.GetStatusInstances(tid)) StatusBindingSystem.ExtendDuration(inst, add);
        }
    }

    // 获取状态宿主的位置编号（host 可能是 FieldPosition 或 BattleEntity）
    int GetHostSlotPosition(object host)
    {
        if (host is FieldPosition slot) return slot.SlotIndex;
        if (host is BattleEntity entity) return entity.SlotPosition;
        return -1;
    }

    /// <summary>
    /// 状态伤害多段 HitData 解析（2026-08-14）：Hits1-7 逐段，空段跳过；%引用单段。
    /// </summary>
    List<HitData> GetHitDataFromStatusEffectList(StatusEffectData statusEff)
    {
        var list = new List<HitData>();
        if (statusEff == null) return list;

        // 武器效果把完整倍率公式直接写在 Param1（如 Index(1)*TotalATK），不依赖角色 SkillLevel 表。
        if (!string.IsNullOrEmpty(statusEff.Param1) && statusEff.Param1.Contains("Index("))
        {
            list.Add(new HitData(1f, 0f));
            return list;
        }

        string[] direct = GetDirectHitExpressions(statusEff.Param1);
        if (direct != null)
        {
            foreach (string expression in direct) list.Add(HitDataParser.Parse(expression));
            return list;
        }

        // %引用（状态伤害如兔兔伯爵爆炸 STE_Bunny3）：单段，倍率在 Param1
        if (!string.IsNullOrEmpty(statusEff.Param1) && statusEff.Param1.StartsWith("%"))
        {
            HitData reference = HitDataParser.ResolveReferencedHit(statusEff.Param1,
                GetReferenceSourceHit(statusEff.Param1, GetEffectSkillType(statusEff.StatusEffectID2, 2)));
            list.Add(new HitData(1f, reference.ElementAura));
            return list;
        }

        // 状态伤害的等级表：查该效果在 SkillLevel 表里对应的 SkillType（兔兔伯爵爆炸=战技）
        int effSkillType = GetEffectSkillType(statusEff.StatusEffectID2, 2);
        if (!_levelDataBySkillType.TryGetValue(effSkillType, out var levelList) || levelList.Count == 0)
            return list;

        List<SkillLevelData> effectLevels = levelList.FindAll(s => s.ParamID == statusEff.StatusEffectID2);
        if (effectLevels.Count == 0) return list;

        SkillLevelData lv1 = effectLevels.Find(s => s.SkillLevel == 1);
        // 状态伤害跟随所属技能的等级（包括C3/C5），不能把角色等级当作天赋等级。
        int curLevel = SourceSkillLevel(statusEff.StatusEffectID2, effSkillType);
        SkillLevelData cur = effectLevels.Find(s => s.SkillLevel == curLevel);
        if (cur == null) cur = lv1;
        if (lv1 == null) lv1 = cur;
        if (cur == null) return list;

        for (int n = 1; n <= 7; n++)
        {
            string curStr = GetHitsField(cur, n);
            string lv1Str = GetHitsField(lv1, n);
            if (string.IsNullOrEmpty(curStr) && string.IsNullOrEmpty(lv1Str)) continue;
            list.Add(MergeHitData(curStr, lv1Str));
        }
        return list;
    }

    HitData GetHitDataFromStatusEffect(StatusEffectData statusEff)
    {
        var list = GetHitDataFromStatusEffectList(statusEff);
        return list.Count > 0 ? list[0] : new HitData(0, 0);
    }

    private async Task ExecuteAnemoStatusDamageAsync(StatusEffectData statusEff, StatusInstance sourceStatus,
        List<HitData> hits, int skillType, float baseValue, List<BattleEntity> targets,
        long effectExecutionID, string[] directHits, int hitOffset = 0)
    {
        BattleEntity source = sourceStatus != null && sourceStatus.Caster != null
            ? sourceStatus.Caster : Entity;
        for (int hitIndex = 0; hitIndex < hits.Count; hitIndex++)
        {
            int actualHitIndex = hitOffset + hitIndex;
            await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Hit, source, statusEff.StatusEffectID2, actualHitIndex + 1, targets), () =>
            {
                if (!EquipmentStatusLifetime.CanExecute(sourceStatus)) return;
                using var hitResponses = BattleResolutionContext.BeginCollection();
                using var countDamage = new CountDamageBatch();
                HitData hit = hits[hitIndex];
                var hitTargets = DamageHitTargets.Expand(targets, statusEff.Param2, statusEff.Element,
                    statusEff.TargetType == "Self" ? sourceStatus?.Host as BattleEntity : null);
                long logicalHitID = BattleEventHub.NextIdentity();
                using var hitSnapshot = BattleSpecialRules.BeginHit(logicalHitID, source,
                    hitTargets.Select(entry => entry.Target));
                var contexts = new List<ReactionContext>();
                var countPlans = new List<CountDamagePlan>();
                var takenSnapshots = new List<DMGTakenSnapshot>();
                foreach (var entry in hitTargets)
                {
                    BattleEntity target = entry.Target;
                    using var snapshotTarget = StatusFormulaSnapshot.EnterTarget(target);
                    HitData targetHit = entry.Scale(hit);
                    float hitBaseValue = directHits != null
                        ? ResolveDirectHitBase(directHits[hitIndex], statusEff.StatusEffectID2, skillType, statusEff.Element)
                        : StatusFormulaSnapshot.For(Entity, statusEff.StatusEffectID2) != null
                            ? ResolveBaseValue(WeaponExpressionEvaluator.Expand(statusEff.Param1, sourceStatus),
                                statusEff.StatusEffectID2, skillType, statusEff.Element) : baseValue;
                    CountDamagePlan countPlan = countDamage.Prepare(new CountDamageContext
                    {
                        Attacker = source, Target = target, SourceKind = ReactionSourceKind.StatusEffect,
                        EffectID = statusEff.StatusEffectID2, SkillID = string.Empty, DamageType = statusEff.DamageType,
                        Element = "Anemo", SkillType = skillType, EffectExecutionID = effectExecutionID,
                        HitIndex = actualHitIndex + 1, TargetIndex = contexts.Count + 1
                    });
                    countPlans.Add(countPlan);
                    takenSnapshots.Add(target.PrepareDMGTaken(
                        statusEff.StatusEffectID2, string.Empty, statusEff.DamageType, "Anemo"));
                    float damage = CalculateStatusDamage(hitBaseValue, targetHit, statusEff, target, skillType,
                        out ReactionDamageComponents components, countPlan.BaseDamageBonus);
                    contexts.Add(new ReactionContext
                    {
                        SourceEntity = source, Target = target, SourceKind = ReactionSourceKind.StatusEffect,
                        EquipmentSource = sourceStatus?.EquipmentSource,
                        SourceEffectID = statusEff.StatusEffectID2, EffectExecutionID = effectExecutionID,
                        LogicalHitID = logicalHitID, TargetIndex = contexts.Count + 1,
                        AttackElement = "Anemo", AttackAmount = targetHit.ElementAura,
                        PreReactionDamage = damage, DamageComponents = components,
                        DamageType = statusEff.DamageType
                    });
                }
                var primaryReactions = new List<ReactionResult>(contexts.Count);
                foreach (ReactionContext context in contexts)
                {
                    ReactionResult primaryReaction = ReactionResolver.Resolve(context);
                    StatusOnHitHookSystem.NotifyReactions(primaryReaction.TriggeredReactions);
                    primaryReactions.Add(primaryReaction);
                }

                SwirlPreparedBatch prepared = SwirlReactionHandler.PrepareBatch(contexts);
                var resolutions = new List<HitResolution>(contexts.Count);
                using (var batch = new HitBatch(logicalHitID))
                {
                    for (int index = 0; index < contexts.Count; index++)
                    {
                        ReactionContext context = contexts[index];
                        CriticalHitResult critical = ResolveFinalCriticalDamage(
                            primaryReactions[index].FinalDamage,
                            context.Target,
                            statusEff.StatusEffectID2,
                            string.Empty,
                            skillType,
                            "Anemo",
                            actualHitIndex + 1,
                            "风元素状态直伤", WeaponHooks.ForceCritical(statusEff), primaryReactions[index].PrimaryDamageReactionType);
                        resolutions.Add(batch.Resolve(new HitRequest
                        {
                            Properties = DamageProperties.Parse(statusEff.Element, statusEff.Param3),
                            Target = context.Target,
                            DamageBeforeShield = critical.DamageAfterCritical,
                            Critical = critical,
                            Element = "Anemo",
                            ElementAmount = context.AttackAmount,
                            DamageType = statusEff.DamageType,
                            EnemyShieldHit = primaryReactions[index].EnemyShieldHit,
                            DMGTaken = takenSnapshots[index],
                            Source = CreateStatusDamageSource(
                                sourceStatus, statusEff.StatusEffectID2, ReactionType.None),
                            HitIndex = actualHitIndex,
                            TargetIndex = index,
                            CountPlan = countPlans[index]
                        }));
                    }
                    batch.Flush();
                }
                for (int index = 0; index < primaryReactions.Count; index++)
                {
                    HitResolution resolution = resolutions[index];
                    float actualHPDamage = resolution?.DamageEvent != null
                        ? resolution.DamageEvent.ActualHPDamage
                        : 0f;
                    ReactionEffectExecutor.FinalizePrimaryHit(primaryReactions[index], actualHPDamage);
                    ReactionEffectExecutor.ExecuteDerivedHits(primaryReactions[index]);
                }
                SwirlBatchResult swirl = SwirlReactionHandler.ResolvePreparedBatch(prepared);
                BattleSpecialRules.OnAnemoResult(source, statusEff.StatusEffectID2,
                    statusEff.DamageHook, sourceStatus, swirl.Reaction);
                StatusOnHitHookSystem.NotifyReactions(swirl.Reaction.TriggeredReactions);
                ReactionEffectExecutor.ExecuteDerivedHits(swirl.Reaction);
            });
        }
    }

    DamageSourceInfo CreateStatusDamageSource(
        StatusInstance sourceStatus,
        string sourceEffectID,
        ReactionType reactionType)
    {
        return sourceStatus != null
            ? DamageSourceInfo.FromStatus(sourceStatus, sourceEffectID, reactionType)
            : DamageSourceInfo.Create(
                Entity,
                ReactionSourceKind.StatusEffect,
                string.Empty,
                sourceEffectID,
                reactionType);
    }
    float CalculateStatusDamage(
        float baseValue,
        HitData hit,
        StatusEffectData eff,
        BattleEntity target,
        int skillType,
        out ReactionDamageComponents components,
        float countBaseDamageBonus = 0f)
    {
        float baseDMG = baseValue * hit.Multiplier;

        DamageProperties properties = DamageProperties.Parse(eff.Element, eff.Param3);
        if (properties.UsesSpecialFormula)
            return SpecialSkillDamageCalculator.Calculate(Entity, target, baseDMG, properties,
                eff.Element, eff.StatusEffectID2, string.Empty, eff.DamageType, out components, countBaseDamageBonus);

        baseDMG += countBaseDamageBonus + SourceValue("BaseDMGBonusFlat", Entity.BaseDMGBonusFlat)
            + SourceBonus("BaseDMGBonusFlat", eff.StatusEffectID2, eff.Element, target,
                () => Entity.GetStatusBaseDMGBonusFlat(eff.StatusEffectID2, "", skillType, eff.Element, target));

        var memory = StatusFormulaSnapshot.For(Entity, eff.StatusEffectID2);
        float dmgBonus = 1f + SourceValue("DMGBonus", Entity.DMGBonus) + GetElementBonus(eff.Element)
            + (memory != null ? memory.TeamDamage(eff.Element) + memory.Bonus("DMGBonus", eff.StatusEffectID2, eff.Element, target)
                : Entity.GetStatusDMGBonus(eff.StatusEffectID2, "", skillType, eff.Element, target));
        float sourceLevel = memory?.SourceLevel ?? Entity.Level;
        float reduction = SourceValue("DEFReduction", Entity.DEFReduction);
        float ignored = SourceValue("DEFIgnored", Entity.DEFIgnored);
        float defRes;
        if (target.Type == BattleEntity.EntityType.Enemy)
        {
            float attacker = sourceLevel + 100f;
            float defender = (target.Level + 100f) * (1f - reduction) * (1f - ignored);
            defRes = attacker / (attacker + defender);
        }
        else
        {
            float effectiveDef = target.GetFinalDEF() * (1f - reduction) * (1f - ignored);
            if (effectiveDef < 0) effectiveDef = 0;
            defRes = 1f - effectiveDef / (effectiveDef + 5f * sourceLevel + 500f);
            defRes = Mathf.Clamp(defRes, 0.1f, 1f);
        }

        float res = GetElementResistance(target, eff.Element);
        float resMultiplier;
        if (res < 0)
            resMultiplier = 1f - res / 2f;
        else if (res < 0.75f)
            resMultiplier = 1f - res;
        else
            resMultiplier = 1f / (1f + 4f * res);

        components = new ReactionDamageComponents
        {
            SkillBaseDamage = baseDMG,
            DamageBonusMultiplier = dmgBonus,
            DefenseMultiplier = defRes,
            ResistanceMultiplier = resMultiplier
        };
        memory?.Trace(eff.StatusEffectID2, target, baseValue, hit.Multiplier, baseDMG, dmgBonus, defRes, resMultiplier);
        return baseDMG * dmgBonus * defRes * resMultiplier;
    }
}
