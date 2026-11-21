using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>无相三只的技能资格、入场计时和一次性锁血。伤害、Count、治疗仍走公共执行器。</summary>
public static class Enemy2064To2066Hooks
{
    public const string WindEntry = "ST_AnemoHypostasis_Entry";
    public const string ElectroEntry = "ST_ElectroHypostasis_Entry";
    public const string GeoEntry = "ST_GeoHypostasis_Entry";
    public const string WindStorm = "ST_AnemoHypostasis_Tornado";
    public const string ElectroStorm = "ST_ElectroHypostasis_Tremors";
    public const string GeoStorm = "ST_GeoHypostasis_Resonance";
    public const string GeoMark = "ST_GeoHypostasis_Mark";
    public const string PillarShield = "ST_GeoHypostasis_PillarShield";

    private sealed class Entry
    {
        internal int StormDue, PillarDue;
        internal bool RecoveryUsed, RecoveryPending;
    }
    private sealed class Shot { internal BattleEntity Target; internal bool Selected; }
    private static readonly ConditionalWeakTable<StatusInstance, Entry> Entries = new ConditionalWeakTable<StatusInstance, Entry>();
    private static readonly ConditionalWeakTable<BattleResolutionContext.CounterScope, Dictionary<BattleEntity, Shot>> Shots =
        new ConditionalWeakTable<BattleResolutionContext.CounterScope, Dictionary<BattleEntity, Shot>>();
    private static int Turn => Math.Max(1, BattleManager.Instance?.TurnCount ?? 1);

    internal static void Register()
    {
        foreach (string id in new[] { WindEntry, ElectroEntry, GeoEntry })
            BattleSpecialRules.RegisterStatusLifecycle(id, (s, _) =>
                Entries.Add(s, new Entry { StormDue = Turn + 5, PillarDue = Turn }), null);
        foreach (string id in new[] { WindStorm, ElectroStorm, GeoStorm })
            BattleSpecialRules.RegisterStatusLifecycle(id, (s, _) => s.EarliestTimerTurn = Turn + 1, (_, host) => {
                var entry = GetEntry(host as BattleEntity);
                if (entry != null) entry.StormDue = Turn + 5;
            });
        foreach (string id in new[] { "ST_AnemoHypostasis_TornadoEnemyStart", "ST_ElectroHypostasis_TremorsEnemyStart" })
            BattleSpecialRules.RegisterStatusLifecycle(id, (s, _) => s.EarliestTimerTurn = Turn + 1, null);
        BattleSpecialRules.RegisterHPDamageGuard(GuardHP);
        BattleSpecialRules.RegisterCondition("HypostasisRecoveryReady", (_, c) => GetEntry(c?.Caster)?.RecoveryPending == true
            && !c.Caster.HasStatus(HypostasisPublicStates.Recovery));
        BattleSpecialRules.RegisterCondition("HypostasisStormDue", (_, c) => StormDue(c?.Caster) && !PillarsDue(c.Caster));
        BattleSpecialRules.RegisterCondition("HypostasisPillarsDue", (_, c) => PillarsDue(c?.Caster));
        BattleSpecialRules.RegisterCondition("HypostasisNormal", (_, c) => c?.Caster?.IsAlive == true
            && !StormDue(c.Caster) && !PillarsDue(c.Caster));
        BattleSpecialRules.RegisterCondition("HypostasisNoPillars", (_, c) => Pillars(c?.Caster).Count == 0);
        BattleSpecialRules.RegisterCondition("HypostasisExposureBelow20", (_, c) =>
            (c?.Caster?.GetStatus(HypostasisPublicStates.Exposure)?.RuntimeIntValue ?? 0) < 20);
        BattleSpecialRules.RegisterCount("HypostasisEmptySlots", owner => Enemy2021To2030Hooks.EmptyEnemyPositions(BattleManager.Instance).Count);
        BattleSpecialRules.RegisterCount("HypostasisPillarHits", owner => Pillars(owner).Count + 1);
        // 先清零再补20的两个Count效果由同一条件确认；已有20层及以上时不执行它们。
        BattleSpecialRules.RegisterTargets("HypostasisMarkTarget()", (_, owner) => PickAlly(owner));
        BattleSpecialRules.RegisterTargets("HypostasisPelletTarget()", (battle, owner) => {
            var scope = BattleResolutionContext.CurrentEffect;
            if (scope == null) return PickAlly(owner);
            var shots = Shots.GetOrCreateValue(scope);
            if (!shots.TryGetValue(owner, out var shot)) shots.Add(owner, shot = new Shot());
            if (!shot.Selected)
            {
                var chosen = PickAlly(owner, true);
                shot.Target = chosen.Count > 0 ? battle.GetEntityByPosition(BattleSide.Ally, chosen[0]) : null;
                shot.Selected = true;
            }
            return shot.Target?.IsOnField == true
                && battle.GetEntityByPosition(BattleSide.Ally, shot.Target.SlotPosition) == shot.Target
                ? new List<int> { shot.Target.SlotPosition } : new List<int>();
        });
        BattleSpecialRules.RegisterDeclarationObserver(declaration => {
            if (declaration.Data is ActionEventData action && declaration.Kind == BattleEventKind.ActionCompleted
                && action.SkillId == "SK_GeoHypostasis_Pillars")
            {
                var entry = GetEntry(action.Subject);
                if (entry != null) entry.PillarDue = int.MaxValue;
            }
            if (!(declaration.Data is DeathEventData death)) return;
            var dead = death.Subject;
            if (dead?.EntityID == 2609)
            {
                var owner = dead.Summoner;
                var entry = GetEntry(owner);
                if (entry != null && owner.IsAlive && Pillars(owner).Count == 0) entry.PillarDue = Turn + 5;
            }
        });
    }

