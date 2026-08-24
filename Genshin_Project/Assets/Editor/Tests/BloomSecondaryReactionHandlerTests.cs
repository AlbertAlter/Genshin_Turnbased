using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public class BloomSecondaryReactionHandlerTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private BattleManager _battle;
        private BattleEntity _coreCreator;
        private BattleEntity _trigger;
        private BattleEntity _left;
        private BattleEntity _main;
        private BattleEntity _right;

        [SetUp]
        public void SetUp()
        {
            ReactionResolver.ResetSession();
            _battle = NewObject("BloomSecondary_BattleManager").AddComponent<BattleManager>();
            _coreCreator = NewEntity("BloomSecondary_Creator", BattleSide.Ally, 1);
            _coreCreator.Type = BattleEntity.EntityType.Character;
            _coreCreator.Level = 80;
            _coreCreator.TotalEM = 10f;

            _trigger = NewEntity("BloomSecondary_Trigger", BattleSide.Ally, 2);
            _trigger.Type = BattleEntity.EntityType.Character;
            _trigger.Level = 80;
            _trigger.TotalEM = 500f;

            _left = NewEntity("BloomSecondary_Left", BattleSide.Enemy, 1);
            _main = NewEntity("BloomSecondary_Main", BattleSide.Enemy, 2);
            _right = NewEntity("BloomSecondary_Right", BattleSide.Enemy, 3);
        }

        [TearDown]
        public void TearDown()
        {
            ReactionResolver.ResetSession();
            foreach (GameObject gameObject in _objects)
                Object.DestroyImmediate(gameObject);
            _objects.Clear();
        }

        [Test]
        public void Hyperbloom_EachCoreCreatesFiveRandomMissilesUsingTriggerMastery()
        {
            CreateCore(101);
            CreateCore(102);
            int nextTarget = 0;
            BloomSecondaryReactionHandler.SetRandomIndexProvider(
                count => nextTarget++ % count);

            bool triggered = BloomSecondaryReactionHandler.TryResolve(
                TriggerContext("Electro", 200),
                ReactionType.None,
                100f,
                out BloomSecondaryReactionResolution resolution);

            Assert.That(triggered, Is.True);
            Assert.That(resolution.Type, Is.EqualTo(ReactionType.Hyperbloom));
            Assert.That(resolution.ConsumedCoreCount, Is.EqualTo(2));
            Assert.That(resolution.DerivedHits, Has.Count.EqualTo(10));
            Assert.That(BloomCoreSystem.ActiveCores, Is.Empty);
            float expectedDamage = 100f * 3f * (1f + 16f * 500f / 2500f);
            foreach (ReactionDerivedHit hit in resolution.DerivedHits)
            {
                Assert.That(hit.Damage, Is.EqualTo(expectedDamage).Within(0.001f));
                Assert.That(hit.DamageElement, Is.EqualTo("Dendro"));
                Assert.That(hit.ElementAmount, Is.Zero);
                Assert.That(hit.PoiseDamage, Is.Zero);
            }
        }

        [Test]
        public void Burgeon_EachCoreHitsOriginAndBothAdjacentTargets()
        {
            CreateCore(101);
            CreateCore(102);

            bool triggered = BloomSecondaryReactionHandler.TryResolve(
                TriggerContext("Pyro", 200),
                ReactionType.None,
                100f,
                out BloomSecondaryReactionResolution resolution);

            Assert.That(triggered, Is.True);
            Assert.That(resolution.Type, Is.EqualTo(ReactionType.Burgeon));
            Assert.That(resolution.DerivedHits, Has.Count.EqualTo(6));
            Assert.That(resolution.DerivedHits.FindAll(hit => hit.Target == _left), Has.Count.EqualTo(2));
            Assert.That(resolution.DerivedHits.FindAll(hit => hit.Target == _main), Has.Count.EqualTo(2));
            Assert.That(resolution.DerivedHits.FindAll(hit => hit.Target == _right), Has.Count.EqualTo(2));
            Assert.That(resolution.DerivedHits[0].Damage,
                Is.EqualTo(100f * 4f * (1f + 16f * 500f / 2500f)).Within(0.001f));
        }

        [Test]
        public void SameEffectCoreRemainsWhileOlderCoreIsTriggered()
        {
            CreateCore(99);
            BloomCoreInstance sameEffectCore = CreateCore(200);

            BloomSecondaryReactionHandler.TryResolve(
                TriggerContext("Pyro", 200),
                ReactionType.None,
                100f,
                out BloomSecondaryReactionResolution resolution);

            Assert.That(resolution.ConsumedCoreCount, Is.EqualTo(1));
            Assert.That(BloomCoreSystem.ActiveCores, Has.Count.EqualTo(1));
            Assert.That(BloomCoreSystem.ActiveCores[0], Is.SameAs(sameEffectCore));
            Assert.That(sameEffectCore.Position.StatusList.Contains(sameEffectCore.DisplayStatus), Is.True);
        }

        [TestCase(ReactionType.Overloaded)]
        [TestCase(ReactionType.ElectroCharged)]
        public void OverloadedAndElectroChargedCannotTriggerCores(ReactionType sourceReactionType)
        {
            CreateCore(99);

            bool triggered = BloomSecondaryReactionHandler.TryResolve(
                TriggerContext(sourceReactionType == ReactionType.Overloaded ? "Pyro" : "Electro", 200),
                sourceReactionType,
                100f,
                out _);

            Assert.That(triggered, Is.False);
            Assert.That(BloomCoreSystem.ActiveCores, Has.Count.EqualTo(1));
        }

        [Test]
        public void EmptyOriginPositionCanBeTriggeredThroughFieldPositionInterface()
        {
            CreateCore(99);
            FieldPosition origin = _battle.Field.GetSlot(BattleSide.Enemy, 2);
            origin.Occupant = null;
            _main.Position = null;

            bool triggered = BloomSecondaryReactionHandler.TryResolveAtPosition(
                TriggerContext("Pyro", 200),
                origin,
                ReactionType.None,
                100f,
                out BloomSecondaryReactionResolution resolution);

            Assert.That(triggered, Is.True);
            Assert.That(resolution.DerivedHits, Has.Count.EqualTo(2));
            Assert.That(resolution.DerivedHits.Exists(hit => hit.Target == _main), Is.False);
        }

        [Test]
        public void EnemyTriggerUsesEnemyFormulaWithoutMasteryOrFlatBonus()
        {
            CreateCore(99);
            _trigger.Type = BattleEntity.EntityType.Enemy;
            _trigger.TotalEM = 999f;
            _trigger.DMGBonus = 0.2f;
            _trigger.BaseDMGBonusFlat = 999f;

            BloomSecondaryReactionHandler.TryResolve(
                TriggerContext("Pyro", 200),
                ReactionType.None,
                100f,
                out BloomSecondaryReactionResolution resolution);

            Assert.That(resolution.DerivedHits[0].Damage, Is.EqualTo(480f).Within(0.001f));
        }

        private BloomCoreInstance CreateCore(long effectExecutionID)
        {
            _main.ApplyAura("Dendro", 1f, 1);
            ReactionContext context = new ReactionContext
            {
                SourceEntity = _coreCreator,
                Target = _main,
                SourceKind = ReactionSourceKind.CharacterSkill,
                SourceEffectID = "SE_CreateCore",
                EffectExecutionID = effectExecutionID,
                ApplicationPhase = (int)TurnPhase.AllyAction,
                AttackElement = "Hydro",
                AttackAmount = 2f,
                PreReactionDamage = 100f
            };
            BloomReactionHandler.TryResolve(context, 100f, out BloomReactionResolution resolution);
            return resolution.CreatedCore;
        }

        private ReactionContext TriggerContext(string element, long effectExecutionID)
        {
            return new ReactionContext
            {
                SourceEntity = _trigger,
                Target = _main,
                SourceKind = _trigger.Type == BattleEntity.EntityType.Enemy
                    ? ReactionSourceKind.EnemySkill
                    : ReactionSourceKind.CharacterSkill,
                SourceEffectID = "SE_TriggerCore",
                EffectExecutionID = effectExecutionID,
                ApplicationPhase = (int)TurnPhase.AllyAction,
                AttackElement = element,
                AttackAmount = 1f,
                PreReactionDamage = 100f
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
