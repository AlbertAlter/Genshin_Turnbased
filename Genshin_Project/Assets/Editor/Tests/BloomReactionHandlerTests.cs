using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public class BloomReactionHandlerTests
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
            _battle = NewObject("Bloom_BattleManager").AddComponent<BattleManager>();
            _source = NewEntity("Bloom_Source", BattleSide.Ally, 1);
            _source.Type = BattleEntity.EntityType.Character;
            _source.Level = 80;
            _source.TotalEM = 200f;

            _left = NewEntity("Bloom_Left", BattleSide.Enemy, 1);
            _main = NewEntity("Bloom_Main", BattleSide.Enemy, 2);
            _right = NewEntity("Bloom_Right", BattleSide.Enemy, 3);
        }

        [TearDown]
        public void TearDown()
        {
            ReactionResolver.ResetSession();
            for (int index = _objects.Count - 1; index >= 0; index--)
                Object.DestroyImmediate(_objects[index]);
            _objects.Clear();
        }

        [TestCase("Hydro", "Dendro", 2f, 2f, 0f, 1f)]
        [TestCase("Dendro", "Hydro", 2f, 2f, 1f, 0f)]
        public void TryResolve_UsesTwoToOneConsumptionAndCreatesOneCore(
            string attackElement,
            string auraElement,
            float attackAmount,
            float auraAmount,
            float expectedAttackRemaining,
            float expectedAuraRemaining)
        {
            _main.ApplyAura(auraElement, auraAmount, 1);

            bool triggered = BloomReactionHandler.TryResolve(
                NewContext(attackElement, attackAmount),
                100f,
                out BloomReactionResolution result);

            Assert.That(triggered, Is.True);
            Assert.That(result.RemainingAttackAmount, Is.EqualTo(expectedAttackRemaining));
            ElementalAura remainingAura = _main.GetAura(auraElement);
            if (expectedAuraRemaining <= 0f)
                Assert.That(remainingAura, Is.Null);
            else
                Assert.That(remainingAura.AuraAmount, Is.EqualTo(expectedAuraRemaining));
            Assert.That(BloomCoreSystem.ActiveCores, Has.Count.EqualTo(1));
            Assert.That(result.CreatedCore.Position,
                Is.SameAs(_battle.Field.GetSlot(BattleSide.Enemy, 2)));
            Assert.That(result.CreatedCore.Position.StatusList.Exists(
                status => status.StatusID2 == BloomCoreSystem.StatusID), Is.True);
        }

        [Test]
        public void TickPhase_ExpiresAtCreationPhaseAndHitsPositionAndNeighbours()
        {
            BloomCoreInstance core = CreateCoreAtMain(100f);

            BloomCoreSystem.TickPhase((int)TurnPhase.EnemyAction);
            Assert.That(BloomCoreSystem.ActiveCores, Has.Count.EqualTo(1));
            Assert.That(_main.CurrentHP, Is.EqualTo(10000f));

            BloomCoreSystem.TickPhase((int)TurnPhase.AllyAction);

            float damage = 100f * 2f * (1f + 16f * 200f / 2200f);
            Assert.That(_left.CurrentHP, Is.EqualTo(10000f - damage).Within(0.001f));
            Assert.That(_main.CurrentHP, Is.EqualTo(10000f - damage).Within(0.001f));
            Assert.That(_right.CurrentHP, Is.EqualTo(10000f - damage).Within(0.001f));
            Assert.That(_main.Poise, Is.EqualTo(500f));
            Assert.That(BloomCoreSystem.ActiveCores, Is.Empty);
            Assert.That(core.Position.StatusList.Contains(core.DisplayStatus), Is.False);
        }

        [Test]
        public void CoreRemainsOnEmptyPositionAndExplosionDoesNotJumpAcrossEmptySlot()
        {
            BloomCoreInstance core = CreateCoreAtMain(100f);
            _battle.Field.GetSlot(BattleSide.Enemy, 2).Occupant = null;
            _main.Position = null;
            _battle.Field.GetSlot(BattleSide.Enemy, 3).Occupant = null;
            _right.Position = null;
            BattleEntity distant = NewEntity("Bloom_Distant", BattleSide.Enemy, 4);

            Assert.That(core.Position.StatusList.Contains(core.DisplayStatus), Is.True);
            BloomCoreSystem.TickPhase((int)TurnPhase.AllyAction);

            Assert.That(_left.CurrentHP, Is.LessThan(10000f));
            Assert.That(_main.CurrentHP, Is.EqualTo(10000f));
            Assert.That(_right.CurrentHP, Is.EqualTo(10000f));
            Assert.That(distant.CurrentHP, Is.EqualTo(10000f));
        }

        [Test]
        public void SixthCoreDetonatesOldestAndKeepsFiveNewestCores()
        {
            BloomCoreInstance first = null;
            var overflowHits = new List<ReactionDerivedHit>();
            for (int count = 0; count < 6; count++)
            {
                _main.ApplyAura("Dendro", 1f, 1);
                BloomReactionHandler.TryResolve(
                    NewContext("Hydro", 2f),
                    100f,
                    out BloomReactionResolution resolution);
                BloomCoreInstance created = resolution.CreatedCore;
                overflowHits.AddRange(resolution.DerivedHits);
                if (count == 0) first = created;
            }

            Assert.That(BloomCoreSystem.ActiveCores, Has.Count.EqualTo(5));
            Assert.That(
                new List<BloomCoreInstance>(BloomCoreSystem.ActiveCores).Contains(first),
                Is.False);
            Assert.That(_battle.Field.GetSlot(BattleSide.Enemy, 2).StatusList.FindAll(
                status => status.StatusID2 == BloomCoreSystem.StatusID), Has.Count.EqualTo(5));
            Assert.That(overflowHits, Has.Count.EqualTo(3));
            foreach (ReactionDerivedHit hit in overflowHits)
            {
                Assert.That(hit.Source, Is.Not.Null);
                Assert.That(hit.Source.SourceEffectID, Is.EqualTo("SE_Bloom_Test"));
                Assert.That(hit.Source.ReactionType, Is.EqualTo(ReactionType.Bloom));
                Assert.That(hit.ReactionType, Is.EqualTo(ReactionType.Bloom));
            }
            Assert.That(_main.CurrentHP, Is.EqualTo(10000f));
            var result = new ReactionResult();
            result.DerivedHits.AddRange(overflowHits);
            ReactionEffectExecutor.ExecuteDerivedHits(result);
            Assert.That(_main.CurrentHP, Is.LessThan(10000f));
        }

        [Test]
        public void NaturalDetonation_CarriesBloomSourceAndReactionType()
        {
            BloomCoreInstance core = CreateCoreAtMain(100f);

            List<ReactionDerivedHit> hits = BloomCoreSystem.Detonate(core);

            Assert.That(hits, Is.Not.Empty);
            foreach (ReactionDerivedHit hit in hits)
            {
                Assert.That(hit.Source, Is.Not.Null);
                Assert.That(hit.Source.SourceEntityID, Is.EqualTo(_source.EntityID));
                Assert.That(hit.Source.SourceEffectID, Is.EqualTo("SE_Bloom_Test"));
                Assert.That(hit.Source.ReactionType, Is.EqualTo(ReactionType.Bloom));
                Assert.That(hit.ReactionType, Is.EqualTo(ReactionType.Bloom));
            }
        }

        [Test]
        public void TriggerConditions_RejectSameEffectOverloadedAndElectroCharged()
        {
            ReactionContext context = NewContext("Hydro", 2f);
            context.EffectExecutionID = 99;
            _main.ApplyAura("Dendro", 1f, 1);
            BloomReactionHandler.TryResolve(context, 100f, out _);

            Assert.That(BloomCoreSystem.GetTriggerableCores(
                2, "Pyro", ReactionType.None, 99), Is.Empty);
            Assert.That(BloomCoreSystem.GetTriggerableCores(
                2, "Pyro", ReactionType.Overloaded, 100), Is.Empty);
            Assert.That(BloomCoreSystem.GetTriggerableCores(
                2, "Electro", ReactionType.ElectroCharged, 100), Is.Empty);
            Assert.That(BloomCoreSystem.GetTriggerableCores(
                2, "Pyro", ReactionType.None, 100), Has.Count.EqualTo(1));
        }

        [Test]
        public void HydroPrioritizesExcessDendroBeforeBurningElement()
        {
            _main.ApplyAura("Dendro", 2f, 1);
            BurningReactionHandler.TryResolve(
                NewContext("Pyro", 1f),
                100f,
                out _);
            ReactionContext hydro = NewContext("Hydro", 2f);

            Assert.That(AmplifyingReactionHandler.TryResolve(hydro, out _), Is.False);
            Assert.That(BloomReactionHandler.TryResolve(
                hydro,
                100f,
                out _), Is.True);
            Assert.That(BurningReactionHandler.IsBurning(_main), Is.True);
        }

        [Test]
        public void CoreExplosion_EnemyFormulaDoesNotUseElementalMasteryOrFlatBonus()
        {
            _source.Type = BattleEntity.EntityType.Enemy;
            _source.TotalEM = 999f;
            _source.DMGBonus = 0.2f;
            _source.BaseDMGBonusFlat = 999f;
            BloomCoreInstance core = CreateCoreAtMain(100f);

            BloomCoreSystem.Detonate(core);

            Assert.That(_main.CurrentHP, Is.EqualTo(9760f).Within(0.001f));
        }

        private BloomCoreInstance CreateCoreAtMain(float levelCoefficient)
        {
            _main.ApplyAura("Dendro", 1f, 1);
            BloomReactionHandler.TryResolve(
                NewContext("Hydro", 2f),
                levelCoefficient,
                out BloomReactionResolution resolution);
            return resolution.CreatedCore;
        }

        private ReactionContext NewContext(string attackElement, float attackAmount)
        {
            return new ReactionContext
            {
                SourceEntity = _source,
                Target = _main,
                SourceKind = _source.Type == BattleEntity.EntityType.Enemy
                    ? ReactionSourceKind.EnemySkill
                    : ReactionSourceKind.CharacterSkill,
                SourceEffectID = "SE_Bloom_Test",
                EffectExecutionID = ReactionResolver.BeginEffectExecution(),
                ApplicationPhase = (int)TurnPhase.AllyAction,
                AttackElement = attackElement,
                AttackAmount = attackAmount,
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
