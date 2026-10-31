using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public class EnemyTurnActionPoolTests
    {
        [Test]
        public void AllFreeze_KeepsContributionButReceivesNoActions()
        {
            using var env = new BattleFlowTestEnv();
            env.CreateMinimalBattle();
            ConfigureEnemy(env, env.Enemy, 2, 10);
            var other = env.CreateEnemy(2);
            ConfigureEnemy(env, other, 1, 1);
            AddFreeze(env, env.Enemy, "All");
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            var pool = new EnemyTurnActionPool(new[] { env.Enemy, other });
            Assert.That(pool.RemainingActions, Is.EqualTo(3));
            CollectionAssert.AreEqual(new[] { other, other, other }, Drain(pool));
            Assert.That(env.Enemy.CanAct, Is.True);
            Assert.That(random.IntCallCount, Is.Zero);
        }

        [Test]
        public void ExtraFreeze_ReceivesEveryGuaranteedRoundButNoRandomRemainder()
        {
            using var env = new BattleFlowTestEnv();
            env.CreateMinimalBattle();
            ConfigureEnemy(env, env.Enemy, 3, 1);
            var other = env.CreateEnemy(2);
            ConfigureEnemy(env, other, 2, 1);
            AddFreeze(env, env.Enemy, "Extra");
            var random = new SequenceBattleRandomSource(intValues: new[] { 0, 0 }); BattleRandom.SetSource(random);
            var pool = new EnemyTurnActionPool(new[] { env.Enemy, other });
            Assert.That(pool.RemainingActions, Is.EqualTo(5));
            CollectionAssert.AreEqual(new[] { env.Enemy, other, env.Enemy, other, other }, Drain(pool));
            Assert.That(random.IntCallCount, Is.EqualTo(2));
            Assert.That(random.FloatCallCount, Is.Zero);
        }

        [Test]
        public void ExtraFreeze_FilteringDoesNotReclassifyRandomRemainderAsGuaranteed()
        {
            using var env = new BattleFlowTestEnv();
            env.CreateMinimalBattle();
            ConfigureEnemy(env, env.Enemy, 1, 1);
            var other = env.CreateEnemy(2);
            ConfigureEnemy(env, other, 0, 1);
            AddFreeze(env, env.Enemy, "Extra");
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            var pool = new EnemyTurnActionPool(new[] { env.Enemy, other });
            Assert.That(pool.TryTakeNext(out var actor), Is.True);
            Assert.That(actor, Is.SameAs(other));
            Assert.That(pool.LastSelectionIsLastGuaranteedAction, Is.False);
            Assert.That(random.IntCallCount, Is.Zero);
        }

        [Test]
        public void ExtraFreeze_NoEligibleRemainderEndsWithoutSpendingOrDrawing()
        {
            using var env = new BattleFlowTestEnv();
            env.CreateMinimalBattle();
            ConfigureEnemy(env, env.Enemy, 1, 1);
            var other = env.CreateEnemy(2);
            ConfigureEnemy(env, other, 0, 1);
            AddFreeze(env, env.Enemy, "Extra"); AddFreeze(env, other, "Extra");
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            var pool = new EnemyTurnActionPool(new[] { env.Enemy, other });
            Assert.That(pool.TryTakeNext(out _), Is.False);
            Assert.That(pool.RemainingActions, Is.EqualTo(1));
            Assert.That(random.IntCallCount, Is.Zero);
        }

        [Test]
        public void ExtraFreeze_RemovalRestoresRemainderEligibility_AndAllTakesPrecedence()
        {
            using var env = new BattleFlowTestEnv();
            env.CreateMinimalBattle();
            ConfigureEnemy(env, env.Enemy, 1, 1);
            var other = env.CreateEnemy(2); ConfigureEnemy(env, other, 0, 1);
            var extra = AddFreeze(env, env.Enemy, "Extra");
            AddFreeze(env, other, "Extra");
            var all = AddFreeze(env, env.Enemy, "All");
            Assert.That(env.Enemy.CanReceiveOrdinaryAction, Is.False);
            env.Enemy.Entity.RemoveStatusInstance(all);
            Assert.That(env.Enemy.CanReceiveOrdinaryAction, Is.True);
            Assert.That(env.Enemy.CanReceiveExtraAction, Is.False);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            var pool = new EnemyTurnActionPool(new[] { env.Enemy, other });
            Assert.That(pool.TryTakeNext(out _), Is.False);
            env.Enemy.Entity.RemoveStatusInstance(extra);
            Assert.That(pool.TryTakeNext(out var actor), Is.True);
            Assert.That(actor, Is.SameAs(env.Enemy));
            Assert.That(pool.LastSelectionIsLastGuaranteedAction, Is.False);
            Assert.That(random.IntCallCount, Is.Zero);
        }

        [TestCase("All")]
        [TestCase("Extra")]
        public void EnemyChangeControl_AcceptsBothScopesWithoutChangingCharacterRules(string scope)
        {
            Assert.DoesNotThrow(() => TableValidator.ValidateChangeControl("EnemyStatus", 3,
                scope, "Freeze", null, 0, true, true));
            if (scope == "Extra")
                Assert.Throws<TableValidationException>(() => TableValidator.ValidateChangeControl("CharacterStatus", 3,
                    scope, "Freeze", null, 0, true, false));
        }

        private static StatusInstance AddFreeze(BattleFlowTestEnv env, EnemyBattleController enemy, string scope)
        {
            string id = "ST_PoolFreeze" + enemy.Entity.SlotPosition + scope;
            env.ConfigureStatus(id);
            var main = env.DataManager.StatusMainDict[id];
            main.StatusID = 4998000 + enemy.Entity.SlotPosition * 2 + (scope == "All" ? 0 : 1);
            env.DataManager.StatusEffectDict[id + "Effect"] = new StatusEffectData {
                StatusEffectID = main.StatusID * 100 + 1, StatusEffectID2 = id + "Effect",
                EffectType = "ChangeControl", Param1 = scope, Param2 = "Freeze"
            };
            return enemy.Entity.AddStatus(id, enemy.Entity, 1, 5, 0, main);
        }

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

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void EligibilityRestored_ControlRejoinsNextRound_OffFieldWaitsForNextPool(bool removedDuringTurn, bool offField)
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            ConfigureEnemy(env, env.Enemy, 2, 10);
            var other = env.CreateEnemy(2); ConfigureEnemy(env, other, 4, 1);
            StatusInstance Block() => offField ? OffFieldTests.Leave(env, env.Enemy.Entity) : AddAirborne(env, env.Enemy);
            StatusInstance blocked = removedDuringTurn ? null : Block();
            var roster = new[] { env.Enemy, other };
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            var pool = new EnemyTurnActionPool(roster);
            Assert.That(pool.RemainingActions, Is.EqualTo(removedDuringTurn || offField ? 6 : 4));
            if (removedDuringTurn)
            {
                Assert.That(pool.TryTakeNext(out var first), Is.True);
                Assert.That(first, Is.SameAs(env.Enemy));
                blocked = Block();
            }
            Assert.That(pool.TryTakeNext(out var next), Is.True);
            Assert.That(next, Is.SameAs(other));
            long remaining = offField ? (removedDuringTurn ? 4 : 5) : (removedDuringTurn ? 2 : 3);
            Assert.That(pool.RemainingActions, Is.EqualTo(remaining));
            env.Enemy.Entity.RemoveStatusInstance(blocked);
            Assert.That(env.Enemy.CanAct, Is.True);
            var rest = Drain(pool);
            Assert.That(rest.Count, Is.EqualTo(remaining));
            if (offField)
                foreach (var actor in rest) Assert.That(actor, Is.SameAs(other));
            else
                CollectionAssert.AreEqual(removedDuringTurn ? new[] { env.Enemy, other }
                    : new[] { env.Enemy, other, env.Enemy }, rest);
            Assert.That(pool.RemainingActions, Is.Zero);
            var rebuilt = new EnemyTurnActionPool(roster);
            Assert.That(rebuilt.RemainingActions, Is.EqualTo(6));
            CollectionAssert.AreEqual(new[] { env.Enemy, other, env.Enemy, other, env.Enemy, other }, Drain(rebuilt));
            Assert.That(random.IntCallCount, Is.Zero); Assert.That(random.FloatCallCount, Is.Zero);
        }

        [Test]
        public void NewlyRegisteredEnemy_DoesNotExpandExistingPool()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            ConfigureEnemy(env, env.Enemy, 2, 1);
            var roster = new List<EnemyBattleController> { env.Enemy };
            var pool = new EnemyTurnActionPool(roster);
            var added = env.CreateEnemy(2); ConfigureEnemy(env, added, 3, 10); roster.Add(added);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            Assert.That(pool.RemainingActions, Is.EqualTo(2));
            CollectionAssert.AreEqual(new[] { env.Enemy, env.Enemy }, Drain(pool));
            var rebuilt = new EnemyTurnActionPool(roster);
            Assert.That(rebuilt.RemainingActions, Is.EqualTo(5));
            Assert.That(rebuilt.TryTakeNext(out var first), Is.True); Assert.That(first, Is.SameAs(added));
            Assert.That(random.IntCallCount, Is.Zero); Assert.That(random.FloatCallCount, Is.Zero);
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

        [Test]
        public void WakeAndAddDuringRound_WaitsForNextRound_WithoutRestoringContribution()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            ConfigureEnemy(env, env.Enemy, 1, 3);
            var other = env.CreateEnemy(2); ConfigureEnemy(env, other, 1, 2);
            var sleeper = env.CreateEnemy(3); ConfigureEnemy(env, sleeper, 5, 10);
            var stun = AddAirborne(env, sleeper);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            var pool = new EnemyTurnActionPool(new[] { env.Enemy, other, sleeper });
            Assert.That(pool.RemainingActions, Is.EqualTo(2));
            Assert.That(pool.TryTakeNext(out var first), Is.True); Assert.That(first, Is.SameAs(env.Enemy));
            sleeper.Entity.RemoveStatusInstance(stun);
            pool.QueueActionChange(1);
            Assert.That(pool.RemainingActions, Is.EqualTo(1), "变动尚未结算，唤醒不返还原贡献");
            Assert.That(pool.PendingActionDelta, Is.EqualTo(1));
            CollectionAssert.AreEqual(new[] { other, sleeper }, Drain(pool));
            Assert.That(random.IntCallCount, Is.Zero);
        }

        [Test]
        public void AddedBudget_DoesNotAppendUnselectedUnitToPartialRound()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            ConfigureEnemy(env, env.Enemy, 1, 3);
            var other = env.CreateEnemy(2); ConfigureEnemy(env, other, 1, 2);
            var low = env.CreateEnemy(3); ConfigureEnemy(env, low, 0, 1);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            var pool = new EnemyTurnActionPool(new[] { env.Enemy, other, low });
            pool.TryTakeNext(out var first); Assert.That(first, Is.SameAs(env.Enemy));
            pool.QueueActionChange(1);
            CollectionAssert.AreEqual(new[] { other, env.Enemy }, Drain(pool), "新增1次进入下一轮，不补给本轮未入选者");
            Assert.That(random.IntCallCount, Is.Zero);
        }

        [Test]
        public void WakeWithoutAddingBudget_RestoresEligibilityOnly()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            ConfigureEnemy(env, env.Enemy, 2, 3);
            var other = env.CreateEnemy(2); ConfigureEnemy(env, other, 1, 2);
            var sleeper = env.CreateEnemy(3); ConfigureEnemy(env, sleeper, 5, 10);
            var stun = AddAirborne(env, sleeper);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            var pool = new EnemyTurnActionPool(new[] { env.Enemy, other, sleeper });
            pool.TryTakeNext(out _); sleeper.Entity.RemoveStatusInstance(stun);
            Assert.That(pool.RemainingActions, Is.EqualTo(2));
            CollectionAssert.AreEqual(new[] { other, sleeper }, Drain(pool));
            Assert.That(random.IntCallCount, Is.Zero);
        }

        [Test]
        public void RoundRosterAndTieOrder_AreFixedBeforeFirstAction()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            ConfigureEnemy(env, env.Enemy, 3, 10);
            var a = env.CreateEnemy(2); ConfigureEnemy(env, a, 0, 1);
            var b = env.CreateEnemy(3); ConfigureEnemy(env, b, 0, 1);
            var random = new SequenceBattleRandomSource(intValues: new[] { 1 }); BattleRandom.SetSource(random);
            var pool = new EnemyTurnActionPool(new[] { env.Enemy, a, b });
            pool.TryTakeNext(out var first); Assert.That(first, Is.SameAs(env.Enemy));
            Assert.That(random.IntCallCount, Is.EqualTo(1), "低威胁组的顺序也在首个技能前确定");
            a.Entity.Threat = 100;
            CollectionAssert.AreEqual(new[] { b, a }, Drain(pool));
            Assert.That(random.IntCallCount, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ContributionDeduction_IsOnceOnly_AndDoesNotLeaveDebt(bool deadAfterWake)
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            ConfigureEnemy(env, env.Enemy, 3, 10);
            var other = env.CreateEnemy(2); ConfigureEnemy(env, other, 0, 1);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            var pool = new EnemyTurnActionPool(new[] { env.Enemy, other });
            pool.TryTakeNext(out _);
            var stun = AddAirborne(env, env.Enemy);
            Assert.That(pool.TryTakeNext(out _), Is.False);
            Assert.That(pool.RemainingActions, Is.Zero);
            env.Enemy.Entity.RemoveStatusInstance(stun);
            pool.QueueActionChange(2);
            if (deadAfterWake) env.Enemy.Entity.CurrentHP = 0;
            else AddAirborne(env, env.Enemy);
            CollectionAssert.AreEqual(new[] { other, other }, Drain(pool));
            Assert.That(random.IntCallCount, Is.Zero);
        }

        [Test]
        public void AddedBudget_WithNoEligibleUnits_StopsWithoutSpendingOrRandom()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            AddAirborne(env, env.Enemy);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            var pool = new EnemyTurnActionPool(new[] { env.Enemy });
            pool.QueueActionChange(2);
            Assert.That(pool.TryTakeNext(out _), Is.False);
            Assert.That(pool.RemainingActions, Is.EqualTo(2));
            pool.QueueActionChange(0);
            pool.QueueActionChange(-1);
            Assert.That(pool.RemainingActions, Is.EqualTo(2));
            Assert.That(pool.PendingActionDelta, Is.EqualTo(-1));
            Assert.That(random.IntCallCount, Is.Zero);
        }

        [Test]
        [Timeout(6000)]
        public void RealTurn_AddedSharedActionExecutes_AndInterfaceClosesAfterPhase()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureEnemyDamageSkill();
            env.DataManager.EnemySkillMainDict[990201].UsePerTurn = 0;
            env.DataManager.EnemyMainDict[BattleFlowTestEnv.EnemyID].ActsGiven = 1;
            env.CreateMinimalBattle();
            Assert.That(env.Manager.TryGrantEnemyActions(1), Is.False);
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            int completed = 0;
            using var listener = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted, (_, fact) =>
            {
                if (fact.Subject != env.Enemy.Entity || ++completed != 1) return;
                Assert.That(env.Manager.TryGrantEnemyActions(0), Is.True);
                Assert.That(env.Manager.TryGrantEnemyActions(1), Is.True);
            }, 0);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPostTurn);
            Assert.That(completed, Is.EqualTo(2));
            Assert.That(GetUsed(env.Enemy), Is.EqualTo(2));
            Assert.That(random.IntCallCount, Is.EqualTo(2));
            Assert.That(env.Manager.TryGrantEnemyActions(1), Is.False);
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPostTurn);
            Assert.That(GetUsed(env.Enemy), Is.EqualTo(1), "额外次数不能泄漏到下回合");
            Assert.That(random.IntCallCount, Is.EqualTo(3));
        }

        [TestCase(-10)]
        [TestCase(-1)]
        public void NegativeGrant_DoesNotCancelAlreadyAllocatedRound(int delta)
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            ConfigureEnemy(env, env.Enemy, 2, 3);
            var b = env.CreateEnemy(2); ConfigureEnemy(env, b, 1, 2);
            var c = env.CreateEnemy(3); ConfigureEnemy(env, c, 1, 1);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            var pool = new EnemyTurnActionPool(new[] { env.Enemy, b, c });
            pool.TryTakeNext(out _);
            pool.QueueActionChange(delta);
            Assert.That(pool.RemainingActions, Is.EqualTo(3));
            Assert.That(pool.PendingActionDelta, Is.EqualTo(delta));
            CollectionAssert.AreEqual(new[] { b, c }, Drain(pool));
            Assert.That(pool.RemainingActions, Is.Zero);
            Assert.That(pool.PendingActionDelta, Is.Zero);
            Assert.That(random.IntCallCount, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SignedGrants_AreSummedBeforeClamping_AndAppliedOnce(bool reverse)
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            ConfigureEnemy(env, env.Enemy, 1, 1);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            var pool = new EnemyTurnActionPool(new[] { env.Enemy });
            pool.TryTakeNext(out _);
            pool.QueueActionChange(reverse ? 4 : -3);
            pool.QueueActionChange(reverse ? -3 : 4);
            Assert.That(pool.RemainingActions, Is.Zero);
            Assert.That(pool.PendingActionDelta, Is.EqualTo(1));
            CollectionAssert.AreEqual(new[] { env.Enemy }, Drain(pool));
            Assert.That(pool.PendingActionDelta, Is.Zero);
            Assert.That(pool.TryTakeNext(out _), Is.False);
            Assert.That(random.IntCallCount, Is.Zero);
        }

        [Test]
        public void OversizedDeduction_DoesNotLeaveDebtForLaterGrant()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            ConfigureEnemy(env, env.Enemy, 1, 1);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            var pool = new EnemyTurnActionPool(new[] { env.Enemy });
            pool.TryTakeNext(out _); pool.QueueActionChange(-100);
            Assert.That(pool.TryTakeNext(out _), Is.False);
            Assert.That(pool.RemainingActions, Is.Zero);
            pool.QueueActionChange(1);
            CollectionAssert.AreEqual(new[] { env.Enemy }, Drain(pool));
            Assert.That(random.IntCallCount, Is.Zero);
        }

        [TestCase("+2")]
        [TestCase("-2")]
        [TestCase("0")]
        public void GrantAction_ValidatesSignedIntegerAndEnemyOnly(string delta)
        {
            Assert.DoesNotThrow(() => TableValidator.ValidateGrantAction("EnemySkill", 2,
                delta, null, null, null, null, null, 0, 0, 0));
            Assert.DoesNotThrow(() => TableValidator.ValidateEffectRow("EnemySkill", 2,
                "GrantAction", null, 0, null, null, 0, 0, null, isEnemy: true));
            Assert.Throws<TableValidationException>(() => TableValidator.ValidateEffectRow("CharacterSkill", 2,
                "GrantAction", null, 0, null, null, 0, 0, null, isEnemy: false));
        }

        [TestCase("")]
        [TestCase("1.5")]
        [TestCase("ATK")]
        public void GrantAction_RejectsInvalidDelta(string delta)
            => Assert.Throws<TableValidationException>(() => TableValidator.ValidateGrantAction("EnemySkill", 2,
                delta, null, null, null, null, null, 0, 0, 0));

        [TestCase(1, 1, 2)]
        [TestCase(-2, 4, 2)]
        [Timeout(6000)]
        public void RealTurn_GrantActionEffect_ExecutesThroughEnemySkill(int delta, int initial, int expected)
        {
            using var env = new BattleFlowTestEnv(); env.ConfigureEnemyDamageSkill(.1f);
            env.DataManager.EnemySkillMainDict[990201].UsePerTurn = 0;
            env.DataManager.EnemyMainDict[BattleFlowTestEnv.EnemyID].ActsGiven = initial;
            var grant = new EnemySkillEffectData {
                SkillEffectID = 99020102, SkillEffectID2 = "SE_TestGrantAction", EffectIndex = 1,
                EffectType = "GrantAction", Param1 = delta.ToString()
            };
            env.DataManager.EnemySkillEffectDict[grant.SkillEffectID2] = grant;
            env.CreateMinimalBattle(); env.Manager.StartBattle();
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            // 只首个技能修改池；后续效果填0，避免正向自增技能无限延长回合。
            using var listener = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted,
                (_, fact) => { if (fact.Subject == env.Enemy.Entity) grant.Param1 = "0"; }, 0);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPostTurn);
            Assert.That(GetUsed(env.Enemy), Is.EqualTo(expected));
            Assert.That(random.IntCallCount, Is.EqualTo(expected));
            Assert.That(env.Manager.TryGrantEnemyActions(-1), Is.False);
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
