using System.Collections.Generic;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class EnemyFourthTenIntegrationTests
    {
        [Test]
        public void SummonedMage_BindsInitialShieldImmunityOnlyAfterPlacement()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            var shield = new StatusMainData { StatusID = 49991, StatusID2 = "ST_TestEntryShield", StatusType = "Shield", MaxStack = 1 };
            var immune = new StatusMainData { StatusID = 49992, StatusID2 = "ST_TestEntryImmune", StatusType = "Immune", MaxStack = 1 };
            env.DataManager.StatusMainDict[shield.StatusID2] = shield;
            env.DataManager.StatusMainDict[immune.StatusID2] = immune;
            env.DataManager.StatusEffectDict["STE_TestEntryShield"] = new StatusEffectData {
                StatusEffectID = 4999101, StatusEffectID2 = "STE_TestEntryShield", EffectType = "Shield", Element = "Hydro", Param1 = "12U", TargetType = "Self" };
            env.DataManager.StatusEffectDict["STE_TestEntryBind"] = new StatusEffectData {
                StatusEffectID = 4999102, StatusEffectID2 = "STE_TestEntryBind", EffectType = "BindStatus", Param1 = immune.StatusID2, TargetType = "Self" };
            env.DataManager.StatusEffectDict["STE_TestEntryImmune"] = new StatusEffectData {
                StatusEffectID = 4999201, StatusEffectID2 = "STE_TestEntryImmune", EffectType = "Immune", Element = "Physical;Pyro;Hydro;Electro;Cryo;Anemo;Dendro;Geo;None" };
            env.DataManager.EnemyMainDict[BattleFlowTestEnv.EnemyID].InitiateStatuses.Add(shield.StatusID2);
            var summoned = env.Manager.SummonEnemy(BattleFlowTestEnv.EnemyID, 1, 2).Entity;
            Assert.That(summoned.Position.Occupant, Is.SameAs(summoned));
            Assert.That(summoned.GetEnemyElementalShieldHP(), Is.EqualTo(12));
            Assert.That(summoned.HasStatus(immune.StatusID2), Is.True);
            Assert.That(summoned.GetStatus(shield.StatusID2).BoundStatuses, Has.Count.EqualTo(1));
            Assert.That(summoned.GetStatusInstances(shield.StatusID2), Has.Count.EqualTo(1));
        }

        [Test]
        public void RuntimeTables_FourthBatchReferencesAndReleaseModes()
        {
            using var env = new BattleFlowTestEnv(); env.DataManager.ReloadAllData();
            for (int id = 2031; id <= 2040; id++)
            {
                Assert.That(env.DataManager.EnemyMainDict[id].ActsGiven, Is.EqualTo(1));
                Assert.That(env.DataManager.EnemyAIRuleDict[id], Is.Not.Empty);
            }
            foreach (string name in new[] { "CryoWhopperflower", "PyroWhopperflower" })
            {
                Assert.That(env.DataManager.EnemySkillEffectDict["SE_" + name + "_Release_Damage"].ExecuteCount, Is.EqualTo(10));
                Assert.That(env.DataManager.EnemySkillEffectDict["SE_" + name + "_Release_Damage"].TargetConsecutive, Is.EqualTo(3));
                Assert.That(env.DataManager.StatusActionDict["ST_" + name + "_ChargeShield"][0].ScriptHook, Is.EqualTo("EnemyShieldBroken()"));
            }
            var shield = env.DataManager.StatusEffectDict["STE_Anemoboxer_Barrier_Shield"];
            Assert.That(shield.Element, Is.EqualTo("None")); Assert.That(shield.Param1, Is.EqualTo("0.2*TotalHP"));
            Assert.That(env.DataManager.StatusActionDict[Enemy2031To2040Hooks.WindShield][0].ActionType, Is.EqualTo("OnEnd"));
            foreach (int skill in new[] { 203804, 203903, 204004 })
                Assert.That(env.DataManager.EnemySkillMainDict[skill].SkillTags, Does.Contain("Armor"));
        }

        [Test]
        public void WindBarrier_RecordsZeroHPHits_FirstHitLocksElement_SameHitUsesPriority()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var owner = env.Enemy.Entity;
            var main = new StatusMainData { StatusID = 4203701, StatusID2 = Enemy2031To2040Hooks.WindShield, StatusType = "Shield", MaxStack = 1 };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            env.DataManager.StatusEffectDict["STE_TestWindShield"] = new StatusEffectData {
                StatusEffectID = 420370101, StatusEffectID2 = "STE_TestWindShield", EffectType = "Shield", Element = "None", Param1 = "100" };
            var status = owner.AddStatus(main.StatusID2, owner, 1, 5, 0, main);
            float before = owner.CurrentHP;
            var source = DamageSourceInfo.Create(env.Ally.Entity, ReactionSourceKind.CharacterSkill, "SK_Test", "SE_Test");
            using (var batch = new HitBatch())
                foreach (string element in new[] { "Cryo", "Hydro", "Pyro", "Electro" })
                    batch.Resolve(new HitRequest { Target = owner, DamageBeforeShield = 1, Element = element, Source = source });
            Assert.That(owner.CurrentHP, Is.EqualTo(before));
            Assert.That(Enemy2031To2040Hooks.RecordedElement(status), Is.EqualTo("Pyro"));
            using (var batch = new HitBatch())
                batch.Resolve(new HitRequest { Target = owner, DamageBeforeShield = 1, Element = "Hydro", Source = source });
            Assert.That(Enemy2031To2040Hooks.RecordedElement(status), Is.EqualTo("Pyro"));
            owner.RemoveStatusInstance(status);
            var fresh = owner.AddStatus(main.StatusID2, owner, 1, 5, 0, main);
            Assert.That(Enemy2031To2040Hooks.RecordedElement(fresh), Is.EqualTo("Anemo"));
        }

        [Test]
        public void WakeFilters_RequireSkirmisherMetadata_Stun_Presence_AndOtherEntity()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            env.ConfigureStatus(PublicControlStates.EnemyStun);
            env.ConfigureStatus(PublicControlStates.OffField);
            var owner = env.Enemy.Entity; owner.EntityID = 2037;
            var valid = env.CreateEnemy(2).Entity; valid.EntityID = 2038;
            var absent = env.CreateEnemy(3).Entity; absent.EntityID = 2039;
            var otherFatui = env.CreateEnemy(4).Entity; otherFatui.EntityID = 2100;
            foreach (var entity in new[] { owner, valid, absent, otherFatui })
            {
                env.DataManager.EnemyMainDict[entity.EntityID] = new EnemyMainData { EnemyID = entity.EntityID, EnemyFamily = "愚人众" };
                entity.AddStatus(PublicControlStates.EnemyStun, owner, 1, 6, 0, env.DataManager.StatusMainDict[PublicControlStates.EnemyStun]);
            }
            absent.AddStatus(PublicControlStates.OffField, absent, 0, 5, 0, env.DataManager.StatusMainDict[PublicControlStates.OffField]);
            Assert.That(Enemy2031To2040Hooks.WakeCandidates(env.Manager, owner), Is.EqualTo(new[] { valid }));
            valid.RemoveStatus(PublicControlStates.EnemyStun);
            Assert.That(Enemy2031To2040Hooks.WakeCandidates(env.Manager, owner), Is.Empty);
        }

        [Test]
        public void ArmorConditions_AreMutuallyExclusive_AndCooldownBlocksMandatoryCast()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var owner = env.Enemy.Entity; owner.EntityID = 2038;
            env.DataManager.EnemySkillMainDict[203804] = new EnemySkillMainData { EnemySkillID = 203804, EnemySkillID2 = "SK_TestArmor", SkillTags = new List<string> { "Armor" } };
            env.DataManager.EnemyAIRuleDict[env.Enemy.EnemyID] = new List<EnemyAIRuleData> { new EnemyAIRuleData { SkillID = 203804 } };
            owner.CurrentHP = owner.GetFinalHP() * .5f;
            Assert.That(Enemy2031To2040Hooks.ArmorReady(owner), Is.True);
            Assert.That(ScriptHookEvaluator.Evaluate("FatuiNormalReady()", owner), Is.False);
            Assert.That(env.Enemy.ChangeSkillCooldown("Tag(Armor)", 3), Is.True);
            Assert.That(Enemy2031To2040Hooks.ArmorReady(owner), Is.False);
            Assert.That(ScriptHookEvaluator.Evaluate("FatuiNormalReady()", owner), Is.True);
            env.Enemy.ChangeSkillCooldown("Tag(Armor)", -3);
            env.ConfigureStatus("ST_Cryogunner_Unlocked");
            owner.AddStatus("ST_Cryogunner_Unlocked", owner, 0, 5, 0, env.DataManager.StatusMainDict["ST_Cryogunner_Unlocked"]);
            owner.CurrentHP = owner.GetFinalHP();
            Assert.That(Enemy2031To2040Hooks.ArmorReady(owner), Is.True, "已解锁后治疗不撤销资格");
        }
    }
}
