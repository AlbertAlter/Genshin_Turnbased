using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;

/// <summary>风龙、奇怪丘丘人与公子的实例机制。实际伤害、状态、盾量及计数增减仍读取效果表。</summary>
public static class Enemy2071To2073Hooks
{
    public const string DEntry = "ST_Dvalin_Entry", DShield = "ST_Dvalin_Shield", DWeak = "ST_Dvalin_Weakness";
    public const string DCharge = "ST_Dvalin_Charge", Rift = "ST_Dvalin_RiftCount", CEntry = "ST_Childe_Entry";
    public const string Hydro = "ST_Childe_RiptideHydro", Electro = "ST_Childe_RiptideElectro";
    public const string CCharge = "ST_Childe_WhaleCharge", Leap = "ST_Childe_Leap";
    private sealed class Memory
    {
        internal readonly List<IDisposable> Listeners = new List<IDisposable>();
        internal readonly HashSet<long> ShieldHits = new HashSet<long>();
        internal readonly Dictionary<BattleEntity, int> ActiveTurns = new Dictionary<BattleEntity, int>();
        internal int WhaleTurn, WhalePhase, ShieldDue;
        internal bool FinalePending, FinaleUsed;
        internal Action PhaseExit;
        internal BattleEntity LeapTarget, LeapRelease;
        internal int LeapTurn;
    }
    private sealed class Frame : IDisposable
    {
        internal BattleEntity Owner;
        internal SkillMainData Skill;
        private readonly Frame previous = Active.Value;
        internal Frame(BattleEntity owner, SkillMainData skill) { Owner = owner; Skill = skill; Active.Value = this; }
        public void Dispose() => Active.Value = previous;
    }
    private static readonly AsyncLocal<Frame> Active = new AsyncLocal<Frame>();
    private static readonly ConditionalWeakTable<StatusInstance, Memory> Memories = new ConditionalWeakTable<StatusInstance, Memory>();
    private static int Turn => Math.Max(1, BattleManager.Instance?.TurnCount ?? 1);
    private static int Phase(BattleEntity owner) => owner?.EnemyCtrl?.HealthPhases.Current ?? 1;
    private static Memory Get(BattleEntity owner)
    {
        var status = owner?.GetStatus(owner.EntityID == 2071 ? DEntry : CEntry);
        return status != null && Memories.TryGetValue(status, out var memory) ? memory : null;
    }
    private static List<BattleEntity> Allies() => BattleManager.Instance?.Allies.Where(c => c?.Entity?.IsOnField == true)
        .Select(c => c.Entity).ToList() ?? new List<BattleEntity>();
    private static List<int> Position(BattleEntity unit) => unit?.IsOnField == true
        && BattleManager.Instance?.GetEntityByPosition(unit.Side, unit.SlotPosition) == unit
        ? new List<int> { unit.SlotPosition } : new List<int>();
    private static bool Marked(BattleEntity unit, BattleEntity owner, string id) => unit?.GetStatusInstances(id)
        .Any(s => s.IsActive && s.Caster == owner) == true;
    private static bool AnyMark(BattleEntity unit, BattleEntity owner) => Marked(unit, owner, Hydro) || Marked(unit, owner, Electro);
    private static List<BattleEntity> MarkedTargets(BattleEntity owner, bool fresh = false) => Allies().Where(unit =>
        (Phase(owner) == 2 ? unit.GetStatusInstances(Electro) : unit.GetStatusInstances(Hydro).Concat(unit.GetStatusInstances(Electro)))
        .Any(s => s.IsActive && s.Caster == owner && (!fresh || s.RemainingPhaseCount == 2))).ToList();

