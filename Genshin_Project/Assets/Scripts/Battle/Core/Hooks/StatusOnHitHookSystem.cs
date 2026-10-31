using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>
/// 新版 Status_Action 的事件分发入口。
/// 主动行为统一查询 ActiveActionQuery；本系统不读取按钮返回值，也不自行定义哪些 Skill 属于主动行为。
/// </summary>
public static class StatusOnHitHookSystem
{
    private static readonly ConditionalWeakTable<ReactionOccurrence, object> NotifiedReactions
        = new ConditionalWeakTable<ReactionOccurrence, object>();
    private static readonly object ReactionNotifiedMarker = new object();
    private static readonly List<DamageEffectFrame> DamageEffectFrames = new List<DamageEffectFrame>();

    private sealed class DamageEffectFrame
    {
        public IDisposable ResolutionScope;
        public BattleEntity Source;
        public string SourceEffectID;
        public bool IsActiveAction;
        public readonly List<DamageResolvedEvent> Events = new List<DamageResolvedEvent>();
    }

    private sealed class PostDamageHookCandidate
    {
        public StatusInstance Status;
        public object Host;
        public string EventHookName;
    }

    private sealed class DamageHookCandidate
    {
        public StatusInstance Status;
        public object Host;
        public StatusActionData Action;
        public DamageResolvedEvent DamageEvent;
        public long Sequence;
    }

    private sealed class DamageEffectScope : IDisposable
    {
        private DamageEffectFrame _frame;

        public DamageEffectScope(DamageEffectFrame frame)
        {
            _frame = frame;
        }

        public void Dispose()
        {
            if (_frame == null) return;
            EndDamageEffect(_frame);
            _frame = null;
        }
    }

    /// <summary>标记一个 Damage 效果的完整执行区间；结束时统一派发 PostAlliesDamage / PostSelfDamage。</summary>
    public static IDisposable BeginDamageEffect(
        BattleEntity source,
        string sourceEffectID,
        bool isActiveAction)
    {
        var frame = new DamageEffectFrame
        {
            ResolutionScope = BattleResolutionContext.BeginEffect(),
            Source = source,
            SourceEffectID = sourceEffectID ?? string.Empty,
            IsActiveAction = isActiveAction
        };
        DamageEffectFrames.Add(frame);
        return new DamageEffectScope(frame);
    }

    public static void NotifyPlayerAction(BattleEntity actor, SkillMainData skill)
    {
        using var collection = BattleResolutionContext.BeginCollection();
        if (actor == null || skill == null) return;
        using var declaration = BattleEventHub.Enter(BattleEventHub.Publish(
            new ActionEventData(actor, skill.SkillID2, skill.ActionType, false)));
        DataManager dm = DataManager.Instance;
        if (dm == null) return;

        var visited = new HashSet<StatusInstance>();
        StatusHostAccess.VisitEntity(actor, (statuses, host) =>
        {
            foreach (var status in statuses)
                if (visited.Add(status)) NotifyPlayerActionForStatus(status, host, actor, skill, dm);
        });
        // 荆棘监听任意玩家主动行动，复用本次声明与响应集合；本地访问过的实例不重复收集。
        var fields = BattleManager.Instance?.Field?.AllySlots;
        if (fields != null)
            foreach (var field in fields)
                foreach (var status in field.StatusList)
                    if (Enemy2011To2020Hooks.IsGlobalPlayerActionStatus(status) && visited.Add(status))
                        NotifyPlayerActionForStatus(status, field, actor, skill, dm);
    }

    /// <summary>换人正式提交后，收集双方进出场声明；响应仍按全局 ApplyOrder 执行。</summary>
    public static void NotifySwitch(BattleEntity outgoing, BattleEntity incoming, BattleSwitchReason reason = BattleSwitchReason.Unknown)
    {
        if (outgoing == incoming || incoming == null) return;
        using var collection = BattleResolutionContext.BeginCollection();
        using var declaration = BattleEventHub.Enter(BattleEventHub.Publish(new SwitchEventData(outgoing, incoming, reason)));
        DataManager dm = DataManager.Instance;
        BattleManager battle = BattleManager.Instance;
        if (dm == null || battle == null) return;
        StatusHostAccess.VisitBattle(battle, (statuses, host) => NotifySwitchForHost(statuses, host, outgoing, incoming, dm));
    }

    static void NotifySwitchForHost(IEnumerable<StatusInstance> statuses, object host,
        BattleEntity outgoing, BattleEntity incoming, DataManager dm)
    {
        foreach (StatusInstance status in new List<StatusInstance>(statuses))
        {
            if (!CanExecute(status) || !dm.StatusActionDict.TryGetValue(status.StatusID2, out var actions)) continue;
            foreach (StatusActionData action in actions)
            {
                if (action.ActionType != "OnTrigger") continue;
                NotifySwitchAction(status, action, host, "SwitchOut", outgoing);
                NotifySwitchAction(status, action, host, "SwitchIn", incoming);
            }
        }
    }

