using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

/// <summary>第六批敌人共用原有技能、护盾和状态执行器。这里只处理表无法单独判定的资格和实例。</summary>
public static class Enemy2051To2060Hooks
{
    public const string Clone = "ST_PyroAgent_Clone";
    public const string CicinVolley = "SE_CicinMage_Volley_Cicins";
    private const string MageClock = "ST_CicinMage_Entry";
    private const string StoneClock = "ST_Stonehide_Entry";
    private const string Stealth = "ST_PyroAgent_Stealth";
    private const int StealthBinding = 420600204;
    private const string Revealed = "ST_PyroAgent_Revealed";
    private const string Blades = "ST_PyroAgent_Blades";
    private const string Charging = "ST_CicinMage_ChargeShield";
    private const string RingShield = "ST_CicinMage_RingShield";
    private const string AgentWatch = "ST_PyroAgent_CloneWatch";
    private sealed class VisibilityWatch
    {
        internal IDisposable Added;
        internal IDisposable Removed;
    }
    private static readonly ConditionalWeakTable<StatusInstance, VisibilityWatch> VisibilityWatches = new ConditionalWeakTable<StatusInstance, VisibilityWatch>();
    private sealed class Entry
    {
        internal int Due;
        internal bool First = true;
        internal IDisposable FirstAction;
    }
    private sealed class CloneField
    {
        internal int Instances;
        internal IDisposable Action;
    }
    private static readonly ConditionalWeakTable<StatusInstance, Entry> Entries = new ConditionalWeakTable<StatusInstance, Entry>();
    private static readonly ConditionalWeakTable<FieldPosition, CloneField> CloneFields = new ConditionalWeakTable<FieldPosition, CloneField>();
    private static int Turn => Math.Max(1, BattleManager.Instance?.TurnCount ?? 1);

    internal static void Register()
    {
        BattleSpecialRules.RegisterEnemyDamageParticipants(CicinVolley, (battle, _) => CicinParticipants(battle).ToList());
        RegisterEntry(StoneClock, 1);
        RegisterEntry(MageClock, 2);
        BattleSpecialRules.RegisterStatusLifecycle(Charging, null, ShieldLost);
        BattleSpecialRules.RegisterStatusLifecycle(RingShield, null, ShieldLost);
        foreach (string id in new[] { "ST_Rockwall_Rebuild", "ST_Woodwall_Rebuild" })
            BattleSpecialRules.RegisterStatusLifecycle(id, (s, _) => s.EarliestTimerTurn = Turn + 1, null);
        Condition("StoneArmorDue", StoneArmorDue);
        Condition("StoneAttackReady", e => !StoneArmorDue(e));
        BattleSpecialRules.RegisterCondition("StonePunchHit", (_, c) => c?.DamageEvent?.HitLanded == true
            && c.DamageEvent.Source?.SourceEntity == c.Caster && c.DamageEvent.Source.SourceEffectID == "SE_Stonehide_Punch_Damage");
        BattleSpecialRules.RegisterTargets("StonePunchNeighbors()", (_, __) => {
            var target = ScriptHookContext.Current?.DamageEvent?.Target;
            return target == null ? new List<int>() : TargetResolver.ApplyPreviousRange(new[] { target.SlotPosition }, 1, 1, true,
                BattlePositionSystem.GetSideCount(BattleSide.Ally));
        });
        Condition("EyeCanCharge", e => e.EnemyCtrl.IsLastGuaranteedAction);
        Condition("EyeCanWindWall", e => !BattleManager.Instance.Enemies.Any(c => c?.Entity?.IsAlive == true && c.Entity.HasStatus("ST_StormEye_Wall")));
        Condition("CicinShieldDue", ShieldDue);
        Condition("CicinMaySummon", e => !ShieldDue(e) && CanSummon());
        Condition("CicinMayAttack", e => !ShieldDue(e) && !(GetEntry(e, MageClock)?.First == true && CanSummon() && e.EnemyCtrl.MeetsSkillLimits(205901)));
        Condition("AgentBurstDue", BurstDue);
        Condition("AgentBladesDue", e => !BurstDue(e) && !e.HasStatus(Blades));
        Condition("AgentMayAttack", e => !BurstDue(e) && e.HasStatus(Blades));
        Condition("AgentMayHide", e => e.HasStatus(Blades) && !PublicControlStates.IsHidden(e) && !e.HasStatus(Stealth) && !e.HasStatus(Revealed));
        Condition("AgentHidden", e => PublicControlStates.IsHidden(e) && !e.HasStatus(Revealed));
        BattleSpecialRules.RegisterCondition("AgentBecameHidden", (_, c) => VisibilityChanged(c) && PublicControlStates.IsHidden(c.Caster));
        BattleSpecialRules.RegisterCondition("AgentBecameVisible", (_, c) => VisibilityChanged(c) && !PublicControlStates.IsHidden(c.Caster));
        BattleSpecialRules.RegisterStatusLifecycle(AgentWatch, WatchVisibility, StopWatchingVisibility);
        BattleSpecialRules.RegisterStatusLifecycle(Revealed, SuspendStealth, RestoreStealth);
        Condition("RockwallUnshielded", e => !e.HasStatus("ST_Rockwall_Shield"));
        Condition("WoodwallUnshielded", e => !e.HasStatus("ST_Woodwall_Shield"));
        Condition("StoneUnshielded", e => !e.HasStatus("ST_Stonehide_Armor"));
        BattleSpecialRules.RegisterCondition("AgentAttackHit", (_, c) => IsAgentAttack(c));
        BattleSpecialRules.RegisterCondition("AgentCloneHit", (_, c) => IsAgentAttack(c)
            && (c.DamageEvent.Source.SourceSkillID == "SK_PyroAgent_Thrust" || c.DamageEvent.Source.SourceSkillID == "SK_PyroAgent_Shadow"));
        BattleSpecialRules.RegisterTargets("AgentHitSlot()", (_, __) => {
            var target = ScriptHookContext.Current?.DamageEvent?.Target;
            return target == null ? new List<int>() : new List<int> { target.SlotPosition };
        });
        BattleSpecialRules.RegisterStatusLifecycle(Clone, AddClone, RemoveClone);
        for (int slot = 1; slot <= BattlePositionSystem.GetSideCount(BattleSide.Ally); slot++)
        {
            int index = slot;
            BattleSpecialRules.RegisterCount("AgentClonesSlot" + index, _ => CountClones(BattleManager.Instance?.Field?.GetSlot(BattleSide.Ally, index)));
        }
    }

