using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>两种树、狼王及纯水精灵的资格和事件桥接。伤害与状态变化由配表效果提交。</summary>
public static class Enemy2067To2070Hooks
{
    public const string MimicFamily = "水幻形生物";
    public const string WolfEntry = "ST_Andrius_Entry", Trial = "ST_Andrius_Trial";
    public const string Approval = "ST_Andrius_Approval", Interlude = "ST_Andrius_Interlude";
    public const string OceanidEntry = "ST_Oceanid_Entry";
    private sealed class Memory
    {
        internal bool TransitionUsed, TransitionPending, Protected;
        internal IDisposable Listener;
    }
    private sealed class AttackFrame
    {
        internal BattleEntity Actor;
        internal SkillMainData Skill;
        internal bool HasAttack, Settled;
        internal List<StatusInstance> Approvals;
        internal readonly Dictionary<BattleEntity, BattleDeclaration> Wolves = new Dictionary<BattleEntity, BattleDeclaration>();
    }
    private sealed class Scope : IDisposable
    {
        private readonly AttackFrame previous;
        internal Scope(AttackFrame frame) { previous = Current.Value; Current.Value = frame; }
        public void Dispose() { Current.Value = previous; }
    }
    private static readonly AsyncLocal<AttackFrame> Current = new AsyncLocal<AttackFrame>();
    private static readonly ConditionalWeakTable<StatusInstance, Memory> Memories = new ConditionalWeakTable<StatusInstance, Memory>();
    private static string Prefix(BattleEntity owner) => owner?.EntityID == 2067 ? "ST_CryoRegisvine_" : "ST_PyroRegisvine_";

