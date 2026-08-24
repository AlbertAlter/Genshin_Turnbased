using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public class EnemyAiRegressionTests
    {
        [Test]
        public void Reinitialize_ClearsCooldownsAndRulesFromPreviousBattle()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureEnemyDamageSkill();
                env.CreateMinimalBattle();
                GetCooldowns(env.Enemy)[990201] = 3;
                Assert.That(GetRules(env.Enemy), Is.Not.Empty);

                env.DataManager.EnemyAIDict[BattleFlowTestEnv.EnemyID] = new List<EnemyAIData>();
                env.DataManager.EnemyAIRuleDict[BattleFlowTestEnv.EnemyID] = new List<EnemyAIRuleData>();
                env.Enemy.InitEnemy(BattleFlowTestEnv.EnemyID, 1);

                Assert.That(GetCooldowns(env.Enemy), Is.Empty);
                Assert.That(GetRules(env.Enemy), Is.Empty);
            }
        }

        [Test]
        public void PickSkill_IgnoresMissingSkillsAndNonPositiveWeights()
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
                        Weight = 100
                    },
                    new EnemyAIRuleData
                    {
                        EnemyID = BattleFlowTestEnv.EnemyID,
                        PatternID = 1,
                        PatternType = "Weighted",
                        SkillID = 990201,
                        Weight = 0
                    }
                };
                env.CreateMinimalBattle();

                object picked = typeof(EnemyBattleController)
                    .GetMethod("PickSkillByWeight", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(env.Enemy, null);

                Assert.That(picked, Is.Null);
            }
        }

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

        private static List<EnemyAIRuleData> GetRules(EnemyBattleController enemy)
        {
            return (List<EnemyAIRuleData>)typeof(EnemyBattleController)
                .GetField("_currentRules", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(enemy);
        }
    }
}
