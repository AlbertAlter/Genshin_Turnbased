using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public class ElementAuraSystemTests
    {
        private GameObject _sourceObject;
        private GameObject _targetObject;
        private BattleEntity _source;
        private BattleEntity _target;

        [SetUp]
        public void SetUp()
        {
            ReactionResolver.ResetSession();
            _sourceObject = new GameObject("Aura_Source");
            _targetObject = new GameObject("Aura_Target");
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

        [TestCase(0.5f)]
        [TestCase(2f)]
        [TestCase(4f)]
        public void ApplyAura_SameElement_AlwaysReplacesAmountAndSource(float newAmount)
        {
            _target.ApplyAura("Electro", 2f, 1, "SE_Old", "SK_Old");
            ElementalAura oldAura = _target.GetAura("Electro");
            oldAura.OriginPhase = 6;

            _target.ApplyAura("Electro", newAmount, 2, "SE_New", "SK_New");

            ElementalAura aura = _target.GetAura("Electro");
            Assert.That(aura, Is.SameAs(oldAura));
            Assert.That(aura.AuraAmount, Is.EqualTo(newAmount));
            Assert.That(aura.SourceEntityID, Is.EqualTo(2));
            Assert.That(aura.SourceEffectID, Is.EqualTo("SE_New"));
            Assert.That(aura.SourceSkillID, Is.EqualTo("SK_New"));
            Assert.That(aura.OriginPhase, Is.EqualTo(-1));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        public void ApplyAura_NonPositiveAmount_DoesNotCreateOrReplace(float amount)
        {
            _target.ApplyAura("Pyro", 2f, 1, "SE_Old", "SK_Old");

            _target.ApplyAura("Pyro", amount, 2, "SE_New", "SK_New");
            _target.ApplyAura("Hydro", amount, 2, "SE_New", "SK_New");

            Assert.That(_target.GetAura("Pyro").AuraAmount, Is.EqualTo(2f));
            Assert.That(_target.GetAura("Pyro").SourceEntityID, Is.EqualTo(1));
            Assert.That(_target.GetAura("Hydro"), Is.Null);
        }

        [Test]
        public void Resolve_ReplacesSameElementBeforeReactingWithCoexistingAura()
        {
            _target.ApplyAura("Pyro", 4f, 1, "SE_Old", "SK_Old");
            _target.ApplyAura("Hydro", 2f, 3, "SE_Hydro", "SK_Hydro");

            ReactionResult result = ReactionResolver.Resolve(NewContext("Pyro", 1f));

            Assert.That(result.HasReaction, Is.False, "蒸发在阶段3实现，本阶段只验证附着顺序");
            Assert.That(_target.GetAura("Pyro").AuraAmount, Is.EqualTo(1f));
            Assert.That(_target.GetAura("Pyro").SourceEntityID, Is.EqualTo(_source.EntityID));
            Assert.That(_target.GetAura("Hydro").AuraAmount, Is.EqualTo(2f));
        }

        [Test]
        public void TickAuras_ElementsDecayIndependently()
        {
            _target.ApplyAura("Pyro", 1f, 1);
            _target.ApplyAura("Hydro", 2f, 2);

            _target.ConsumeAura("Pyro", 0.25f);
            _target.TickAuras();

            Assert.That(_target.GetAura("Pyro").AuraAmount, Is.EqualTo(0.25f));
            Assert.That(_target.GetAura("Hydro").AuraAmount, Is.EqualTo(1.5f));
        }

        private ReactionContext NewContext(string element, float amount)
        {
            return new ReactionContext
            {
                SourceEntity = _source,
                Target = _target,
                SourceKind = ReactionSourceKind.CharacterSkill,
                SourceSkillID = "SK_New",
                SourceEffectID = "SE_New",
                EffectExecutionID = ReactionResolver.BeginEffectExecution(),
                AttackElement = element,
                AttackAmount = amount,
                PreReactionDamage = 100f,
                CanTriggerReaction = false,
                CanApplyAura = true
            };
        }
    }
}