    static void NotifySwitchAction(StatusInstance status, StatusActionData action, object host,
        string eventName, BattleEntity actor)
    {
        if (actor == null || !ScriptHookParser.TryFind(action.ScriptHook, eventName, out string argument)
            || !ScriptHookEvaluator.MatchesSwitchActor(argument, status, actor)) return;
        ExecuteEvent(status, action, host, new ScriptHookContext
        {
            Caster = status.Caster,
            Target = actor,
            Status = status,
            StatusHost = host,
            SwitchedEntity = actor,
            EventHookName = eventName,
            EventHookArgument = argument
        });
    }

    /// <summary>兼容旧调用；新伤害入口统一使用 NotifyDamageBatch。</summary>
    public static void NotifyHit(DamageResolvedEvent damageEvent)
    {
        NotifyDamageBatch(new[] { damageEvent });
    }

    /// <summary>
    /// 同一个逻辑 hit 的全部目标完成扣血后统一收集状态响应。
    /// OnHit 只读取 HitLanded；OnDamage 读取实际生命伤害。候选项跨来源方、受击方和所有目标按 ApplyOrder 排序。
    /// </summary>
    public static void NotifyDamageBatch(IEnumerable<DamageResolvedEvent> damageEvents)
    {
        using var collection = BattleResolutionContext.BeginCollection();
        if (damageEvents == null) return;
        DataManager dm = DataManager.Instance;

        var candidates = new List<DamageHookCandidate>();
        long sequence = 0;
        foreach (DamageResolvedEvent damageEvent in damageEvents)
        {
            if (damageEvent == null || damageEvent.Target == null) continue;
            damageEvent.Declaration = damageEvent.Declaration ?? BattleEventHub.Record(damageEvent,
                new DamageEventData(damageEvent), damageEvent.RuntimeHitID, damageEvent.TargetIndex);
            using var declaration = BattleEventHub.Enter(damageEvent.Declaration);
            BattleEventHub.Dispatch(damageEvent.Declaration);
            HPDecreaseEvent hpDecrease = null;
            BattleDeclaration hpDeclaration = null;
            if (damageEvent.ActualHPDamage > 0f)
            {
                hpDecrease = new HPDecreaseEvent(damageEvent.Target, damageEvent.HPBefore, damageEvent.HPAfter);
                hpDeclaration = BattleEventHub.Publish(new HPChangedEventData(damageEvent.Target,
                    damageEvent.HPBefore, damageEvent.HPAfter, "Damage"), hpDecrease);
            }
            if (dm == null) continue;
            if (!damageEvent.HitLanded && damageEvent.ActualHPDamage <= 0f) continue;
            RecordDamageEffectHit(damageEvent);
            if (damageEvent.ActualHPDamage > 0f)
                CollectHPDecrease(hpDecrease, dm, damageEvent, hpDeclaration);

            BattleEntity source = damageEvent.Source != null ? damageEvent.Source.SourceEntity : null;
            if (source != null)
            {
                AddDamageCandidates(candidates, source.GetStatusList(), source, damageEvent, dm, true, ref sequence);
                if (source.Position != null)
                    AddDamageCandidates(candidates, source.Position.StatusList, source.Position, damageEvent, dm, true, ref sequence);
                var sourceTeam = BattleManager.Instance?.GetTeamFor(source);
                if (sourceTeam != null) AddDamageCandidates(candidates, sourceTeam.GetStatusList(), sourceTeam, damageEvent, dm, true, ref sequence);
            }

            BattleEntity target = damageEvent.Target;
            AddDamageCandidates(candidates, target.GetStatusList(), target, damageEvent, dm, false, ref sequence);
            if (target.Position != null)
                AddDamageCandidates(candidates, target.Position.StatusList, target.Position, damageEvent, dm, false, ref sequence);
            var targetTeam = BattleManager.Instance?.GetTeamFor(target);
            if (targetTeam != null) AddDamageCandidates(candidates, targetTeam.GetStatusList(), targetTeam, damageEvent, dm, false, ref sequence);
        }

        candidates.Sort((left, right) =>
        {
            int order = left.Status.ApplyOrder.CompareTo(right.Status.ApplyOrder);
            return order != 0 ? order : left.Sequence.CompareTo(right.Sequence);
        });
        foreach (DamageHookCandidate candidate in candidates)
            ExecuteDamageCandidate(candidate);
    }

