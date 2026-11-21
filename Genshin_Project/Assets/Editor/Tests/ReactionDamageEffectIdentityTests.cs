using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public class ReactionDamageEffectIdentityTests
    {
        [Test]
        public void IndependentExecution_GetsNewIdentity_RestoresCaller_AndPreservesChainLimits()
        {
            using var env = new BattleFlowTestEnv();
            long parent = ReactionResolver.BeginEffectExecution();
            using (BattleResolutionContext.BeginEffect())
            using (BattleEventHub.BeginEffectExecution(parent))
            {
                var counters = BattleResolutionContext.CurrentEffect;
                using (var child = ReactionEffectExecutor.BeginDamageEffect())
                {
                    Assert.That(child.ExecutionID, Is.Positive.And.Not.EqualTo(parent));
                    Assert.That(BattleEventHub.CurrentEffectExecutionId, Is.EqualTo(child.ExecutionID));
                    Assert.That(BattleResolutionContext.CurrentEffect, Is.SameAs(counters));
                }
                Assert.That(BattleEventHub.CurrentEffectExecutionId, Is.EqualTo(parent));
                using (var derived = ReactionEffectExecutor.BeginDamageEffect(parent))
                    Assert.That(derived.ExecutionID, Is.EqualTo(parent));
            }
            Assert.That(BattleEventHub.CurrentEffectExecutionId, Is.Zero);
            Assert.That(BattleResolutionContext.CurrentEffect, Is.Null);
        }

        [UnityTest]
        public IEnumerator StandaloneReactionBatch_HasOneRealEffectForAllTargets_AndNextCallGetsNewIdentity()
            => HookTestEnv.AwaitTask(StandaloneAsync);

        private async Task StandaloneAsync()
        {
            using var env = new BattleFlowTestEnv();
            env.CreateMinimalBattle();
            var other = env.CreateEnemy(2).Entity;
            var facts = new List<BattleEventHeader>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,
                (header, damage) => {
                    if (damage.Source.SourceEffectID == "SE_TestReactionBatch") facts.Add(header);
                }, 0);
            async Task Execute()
            {
                await BattleExecution.Run(async () => {
                    var result = new ReactionResult();
                    foreach (var target in new[] { env.Enemy.Entity, other })
                        result.DerivedHits.Add(new ReactionDerivedHit {
                            Target = target, Damage = 10f, DamageElement = "Pyro", ReactionType = ReactionType.Overloaded,
                            Source = DamageSourceInfo.Create(env.Ally.Entity, ReactionSourceKind.DerivedReaction,
                                "SK_TestReactionOrigin", "SE_TestReactionBatch", ReactionType.Overloaded, false) });
                    ReactionEffectExecutor.ExecuteDerivedHits(result);
                    await BattleExecution.Flush();
                });
            }
            await Execute();
            await Execute();
            Assert.That(facts, Has.Count.EqualTo(4));
            Assert.That(facts.All(f => f.EffectExecutionId > 0 && f.EffectScopeId > 0), Is.True);
            Assert.That(facts[0].EffectExecutionId, Is.EqualTo(facts[1].EffectExecutionId));
            Assert.That(facts[2].EffectExecutionId, Is.EqualTo(facts[3].EffectExecutionId));
            Assert.That(facts[0].EffectExecutionId, Is.Not.EqualTo(facts[2].EffectExecutionId));
            Assert.That(facts[0].EffectScopeId, Is.Not.EqualTo(facts[2].EffectScopeId));
            Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(980f));
            Assert.That(other.CurrentHP, Is.EqualTo(980f));
        }

        [UnityTest]
        public IEnumerator BurningAndConductTicks_UseFreshEffectIdentities_AndRetainOriginalSource()
            => HookTestEnv.AwaitTask(TicksAsync);

        private async Task TicksAsync()
        {
            using var env = new BattleFlowTestEnv();
            env.CreateMinimalBattle();
            var conduct = env.Enemy.Entity;
            var burning = env.CreateEnemy(2).Entity;
            ReactionContext Context(BattleEntity target, string element, float amount) => new ReactionContext {
                SourceEntity = env.Ally.Entity, Target = target, SourceKind = ReactionSourceKind.CharacterSkill,
                SourceSkillID = "SK_TestReactionOrigin", SourceEffectID = "SE_TestReactionOrigin",
                AttackElement = element, AttackAmount = amount, ApplicationPhase = 2 };
            conduct.ApplyAura("Electro", 3f, 2);
            Assert.That(ElectroChargedReactionHandler.TryResolve(Context(conduct, "Hydro", 3f), 17.2f, out var immediate), Is.True);
            conduct.ApplyAura("Hydro", immediate.RemainingAttackAmount, 2);
            burning.ApplyAura("Dendro", 2f, 2);
            Assert.That(BurningReactionHandler.TryResolve(Context(burning, "Pyro", 2f), 17.2f, out _), Is.True);
            var facts = new List<BattleEventHeader>();
            var reactions = new List<ReactionType>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,
                (header, damage) => {
                    if (damage.Source.SourceEffectID != "SE_TestReactionOrigin") return;
                    facts.Add(header); reactions.Add(damage.Source.ReactionType);
                }, 0);
            long parent = ReactionResolver.BeginEffectExecution();
            using (BattleEventHub.BeginEffectExecution(parent))
                await BattleExecution.Run(async () => {
                    foreach (var phase in new[] { TurnPhase.AllyPostTurn, TurnPhase.EnemyPostTurn })
                    {
                        ElectroChargedReactionHandler.TickTurnEnd(phase);
                        await BattleExecution.Flush();
                        BurningReactionHandler.TickTurnEnd(phase);
                        await BattleExecution.Flush();
                    }
                });
            Assert.That(reactions.Count(r => r == ReactionType.ElectroCharged), Is.EqualTo(2));
            Assert.That(reactions.Count(r => r == ReactionType.Burning), Is.EqualTo(2));
            Assert.That(facts.Select(f => f.EffectExecutionId).Distinct().Count(), Is.EqualTo(4));
            Assert.That(facts.All(f => f.EffectExecutionId > 0 && f.EffectExecutionId != parent), Is.True);
        }
    }
}
