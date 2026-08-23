using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public class OverloadedReactionHandlerTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private GameObject _battleObject;
        private BattleManager _battle;
        private BattleEntity _source;
        private BattleEntity _left;
        private BattleEntity _main;
        private BattleEntity _right;

        [SetUp]
        public void SetUp()
        {
            ReactionResolver.ResetSession();
            _battleObject = NewObject("Overloaded_BattleManager");
            _battle = _battleObject.AddComponent<BattleManager>();
            _source = NewEntity("Overloaded_Source", BattleSide.Ally, 1);
            _source.Level = 80;
            _source.TotalEM = 200f;
            _source.Type = BattleEntity.EntityType.Character;

            _left = NewEntity("Overloaded_Left", BattleSide.Enemy, 1);
            _main = NewEntity("Overloaded_Main", BattleSide.Enemy, 2);
            _right = NewEntity("Overloaded_Right", BattleSide.Enemy, 3);
        }

        [TearDown]
        public void TearDown()
        {
            ReactionResolver.ResetSession();
            for (int i = _objects.Count - 1; i >= 0; i--)
                Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
        }

        [TestCase("Pyro", "Electro")]
        [TestCase("Electro", "Pyro")]
        public void TryResolve_ConsumesOneToOneAndKeepsIncomingRemainder(
            string attackElement,
            string auraElement)
        {
            _main.ApplyAura(auraElement, 1f, 1);

            bool triggered = OverloadedReactionHandler.TryResolve(
                NewContext(attackElement, 2f),
                100f,
                out OverloadedReactionResolution result);

            Assert.That(triggered, Is.True);
            Assert.That(result.RemainingAttackAmount, Is.EqualTo(1f));
            Assert.That(_main.GetAura(auraElement), Is.Null);
            Assert.That(result.DerivedHits, Has.Count.EqualTo(3));
        }

        [Test]
        public void TryResolve_CharacterDamageHitsMainAndPhysicalNeighbours()
        {
            _main.ApplyAura("Electro", 1f, 1);
            _right.PyroRes = 0.5f;

            OverloadedReactionHandler.TryResolve(
                NewContext("Pyro", 1f),
                100f,
                out OverloadedReactionResolution result);
            ReactionEffectExecutor.ExecuteDerivedHits(ToResult(result));

            float normalDamage = 100f * 4f * (1f + 16f * 200f / 2200f);
            Assert.That(_left.CurrentHP, Is.EqualTo(10000f - normalDamage).Within(0.001f));
            Assert.That(_main.CurrentHP, Is.EqualTo(10000f - normalDamage).Within(0.001f));
            Assert.That(_right.CurrentHP, Is.EqualTo(10000f - normalDamage * 0.5f).Within(0.001f));
            Assert.That(_left.Poise, Is.EqualTo(400f));
            Assert.That(_main.Poise, Is.EqualTo(400f));
            Assert.That(_right.Poise, Is.EqualTo(400f));
        }

        [Test]
        public void TryResolve_EmptyAdjacentSlotDoesNotJumpToNextUnit()
        {
            _battle.Field.GetSlot(BattleSide.Enemy, 3).Occupant = null;
            _right.Position = null;
            BattleEntity distant = NewEntity("Overloaded_Distant", BattleSide.Enemy, 4);
            _main.ApplyAura("Electro", 1f, 1);

            OverloadedReactionHandler.TryResolve(
                NewContext("Pyro", 1f),
                100f,
                out OverloadedReactionResolution result);

            Assert.That(result.DerivedHits, Has.Count.EqualTo(2));
            Assert.That(result.DerivedHits.Exists(hit => hit.Target == distant), Is.False);
        }

        [Test]
        public void TryResolve_EnemyUsesEnemyTransformativeFormula()
        {
            _source.Type = BattleEntity.EntityType.Enemy;
            _source.TotalEM = 999f;
            _source.DMGBonus = 0.2f;
            _source.BaseDMGBonusFlat = 999f;
            _main.ApplyAura("Pyro", 1f, 1);

            OverloadedReactionHandler.TryResolve(
                NewContext("Electro", 1f, ReactionSourceKind.EnemySkill),
                100f,
                out OverloadedReactionResolution result);

            Assert.That(result.DerivedHits[0].Damage, Is.EqualTo(480f).Within(0.001f));
        }

        [Test]
        public void ExecuteDerivedHits_FullyShieldedHitDoesNotDealPoiseDamage()
        {
            _main.ApplyAura("Electro", 1f, 1);
            _main.AddShield(10000f, "Pyro", 1f);

            OverloadedReactionHandler.TryResolve(
                NewContext("Pyro", 1f),
                100f,
                out OverloadedReactionResolution result);
            ReactionEffectExecutor.ExecuteDerivedHits(ToResult(result));

            Assert.That(_main.CurrentHP, Is.EqualTo(10000f));
            Assert.That(_main.Poise, Is.EqualTo(500f));
        }

        private ReactionContext NewContext(
            string attackElement,
            float amount,
            ReactionSourceKind sourceKind = ReactionSourceKind.CharacterSkill)
        {
            return new ReactionContext
            {
                SourceEntity = _source,
                Target = _main,
                SourceKind = sourceKind,
                SourceEffectID = "SE_Overloaded_Test",
                EffectExecutionID = ReactionResolver.BeginEffectExecution(),
                AttackElement = attackElement,
                AttackAmount = amount,
                PreReactionDamage = 100f,
                DamageType = "Skill"
            };
        }

        private static ReactionResult ToResult(OverloadedReactionResolution resolution)
        {
            var result = new ReactionResult();
            result.DerivedHits.AddRange(resolution.DerivedHits);
            return result;
        }

        private BattleEntity NewEntity(string name, BattleSide side, int position)
        {
            GameObject gameObject = NewObject(name);
            BattleEntity entity = gameObject.AddComponent<BattleEntity>();
            entity.Side = side;
            entity.SlotPosition = position;
            entity.TotalHP = 10000f;
            entity.CurrentHP = 10000f;
            entity.MaxPoise = 500f;
            entity.Poise = 500f;

            FieldPosition slot = _battle.Field.GetSlot(side, position);
            slot.Occupant = entity;
            entity.Position = slot;
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