    static void AddDamageCandidates(
        List<DamageHookCandidate> candidates,
        IEnumerable<StatusInstance> statuses,
        object host,
        DamageResolvedEvent damageEvent,
        DataManager dm,
        bool outgoing,
        ref long sequence)
    {
        if (statuses == null) return;
        foreach (StatusInstance status in new List<StatusInstance>(statuses))
        {
            if (!CanExecute(status)
                || !dm.StatusActionDict.TryGetValue(status.StatusID2, out List<StatusActionData> actions))
                continue;

            foreach (StatusActionData action in actions)
            {
                if (action.ActionType != "OnTrigger" || ScriptHookEvaluator.IsEventDriven(action.ScriptHook))
                    continue;
                bool matched = outgoing
                    ? (damageEvent.HitLanded && MatchesDamageFilter(action.OutgoingHit, damageEvent, dm))
                      || (damageEvent.ActualHPDamage > 0f && MatchesDamageFilter(action.OutgoingDamage, damageEvent, dm))
                    : (damageEvent.HitLanded && MatchesDamageFilter(action.OnHit, damageEvent, dm))
                      || (damageEvent.ActualHPDamage > 0f && MatchesDamageFilter(action.OnDamage, damageEvent, dm));
                if (!matched || ContainsDamageCandidate(candidates, status, host, action, damageEvent)) continue;

                candidates.Add(new DamageHookCandidate
                {
                    Status = status,
                    Host = host,
                    Action = action,
                    DamageEvent = damageEvent,
                    Sequence = sequence++
                });
            }
        }
    }

    /// <summary>
    /// 非伤害生命消耗在正式扣血后提交一次此通知。伤害路径已在NotifyDamageBatch接入，不得重复通知。
    /// 复用原候选收集与ApplyOrder，不直接修改Count，不增加结算阶段。
    /// </summary>
    public static void NotifyHPDecrease(HPDecreaseEvent change)
    {
        if (change == null) return;
        using var collection = BattleResolutionContext.BeginCollection();
        using var declaration = BattleEventHub.Enter(BattleEventHub.Publish(new HPChangedEventData(
            change.Target, change.HPBefore, change.HPAfter, "HPCost"), change));
        DataManager dm = DataManager.Instance;
        if (dm != null) CollectHPDecrease(change, dm, null);
    }

    static void CollectHPDecrease(HPDecreaseEvent change, DataManager dm, DamageResolvedEvent damage, BattleDeclaration declaration = null)
    {
        // 保存变化发生时已存在的状态候选；不能让同层响应新建的潜光追溯获得本次计数。
        StatusHostAccess.VisitEntity(change.Target, (statuses, host) =>
        {
          foreach (StatusInstance status in statuses)
          {
            if (!CanExecute(status) || !dm.StatusActionDict.TryGetValue(status.StatusID2, out var actions)) continue;
            foreach (StatusActionData action in actions)
            {
                if (action.ActionType != "OnTrigger"
                    || !ScriptHookParser.TryFind(action.ScriptHook, "HPDecreased", out string argument)
                    || !ScriptHookEvaluator.MatchesSwitchActor(argument, status, change.Target)) continue;
                ExecuteEvent(status, action, host, new ScriptHookContext
                {
                    Caster = status.Caster, Target = change.Target, Status = status, StatusHost = host,
                    HPDecreaseEvent = change, DamageEvent = damage,
                    Declaration = declaration ?? BattleEventHub.Find(change) ?? BattleEventHub.Current,
                    HitEffectID = damage?.Source?.SourceEffectID,
                    EventHookName = "HPDecreased", EventHookArgument = argument
                });
            }
          }
        });
    }

    static bool ContainsDamageCandidate(
        IEnumerable<DamageHookCandidate> candidates,
        StatusInstance status,
        object host,
        StatusActionData action,
        DamageResolvedEvent damageEvent)
    {
        foreach (DamageHookCandidate candidate in candidates)
            if (candidate.Status == status
                && ReferenceEquals(candidate.Host, host)
                && candidate.Action == action
                && candidate.DamageEvent == damageEvent)
                return true;
        return false;
    }

    static void ExecuteDamageCandidate(DamageHookCandidate candidate)
    {
        if (candidate == null
            || !CanExecute(candidate.Status)
            || !IsStillHosted(candidate.Status, candidate.Host)
            || candidate.DamageEvent == null)
            return;
        DamageResolvedEvent damageEvent = candidate.DamageEvent;
        ExecuteEvent(candidate.Status, candidate.Action, candidate.Host, new ScriptHookContext
        {
            Caster = candidate.Status.Caster,
            Target = damageEvent.Target,
            Status = candidate.Status,
            StatusHost = candidate.Host,
            HitEffectID = damageEvent.Source != null ? damageEvent.Source.SourceEffectID : null,
            DamageEvent = damageEvent,
            ActionTargetPositions = BattleManager.Instance != null
                ? BattleManager.Instance.PendingActionTargetPositions
                : null
        });
    }

    static bool IsStillHosted(StatusInstance status, object host)
    {
        return StatusHostAccess.Contains(host, status);
    }

