using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public class AmplifyingReactionHandlerTests
    {
        private GameObject _sourceObject;
        private GameObject _targetObject;
        private BattleEntity _source;
        private BattleEntity _target;

        [SetUp]
        public void SetUp()
        {
            ReactionResolver.ResetSession();
            _sourceObject = new GameObject("Amplifying_Source");
            _targetObject = new GameObject("Amplifying_Target");
            _source = _sourceObject.AddComponent<BattleEntity>();
            _target = _targetObject.AddComponent<BattleEntity>();
            _source.EntityID = 1009;
            _target.EntityID = 20001;
        }

        [TearDown]
        public void TearDown()
        {
            ReactionResolver.ResetSession();
            Object.DestroyImmediate(_sourceObject);
            Object.DestroyImmediate(_targetObject);
        }

        [TestCase("Pyro", "Hydro", 1.5f, ReactionType.Vaporize)]
        [TestCase("Hydro", "Pyro", 2f, ReactionType.Vaporize)]
        [TestCase("Pyro", "Cryo", 2f, ReactionType.Melt)]
        [TestCase("Cryo", "Pyro", 1.5f, ReactionType.Melt)]
        public void Resolve_UsesDirectionalMultiplier(
            string attackElement,
            string auraElement,
            float multiplier,
            ReactionType expectedType)
        {
            _source.TotalEM = 0f;
            _target.ApplyAura(auraElement, 4f, 1);

            ReactionResult result = Resolve(attackElement, 2f);

            Assert.That(result.TriggeredReactions[0].Type, Is.EqualTo(expectedType));
            Assert.That(result.FinalDamage, Is.EqualTo(100f * multiplier).Within(0.001f));
        }

        [Test]
        public void Resolve_CharacterAmplifyingDamageIncludesElementalMastery()
        {
            _source.TotalEM = 200f;
            _target.ApplyAura("Hydro", 1f, 1);

            ReactionResult result = Resolve("Pyro", 1f);
            float expected = 100f * 1.5f * (1f + 2.78f * 200f / 1600f);

            Assert.That(result.FinalDamage, Is.EqualTo(expected).Within(0.001f));
        }

        [Test]
        public void Resolve_EnemyAmplifyingDamageUsesEnemyFormula()
        {
            _source.Type = BattleEntity.EntityType.Enemy;
            _source.TotalEM = 999f;
            _target.ApplyAura("Pyro", 1f, 1);

            ReactionResult result = Resolve("Hydro", 1f, ReactionSourceKind.EnemySkill);

            Assert.That(result.FinalDamage, Is.EqualTo(200f).Within(0.001f));
        }

        [Test]
        public void Resolve_PyroOnHydro_ConsumesOneToTwoAndWritesAttackRemainder()
        {
            _target.ApplyAura("Hydro", 1f, 1);

            ReactionResult result = Resolve("Pyro", 4f);

            Assert.That(_target.GetAura("Hydro"), Is.Null);
            Assert.That(result.RemainingAttackAmount, Is.EqualTo(2f));
            Assert.That(_target.GetAura("Pyro").AuraAmount, Is.EqualTo(2f));
        }

        [Test]
        public void Resolve_HydroOnPyro_ConsumesOneToTwoAndLeavesTargetAura()
        {
            _target.ApplyAura("Pyro", 4f, 1);

            ReactionResult result = Resolve("Hydro", 1f);

            Assert.That(result.RemainingAttackAmount, Is.Zero);
            Assert.That(_target.GetAura("Pyro").AuraAmount, Is.EqualTo(2f));
            Assert.That(_target.GetAura("Hydro"), Is.Null);
        }

        [TestCase(ReactionSourceKind.CharacterSkill)]
        [TestCase(ReactionSourceKind.StatusEffect)]
        [TestCase(ReactionSourceKind.EnemySkill)]
        public void Resolve_AllDamageSourcesUseSharedAmplifyingHandler(ReactionSourceKind sourceKind)
        {
            _source.Type = sourceKind == ReactionSourceKind.EnemySkill
                ? BattleEntity.EntityType.Enemy
                : BattleEntity.EntityType.Character;
            _target.ApplyAura("Hydro", 1f, 1);

            ReactionResult result = Resolve("Pyro", 1f, sourceKind);

            Assert.That(result.TriggeredReactions[0].Type, Is.EqualTo(ReactionType.Vaporize));
        }

        [Test]
        public void Resolve_PyroCanMeltFrozenRuntimeAura()
        {
            ReactionStateSystem.SetEntityState(
                _target,
                ReactionType.Frozen,
                1,
                new ReactionSourceSnapshot(),
                -1,
                true,
                1f);

            ReactionResult result = Resolve("Pyro", 1f);

            Assert.That(result.TriggeredReactions[0].Type, Is.EqualTo(ReactionType.Melt));
            Assert.That(ReactionStateSystem.TryGetEntityState(_target, ReactionType.Frozen, out _), Is.False);
            Assert.That(_target.GetAura("Pyro").AuraAmount, Is.EqualTo(0.5f));
        }

        private ReactionResult Resolve(
            string attackElement,
            float attackAmount,
            ReactionSourceKind sourceKind = ReactionSourceKind.CharacterSkill)
        {
            return ReactionResolver.Resolve(new ReactionContext
            {
                SourceEntity = _source,
                Target = _target,
                SourceKind = sourceKind,
                SourceSkillID = "SK_Test",
                SourceEffectID = "SE_Test",
                EffectExecutionID = ReactionResolver.BeginEffectExecution(),
                AttackElement = attackElement,
                AttackAmount = attackAmount,
                PreReactionDamage = 100f,
                DamageType = "Skill"
            });
        }
    }
}
