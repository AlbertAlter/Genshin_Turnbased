using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public class BurningReactionHandlerTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private BattleEntity _source;
        private BattleEntity _target;

        [SetUp]
        public void SetUp()
        {
            ReactionResolver.ResetSession();
            NewObject("Burning_BattleManager").AddComponent<BattleManager>();
            ReactionStateSystem.SetDisplayAdapter(new StatusDataReactionStateDisplayAdapter());

            _source = NewEntity("Burning_Source", BattleSide.Ally, BattleEntity.EntityType.Character);
            _source.EntityID = 10001;
            _source.Level = 80;
            _source.TotalEM = 200f;
            _target = NewEntity("Burning_Target", BattleSide.Enemy, BattleEntity.EntityType.Enemy);
        }

        [TearDown]
        public void TearDown()
        {
            ReactionResolver.ResetSession();
            ReactionStateSystem.SetDisplayAdapter(null);
            for (int i = _objects.Count - 1; i >= 0; i--)
                Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
        }

        [TestCase("Pyro", "Dendro")]
        [TestCase("Dendro", "Pyro")]
        public void TryResolve_ConsumesOneToOneAndStoresExactBurningAmount(
            string attackElement,
            string auraElement)
        {
            _target.ApplyAura(auraElement, 1.5f, 2);

            bool triggered = BurningReactionHandler.TryResolve(
                NewContext(attackElement, 2f),
                100f,
                out BurningReactionResolution result);

            Assert.That(triggered, Is.True);
            Assert.That(result.RemainingAttackAmount, Is.EqualTo(0.5f));
            Assert.That(_target.GetAura(auraElement), Is.Null);
            Assert.That(ReactionStateSystem.TryGetEntityState(
                _target,
                ReactionType.Burning,
                out ReactionStateInstance state), Is.True);
            Assert.That(state.ReactionElementAmount, Is.EqualTo(1.5f));
            Assert.That(state.RemainingRounds, Is.EqualTo(2));
            Assert.That(state.SourceSnapshot.LevelCoefficient, Is.EqualTo(100f));
            Assert.That(_target.HasStatus("ST_Burning"), Is.True);
        }

        [Test]
        public void TryResolve_ReplacesSnapshotAndAddsNewBurningAmount()
        {
            _target.ApplyAura("Dendro", 1.5f, 2);
            BurningReactionHandler.TryResolve(
                NewContext("Pyro", 1.5f),
                100f,
                out _);
            ReactionStateSystem.TryGetEntityState(
                _target,
                ReactionType.Burning,
                out ReactionStateInstance original);

            _source.TotalEM = 600f;
            _target.ApplyAura("Dendro", 1f, 2);
            BurningReactionHandler.TryResolve(
                NewContext("Pyro", 1f),
                120f,
                out _);

            ReactionStateSystem.TryGetEntityState(
                _target,
                ReactionType.Burning,
                out ReactionStateInstance current);
            Assert.That(current, Is.Not.SameAs(original));
            Assert.That(current.ReactionElementAmount, Is.EqualTo(2.5f));
            Assert.That(current.RemainingRounds, Is.EqualTo(3));
            Assert.That(current.SourceSnapshot.TotalEM, Is.EqualTo(600f));
            Assert.That(current.SourceSnapshot.LevelCoefficient, Is.EqualTo(120f));
        }

        [Test]
        public void TickTurnEnd_DealsSnapshotDamageAndAppliesOnePyro()
        {
            _target.ApplyAura("Dendro", 1f, 2);
            BurningReactionHandler.TryResolve(
                NewContext("Pyro", 1f),
                100f,
                out _);

            BurningReactionHandler.TickTurnEnd(TurnPhase.AllyPostTurn);

            float expected = 100f * 2f * (1f + 16f * 200f / 2200f);
            Assert.That(_target.CurrentHP, Is.EqualTo(10000f - expected).Within(0.001f));
            Assert.That(_target.Poise, Is.EqualTo(500f));
            Assert.That(_target.GetAura("Pyro").AuraAmount, Is.EqualTo(1f));
            Assert.That(_target.GetAura("Pyro").SkipNextOriginDecay, Is.True);
        }

        [Test]
        public void TickTurnEnd_WeakPyroConsumesExcessDendroAndAddsBurningAmount()
        {
            _target.ApplyAura("Dendro", 2f, 2);
            BurningReactionHandler.TryResolve(
                NewContext("Pyro", 1f),
                100f,
                out _);

            BurningReactionHandler.TickTurnEnd(TurnPhase.AllyPostTurn);

            Assert.That(_target.GetAura("Dendro"), Is.Null);
            Assert.That(_target.GetAura("Pyro"), Is.Null);
            ReactionStateSystem.TryGetEntityState(
                _target,
                ReactionType.Burning,
                out ReactionStateInstance state);
            Assert.That(state.ReactionElementAmount, Is.EqualTo(2f));
            Assert.That(state.RemainingRounds, Is.EqualTo(2));
            Assert.That(state.SkipNextDurationTick, Is.True);
        }

        [Test]
        public void AmplifyingReaction_ConsumesNormalPyroBeforeBurningElement()
        {
            _target.ApplyAura("Dendro", 1.5f, 2);
            BurningReactionHandler.TryResolve(
                NewContext("Pyro", 1.5f),
                100f,
                out _);
            _target.ApplyAura("Pyro", 0.5f, 2);

            bool triggered = AmplifyingReactionHandler.TryResolve(
                NewContext("Cryo", 2f),
                out _);

            Assert.That(triggered, Is.True);
            Assert.That(_target.GetAura("Pyro"), Is.Null);
            Assert.That(BurningReactionHandler.GetBurningAuraAsPyro(_target),
                Is.EqualTo(1f));
        }

        [Test]
        public void BurningElement_CannotTriggerBurningAgain()
        {
            _target.ApplyAura("Dendro", 1f, 2);
            BurningReactionHandler.TryResolve(
                NewContext("Pyro", 1f),
                100f,
                out _);

            Assert.That(BurningReactionHandler.CanResolve(
                NewContext("Dendro", 1f)), Is.False);
        }

        [Test]
        public void TickTurnEnd_EnemyFormulaDoesNotUseElementalMasteryOrFlatBonus()
        {
            _source.Type = BattleEntity.EntityType.Enemy;
            _source.TotalEM = 999f;
            _source.DMGBonus = 0.2f;
            _source.BaseDMGBonusFlat = 999f;
            _target.ApplyAura("Dendro", 1f, 2);
            BurningReactionHandler.TryResolve(
                NewContext("Pyro", 1f),
                100f,
                out _);

            BurningReactionHandler.TickTurnEnd(TurnPhase.EnemyPostTurn);

            Assert.That(_target.CurrentHP, Is.EqualTo(9760f).Within(0.001f));
        }

        [Test]
        public void OverloadedReaction_CanConsumeBurningElement()
        {
            _target.ApplyAura("Dendro", 1f, 2);
            BurningReactionHandler.TryResolve(
                NewContext("Pyro", 1f),
                100f,
                out _);

            bool triggered = OverloadedReactionHandler.TryResolve(
                NewContext("Electro", 1f),
                100f,
                out _);

            Assert.That(triggered, Is.True);
            Assert.That(BurningReactionHandler.IsBurning(_target), Is.False);
            Assert.That(_target.HasStatus("ST_Burning"), Is.False);
        }

        private ReactionContext NewContext(string attackElement, float amount)
        {
            return new ReactionContext
            {
                SourceEntity = _source,
                Target = _target,
                SourceKind = _source.Type == BattleEntity.EntityType.Enemy
                    ? ReactionSourceKind.EnemySkill
                    : ReactionSourceKind.CharacterSkill,
                SourceEffectID = "SE_Burning_Test",
                EffectExecutionID = ReactionResolver.BeginEffectExecution(),
                ApplicationPhase = (int)TurnPhase.AllyAction,
                AttackElement = attackElement,
                AttackAmount = amount,
                PreReactionDamage = 100f,
                DamageType = "Skill"
            };
        }

        private BattleEntity NewEntity(
            string name,
            BattleSide side,
            BattleEntity.EntityType type)
        {
            BattleEntity entity = NewObject(name).AddComponent<BattleEntity>();
            entity.Side = side;
            entity.Type = type;
            entity.TotalHP = 10000f;
            entity.CurrentHP = 10000f;
            entity.MaxPoise = 500f;
            entity.Poise = 500f;
            return entity;
        }

        private GameObject NewObject(string name)
        {
            var gameObject = new GameObject(name);
            _objects.Add(gameObject);
            return gameObject;
        }
    }
}
