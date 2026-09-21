using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public class ReactionPipelineTests
    {
        private GameObject _sourceObject;
        private GameObject _targetObject;
        private BattleEntity _source;
        private BattleEntity _target;

        [SetUp]
        public void SetUp()
        {
            ReactionResolver.ResetSession();
            BattleEntity._applyOrderCounter = 0;
            _sourceObject = new GameObject("Reaction_Source");
            _targetObject = new GameObject("Reaction_Target");
            _source = _sourceObject.AddComponent<BattleEntity>();
            _target = _targetObject.AddComponent<BattleEntity>();
            _source.EntityID = 1009;
            _source.Level = 80;
            _source.TotalEM = 240f;
            _target.EntityID = 20001;
        }

        [TearDown]
        public void TearDown()
        {
            ReactionResolver.ResetSession();
            BattleEntity._applyOrderCounter = 0;
            Object.DestroyImmediate(_sourceObject);
            Object.DestroyImmediate(_targetObject);
        }

        [TestCase(ReactionSourceKind.CharacterSkill)]
        [TestCase(ReactionSourceKind.StatusEffect)]
        [TestCase(ReactionSourceKind.EnemySkill)]
        public void Resolve_ZeroElementAmount_NeverReacts(ReactionSourceKind sourceKind)
        {
            _target.ApplyAura("Cryo", 2f, 1010);

            ReactionResult result = ReactionResolver.Resolve(NewContext(sourceKind, 0f));

            Assert.That(result.HasReaction, Is.False);
            Assert.That(result.FinalDamage, Is.EqualTo(100f));
            Assert.That(result.RemainingAttackAmount, Is.Zero);
            Assert.That(_target.GetAura("Cryo").AuraAmount, Is.EqualTo(2f));
        }

        [Test]
        public void Resolve_NonZeroElementAmount_UsesSharedReactionPath()
        {
            _target.ApplyAura("Cryo", 2f, 1010);

            ReactionResult result = ReactionResolver.Resolve(NewContext(ReactionSourceKind.CharacterSkill, 1f));

            Assert.That(result.HasReaction, Is.True);
            Assert.That(result.TriggeredReactions[0].Type, Is.EqualTo(ReactionType.Melt));
            float expected = ReactionDamageCalculator.CalculateCharacterAmplifying(100f, 2f, _source.TotalEM);
            Assert.That(result.FinalDamage, Is.EqualTo(expected).Within(0.001f));
            Assert.That(result.RemainingAttackAmount, Is.Zero);
        }

        [Test]
        public void Resolve_ThirdElementAgainstElectroCharged_SplitsAmountAndReturnsBothReactions()
        {
            _target.ApplyAura("Hydro", 2f, 1010);
            _target.ApplyAura("Electro", 2f, 1010);
            ReactionStateSystem.SetEntityState(
                _target,
                ReactionType.ElectroCharged,
                1,
                new ReactionSourceSnapshot(),
                -1,
                false,
                2f);

            ReactionResult result = ReactionResolver.Resolve(
                NewContext(ReactionSourceKind.CharacterSkill, 2f));

            Assert.That(result.TriggeredReactions.Exists(x => x.Type == ReactionType.Vaporize), Is.True);
            Assert.That(result.TriggeredReactions.Exists(x => x.Type == ReactionType.Overloaded), Is.True);
            Assert.That(result.PrimaryDamageReactionType, Is.EqualTo(ReactionType.Vaporize));
            Assert.That(result.RemainingAttackAmount, Is.Zero);
            Assert.That(_target.GetAura("Hydro").AuraAmount, Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(_target.GetAura("Electro").AuraAmount, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void Resolve_BurningTarget_ConsumesExcessDendroBeforeBurningElement()
        {
            _target.ApplyAura("Dendro", 0.5f, 1010);
            ReactionStateSystem.SetEntityState(
                _target,
                ReactionType.Burning,
                1,
                new ReactionSourceSnapshot(),
                -1,
                true,
                1f);
            ReactionContext context = NewContext(ReactionSourceKind.CharacterSkill, 2f);
            context.AttackElement = "Hydro";

            ReactionResult result = ReactionResolver.Resolve(context);

            Assert.That(result.TriggeredReactions[0].Type, Is.EqualTo(ReactionType.Bloom));
            Assert.That(result.TriggeredReactions[1].Type, Is.EqualTo(ReactionType.Vaporize));
            Assert.That(result.PrimaryDamageReactionType, Is.EqualTo(ReactionType.Vaporize));
            Assert.That(_target.GetAura("Dendro"), Is.Null);
            Assert.That(BurningReactionHandler.IsBurning(_target), Is.False);
            Assert.That(result.RemainingAttackAmount, Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void Resolve_FrozenTarget_ConsumesNormalCryoBeforeFrozenElement()
        {
            _target.ApplyAura("Cryo", 0.5f, 1010);
            ReactionStateSystem.SetEntityState(
                _target,
                ReactionType.Frozen,
                1,
                new ReactionSourceSnapshot(),
                -1,
                true,
                0.5f);

            ReactionResult result = ReactionResolver.Resolve(
                NewContext(ReactionSourceKind.CharacterSkill, 1f));

            Assert.That(result.TriggeredReactions.FindAll(x => x.Type == ReactionType.Melt), Has.Count.EqualTo(2));
            Assert.That(result.PrimaryDamageReactionType, Is.EqualTo(ReactionType.Melt));
            Assert.That(_target.GetAura("Cryo"), Is.Null);
            Assert.That(FrozenReactionHandler.IsFrozen(_target), Is.False);
            Assert.That(result.RemainingAttackAmount, Is.EqualTo(0.5f).Within(0.001f));
        }

        [Test]
        public void SetEntityState_SameType_ReplacesStateAndSnapshot()
        {
            var first = ReactionSourceSnapshot.Capture(NewContext(ReactionSourceKind.CharacterSkill, 1f));
            ReactionStateSystem.SetEntityState(_target, ReactionType.Burning, 2, first);

            _source.TotalEM = 500f;
            var second = ReactionSourceSnapshot.Capture(NewContext(ReactionSourceKind.StatusEffect, 1f));
            ReactionStateSystem.SetEntityState(_target, ReactionType.Burning, 4, second);

            Assert.That(ReactionStateSystem.ActiveStates.Count, Is.EqualTo(1));
            Assert.That(ReactionStateSystem.TryGetEntityState(_target, ReactionType.Burning, out var state), Is.True);
            Assert.That(state.RemainingRounds, Is.EqualTo(4));
            Assert.That(state.SourceSnapshot.TotalEM, Is.EqualTo(500f));
            Assert.That(state.SourceSnapshot.SourceEffectID, Is.EqualTo("SE_Test"));
        }

        private ReactionContext NewContext(ReactionSourceKind sourceKind, float amount)
        {
            return new ReactionContext
            {
                SourceEntity = _source,
                Target = _target,
                SourceKind = sourceKind,
                SourceSkillID = "SK_Test",
                SourceEffectID = "SE_Test",
                EffectExecutionID = ReactionResolver.BeginEffectExecution(),
                AttackElement = "Pyro",
                AttackAmount = amount,
                PreReactionDamage = 100f,
                DamageType = "Skill",
                PoiseDamage = 20f
            };
        }
    }
}
