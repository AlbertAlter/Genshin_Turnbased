using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public class ElectroChargedReactionHandlerTests
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
            _battle = NewObject("ElectroCharged_BattleManager").AddComponent<BattleManager>();
            ReactionStateSystem.SetDisplayAdapter(new StatusDataReactionStateDisplayAdapter());

            _source = NewEntity("ElectroCharged_Source", BattleSide.Ally, 1);
            _source.Level = 80;
            _source.TotalEM = 200f;
            _source.Type = BattleEntity.EntityType.Character;

            _left = NewEntity("ElectroCharged_Left", BattleSide.Enemy, 1);
            _main = NewEntity("ElectroCharged_Main", BattleSide.Enemy, 2);
            _right = NewEntity("ElectroCharged_Right", BattleSide.Enemy, 3);
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

        [TestCase("Hydro", "Electro")]
        [TestCase("Electro", "Hydro")]
        public void TryResolve_ConsumesOneFromEachAndKeepsCoexistingRemainders(
            string attackElement,
            string auraElement)
        {
            _main.ApplyAura(auraElement, 2f, 1);

            bool triggered = ElectroChargedReactionHandler.TryResolve(
                NewContext(attackElement, 2f),
                100f,
                out ElectroChargedReactionResolution result);

            Assert.That(triggered, Is.True);
            Assert.That(result.RemainingAttackAmount, Is.EqualTo(1f));
            Assert.That(_main.GetAura(auraElement).AuraAmount, Is.EqualTo(1f));
            Assert.That(result.DerivedHits, Has.Count.EqualTo(1));
            Assert.That(result.DerivedHits[0].DamageElement, Is.EqualTo("Electro"));
            Assert.That(result.DerivedHits[0].ElementAmount, Is.Zero);
            Assert.That(result.DerivedHits[0].PoiseDamage, Is.EqualTo(50f));
            Assert.That(ReactionStateSystem.TryGetEntityState(
                _main,
                ReactionType.ElectroCharged,
                out ReactionStateInstance state), Is.True);
            Assert.That(state.UsesRoundDuration, Is.False);
            Assert.That(_main.HasStatus("ST_ElectroCharged"), Is.True);
        }

        [Test]
        public void TryResolve_TransmissionFollowsAdjacentHydroWithoutRepeatingTargets()
        {
            _main.ApplyAura("Electro", 1f, 1);
            _left.ApplyAura("Hydro", 1f, 1);
            _right.ApplyAura("Hydro", 1f, 1);

            ElectroChargedReactionHandler.TryResolve(
                NewContext("Hydro", 1f),
                100f,
                out ElectroChargedReactionResolution result);

            Assert.That(result.DerivedHits, Has.Count.EqualTo(3));
            Assert.That(result.DerivedHits.FindAll(hit => hit.Target == _main), Has.Count.EqualTo(1));
            Assert.That(_left.GetAura("Hydro"), Is.Null);
            Assert.That(_right.GetAura("Hydro"), Is.Null);
        }

        [Test]
        public void TryResolve_EmptyAdjacentSlotStopsTransmission()
        {
            _battle.Field.GetSlot(BattleSide.Enemy, 3).Occupant = null;
            _right.Position = null;
            BattleEntity distant = NewEntity("ElectroCharged_Distant", BattleSide.Enemy, 4);
            distant.ApplyAura("Hydro", 1f, 1);
            _main.ApplyAura("Electro", 1f, 1);

            ElectroChargedReactionHandler.TryResolve(
                NewContext("Hydro", 1f),
                100f,
                out ElectroChargedReactionResolution result);

            Assert.That(result.DerivedHits.Exists(hit => hit.Target == distant), Is.False);
            Assert.That(distant.GetAura("Hydro").AuraAmount, Is.EqualTo(1f));
        }

        [Test]
        public void TickTurnEnd_ConsumesBothAurasDealsDamageAndEndsState()
        {
            _main.ApplyAura("Electro", 2f, 1);
            ElectroChargedReactionHandler.TryResolve(
                NewContext("Hydro", 2f),
                100f,
                out ElectroChargedReactionResolution immediate);
            _main.ApplyAura("Hydro", immediate.RemainingAttackAmount, 1);
            float hpBeforeTick = _main.CurrentHP;

            ReactionStateSystem.TickPhase((int)TurnPhase.AllyAction);
            Assert.That(ReactionStateSystem.TryGetEntityState(
                _main,
                ReactionType.ElectroCharged,
                out _), Is.True);

            ElectroChargedReactionHandler.TickTurnEnd(TurnPhase.AllyPostTurn);

            Assert.That(_main.CurrentHP, Is.LessThan(hpBeforeTick));
            Assert.That(_main.Poise, Is.EqualTo(450f));
            Assert.That(_main.GetAura("Hydro"), Is.Null);
            Assert.That(_main.GetAura("Electro"), Is.Null);
            Assert.That(ReactionStateSystem.TryGetEntityState(
                _main,
                ReactionType.ElectroCharged,
                out _), Is.False);
            Assert.That(_main.HasStatus("ST_ElectroCharged"), Is.False);
        }

        [Test]
        public void TryResolve_EnemyFormulaDoesNotUseElementalMasteryOrFlatBonus()
        {
            _source.Type = BattleEntity.EntityType.Enemy;
            _source.TotalEM = 999f;
            _source.DMGBonus = 0.2f;
            _source.BaseDMGBonusFlat = 999f;
            _main.ApplyAura("Hydro", 1f, 1);

            ElectroChargedReactionHandler.TryResolve(
                NewContext("Electro", 1f, ReactionSourceKind.EnemySkill),
                100f,
                out ElectroChargedReactionResolution result);

            Assert.That(result.DerivedHits[0].Damage, Is.EqualTo(1152f).Within(0.001f));
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
                SourceEffectID = "SE_ElectroCharged_Test",
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
