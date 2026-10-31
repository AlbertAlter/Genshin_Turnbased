using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class EnemySecondTenIntegrationTests
    {
        [Test]
        public void RuntimeTables_LoadSecondBatchAndUseRainDamageHook()
        {
            using var env = new BattleFlowTestEnv(); env.DataManager.ReloadAllData();
            for (int id = 2011; id <= 2020; id++) {
                Assert.That(env.DataManager.EnemyMainDict[id].ActsGiven, Is.EqualTo(1));
                Assert.That(env.DataManager.EnemyAIRuleDict[id], Is.Not.Empty);
            }
            var heal = env.DataManager.StatusEffectDict["STE_HydroShaman_RainHeal"];
            Assert.That(heal.Param1, Is.EqualTo("0.15*Target.TotalHP"));
            Assert.That(string.IsNullOrEmpty(heal.Param2), Is.True);
            Assert.That(heal.DamageHook, Is.EqualTo(Enemy2011To2020Hooks.RainHealHook));
            Assert.That(env.DataManager.EnemyMainDict[2001].EnemyFamily, Is.EqualTo("丘丘部族"));
            Assert.That(env.DataManager.EnemyMainDict[2017].InitiateStatuses,
                Does.Contain("ST_DendroSlime_PanicWatch").And.Contain("ST_DendroSlime_Immune"));
        }

        [Test]
        public void HealBatch_CapUsesProjectedHPAndLeavesBondUntouchedAboveCap()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var target = env.CreateEnemy(2, 1000).Entity; target.CurrentHP = 550;
            using (var batch = new HealBatch()) {
                batch.Add(target, 100, hpCeiling: 600);
                batch.Add(target, 100, hpCeiling: 600);
                batch.Flush();
                Assert.That(batch.Results.Count, Is.EqualTo(1));
                Assert.That(batch.Results[0].ActualAmount, Is.EqualTo(50));
            }
            BondOfLifeStatusHandler.AddValue(target, 100);
            float bond = BondOfLifeStatusHandler.GetValue(target);
            using (var batch = new HealBatch()) batch.Add(target, 200, hpCeiling: 600);
            Assert.That(target.CurrentHP, Is.EqualTo(600));
            Assert.That(BondOfLifeStatusHandler.GetValue(target), Is.EqualTo(bond));
        }

        [UnityEngine.TestTools.UnityTest]
        public IEnumerator Thorns_AnyPlayerActionHitsBothFieldsOnceWithoutEnemyAction()
            => HookTestEnv.AwaitTask(ThornsAsync);
        private async Task ThornsAsync()
        {
            using var env = new BattleFlowTestEnv(); env.ConfigureEnemyDamageSkill(); env.CreateMinimalBattle();
            var other = env.CreateAlly(2, true);
            var name = Enemy2011To2020Hooks.Thorns;
            var main = new StatusMainData { StatusID = 4201201, StatusID2 = name, MaxStack = 0 };
            var action = new StatusActionData { StatusID2 = name, ActionType = "OnTrigger", OnAction = "All",
                ScriptHook = "Hook(420120102)", Param1 = "STE_DendroShaman_Thorns" };
            env.DataManager.StatusMainDict[name] = main;
            env.DataManager.StatusActionDict[name] = new List<StatusActionData> { action };
            env.DataManager.StatusEffectDict[action.Param1] = new StatusEffectData {
                StatusEffectID = 420120101, StatusEffectID2 = action.Param1, EffectType = "Damage", Element = "Dendro",
                Param1 = "0.3*ATK,1", TargetType = "Enemy", TargetSelect = "1", TargetOverride = "Hook(420120101)" };
            var fields = env.Manager.Field.AllySlots;
            fields[0].AddStatus(name, env.Enemy.Entity, 2, 3, 0, main);
            fields[1].AddStatus(name, env.Enemy.Entity, 2, 3, 0, main);
            var hits = new List<DamageEventData>(); int enemyActions = 0;
            using var damage = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,
                (_, fact) => { if (fact.Source.SourceEffectID == action.Param1) hits.Add(fact); }, 0);
            using var actions = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted,
                (_, fact) => { if (fact.Subject == env.Enemy.Entity) enemyActions++; }, 0);
            await BattleExecution.Run(async () => {
                StatusOnHitHookSystem.NotifyPlayerAction(env.Ally.Entity, new SkillMainData { SkillID2 = "SK_Test", ActionType = "Normal" });
                await BattleExecution.Flush();
            });
            Assert.That(hits.Count, Is.EqualTo(2));
            Assert.That(hits.ConvertAll(x => x.Subject), Is.EquivalentTo(new[] { env.Ally.Entity, other.Entity }));
            Assert.That(enemyActions, Is.Zero);
            foreach (var hit in hits) {
                Assert.That(hit.Source.SourceKind, Is.EqualTo(ReactionSourceKind.StatusEffect));
                Assert.That(hit.Source.SourceStatusID, Is.EqualTo(name));
                Assert.That(hit.Source.SourceSkillID, Is.Empty);
            }
        }
    }
}
