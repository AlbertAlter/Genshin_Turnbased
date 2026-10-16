using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public class EnemyTurnActionPoolTests
    {
        [Test]
        public void Pool_DistributesInRounds_WithThreatPriorityAndRepeatableTieBreaks()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                ConfigureEnemy(env, env.Enemy, 3, 10);
                var a = env.CreateEnemy(2);
                var b = env.CreateEnemy(3);
                var c = env.CreateEnemy(4);
                var enemies = new[] { a, b, env.Enemy, c };

                for (int repeat = 0; repeat < 2; repeat++)
                {
                    var random = new SequenceBattleRandomSource(intValues: new[] { 0, 0, 2 });
                    BattleRandom.SetSource(random);
                    var pool = new EnemyTurnActionPool(enemies);
                    Assert.That(pool.RemainingActions, Is.EqualTo(6));
                    CollectionAssert.AreEqual(new[] { env.Enemy, a, b, c, env.Enemy, c }, Drain(pool));
                    Assert.That(pool.RemainingActions, Is.Zero);
                    Assert.That(random.IntCallCount, Is.EqualTo(3));
                    Assert.That(random.FloatCallCount, Is.Zero);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ControlledBoss_DoesNotContributeOrReceiveActions(bool frozen)
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                ConfigureEnemy(env, env.Enemy, 3, 10);
                var minion = env.CreateEnemy(2);
                if (frozen)
                    ReactionStateSystem.SetEntityState(env.Enemy.Entity, ReactionType.Frozen,
                        2, null, reactionElementAmount: 1f);
                else
                    AddAirborne(env, env.Enemy);

                var random = new SequenceBattleRandomSource();
                BattleRandom.SetSource(random);
                var pool = new EnemyTurnActionPool(new[] { env.Enemy, minion });
                Assert.That(pool.RemainingActions, Is.EqualTo(1));
                CollectionAssert.AreEqual(new[] { minion }, Drain(pool));
                Assert.That(random.IntCallCount, Is.Zero);
                Assert.That(random.FloatCallCount, Is.Zero);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LosingEligibilityDuringTurn_RemovesContributionWithoutRefundingSpentAction(bool dead)
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                ConfigureEnemy(env, env.Enemy, 3, 10);
                var minion = env.CreateEnemy(2);
                var pool = new EnemyTurnActionPool(new[] { env.Enemy, minion });
                var random = new SequenceBattleRandomSource();
                BattleRandom.SetSource(random);
                Assert.That(pool.TryTakeNext(out var first), Is.True);
                Assert.That(first, Is.SameAs(env.Enemy));
                if (dead) env.Enemy.Entity.CurrentHP = 0f;
                else AddAirborne(env, env.Enemy);

                // 总额 4 - 失去资格单位的贡献 3 - 已花费 1 = 0，不把被扣次数转给队友。
                Assert.That(pool.TryTakeNext(out _), Is.False);
                Assert.That(pool.RemainingActions, Is.Zero);
                Assert.That(random.IntCallCount, Is.Zero);
            }
        }

        [Test]
        public void ZeroContribution_CanReceiveSharedActions_AndPoolDoesNotDuplicateUnits()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                ConfigureEnemy(env, env.Enemy, 2, 10);
                var minion = env.CreateEnemy(2);
                ConfigureEnemy(env, minion, 0, 1);
                var random = new SequenceBattleRandomSource();
                BattleRandom.SetSource(random);
                var pool = new EnemyTurnActionPool(new[] { env.Enemy, minion, env.Enemy });
                CollectionAssert.AreEqual(new[] { env.Enemy, minion }, Drain(pool));
                Assert.That(random.IntCallCount, Is.Zero);
            }
        }

        [Test]
        public void Airborne_DirectActionCannotConsumeStatusOrDrawRandom()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureEnemyDamageSkill();
                env.CreateMinimalBattle();
                var status = AddAirborne(env, env.Enemy);
                var random = new SequenceBattleRandomSource();
                BattleRandom.SetSource(random);
                env.Enemy.TakeEnemyTurn();
                env.Enemy.TakeEnemyTurn();

                Assert.That(env.Enemy.Entity.GetStatus(EnemyBattleController.AirborneStatusID), Is.SameAs(status));
                Assert.That(env.Ally.Entity.CurrentHP, Is.EqualTo(env.Ally.Entity.TotalHP));
                Assert.That(new EnemyTurnActionPool(new[] { env.Enemy }).TryTakeNext(out _), Is.False);
                Assert.That(random.IntCallCount, Is.Zero);
                Assert.That(random.FloatCallCount, Is.Zero);
            }
        }

        [TestCase(0, 0, 3)]
        [TestCase(1, 0, 1)]
        [TestCase(0, 2, 1)]
        [Timeout(6000)]
        public void RealTurn_SharesBudgetWithoutResettingSkillLimits(int usePerTurn, int cooldown, int expected)
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureEnemyDamageSkill();
                env.DataManager.EnemySkillMainDict[990201].UsePerTurn = usePerTurn;
                env.DataManager.EnemySkillMainDict[990201].Cooldown = cooldown;
                env.DataManager.EnemyMainDict[BattleFlowTestEnv.EnemyID].ActsGiven = 3;
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                var random = new SequenceBattleRandomSource();
                BattleRandom.SetSource(random);
                env.Manager.EndAllyTurn();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPostTurn);

                Assert.That(GetUsed(env.Enemy), Is.EqualTo(expected));
                Assert.That(env.Ally.Entity.CurrentHP, Is.EqualTo(1000f - 83.471074f * expected).Within(0.02f));
                Assert.That(random.IntCallCount, Is.EqualTo(expected), "无平局；仅实际选技能时抽取");
            }
        }

        [Test]
        [Timeout(8000)]
        public void RealTurn_AirborneRemovesContribution_ExpiresAtPostTurn_AndNextTurnCanAct()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureEnemyDamageSkill(0.1f);
                env.DataManager.EnemySkillMainDict[990201].UsePerTurn = 0;
                env.CreateMinimalBattle();
                ConfigureEnemy(env, env.Enemy, 3, 10);
                var minion = env.CreateEnemy(2);
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                AddAirborne(env, env.Enemy);
                var random = new SequenceBattleRandomSource();
                BattleRandom.SetSource(random);
                env.Manager.EndAllyTurn();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPostTurn);

                Assert.That(GetUsed(env.Enemy), Is.Zero);
                Assert.That(GetUsed(minion), Is.EqualTo(1));
                Assert.That(random.IntCallCount, Is.EqualTo(1));
                Assert.That(env.Enemy.Entity.HasStatus(EnemyBattleController.AirborneStatusID), Is.False);

                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Manager.EndAllyTurn();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPostTurn);
                Assert.That(GetUsed(env.Enemy), Is.EqualTo(2));
                Assert.That(GetUsed(minion), Is.EqualTo(2));
                Assert.That(random.IntCallCount, Is.EqualTo(5));
            }
        }

        private static StatusInstance AddAirborne(BattleFlowTestEnv env, EnemyBattleController enemy)
        {
            string id = EnemyBattleController.AirborneStatusID;
            env.ConfigureStatus(id);
            env.DataManager.StatusMainDict[id].StatusID = 40002;
            return enemy.Entity.AddStatus(id, env.Ally.Entity, 1, (int)TurnPhase.EnemyPostTurn, 0,
                env.DataManager.StatusMainDict[id]);
        }

        private static void ConfigureEnemy(BattleFlowTestEnv env, EnemyBattleController enemy, int contribution, int threat)
        {
            int id = 991000 + enemy.Entity.SlotPosition;
            env.DataManager.EnemyMainDict[id] = new EnemyMainData
            {
                EnemyID = id, ActsGiven = contribution, Threat = threat,
                HP_coeff = 1f, ATK_coeff = 1f, ATK_Curve = 1
            };
            if (env.DataManager.EnemyAIDict.TryGetValue(BattleFlowTestEnv.EnemyID, out var ai))
                env.DataManager.EnemyAIDict[id] = ai;
            if (env.DataManager.EnemyAIRuleDict.TryGetValue(BattleFlowTestEnv.EnemyID, out var rules))
                env.DataManager.EnemyAIRuleDict[id] = rules;
            enemy.InitEnemy(id, 1);
        }

        private static int GetUsed(EnemyBattleController enemy)
        {
            var used = (Dictionary<int, int>)typeof(EnemyBattleController)
                .GetField("_skillUsedThisTurn", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(enemy);
            return used.TryGetValue(990201, out int count) ? count : 0;
        }

        private static List<EnemyBattleController> Drain(EnemyTurnActionPool pool)
        {
            var result = new List<EnemyBattleController>();
            while (pool.TryTakeNext(out var enemy))
            {
                Assert.That(result.Count, Is.LessThan(20), "测试行动池未收敛");
                result.Add(enemy);
            }
            return result;
        }
    }
}
