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
                env.ConfigureAllyAction(0, "SK_Normal_DeathGuard", "SE_Normal_DeathGuard", "None", "1*TotalATK,0,0");
                env.ConfigureAllyAction(1, "SK_Heavy_DeathGuard", "SE_Heavy_DeathGuard", "None", "1*TotalATK,0,0");
                env.ConfigureAllyAction(2, "SK_Skill_DeathGuard", "SE_Skill_DeathGuard", "None", "1*TotalATK,0,0");
                env.ConfigureAllyAction(3, "SK_Burst_DeathGuard", "SE_Burst_DeathGuard", "None", "1*TotalATK,0,0");
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
        [Timeout(6000)]
        public void EnemyBatch_StopsImmediatelyAfterBattleEnds()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureEnemyDamageSkill(20f);
                env.DataManager.EnemySkillMainDict[990201].Cooldown = 2;
                env.CreateMinimalBattle();
                EnemyBattleController second = env.CreateEnemy(2);
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Manager.EndAllyTurn();
                env.Flow.AdvanceUntil(env.Manager, () => env.Manager.IsBattleOver, "enemy lethal action");

                Assert.That(GetCooldowns(env.Enemy), Contains.Key(990201));
                Assert.That(GetCooldowns(second), Is.Empty,
                    "Enemies after the lethal actor must not continue the finished battle.");
            }
        }

        [Test]
        [Timeout(6000)]
        public void ReappliedStatus_IsRegisteredOnce_AndTicksOncePerMatchingPhase()
        {
            using (var env = new BattleFlowTestEnv())
            {
                const string statusID = "ST_Lifecycle_NoDuplicateTick";
                env.ConfigureStatus(statusID, maxStack: 3);
                env.ConfigureAllyAction(2, "SK_Status_NoDuplicateTick", "SE_Status_NoDuplicateTick", "None", null,
                    apCost: 10, effectType: "ApplyStatus", param1: statusID, duration: 3,
                    addInPhase: (int)TurnPhase.AllyPostTurn, triggerPhase: 0);
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);

                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                env.Manager.EndAllyTurn();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);

                StatusInstance status = env.Enemy.Entity.GetStatus(statusID);
                Assert.That(status, Is.Not.Null);
                Assert.That(status.StackCount, Is.EqualTo(2));
                Assert.That(status.RemainingPhaseCount, Is.EqualTo(2),
                    "Reapplying one status instance must not duplicate its timer registration.");
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
