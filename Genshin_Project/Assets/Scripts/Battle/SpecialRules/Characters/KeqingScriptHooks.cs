using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>刻晴：雷楔具体实例、换键后的手动清除、切入自动二段，以及命中前雷附着。数值与收益顺序留在配表。</summary>
public static class KeqingScriptHooks
{
    public const string Mark = "ST_Keqing_Stiletto", Control = "ST_Keqing_StilettoControl";
    public const string Dignity = "ST_Keqing_Dignity", C2 = "ST_Keqing_C2", C4 = "ST_Keqing_C4", C6 = "ST_Keqing_C6";
    private sealed class OwnerMark { internal StatusInstance Root; }
    private sealed class Memory
    {
        internal StatusInstance Root;
        internal bool Manual;
        internal string Pending;
        internal readonly List<IDisposable> Listeners = new List<IDisposable>();
    }
    private static readonly ConditionalWeakTable<BattleEntity, OwnerMark> Owners = new ConditionalWeakTable<BattleEntity, OwnerMark>();
    private static readonly ConditionalWeakTable<StatusInstance, Memory> Marks = new ConditionalWeakTable<StatusInstance, Memory>();
    // 只为实际执行清楔效果的旧实例保留资格；普通Removed、替换、到期和死亡不获收益。
    private static readonly ConditionalWeakTable<StatusInstance, Memory> ActiveClears = new ConditionalWeakTable<StatusInstance, Memory>();
    private static readonly ConditionalWeakTable<StatusInstance, IDisposable> Listeners = new ConditionalWeakTable<StatusInstance, IDisposable>();
    private static readonly ConditionalWeakTable<BattleDeclaration, Hit> HitFacts = new ConditionalWeakTable<BattleDeclaration, Hit>();
    private static readonly AsyncLocal<Hit> Hitting = new AsyncLocal<Hit>();
    private static readonly AsyncLocal<Clearing> ClearingNow = new AsyncLocal<Clearing>();
    private sealed class Restore : IDisposable
    {
        private Action callback;
        internal Restore(Action value) { callback = value; }
        public void Dispose() { var value = callback; callback = null; value?.Invoke(); }
    }
    private sealed class Hit : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly HashSet<BattleEntity> Electro = new HashSet<BattleEntity>();
        private readonly Hit previous = Hitting.Value;
        internal Hit(BattleEntity owner, IEnumerable<BattleEntity> targets)
        {
            Owner = owner; Hitting.Value = this;
            if (owner?.EntityID == 1029 && targets != null)
                foreach (var target in targets)
                    if (target != null && target.Side != owner.Side && ElementQuery.HasAura(target, "Electro")) Electro.Add(target);
        }
        public void Dispose() => Hitting.Value = previous;
    }
    private sealed class Clearing : IDisposable
    {
        internal readonly Memory Memory;
        private readonly Clearing previous = ClearingNow.Value;
        internal Clearing(Memory memory) { Memory = memory; ClearingNow.Value = this; }
        public void Dispose()
        {
            if (Active(Memory.Root)) Memory.Pending = null;
            ClearingNow.Value = previous;
        }
    }
    private static bool Empty(string argument) => string.IsNullOrEmpty(argument);
    private static bool Active(StatusInstance state) => state?.IsActive == true && state.Caster?.IsAlive == true
        && state.Caster.EntityID == 1029 && StatusHostAccess.Contains(state.Host, state);
    private static StatusInstance Find(BattleEntity owner) => owner != null && Owners.TryGetValue(owner, out var record) && Active(record.Root) ? record.Root : null;
    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) =>
        {
            var root = skill?.SkillID2 == "SK_Skill_Keqing2" ? Find(owner) : null;
            if (root == null || !Marks.TryGetValue(root, out var memory)) return new Restore(null);
            memory.Manual = true;
            return new Restore(() => memory.Manual = false);
        });
        BattleSpecialRules.RegisterHitScope((id, owner, targets) => new Hit(owner, targets));
        BattleSpecialRules.RegisterDeclarationObserver(fact =>
        {
            var hit = fact.Get<DamageEventData>(); var frame = Hitting.Value;
            if (fact.Kind == BattleEventKind.DamageResolved && frame?.Owner?.EntityID == 1029
                && hit?.Source?.SourceEntity == frame.Owner && hit.Source.CountsAsHit && hit.HitLanded)
                HitFacts.Add(fact, frame);
        });
        BattleSpecialRules.RegisterStatusLifecycle(Mark, StartMark, StopMark);
        foreach (string key in new[] { Dignity, C6 }) BattleSpecialRules.RegisterStatusLifecycle(key, StartActionListener, Stop);
        BattleSpecialRules.RegisterStatusLifecycle(C4, StartReactionListener, Stop);
        BattleSpecialRules.RegisterStatusActionScope(state => state?.StatusID2 == Mark && Marks.TryGetValue(state, out var memory)
            ? (IDisposable)new Clearing(memory) : new Restore(null));
        foreach (string kind in new[] { "Manual", "Switch", "Heavy" })
        {
            string selected = kind;
            BattleSpecialRules.RegisterCondition("Keqing_" + kind, (a, c) => Empty(a) && Active(c?.Status)
                && Marks.TryGetValue(c.Status, out var memory) && memory.Pending == selected);
        }
        BattleSpecialRules.RegisterCondition("Keqing_ActiveClear", (a, c) => Empty(a) && c?.Caster?.IsAlive == true
            && c.Status?.StatusID2 == Mark && !c.Status.IsActive && ActiveClears.TryGetValue(c.Status, out _)
            && c.Declaration?.Kind == BattleEventKind.StatusRemoved
            && c.Declaration.Get<StatusEventData>() is StatusEventData fact
            && fact.InstanceId == c.Status.ApplyOrder && fact.Host == c.Status.Host && fact.Reason == "Removed");
        BattleSpecialRules.RegisterCondition("Keqing_FirstSkill", (a, c) => Empty(a) && c?.Caster?.EntityID == 1029
            && c.APCostSkillID == "SK_Skill_Keqing1");
        BattleSpecialRules.RegisterCondition("Keqing_C2Hit", (a, c) => Empty(a) && Active(c?.Status)
            && c.Status.StatusID2 == C2 && c.DamageEvent?.Source?.SourceEntity == c.Caster
            && (c.DamageEvent.DamageType == "Normal" || c.DamageEvent.DamageType == "Heavy")
            && c.DamageEvent.Declaration != null && HitFacts.TryGetValue(c.DamageEvent.Declaration, out var hit)
            && hit.Electro.Contains(c.DamageEvent.Target));
        BattleSpecialRules.RegisterCondition("Keqing_ElectroReaction", (a, c) => Empty(a) && Active(c?.Status)
            && c.Declaration?.Get<ReactionEventData>() is ReactionEventData reaction
            && reaction.Source == c.Caster && reaction.Elements.Contains("Electro"));
        foreach (string action in new[] { "Normal", "Heavy", "Burst" })
        {
            string selected = action;
            BattleSpecialRules.RegisterCondition("Keqing_" + action + "Start", (a, c) => Empty(a) && Active(c?.Status)
                && c.Declaration?.Kind == BattleEventKind.ActionStarted
                && c.Declaration.Get<ActionEventData>() is ActionEventData act && act.Subject == c.Caster && act.ActionType == selected);
        }
        BattleSpecialRules.RegisterTargets("Keqing_Owner()", (_, owner) => owner?.IsAlive == true ? new List<int> { owner.SlotPosition } : new List<int>());
        BattleSpecialRules.RegisterTargets("Keqing_Center()", (_, owner) => Center(owner));
        BattleSpecialRules.RegisterTargets("Keqing_Area()", (_, owner) =>
        {
            var center = Center(owner); var result = new List<int>();
            if (center.Count == 0) return result;
            int count = BattlePositionSystem.GetSideCount(owner.Side == BattleSide.Ally ? BattleSide.Enemy : BattleSide.Ally);
            for (int p = Math.Max(1, center[0] - 1); p <= Math.Min(count, center[0] + 1); p++) result.Add(p);
            return result;
        });
    }
    private static List<int> Center(BattleEntity owner)
    {
        var root = ClearingNow.Value?.Memory.Root;
        if (root?.Caster != owner) root = Find(owner);
        return root?.Host is FieldPosition field ? new List<int> { field.SlotIndex } : new List<int>();
    }
    private static void StartMark(StatusInstance root, object host)
    {
        var owner = root.Caster;
        if (owner?.EntityID != 1029 || !(host is FieldPosition position) || position.Side == owner.Side)
            throw new InvalidOperationException("雷楔必须由刻晴施加在敌方场地。");
        var record = Owners.GetValue(owner, _ => new OwnerMark());
        if (Active(record.Root)) StatusHostAccess.Remove(record.Root.Host, record.Root, "Replaced");
        record.Root = root;
        var memory = new Memory { Root = root }; Marks.Add(root, memory);
        memory.Listeners.Add(BattleEventHub.Subscribe<SwitchEventData>(BattleEventKind.SwitchResolved, (_, change) =>
        {
            if (change.Subject == owner && !memory.Manual) RequestMark(memory, "Switch", BattleEventHub.Current);
        }, root.ApplyOrder, () => Active(root)));
        memory.Listeners.Add(BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, damage) =>
        {
            if (!memory.Manual && damage.Source?.SourceEntity == owner && damage.Source.CountsAsHit && damage.HitLanded
                && damage.DamageType == "Heavy" && damage.Subject?.Side == position.Side && damage.Subject.SlotPosition == position.SlotIndex)
                RequestMark(memory, "Heavy", BattleEventHub.Current);
        }, root.ApplyOrder, () => Active(root)));
        memory.Listeners.Add(BattleEventHub.Subscribe<StatusEventData>(BattleEventKind.StatusRemoved, (_, removal) =>
        {
            if (memory.Manual && removal.Host == owner && removal.StatusId == Control
                && root.BoundStatuses.Any(s => s.ApplyOrder == removal.InstanceId)) RequestMark(memory, "Manual", BattleEventHub.Current);
        }, root.ApplyOrder, () => Active(root)));
    }
    private static void StopMark(StatusInstance root, object host)
    {
        var frame = ClearingNow.Value;
        if (frame?.Memory.Root == root && ScriptHookContext.Current?.ExecutingStatusEffectID == 410290103
            && (frame.Memory.Pending == "Manual" || frame.Memory.Pending == "Switch" || frame.Memory.Pending == "Heavy"))
            ActiveClears.Add(root, frame.Memory);
        if (Marks.TryGetValue(root, out var memory)) foreach (var listener in memory.Listeners) listener.Dispose();
        Marks.Remove(root);
        if (root.Caster != null && Owners.TryGetValue(root.Caster, out var owner) && owner.Root == root) owner.Root = null;
    }
    private static void RequestMark(Memory memory, string kind, BattleDeclaration cause)
    {
        if (!Active(memory.Root) || memory.Pending != null) return;
        memory.Pending = kind;
        if (!Request(memory.Root, "Keqing_" + kind + "()", cause)) memory.Pending = null;
    }
    private static void StartActionListener(StatusInstance state, object host)
    {
        var owner = RequireOwner(state, host);
        Listeners.Add(state, BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted, (_, action) =>
        {
            if (action.Subject != owner) return;
            if (state.StatusID2 == Dignity && action.ActionType != "Burst") return;
            if (action.ActionType == "Normal" || action.ActionType == "Heavy" || action.ActionType == "Burst")
                Request(state, "Keqing_" + action.ActionType + "Start()", BattleEventHub.Current);
        }, state.ApplyOrder, () => Active(state)));
    }
    private static void StartReactionListener(StatusInstance state, object host)
    {
        var owner = RequireOwner(state, host);
        Listeners.Add(state, BattleEventHub.Subscribe<ReactionEventData>(BattleEventKind.ReactionOccurred, (_, reaction) =>
        {
            if (reaction.Source == owner && reaction.Elements.Contains("Electro")) Request(state, "Keqing_ElectroReaction()", BattleEventHub.Current);
        }, state.ApplyOrder, () => Active(state)));
    }
    private static BattleEntity RequireOwner(StatusInstance state, object host)
    {
        if (!(host is BattleEntity owner) || owner != state.Caster || owner.EntityID != 1029)
            throw new InvalidOperationException("刻晴初始化监听必须属于本人。");
        return owner;
    }
    private static void Stop(StatusInstance state, object host)
    { if (Listeners.TryGetValue(state, out var listener)) listener.Dispose(); Listeners.Remove(state); }
    private static bool Request(StatusInstance state, string hook, BattleDeclaration cause)
    {
        if (!Active(state) || cause == null || !DataManager.Instance.StatusActionDict.TryGetValue(state.StatusID2, out var actions)) return false;
        bool submitted = false;
        foreach (var action in actions) if (action.ActionType == "OnTrigger" && action.ScriptHook == hook)
            submitted |= state.Caster.CharacterCtrl.TryExecuteEventStatusAction(state, action, state.Host,
                new ScriptHookContext { Caster = state.Caster, Status = state, StatusHost = state.Host, Declaration = cause }, () => Active(state));
        return submitted;
    }
}
