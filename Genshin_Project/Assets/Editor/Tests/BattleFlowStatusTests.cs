using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public class BattleFlowStatusTests
    {
        [Test]
        [Timeout(5000)]
        public void SkillApplyStatus_UsesConfiguredHostStackDurationAndTimerPhase()
        {
            using (var env = new BattleFlowTestEnv())
            {
                const string statusID = "ST_BattleFlow_Debuff";
                env.ConfigureStatus(statusID, maxStack: 2);
                env.ConfigureAllyAction(2, "SK_Skill_Flow", "SE_Skill_Flow", "None", null,
                    effectType: "ApplyStatus", param1: statusID, duration: 2,
                    addInPhase: (int)TurnPhase.AllyAction, triggerPhase: (int)TurnPhase.AllyPostTurn);
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);

                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                StatusInstance status = env.Enemy.Entity.GetStatus(statusID);
                Assert.That(status, Is.Not.Null);
                Assert.That(status.StackCount, Is.EqualTo(1));
                Assert.That(status.RemainingPhaseCount, Is.EqualTo(2));
                Assert.That(status.AddInPhase, Is.EqualTo((int)TurnPhase.AllyAction));
                Assert.That(status.TriggerPhase, Is.EqualTo((int)TurnPhase.AllyPostTurn));

                // 新状态不能在施加它的同一个阶段立即损失持续时间。
                Assert.That(status.RemainingPhaseCount, Is.EqualTo(2));
            }
        }

        [Test]
        [Timeout(5000)]
        public void SameStatus_FromRealSkill_StacksAndRefreshesWithoutReplacingEarly()
        {
            using (var env = new BattleFlowTestEnv())
            {
                const string statusID = "ST_BattleFlow_Stack";
                env.ConfigureStatus(statusID, maxStack: 2);
                env.ConfigureAllyAction(2, "SK_Skill_Flow", "SE_Skill_Flow", "None", null,
                    apCost: 10, effectType: "ApplyStatus", param1: statusID,
                    duration: 2, addInPhase: 2, triggerPhase: 3);
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);

                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                long firstOrder = env.Enemy.Entity.GetStatus(statusID).ApplyOrder;
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);

                StatusInstance status = env.Enemy.Entity.GetStatus(statusID);
                Assert.That(status.StackCount, Is.EqualTo(2));
                Assert.That(status.RemainingPhaseCount, Is.EqualTo(2));
                Assert.That(status.ApplyOrder, Is.EqualTo(firstOrder));
            }
        }

        [Test]
        [Timeout(5000)]
        public void ResetBattle_RemovesEntityFieldAndRegisteredStatusState()
        {
            using (var env = new BattleFlowTestEnv())
            {
                const string statusID = "ST_BattleFlow_Reset";
                env.ConfigureStatus(statusID);
                env.ConfigureAllyAction(2, "SK_Skill_Flow", "SE_Skill_Flow", "None", null,
                    effectType: "ApplyStatus", param1: statusID, duration: 2, addInPhase: 2, triggerPhase: 3);
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                Assert.That(env.Enemy.Entity.HasStatus(statusID), Is.True);

                env.Manager.ResetBattle();

                Assert.That(env.Manager.Allies, Is.Empty);
                Assert.That(env.Manager.Enemies, Is.Empty);
                Assert.That(env.Manager.Field.GetSlot(BattleSide.Enemy, 1).StatusList, Is.Empty);
                Assert.That(ReactionStateSystem.ActiveStates, Is.Empty);
                Assert.That(BloomCoreSystem.ActiveCores, Is.Empty);
            }
        }

        [Test]
        [Timeout(9000)]
        public void DamageAndStatus_ThenPhaseTrigger_DamagesTicksAndExpiresInOrder()
        {
            using (var env = new BattleFlowTestEnv())
            {
                const string statusID = "ST_BattleFlow_Dot";
                const string statusEffectID = "STE_BattleFlow_Dot";
                env.ConfigureStatus(statusID, actionType: "OnTrigger", actionEffectID2: statusEffectID);
                env.ConfigureAllyAction(2, "SK_Skill_Flow", "SE_Skill_Flow", "None", null,
                    apCost: 10, effectType: "ApplyStatus", param1: statusID, duration: 2,
                    addInPhase: (int)TurnPhase.AllyPostTurn,
                    triggerPhase: (int)TurnPhase.AllyPostTurn);
                env.DataManager.StatusEffectDict[statusEffectID] = new StatusEffectData
                {
                    StatusEffectID = 99000101,
                    StatusEffectID2 = statusEffectID,
                    EffectType = "Damage",
                    Element = "None",
                    DamageType = "Skill",
                    Param1 = string.Empty,
                    TargetType = "Enemy",
                    TargetSelect = "1,1"
                };
                env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_2"] =
                    new System.Collections.Generic.Dictionary<int, SkillLevelData>
                    {
                        [1] = new SkillLevelData
                        {
                            CharacterID = BattleFlowTestEnv.AllyID,
                            SkillType = 2,
                            SkillLevel = 1,
                            ParamID = statusEffectID,
                            Hits1 = "1*TotalATK,0,0"
                        }
                    };
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                env.Manager.EndAllyTurn();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);

                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(950f).Within(0.01f));
                Assert.That(env.Enemy.Entity.GetStatus(statusID).RemainingPhaseCount, Is.EqualTo(1));

                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Manager.EndAllyTurn();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);
                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(900f).Within(0.01f));
                Assert.That(env.Enemy.Entity.GetStatus(statusID), Is.Null);
            }
        }

        [Test]
        [Timeout(9000)]
        public void StatusDamage_EachTickUsesOneFinalIndependentCritRoll()
        {
            using (var env = new BattleFlowTestEnv())
            {
                const string statusID = "ST_BattleFlow_CritDot";
                const string statusEffectID = "STE_BattleFlow_CritDot";
                env.ConfigureStatus(statusID, actionType: "OnTrigger", actionEffectID2: statusEffectID);
                env.ConfigureAllyAction(2, "SK_Skill_CritDot", "SE_Skill_CritDot", "None", null,
                    apCost: 10, effectType: "ApplyStatus", param1: statusID, duration: 2,
                    addInPhase: (int)TurnPhase.AllyPostTurn,
                    triggerPhase: (int)TurnPhase.AllyPostTurn);
                env.DataManager.StatusEffectDict[statusEffectID] = new StatusEffectData
                {
                    StatusEffectID = 99000201,
                    StatusEffectID2 = statusEffectID,
                    EffectType = "Damage",
                    Element = "None",
                    DamageType = "Skill",
                    TargetType = "Enemy",
                    TargetSelect = "1,1"
                };
                env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_2"] =
                    new System.Collections.Generic.Dictionary<int, SkillLevelData>
                    {
                        [1] = new SkillLevelData
                        {
                            CharacterID = BattleFlowTestEnv.AllyID,
                            SkillType = 2,
                            SkillLevel = 1,
                            ParamID = statusEffectID,
                            Hits1 = "1*TotalATK,0,0"
                        }
                    };
                env.CreateMinimalBattle();
                env.Ally.Entity.CritRate = 0.5f;
                env.Ally.Entity.CritDMG = 1f;
                var random = new SequenceBattleRandomSource(new[] { 0.2f, 0.8f });
                BattleRandom.SetSource(random);
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);

                env.Manager.EndAllyTurn();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);
                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(900f).Within(0.01f));

                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Manager.EndAllyTurn();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);
                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(850f).Within(0.01f));
                Assert.That(random.FloatCallCount, Is.EqualTo(2));
            }
        }
    }
}
