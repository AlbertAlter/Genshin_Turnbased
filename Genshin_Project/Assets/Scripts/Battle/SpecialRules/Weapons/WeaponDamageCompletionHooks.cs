using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>3171/3176/3182的完整Damage边界；只索引已提交事实，沿原行动响应入口结算。</summary>
internal static class WeaponDamageCompletionHooks
{
    internal const string Pride = "ST_Weapon_4317103", Verdict = "ST_Weapon_4317603", Harp = "ST_Weapon_4318202";
    private static readonly AsyncLocal<Frame> Current = new AsyncLocal<Frame>();
    private static readonly AsyncLocal<HitFacts> Hit = new AsyncLocal<HitFacts>();
    private static readonly ConditionalWeakTable<BattleDeclaration, Frame> Completed = new ConditionalWeakTable<BattleDeclaration, Frame>();
    private static readonly ConditionalWeakTable<BattleDeclaration, HitFacts> HitTargets = new ConditionalWeakTable<BattleDeclaration, HitFacts>();
    internal sealed class Window
    {
        internal long ArmedBy;
        internal readonly HashSet<long> Effects = new HashSet<long>();
    }
    private sealed class Frame : IDisposable
    {
        internal Frame Previous;
        internal BattleEntity Owner;
        internal string Effect;
        internal long Execution;
        internal bool Closed;
        internal readonly List<DamageResolvedEvent> Events = new List<DamageResolvedEvent>();
        internal readonly Dictionary<StatusInstance, object> Windows = new Dictionary<StatusInstance, object>();
        public void Dispose()
        {
            try { Complete(this); }
            finally { Current.Value = Previous; }
        }
    }
    private sealed class HitFacts : IDisposable
    {
        internal HitFacts Previous;
        internal BattleEntity Owner;
        internal long ID;
        internal StatusInstance Count;
        internal object Generation;
        internal readonly List<BattleEntity> Targets = new List<BattleEntity>();
        public void Dispose() { Hit.Value = Previous; }
    }
    internal static bool OwnsPostDamage(StatusInstance status) => status?.StatusID2 == Verdict || status?.StatusID2 == Harp
        || status?.StatusID2 == CinnabarWeaponHooks.ID;
    internal static bool CurrentWindow(StatusInstance status, BattleEntity owner, object window)
        => status != null && status.IsActive && owner.HasStatusInstance(status) && EquipmentStatusLifetime.CanExecute(status)
            && ReferenceEquals(status.CountRuntimeState, window);
    internal static void Register()
    {
        BattleSpecialRules.RegisterDamageEffectScope((owner, effect, execution) =>
        {
            var frame = new Frame { Previous = Current.Value, Owner = owner, Effect = effect, Execution = execution };
            Current.Value = frame; return frame;
        }, (owner, effect) =>
        {
            var frame = Current.Value;
            if (frame != null && frame.Owner == owner && frame.Effect == effect) Complete(frame);
        });
        BattleSpecialRules.RegisterHitScope((id, owner, targets) =>
        {
            var count = owner?.GetStatus(Pride);
            var facts = new HitFacts { Previous = Hit.Value, Owner = owner, ID = id, Count = count, Generation = count?.CountRuntimeState };
            Hit.Value = facts; return facts;
        });
        BattleSpecialRules.RegisterDeclarationObserver(Observe);
        BattleSpecialRules.RegisterNumberedCondition("4317103", (_, c) => ValidPride(c));
        BattleSpecialRules.RegisterTargets("Hook(431710301)", (battle, owner) =>
            GetPrideTargets(battle, owner, ScriptHookContext.Current));
        BattleSpecialRules.RegisterNumberedCondition("4318202", (_, c) => HarpChance(c));
    }
    private static DamageResolvedEvent Snapshot(BattleDeclaration declaration, DamageEventData data)
        => new DamageResolvedEvent { Declaration = declaration, Target = data.Subject, Source = data.Source,
            SourceWasPartyMember = data.SourceWasPartyMember, SourceWasOnField = data.SourceWasOnField,
            HitLanded = data.HitLanded, ActualHPDamage = data.ActualHPDamage, HPBefore = data.HPBefore, HPAfter = data.HPAfter,
            RequestedHPDamage = data.RequestedDamage, DamageBeforeShield = data.DamageBeforeShield, Critical = data.Critical,
            ShieldAbsorbed = data.ShieldAbsorbed, IsImmune = data.IsImmune, ImmuneDamage = data.ImmuneDamage,
            DamageType = data.DamageType, Element = data.Element, ElementAmount = data.ElementAmount, Properties = data.Properties,
            Resonance = data.Resonance,
            OverallShieldProtections = data.OverallShieldProtections,
            IsNightSoulDamage = data.IsNightSoulDamage, CausedDeath = data.CausedDeath, HitIndex = data.HitIndex,
            RuntimeHitID = declaration.Header.HitId, TargetIndex = declaration.Header.TargetIndex };
    private static void Observe(BattleDeclaration declaration)
    {
        var data = declaration.Get<DamageEventData>();
        if (data == null || !data.HitLanded || !data.Source.CountsAsHit || data.Source.SourceKind == ReactionSourceKind.DerivedReaction) return;
        var hit = Hit.Value;
        if (hit != null && hit.ID == declaration.Header.HitId && hit.Owner == data.Source.SourceEntity
            && data.Subject != null && data.Subject.Side != hit.Owner.Side)
        {
            if (!hit.Targets.Contains(data.Subject)) hit.Targets.Add(data.Subject);
            HitTargets.Add(declaration, hit); // 后续目标补入同一个真实hit池；响应读取时该批目标已提交。
        }
        var frame = Current.Value;
        if (frame == null || frame.Closed || frame.Owner != data.Source.SourceEntity || frame.Effect != data.Source.SourceEffectID
            || frame.Execution != declaration.Header.EffectExecutionId || data.Subject == null || data.Subject.Side == frame.Owner.Side) return;
        frame.Events.Add(Snapshot(declaration, data));
        foreach (string id in new[] { Pride, Verdict, Harp })
        {
            var count = frame.Owner.GetStatus(id);
            if (count == null || !count.IsActive || !EquipmentStatusLifetime.CanExecute(count)) continue;
            if (id == Pride && (frame.Effect != "STE_Weapon_431710301" || data.Source.SourceStatus != count)) continue;
            if (id == Verdict && (data.DamageType != "Skill" || data.ActualHPDamage <= 0f)) continue;
            if (id == Harp && frame.Effect == "STE_Weapon_431820202") continue;
            frame.Windows[count] = count.CountRuntimeState;
        }
    }
    private static List<int> LivePositions(BattleManager battle, BattleEntity owner, IEnumerable<BattleEntity> targets)
    {
        var positions = new List<int>();
        foreach (var target in targets)
            if (target != null && target.IsAlive && target.Side != owner.Side && target.SlotPosition > 0
                && ReferenceEquals(battle?.GetEntityByPosition(target.Side, target.SlotPosition), target)
                && !positions.Contains(target.SlotPosition)) positions.Add(target.SlotPosition);
        return positions;
    }
    private static bool ValidPride(ScriptHookContext c)
        => c?.Status?.StatusID2 == Pride && c.Status.RuntimeValue > 0f && c.Declaration != null
            && HitTargets.TryGetValue(c.Declaration, out var hit) && hit.Count == c.Status
            && CurrentWindow(c.Status, hit.Owner, hit.Generation)
            && GetPrideTargets(BattleManager.Instance, hit.Owner, c).Count > 0;
    private static List<int> GetPrideTargets(BattleManager battle, BattleEntity owner, ScriptHookContext c)
    {
        if (c?.Declaration == null || !HitTargets.TryGetValue(c.Declaration, out var hit) || hit.Owner != owner
            || c.Status != hit.Count || !CurrentWindow(hit.Count, owner, hit.Generation)) return new List<int>();
        return LivePositions(battle, owner, hit.Targets);
    }
    internal static bool CanExecutePride(StatusInstance status)
    {
        var c = ScriptHookContext.Current;
        return c?.Status == status && ValidPride(c);
    }
    private static bool HarpChance(ScriptHookContext c)
    {
        var status = c?.Status;
        if (status?.StatusID2 != Harp || status.RuntimeValue != 0f || c.EventHookName != "PostSelfDamage"
            || c.Declaration == null || !Completed.TryGetValue(c.Declaration, out var frame)
            || !frame.Windows.TryGetValue(status, out var window) || !CurrentWindow(status, frame.Owner, window)) return false;
        foreach (var runtime in status.ActionRuntime.Values) if (runtime.PendingTriggerCount > 0) return false;
        var targets = new List<BattleEntity>(); foreach (var e in frame.Events) targets.Add(e.Target);
        if (LivePositions(BattleManager.Instance, frame.Owner, targets).Count == 0) return false;
        float chance = WeaponHooks.Param(status, 2); float roll = BattleRandom.NextFloat01();
        bool success = roll < chance;
        LogManager.Log(LogCategory.ScriptHook, $"Hook(4318202) effectExecution={frame.Execution} rate={chance:R} roll={roll:R} success={success}");
        return success;
    }
    internal static void ArmHarp(StatusInstance status, BattleEntity owner)
    {
        var c = WeaponCountClock.RequireWindowSource(status);
        if (c.Declaration == null || !Completed.TryGetValue(c.Declaration, out var frame)
            || !frame.Windows.TryGetValue(status, out var window) || !CurrentWindow(status, owner, window))
            throw new InvalidOperationException("天空之翼间隔只能在本次完整攻击已确认成功的原表响应中启动。");
        float interval = WeaponHooks.Param(status, 3);
        if (interval < 1f || interval != Math.Floor(interval)) throw new InvalidOperationException("天空之翼间隔必须为正整数效果数。");
        status.RuntimeValue = interval; ((Window)window).ArmedBy = frame.Execution;
    }
    private static void Complete(Frame frame)
    {
        if (frame.Closed) return; frame.Closed = true;
        if (frame.Events.Count == 0) return;
        using var responses = BattleResolutionContext.BeginCollection();
        // 与原PostSelfDamage行一致：消费的是效果结束时仍存在的本来源约印，不要求首hit之前已有。
        foreach (var e in frame.Events)
            if (e.DamageType == "Skill" && e.ActualHPDamage > 0f)
            {
                var current = frame.Owner.GetStatus(Verdict);
                if (current != null && current.IsActive) frame.Windows[current] = current.CountRuntimeState;
                break;
            }
        var last = frame.Events[frame.Events.Count - 1];
        Completed.Add(last.Declaration, frame);
        var targets = new List<BattleEntity>(); foreach (var e in frame.Events) targets.Add(e.Target);
        foreach (var pair in frame.Windows)
        {
            var count = pair.Key;
            if (!CurrentWindow(count, frame.Owner, pair.Value) || !(pair.Value is Window window) || !window.Effects.Add(frame.Execution)) continue;
            if (count.StatusID2 == Pride)
            {
                count.RuntimeValue = Math.Max(0f, count.RuntimeValue - 1f);
                if (count.RuntimeValue == 0f) frame.Owner.RemoveStatusInstance(count, "SkywardPrideExhausted");
                continue;
            }
            if (count.StatusID2 == Harp && count.RuntimeValue > 0f && window.ArmedBy != frame.Execution)
                count.RuntimeValue -= 1f;
            if (count.StatusID2 == Harp && count.RuntimeValue > 0f) continue;
            if (BattleExecution.IsCancelled || BattleManager.Instance?.IsBattleRunning != true || BattleManager.Instance.IsBattleOver) continue;
            if (!DataManager.Instance.StatusActionDict.TryGetValue(count.StatusID2, out var actions)) continue;
            var positions = LivePositions(BattleManager.Instance, frame.Owner, targets);
            foreach (var action in actions)
            {
                if (action.ActionType != "OnTrigger" || !ScriptHookParser.TryFind(action.ScriptHook, "PostSelfDamage", out string argument)) continue;
                var context = new ScriptHookContext { Caster = frame.Owner, Status = count, StatusHost = frame.Owner,
                    Target = last.Target, Declaration = last.Declaration, DamageEvent = last, HitEffectID = frame.Effect,
                    EventHookName = "PostSelfDamage", EventHookArgument = argument, ActionTargetPositions = positions };
                frame.Owner.CharacterCtrl.TryExecuteEventStatusAction(count, action, frame.Owner, context,
                    () => CurrentWindow(count, frame.Owner, window));
            }
        }
    }
}

