using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public class SuperconductReactionHandlerTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private BattleManager _battle;
        private BattleEntity _source;
        private BattleEntity _left;
        private BattleEntity _main;
        private BattleEntity _right;

        [SetUp]
        public void SetUp()
        {
            ReactionResolver.ResetSession();
            _battle = NewObject("Superconduct_BattleManager").AddComponent<BattleManager>();
            ReactionStateSystem.SetDisplayAdapter(new StatusDataReactionStateDisplayAdapter());

            _source = NewEntity("Superconduct_Source", BattleSide.Ally, 1);
            _source.Level = 80;
            _source.TotalEM = 200f;
            _source.Type = BattleEntity.EntityType.Character;

            _left = NewEntity("Superconduct_Left", BattleSide.Enemy, 1);
            _main = NewEntity("Superconduct_Main", BattleSide.Enemy, 2);
            _right = NewEntity("Superconduct_Right", BattleSide.Enemy, 3);
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

        [TestCase("Cryo", "Electro")]
        [TestCase("Electro", "Cryo")]
        public void TryResolve_ConsumesOneToOneAndKeepsIncomingRemainder(
            string attackElement,
            string auraElement)
        {
            _main.ApplyAura(auraElement, 1f, 1);

            bool triggered = SuperconductReactionHandler.TryResolve(
                NewContext(attackElement, 2f),
                100f,
                out SuperconductReactionResolution result);

            Assert.That(triggered, Is.True);
            Assert.That(result.RemainingAttackAmount, Is.EqualTo(1f));
            Assert.That(_main.GetAura(auraElement), Is.Null);
            Assert.That(result.DerivedHits, Has.Count.EqualTo(3));
        }

        [Test]
        public void TryResolve_CreatesCryoHitsAndThreeRoundStates()
        {
            _main.ApplyAura("Electro", 1f, 1);

            SuperconductReactionHandler.TryResolve(
                NewContext("Cryo", 1f),
                100f,
                out SuperconductReactionResolution result);

            float expectedDamage = 100f * (1f + 16f * 200f / 2200f);
            Assert.That(result.DerivedHits, Has.Count.EqualTo(3));
            foreach (ReactionDerivedHit hit in result.DerivedHits)
            {
                Assert.That(hit.Damage, Is.EqualTo(expectedDamage).Within(0.001f));
                Assert.That(hit.DamageElement, Is.EqualTo("Cryo"));
                Assert.That(hit.ElementAmount, Is.Zero);
                Assert.That(hit.PoiseDamage, Is.Zero);
                Assert.That(ReactionStateSystem.TryGetEntityState(
                    hit.Target,
                    ReactionType.Superconduct,
                    out ReactionStateInstance state), Is.True);
                Assert.That(state.RemainingRounds, Is.EqualTo(3));
                Assert.That(hit.Target.HasStatus("ST_SuperConduct"), Is.True);
            }
        }

        [Test]
        public void TryResolve_ExistingStateIsNotRefreshedOrReplaced()
        {
            var originalSnapshot = new ReactionSourceSnapshot { SourceEntityID = 123 };
            ReactionStateInstance original = ReactionStateSystem.SetEntityState(
                _main,
                ReactionType.Superconduct,
                1,
                originalSnapshot);
            _main.ApplyAura("Electro", 1f, 1);

            SuperconductReactionHandler.TryResolve(
                NewContext("Cryo", 1f),
                100f,
                out SuperconductReactionResolution result);

            ReactionStateSystem.TryGetEntityState(
                _main,
                ReactionType.Superconduct,
                out ReactionStateInstance current);
            Assert.That(result.DerivedHits, Has.Count.EqualTo(3));
            Assert.That(current, Is.SameAs(original));
            Assert.That(current.RemainingRounds, Is.EqualTo(1));
            Assert.That(current.SourceSnapshot, Is.SameAs(originalSnapshot));
        }

        [Test]
        public void TryResolve_EnemyUsesEnemyTransformativeFormula()
        {
            _source.Type = BattleEntity.EntityType.Enemy;
            _source.TotalEM = 999f;
            _source.DMGBonus = 0.2f;
            _source.BaseDMGBonusFlat = 999f;
            _main.ApplyAura("Cryo", 1f, 1);

            SuperconductReactionHandler.TryResolve(
                NewContext("Electro", 1f, ReactionSourceKind.EnemySkill),
                100f,
                out SuperconductReactionResolution result);

            Assert.That(result.DerivedHits[0].Damage, Is.EqualTo(120f).Within(0.001f));
        }

        [Test]
        public void TryResolve_EmptyAdjacentSlotDoesNotJumpToNextUnit()
        {
            _battle.Field.GetSlot(BattleSide.Enemy, 3).Occupant = null;
            _right.Position = null;
            BattleEntity distant = NewEntity("Superconduct_Distant", BattleSide.Enemy, 4);
            _main.ApplyAura("Electro", 1f, 1);

            SuperconductReactionHandler.TryResolve(
                NewContext("Cryo", 1f),
                100f,
                out SuperconductReactionResolution result);

            Assert.That(result.DerivedHits, Has.Count.EqualTo(2));
            Assert.That(result.DerivedHits.Exists(hit => hit.Target == distant), Is.False);
            Assert.That(ReactionStateSystem.TryGetEntityState(
                distant,
                ReactionType.Superconduct,
                out _), Is.False);
        }

        [Test]
        public void Electro_ConsumesFrozenVirtualCryoUnlessShatterReservedIt()
        {
            ReactionStateSystem.SetEntityState(
                _main,
                ReactionType.Frozen,
                1,
                ReactionSourceSnapshot.Capture(NewContext("Hydro", 1f)),
                -1,
                true,
                1f);

            bool triggered = SuperconductReactionHandler.TryResolve(
                NewContext("Electro", 1f),
                100f,
                out _);

            Assert.That(triggered, Is.True);
            Assert.That(FrozenReactionHandler.IsFrozen(_main), Is.False);

            ReactionStateSystem.SetEntityState(
                _main,
                ReactionType.Frozen,
                1,
                ReactionSourceSnapshot.Capture(NewContext("Hydro", 1f)),
                -1,
                true,
                1f);
            ReactionContext reserved = NewContext("Electro", 1f);
            reserved.IgnoreFrozenAura = true;

            Assert.That(SuperconductReactionHandler.CanResolve(reserved), Is.False);
            Assert.That(FrozenReactionHandler.IsFrozen(_main), Is.True);
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
                SourceEffectID = "SE_Superconduct_Test",
                EffectExecutionID = ReactionResolver.BeginEffectExecution(),
                ApplicationPhase = (int)TurnPhase.AllyAction,
                AttackElement = attackElement,
                AttackAmount = amount,
                PreReactionDamage = 100f,
                DamageType = "Skill"
            };
        }

        private BattleEntity NewEntity(string name, BattleSide side, int position)
        {
            BattleEntity entity = NewObject(name).AddComponent<BattleEntity>();
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