    private static void Condition(string name, Func<BattleEntity, bool> predicate)
        => BattleSpecialRules.RegisterCondition(name, (_, ctx) => ctx?.Caster?.EnemyCtrl != null && predicate(ctx.Caster));

    private static bool IsAgentAttack(ScriptHookContext c)
    {
        var hit = c?.DamageEvent;
        return hit?.HitLanded == true && hit.Target?.Side == BattleSide.Ally && hit.Source?.SourceEntity == c.Caster
            && hit.Source.SourceKind == ReactionSourceKind.EnemySkill;
    }

    private static bool CanSummon() => Enemy2021To2030Hooks.EmptyEnemyPositions(BattleManager.Instance).Count >= 2;

    internal static IEnumerable<EnemyBattleController> CicinParticipants(BattleManager battle)
        => battle.Enemies.Where(fly => fly?.Entity?.IsOnField == true && fly.EnemyID == 2035)
            .OrderBy(fly => fly.Entity.SlotPosition).ToList();

    private static void RegisterEntry(string id, int delay)
    {
        BattleSpecialRules.RegisterStatusLifecycle(id, (status, host) => {
            var entry = new Entry { Due = Turn + delay };
            Entries.Add(status, entry);
            if (id == MageClock)
                entry.FirstAction = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted, (_, action) => {
                    if (action.Subject != host) return;
                    entry.First = false;
                    entry.FirstAction?.Dispose();
                }, status.ApplyOrder);
        }, (status, _) => {
            if (Entries.TryGetValue(status, out var entry)) entry.FirstAction?.Dispose();
            Entries.Remove(status);
        });
    }

    private static Entry GetEntry(BattleEntity owner, string id)
    {
        var state = owner?.GetStatus(id);
        return state != null && Entries.TryGetValue(state, out var memory) ? memory : null;
    }

    private static void ShieldLost(StatusInstance shield, object host)
    {
        var entry = GetEntry(host as BattleEntity, MageClock);
        if (entry != null) entry.Due = Turn + 3;
    }

    private static bool StoneArmorDue(BattleEntity e)
        => Turn >= (GetEntry(e, StoneClock)?.Due ?? int.MaxValue) && e.EnemyCtrl.MeetsSkillLimits(205702);

    private static bool ShieldDue(BattleEntity e)
        => Turn >= (GetEntry(e, MageClock)?.Due ?? int.MaxValue) && e.EnemyCtrl.IsLastGuaranteedAction
            && !e.HasStatus(Charging) && !e.HasStatus(RingShield);

    private static bool VisibilityChanged(ScriptHookContext c) => c?.Declaration?.Data is StatusEventData fact
        && fact.Host == c.Caster && fact.StatusId == PublicControlStates.EnemyStealth;

    private static void WatchVisibility(StatusInstance status, object host)
    {
        var owner = status.Caster;
        void Changed(BattleEventHeader header, StatusEventData fact)
        {
            if (fact.Host != host || fact.StatusId != PublicControlStates.EnemyStealth || !owner.IsAlive) return;
            bool hidden = PublicControlStates.IsHidden(owner);
            if (owner.EnemyCtrl.CurrentPatternID == (hidden ? 2 : 1)) return;
            if (!DataManager.Instance.StatusActionDict.TryGetValue(AgentWatch, out var actions)) return;
            string hook = hidden ? "AgentBecameHidden()" : "AgentBecameVisible()";
            foreach (var row in actions)
                if (row.ActionType == "OnTrigger" && row.ScriptHook == hook)
                    owner.EnemyCtrl.TryExecuteEventStatusAction(status, row, host,
                        new ScriptHookContext { Caster = owner, Target = owner, Declaration = BattleEventHub.Current },
                        () => PublicControlStates.IsHidden(owner) == hidden);
        }
        var watch = new VisibilityWatch {
            Added = BattleEventHub.Subscribe<StatusEventData>(BattleEventKind.StatusAdded, Changed, status.ApplyOrder),
            Removed = BattleEventHub.Subscribe<StatusEventData>(BattleEventKind.StatusRemoved, Changed, status.ApplyOrder)
        };
        VisibilityWatches.Add(status, watch);
    }

    private static void StopWatchingVisibility(StatusInstance status, object host)
    {
        if (!VisibilityWatches.TryGetValue(status, out var watch)) return;
        watch.Added.Dispose();
        watch.Removed.Dispose();
        VisibilityWatches.Remove(status);
    }

    // 显形仅解除本次隐身绑定的公共实例。其他来源施加的隐形保留自己的寿命。
    private static void SuspendStealth(StatusInstance reveal, object host)
    {
        if (!(host is BattleEntity owner)) return;
        var parent = owner.GetStatus(Stealth);
        if (parent == null) return;
        foreach (var child in new List<StatusInstance>(parent.BoundStatuses))
        {
            if (child.StatusID2 != PublicControlStates.EnemyStealth) continue;
            parent.BoundStatuses.Remove(child);
            owner.RemoveStatusInstance(child, "Revealed");
        }
    }

    private static void RestoreStealth(StatusInstance reveal, object host)
    {
        if (!(host is BattleEntity owner) || !owner.IsAlive || owner.HasStatus(Revealed)) return;
        var parent = owner.GetStatus(Stealth);
        if (parent == null || !parent.IsActive || parent.DeathCleanupPending || parent.RemainingPhaseCount <= 0) return;
        StatusBindingSystem.ApplyBinding(parent, owner, StealthBinding);
    }

    private static bool BurstDue(BattleEntity e)
    {
        if (!PublicControlStates.IsHidden(e)) return false;
        int count = 0;
        foreach (var slot in BattleManager.Instance.Field.AllySlots) count += CountClones(slot);
        return count >= 4;
    }

    private static int CountClones(FieldPosition field)
        => field?.StatusList.FindAll(s => s.IsActive && s.StatusID2 == Clone).Count ?? 0;

    // 每格共用一次响应。多个债务处理人的分身同池，单位行动完成只移除该格最早一份。
    private static void AddClone(StatusInstance status, object host)
    {
        if (!(host is FieldPosition field)) return;
        var memory = CloneFields.GetOrCreateValue(field);
        if (++memory.Instances != 1) return;
        memory.Action = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted, (_, action) => {
            if (action.Subject?.Side != BattleSide.Ally || action.Subject.Position != field) return;
            var oldest = field.StatusList.Find(s => s.IsActive && s.StatusID2 == Clone);
            if (oldest != null) field.RemoveStatusInstance(oldest, "CloneDispelled");
        }, status.ApplyOrder);
    }

    private static void RemoveClone(StatusInstance status, object host)
    {
        if (!(host is FieldPosition field) || !CloneFields.TryGetValue(field, out var memory) || --memory.Instances > 0) return;
        memory.Action.Dispose();
        CloneFields.Remove(field);
    }
}