    internal static void Register()
    {
        foreach (string id in new[] { "ST_CryoRegisvine_CoreAge", "ST_PyroRegisvine_CoreAge", "ST_CryoRegisvine_RegenWait", "ST_PyroSeed_Fuse" })
            BattleSpecialRules.RegisterStatusLifecycle(id, (s, _) => {
                s.EarliestTimerTurn = Math.Max(1, BattleManager.Instance?.TurnCount ?? 1) + 1;
            }, null);
        foreach (string id in new[] { "ST_CryoRegisvine_Weakness", "ST_PyroRegisvine_Weakness" })
            BattleSpecialRules.RegisterStatusLifecycle(id, (s, _) => {
                var battle = BattleManager.Instance;
                if (battle != null && (int)battle.CurrentPhase == 6) s.EarliestTimerTurn = battle.TurnCount + 1;
            }, null);
        BattleSpecialRules.RegisterCondition("RegisvineShieldPresent", (_, c) => c?.Caster?.HasStatus(Prefix(c.Caster) + "Core") == true);
        BattleSpecialRules.RegisterCondition("RegisvineRegenReady", (_, c) => c?.Caster?.HasStatus("ST_CryoRegisvine_RegenReady") == true
            && !c.Caster.HasStatus("ST_CryoRegisvine_Core"));
        BattleSpecialRules.RegisterCondition("RegisvineNormal", (_, c) => c?.Caster?.HasStatus("ST_CryoRegisvine_RegenReady") != true);
        BattleSpecialRules.RegisterCondition("RegisvineCanSeed", (_, c) => AdjacentEmpty(c?.Caster).Count > 0
            && BattleManager.Instance?.Enemies.Any(enemy => enemy?.Entity?.IsOnField == true
                && enemy.Entity.EntityID == 2610) == false);
        BattleSpecialRules.RegisterCount("EnemyAdjacentEmptyCount", owner => AdjacentEmpty(owner).Count);
        BattleSpecialRules.RegisterTargets("EnemyAdjacentEmpty()", (_, owner) => AdjacentEmpty(owner));
        BattleSpecialRules.RegisterTargets("WolfActionActor()", (_, owner) => ActorPosition(ScriptHookContext.Current?.Target));
        BattleSpecialRules.RegisterTargets("WolfApprovalField()", (_, owner) => ActorPosition(ScriptHookContext.Current?.StatusHost as BattleEntity));
        BattleSpecialRules.RegisterTargets("WolfIcicleField()", (battle, owner) => {
            var victim = ScriptHookContext.Current?.DamageEvent?.Target;
            return victim != null && battle?.GetEntityByPosition(victim.Side, victim.SlotPosition) == victim
                ? new List<int> { victim.SlotPosition } : new List<int>();
        });
        BattleSpecialRules.RegisterCondition("WolfIcicleHit", (_, c) => c?.DamageEvent?.HitLanded == true
            && c.DamageEvent.Source?.SourceSkillID == "SK_Andrius_Icicles");
        BattleSpecialRules.RegisterCondition("WolfTrialFull", (_, c) => c?.Target?.GetStatusInstances(Trial).Count(s => s.IsActive) >= 3);
        BattleSpecialRules.RegisterCondition("WolfOwnDamage", (_, c) => c?.DamageAttacker != null && c.DamageAttacker == c.Status?.Caster);
        BattleSpecialRules.RegisterCondition("WolfApprovalAttack", (_, c) => Current.Value is AttackFrame frame
            && c?.Recipient == frame.Actor && c.DamageSkillID == frame.Skill.SkillID2);
        BattleSpecialRules.RegisterCondition("WolfTrialSettled", (_, c) => c?.Target?.IsAlive == true && c.Caster?.IsAlive == true);
        BattleSpecialRules.RegisterCondition("WolfInterludePending", (_, c) => State(c?.Caster)?.TransitionPending == true);
        BattleSpecialRules.RegisterCondition("WolfReleaseInterlude", (_, c) => c?.Declaration?.Get<ActionEventData>()?.Subject == c.Caster
            && c.Caster.HasStatus(Interlude));
        BattleSpecialRules.RegisterStatusLifecycle(WolfEntry, OnWolfEntry, Stop);
        BattleSpecialRules.RegisterStatusLifecycle(Interlude, (s, host) => {
            var memory = State(host as BattleEntity);
            if (memory == null) return;
            memory.TransitionUsed = true; memory.TransitionPending = false; memory.Protected = true;
        }, (_, host) => { var memory = State(host as BattleEntity); if (memory != null) memory.Protected = false; });
        BattleSpecialRules.RegisterHPDamageGuard(GuardWolfHP);
        BattleSpecialRules.RegisterSkillExecutionScope((actor, skill) => new Scope(actor?.Side == BattleSide.Ally
            && ActiveActionQuery.IsActiveAction(actor, skill) ? new AttackFrame {
                Actor = actor, Skill = skill, Approvals = actor.GetStatusInstances(Approval).Where(s => s.IsActive).ToList()
            } : null));
        BattleSpecialRules.RegisterDeclarationObserver(RecordAttack);
        BattleSpecialRules.RegisterSkillSettled(SettleAttack);

        BattleSpecialRules.RegisterCondition("OceanidCanSummon", (_, c) => c?.Caster?.IsAlive == true
            && Enemy2021To2030Hooks.EmptyEnemyPositions(BattleManager.Instance).Count >= 2
            && EnemySummonSelection.FamilyPool(MimicFamily).Count >= 2 && Mimics().Count == 0);
        BattleSpecialRules.RegisterCondition("OceanidCanHeal", (_, c) => Mimics().Count > 0);
        BattleSpecialRules.RegisterTargets("OceanidHealTarget()", (_, owner) => {
            var pool = Mimics();
            return pool.Count == 0 ? new List<int>() : new List<int> { pool[BattleRandom.NextInt(0, pool.Count)].SlotPosition };
        });
        BattleSpecialRules.RegisterCondition("OceanidMimicDeath", (_, c) => IsMimic(c?.Declaration?.Get<DeathEventData>()?.Subject));
        BattleSpecialRules.RegisterFixedStatusAmount("OceanidMimicLoss()", (_, status, host) => (host as BattleEntity ?? status.Caster).GetFinalHP() * .18f);
        BattleSpecialRules.RegisterFixedStatusAmount("SummonShieldCollapse()", (_, status, host) => (host as BattleEntity ?? status.Caster).CurrentHP);
        BattleSpecialRules.RegisterStatusLifecycle(OceanidEntry, (s, host) => {
            var memory = new Memory(); Memories.Add(s, memory);
            memory.Listener = BattleEventHub.Subscribe<DeathEventData>(BattleEventKind.Death, (_, fact) => {
                if (IsMimic(fact.Subject)) Request(s, host, "OceanidMimicDeath()", fact.Subject, BattleEventHub.Current);
            }, s.ApplyOrder, () => s.IsActive && s.Caster?.IsAlive == true);
        }, Stop);
    }