    internal static void Register()
    {
        BattleSpecialRules.RegisterStatusLifecycle(DEntry, StartEntry, Stop);
        BattleSpecialRules.RegisterStatusLifecycle(CEntry, StartEntry, Stop);
        BattleSpecialRules.RegisterStatusLifecycle(DWeak, (s, host) => {
            var m = Get(host as BattleEntity); if (m != null) m.WhaleTurn = 0;
            DelayTimer(s);
        }, null);
        BattleSpecialRules.RegisterStatusLifecycle(DShield, (s, host) => {
            var owner = host as BattleEntity; var m = Get(owner);
            // OnEnd 的重盾效果执行时，瘫痪实例仍在，移除回调已不处于 OnEnd 作用域。
            if (owner?.HasStatus(DWeak) == true && m != null && !m.FinaleUsed) m.FinalePending = true;
        }, null);
        foreach (string id in new[] { "ST_Childe_HydroWeakness", "ST_Childe_ElectroWeakness", "ST_Dvalin_BacklineProtection" })
            BattleSpecialRules.RegisterStatusLifecycle(id, (s, _) => DelayTimer(s), null);
        BattleSpecialRules.RegisterStatusLifecycle(DCharge, StartWhale, null);
        BattleSpecialRules.RegisterStatusLifecycle(CCharge, StartWhale, null);
        BattleSpecialRules.RegisterStatusLifecycle(Leap, (s, host) => {
            var owner = host as BattleEntity; var m = Get(owner); if (m == null) return;
            m.LeapTarget = PickWeighted(MarkedTargets(owner, true)); m.LeapTurn = Turn + 1;
        }, null);
        BattleSpecialRules.RegisterStatusLifecycle(Rift, (s, host) => { var m = Get(host as BattleEntity); if (m != null) { m.FinaleUsed = true; m.FinalePending = false; } }, null);
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => new Frame(owner, skill));
        BattleSpecialRules.RegisterHitScope(BeforeHit);
        BattleSpecialRules.RegisterEnemySkillSelection(SelectReserved);

