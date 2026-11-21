using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

/// <summary>冰系普通敌人的释放资格、入场时钟与实际参与单位；数值和状态操作均读取效果表。</summary>
public static class Enemy2074To2080Hooks
{
    public const string MageEntry = "ST_CryoCicinMage_Entry", MageCharge = "ST_CryoCicinMage_ChargeShield";
    public const string MageRingShield = "ST_CryoCicinMage_RingShield", MageVolley = "SE_CryoCicinMage_Volley_Cicins";
    public const string Pillar = "ST_CryoShaman_PillarShield", GuardShield = "ST_CryoGuard_Shield";
    public const string WallShield = "ST_Cryowall_Shield", WallWait = "ST_Cryowall_Rebuild";
    public const string FrostEntry = "ST_Frostarm_Entry", FrostArmor = "ST_Frostarm_Armor";
    private sealed class Clock
    {
        internal int Due;
        internal bool First = true;
        internal IDisposable Action;
    }
    private static readonly ConditionalWeakTable<StatusInstance, Clock> Clocks = new ConditionalWeakTable<StatusInstance, Clock>();
    private static int Turn => Math.Max(1, BattleManager.Instance?.TurnCount ?? 1);
    internal static void Register()
    {
        Entry(MageEntry, 2, true); Entry(FrostEntry, 1, false);
        foreach (var id in new[] { MageCharge, MageRingShield })
            BattleSpecialRules.RegisterStatusLifecycle(id, null, (s, host) => {
                var clock = Get(host as BattleEntity, MageEntry); if (clock != null) clock.Due = Turn + 3;
            });
        BattleSpecialRules.RegisterStatusLifecycle(WallWait, (s, _) => s.EarliestTimerTurn = Turn + 1, null);
        Condition("CryoGuardUnshielded", e => !e.HasStatus(GuardShield));
        Condition("CryowallUnshielded", e => !e.HasStatus(WallShield));
        Condition("CryoShamanCanPillar", e => CanPillar(BattleManager.Instance, e));
        Condition("CryoCicinShieldDue", MageShieldDue);
        Condition("CryoCicinMaySummon", e => !MageShieldDue(e) && CanSummon());
        Condition("CryoCicinMayAttack", e => !MageShieldDue(e) && !(Get(e, MageEntry)?.First == true
            && CanSummon() && e.EnemyCtrl.MeetsSkillLimits(207801)));
        Condition("FrostarmArmorDue", ArmorDue);
        Condition("FrostarmNormal", e => !ArmorDue(e));
        Condition("FrostarmUnshielded", e => !e.HasStatus(FrostArmor));
        BattleSpecialRules.RegisterCondition("FrostarmPunchHit", (_, c) => c?.DamageEvent?.HitLanded == true
            && c.DamageEvent.Source?.SourceEntity == c.Caster
            && c.DamageEvent.Source.SourceKind == ReactionSourceKind.EnemySkill
            && c.DamageEvent.Source.SourceSkillID == "SK_Frostarm_Punch");
        BattleSpecialRules.RegisterTargets("FrostarmPunchNeighbors()", (battle, _) => {
            var target = ScriptHookContext.Current?.DamageEvent?.Target;
            return target != null && battle != null && target.Side == BattleSide.Ally && target.SlotPosition > 0
                ? battle.Allies.Where(c => c?.Entity?.IsOnField == true
                    && Math.Abs(c.Entity.SlotPosition - target.SlotPosition) <= 1).Select(c => c.Entity.SlotPosition).ToList()
                : new List<int>();
        });
        BattleSpecialRules.RegisterTargets("CryoShamanPillarTargets()", PillarTargets);
        BattleSpecialRules.RegisterEnemyDamageParticipants(MageVolley, (battle, owner) =>
            battle.Enemies.Where(c => c?.EnemyID == 2074 && c.Entity.IsOnField && c.Entity.Summoner == owner)
                .OrderBy(c => c.Entity.SlotPosition).ToList());
    }
    private static void Condition(string name, Func<BattleEntity, bool> test)
        => BattleSpecialRules.RegisterCondition(name, (_, c) => c?.Caster?.EnemyCtrl != null && test(c.Caster));
    private static void Entry(string id, int delay, bool firstAction)
    {
        BattleSpecialRules.RegisterStatusLifecycle(id, (s, host) => {
            var clock = new Clock { Due = Turn + delay }; Clocks.Add(s, clock);
            if (firstAction) clock.Action = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted, (_, action) => {
                if (action.Subject != host) return;
                clock.First = false; clock.Action?.Dispose();
            }, s.ApplyOrder, () => s.IsActive);
        }, (s, _) => { if (Clocks.TryGetValue(s, out var clock)) clock.Action?.Dispose(); Clocks.Remove(s); });
    }
    private static Clock Get(BattleEntity e, string id)
    { var s = e?.GetStatus(id); return s != null && Clocks.TryGetValue(s, out var c) ? c : null; }
    private static bool CanSummon() => Enemy2021To2030Hooks.EmptyEnemyPositions(BattleManager.Instance).Count >= 2;
    private static bool MageShieldDue(BattleEntity e) => Turn >= (Get(e, MageEntry)?.Due ?? int.MaxValue)
        && e.EnemyCtrl.IsLastGuaranteedAction && !e.HasStatus(MageCharge) && !e.HasStatus(MageRingShield);
    private static bool ArmorDue(BattleEntity e) => Turn >= (Get(e, FrostEntry)?.Due ?? int.MaxValue)
        && e.EnemyCtrl.MeetsSkillLimits(208002);
    public static List<int> PillarTargets(BattleManager battle, BattleEntity caster) => battle == null || caster == null
        ? new List<int>() : battle.Enemies.Where(c => c?.Entity?.IsOnField == true && Enemy2011To2020Hooks.IsPillarTarget(c.Entity))
            .Select(c => c.Entity.SlotPosition).ToList();
    public static bool CanPillar(BattleManager battle, BattleEntity caster) => battle != null && caster != null
        && !battle.Enemies.Any(c => c?.Entity?.IsAlive == true && c.Entity.GetEnemyElementalShield()?.SourceStatusID2 == Pillar)
        && PillarTargets(battle, caster).Count > 0;
}
