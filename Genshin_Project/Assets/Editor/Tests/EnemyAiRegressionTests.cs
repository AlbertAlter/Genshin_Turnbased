using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public class EnemyAiRegressionTests
    {
        [UnityTest]
        public IEnumerator Reinitialize_ClearsCooldownsAndRulesFromPreviousBattle() => HookTestEnv.AwaitTask(async () =>
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureEnemyDamageSkill();
                env.CreateMinimalBattle();
                GetCooldowns(env.Enemy)[990201] = 3;
                Assert.That(env.Enemy.CurrentPatternID, Is.EqualTo(1));

                env.DataManager.EnemyAIDict[BattleFlowTestEnv.EnemyID] = new List<EnemyAIData>();
                env.DataManager.EnemyAIRuleDict[BattleFlowTestEnv.EnemyID] = new List<EnemyAIRuleData>();
                env.Enemy.InitEnemy(BattleFlowTestEnv.EnemyID, 1);

                Assert.That(GetCooldowns(env.Enemy), Is.Empty);
                Assert.That(env.Enemy.CurrentPatternID, Is.Zero);
                var random = new SequenceBattleRandomSource();
                BattleRandom.SetSource(random);
                await BattleExecution.Run(() => env.Enemy.TakeEnemyActionAsync());
                Assert.That(random.IntCallCount, Is.Zero);
                Assert.That(random.FloatCallCount, Is.Zero);
            }
        });

        [UnityTest]
        public IEnumerator PickSkill_IgnoresMissingSkillsAndNonPositiveWeights() => HookTestEnv.AwaitTask(async () =>
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureEnemyDamageSkill();
                env.DataManager.EnemyAIRuleDict[BattleFlowTestEnv.EnemyID] = new List<EnemyAIRuleData>
                {
                    new EnemyAIRuleData
                    {
                        EnemyID = BattleFlowTestEnv.EnemyID,
                        PatternID = 1,
                        PatternType = "Weighted",
                        SkillID = 123456789,
                        Weight = 100,
                        ScriptHook = "Chance(1)"
                    },
                    new EnemyAIRuleData
                    {
                        EnemyID = BattleFlowTestEnv.EnemyID,
                        PatternID = 1,
                        PatternType = "Weighted",
                        SkillID = 990201,
                        Weight = 0,
                        ScriptHook = "Chance(1)"
                    }
                };
                env.CreateMinimalBattle();

                var random = new SequenceBattleRandomSource();
                BattleRandom.SetSource(random);
                await BattleExecution.Run(() => env.Enemy.TakeEnemyActionAsync());

                Assert.That(random.IntCallCount, Is.Zero);
                Assert.That(random.FloatCallCount, Is.Zero);
                Assert.That(env.Enemy.MeetsSkillLimits(990201), Is.True);
            }
        });

        [UnityTest]
        public IEnumerator PickSkill_FiltersLimitsBeforeHooksAndRollsOncePerSelection() => HookTestEnv.AwaitTask(async () =>
        {
            using (var env = new BattleFlowTestEnv())
            {
                const int coolingSkill = 990201, firstSkill = 990202, secondSkill = 990203;
                env.ConfigureEnemyDamageSkill();
                var rules = env.DataManager.EnemyAIRuleDict[BattleFlowTestEnv.EnemyID];
                rules[0].ScriptHook = "Chance(1)";
                foreach (int skillID in new[] { firstSkill, secondSkill })
                {
                    env.DataManager.EnemySkillMainDict[skillID] = new EnemySkillMainData
                    {
                        EnemySkillID = skillID, EnemySkillID2 = $"SK_TestAI_{skillID}",
                        EnemySkillName = $"Test AI {skillID}", UsePerTurn = 1
                    };
                    rules.Add(new EnemyAIRuleData
                    {
                        PatternID = 1, SkillID = skillID, Weight = skillID == firstSkill ? 2 : 5,
                        ScriptHook = "Chance(0.5)"
                    });
                }
                env.CreateMinimalBattle();
                GetCooldowns(env.Enemy)[coolingSkill] = 2;
                var random = new SequenceBattleRandomSource(new[] { 0.1f, 0.2f, 0.3f }, new[] { 2, 0 });
                BattleRandom.SetSource(random);

                await BattleExecution.Run(() => env.Enemy.TakeEnemyActionAsync());
                Assert.That(env.Enemy.MeetsSkillLimits(firstSkill), Is.True);
                Assert.That(env.Enemy.MeetsSkillLimits(secondSkill), Is.False);
                Assert.That(random.IntCallCount, Is.EqualTo(1));
                Assert.That(random.FloatCallCount, Is.EqualTo(2));

                await BattleExecution.Run(() => env.Enemy.TakeEnemyActionAsync());
                Assert.That(env.Enemy.MeetsSkillLimits(firstSkill), Is.False);
                await BattleExecution.Run(() => env.Enemy.TakeEnemyActionAsync());
                Assert.That(random.IntCallCount, Is.EqualTo(2));
                Assert.That(random.FloatCallCount, Is.EqualTo(3));
            }
        });

        [Test]
        public void DeadEnemy_DoesNotSelectOrExecuteAnAction()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureEnemyDamageSkill();
                env.CreateMinimalBattle();
                env.Enemy.Entity.CurrentHP = 0f;
                float allyHP = env.Ally.Entity.CurrentHP;

                env.Enemy.TakeEnemyTurn();

                Assert.That(env.Ally.Entity.CurrentHP, Is.EqualTo(allyHP));
                Assert.That(GetCooldowns(env.Enemy), Is.Empty);
            }
        }

        private static Dictionary<int, int> GetCooldowns(EnemyBattleController enemy)
        {
            return (Dictionary<int, int>)typeof(EnemyBattleController)
                .GetField("_skillCooldownLeft", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(enemy);
        }
    }
}