    private static void OnWolfEntry(StatusInstance status, object host)
    {
        var memory = new Memory(); Memories.Add(status, memory);
        memory.Listener = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted, (_, action) => {
            if (ReferenceEquals(action.Subject, host)) Request(status, host, "WolfReleaseInterlude()", action.Subject, BattleEventHub.Current);
        }, status.ApplyOrder, () => status.IsActive && status.Caster?.IsAlive == true);
    }
    private static void Stop(StatusInstance status, object host)
    { if (Memories.TryGetValue(status, out var memory)) memory.Listener?.Dispose(); Memories.Remove(status); }
    private static Memory State(BattleEntity owner)
    { var s = owner?.GetStatus(WolfEntry); return s != null && Memories.TryGetValue(s, out var memory) ? memory : null; }
    private static float GuardWolfHP(BattleEntity target, float amount)
    {
        var memory = State(target);
        if (memory == null || amount <= 0f || memory.TransitionUsed && !memory.Protected) return amount;
        float available = Math.Max(0f, target.CurrentHP - target.GetFinalHP() * .55f);
        if (!memory.TransitionUsed && amount >= available)
        { memory.TransitionPending = true; memory.Protected = true; }
        return memory.Protected ? Math.Min(amount, available) : amount;
    }
    private static void RecordAttack(BattleDeclaration declaration)
    {
        var frame = Current.Value;
        if (frame == null || !(declaration.Data is DamageEventData damage) || !damage.HitLanded) return;
        var source = damage.Source;
        if (source.SourceKind != ReactionSourceKind.CharacterSkill || source.SourceEntity != frame.Actor
            || source.SourceSkillID != frame.Skill.SkillID2) return;
        frame.HasAttack = true;
        if (damage.Subject?.EntityID == 2069 && damage.Subject.Side == BattleSide.Enemy)
            frame.Wolves[damage.Subject] = declaration;
    }
    private static void SettleAttack(BattleEntity actor, SkillMainData skill, BattleDeclaration completed)
    {
        var frame = Current.Value;
        if (frame == null || frame.Actor != actor || frame.Skill != skill || frame.Settled || !frame.HasAttack) return;
        frame.Settled = true;
        // 先消费本次开始时已有的认可，再给本次实际命中的狼王各结算一层；第三次攻击不能提前吃到新认可。
        BattleExecution.Enqueue(async () => {
            foreach (var approval in frame.Approvals)
            {
                if (!actor.HasStatusInstance(approval)) continue;
                Request(approval, actor, string.Empty, actor, completed);
                await BattleExecution.Flush();
            }
            foreach (var hit in frame.Wolves)
            {
                var entry = hit.Key.GetStatus(WolfEntry);
                if (entry == null || !hit.Key.IsAlive || !actor.IsAlive) continue;
                Request(entry, hit.Key, "WolfTrialSettled()", actor, hit.Value);
                await BattleExecution.Flush();
            }
        });
    }
    private static void Request(StatusInstance status, object host, string hook, BattleEntity target, BattleDeclaration cause)
    {
        if (status?.Caster?.EnemyCtrl == null || !DataManager.Instance.StatusActionDict.TryGetValue(status.StatusID2, out var actions)) return;
        foreach (var action in actions)
            if (action.ActionType == "OnTrigger" && (action.ScriptHook ?? string.Empty) == hook)
                status.Caster.EnemyCtrl.TryExecuteEventStatusAction(status, action, host,
                    new ScriptHookContext { Caster = status.Caster, Status = status, StatusHost = host, Target = target, Declaration = cause });
    }
    private static List<int> ActorPosition(BattleEntity actor)
        => actor?.IsAlive == true && BattleManager.Instance?.GetEntityByPosition(actor.Side, actor.SlotPosition) == actor
            ? new List<int> { actor.SlotPosition } : new List<int>();
    public static List<int> AdjacentEmpty(BattleEntity owner)
    {
        var free = Enemy2021To2030Hooks.EmptyEnemyPositions(BattleManager.Instance);
        return owner == null ? new List<int>() : free.Where(p => Math.Abs(p - owner.SlotPosition) == 1).ToList();
    }
    private static bool IsMimic(BattleEntity unit) => unit?.Side == BattleSide.Enemy && EnemyTraits.HasFamily(unit, MimicFamily);
    private static List<BattleEntity> Mimics()
        => BattleManager.Instance?.Enemies.Where(c => c?.Entity?.IsOnField == true && IsMimic(c.Entity))
            .Select(c => c.Entity).ToList() ?? new List<BattleEntity>();
}
