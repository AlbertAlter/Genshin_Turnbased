using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public class BattleFlowDamageTests
    {
        [Test]
        [Timeout(5000)]
        public void SkillEntry_DeductsAPStartsCooldownAndDealsDocumentDamage()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(2, "SK_Skill_Flow", "SE_Skill_Flow", "None", "1*TotalATK,10,0", 30, 2);
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);

                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);

                // 文档手算：100 ATK × 1.0 × 防御区[101/(101+101)=0.5] × 0抗 = 50。
                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(950f).Within(0.01f));
                Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(70));
                Assert.That(env.Ally.GetCooldownRemaining(2), Is.EqualTo(2));
            }
        }

        [Test]
        [Timeout(5000)]
        public void MultiHit_IsSettledHitByHitAndHPNeverDropsBelowZero()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(2, "SK_Skill_Flow", "SE_Skill_Flow", "None", "1*TotalATK,0,0");
                env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_2"][1].Hits2 = "1*TotalATK,0,0";
                env.CreateMinimalBattle();
                env.Enemy.Entity.TotalHP = 75f;
                env.Enemy.Entity.CurrentHP = 75f;
                int ended = 0;
                env.Manager.OnBattleEnded += _ => ended++;
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);

                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);

                Assert.That(env.Enemy.Entity.CurrentHP, Is.Zero.Within(0.01f));
                Assert.That(env.Enemy.Entity.IsDead, Is.True);
                Assert.That(ended, Is.EqualTo(1), "致死只允许发出一次战斗结束通知");
            }
        }

        [Test]
        [Timeout(5000)]
        public void MultiHitMultiTarget_EachTargetHitUsesOneFinalIndependentCritRoll()
        {
            using (var env = new BattleFlowTestEnv())
            {
                const string effectID = "SE_Skill_CritSequence";
                env.ConfigureAllyAction(2, "SK_Skill_CritSequence", effectID, "None", "1*TotalATK,0,0");
                env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_2"][1].Hits2 =
                    "1*TotalATK,0,0";
                env.CreateMinimalBattle();
                EnemyBattleController second = env.CreateEnemy(2);
                env.Ally.Entity.CritRate = 0.5f;
                env.Ally.Entity.CritDMG = 1f;
                env.Ally.ForcedTargetPositions.Clear();
                env.Ally.ForcedTargetPositions.Add(1);
                env.Ally.ForcedTargetPositions.Add(2);
                var random = new SequenceBattleRandomSource(
                    new[] { 0.1f, 0.9f, 0.9f, 0.1f });
                BattleRandom.SetSource(random);

                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);

                // 每个单位分别承受一次暴击100和一次未暴击50；共四次独立抽取。
                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(850f).Within(0.01f));
                Assert.That(second.Entity.CurrentHP, Is.EqualTo(850f).Within(0.01f));
                Assert.That(random.FloatCallCount, Is.EqualTo(4));
            }
        }

        [Test]
        [Timeout(5000)]
        public void Splash_UsesAttackerStatsAndRollsIndependentlyFromPrimaryTarget()
        {
            using (var env = new BattleFlowTestEnv())
            {
                const string effectID = "SE_Skill_SplashCrit";
                env.ConfigureAllyAction(2, "SK_Skill_SplashCrit", effectID, "None", "1*TotalATK,0,0");
                env.DataManager.SkillEffectDict[effectID].Param2 = "Splash(1; 0,1)";
                env.CreateMinimalBattle();
                EnemyBattleController adjacent = env.CreateEnemy(2);
                env.Ally.Entity.CritRate = 0.5f;
                env.Ally.Entity.CritDMG = 1f;
                env.Ally.ForcedTargetPositions.Clear();
                env.Ally.ForcedTargetPositions.Add(1);
                env.Enemy.Entity.AddShield(1000f, string.Empty, 2f);
                var random = new SequenceBattleRandomSource(new[] { 0.8f, 0.2f });
                BattleRandom.SetSource(random);

                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);

                // 主目标未暴击50且被护盾完全吸收；溅射仍发生，并用攻击者属性独立暴击为100。
                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(1000f).Within(0.01f));
                Assert.That(adjacent.Entity.CurrentHP, Is.EqualTo(900f).Within(0.01f));
                Assert.That(random.FloatCallCount, Is.EqualTo(2));
            }
        }

        [Test]
        [Timeout(5000)]
        public void Shield_ReportsAbsorptionSeparatelyFromActualHPDamage()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(2, "SK_Skill_Flow", "SE_Skill_Flow", "None", "1*TotalATK,0,0");
                env.CreateMinimalBattle();
                env.Enemy.Entity.AddShield(30f, string.Empty, 2f);
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);

                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);

                // 50 最终伤害 - 30 白条盾 = 20 实际扣血。
                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(980f).Within(0.01f));
                Assert.That(env.Enemy.Entity.GetTotalShieldHP(), Is.Zero.Within(0.01f));
            }
        }

        [Test]
        [Timeout(5000)]
        public void ElementShield_UsesDocumentAbsorptionMultiplierForOtherElements()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(2, "SK_Skill_Flow", "SE_Skill_Flow", "Hydro", "1*TotalATK,0,0");
                env.CreateMinimalBattle();
                env.Enemy.Entity.AddShield(20f, "Pyro", 2f);
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);

                // 文档：非同元素盾也有150%吸收，20盾吸收30，50最终伤害仅扣20血。
                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(980f).Within(0.01f));
                Assert.That(env.Enemy.Entity.GetTotalShieldHP(), Is.Zero.Within(0.01f));
            }
        }
    }
}