        Condition("BossLastAction", c => c?.Caster?.EnemyCtrl?.IsLastGuaranteedAction == true);
        Condition("DvalinCanTornado", c => BattleManager.Instance?.Field.AllySlots.All(f => !f.StatusList.Any(s => s.IsActive && s.StatusID2 == "ST_Dvalin_Tornado")) == true);
        Condition("DvalinFinalePending", c => Get(c?.Caster)?.FinalePending == true);
        Condition("DvalinFieldInactive", c => {
            var field = c?.StatusHost as FieldPosition; var unit = field?.Occupant; var m = Get(c?.Caster);
            return unit?.IsOnField == true && unit.CharacterCtrl?.IsActive != true && m != null
                && (!m.ActiveTurns.TryGetValue(unit, out int turn) || turn != Turn);
        });
        Condition("DvalinShieldBroken", c => c?.Status?.StatusID2 == DShield && c.Declaration?.Get<StatusEventData>()?.Reason == "ShieldBroken");
        Condition("UnusualFirst", c => c?.Caster != null && !c.Caster.HasStatus("ST_UnusualHilichurl_Warning") && !c.Caster.HasStatus("ST_UnusualHilichurl_Leaving"));
        Condition("UnusualSecond", c => c?.Caster?.HasStatus("ST_UnusualHilichurl_Warning") == true);
        Condition("UnusualThird", c => c?.Caster?.HasStatus("ST_UnusualHilichurl_Leaving") == true);
        Condition("ChildeMarked", c => MarkedTargets(c?.Caster).Count > 0);
        Condition("ChildeCanLeap", c => c?.Caster != null && !c.Caster.HasStatus(Leap) && MarkedTargets(c.Caster, true).Count > 0);
        Condition("ChildeHydroGain", c => Gain(c, true));
        Condition("ChildeElectroGain", c => Gain(c, false));
        Condition("ChildeHydroTrigger", c => Trigger(c, Hydro));
        Condition("ChildeElectroTrigger", c => Trigger(c, Electro));
        Condition("ChildeCounterHydro", c => Counter(c, 1));
        Condition("ChildeCounterElectro", c => Counter(c, 2));
        Condition("ChildeClearEarly", c => Phase(c?.Caster) < 3 && Clearable(c));
        Condition("ChildeClearFinal", c => Phase(c?.Caster) == 3 && Clearable(c));
        Condition("ChildeCanClearHydro", c => Phase(c?.Caster) != 2);
        Condition("ChildeCanClearElectro", c => Phase(c?.Caster) != 1);
        Condition("ChildeHydroBroken", c => c?.Declaration?.Get<StatusEventData>()?.Reason == "ShieldBroken" && c.Status?.StatusID2 == "ST_Childe_HydroShield");
        Condition("ChildeElectroBroken", c => c?.Declaration?.Get<StatusEventData>()?.Reason == "ShieldBroken" && c.Status?.StatusID2 == "ST_Childe_ElectroShield");
        BattleSpecialRules.RegisterTargets("BossFieldOccupant()", (_, __) => Position((ScriptHookContext.Current?.StatusHost as FieldPosition)?.Occupant));
        BattleSpecialRules.RegisterTargets("BossDamageTarget()", (_, __) => Position(ScriptHookContext.Current?.DamageEvent?.Target));
        BattleSpecialRules.RegisterTargets("BossAttacker()", (_, __) => Position(ScriptHookContext.Current?.DamageEvent?.Source?.SourceEntity));
        BattleSpecialRules.RegisterTargets("BossSwitchTarget()", (_, __) => Position(ScriptHookContext.Current?.Declaration?.Get<SwitchEventData>()?.Subject));
        BattleSpecialRules.RegisterTargets("ChildeMarkedTargets()", (_, owner) => Allies().Where(u => Marked(u, owner, Phase(owner) == 2 ? Electro : Hydro)).Select(u => u.SlotPosition).ToList());
        BattleSpecialRules.RegisterTargets("ChildeMarkedSplash()", (_, owner) => {
            var target = PickWeighted(MarkedTargets(owner)); if (target == null) return new List<int>();
            return Allies().Where(u => Math.Abs(u.SlotPosition - target.SlotPosition) <= 1).Select(u => u.SlotPosition).ToList();
        });
        BattleSpecialRules.RegisterTargets("ChildeLeapTarget()", (_, owner) => Position(Get(owner)?.LeapRelease));
        BattleSpecialRules.RegisterTargets("ChildeLeapAll()", (_, owner) => Get(owner)?.LeapRelease != null ? Allies().Select(u => u.SlotPosition).ToList() : new List<int>());
        BattleSpecialRules.RegisterDamage("DvalinRift()", (_, __, ___) => { }, (_, owner, __, hit, ___) =>
            new HitData(hit.Multiplier * (owner.GetStatus(Rift)?.RuntimeIntValue ?? 0), hit.ElementAura));
    }

    private static void Condition(string name, Func<ScriptHookContext, bool> test)
        => BattleSpecialRules.RegisterCondition(name, (arg, context) => string.IsNullOrEmpty(arg) && test(context));
    private static void DelayTimer(StatusInstance status)
    {
        if ((int)(BattleManager.Instance?.CurrentPhase ?? 0) == status.AddInPhase) status.EarliestTimerTurn = Turn + 1;
    }
    private static void StartWhale(StatusInstance status, object host)
    {
        var m = Get(host as BattleEntity); if (m == null) return;
        m.WhaleTurn = Turn + 1; m.WhalePhase = Phase(host as BattleEntity);
    }
    private static void StartEntry(StatusInstance status, object host)
    {
        var owner = host as BattleEntity; var m = new Memory(); Memories.Add(status, m);
        m.Listeners.Add(BattleEventHub.Subscribe<SwitchEventData>(BattleEventKind.SwitchResolved, (_, change) => {
            if (change.Outgoing != null) m.ActiveTurns[change.Outgoing] = Turn;
            if (change.Subject != null) m.ActiveTurns[change.Subject] = Turn;
            if (owner.EntityID == 2073) { Request(status, owner, "ChildeClearEarly()", change.Subject); Request(status, owner, "ChildeClearFinal()", change.Subject); }
        }, status.ApplyOrder, () => status.IsActive && owner.IsAlive));
        if (owner.EntityID == 2073)
        {
            m.PhaseExit = () => {
                m.WhaleTurn = m.LeapTurn = 0;
                m.LeapTarget = m.LeapRelease = null;
                m.ShieldHits.Clear();
                m.ShieldDue = Turn;
            };
            owner.EnemyCtrl.HealthPhaseExited += m.PhaseExit;
            m.Listeners.Add(BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, damage) => {
                if (damage.Subject == owner)
                {
                    Request(status, owner, "ChildeCounterHydro()", damage.Source?.SourceEntity);
                    Request(status, owner, "ChildeCounterElectro()", damage.Source?.SourceEntity);
                }
                if (damage.Source?.SourceEntity != owner || damage.Source.SourceKind != ReactionSourceKind.EnemySkill || !damage.HitLanded) return;
                Request(status, owner, "ChildeHydroGain()", damage.Subject);
                Request(status, owner, "ChildeElectroGain()", damage.Subject);
                foreach (string id in new[] { Hydro, Electro })
                    foreach (var mark in damage.Subject.GetStatusInstances(id).Where(s => s.IsActive && s.Caster == owner).ToArray())
                        Request(mark, damage.Subject, id == Hydro ? "ChildeHydroTrigger()" : "ChildeElectroTrigger()", damage.Subject);
            }, status.ApplyOrder, () => status.IsActive && owner.IsAlive));
        }
    }
    private static void Stop(StatusInstance status, object host)
    {
        if (Memories.TryGetValue(status, out var m))
        {
            foreach (var listener in m.Listeners) listener.Dispose();
            if (host is BattleEntity owner && owner.EnemyCtrl != null)
                owner.EnemyCtrl.HealthPhaseExited -= m.PhaseExit;
        }
        Memories.Remove(status);
    }
    private static void Request(StatusInstance status, object host, string hook, BattleEntity target)
    {
        if (status?.Caster?.EnemyCtrl == null || DataManager.Instance == null
            || !DataManager.Instance.StatusActionDict.TryGetValue(status.StatusID2, out var actions)) return;
        foreach (var action in actions)
            if (action.ActionType == "OnTrigger" && action.ScriptHook == hook)
            {
                var cause = BattleEventHub.Current;
                var fact = cause?.Get<DamageEventData>();
                status.Caster.EnemyCtrl.TryExecuteEventStatusAction(status, action, host, new ScriptHookContext {
                    Caster = status.Caster, Status = status, StatusHost = host, Target = target, Declaration = cause,
                    DamageEvent = fact == null ? null : new DamageResolvedEvent { Target = fact.Subject, Source = fact.Source,
                        HitLanded = fact.HitLanded, ActualHPDamage = fact.ActualHPDamage, RuntimeHitID = cause.Header.HitId } });
            }
    }
    private static bool DirectPlayerHit(ScriptHookContext c) => c?.DamageEvent?.HitLanded == true
        && c.DamageEvent.Source?.SourceKind == ReactionSourceKind.CharacterSkill
        && c.DamageEvent.Source.SourceSide == BattleSide.Ally && c.Declaration?.Header.ActionExecutionId > 0;
    private static bool Counter(ScriptHookContext c, int phase) => Phase(c?.Caster) == phase && DirectPlayerHit(c)
        && Get(c.Caster)?.ShieldHits.Contains(c.DamageEvent.RuntimeHitID) == true;
    private static bool Clearable(ScriptHookContext c)
    {
        var change = c?.Declaration?.Get<SwitchEventData>();
        return change?.Subject?.Side == BattleSide.Ally && (Phase(c.Caster) == 3 ? AnyMark(change.Subject, c.Caster)
            : Marked(change.Subject, c.Caster, Phase(c.Caster) == 1 ? Hydro : Electro));
    }
    private static bool Gain(ScriptHookContext c, bool water)
    {
        string skill = c?.DamageEvent?.Source?.SourceSkillID;
        return c?.DamageEvent?.HitLanded == true && c.DamageEvent.Source.SourceEntity == c.Caster
            && (water ? new[] { "SK_Childe_HydroWave", "SK_Childe_FinalShots" }.Contains(skill)
                : new[] { "SK_Childe_ElectroDashes", "SK_Childe_FinalTwin" }.Contains(skill));
    }
    private static bool Trigger(ScriptHookContext c, string mark)
    {
        var damage = c?.DamageEvent;
        if (damage?.HitLanded != true || damage.Source?.SourceEntity != c.Caster
            || damage.Source.SourceKind != ReactionSourceKind.EnemySkill || !ReferenceEquals(damage.Target, c.StatusHost)) return false;
        string skill = damage.Source.SourceSkillID;
        if (new[] { "SK_Childe_FinalVortex", "SK_Childe_FinalDashes", "SK_Childe_FinalLeapRelease" }.Contains(skill)) return true;
        return mark == Hydro ? new[] { "SK_Childe_Airstrike", "SK_Childe_WhaleRelease", "SK_Childe_FinalWhaleRelease" }.Contains(skill)
            : new[] { "SK_Childe_Flicker", "SK_Childe_LeapRelease" }.Contains(skill);
    }
    private static IDisposable BeforeHit(long hit, BattleEntity source, IEnumerable<BattleEntity> targets)
    {
        var frame = Active.Value;
        if (frame?.Owner != source || !ActiveActionQuery.IsActiveAction(source, frame.Skill)
            || BattleResolutionContext.CurrentExecution?.IndependentStatus != null) return null;
        foreach (var owner in targets.Distinct().Where(t => t?.EntityID == 2073 && t.Side == BattleSide.Enemy && t.IsAlive))
        {
            var m = Get(owner); int phase = Phase(owner);
            if (m == null || phase > 2) continue;
            string shield = phase == 1 ? "ST_Childe_HydroShield" : "ST_Childe_ElectroShield";
            if (!owner.HasStatus(shield) && Turn >= m.ShieldDue && !owner.HasStatus("ST_Childe_HydroWeakness")
                && !owner.HasStatus("ST_Childe_ElectroWeakness") && BattleRandom.NextFloat01() < .1f)
            {
                owner.AddStatus(shield, owner, 0, 6, 0, DataManager.Instance.StatusMainDict[shield]);
                m.ShieldDue = Turn + 2;
            }
            if (owner.HasStatus(shield)) m.ShieldHits.Add(hit);
        }
        return null;
    }
    private static int? SelectReserved(EnemyBattleController controller)
    {
        var owner = controller?.Entity; var m = Get(owner);
        if (m == null) return null;
        if (owner.EntityID == 2071 && m.FinalePending) return 207105;
        if (m.WhaleTurn > 0 && Turn >= m.WhaleTurn)
        {
            m.WhaleTurn = 0;
            return owner.EntityID == 2071 ? 207107 : m.WhalePhase == 1 ? 207310 : 207334;
        }
        if (owner.EntityID != 2073) return null;
        if (m.LeapTurn > 0 && Turn >= m.LeapTurn)
        {
            var target = m.LeapTarget; m.LeapTurn = 0; m.LeapTarget = null;
            owner.RemoveStatus(Leap);
            if (target?.IsOnField == true && (Phase(owner) == 2 ? Marked(target, owner, Electro) : AnyMark(target, owner)))
            { m.LeapRelease = target; return Phase(owner) == 2 ? 207320 : 207335; }
            m.LeapRelease = null; // 条件失败：同一次分配直接走正常权重，不空耗机会。
        }
        if (Phase(owner) == 1 && !Allies().Any(u => Marked(u, owner, Hydro))) return 207304;
        if (Phase(owner) == 2 && !Allies().Any(u => Marked(u, owner, Electro))) return 207315;
        return null;
    }
    private static BattleEntity PickWeighted(List<BattleEntity> candidates)
    {
        if (candidates.Any(BattleTargetResolver.IsTaunter)) candidates = candidates.Where(BattleTargetResolver.IsTaunter).ToList();
        float sum = candidates.Sum(t => Math.Max(0f, t.HitWeight)); if (sum <= 0f) return null;
        float roll = BattleRandom.NextFloat01() * sum;
        foreach (var target in candidates) { roll -= Math.Max(0f, target.HitWeight); if (roll < 0f) return target; }
        return candidates[candidates.Count - 1];
    }
    internal sealed class RiftCounter : CountStatusHandler
    {
        public override bool UseStandardApplication => true;
        public override bool SupportsEffectChange => true;
        public override bool EffectUsesIntegerValue => true;
        public override void OnCreated(StatusInstance status, object host) => status.RuntimeIntValue = 0;
        public override void ChangeFromEffect(StatusInstance status, BattleEntity host, CountChangeRequest request)
            => request.ApplyInteger(status, host, 0, 100);
    }
}
