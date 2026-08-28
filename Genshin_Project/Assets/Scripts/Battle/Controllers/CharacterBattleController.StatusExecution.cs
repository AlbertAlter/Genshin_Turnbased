using System;
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
    /// MaxTimePerTurn（每回合最大次数）/ MaxTimePerLife（全场最大次数）/ Cooldown（行动冷却回合）。
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
        // 懒重置：新回合时清零本回合计数
        if (state.LastActionTurn != turn)
        {
            state.LastActionTurn = turn;
            state.TurnTriggerCount = 0;
        }
        if (act.MaxTimePerTurn > 0 && state.TurnTriggerCount >= act.MaxTimePerTurn)
            return true;
        if (act.MaxTimePerLife > 0 && state.LifeTriggerCount >= act.MaxTimePerLife)
            return true;
        if (act.Cooldown > 0 && state.LastTriggerTurn >= 0 && turn - state.LastTriggerTurn < act.Cooldown)
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
        if (act.Cooldown > 0) state.LastTriggerTurn = turn;

        // ActionRuntime 负责逐行动限制；StatusInstance 上的旧字段仍是状态级公开汇总，
        // 供界面、调试与既有调用方读取，不能在迁移后停止记账。
        if (inst.LastActionTurn != turn)
        {
            inst.LastActionTurn = turn;
            inst.TurnTriggerCount = 0;
        }
        inst.TurnTriggerCount++;
        inst.LifeTriggerCount++;
        if (act.Cooldown > 0) inst.LastTriggerTurn = turn;
    }

    public void ExecuteStatusActions(StatusInstance inst, List<StatusActionData> actions, object host, string actionType)
    {
        foreach (var act in actions)
        {
            if (act.ActionType != actionType) continue;
            // 事件驱动钩子行（ScriptHook=PreAlliesDamage / Kill(...)）：不参与普通触发（阶段/回合等），由各自钩子机制单独触发（2026-08-19）
            if (ScriptHookEvaluator.IsEventDriven(act.ScriptHook)) continue;
            // ScriptHook：只有该特殊处理器返回真值时才触发该行行动（统一求值入口 ScriptHookEvaluator）
            if (!ScriptHookEvaluator.Evaluate(act.ScriptHook, inst != null ? inst.Caster : null)) continue;
            // 触发限制：每回合/全场次数上限、行动冷却（2026-08-14）
            if (StatusActionLimitReached(inst, act)) continue;
            CountStatusActionTrigger(inst, act);
            ExecuteStatusActionEffects(inst, act, host);
        }
    }

    /// <summary>
    /// 状态行动协程版（2026-08-13，战斗流程协程链使用）：
    /// Display==1（局内显示图标）的行动先 yield 0.5s 再执行——【整个战斗流程暂停等待】，
    /// 演出完成才继续后续结算（如蓄力射击 OnEnd 等待期间，同批到期状态的移除也被暂停，标记不会提前消失）。
    /// OnTrigger/OnItsTurn/OnEnd 由 BattleManager 状态机协程调用；OnApply/OnHit 用同步版（技能执行链内无法暂停）。
    /// </summary>
    public System.Collections.IEnumerator ExecuteStatusActionsAsync(StatusInstance inst, List<StatusActionData> actions, object host, string actionType)
    {
        foreach (var act in actions)
        {
            if (act.ActionType != actionType) continue;
            // 事件驱动钩子行（ScriptHook=PreAlliesDamage / Kill(...)）：不参与普通触发，由各自钩子机制单独触发（2026-08-19）
            if (ScriptHookEvaluator.IsEventDriven(act.ScriptHook)) continue;
            if (!ScriptHookEvaluator.Evaluate(act.ScriptHook, inst != null ? inst.Caster : null)) continue;
            // 触发限制：每回合/全场次数上限、行动冷却（2026-08-14）
            if (StatusActionLimitReached(inst, act)) continue;
            CountStatusActionTrigger(inst, act);
            if (inst != null && inst.MainData != null && inst.MainData.Display == 1)
            {
                // OnEnd延迟=流程暂停（expired循环协程等待），标记不会被提前移除，无需预解析快照（2026-08-14）
                yield return new WaitForSeconds(0.5f);
            }
            ExecuteStatusActionEffects(inst, act, host);
        }
    }

    /// <summary>
    /// 预解析状态行动的效果目标并快照（结果先记着）：
    /// 遍历行动的效果（Param1 逗号分隔），对 Damage 类效果调用 ResolveTargets（此刻目标/标记还在），
    /// 结果存入快照表；延迟结束后 ExecuteDamage 直接用快照，不重新解析。
    /// </summary>
    void PreResolveStatusActionTargets(StatusActionData act, object host)
    {
        if (string.IsNullOrEmpty(act.Param1)) return;
        var dm = DataManager.Instance;
        if (dm == null) return;
        foreach (var tid in act.Param1.Split(','))
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

void ExecuteStatusActionEffects(StatusInstance sourceStatus, StatusActionData act, object host)
    {
        if (string.IsNullOrEmpty(act.Param1)) return;
        var dm = DataManager.Instance;
        BeginStatusTargetSequence();

        // Param1 是效果ID2列表（逗号分隔），如 "STE_Bunny2,STE_Bunny4"
        // 特殊：以 SK_ 开头的是技能ID（如蓄力箭 SK_HeavyAttack_Amber），执行技能效果序列
        string[] ids = act.Param1.Split(',');
        foreach (var id in ids)
        {
            string tid = id.Trim();
            if (string.IsNullOrEmpty(tid)) continue;

            if (tid.StartsWith("SK_"))
            {
                // Skill 容器调用：完整执行其资源/冷却/次数/效果规则；是否属于主动行为只看被调用 Skill 的 ActionType。
                if (_skillById2.ContainsKey(tid))
                {
                    ExecuteSkillByID(tid, GetSkillTypeForExecution(tid, 1), false);
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
                    ExecuteEffect(skillEff, GetEffectSkillType(tid, 1));
                }
                else
                {
                    LogManager.LogWarning(LogCategory.StatusAction, $"状态效果不存在: {tid}");
                }
                continue;
            }
            ExecuteStatusEffect(statusEff, sourceStatus, host);
        }
    }

    void ExecuteStatusEffect(StatusEffectData statusEff, StatusInstance sourceStatus, object host)
    {
        switch (statusEff.EffectType)
        {
            case "ApplyStatus":
            {
                if (string.IsNullOrEmpty(statusEff.Param1)) break;
                var dm = DataManager.Instance;
                if (dm == null || !dm.StatusMainDict.TryGetValue(statusEff.Param1, out var main))
                {
                    LogManager.LogWarning(LogCategory.ApplyStatus, $"状态不存在: {statusEff.Param1}");
                    break;
                }
                var targets = ResolveStatusTargets(statusEff, sourceStatus, host);
                foreach (BattleEntity target in targets)
                {
                    if (target == null || !target.IsAlive) continue;
                    target.AddStatus(statusEff.Param1,
                        sourceStatus?.Caster ?? Entity,
                        statusEff.Duration,
                        statusEff.AddInPhase,
                        statusEff.TriggerPhase,
                        main,
                        sourceStatus?.WeaponContext);
                }
                break;
            }

            case "Damage":
            {
                long effectExecutionID = ReactionResolver.BeginEffectExecution();
                // 状态伤害（兔兔伯爵爆炸 STE_Bunny2 等）：从 SkillType=2 查等级，多段 Hits1-7 逐段结算
                var hits = GetHitDataFromStatusEffectList(statusEff);
                if (hits.Count == 0 || (hits.Count == 1 && hits[0].Multiplier <= 0)) return;
                int stSkillType = GetEffectSkillType(statusEff.StatusEffectID2, 2);
                string resolvedParam = WeaponExpressionEvaluator.Expand(statusEff.Param1, sourceStatus);
                float baseValue = ResolveBaseValue(resolvedParam, statusEff.StatusEffectID2, stSkillType, statusEff.Element);
                var targets = ResolveStatusTargets(statusEff, sourceStatus, host);
                if (targets == null) return;
                if (statusEff.Element == "Geo" && targets.Count > 1)
                {
                    targets = targets
                        .OrderBy(target => target != null ? target.SlotPosition : int.MaxValue)
                        .ToList();
                }
                if (statusEff.Element == "Anemo")
                {
                    ExecuteAnemoStatusDamage(statusEff, sourceStatus, hits, stSkillType,
                        baseValue, targets, effectExecutionID);
                    break;
                }

                foreach (var target in targets)
                {
                    foreach (var hit in hits)
                    {
                        if (!target.IsAlive) break;
                        float damage = CalculateStatusDamage(
                            baseValue,
                            hit,
                            statusEff,
                            target,
                            stSkillType,
                            out ReactionDamageComponents damageComponents);
                        // 元素反应（2026-08-14）：状态伤害同样参与反应判定；
                        // 2026-08-15：按元素量模型消耗，攻击元素残留量决定是否上附着
                        ReactionResult reaction = ReactionResolver.Resolve(new ReactionContext
                        {
                            SourceEntity = sourceStatus != null && sourceStatus.Caster != null ? sourceStatus.Caster : Entity,
                            Target = target,
                            SourceKind = ReactionSourceKind.StatusEffect,
                            SourceEffectID = statusEff.StatusEffectID2,
                            EffectExecutionID = effectExecutionID,
                            AttackElement = statusEff.Element,
                            AttackAmount = hit.ElementAura,
                            PreReactionDamage = damage,
                            DamageComponents = damageComponents,
                            DamageType = statusEff.DamageType,
                            PoiseDamage = hit.Poise
                        });
                        StatusOnHitHookSystem.NotifyReactions(reaction.TriggeredReactions);
                        if (reaction.HasReaction)
                            LogManager.Log(LogCategory.StatusDamage, $"{statusEff.StatusEffectID2} -> {target.EntityID} 触发{reaction.TriggeredReactions[0].DisplayName}");
                        float final = target.AbsorbDamageWithShield(reaction.FinalDamage, statusEff.Element);
                        // 带来源扣血（2026-08-19）：状态伤害统一入口（来源=状态施放者）
                        target.TakeDamage(final, DamageSourceInfo.Create(
                            sourceStatus != null && sourceStatus.Caster != null ? sourceStatus.Caster : Entity,
                            ReactionSourceKind.StatusEffect,
                            string.Empty,
                            statusEff.StatusEffectID2,
                            reaction != null && reaction.HasReaction ? reaction.TriggeredReactions[0].Type : ReactionType.None));
                        ReactionEffectExecutor.FinalizePrimaryHit(reaction, final);
                        PoiseSystem.ApplyPoiseDamage(target, hit.Poise, final,
                            sourceStatus != null && sourceStatus.Caster != null ? sourceStatus.Caster : Entity);
                        ReactionEffectExecutor.ExecuteDerivedHits(reaction);
                        LogManager.Log(LogCategory.StatusDamage, $"{statusEff.StatusEffectID2} -> {target.EntityID} : {final:F1}");
                    }
                }
                break;
            }

            case "GainEnergy":
            {
                if (!float.TryParse(statusEff.Param1, out float gained)) return;
                var targets = ResolveStatusTargets(statusEff, sourceStatus, host);
                if (targets == null) return;
                foreach (var target in targets)
                {
                    float total = gained;
                    if (statusEff.Param2 == "Based")
                    {
                        float elementStatus = (target.Type == BattleEntity.EntityType.Character
                            && target.CharacterCtrl != null && target.CharacterCtrl._attrData != null
                            && target.CharacterCtrl._attrData.Element == statusEff.Element) ? 1.5f : 0.5f;
                        float recharge = target.TotalRechargeRate > 0 ? target.TotalRechargeRate : 1f;
                        total = gained * 1f * elementStatus * recharge;
                    }
                    target.CurrentEnergy = Mathf.Min(target.CurrentEnergy + Mathf.RoundToInt(total), target.MaxEnergy);
                    LogManager.Log(LogCategory.StatusEnergy, $"{target.EntityID} +{total:F1}");
                }
                break;
            }

            case "RemoveStatus":
            {
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
                    target.RemoveStatus(statusEff.Param1, -1);
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
                // 位置持续效果以 0,0 指向自身格时，空格属于合法的“本次无命中”。
                // 例如箭雨会覆盖连续场地，其中没有单位的格子应直接跳过，不作为目标解析错误报警。
                if (host is FieldPosition originField
                    && string.Equals(statusEff.TargetSelect?.Replace(" ", ""), "0,0", StringComparison.Ordinal)
                    && (originField.Occupant == null || !originField.Occupant.IsAlive))
                {
                    break;
                }
                // 先按状态效果自己的 TargetSelect/TargetOverride 解析位置，再把该位置交给被调用技能效果。
                var statusTargets = ResolveStatusTargetResult(statusEff, sourceStatus, host);
                if (!statusTargets.IsValid) break;
                List<int> savedForced = new List<int>(ForcedTargetPositions);
                var savedPreviousSkillTarget = _lastSkillTargetResult;
                var savedPreviousPositions = new List<int>(_lastTargetPositions);
                ForcedTargetPositions.Clear();
                ForcedTargetPositions.AddRange(statusTargets.Positions);
                _lastSkillTargetResult = null;
                _lastTargetPositions.Clear();

                if (targetEff != null)
                {
                    ExecuteEffect(targetEff, GetEffectSkillType(targetEff.SkillEffectID2, 3));
                }

                ForcedTargetPositions.Clear();
                ForcedTargetPositions.AddRange(savedForced); // 恢复外层强制位置
                _lastSkillTargetResult = savedPreviousSkillTarget;
                _lastTargetPositions.Clear();
                _lastTargetPositions.AddRange(savedPreviousPositions);
                LogManager.Log(LogCategory.StatusAction, $"ExecuteEffect {statusEff.Param1} via status (positions={string.Join(",", statusTargets.Positions)})");
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

            case "AddTurn":
            {
                // 延长状态回合（凯亚 C2 击杀延长：Param1 逗号分隔多个状态ID，存在的都 +Param2）
                ExecuteAddTurn(statusEff, host);
                break;
            }

            case "Heal":
            {
                // 治疗（2026-08-14）：Param1=治疗量公式，目标=TargetType解析
                var healTargets = ResolveStatusTargets(statusEff, sourceStatus, host);
                if (healTargets == null) return;
                float healAmt = ResolveBaseValue(statusEff.Param1, statusEff.StatusEffectID2, GetEffectSkillType(statusEff.StatusEffectID2, 2), statusEff.Element);
                foreach (var t in healTargets)
                {
                    if (t == null || !t.IsAlive) continue;
                    t.Heal(healAmt, sourceStatus != null && sourceStatus.Caster != null
                        ? sourceStatus.Caster
                        : Entity);
                    LogManager.Log(LogCategory.Effect, $"治疗 {t.EntityID} +{healAmt:F1} (HP {t.CurrentHP:F1})");
                }
                break;
            }

            case "Shield":
            {
                // 护盾（2026-08-14）：Param1=护盾量公式（如 0.3*TotalHP），Element=护盾元素，Duration=持续回合
                var shieldTargets = ResolveStatusTargets(statusEff, sourceStatus, host);
                LogManager.Log(LogCategory.Effect, $"[Shield诊断] {statusEff.StatusEffectID2}: 目标数={(shieldTargets != null ? shieldTargets.Count : -1)} host={host} 来源状态={sourceStatus?.StatusID2}");
                if (shieldTargets == null) return;
                float shieldAmt = ResolveBaseValue(statusEff.Param1, statusEff.StatusEffectID2, GetEffectSkillType(statusEff.StatusEffectID2, 2), statusEff.Element);
                float shieldDur = statusEff.Duration > 0 ? statusEff.Duration : 999;
                foreach (var t in shieldTargets)
                {
                    if (t == null || !t.IsAlive) continue;
                    t.AddShield(shieldAmt, statusEff.Element, shieldDur);
                    LogManager.Log(LogCategory.Effect, $"护盾 {t.EntityID} +{shieldAmt:F1} (元素 {statusEff.Element})");
                }
                break;
            }

            default:
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
                var inst = entity.GetStatus(tid);
                if (inst == null) continue; // 找不到静默跳过（C6状态下 ST_Burst_Kaeya 不存在）
                inst.RemainingPhaseCount += add;
                LogManager.Log(LogCategory.AddTurn, $"{tid} +{add}t -> {inst.RemainingPhaseCount}");
            }
            else if (host is FieldPosition slot)
            {
                foreach (var inst in slot.StatusList)
                {
                    if (inst.StatusID2 == tid)
                    {
                        inst.RemainingPhaseCount += add;
                        LogManager.Log(LogCategory.AddTurn, $"{tid} +{add}t -> {inst.RemainingPhaseCount} ({slot})");
                        break;
                    }
                }
            }
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
            list.Add(new HitData(1f, 0f, 0f));
            return list;
        }

        // %引用（状态伤害如兔兔伯爵爆炸 STE_Bunny3）：单段，倍率在 Param1
        if (!string.IsNullOrEmpty(statusEff.Param1) && statusEff.Param1.StartsWith("%"))
        {
            string rest = statusEff.Param1.Contains(",") ? statusEff.Param1.Substring(statusEff.Param1.IndexOf(',')) : "";
            float poise = 0f, aura = 0f;
            var p = rest.Split(',');
            if (p.Length > 1) float.TryParse(p[1].Trim(), out poise);
            if (p.Length > 2) float.TryParse(p[2].Trim(), out aura);
            list.Add(new HitData(1f, poise, aura));
            return list;
        }

        // 状态伤害的等级表：查该效果在 SkillLevel 表里对应的 SkillType（兔兔伯爵爆炸=战技）
        int effSkillType = GetEffectSkillType(statusEff.StatusEffectID2, 2);
        if (!_levelDataBySkillType.TryGetValue(effSkillType, out var levelList) || levelList.Count == 0)
            return list;

        List<SkillLevelData> effectLevels = levelList.FindAll(s => s.ParamID == statusEff.StatusEffectID2);
        if (effectLevels.Count == 0 && levelList.Count > 0)
        {
            string fallbackParamID = levelList[0].ParamID;
            effectLevels = levelList.FindAll(s => s.ParamID == fallbackParamID);
        }
        if (effectLevels.Count == 0) return list;

        SkillLevelData lv1 = effectLevels.Find(s => s.SkillLevel == 1);
        int curLevel = Mathf.Clamp(Level, 1, 15);
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
        return list.Count > 0 ? list[0] : new HitData(0, 0, 0);
    }

    private void ExecuteAnemoStatusDamage(StatusEffectData statusEff, StatusInstance sourceStatus,
        List<HitData> hits, int skillType, float baseValue, List<BattleEntity> targets,
        long effectExecutionID)
    {
        BattleEntity source = sourceStatus != null && sourceStatus.Caster != null
            ? sourceStatus.Caster : Entity;
        foreach (HitData hit in hits)
        {
            var contexts = new List<ReactionContext>();
            foreach (BattleEntity target in targets)
            {
                if (target == null || !target.IsAlive) continue;
                float damage = CalculateStatusDamage(baseValue, hit, statusEff, target, skillType,
                    out ReactionDamageComponents components);
                contexts.Add(new ReactionContext
                {
                    SourceEntity = source, Target = target, SourceKind = ReactionSourceKind.StatusEffect,
                    SourceEffectID = statusEff.StatusEffectID2, EffectExecutionID = effectExecutionID,
                    AttackElement = "Anemo", AttackAmount = hit.ElementAura,
                    PreReactionDamage = damage, DamageComponents = components,
                    DamageType = statusEff.DamageType, PoiseDamage = hit.Poise
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
            for (int index = 0; index < contexts.Count; index++)
            {
                ReactionContext context = contexts[index];
                ReactionResult primaryReaction = primaryReactions[index];
                float finalDamage = context.Target.AbsorbDamageWithShield(context.PreReactionDamage, "Anemo");
                context.Target.TakeDamage(finalDamage, DamageSourceInfo.Create(source,
                    ReactionSourceKind.StatusEffect, string.Empty, statusEff.StatusEffectID2,
                    ReactionType.None));
                ReactionEffectExecutor.FinalizePrimaryHit(primaryReaction, finalDamage);
                PoiseSystem.ApplyPoiseDamage(context.Target, hit.Poise, finalDamage, source);
                ReactionEffectExecutor.ExecuteDerivedHits(primaryReaction);
            }
            SwirlBatchResult swirl = SwirlReactionHandler.ResolvePreparedBatch(prepared);
            StatusOnHitHookSystem.NotifyReactions(swirl.Reaction.TriggeredReactions);
            ReactionEffectExecutor.ExecuteDerivedHits(swirl.Reaction);
        }
    }
    float CalculateStatusDamage(
        float baseValue,
        HitData hit,
        StatusEffectData eff,
        BattleEntity target,
        int skillType,
        out ReactionDamageComponents components)
    {
        float baseDMG = baseValue * hit.Multiplier;

        float critMult = 1f;
        float critRate = Mathf.Clamp(Entity.CritRate + Entity.WeaponCritRate + Entity.GetStatusCritRate(eff.StatusEffectID2, "", skillType, eff.Element, target), 0f, 1f);
        float critRoll = UnityEngine.Random.value;
        if (critRoll < critRate)
            critMult = 1f + Entity.CritDMG + Entity.WeaponCritDMG;
        LogManager.Log(LogCategory.Crit, $"率={critRate:P0}(基础{Entity.CritRate:P0}+状态{Entity.GetStatusCritRate(eff.StatusEffectID2, "", skillType, eff.Element, target):P0}) 随机={critRoll:F4} → {(critRoll < critRate ? $"暴击 x{critMult:F2}" : "未暴击")}");

        float dmgBonus = 1f + Entity.DMGBonus + GetElementBonus(eff.Element)
            + Entity.GetStatusDMGBonus(eff.StatusEffectID2, "", skillType, eff.Element, target);

        float defRes;
        if (target.Type == BattleEntity.EntityType.Enemy)
        {
            float attacker = Entity.Level + 100f;
            float defender = (target.Level + 100f) * (1f - Entity.DEFReduction) * (1f - Entity.DEFIgnored);
            defRes = attacker / (attacker + defender);
        }
        else
        {
            float effectiveDef = target.TotalDEF * (1f - Entity.DEFReduction) * (1f - Entity.DEFIgnored);
            if (effectiveDef < 0) effectiveDef = 0;
            defRes = 1f - effectiveDef / (effectiveDef + 5f * Entity.Level + 500f);
            defRes = Mathf.Clamp(defRes, 0.1f, 1f);
        }

        float res = GetElementResistance(target, eff.Element ?? "None");
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
            CriticalMultiplier = critMult,
            DamageBonusMultiplier = dmgBonus,
            DefenseMultiplier = defRes,
            ResistanceMultiplier = resMultiplier
        };
        return baseDMG * critMult * dmgBonus * defRes * resMultiplier;
    }
}