    static void RecordDamageEffectHit(DamageResolvedEvent damageEvent)
    {
        if (DamageEffectFrames.Count == 0 || damageEvent?.Source == null) return;
        DamageEffectFrame frame = DamageEffectFrames[DamageEffectFrames.Count - 1];
        if (frame.Source != damageEvent.Source.SourceEntity) return;
        if (frame.SourceEffectID != damageEvent.Source.SourceEffectID) return;
        if (damageEvent.Source.SourceKind != ReactionSourceKind.CharacterSkill) return;
        frame.Events.Add(damageEvent);
    }

    static void EndDamageEffect(DamageEffectFrame frame)
    {
        using var resolutionEffect = frame?.ResolutionScope;
        using var collection = BattleResolutionContext.BeginCollection();
        if (frame == null) return;
        int index = DamageEffectFrames.LastIndexOf(frame);
        if (index >= 0) DamageEffectFrames.RemoveAt(index);
        BattleSpecialRules.CompleteDamageEffect(frame.Source, frame.SourceEffectID);
        if (!frame.IsActiveAction
            || frame.Source == null
            || frame.Source.Side != BattleSide.Ally
            || frame.Events.Count == 0)
            return;

        DataManager dm = DataManager.Instance;
        if (dm == null) return;
        var candidates = new List<PostDamageHookCandidate>();

        // Self：只检查主动行为角色自身及其所在场地的状态。
        AddPostDamageCandidates(candidates, frame.Source.GetStatusList(), frame.Source, "PostSelfDamage", dm);
        if (frame.Source.Position != null)
            AddPostDamageCandidates(candidates, frame.Source.Position.StatusList, frame.Source.Position, "PostSelfDamage", dm);
        var sourceTeam = BattleManager.Instance?.GetTeamFor(frame.Source);
        if (sourceTeam != null)
            foreach (var status in sourceTeam.GetStatusList())
                if (status.Caster == frame.Source)
                    AddPostDamageCandidates(candidates, new[] { status }, sourceTeam, "PostSelfDamage", dm);

        // Allies：检查我方所有角色和我方场地状态。
        BattleManager battle = BattleManager.Instance;
        if (battle != null)
        {
            AddPostDamageCandidates(candidates, battle.AllyTeam.GetStatusList(), battle.AllyTeam, "PostAlliesDamage", dm);
            foreach (CharacterBattleController ally in battle.Allies)
            {
                BattleEntity entity = ally != null ? ally.Entity : null;
                if (entity != null)
                    AddPostDamageCandidates(candidates, entity.GetStatusList(), entity, "PostAlliesDamage", dm);
            }
            if (battle.Field != null)
            {
                foreach (FieldPosition field in battle.Field.AllySlots)
                    if (field != null)
                        AddPostDamageCandidates(candidates, field.StatusList, field, "PostAlliesDamage", dm);
            }
        }

        candidates.Sort((left, right) => left.Status.ApplyOrder.CompareTo(right.Status.ApplyOrder));
        foreach (PostDamageHookCandidate candidate in candidates)
            NotifyPostDamageForStatus(
                candidate.Status,
                candidate.Host,
                candidate.EventHookName,
                frame,
                dm);
    }

    static void AddPostDamageCandidates(
        List<PostDamageHookCandidate> candidates,
        IEnumerable<StatusInstance> statuses,
        object host,
        string eventHookName,
        DataManager dm)
    {
        if (statuses == null) return;
        foreach (StatusInstance status in new List<StatusInstance>(statuses))
        {
            if (!CanExecute(status)
                || !dm.StatusActionDict.TryGetValue(status.StatusID2, out List<StatusActionData> actions))
                continue;
            bool hasHook = false;
            foreach (StatusActionData action in actions)
            {
                if (action.ActionType == "OnTrigger"
                    && ScriptHookParser.TryFind(action.ScriptHook, eventHookName, out _))
                {
                    hasHook = true;
                    break;
                }
            }
            if (!hasHook) continue;

            bool duplicate = false;
            foreach (PostDamageHookCandidate candidate in candidates)
            {
                if (candidate.Status == status && candidate.EventHookName == eventHookName)
                {
                    duplicate = true;
                    break;
                }
            }
            if (!duplicate)
            {
                candidates.Add(new PostDamageHookCandidate
                {
                    Status = status,
                    Host = host,
                    EventHookName = eventHookName
                });
            }
        }
    }

