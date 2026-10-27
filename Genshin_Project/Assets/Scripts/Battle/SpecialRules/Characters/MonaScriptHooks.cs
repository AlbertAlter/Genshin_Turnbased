using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>莫娜的逐hit资格、独立虚影与星异投影。数值、目标、限次和执行顺序仍使用正式效果与声明入口。</summary>
public static class MonaScriptHooks
{
    public const string Phantom = "ST_Mona_Phantom", Lesser = "ST_Mona_LesserPhantom", Bubble = "ST_Mona_Bubble", Omen = "ST_Mona_Omen";
    public const string Escape = "ST_Mona_Escape", Hexerei = "ST_Mona_Hexerei", Glow = "ST_Mona_Glow", GlowBonus = "ST_Mona_GlowBonus";
    public const string C2 = "ST_Mona_C2", Guarantee = "ST_Mona_Guarantee", Calamity = "ST_Mona_Calamity";
    private static readonly AsyncLocal<HitFrame> Hitting = new AsyncLocal<HitFrame>();
    private sealed class GainFrame { internal StatusInstance State; internal int Value; }
    private static readonly AsyncLocal<GainFrame> Gaining = new AsyncLocal<GainFrame>();
    private static readonly ConditionalWeakTable<BattleDeclaration, HitFrame> Facts = new ConditionalWeakTable<BattleDeclaration, HitFrame>();
    private static readonly ConditionalWeakTable<StatusInstance, DodgeMemory> Dodges = new ConditionalWeakTable<StatusInstance, DodgeMemory>();
    private static readonly ConditionalWeakTable<StatusInstance, IDisposable> Listeners = new ConditionalWeakTable<StatusInstance, IDisposable>();
    private sealed class DodgeMemory { internal int Turn = -1; internal long Action; }
    private sealed class Restore : IDisposable
    {
        private Action restore;
        internal Restore(Action value) { restore = value; }
        public void Dispose() { var value = restore; restore = null; value?.Invoke(); }
    }
    private sealed class HitFrame : IDisposable
    {
        internal readonly BattleEntity Source;
        internal readonly int SourceSlot;
        internal readonly long Action;
        internal readonly Dictionary<BattleEntity, int> Positions = new Dictionary<BattleEntity, int>();
        internal long FirstNormal, FirstHeavy, FirstMona;
        internal readonly Dictionary<BattleEntity, List<StatusInstance>> Omens = new Dictionary<BattleEntity, List<StatusInstance>>();
        internal readonly Dictionary<BattleEntity, List<StatusInstance>> Glows = new Dictionary<BattleEntity, List<StatusInstance>>();
        internal readonly HashSet<StatusInstance> DodgeCandidates = new HashSet<StatusInstance>();
        private readonly HitFrame previous = Hitting.Value;
        internal HitFrame(long id, BattleEntity source, IEnumerable<BattleEntity> targets)
        {
            Source = source; SourceSlot = source?.SlotPosition ?? 0; Action = BattleResolutionContext.CurrentAction?.ScopeId ?? id;
            Hitting.Value = this;
            if (targets != null) foreach (var target in targets)
            {
                if (target == null) continue;
                Positions[target] = target.SlotPosition;
                Omens[target] = target.GetStatusInstances(Omen).Where(Active).ToList();
                if (target.EntityID != 1028 || target.Side == source?.Side || source == null) continue;
                var state = target.GetStatus(Escape);
                if (Active(state) && Dodges.TryGetValue(state, out var memory)
                    && (memory.Turn != Turn || memory.Action == Action)) DodgeCandidates.Add(state);
            }
            var battle = BattleManager.Instance;
            if (battle != null) foreach (var ally in battle.Allies)
                if (ally?.Entity?.EntityID == 1028 && ally.Entity.IsAlive)
                    Glows[ally.Entity] = ally.Entity.GetStatusInstances(Glow).Where(Active).ToList();
        }
        public void Dispose() => Hitting.Value = previous;
    }
    private static int Turn => BattleManager.Instance?.TurnCount ?? 0;
    private static bool Empty(string value) => string.IsNullOrEmpty(value);
    internal static bool Active(StatusInstance state) => state?.IsActive == true && state.Caster?.IsAlive == true
        && state.Caster.EntityID == 1028 && StatusHostAccess.Contains(state.Host, state);
    internal static BattleEntity RequireOwner(StatusInstance state, object host)
    {
        if (!(host is BattleEntity owner) || owner != state.Caster || owner.EntityID != 1028)
            throw new InvalidOperationException("莫娜本人状态的来源或宿主不正确。");
        return owner;
    }
    internal static void Register()
    {
        BattleSpecialRules.RegisterHitScope((id, owner, targets) => new HitFrame(id, owner, targets));
        BattleSpecialRules.RegisterDeclarationObserver(Index);
        BattleSpecialRules.RegisterStatusActionScope(BeginAction);
        BattleSpecialRules.RegisterStatusLifecycle(Escape, (state, host) => { RequireOwner(state, host); Dodges.Add(state, new DodgeMemory()); },
            (state, _) => Dodges.Remove(state));
        BattleSpecialRules.RegisterStatusLifecycle(Hexerei, StartHexerei, Stop);
        BattleSpecialRules.RegisterStatusLifecycle(C2, StartC2, Stop);
        BattleSpecialRules.RegisterStatusLifecycle(Glow, RefreshGlows, null);
        BattleSpecialRules.RegisterStatusLifecycle(Phantom, StartPhantom, Stop);
        BattleSpecialRules.RegisterStatusLifecycle(Lesser, StartPhantom, Stop);
        foreach (string id in new[] { "ST_Mona_OmenBonus", "ST_Mona_C1Amplifying", "ST_Mona_C1Transformative",
            "ST_Mona_C1Lunar", "ST_Mona_C4Rate", "ST_Mona_C4Damage", GlowBonus }) StatusSnapshotConditions.RegisterLiveTargetStatus(id);
        BattleSpecialRules.RegisterCondition("Mona_Omen", (a, c) => Empty(a) && BoundOmen(c));
        BattleSpecialRules.RegisterCondition("Mona_OmenDamage", (a, c) => Empty(a) && c?.DamageType != "Reaction" && BoundOmen(c));
        BattleSpecialRules.RegisterCondition("Mona_C1Transform", (a, c) => Empty(a) && BoundOmen(c)
            && (c.DamageReaction == ReactionType.ElectroCharged || c.DamageReaction == ReactionType.Swirl && c.DamageElement == "Hydro"));
        BattleSpecialRules.RegisterCondition("Mona_Background", (a, c) => Empty(a) && c?.Recipient?.CharacterCtrl != null && !c.Recipient.CharacterCtrl.IsActive);
        BattleSpecialRules.RegisterCondition("Mona_Foreground", (a, c) => Empty(a) && c?.Recipient?.CharacterCtrl?.IsActive == true);
        BattleSpecialRules.RegisterCondition("Mona_HexCharacter", (a, c) => Empty(a) && BoundOmen(c)
            && DataManager.Instance?.TeamBonusRules.Hexerei.CharacterIDs().Contains(c.Recipient.EntityID) == true);
        BattleSpecialRules.RegisterCondition("Mona_GlowBonus", (a, c) => Empty(a) && QualifiesGlow(c));
        BattleSpecialRules.RegisterCondition("Mona_FirstHit", (a, c) => Empty(a) && Frame(c, out var f)
            && f.Source == c.Caster && f.Source.EntityID == 1028 && f.FirstMona == c.DamageEvent.Declaration.Header.EventId);
        BattleSpecialRules.RegisterCondition("Mona_HeavyHit", (a, c) => Empty(a) && Frame(c, out var f)
            && f.Source == c.Caster && f.FirstHeavy != 0 && f.FirstHeavy == c.DamageEvent.Declaration.Header.EventId);
        BattleSpecialRules.RegisterCondition("Mona_C2", (a, c) => Empty(a) && C2Ready(c));
        BattleSpecialRules.RegisterCondition("Mona_BurstDone", (a, c) => Empty(a) && Active(c?.Status)
            && c.Declaration?.Get<ActionEventData>() is ActionEventData act && act.Subject == c.Caster && act.SkillId == "SK_Burst_Mona");
        BattleSpecialRules.RegisterCondition("Mona_Dodge", (a, c) => Empty(a) && Hitting.Value?.DodgeCandidates.Contains(c?.Status) == true);
        BattleSpecialRules.RegisterCondition("Mona_DodgeHit", (a, c) => Empty(a) && c?.Status?.StatusID2 == Escape
            && c.DamageEvent?.HitLanded == true && c.DamageEvent.Source.CountsAsHit && c.DamageEvent.Target == c.Caster
            && c.DamageEvent.Source.SourceEntity?.Side != c.Caster.Side
            && (!Frame(c, out var f) || f.DodgeCandidates.Contains(c.Status)));
        BattleSpecialRules.RegisterCondition("Mona_Pop", (a, c) => Empty(a) && Active(c?.Status) && c.Status.StatusID2 == Bubble
            && c.DamageEvent?.HitLanded == true && c.DamageEvent.Source.CountsAsHit && c.DamageEvent.Source.SourceSide != c.DamageEvent.Target.Side);
        BattleSpecialRules.RegisterCondition("Mona_ExtendHit", (a, c) => Empty(a) && Active(c?.Status) && c.Status.StatusID2 == Omen
            && c.DamageEvent?.HitLanded == true && c.DamageEvent.Source.CountsAsHit && c.DamageEvent.Source.SourceEntity == c.Caster
            && (c.DamageEvent.DamageType == "Normal" || c.DamageEvent.DamageType == "Heavy") && HasHexerei());
        BattleSpecialRules.RegisterCondition("Mona_ExtendReady", (a, c) => Empty(a) && c?.Status?.StatusID2 == Omen
            && c.Status.LifeTriggerCount > 0 && c.Status.LifeTriggerCount % 2 == 0);
        BattleSpecialRules.RegisterCondition("Mona_Minion", (a, c) => Empty(a) && c?.StatusHost is BattleEntity target
            && DataManager.Instance.EnemyMainDict.TryGetValue(target.EntityID, out var main) && main.EnemyType == "Minion");
        BattleSpecialRules.RegisterCondition("Mona_PhantomHit", (a, c) => Empty(a) && c?.Declaration != null
            && Facts.TryGetValue(c.Declaration, out var f) && f.FirstMona == c.Declaration.Header.EventId
            && c.Declaration.Get<DamageEventData>()?.Source?.SourceStatus == c.Status);
        BattleSpecialRules.RegisterCondition("Mona_Vape", (a, c) => Empty(a) && Active(c?.Status) && c.Status.StatusID2 == Hexerei
            && HasHexerei() && c.Declaration?.Get<ReactionEventData>() is ReactionEventData reaction
            && reaction.Reaction == ReactionType.Vaporize && reaction.Source != c.Caster && reaction.Source?.Side == c.Caster.Side
            && Facts.TryGetValue(c.Declaration, out var hit) && hit.Glows.TryGetValue(c.Caster, out var layers) && layers.Count > 0);
        BattleSpecialRules.RegisterCondition("Mona_C6Gain", (a, c) => int.TryParse(a, out int minimum) && c?.Caster?.EntityID == 1028
            && (Gaining.Value?.State == c.Status ? Gaining.Value.Value : C6Gain(c.Caster)) >= minimum);
        BattleSpecialRules.RegisterTargets("Mona_PhantomArea()", (_, owner) => Area(ScriptHookContext.Current?.StatusHost as FieldPosition, owner));
        BattleSpecialRules.RegisterTargets("Mona_AttackerField()", (_, owner) =>
        {
            var source = ScriptHookContext.Current?.DamageEvent?.Source?.SourceEntity;
            int slot = Frame(ScriptHookContext.Current, out var frame) ? frame.SourceSlot : source?.SlotPosition ?? 0;
            return source != null && source.Side != owner?.Side && slot > 0 ? new List<int> { slot } : new List<int>();
        });
        BattleSpecialRules.RegisterTargets("Mona_AutoArea()", (_, owner) =>
        {
            var target = ScriptHookContext.Current?.DamageEvent?.Target;
            int slot = Frame(ScriptHookContext.Current, out var frame) && target != null && frame.Positions.TryGetValue(target, out int saved)
                ? saved : target?.SlotPosition ?? 0;
            return target?.Side != owner?.Side ? Area(slot, target?.Side ?? BattleSide.Enemy) : new List<int>();
        });
        BattleSpecialRules.RegisterBuffScalar("Mona_Recharge()", (effect, state) =>
            state?.Caster?.EntityID == 1028 && BuffExpressionEvaluator.TryResolveScalar(effect.Param1, state, out float coefficient)
                ? coefficient * state.Caster.GetFinalRechargeRate() : throw new InvalidOperationException("莫娜充能换算缺少本人来源或数值系数。"));
        BattleSpecialRules.RegisterDamage("Mona_C6()", (hook, owner, type) =>
        { if (owner?.EntityID != 1028 || type != "Damage") throw new NotSupportedException("Mona_C6只用于莫娜重击伤害。"); },
            (hook, owner, target, hit, level) => owner.ConstellationLevel >= 6 && HasOmen(target, owner)
                ? new HitData(hit.Multiplier * 2f, hit.ElementAura) : hit);
        BattleSpecialRules.RegisterStatusHitModifier("Mona_C6()");
    }
    private static bool HasHexerei() => BattleManager.Instance?.TeamBonuses?.HasHexerei == true;
    private static bool Frame(ScriptHookContext context, out HitFrame frame)
    { frame = null; return context?.DamageEvent?.Declaration != null && Facts.TryGetValue(context.DamageEvent.Declaration, out frame); }
    private static bool HasOmen(BattleEntity target, BattleEntity owner)
    {
        if (target == null) return false;
        var frame = Hitting.Value;
        var roots = frame != null && frame.Omens.TryGetValue(target, out var saved) ? saved : target.GetStatusInstances(Omen);
        return roots.Any(root => Active(root) && root.Caster == owner);
    }
    private static bool BoundOmen(ScriptHookContext c)
    {
        if (c?.Caster?.EntityID != 1028 || c.Target == null || c.Recipient?.CharacterCtrl == null
            || c.Recipient.Side != c.Caster.Side || BattleManager.Instance?.TeamBonuses?.Contains(c.Recipient) != true) return false;
        var frame = Hitting.Value;
        var roots = frame != null && frame.Omens.TryGetValue(c.Target, out var saved) ? saved : c.Target.GetStatusInstances(Omen);
        return roots.Any(root => Active(root) && root.Caster == c.Caster && root.BoundStatuses.Contains(c.Status));
    }
    private static bool QualifiesGlow(ScriptHookContext c)
    {
        if (c?.Status?.StatusID2 != GlowBonus || c.DamageReaction != ReactionType.Vaporize
            || c.Recipient == null || c.Recipient == c.Caster || c.Recipient.Side != c.Caster?.Side || c.Target == null) return false;
        var frame = Hitting.Value;
        var roots = frame != null && frame.Glows.TryGetValue(c.Caster, out var saved) ? saved : c.Caster.GetStatusInstances(Glow);
        return roots.Any(root => Active(root) && root.BoundStatuses.Contains(c.Status));
    }
    private static bool C2Ready(ScriptHookContext c)
        => Frame(c, out var frame) && c.Caster == frame.Source && frame.Source.EntityID == 1028
            && frame.FirstNormal != 0 && frame.FirstNormal == c.DamageEvent.Declaration.Header.EventId
            && (c.Caster.HasStatus(Guarantee) || ScriptHookEvaluator.Evaluate("Chance(0.2)", c));
    private static void Index(BattleDeclaration declaration)
    {
        var frame = Hitting.Value;
        if (frame == null || declaration == null) return;
        if (declaration.Kind == BattleEventKind.ReactionOccurred)
        { Facts.Add(declaration, frame); return; }
        if (declaration.Kind != BattleEventKind.DamageResolved) return;
        var damage = declaration.Get<DamageEventData>();
        if (damage?.HitLanded != true || !damage.Source.CountsAsHit || damage.Source.SourceEntity != frame.Source) return;
        Facts.Add(declaration, frame);
        if (frame.FirstMona == 0) frame.FirstMona = declaration.Header.EventId;
        if (damage.Source.SourceEntity?.EntityID != 1028 || damage.Subject.Side == frame.Source.Side) return;
        if (damage.DamageType == "Normal" && frame.FirstNormal == 0) frame.FirstNormal = declaration.Header.EventId;
        if (damage.DamageType == "Heavy" && frame.FirstHeavy == 0) frame.FirstHeavy = declaration.Header.EventId;
    }
    private static IDisposable BeginAction(StatusInstance state)
    {
        if (state?.StatusID2 == Calamity)
        {
            var previous = Gaining.Value;
            Gaining.Value = new GainFrame { State = state, Value = C6Gain(state.Caster) };
            return new Restore(() => Gaining.Value = previous);
        }
        var c = ScriptHookContext.Current;
        if (state?.StatusID2 == Escape && c?.DamageEvent != null && Dodges.TryGetValue(state, out var memory))
        {
            memory.Turn = Turn;
            memory.Action = Frame(c, out var frame) ? frame.Action
                : BattleResolutionContext.CurrentAction?.ScopeId ?? c.DamageEvent.RuntimeHitID;
        }
        return new Restore(null);
    }
    private static List<int> Area(FieldPosition position, BattleEntity owner)
        => position == null ? new List<int>() : Area(position.SlotIndex, position.Side);
    private static List<int> Area(int center, BattleSide side)
    {
        var result = new List<int>();
        if (center < 1) return result;
        for (int i = Math.Max(1, center - 1); i <= Math.Min(BattlePositionSystem.GetSideCount(side), center + 1); i++) result.Add(i);
        return result;
    }
    private static int C6Gain(BattleEntity owner)
    {
        int gain = owner.CharacterCtrl?.IsActive == true ? 0 : 1;
        var battle = BattleManager.Instance;
        if (battle != null) foreach (var enemy in battle.Enemies)
            if (enemy?.Entity?.IsAlive == true && HasOmen(enemy.Entity, owner)) gain++;
        return Math.Min(3, gain);
    }
    private static void RefreshGlows(StatusInstance state, object host)
    {
        var owner = RequireOwner(state, host);
        // 层的强度各自在创建时固定（兼容菲谢尔六命）；重新获得时只统一刷新旧层及其绑定的计时。
        foreach (var old in owner.GetStatusInstances(Glow))
            if (Active(old)) StatusBindingSystem.ExtendDuration(old, state.RemainingPhaseCount - old.RemainingPhaseCount);
    }
    private static void StartHexerei(StatusInstance state, object host)
    {
        var owner = RequireOwner(state, host);
        Listeners.Add(state, BattleEventHub.Subscribe<ReactionEventData>(BattleEventKind.ReactionOccurred, (_, reaction) =>
        {
            if (reaction.Reaction == ReactionType.Vaporize && reaction.Source != owner && reaction.Source?.Side == owner.Side)
                Request(state, "Mona_Vape()", BattleEventHub.Current);
        }, state.ApplyOrder, () => Active(state)));
    }
    private static void StartPhantom(StatusInstance state, object host)
    {
        if (!(host is FieldPosition) || state.Caster?.EntityID != 1028) throw new InvalidOperationException("虚影必须挂在敌方场地位置。");
        Listeners.Add(state, BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
        {
            if (hit.HitLanded && hit.Source.CountsAsHit && hit.Source.SourceStatus == state
                && (hit.Source.SourceEffectID == "STE_Mona_PhantomExplosion" || hit.Source.SourceEffectID == "STE_Mona_LesserExplosion"))
                Request(state, "Mona_PhantomHit()", BattleEventHub.Current);
        }, state.ApplyOrder, () => Active(state)));
    }
    private static void StartC2(StatusInstance state, object host)
    {
        var owner = RequireOwner(state, host);
        Listeners.Add(state, BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted, (_, action) =>
        { if (action.Subject == owner && action.SkillId == "SK_Burst_Mona") Request(state, "Mona_BurstDone()", BattleEventHub.Current); },
            state.ApplyOrder, () => Active(state)));
    }
    private static void Stop(StatusInstance state, object host)
    { if (Listeners.TryGetValue(state, out var listener)) listener.Dispose(); Listeners.Remove(state); }
    private static void Request(StatusInstance state, string hook, BattleDeclaration cause)
    {
        if (!Active(state) || cause == null || !DataManager.Instance.StatusActionDict.TryGetValue(state.StatusID2, out var actions)) return;
        foreach (var action in actions) if (action.ActionType == "OnTrigger" && action.ScriptHook == hook)
            state.Caster.CharacterCtrl.TryExecuteEventStatusAction(state, action, state.Host,
                new ScriptHookContext { Caster = state.Caster, Status = state, StatusHost = state.Host, Declaration = cause }, () => Active(state));
    }
    /// <summary>仍是RemoveStatus；只移除本次hit开始时实际参与的具体辉光，不能吞掉响应中新获得的层。</summary>
    internal static bool TryRemove(StatusEffectData effect, StatusInstance source, BattleEntity target)
    {
        if (effect?.StatusEffectID2 != "STE_Mona_GlowConsume") return false;
        if (source?.StatusID2 != Hexerei || target != source.Caster) throw new InvalidOperationException("辉光消耗缺少本次莫娜来源。");
        var cause = ScriptHookContext.Current?.Declaration;
        if (cause != null && Facts.TryGetValue(cause, out var frame) && frame.Glows.TryGetValue(target, out var saved))
            foreach (var glow in saved) if (Active(glow)) target.RemoveStatusInstance(glow, "Consumed");
        return true;
    }
}

/// <summary>厄运的修辞只存0～3整数；增减走Count效果，施放重击后的清零也由配表负责。</summary>
public sealed class MonaCalamityCount : CountStatusHandler
{
    public override bool SupportsEffectChange => true;
    public override bool EffectUsesIntegerValue => true;
    public override void OnCreated(StatusInstance state, object host)
    { MonaScriptHooks.RequireOwner(state, host); state.RuntimeIntValue = 0; state.StackCount = 1; state.IsPermanent = true; state.TriggerPhase = 3; }
    public override StatusInstance OnReapplied(StatusInstance state, BattleEntity host, CountStatusApplication application)
    { MonaScriptHooks.RequireOwner(state, host); return state; }
    public override void ValidateEffectChange(StatusInstance state, BattleEntity host, CountChangeRequest request)
    { base.ValidateEffectChange(state, host, request); MonaScriptHooks.RequireOwner(state, host); }
    public override void ChangeFromEffect(StatusInstance state, BattleEntity host, CountChangeRequest request)
    { ValidateEffectChange(state, host, request); request.ApplyInteger(state, host, 0, 3); }
}
