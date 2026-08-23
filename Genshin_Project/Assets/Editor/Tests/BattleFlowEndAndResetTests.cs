using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public class BattleFlowEndAndResetTests
    {
        [Test]
        [Timeout(5000)]
        public void LethalSkill_EmitsVictoryOnceAndBlocksFurtherActions()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(2, "SK_Skill_Flow", "SE_Skill_Flow", "None", "1*TotalATK,0,0", 10);
                env.CreateMinimalBattle();
                env.Enemy.Entity.TotalHP = env.Enemy.Entity.CurrentHP = 40f;
                int count = 0;
                bool? result = null;
                env.Manager.OnBattleEnded += won => { count++; result = won; };
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);

                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                Assert.That(env.Manager.IsBattleOver, Is.True);
                Assert.That(env.Manager.Victory, Is.True);
                Assert.That(count, Is.EqualTo(1));
                Assert.That(result, Is.True);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.False,
                    "战斗结束后公开行动入口必须拒绝继续行动");
            }
        }

        [Test]
        [Timeout(6000)]
        public void EnemyLethalAction_EmitsFailureOnceAndStopsPhaseLoop()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureEnemyDamageSkill(20f);
                env.CreateMinimalBattle();
                int count = 0;
                env.Manager.OnBattleEnded += _ => count++;
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Manager.EndAllyTurn();
                env.Flow.AdvanceUntil(env.Manager, () => env.Manager.IsBattleOver, "battle over after enemy lethal action");

                Assert.That(env.Ally.Entity.CurrentHP, Is.Zero.Within(0.01f));
                Assert.That(env.Manager.IsBattleOver, Is.True);
                Assert.That(env.Manager.Victory, Is.False);
                Assert.That(count, Is.EqualTo(1));
                Assert.That(env.Scheduler.IsRunning, Is.False);
                TurnPhase stoppedAt = env.Manager.CurrentPhase;
                Assert.That(env.Scheduler.Step(), Is.False);
                Assert.That(env.Manager.CurrentPhase, Is.EqualTo(stoppedAt));
            }
        }

        [Test]
        [Timeout(5000)]
        public void ResetBattle_ClearsAllSessionStateAndAllowsCleanRestart()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                env.Enemy.Entity.ApplyAura("Pyro", 2f, env.Ally.Entity.EntityID);
                env.Enemy.Entity.AddShield(40f, "Pyro", 2f);
                env.Manager.PendingActionTargetPositions.Add(1);
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);

                env.Manager.ResetBattle();

                Assert.That(env.Manager.IsBattleRunning, Is.False);
                Assert.That(env.Manager.IsBattleOver, Is.False);
                Assert.That(env.Manager.TurnCount, Is.EqualTo(1));
                Assert.That(env.Manager.PendingActionTargetPositions, Is.Empty);
                Assert.That(env.Manager.Allies, Is.Empty);
                Assert.That(env.Manager.Enemies, Is.Empty);
                Assert.That(ReactionStateSystem.ActiveStates, Is.Empty);
                Assert.That(BloomCoreSystem.ActiveCores, Is.Empty);
                Assert.That(env.Enemy.Entity.ElementalAuras, Is.Empty,
                    "旧单位不能保留上一局元素附着");
                Assert.That(env.Enemy.Entity.Shields, Is.Empty,
                    "旧单位不能保留上一局护盾");
            }
        }
    }
}