    static void NotifyPostDamageForStatus(
        StatusInstance status,
        object host,
        string eventHookName,
        DamageEffectFrame frame,
        DataManager dm)
    {
        if (!CanExecute(status) || WeaponDamageCompletionHooks.OwnsPostDamage(status)) return;
        if (!dm.StatusActionDict.TryGetValue(status.StatusID2, out List<StatusActionData> actions)) return;

        foreach (StatusActionData action in actions)
        {
            if (action.ActionType != "OnTrigger"
                || !ScriptHookParser.TryFind(action.ScriptHook, eventHookName, out string argument))
                continue;

            var matchedEvents = new List<DamageResolvedEvent>();
            bool hasHitFilter = !string.IsNullOrWhiteSpace(action.OutgoingHit);
            bool hasDamageFilter = !string.IsNullOrWhiteSpace(action.OutgoingDamage);
            foreach (DamageResolvedEvent damageEvent in frame.Events)
            {
                bool matched = (!hasHitFilter && !hasDamageFilter && damageEvent.HitLanded)
                    || (damageEvent.HitLanded
                                && MatchesDamageFilter(action.OutgoingHit, damageEvent, dm))
                    || (damageEvent.ActualHPDamage > 0f
                        && MatchesDamageFilter(action.OutgoingDamage, damageEvent, dm));
                if (matched) matchedEvents.Add(damageEvent);
            }
            if (matchedEvents.Count == 0) continue;

            var positions = new List<int>();
            foreach (DamageResolvedEvent damageEvent in matchedEvents)
            {
                int position = damageEvent.Target != null ? damageEvent.Target.SlotPosition : -1;
                if (position > 0 && !positions.Contains(position)) positions.Add(position);
            }
            if (positions.Count == 0) continue;

            PostDamageHookSystem.SetPositions(argument, positions);
            DamageResolvedEvent lastEvent = matchedEvents[matchedEvents.Count - 1];
            if (!WeaponPostDamageGate.Allow(status, action, lastEvent)) continue;
            ExecuteEvent(status, action, host, new ScriptHookContext
            {
                Caster = status.Caster,
                Target = lastEvent.Target,
                Status = status,
                StatusHost = host,
                HitEffectID = frame.SourceEffectID,
                DamageEvent = lastEvent,
                EventHookName = eventHookName,
                EventHookArgument = argument,
                ActionTargetPositions = positions
            });
        }
    }

    /// <summary>
    /// 兼容已提交的单目标治疗通知；正式治疗入口使用 HealBatch 先记录后提交。
    /// OnHealFrom 属于被治疗者（筛选来源状态）；WhenHealing 属于治疗者（筛选目标状态）。
    /// </summary>
    public static void NotifyHeal(
        BattleEntity source,
        BattleEntity target,
        float requestedAmount,
        float actualAmount)
    {
        if (target == null || requestedAmount <= 0f) return;
        var heal = new HealResolvedEvent(source, target, requestedAmount,
            target.CurrentHP - actualAmount, target.CurrentHP, target.GetFinalHP(), null, 1, 1);
        heal.MarkCommitted();
        using (BattleResolutionContext.BeginCollection())
        {
            BattleEventHub.Publish(new HealEventData(heal), heal);
            CollectHeal(heal);
        }
    }

    /// <summary>只记录候选；由 HealBatch 的收集作用域在全部 HP 提交后统一确认。</summary>
    internal static void CollectHeal(HealResolvedEvent heal)
    {
        if (heal == null || heal.Target == null || heal.RequestedAmount <= 0f) return;
        BattleEventHub.Record(heal, new HealEventData(heal), targetIndex: heal.TargetIndex);
        BattleEntity source = heal.Source;
        BattleEntity target = heal.Target;
        DataManager dm = DataManager.Instance;
        if (dm == null) return;

        var visited = new HashSet<object>();
        var sourceTeam = BattleManager.Instance?.GetTeamFor(source);
        StatusHostAccess.VisitEntity(target, (statuses, host) =>
        {
            if (!visited.Add(host)) return;
            bool performed = source == target || host is BattleTeamStatusHost team && team == sourceTeam;
            foreach (var status in statuses) NotifyHealForStatus(status, host, heal, true, performed, dm);
        });
        StatusHostAccess.VisitEntity(source, (statuses, host) =>
        {
            if (!visited.Add(host)) return;
            foreach (var status in statuses) NotifyHealForStatus(status, host, heal, false, true, dm);
        });
    }

