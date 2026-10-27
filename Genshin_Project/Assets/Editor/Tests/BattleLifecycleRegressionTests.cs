using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public class BattleLifecycleRegressionTests
    {
        [Test]
        [Timeout(5000)]
        public void DeadActiveAlly_AllPublicActionEntrypointsRejectExecution()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(0, "SK_Normal_DeathGuard", "SE_Normal_DeathGuard", "Physical", "1*TotalATK,0");
                env.ConfigureAllyAction(1, "SK_Heavy_DeathGuard", "SE_Heavy_DeathGuard", "Physical", "1*TotalATK,0");
                env.ConfigureAllyAction(2, "SK_Skill_DeathGuard", "SE_Skill_DeathGuard", "Physical", "1*TotalATK,0");
                env.ConfigureAllyAction(3, "SK_Burst_DeathGuard", "SE_Burst_DeathGuard", "Physical", "1*TotalATK,0");
                env.CreateMinimalBattle();
                env.CreateAlly(2, false);
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);

                env.Ally.Entity.CurrentEnergy = env.Ally.Entity.MaxEnergy;
                env.Ally.Entity.CurrentHP = 0f;

                Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.False);
                Assert.That(env.Manager.UseHeavyAttackBySlot(0), Is.False);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.False);
                Assert.That(env.Manager.UseBurstBySlot(0), Is.False);
                Assert.That(env.Ally.GetBlockReason(0), Does.Contain("死亡"));
            }
        }

        [Test]
        [Timeout(5000)]
        public void Switch_RejectsDeadTarget_AndAllowsEmergencyReplacementOfDeadActiveAlly()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                CharacterBattleController reserve = env.CreateAlly(2, false);
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);

                reserve.Entity.CurrentHP = 0f;
                Assert.That(env.Manager.TrySwitchActiveAlly(1), Is.False,
                    "Dead reserve characters must never become active.");
                Assert.That(env.Ally.IsActive, Is.True);

                reserve.Entity.CurrentHP = reserve.Entity.TotalHP;
                env.Ally.Entity.CurrentHP = 0f;
                Assert.That(env.Manager.CanSwitchActiveAlly(), Is.True,
                    "A dead active character must not soft-lock switching.");
                Assert.That(env.Manager.TrySwitchActiveAlly(1), Is.True);
                Assert.That(env.Ally.IsActive, Is.False);
                Assert.That(reserve.IsActive, Is.True);
            }
        }

        [Test]
        [Timeout(5000)]
        public void DeathAtAllyTurnStart_RequiresFreeSwitch_ThenNormalSwitchCostsAP()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                CharacterBattleController reserve = env.CreateAlly(2, false);
                env.Ally.Entity.CurrentHP = 0f;
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);

                int apBeforeForcedSwitch = env.Manager.APManager.CurrentAP;
                Assert.That(env.Manager.IsFreeDeathSwitchPending, Is.True);
                env.Manager.EndAllyTurn();
                env.Scheduler.Step();
                Assert.That(env.Manager.CurrentPhase, Is.EqualTo(TurnPhase.AllyAction),
                    "The forced replacement cannot be bypassed by ending the turn.");
                Assert.That(env.Manager.TrySwitchActiveAllyWithAPCost(1), Is.True);
                Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(apBeforeForcedSwitch));
                Assert.That(env.Manager.IsFreeDeathSwitchPending, Is.False);
                Assert.That(reserve.IsActive, Is.True);

                env.Ally.Entity.CurrentHP = env.Ally.Entity.TotalHP;
                Assert.That(env.Manager.TrySwitchActiveAllyWithAPCost(0), Is.True);
                Assert.That(env.Manager.APManager.CurrentAP,
                    Is.EqualTo(apBeforeForcedSwitch - BattleManager.SwitchAPCost));
            }
        }

        [Test]
        [Timeout(5000)]
        public void SwitchSelection_SkipsDeadAndEmptyFormationSlots()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                CharacterBattleController deadSecond = env.CreateAlly(2, false);
                CharacterBattleController livingFourth = env.CreateAlly(4, false);
                deadSecond.Entity.CurrentHP = 0f;

                Assert.That(env.Manager.GetAllyBySlot(2), Is.Null,
                    "Formation position 3 is an empty slot, not list index 3.");
                Assert.That(env.Manager.FindNextLivingAllySlot(0, 1), Is.EqualTo(3),
                    "Moving right skips the dead position 2 and empty position 3.");
                Assert.That(env.Manager.FindNextLivingAllySlot(3, -1), Is.EqualTo(0),
                    "Moving left skips the empty and dead positions.");
                Assert.That(env.Manager.GetAllyBySlot(3), Is.SameAs(livingFourth));
            }
        }

        [Test]
        [Timeout(5000)]
        public void Switch_AllowsFrozenOrKnockedDownActiveAlly()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                CharacterBattleController reserve = env.CreateAlly(2, false);
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);

                ReactionStateSystem.SetEntityState(
                    env.Ally.Entity,
                    ReactionType.Frozen,
                    1,
                    null,
                    (int)TurnPhase.AllyAction);

                Assert.That(env.Manager.TrySwitchActiveAlly(1), Is.True,
                    "Frozen characters are still allowed to switch out.");

                Assert.That(env.Manager.TrySwitchActiveAlly(0), Is.True,
                    "Knocked-down characters are still allowed to switch out.");
            }
        }

        [Test]
        [Timeout(5000)]
        public void SelfEffect_IgnoresSelectedEnemyOrAllyPosition()
        {
            using (var env = new BattleFlowTestEnv())
            {
                const string statusID = "ST_SelfTarget_Regression";
                env.ConfigureStatus(statusID);
                env.ConfigureAllyAction(2, "SK_SelfTarget_Regression", "SE_SelfTarget_Regression",
                    "Physical", null, 10, effectType: "ApplyStatus", param1: statusID,
                    duration: 1, addInPhase: (int)TurnPhase.AllyAction, targetType: "Self");
                env.CreateMinimalBattle();
                CharacterBattleController reserve = env.CreateAlly(2, false);
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Ally.ForcedTargetPositions.Add(2);

                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                Assert.That(env.Ally.Entity.HasStatus(statusID), Is.True);
                Assert.That(reserve.Entity.HasStatus(statusID), Is.False);
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        [Timeout(6000)]
        public void EnemyBatch_StopsImmediatelyAfterBattleEnds(int firstActorPosition)
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureEnemyDamageSkill(20f);
                env.DataManager.EnemySkillMainDict[990201].Cooldown = 2;
                env.CreateMinimalBattle();
                EnemyBattleController second = env.CreateEnemy(2);
                EnemyBattleController lethalActor = firstActorPosition == 1 ? env.Enemy : second;
                EnemyBattleController waitingActor = firstActorPosition == 1 ? second : env.Enemy;
                // 同威胁度会随机分配先手；显式指定优先级，分别覆盖两个位置先打出致命伤害。
                lethalActor.Entity.Threat = 2;
                waitingActor.Entity.Threat = 1;
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Manager.EndAllyTurn();
                env.Flow.AdvanceUntil(env.Manager, () => env.Manager.IsBattleOver, "enemy lethal action");

                Assert.That(env.Ally.Entity.IsAlive, Is.False);
                Assert.That(GetCooldowns(lethalActor), Contains.Key(990201));
                Assert.That(GetCooldowns(waitingActor), Is.Empty,
                    "Enemies after the lethal actor must not continue the finished battle.");
            }
        }

        [Test]
        [Timeout(9000)]
        public void ReappliedStatus_EachInstanceIsRegisteredOnce_AndTicksOncePerMatchingPhase()
        {
            using (var env = new BattleFlowTestEnv())
            {
                const string statusID = "ST_Lifecycle_NoDuplicateTick";
                env.ConfigureStatus(statusID, maxStack: 3);
                env.ConfigureAllyAction(2, "SK_Status_NoDuplicateTick", "SE_Status_NoDuplicateTick", "Physical", null,
                    apCost: 10, effectType: "ApplyStatus", param1: statusID, duration: 3,
                    addInPhase: (int)TurnPhase.AllyPostTurn, triggerPhase: 0);
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);

                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                StatusInstance first = env.Enemy.Entity.GetStatus(statusID);
                Assert.That(first, Is.Not.Null);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                var instances = env.Enemy.Entity.GetStatusInstances(statusID);
                Assert.That(instances, Has.Count.EqualTo(2));
                Assert.That(instances[0], Is.SameAs(first));
                Assert.That(instances[1], Is.Not.SameAs(first));
                Assert.That(env.Enemy.Entity.GetStatusStackCount(statusID), Is.EqualTo(2));
                foreach (StatusInstance instance in instances)
                {
                    Assert.That(instance.StackCount, Is.EqualTo(1));
                    Assert.That(instance.RemainingPhaseCount, Is.EqualTo(3));
                    // 正式施加已登记过；重复登记同一实例也不能让它每阶段多扣一次。
                    env.Manager.RegisterStatusTick(instance, env.Enemy.Entity, null);
                }
                env.Manager.EndAllyTurn();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);

                for (int remaining = 2; remaining >= 1; remaining--)
                {
                    CollectionAssert.AreEqual(instances, env.Enemy.Entity.GetStatusInstances(statusID));
                    foreach (StatusInstance instance in instances)
                        Assert.That(instance.RemainingPhaseCount, Is.EqualTo(remaining),
                            "每份独立实例在匹配阶段只扣一次，不能合并层数或重复登记计时器。");

                    env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                    foreach (StatusInstance instance in instances)
                        Assert.That(instance.RemainingPhaseCount, Is.EqualTo(remaining),
                            "不匹配的阶段不能扣减该实例的持续时间。");
                    env.Manager.EndAllyTurn();
                    env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);
                }

                Assert.That(env.Enemy.Entity.GetStatusInstances(statusID), Is.Empty);
                foreach (StatusInstance instance in instances)
                {
                    Assert.That(instance.RemainingPhaseCount, Is.Zero);
                    Assert.That(instance.IsActive, Is.False);
                }
            }
        }

        [Test]
        public void DeadEntity_RejectsHealingAndFurtherHitNotifications()
        {
            var gameObject = new GameObject("DeadEntity_Guard_Test");
            try
            {
                BattleEntity entity = gameObject.AddComponent<BattleEntity>();
                entity.TotalHP = 100f;
                entity.CurrentHP = 0f;

                entity.Heal(50f);
                DamageResolvedEvent result = entity.TakeDamage(20f, DamageSourceInfo.CreateUnknown());

                Assert.That(entity.CurrentHP, Is.Zero);
                Assert.That(result.ActualHPDamage, Is.Zero);
                Assert.That(result.HitLanded, Is.False);
                Assert.That(result.CausedDeath, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
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