/// <summary>三个明确的整效果计数，不提供其他Count的默认初值/扣点策略。</summary>
internal sealed class WholeDamageWeaponCount : CountStatusHandler
{
    private readonly int weapon;
    internal WholeDamageWeaponCount(int id)
    {
        if (id != 3171 && id != 3176 && id != 3182) throw new ArgumentOutOfRangeException(nameof(id));
        weapon = id;
    }
    private void Reset(StatusInstance status, BattleEntity owner)
    {
        status.RuntimeValue = weapon == 3171 ? 8f : weapon == 3176 ? 1f : 0f;
        status.CountRuntimeState = new WeaponDamageCompletionHooks.Window();
        WeaponCountLifetime.Refresh(status, owner, weapon == 3171 ? 5 : weapon == 3176 ? 4 : 0, weapon == 3182 ? 2 : 0);
    }
    public override void OnCreated(StatusInstance status, object host) => Reset(status, WeaponCountLifetime.RequireOwner(status, host, weapon));
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        WeaponCountLifetime.RequireApplication(status, host, application, weapon);
        if (weapon == 3182) WeaponDamageCompletionHooks.ArmHarp(status, host);
        else if (weapon == 3171) Reset(status, host);
        else { status.RuntimeValue = Math.Min(2f, status.RuntimeValue + 1f); WeaponCountLifetime.Refresh(status, host, 4, 0); }
        return status;
    }
    public override void OnRemoved(StatusInstance status, object host) { status.RuntimeValue = 0f; status.CountRuntimeState = null; }
}

