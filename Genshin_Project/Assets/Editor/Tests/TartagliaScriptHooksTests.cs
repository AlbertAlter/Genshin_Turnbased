using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class TartagliaScriptHooksTests
    {
        private const string S = "ST_Tartaglia_", F = "SE_Tartaglia_";
        private static StatusInstance Add(BattleFlowTestEnv e, string key, BattleEntity target, int duration = 0, int trigger = 0)
            => target.AddStatus(S + key, e.Ally.Entity, duration, 2, trigger, e.DataManager.StatusMainDict[S + key]);
        private static void Effect(BattleFlowTestEnv e, string key, string type, string param, string target, string selector = null, string hook = null)
        {
            e.DataManager.SkillEffectDict[key] = new SkillEffectData { SkillEffectID = 10300000 + e.DataManager.SkillEffectDict.Count,
                SkillEffectID2 = key, EffectType = type, Param1 = param, Param2 = type == "GainEnergy" ? "Flat" : null,
                Element = type == "Damage" ? "Hydro" : "", DamageType = "Skill", TargetType = type == "Damage" && selector != null ? "EnemyField" : target,
                TargetOverride = selector, ScriptHook = hook, Duration = 3, AddInPhase = 2, TriggerPhase = type == "ApplyStatus" && param == S + "Riptide" ? 3 : 0 };
        }
        private static void Action(BattleFlowTestEnv e, string state, string kind, string effects, string hook = null)
        {
            if (!e.DataManager.StatusActionDict.TryGetValue(S + state, out var actions))
                e.DataManager.StatusActionDict[S + state] = actions = new List<StatusActionData>();
            actions.Add(new StatusActionData { StatusID = e.DataManager.StatusMainDict[S + state].StatusID,
                StatusID2 = S + state, ActionType = kind, Param1 = effects, ScriptHook = hook });
        }
        private static void Setup(BattleFlowTestEnv e, int constellation = 0, bool twoHits = false)
        {
            string[] names = { "Charge", "ChargeTarget", "Melee", "Riptide", "Rules", "C6Ready" };
            for (int i = 0; i < names.Length; i++)
                e.DataManager.StatusMainDict[S + names[i]] = new StatusMainData { StatusID = 4103001 + i,
                    StatusID2 = S + names[i], MaxCount = 1, MaxStack = 1, WhenMax = "0" };
            e.DataManager.CharacterStatusIDs[1030] = new HashSet<string>(names.Select(n => S + n));
            e.ConfigureAllyAction(0, "SK_MeleeNormal_Tartaglia", "SE_MeleeNormal_Tartaglia", "Hydro", null, 0);
            e.DataManager.SkillEffectDict["SE_MeleeNormal_Tartaglia"].Param1 = twoHits ? ".1*TotalATK,0;.1*TotalATK,0" : ".1*TotalATK,0";
            e.DataManager.SkillEffectDict["SE_MeleeNormal_Tartaglia"].TargetNumber = 3;
            e.ConfigureAllyAction(2, "SK_Skill_Tartaglia1", "SE_Skill_Tartaglia1Melee", "", null, 0,
                effectType: "ApplyStatus", param1: S + "Melee", duration: 5, addInPhase: 2, triggerPhase: 1, targetType: "Self");
            e.ConfigureAllyAction(2, "SK_Skill_Tartaglia2", "SE_Skill_Tartaglia2Exit", "", null, 0,
                effectType: "RemoveStatus", param1: S + "Melee", targetType: "Self");
            e.DataManager.CharacterAttributesDict[BattleFlowTestEnv.AllyID].ElementalSkillID = "SK_Skill_Tartaglia1";
            e.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID].Last().ActionType = "";
            e.ConfigureAllyAction(3, "SK_Burst_Tartaglia", "SE_Burst_TartagliaMelee", "Hydro", null, 0);
            e.DataManager.SkillEffectDict["SE_Burst_TartagliaMelee"].Param1 = ".1*TotalATK,0";
            e.DataManager.SkillEffectDict["SE_Burst_TartagliaMelee"].TargetNumber = 2;
            Effect(e, F + "Slash", "Damage", ".2*TotalATK,0", "Enemy", "Tartaglia_Area()");
            Effect(e, F + "Flash", "Damage", ".1*TotalATK,0;.1*TotalATK,0;.1*TotalATK,0", "Enemy", "Tartaglia_Center()");
            Effect(e, F + "Blast", "Damage", ".3*TotalATK,0", "Enemy", "Tartaglia_Area()");
            Effect(e, F + "Death", "Damage", ".2*TotalATK,0", "Enemy", "Tartaglia_Neighbors()");
            Effect(e, F + "RiptideEnergy", "GainEnergy", "2", "Self", hook: "Tartaglia_RiptideEnergy()");
            Effect(e, F + "C2Energy", "GainEnergy", "4", "Self", hook: "Check(1030_C2)");
            Effect(e, F + "ApplyMark", "ApplyStatus", S + "Riptide", "Enemy", "Tartaglia_EventTarget()");
            Effect(e, F + "ClearMark", "RemoveStatus", S + "Riptide", "Enemy", "Tartaglia_Center()", "Tartaglia_LivingMark()");
            Action(e, "Rules", "OnTrigger", F + "ApplyMark", "Tartaglia_ApplyMark()");
            foreach (var kind in new[] { "Slash", "Flash", "Blast", "Death" })
                Action(e, "Riptide", "OnTrigger", F + kind + ";" + F + (kind == "Blast" ? "ClearMark" : kind == "Death" ? "C2Energy" : "RiptideEnergy"), "Tartaglia_" + kind + "()");
            Action(e, "Riptide", "OnItsTurn", F + "Slash;" + F + "RiptideEnergy", "Check(1030_C4);Tartaglia_LivingMark();Tartaglia_Melee()");
            var controls = new List<StatusEffectData> {
                new StatusEffectData { StatusEffectID=410300303,StatusEffectID2="STE_Tartaglia_MeleeSkill",EffectType="ChangeControl",Param1="Skill",Param2="SK_Skill_Tartaglia2",Param3="1" },
                new StatusEffectData { StatusEffectID=410300304,StatusEffectID2="STE_Tartaglia_EntryCooldown",EffectType="Cooldown",Param1="SK_Skill_Tartaglia1",Param2="1",TargetType="Self" },
                new StatusEffectData { StatusEffectID=410300305,StatusEffectID2="STE_Tartaglia_RoundCooldown",EffectType="Cooldown",Param1="SK_Skill_Tartaglia1",Param2="1",TargetType="Self" },
                new StatusEffectData { StatusEffectID=410300306,StatusEffectID2="STE_Tartaglia_ExitReset",EffectType="ResetCooldown",Param1="SK_Skill_Tartaglia1",TargetType="Self" }
            };
            foreach (var effect in controls) e.DataManager.StatusEffectDict[effect.StatusEffectID2] = effect;
            Action(e, "Melee", "OnApply", "STE_Tartaglia_EntryCooldown", "Tartaglia_NoC1()");
            Action(e, "Melee", "OnItsTurn", "STE_Tartaglia_RoundCooldown", "Tartaglia_MeleeRound()");
            Action(e, "Melee", "OnEnd", "STE_Tartaglia_ExitReset", "Tartaglia_ResetOnExit()");
            Effect(e, "SE_Skill_Tartaglia2Reset", "ResetCooldown", "SK_Skill_Tartaglia1", "Self", hook: "Tartaglia_ResetOnExit()");
            e.DataManager.SkillEffectDict["SE_Skill_Tartaglia2Reset"].EffectIndex = -1;
            e.CreateMinimalBattle(); e.Ally.Entity.EntityID = 1030; e.Ally.Entity.ConstellationLevel = constellation;
            e.CreateEnemy(2, 10000); e.CreateEnemy(3, 10000); e.Enemy.Entity.TotalHP = e.Enemy.Entity.CurrentHP = 10000;
            e.Manager.StartBattle(); e.Flow.AdvanceToPhase(e.Manager, TurnPhase.AllyAction);
            e.Ally.Entity.TotalATK = 100; e.Ally.Entity.CritRate = e.Ally.Entity.CritDMG = 0;
            e.Ally.Entity.MaxEnergy = 100; e.Ally.Entity.CurrentEnergy = 0;
            BattleRandom.SetSource(new SequenceBattleRandomSource());
        }
        private static Task Cast(BattleFlowTestEnv e, string skill, int type, params int[] targets)
        {
            e.Ally.ForcedTargetPositions.Clear(); e.Ally.ForcedTargetPositions.AddRange(targets);
            return BattleExecution.Run(() => e.Ally.ExecuteSkillByIDAsync(skill, type, true));
        }
        [UnityTest] public IEnumerator Slash_TwoHitsShareEachTargetsQuota_AndC4IsIndependent() => HookTestEnv.AwaitTask(Slash);
        private static async Task Slash()
        {
            using var e = new BattleFlowTestEnv(); Setup(e, 4, true); Add(e, "Melee", e.Ally.Entity, 5, 1);
            foreach (var enemy in e.Manager.Enemies) Add(e, "Riptide", enemy.Entity, 3, 3);
            int hits = 0;
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, d) => { if (d.Source.SourceEffectID == F + "Slash") hits++; }, 0);
            await Cast(e, "SK_MeleeNormal_Tartaglia", 0, 1, 2, 3);
            Assert.That(hits, Is.EqualTo(14), "两个hit分别引发三个中心，范围命中数每次为2+3+2。");
            await Cast(e, "SK_MeleeNormal_Tartaglia", 0, 1, 2, 3);
            Assert.That(hits, Is.EqualTo(14)); Assert.That(e.Ally.Entity.CurrentEnergy, Is.EqualTo(2));
            foreach (var enemy in e.Manager.Enemies)
            {
                var mark = enemy.Entity.GetStatus(S + "Riptide");
                await BattleExecution.Run(() => e.Ally.ExecuteStatusActionsTask(mark, e.DataManager.StatusActionDict[S + "Riptide"], enemy.Entity, "OnItsTurn"));
            }
            Assert.That(hits, Is.EqualTo(21)); Assert.That(e.Ally.Entity.CurrentEnergy, Is.EqualTo(2));
        }
        [UnityTest] public IEnumerator NewCriticalMark_DoesNotRetroactivelySlash_AndOnlyMarksCriticalTarget() => HookTestEnv.AwaitTask(Critical);
        private static async Task Critical()
        {
            using var e = new BattleFlowTestEnv(); Setup(e); Add(e, "Melee", e.Ally.Entity, 5, 1);
            e.DataManager.TalentDict[1030] = new List<TalentData> { new TalentData { CharacterID=1030,TalentIndex=2,UnlockAfter=0 } };
            e.Ally.Entity.CritRate = .5f;
            var random = new SequenceBattleRandomSource(new[] { .1f, .9f }); BattleRandom.SetSource(random);
            int slashes = 0;
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, d) => { if (d.Source.SourceEffectID == F + "Slash") slashes++; }, 0);
            await Cast(e, "SK_MeleeNormal_Tartaglia", 0, 1, 2);
            Assert.That(e.Enemy.Entity.HasStatus(S + "Riptide"), Is.True);
            Assert.That(e.Manager.Enemies[1].Entity.HasStatus(S + "Riptide"), Is.False);
            Assert.That(slashes, Is.Zero);
            Assert.That(random.FloatCallCount, Is.EqualTo(2), "只为两名实际目标各进行一次最终暴击判定。");
            e.Ally.Entity.CritRate = 0; BattleRandom.SetSource(new SequenceBattleRandomSource());
            await Cast(e, "SK_MeleeNormal_Tartaglia", 0, 1, 2);
            Assert.That(slashes, Is.EqualTo(2));
        }
        [UnityTest] public IEnumerator Blast_ClearsOnlyItsCenters_NotNeighborMarks() => HookTestEnv.AwaitTask(Blast);
        private static async Task Blast()
        {
            using var e = new BattleFlowTestEnv(); Setup(e); Add(e, "Melee", e.Ally.Entity, 5, 1);
            foreach (var enemy in e.Manager.Enemies) Add(e, "Riptide", enemy.Entity, 3, 3);
            int hits = 0;
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, d) => { if (d.Source.SourceEffectID == F + "Blast") hits++; }, 0);
            await Cast(e, "SK_Burst_Tartaglia", 3, 1, 2);
            Assert.That(hits, Is.EqualTo(5));
            Assert.That(e.Enemy.Entity.HasStatus(S + "Riptide"), Is.False);
            Assert.That(e.Manager.Enemies[1].Entity.HasStatus(S + "Riptide"), Is.False);
            Assert.That(e.Manager.Enemies[2].Entity.HasStatus(S + "Riptide"), Is.True);
        }
        [UnityTest] public IEnumerator ImmediateExit_UsesC0OneC1Zero_AndC6ResetsAccumulatedCooldown() => HookTestEnv.AwaitTask(Cooldown);
        private static async Task Cooldown()
        {
            foreach (int constellation in new[] { 0, 1, 6 })
            {
                using var e = new BattleFlowTestEnv(); Setup(e, constellation);
                await Cast(e, "SK_Skill_Tartaglia1", 2);
                Assert.That(e.Ally.GetBoundSkillID(2), Is.EqualTo("SK_Skill_Tartaglia2"));
                if (constellation == 6) { e.Ally.ChangeSkillCooldown("SK_Skill_Tartaglia1", 3); Add(e, "C6Ready", e.Ally.Entity); }
                await Cast(e, "SK_Skill_Tartaglia2", 2);
                Assert.That(e.Ally.GetBoundSkillID(2), Is.EqualTo("SK_Skill_Tartaglia1"));
                Assert.That(e.Ally.GetCooldownRemaining(2), Is.EqualTo(constellation == 0 ? 1 : 0));
                Assert.That(e.Ally.Entity.HasStatus(S + "C6Ready"), Is.False);
            }
        }
        [UnityTest] public IEnumerator NaturalExit_CountsFourFurtherRounds_AndDoesNotCountExpiryAgain() => HookTestEnv.AwaitTask(NaturalExit);
        private static async Task NaturalExit()
        {
            foreach (int constellation in new[] { 0, 1 })
            {
                using var e = new BattleFlowTestEnv(); Setup(e, constellation);
                e.Ally.Entity.TotalHP = e.Ally.Entity.CurrentHP = 1000000;
                await Cast(e, "SK_Skill_Tartaglia1", 2);
                for (int turn = 0; turn < 5; turn++)
                {
                    e.Flow.AdvanceToPhase(e.Manager, TurnPhase.AllyPostTurn);
                    e.Flow.AdvanceToPhase(e.Manager, TurnPhase.AllyAction);
                }
                Assert.That(e.Ally.Entity.HasStatus(S + "Melee"), Is.False);
                Assert.That(e.Ally.GetBoundSkillID(2), Is.EqualTo("SK_Skill_Tartaglia1"));
                Assert.That(e.Ally.GetCooldownRemaining(2), Is.EqualTo(constellation == 0 ? 5 : 4));
            }
        }
    }
}