    private static Entry GetEntry(BattleEntity owner)
    {
        if (owner == null) return null;
        string id = owner.EntityID == 2064 ? WindEntry : owner.EntityID == 2065 ? ElectroEntry : owner.EntityID == 2066 ? GeoEntry : null;
        var status = owner.GetStatus(id);
        return status != null && Entries.TryGetValue(status, out var entry) ? entry : null;
    }
    public static void MarkRecoveryUsed(BattleEntity owner)
    {
        var entry = GetEntry(owner);
        if (entry == null) return;
        entry.RecoveryUsed = true;
        entry.RecoveryPending = false;
    }
    private static float GuardHP(BattleEntity owner, float incoming)
    {
        var entry = GetEntry(owner);
        if (entry == null || entry.RecoveryUsed || incoming <= 0f) return incoming;
        float available = Math.Max(0f, owner.CurrentHP - owner.GetFinalHP() * .05f);
        if (incoming < available) return incoming;
        entry.RecoveryPending = true;
        return Math.Min(incoming, available);
    }
    private static bool StormDue(BattleEntity owner)
    {
        var entry = GetEntry(owner);
        if (entry == null || owner.HasStatus(HypostasisPublicStates.Recovery)) return false;
        string storm = owner.EntityID == 2064 ? WindStorm : owner.EntityID == 2065 ? ElectroStorm : GeoStorm;
        return !owner.HasStatus(storm) && Turn >= entry.StormDue;
    }
    private static bool PillarsDue(BattleEntity owner)
        => owner?.EntityID == 2066 && GetEntry(owner) is Entry entry && Turn >= entry.PillarDue
            && !owner.HasStatus(HypostasisPublicStates.Recovery)
            && Enemy2021To2030Hooks.EmptyEnemyPositions(BattleManager.Instance).Count > 0;
    private static List<BattleEntity> Pillars(BattleEntity owner)
        => BattleManager.Instance?.Enemies.Where(e => e?.Entity?.IsAlive == true && e.Entity.EntityID == 2609
            && e.Entity.Summoner == owner).Select(e => e.Entity).ToList() ?? new List<BattleEntity>();
    private static List<int> PickAlly(BattleEntity owner, bool taunt = false)
    {
        var battle = BattleManager.Instance;
        if (battle == null || owner == null) return new List<int>();
        var result = BattleTargetResolver.Resolve(battle, owner, owner, "Enemy", 1, 1, null, null, null, null,
            respectTaunt: taunt, weightProvider: pos => battle.GetEntityByPosition(BattleSide.Ally, pos)?.HitWeight ?? 0f);
        return result.IsValid ? new List<int>(result.Positions) : new List<int>();
    }
}