    /// <summary>我方角色实际消耗 AP 后派发全局 APUsed。</summary>
    public static void NotifyAPUsed(BattleEntity spender, int amount,
        APSpendKind spendKind = APSpendKind.Unspecified, string sourceSkillID = null)
    {
        using var collection = BattleResolutionContext.BeginCollection();
        if (spender == null || spender.Side != BattleSide.Ally || amount <= 0) return;
        int after = BattleManager.Instance?.APManager?.CurrentAP ?? 0;
        // 独立调用方没有全局池时仅声明已知的消耗量，不伪造池前后余额。
        using var declaration = BattleEventHub.Enter(BattleEventHub.Publish(
            new APSpentEventData(spender, amount, BattleManager.Instance?.APManager != null ? (int?)after : null,
                spendKind, sourceSkillID)));
        DataManager dm = DataManager.Instance;
        if (dm == null) return;

        BattleManager bm = BattleManager.Instance;
        if (bm == null)
        {
            foreach (StatusInstance status in new List<StatusInstance>(spender.GetStatusList()))
                NotifyAPUsedForStatus(status, spender, spender, dm);
            if (spender.Position != null)
            {
                foreach (StatusInstance status in new List<StatusInstance>(spender.Position.StatusList))
                    NotifyAPUsedForStatus(status, spender.Position, spender, dm);
            }
            return;
        }

        var visited = new HashSet<StatusInstance>();
        foreach (var team in new[] { bm.AllyTeam, bm.EnemyTeam })
            foreach (var status in team.GetStatusList())
                if (visited.Add(status)) NotifyAPUsedForStatus(status, team, spender, dm);
        foreach (CharacterBattleController ally in bm.Allies)
        {
            BattleEntity owner = ally != null ? ally.Entity : null;
            if (owner == null) continue;
            foreach (StatusInstance status in new List<StatusInstance>(owner.GetStatusList()))
                if (visited.Add(status)) NotifyAPUsedForStatus(status, owner, spender, dm);
        }
        foreach (EnemyBattleController enemy in bm.Enemies)
        {
            BattleEntity owner = enemy != null ? enemy.Entity : null;
            if (owner == null) continue;
            foreach (StatusInstance status in new List<StatusInstance>(owner.GetStatusList()))
                if (visited.Add(status)) NotifyAPUsedForStatus(status, owner, spender, dm);
        }
        if (bm.Field == null) return;
        NotifyAPUsedForFields(bm.Field.AllySlots, spender, dm, visited);
        NotifyAPUsedForFields(bm.Field.EnemySlots, spender, dm, visited);
    }

    /// <summary>派发已生成的元素反应声明；同一 ReactionOccurrence 只派发一次。</summary>
    public static void NotifyReactions(IEnumerable<ReactionOccurrence> occurrences)
    {
        using var collection = BattleResolutionContext.BeginCollection();
        if (occurrences == null) return;
        DataManager dm = DataManager.Instance;

        foreach (ReactionOccurrence occurrence in occurrences)
        {
            if (occurrence == null || NotifiedReactions.TryGetValue(occurrence, out _)) continue;
            NotifiedReactions.Add(occurrence, ReactionNotifiedMarker);
            occurrence.Declaration = BattleEventHub.Publish(new ReactionEventData(occurrence), occurrence);
            using var declaration = BattleEventHub.Enter(occurrence.Declaration);
            // 沿原反应派发边界提交资源；先有真实反应身份，整批候选收集完成后才执行响应。
            LunarBloomReactionHandler.GrantDew(occurrence);
            BattleExecution.Record(occurrence);
            if (dm == null) continue;
            BattleEntity source = occurrence.SourceEntity;
            if (source == null || occurrence.Type == ReactionType.None) continue;

            StatusHostAccess.VisitEntity(source, (statuses, host) =>
            {
                foreach (var status in statuses) NotifyReactionForStatus(status, host, occurrence, dm);
            });
        }
    }

    static void NotifyPlayerActionForStatus(
        StatusInstance status,
        object host,
        BattleEntity actor,
        SkillMainData skill,
        DataManager dm)
    {
        if (!CanExecute(status)) return;
        if (!dm.StatusActionDict.TryGetValue(status.StatusID2, out List<StatusActionData> actions)) return;

        foreach (StatusActionData action in actions)
        {
            if (action.ActionType != "OnTrigger" || !MatchesPlayerAction(action.OnAction, skill)) continue;
            ExecuteEvent(status, action, host, new ScriptHookContext
            {
                Caster = status.Caster,
                Target = actor,
                Status = status,
                StatusHost = host,
                ActionTargetPositions = BattleManager.Instance != null
                    ? BattleManager.Instance.PendingActionTargetPositions
                    : null
            });
        }
    }

    static void NotifyHealForStatus(
        StatusInstance status,
        object host,
        HealResolvedEvent heal,
        bool receivedHeal,
        bool performedHeal,
        DataManager dm)
    {
        BattleEntity source = heal.Source;
        BattleEntity target = heal.Target;
        if (!CanExecute(status)) return;
        if (!dm.StatusActionDict.TryGetValue(status.StatusID2, out List<StatusActionData> actions)) return;

        foreach (StatusActionData action in actions)
        {
            if (action.ActionType != "OnTrigger") continue;
            bool matched = (receivedHeal && MatchesRelatedStatus(action.OnHealFrom, source))
                || (performedHeal && MatchesRelatedStatus(action.WhenHealing, target));
            if (!matched) continue;

            ExecuteEvent(status, action, host, new ScriptHookContext
            {
                Caster = status.Caster,
                Target = target,
                HealEvent = heal,
                Status = status,
                StatusHost = host,
                ActionTargetPositions = BattleManager.Instance != null
                    ? BattleManager.Instance.PendingActionTargetPositions
                    : null
            });
        }
    }

