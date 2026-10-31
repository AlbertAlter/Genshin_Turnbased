using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class RosariaScriptHooksTests
    {
        private static void Setup(BattleFlowTestEnv env)
        {
            env.ConfigureAllyAction(2, "SK_Skill_Rosaria", "SE_Skill_RosariaDamage", "Cryo", ".1*TotalATK,1", 0);
            env.ConfigureAllyAction(3, "SK_Burst_Rosaria", "SE_Burst_RosariaDamage", "Cryo", ".1*TotalATK,1", 0);
            foreach (var effect in env.DataManager.SkillEffectDict.Values.Where(e => e.SkillEffectID2.Contains("Rosaria")))
                effect.TargetNumber = 3;
            env.DataManager.SkillLevelDict[BattleFlowTestEnv.AllyID + "_2"][1].Hits2 = ".1*TotalATK,1";
            env.DataManager.SkillEffectDict["SE_Burst_RosariaField"] = new SkillEffectData
            {
                SkillEffectID = 10380403, SkillEffectID2 = "SE_Burst_RosariaField", EffectIndex = 1,
                EffectType = "ApplyStatus", Param1 = RosariaScriptHooks.Field, TargetType = "EnemyField",
                TargetOverride = "Rosaria_FieldAnchor()", Duration = 2, AddInPhase = 2, TriggerPhase = 3
            };
            env.ConfigureStatus(RosariaScriptHooks.Field, 0, "0", "OnItsTurn", "STE_Rosaria_RitesDamage");
            env.ConfigureStatus(RosariaScriptHooks.Share);
            env.ConfigureStatus("ST_Rosaria_C4", 1, "0", "OnTrigger", "STE_Rosaria_SkillEnergy");
            var action = env.DataManager.StatusActionDict["ST_Rosaria_C4"][0];
            action.ScriptHook = "Rosaria_SkillEnergy()"; action.OutgoingHit = "Skill";
            env.DataManager.StatusEffectDict["STE_Rosaria_RitesDamage"] = new StatusEffectData
            {
                StatusEffectID2 = "STE_Rosaria_RitesDamage", EffectType = "Damage", Element = "Cryo", DamageType = "Burst",
                Param1 = ".1*TotalATK,1", TargetType = "Enemy", TargetOverride = "Rosaria_RitesPositions()"
            };
            env.DataManager.StatusEffectDict["STE_Rosaria_ShareCrit"] = new StatusEffectData
            { StatusEffectID2 = "STE_Rosaria_ShareCrit", EffectType = "Buff", Param1 = ".15", Param2 = ".15", DamageHook = "Rosaria_FixedCritShare()" };
            env.DataManager.StatusEffectDict["STE_Rosaria_SkillEnergy"] = new StatusEffectData
            { StatusEffectID2 = "STE_Rosaria_SkillEnergy", EffectType = "GainEnergy", Param1 = "6", Param2 = "Flat", TargetType = "Self" };
            env.CreateMinimalBattle(); env.CreateAlly(2); env.CreateAlly(3);
            env.CreateEnemy(2, 100000); env.CreateEnemy(3, 100000); env.CreateEnemy(4, 100000);
            env.Ally.Entity.EntityID = 1038; env.Ally.Entity.ConstellationLevel = 4;
            env.Enemy.Entity.CurrentHP = env.Enemy.Entity.TotalHP = 100000;
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            BattleRandom.SetSource(new SequenceBattleRandomSource());
        }
        private static Task Cast(BattleFlowTestEnv env, string skill, int type, params int[] positions)
        {
            env.Ally.ForcedTargetPositions.Clear(); env.Ally.ForcedTargetPositions.AddRange(positions);
            return BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync(skill, type, true));
        }
        [Test]
        public void T1_UsesPrePaymentHalfAP_IncludingEquality_AndClearsAfterScope()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            var owner = env.Ally.Entity; var ap = env.Manager.APManager;
            ap.ConsumeAP(50);
            using (BattleSpecialRules.BeginSkill(owner))
            {
                ap.ConsumeAP(30);
                using (BattleSpecialRules.BeginSkillExecution(owner, new SkillMainData { SkillID2 = "SK_Skill_Rosaria" }))
                    Assert.That(ScriptHookEvaluator.Evaluate("Rosaria_PreCastHalfAP()", new ScriptHookContext { Caster = owner }), Is.True);
            }
            Assert.That(ScriptHookEvaluator.Evaluate("Rosaria_PreCastHalfAP()", new ScriptHookContext { Caster = owner }), Is.False);
            ap.ResetAP(); ap.ConsumeAP(51);
            using (BattleSpecialRules.BeginSkill(owner))
            using (BattleSpecialRules.BeginSkillExecution(owner, new SkillMainData { SkillID2 = "SK_Skill_Rosaria" }))
                Assert.That(ScriptHookEvaluator.Evaluate("Rosaria_PreCastHalfAP()", new ScriptHookContext { Caster = owner }), Is.False);
        }
        [Test]
        public void Share_FreezesOneValueForAllRecipients_AndRecapturesOnRecast_WithCap()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            var owner = env.Ally.Entity; owner.CritRate = .8f; owner.WeaponCritRate = .1f;
            var main = env.DataManager.StatusMainDict[RosariaScriptHooks.Share];
            var effect = env.DataManager.StatusEffectDict["STE_Rosaria_ShareCrit"];
            StatusInstance first, second;
            using (BattleSpecialRules.BeginSkillExecution(owner, new SkillMainData { SkillID2 = "SK_Burst_Rosaria" }))
            {
                first = env.Manager.Allies[1].Entity.AddStatus(RosariaScriptHooks.Share, owner, 2, 2, 0, main);
                owner.CritRate = .2f; owner.WeaponCritRate = 0;
                second = env.Manager.Allies[2].Entity.AddStatus(RosariaScriptHooks.Share, owner, 2, 2, 0, main);
            }
            BattleSpecialRules.TryResolveBuffScalar(effect, first, out float a);
            BattleSpecialRules.TryResolveBuffScalar(effect, second, out float b);
            Assert.That(a, Is.EqualTo(.135f).Within(.00001f)); Assert.That(b, Is.EqualTo(a));
            owner.CritRate = 2;
            using (BattleSpecialRules.BeginSkillExecution(owner, new SkillMainData { SkillID2 = "SK_Burst_Rosaria" }))
            {
                var refreshed = env.Manager.Allies[1].Entity.AddStatus(RosariaScriptHooks.Share, owner, 2, 2, 0, main);
                BattleSpecialRules.TryResolveBuffScalar(effect, refreshed, out float capped);
                Assert.That(capped, Is.EqualTo(.15f)); Assert.That(first.IsActive, Is.False);
            }
            BattleSpecialRules.TryResolveBuffScalar(effect, second, out float preserved);
            Assert.That(preserved, Is.EqualTo(.135f).Within(.00001f));
        }
        [UnityTest]
        public IEnumerator Burst_Coexists_RemembersOriginalPositions_AndUsesOneRealtimeHit()
            => HookTestEnv.AwaitTask(FieldAsync);
        private static async Task FieldAsync()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            await Cast(env, "SK_Burst_Rosaria", 3, 1, 2, 3);
            await Cast(env, "SK_Burst_Rosaria", 3, 2, 3, 4);
            var fields = env.Manager.Field.EnemySlots.SelectMany(f => f.StatusList).Where(s => s.StatusID2 == RosariaScriptHooks.Field).OrderBy(s => s.ApplyOrder).ToList();
            Assert.That(fields.Count, Is.EqualTo(2));
            Assert.That(fields.All(s => s.RemainingPhaseCount == 3 && s.FormulaSnapshot == null), Is.True);
            var facts = new List<DamageEventData>(); var hits = new HashSet<long>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (h, f) =>
            { if (f.Source.SourceEffectID == "STE_Rosaria_RitesDamage") { facts.Add(f); hits.Add(h.HitId); } }, 0);
            var old = fields[0];
            await BattleExecution.Run(() => env.Ally.ExecuteStatusActionsTask(old, env.DataManager.StatusActionDict[RosariaScriptHooks.Field], old.Host, "OnItsTurn"));
            Assert.That(facts.Select(f => f.Subject.SlotPosition), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(hits.Count, Is.EqualTo(1)); float before = facts[0].DamageBeforeShield;
            env.Ally.Entity.TotalATK *= 2; facts.Clear(); hits.Clear();
            await BattleExecution.Run(() => env.Ally.ExecuteStatusActionsTask(old, env.DataManager.StatusActionDict[RosariaScriptHooks.Field], old.Host, "OnItsTurn"));
            Assert.That(facts[0].DamageBeforeShield, Is.EqualTo(before * 2).Within(.001));
            ((FieldPosition)old.Host).RemoveStatusInstance(old, "Expired");
            Assert.That(fields[1].IsActive, Is.True);
        }
        [UnityTest]
        public IEnumerator C4_SixCriticalTargetsOnlyPayOncePerCast_AndNextCastCanPayAgain()
            => HookTestEnv.AwaitTask(EnergyAsync);
        private static async Task EnergyAsync()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            var owner = env.Ally.Entity; owner.CritRate = 1; owner.CurrentEnergy = 0;
            owner.AddStatus("ST_Rosaria_C4", owner, 0, 2, 0, env.DataManager.StatusMainDict["ST_Rosaria_C4"]);
            int criticalTargets = 0;
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, f) =>
            { if (f.Source.SourceEffectID == "SE_Skill_RosariaDamage" && f.Critical?.IsCritical == true) criticalTargets++; }, 0);
            await Cast(env, "SK_Skill_Rosaria", 2, 1, 2, 3);
            Assert.That(criticalTargets, Is.EqualTo(6)); Assert.That(owner.CurrentEnergy, Is.EqualTo(6));
            await Cast(env, "SK_Skill_Rosaria", 2, 1, 2, 3);
            Assert.That(owner.CurrentEnergy, Is.EqualTo(12));
        }
    }
}
