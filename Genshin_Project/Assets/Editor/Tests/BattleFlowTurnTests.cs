using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public class BattleFlowTurnTests
    {
        [Test]
        [Timeout(5000)]
        public void StartBattle_ThenEndAllyTurn_AdvancesThroughRealPhaseLoop()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();

                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);

                Assert.That(env.Manager.IsBattleRunning, Is.True);
                Assert.That(env.Manager.IsBattleOver, Is.False);
                Assert.That(env.Manager.CurrentPhase, Is.EqualTo(TurnPhase.AllyAction));
                Assert.That(env.Manager.APManager.CurrentAP,
                    Is.EqualTo(env.Manager.APManager.MaxAP));

                env.Manager.EndAllyTurn();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);

                Assert.That(env.Manager.IsBattleRunning, Is.True);
                Assert.That(env.Manager.CurrentPhase, Is.EqualTo(TurnPhase.EnemyPreTurn));
            }
        }

        [Test]
        [Timeout(8000)]
        public void SixPhases_TurnCountAPAndCooldownAdvanceExactlyOnce()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(2, "SK_Skill_Flow", "SE_Skill_Flow", "None",
                    "1*TotalATK,0,0", 30, 2);
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                Assert.That(env.Manager.TurnCount, Is.EqualTo(1));
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                Assert.That(env.Ally.GetCooldownRemaining(2), Is.EqualTo(2),
                    "第一回合不得错误减少刚进入的冷却");

                env.Manager.EndAllyTurn();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyPostTurn);
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyAction);
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPostTurn);
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyPreTurn);
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);

                Assert.That(env.Manager.TurnCount, Is.EqualTo(2));
                Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(100));
                Assert.That(env.Ally.GetCooldownRemaining(2), Is.EqualTo(1));
            }
        }

        [Test]
        [Timeout(6000)]
        public void EnemyAction_ExecutesEachLivingEnemyOnceThroughConfiguredSkill()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureEnemyDamageSkill();
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Manager.EndAllyTurn();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPostTurn);

                // 敌方文档公式：100 × [1 - 100/(100+5*1+500)] = 83.471074。
                Assert.That(env.Ally.Entity.CurrentHP, Is.EqualTo(916.5289f).Within(0.01f));
            }
        }

        [Test]
        [Timeout(8000)]
        public void FrozenEnemy_DoesNotAct_AndAuraDecaysOnlyAtItsOriginPhase()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureEnemyDamageSkill();
                env.ConfigureAllyAction(0, "SK_Normal_Flow", "SE_Normal_Flow", "Cryo", "1*TotalATK,0,1", 10);
                env.ConfigureAllyAction(2, "SK_Skill_Flow", "SE_Skill_Flow", "Hydro", "1*TotalATK,0,2", 10);
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Manager.UseNormalAttackBySlot(0);
                env.Manager.UseSkillBySlot(0);
                Assert.That(FrozenReactionHandler.IsFrozen(env.Enemy.Entity), Is.True);
                Assert.That(env.Enemy.Entity.GetAura("Hydro").AuraAmount, Is.EqualTo(1f).Within(0.01f));

                float allyHP = env.Ally.Entity.CurrentHP;
                env.Manager.EndAllyTurn();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPostTurn);
                Assert.That(env.Ally.Entity.CurrentHP, Is.EqualTo(allyHP).Within(0.01f));
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                Assert.That(env.Enemy.Entity.GetAura("Hydro").AuraAmount, Is.EqualTo(0.5f).Within(0.01f));
            }
        }
    }
}
