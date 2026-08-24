using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public class ShieldResolutionTests
    {
        private GameObject _entityObject;
        private BattleEntity _entity;

        [SetUp]
        public void SetUp()
        {
            _entityObject = new GameObject("ShieldResolution_Entity");
            _entity = _entityObject.AddComponent<BattleEntity>();
            _entity.TotalHP = _entity.CurrentHP = 1000f;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_entityObject);
        }

        [TestCase("Pyro", "Pyro", 2.5f)]
        [TestCase("Pyro", "Hydro", 1.5f)]
        [TestCase("Pyro", "Physical", 1.5f)]
        [TestCase("Geo", "Pyro", 2.5f)]
        [TestCase("Geo", "Physical", 2.5f)]
        [TestCase("", "Pyro", 1f)]
        public void AbsorbDamage_UsesDocumentMultiplier(
            string shieldElement,
            string damageElement,
            float multiplier)
        {
            _entity.AddShield(20f, shieldElement, 2f);

            float remaining = _entity.AbsorbDamageWithShield(100f, damageElement);

            Assert.That(remaining, Is.EqualTo(100f - 20f * multiplier).Within(0.001f));
            Assert.That(_entity.Shields, Is.Empty);
        }

        [Test]
        public void SkillAndCrystallizeShield_TakeSameHitWithoutSerialStacking()
        {
            _entity.AddShield(1000f, string.Empty, 2f);
            _entity.AddOrReplaceCrystallizeShield(200f, "Hydro", 2, 2);

            float remaining = _entity.AbsorbDamageWithShield(300f, "Physical");

            Assert.That(remaining, Is.Zero);
            Assert.That(_entity.Shields, Has.Count.EqualTo(1));
            Assert.That(_entity.Shields[0].Kind, Is.EqualTo(ShieldKind.Skill));
            Assert.That(_entity.Shields[0].Value, Is.EqualTo(700f).Within(0.001f));
        }

        [Test]
        public void GetTotalShieldHP_ReturnsStrongestSingleShieldInsteadOfSum()
        {
            _entity.ShieldStrength = 0.5f;
            _entity.AddShield(10f, "Pyro", 2f);
            _entity.AddShield(20f, string.Empty, 2f);

            Assert.That(_entity.GetTotalShieldHP(), Is.EqualTo(30f).Within(0.001f));
            Assert.That(_entity.GetTotalShieldHP("Pyro"), Is.EqualTo(37.5f).Within(0.001f));
            Assert.That(_entity.GetTotalShieldHP("Hydro"), Is.EqualTo(30f).Within(0.001f));
        }

        [Test]
        public void TwoSmallShields_BlockByLargestSingleShieldRatherThanSum()
        {
            _entity.AddShield(10f, string.Empty, 2f);
            _entity.AddShield(20f, string.Empty, 2f);

            float remaining = _entity.AbsorbDamageWithShield(50f, "Physical");

            Assert.That(remaining, Is.EqualTo(30f).Within(0.001f));
            Assert.That(_entity.Shields, Is.Empty);
        }

        [Test]
        public void ShieldStrengthAndSingleShieldStrength_AreEachAppliedOnce()
        {
            _entity.ShieldStrength = 0.5f;
            _entity.AddShield(10f, "Pyro", 2f, strength: 2f);

            float remaining = _entity.AbsorbDamageWithShield(30f, "Hydro");

            Assert.That(remaining, Is.Zero);
            Assert.That(_entity.Shields[0].Value, Is.EqualTo(10f - 30f / 4.5f).Within(0.001f));
        }

        [Test]
        public void ZeroOrInvalidFactor_DoesNotDivideOrProduceNaN()
        {
            _entity.AddShield(10f, "Pyro", 2f, strength: 0f);

            float remaining = _entity.AbsorbDamageWithShield(30f, "Hydro");

            Assert.That(remaining, Is.EqualTo(30f));
            Assert.That(float.IsNaN(_entity.Shields[0].Value), Is.False);
            Assert.That(_entity.Shields[0].Value, Is.EqualTo(10f));
        }

        [Test]
        public void NewCrystallizeShield_ReplacesOnlyCrystallizeAndRefreshesAllFields()
        {
            _entity.AddShield(50f, string.Empty, 9f);
            Shield oldCrystal = _entity.AddOrReplaceCrystallizeShield(20f, "Hydro", 2, 2);

            Shield current = _entity.AddOrReplaceCrystallizeShield(30f, "Pyro", 2, 5);

            Assert.That(current, Is.SameAs(oldCrystal));
            Assert.That(_entity.Shields, Has.Count.EqualTo(2));
            Assert.That(current.Value, Is.EqualTo(30f));
            Assert.That(current.Element, Is.EqualTo("Pyro"));
            Assert.That(current.Duration, Is.EqualTo(2f));
            Assert.That(current.Strength, Is.EqualTo(1f));
            Assert.That(current.OriginPhase, Is.EqualTo(5));
            Assert.That(_entity.Shields.Exists(shield => shield.Kind == ShieldKind.Skill), Is.True);
        }

        [Test]
        public void CrystallizeShield_TicksOnlyAtOriginPhaseFromTwoToOneToRemoved()
        {
            Shield crystal = _entity.AddOrReplaceCrystallizeShield(20f, "Hydro", 2, 2);

            _entity.TickShields(3);
            Assert.That(crystal.Duration, Is.EqualTo(2f));
            _entity.TickShields(2);
            Assert.That(crystal.Duration, Is.EqualTo(1f));
            _entity.TickShields(4);
            Assert.That(crystal.Duration, Is.EqualTo(1f));
            _entity.TickShields(2);
            Assert.That(_entity.Shields, Is.Empty);
        }

        [Test]
        public void ResetBattle_ClearsRegisteredEntityShieldsBeforeUnregistering()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                env.Ally.Entity.AddOrReplaceCrystallizeShield(20f, "Hydro", 2, 2);
                env.Enemy.Entity.AddShield(30f, "Pyro", 2f);

                env.Manager.ResetBattle();

                Assert.That(env.Ally.Entity.Shields, Is.Empty);
                Assert.That(env.Enemy.Entity.Shields, Is.Empty);
                Assert.That(env.Manager.Allies, Is.Empty);
                Assert.That(env.Manager.Enemies, Is.Empty);
            }
        }
    }
}
