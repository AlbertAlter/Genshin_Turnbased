using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>第四批敌人的资格、事件目标与风拳元素记录；倍率、状态和技能模式均由配表执行。</summary>
public static class Enemy2031To2040Hooks
{
    public const string WindShield = "ST_Anemoboxer_Barrier";
    public const string GeoAura = "ST_Geochanter_CrystalAura";
    private static readonly ConditionalWeakTable<StatusInstance, WindMemory> WindStates = new ConditionalWeakTable<StatusInstance, WindMemory>();

    internal sealed class WindMemory
    {
        internal IDisposable Listener;
        internal string Element = "Anemo";
        private long firstHit;
        internal void Observe(long hit, string element)
        {
            int priority = Priority(element);
            if (priority < 0) return;
            if (Element == "Anemo") { Element = element; firstHit = hit; }
            else if (hit > 0 && hit == firstHit && priority < Priority(Element)) Element = element;
        }
        private static int Priority(string element)
            => element == "Pyro" ? 0 : element == "Hydro" ? 1 : element == "Electro" ? 2 : element == "Cryo" ? 3 : -1;
    }

    internal static void Register()
    {
        BattleSpecialRules.RegisterNumberedCondition("203105", (_, c) => CanRestore(c?.Caster, "ST_HydroMage_Bubbles"));
        BattleSpecialRules.RegisterNumberedCondition("203205", (_, c) => CanRestore(c?.Caster, "ST_PyroMage_Eruption"));
        foreach (int id in new[] { 2033, 2034 })
            BattleSpecialRules.RegisterNumberedCondition((id * 100 + 5).ToString(), (_, c) => c?.Caster?.EnemyCtrl?.IsLastGuaranteedAction == true);
        foreach (string id in new[] { "ST_CryoWhopperflower_Stunned", "ST_PyroWhopperflower_Stunned" })
            BattleSpecialRules.RegisterStatusLifecycle(id, (status, host) => {
                var battle = BattleManager.Instance;
                if (battle != null && (int)battle.CurrentPhase == 6) status.EarliestTimerTurn = battle.TurnCount + 1;
            }, null);
        BattleSpecialRules.RegisterCondition("EnemyPlayerHit", (_, c) => PlayerAttacker(c)?.IsOnField == true);
        BattleSpecialRules.RegisterTargets("EnemyPlayerAttacker()", (_, __) => {
            var actor = PlayerAttacker(ScriptHookContext.Current);
            return actor?.IsOnField == true ? new List<int> { actor.SlotPosition } : new List<int>();
        });
        BattleSpecialRules.RegisterNumberedCondition("203703", (_, c) => c?.Caster != null && !c.Caster.HasStatus(WindShield));
        BattleSpecialRules.RegisterNumberedCondition("203704", (_, c) => WakeCandidates(BattleManager.Instance, c?.Caster).Count > 0);
        BattleSpecialRules.RegisterTargets("WindWakeTarget()", (battle, caster) => {
            var candidates = WakeCandidates(battle, caster);
            return candidates.Count == 0 ? new List<int>() : new List<int> { candidates[BattleRandom.NextInt(0, candidates.Count)].SlotPosition };
        });
        BattleSpecialRules.RegisterCondition("FatuiArmorReady", (_, c) => ArmorReady(c?.Caster));
        BattleSpecialRules.RegisterCondition("FatuiNormalReady", (_, c) => !ArmorReady(c?.Caster));
        BattleSpecialRules.RegisterCondition("FatuiArmorThreshold", (_, c) => c?.Caster?.IsAlive == true
            && c.Caster.CurrentHP <= .5f * c.Caster.GetFinalHP());
        BattleSpecialRules.RegisterNumberedCondition("204003", (_, c) => CanCreateGeoAura(BattleManager.Instance, c?.Caster));
        BattleSpecialRules.RegisterStatusLifecycle(WindShield, StartWind, StopWind);
        foreach (string element in new[] { "Pyro", "Hydro", "Electro", "Cryo", "Anemo" })
        {
            string chosen = element;
            BattleSpecialRules.RegisterCondition("WindCounter" + chosen, (_, c) => RecordedElement(c?.Status) == chosen);
        }
    }

