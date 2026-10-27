using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public class BattleSpecialRulesTests
    {
        [Test]
        public void Registry_DistinguishesRegisteredFalseAndEmptyResultsFromUnknownHandlers()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var random = new SequenceBattleRandomSource();
                BattleRandom.SetSource(random);
                Assert.That(BattleSpecialRules.TryEvaluateCondition("1011_T1", "", null, out bool result), Is.True);
                Assert.That(result, Is.False);
                Assert.That(BattleSpecialRules.TryEvaluateCondition("Unknown", "", null, out _), Is.False);
                Assert.That(BattleSpecialRules.TryResolveTargets(LisaScriptHooks.TargetHook,
                    env.Manager, env.Ally.Entity, out var positions), Is.True);
                Assert.That(positions, Is.Empty);
                Assert.That(BattleSpecialRules.TryResolveTargets("ST_Ordinary",
                    env.Manager, env.Ally.Entity, out _), Is.False);
                Assert.That(ExecuteCountEvaluator.IsSupportedHandler("1011_C1()"), Is.True);
                Assert.That(ExecuteCountEvaluator.IsSupportedHandler("1011_C1(2)"), Is.False);
                Assert.That(ExecuteCountEvaluator.IsSupportedHandler("Unknown()"), Is.False);
                Assert.That(random.IntCallCount, Is.Zero);
                Assert.That(random.FloatCallCount, Is.Zero);
            }
        }

        [UnityTest]
        public IEnumerator RegisteredLisaScope_SurvivesAwaitAndRestoresCallerAfterNestedException()
            => HookTestEnv.AwaitTask(RegisteredLisaScope_SurvivesAwaitAndRestoresCallerAfterNestedExceptionAsync);

        private async Task RegisteredLisaScope_SurvivesAwaitAndRestoresCallerAfterNestedExceptionAsync()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                env.Ally.Init(BattleFlowTestEnv.AllyID, 20, 1);
                var lisa = env.Ally.Entity;
                lisa.EntityID = 1011;
                lisa.ConstellationLevel = 1;
                lisa.Level = 20;
                env.DataManager.TalentDict[1011] = new List<TalentData>
                {
                    new TalentData { TalentIndex = 1, UnlockAfter = 20 }
                };
                env.DataManager.SkillLevelDict["1011_2_ST_Lisa_Conductive"] = new Dictionary<int, SkillLevelData>
                {
                    [1] = new SkillLevelData { Hits1 = "0.48" }
                };
                env.Enemy.Entity.StatusDict[LisaScriptHooks.ConductiveID] = new StatusInstance
                {
                    StatusID2 = LisaScriptHooks.ConductiveID, IsActive = true, StackCount = 3
                };
                var random = new SequenceBattleRandomSource();
                BattleRandom.SetSource(random);
                using (BattleSpecialRules.BeginSkill(lisa))
                {
                    BattleSpecialRules.ValidateDamageHook(LisaScriptHooks.DamageHook, lisa, "Damage");
                    var hit = BattleSpecialRules.ModifyHit(LisaScriptHooks.DamageHook, lisa,
                        env.Enemy.Entity, new HitData(3.2f, 2f), 1);
                    Assert.That(hit.Multiplier, Is.EqualTo(4.76f).Within(0.00001f));
                    Assert.That(hit.ElementAura, Is.EqualTo(2f));
                    await Task.Yield();
                    Assert.That(ExecuteCountEvaluator.Evaluate(1, "1011_C1()", lisa), Is.EqualTo(1));
                    Assert.That(ScriptHookEvaluator.Evaluate("1011_T1()", lisa), Is.True);

                    Assert.Throws<InvalidOperationException>(() =>
                    {
                        using (BattleSpecialRules.BeginSkill(env.Enemy.Entity))
                        {
                            Assert.That(ExecuteCountEvaluator.Evaluate(1, "1011_C1()", lisa), Is.Zero);
                            Assert.That(ScriptHookEvaluator.Evaluate("1011_T1()", lisa), Is.False);
                            throw new InvalidOperationException("nested unwind");
                        }
                    });
                    Assert.That(ExecuteCountEvaluator.Evaluate(1, "1011_C1()", lisa), Is.EqualTo(1));
                    var targets = BattleTargetResolver.Resolve(env.Manager, lisa, lisa, "Enemy", 0, 0,
                        LisaScriptHooks.TargetHook, null, null, null);
                    CollectionAssert.AreEqual(new[] { 1 }, targets.Positions);
                }
                Assert.That(ExecuteCountEvaluator.Evaluate(1, "1011_C1()", lisa), Is.Zero);
                Assert.That(ScriptHookEvaluator.Evaluate("1011_T1()", lisa), Is.False);
                Assert.That(random.IntCallCount, Is.Zero);
                Assert.That(random.FloatCallCount, Is.Zero);
            }
        }

        [Test]
        public void RegisteredKaeyaCondition_PreservesStrictHpBoundaryThroughPublicEvaluator()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var target = env.Ally.Entity;
                var hit = new DamageResolvedEvent
                {
                    Target = target, HitLanded = true, ActualHPDamage = 1f,
                    HPAfter = target.TotalHP / 5f
                };
                var context = new ScriptHookContext { Caster = target, DamageEvent = hit };
                Assert.That(ScriptHookEvaluator.Evaluate("Kaeya_C4()", context), Is.False);
                hit.HPAfter--;
                Assert.That(ScriptHookEvaluator.Evaluate("Kaeya_C4()", context), Is.True);
                hit.ActualHPDamage = 0;
                Assert.That(ScriptHookEvaluator.Evaluate("Kaeya_C4()", context), Is.False);
            }
        }

        [Test]
        public void DamageDispatch_PreservesValidationAndUnhookedHits()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var caster = env.Ally.Entity;
                var hit = BattleSpecialRules.ModifyHit(null, caster, env.Enemy.Entity,
                    new HitData(2f, 1f), 1);
                Assert.That(hit.Multiplier, Is.EqualTo(2f));
                Assert.Throws<NotSupportedException>(() =>
                    BattleSpecialRules.ValidateDamageHook("Unknown()", caster, "Damage"));
                Assert.Throws<NotSupportedException>(() =>
                    BattleSpecialRules.ValidateDamageHook(LisaScriptHooks.DamageHook, caster, "Damage"));
                caster.EntityID = 1011;
                Assert.Throws<NotSupportedException>(() =>
                    BattleSpecialRules.ValidateDamageHook(LisaScriptHooks.DamageHook, caster, "Heal"));
                Assert.DoesNotThrow(() =>
                    BattleSpecialRules.ValidateDamageHook(" " + LisaScriptHooks.DamageHook + " ", caster, "Damage"));
            }
        }

        [Test]
        public void EnemyControl_UsesRegisteredAirborneRule_WithoutMovingFreezeIntoRegistry()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var target = env.Enemy.Entity;
                var status = new StatusInstance { StatusID2 = AirborneStatusHandler.EnemyStatusID, IsActive = false };
                target.StatusDict[status.StatusID2] = status;
                Assert.That(env.Enemy.CanAct, Is.True);
                status.IsActive = true;
                Assert.That(BattleSpecialRules.BlocksEnemyAction(target), Is.True);
                Assert.That(env.Enemy.CanAct, Is.False);
                Assert.That(new EnemyTurnActionPool(new[] { env.Enemy }).RemainingActions, Is.Zero);
                Assert.That(target.GetStatus(status.StatusID2), Is.SameAs(status));
                target.StatusDict.Clear();
                ReactionStateSystem.SetEntityState(target, ReactionType.Frozen, 2, null, reactionElementAmount: 1f);
                Assert.That(BattleSpecialRules.BlocksEnemyAction(target), Is.False);
                Assert.That(env.Enemy.CanAct, Is.False);
                Assert.That(new EnemyTurnActionPool(new[] { env.Enemy }).RemainingActions, Is.Zero);
                ReactionStateSystem.RemoveEntityState(target, ReactionType.Frozen);
                Assert.That(env.Enemy.CanAct, Is.True);
            }
        }
    }
}