    static void NotifyAPUsedForFields(
        IEnumerable<FieldPosition> fields,
        BattleEntity spender,
        DataManager dm,
        HashSet<StatusInstance> visited)
    {
        if (fields == null) return;
        foreach (FieldPosition field in fields)
        {
            if (field == null) continue;
            foreach (StatusInstance status in new List<StatusInstance>(field.StatusList))
                if (visited.Add(status)) NotifyAPUsedForStatus(status, field, spender, dm);
        }
    }

    static void NotifyAPUsedForStatus(
        StatusInstance status,
        object host,
        BattleEntity spender,
        DataManager dm)
    {
        if (!CanExecute(status)) return;
        if (!dm.StatusActionDict.TryGetValue(status.StatusID2, out List<StatusActionData> actions)) return;

        foreach (StatusActionData action in actions)
        {
            if (action.ActionType != "OnTrigger" || !MatchesRelatedStatus(action.APUsed, spender)) continue;
            ExecuteEvent(status, action, host, new ScriptHookContext
            {
                Declaration = BattleEventHub.Current,
                Caster = status.Caster,
                Target = spender,
                Status = status,
                StatusHost = host,
                ActionTargetPositions = BattleManager.Instance != null
                    ? BattleManager.Instance.PendingActionTargetPositions
                    : null
            });
        }
    }

    static void NotifyReactionForStatus(
        StatusInstance status,
        object host,
        ReactionOccurrence occurrence,
        DataManager dm)
    {
        if (!CanExecute(status)) return;
        if (!dm.StatusActionDict.TryGetValue(status.StatusID2, out List<StatusActionData> actions)) return;

        foreach (StatusActionData action in actions)
        {
            if (action.ActionType != "OnTrigger"
                || !MatchesReactionFilter(action.ReactionTriggered, occurrence)) continue;
            ExecuteEvent(status, action, host, new ScriptHookContext
            {
                Caster = status.Caster,
                Target = occurrence.Target,
                ReactionEvent = occurrence,
                Status = status,
                StatusHost = host,
                HitEffectID = occurrence.SourceEffectID,
                ActionTargetPositions = BattleManager.Instance != null
                    ? BattleManager.Instance.PendingActionTargetPositions
                    : null
            });
        }
    }

    static bool CanExecute(StatusInstance status)
    {
        return status != null && status.IsActive && status.Caster != null
            && (status.Caster.CharacterCtrl != null || status.Caster.EnemyCtrl != null);
    }

    /// <summary>新声明钩子统一捕获具体实例；旧 OnHit/APUsed 等仍由各自适配器分发，不重复执行。</summary>
    internal static Action CaptureDeclaration(BattleDeclaration declaration)
    {
        var dm = DataManager.Instance;
        if (dm == null || declaration == null) return null;
        var responses = new List<Action>();
        var visited = new HashSet<StatusInstance>();
        void Collect(IEnumerable<StatusInstance> statuses, object host)
        {
            foreach (StatusInstance status in new List<StatusInstance>(statuses))
            {
                if (!visited.Add(status) || !CanExecute(status)
                    || !dm.StatusActionDict.TryGetValue(status.StatusID2, out var actions)) continue;
                foreach (StatusActionData action in actions)
                {
                    if (action.ActionType != "OnTrigger"
                        || !ScriptHookParser.TryParseAll(action.ScriptHook, out var calls)) continue;
                    foreach (var call in calls)
                    {
                        if (!ScriptHookEvaluator.IsDeclarationHook(call.FunctionName)
                            || !ScriptHookEvaluator.MatchesDeclaration(call.FunctionName, call.Argument, status, declaration)) continue;
                        var context = new ScriptHookContext {
                            Caster = status.Caster, Target = declaration.Data.Subject, Status = status,
                            StatusHost = host, Declaration = declaration,
                            EventHookName = call.FunctionName, EventHookArgument = call.Argument };
                        responses.Add(() => ExecuteEvent(status, action, host, context));
                        break; // 一条行动只提交一次；全部条件仍在原确认阶段求值一次。
                    }
                }
            }
        }
        BattleManager battle = BattleManager.Instance;
        if (battle != null)
        {
            StatusHostAccess.VisitBattle(battle, Collect);
        }
        BattleEntity subject = declaration.Data.Subject;
        if (subject != null)
        {
            Collect(subject.GetStatusList(), subject);
            if (subject.Position != null) Collect(subject.Position.StatusList, subject.Position);
        }
        return () => { foreach (var response in responses) response(); };
    }

    static void ExecuteEvent(
        StatusInstance status,
        StatusActionData action,
        object host,
        ScriptHookContext context)
    {
        if (status.Caster.CharacterCtrl != null)
            status.Caster.CharacterCtrl.TryExecuteEventStatusAction(status, action, host, context,
                () => CanExecute(status) && IsStillHosted(status, host));
        else status.Caster.EnemyCtrl.TryExecuteEventStatusAction(status, action, host, context,
                () => CanExecute(status) && IsStillHosted(status, host));
    }