    private static bool CanRestore(BattleEntity enemy, string recovering)
        => enemy?.IsAlive == true && enemy.GetEnemyElementalShield() == null && !enemy.HasStatus(recovering);

    private static BattleEntity PlayerAttacker(ScriptHookContext context)
    {
        var hit = context?.DamageEvent;
        var source = hit?.Source;
        return hit?.HitLanded == true && hit.Target == context.Caster && source?.CountsAsHit == true
            && source.SourceEntity?.Type == BattleEntity.EntityType.Character && source.SourceSide == BattleSide.Ally
            ? source.SourceEntity : null;
    }

    public static bool IsSkirmisher(BattleEntity enemy)
        => enemy != null && enemy.EntityID >= 2037 && enemy.EntityID <= 2042 && EnemyTraits.HasFamily(enemy, "愚人众");

    public static List<BattleEntity> WakeCandidates(BattleManager battle, BattleEntity caster)
    {
        var result = new List<BattleEntity>();
        if (battle == null || caster?.IsOnField != true) return result;
        foreach (var controller in battle.Enemies)
        {
            var enemy = controller?.Entity;
            if (enemy != caster && enemy?.IsOnField == true && enemy.Side == caster.Side
                && IsSkirmisher(enemy) && enemy.HasStatus(PublicControlStates.EnemyStun)) result.Add(enemy);
        }
        return result;
    }

    internal static int ArmorSkill(int enemyID) => enemyID == 2038 ? 203804 : enemyID == 2039 ? 203903
        : enemyID == 2040 ? 204004 : enemyID == 2041 ? 204104 : enemyID == 2042 ? 204204 : 0;
    internal static string ArmorPrefix(int enemyID) => enemyID == 2038 ? "ST_Cryogunner_" : enemyID == 2039 ? "ST_Electrohammer_"
        : enemyID == 2040 ? "ST_Geochanter_" : enemyID == 2041 ? "ST_Hydrogunner_" : enemyID == 2042 ? "ST_Pyroslinger_" : null;
    public static bool ArmorReady(BattleEntity enemy)
    {
        if (enemy?.IsOnField != true || enemy.EnemyCtrl == null) return false;
        int skill = ArmorSkill(enemy.EntityID);
        if (skill == 0 || !enemy.EnemyCtrl.MeetsSkillLimits(skill) || enemy.GetEnemyElementalShield() != null) return false;
        return ArmorUnlocked(enemy.CurrentHP, enemy.GetFinalHP(), BattleManager.Instance?.TurnCount ?? 0,
            enemy.HasStatus(ArmorPrefix(enemy.EntityID) + "Unlocked"));
    }
    internal static bool ArmorUnlocked(float hp, float maximum, int turn, bool previouslyUnlocked)
        => previouslyUnlocked || maximum > 0 && hp <= maximum * .5f || turn >= 3;

    public static bool CanCreateGeoAura(BattleManager battle, BattleEntity caster)
    {
        if (battle == null || caster?.HasStatus("ST_Geochanter_Armor") != true) return false;
        foreach (var enemy in battle.Enemies)
            if (enemy?.Entity?.IsAlive == true && enemy.Entity.HasStatus(GeoAura)) return false;
        return true;
    }

    public static string RecordedElement(StatusInstance status)
        => status != null && WindStates.TryGetValue(status, out var state) ? state.Element : "Anemo";

    private static void StartWind(StatusInstance status, object host)
    {
        if (!(host is BattleEntity owner)) return;
        var memory = new WindMemory(); WindStates.Add(status, memory);
        memory.Listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (header, hit) => {
            if (hit.Subject == owner && hit.HitLanded && hit.Source.CountsAsHit && hit.Source.HasSourceEntity
                && hit.Source.SourceSide != owner.Side) memory.Observe(header.HitId, hit.Element);
        }, status.ApplyOrder, () => status.IsActive && owner.HasStatusInstance(status));
    }
    private static void StopWind(StatusInstance status, object host)
    {
        if (!WindStates.TryGetValue(status, out var memory)) return;
        memory.Listener?.Dispose(); WindStates.Remove(status);
    }
}