/// <summary>3176：一次真实结晶授盾批次只获1印；月结晶另按真实反应身份获印。</summary>
internal static class VerdictWeaponTriggers
{
    private sealed class State
    {
        internal IDisposable Shield, Reaction;
        internal readonly HashSet<long> Batches = new HashSet<long>(), Reactions = new HashSet<long>();
    }
    private static readonly ConditionalWeakTable<StatusInstance, State> States = new ConditionalWeakTable<StatusInstance, State>();
    private static bool Shield(StatusInstance status, ShieldGrantedEventData fact)
    {
        if (fact?.Shield.Kind != ShieldKind.Crystallize || fact.Shield.Value <= 0f
            || fact.Subject?.Side != status.WeaponContext.Equipper.Side) return false;
        var battle = BattleManager.Instance;
        if (battle != null) foreach (var ally in battle.Allies) if (ally?.Entity == fact.Subject) return true;
        return false;
    }
    private static bool Lunar(StatusInstance status, ReactionEventData fact)
        => fact != null && fact.Reaction == ReactionType.LunarCrystallize && fact.SourceWasPartyMember
            && fact.SourceSide == status.WeaponContext.Equipper.Side;
    internal static void Register()
    {
        const string id = "ST_Weapon_4317602";
        BattleSpecialRules.RegisterNumberedCondition("4317602", (_, c) => c?.Status != null
            && (Shield(c.Status, c.Declaration?.Get<ShieldGrantedEventData>()) || Lunar(c.Status, c.Declaration?.Get<ReactionEventData>())));
        BattleSpecialRules.RegisterStatusLifecycle(id, (status, host) =>
        {
            var owner = WeaponCountLifetime.RequireOwner(status, host, 3176); var state = new State(); States.Add(status, state);
            bool Active() => status.IsActive && owner.HasStatusInstance(status) && EquipmentStatusLifetime.CanExecute(status);
            state.Shield = BattleEventHub.Subscribe<ShieldGrantedEventData>(BattleEventKind.ShieldGranted, (header, fact) =>
            {
                if (Shield(status, fact) && state.Batches.Add(fact.GrantBatchId))
                    WeaponEventResponses.Dispatch(status, owner, "4317602", BattleEventHub.Current);
            }, status.ApplyOrder, Active);
            state.Reaction = BattleEventHub.Subscribe<ReactionEventData>(BattleEventKind.ReactionOccurred, (header, fact) =>
            {
                if (Lunar(status, fact) && state.Reactions.Add(header.EventId))
                    WeaponEventResponses.Dispatch(status, owner, "4317602", BattleEventHub.Current);
            }, status.ApplyOrder, Active);
        }, (status, host) =>
        {
            if (!States.TryGetValue(status, out var state)) return;
            state.Shield.Dispose(); state.Reaction.Dispose(); States.Remove(status);
        });
    }
}