    static bool MatchesPlayerAction(string configured, SkillMainData skill)
    {
        if (string.IsNullOrWhiteSpace(configured) || skill == null) return false;
        foreach (string raw in configured.Split(';'))
        {
            string token = raw.Trim();
            if (token.Length == 0) continue;
            if (string.Equals(token, "All", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(token, skill.ActionType, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(token, skill.SkillID2, StringComparison.OrdinalIgnoreCase)) return true;
            if (int.TryParse(token, out int skillID) && skillID == skill.SkillID) return true;
        }
        return false;
    }

    static bool MatchesDamageFilter(string configured, DamageResolvedEvent damageEvent, DataManager dm)
    {
        if (string.IsNullOrWhiteSpace(configured) || damageEvent == null || damageEvent.Source == null)
            return false;

        GetDamageMetadata(damageEvent.Source, dm,
            out int effectID, out string damageType, out string element);
        // 动态转化不能按共享配表的占位元素分类；事件中的实际提交结果优先。
        if (!string.IsNullOrEmpty(damageEvent.Element)) element = damageEvent.Element;
        if (!string.IsNullOrEmpty(damageEvent.DamageType)) damageType = damageEvent.DamageType;
        string reaction = ReactionDamageCalculator.GetConfiguredReactionName(damageEvent.Source.ReactionType);
        bool isElement = !string.IsNullOrEmpty(element)
            && !string.Equals(element, "None", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(element, "Physical", StringComparison.OrdinalIgnoreCase);

        foreach (string raw in configured.Split(';'))
        {
            string token = raw.Trim();
            if (token.Length == 0) continue;
            if (string.Equals(token, "All", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(token, "Element", StringComparison.OrdinalIgnoreCase) && isElement) return true;
            if (string.Equals(token, damageEvent.Source.SourceEffectID, StringComparison.OrdinalIgnoreCase)) return true;
            if (int.TryParse(token, out int id) && id == effectID) return true;
            if (string.Equals(token, damageType, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(token, element, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(reaction) && token == reaction) return true;
        }
        return false;
    }

    static bool MatchesRelatedStatus(string configured, BattleEntity relatedEntity)
    {
        if (string.IsNullOrWhiteSpace(configured)) return false;
        foreach (string raw in configured.Split(';'))
        {
            string token = raw.Trim();
            if (token.Length == 0) continue;
            if (string.Equals(token, "All", StringComparison.OrdinalIgnoreCase)) return true;
            if (relatedEntity == null) continue;

            foreach (StatusInstance status in relatedEntity.GetStatusList())
                if (MatchesStatusID(token, status)) return true;
            if (relatedEntity.Position == null) continue;
            foreach (StatusInstance status in relatedEntity.Position.StatusList)
                if (MatchesStatusID(token, status)) return true;
        }
        return false;
    }

    static bool MatchesStatusID(string token, StatusInstance status)
    {
        if (status == null || !status.IsActive) return false;
        if (string.Equals(token, status.StatusID2, StringComparison.OrdinalIgnoreCase)) return true;
        return int.TryParse(token, out int statusID)
            && status.MainData != null
            && status.MainData.StatusID == statusID;
    }

    static bool MatchesReactionFilter(string configured, ReactionOccurrence occurrence)
    {
        return occurrence != null
            && ReactionFilterMatcher.Matches(
                configured,
                occurrence.Type,
                occurrence.DisplayName,
                occurrence.InvolvedElements);
    }

    static void GetDamageMetadata(
        DamageSourceInfo source,
        DataManager dm,
        out int effectID,
        out string damageType,
        out string element)
    {
        effectID = 0;
        damageType = string.Empty;
        element = string.Empty;
        if (source == null || dm == null || string.IsNullOrEmpty(source.SourceEffectID)) return;

        if (dm.SkillEffectDict.TryGetValue(source.SourceEffectID, out SkillEffectData skillEffect))
        {
            effectID = skillEffect.SkillEffectID;
            damageType = skillEffect.DamageType;
            element = skillEffect.Element;
            return;
        }
        if (dm.StatusEffectDict.TryGetValue(source.SourceEffectID, out StatusEffectData statusEffect))
        {
            effectID = statusEffect.StatusEffectID;
            damageType = statusEffect.DamageType;
            element = statusEffect.Element;
            return;
        }
        if (dm.EnemySkillEffectDict.TryGetValue(source.SourceEffectID, out EnemySkillEffectData enemyEffect))
        {
            effectID = enemyEffect.SkillEffectID;
            damageType = enemyEffect.DamageType;
            element = enemyEffect.Element;
        }
    }
}
