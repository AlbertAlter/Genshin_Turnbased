using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public class BattleFlowStatusTests
    {
        [TestCase(2, 3)]
        [TestCase(3, 3)]
        [TestCase(3, 0)]
        [Timeout(9000)]
        public void PhaseAction_ExecutesOnItsTurnOnceAndNeverOnTrigger(int timerPhase, int triggerPhase)
        {
            using (var env = new BattleFlowTestEnv())
            {
                const string statusID = "ST_PhaseAction";
                env.ConfigureStatus(statusID, actionType: "OnItsTurn", actionEffectID2: "STE_PhaseEnergy");
                env.DataManager.StatusActionDict[statusID].Add(new StatusActionData
                {
                    StatusID2 = statusID, ActionType = "OnTrigger", Param1 = "STE_EventEnergy"
                });
                env.DataManager.StatusEffectDict["STE_PhaseEnergy"] = new StatusEffectData
                {
                    StatusEffectID2 = "STE_PhaseEnergy", EffectType = "GainEnergy",
                    Param1 = "1", Param2 = "Flat", TargetType = "Self", ExecuteCount = 1
                };
                env.DataManager.StatusEffectDict["STE_EventEnergy"] = new StatusEffectData
                {
                    StatusEffectID2 = "STE_EventEnergy", EffectType = "GainEnergy",
                    Param1 = "10", Param2 = "Flat", TargetType = "Self", ExecuteCount = 1
                };
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Ally.Entity.CurrentEnergy = 0;
                var status = env.Ally.Entity.AddStatus(statusID, env.Ally.Entity, 3,
                    timerPhase, triggerPhase, env.DataManager.StatusMainDict[statusID]);
                env.Manager.EndAllyTurn();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);
                Assert.That(env.Ally.Entity.CurrentEnergy, Is.EqualTo(1));
                Assert.That(status.RemainingPhaseCount, Is.EqualTo(timerPhase == 3 ? 2 : 3));

                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                Assert.That(env.Ally.Entity.CurrentEnergy, Is.EqualTo(1), "计时阶段不能重复执行显式触发阶段的行动");
                env.Manager.EndAllyTurn();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);
                Assert.That(env.Ally.Entity.CurrentEnergy, Is.EqualTo(2));
            }
        }

        [Test]
        [Timeout(9000)]
        public void TriggerPhaseWithoutOnItsTurn_DoesNotFallBackToOnTrigger()
        {
            using (var env = new BattleFlowTestEnv())
            {
                const string statusID = "ST_EventOnlyPhase";
                env.ConfigureStatus(statusID, actionType: "OnTrigger", actionEffectID2: "STE_EventOnlyEnergy");
                env.DataManager.StatusEffectDict["STE_EventOnlyEnergy"] = new StatusEffectData
                {
                    StatusEffectID2 = "STE_EventOnlyEnergy", EffectType = "GainEnergy",
                    Param1 = "10", Param2 = "Flat", TargetType = "Self", ExecuteCount = 1
                };
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Ally.Entity.CurrentEnergy = 0;
                env.Ally.Entity.AddStatus(statusID, env.Ally.Entity, 3, 3, 3,
                    env.DataManager.StatusMainDict[statusID]);
                env.Manager.EndAllyTurn();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);
                Assert.That(env.Ally.Entity.CurrentEnergy, Is.EqualTo(0));
            }
        }

        [Test]
        [Timeout(5000)]
        public void SkillApplyStatus_UsesConfiguredHostStackDurationAndTimerPhase()
        {
            using (var env = new BattleFlowTestEnv())
            {
                const string statusID = "ST_BattleFlow_Debuff";
                env.ConfigureStatus(statusID, maxStack: 2);
                env.ConfigureAllyAction(2, "SK_Skill_Flow", "SE_Skill_Flow", "Physical", null,
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
        public void SameStatus_FromRealSkill_AddsIndependentInstancesAndReplacesOldestOnlyAtCap()
        {
            using (var env = new BattleFlowTestEnv())
            {
                const string statusID = "ST_BattleFlow_Stack";
                env.ConfigureStatus(statusID, maxStack: 2);
                env.ConfigureAllyAction(2, "SK_Skill_Flow", "SE_Skill_Flow", "Physical", null,
                    apCost: 10, effectType: "ApplyStatus", param1: statusID,
                    duration: 2, addInPhase: 2);
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);

                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                StatusInstance first = env.Enemy.Entity.GetStatus(statusID);
                Assert.That(first, Is.Not.Null);
                long firstOrder = first.ApplyOrder;
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);

                // 普通状态按实例叠加，不合并进第一份的 StackCount，也不提前替换旧实例。
                var instances = env.Enemy.Entity.GetStatusInstances(statusID);
                Assert.That(instances, Has.Count.EqualTo(2));
                Assert.That(env.Enemy.Entity.GetStatusStackCount(statusID), Is.EqualTo(2));
                Assert.That(env.Enemy.Entity.GetStatus(statusID), Is.SameAs(first));
                Assert.That(instances[0], Is.SameAs(first));
                Assert.That(first.ApplyOrder, Is.EqualTo(firstOrder));
                StatusInstance second = instances[1];
                Assert.That(second, Is.Not.SameAs(first));
                Assert.That(second.ApplyOrder, Is.GreaterThan(firstOrder));
                foreach (StatusInstance instance in instances)
                {
                    Assert.That(instance.StackCount, Is.EqualTo(1));
                    Assert.That(instance.RemainingPhaseCount, Is.EqualTo(2));
                    Assert.That(instance.Caster, Is.SameAs(env.Ally.Entity));
                    Assert.That(instance.IsActive, Is.True);
                }

                // MaxStack=2、WhenMax=0：第三次施加只替换最早的一份。
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                instances = env.Enemy.Entity.GetStatusInstances(statusID);
                Assert.That(instances, Has.Count.EqualTo(2));
                Assert.That(env.Enemy.Entity.GetStatusStackCount(statusID), Is.EqualTo(2));
                Assert.That(env.Enemy.Entity.HasStatusInstance(first), Is.False);
                Assert.That(first.IsActive, Is.False);
                Assert.That(instances[0], Is.SameAs(second));
                Assert.That(second.RemainingPhaseCount, Is.EqualTo(2));
                Assert.That(instances[1], Is.Not.SameAs(first));
                Assert.That(instances[1], Is.Not.SameAs(second));
                Assert.That(instances[1].ApplyOrder, Is.GreaterThan(second.ApplyOrder));
                Assert.That(instances[1].StackCount, Is.EqualTo(1));
                Assert.That(instances[1].RemainingPhaseCount, Is.EqualTo(2));
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
                env.ConfigureAllyAction(2, "SK_Skill_Flow", "SE_Skill_Flow", "Physical", null,
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
                env.ConfigureStatus(statusID, actionType: "OnItsTurn", actionEffectID2: statusEffectID);
                env.ConfigureAllyAction(2, "SK_Skill_Flow", "SE_Skill_Flow", "Physical", null,
                    apCost: 10, effectType: "ApplyStatus", param1: statusID, duration: 2,
                    addInPhase: (int)TurnPhase.AllyPostTurn,
                    triggerPhase: (int)TurnPhase.AllyPostTurn);
                env.DataManager.StatusEffectDict[statusEffectID] = new StatusEffectData
                {
                    StatusEffectID = 99000101,
                    StatusEffectID2 = statusEffectID,
                    EffectType = "Damage",
                    Element = "Physical",
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
                            Hits1 = "1*TotalATK,0"
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
                env.ConfigureStatus(statusID, actionType: "OnItsTurn", actionEffectID2: statusEffectID);
                env.ConfigureAllyAction(2, "SK_Skill_CritDot", "SE_Skill_CritDot", "Physical", null,
                    apCost: 10, effectType: "ApplyStatus", param1: statusID, duration: 2,
                    addInPhase: (int)TurnPhase.AllyPostTurn,
                    triggerPhase: (int)TurnPhase.AllyPostTurn);
                env.DataManager.StatusEffectDict[statusEffectID] = new StatusEffectData
                {
                    StatusEffectID = 99000201,
                    StatusEffectID2 = statusEffectID,
                    EffectType = "Damage",
                    Element = "Physical",
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
                            Hits1 = "1*TotalATK,0"
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
