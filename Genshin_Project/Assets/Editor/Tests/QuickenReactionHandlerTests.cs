using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public class QuickenReactionHandlerTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private BattleEntity _source;
        private BattleEntity _target;

        [SetUp]
        public void SetUp()
        {
            ReactionResolver.ResetSession();
            ReactionStateSystem.SetDisplayAdapter(new StatusDataReactionStateDisplayAdapter());
            _source = NewEntity("Quicken_Source");
            _source.Type = BattleEntity.EntityType.Character;
            _source.Level = 80;
            _source.TotalEM = 200f;
            _target = NewEntity("Quicken_Target");
            _target.Type = BattleEntity.EntityType.Enemy;
        }

        [TearDown]
        public void TearDown()
        {
            ReactionResolver.ResetSession();
            ReactionStateSystem.SetDisplayAdapter(null);
            foreach (GameObject gameObject in _objects)
                Object.DestroyImmediate(gameObject);
            _objects.Clear();
        }

        [TestCase("Electro", "Dendro")]
        [TestCase("Dendro", "Electro")]
        public void TryResolve_ConsumesOneToOneAndAppliesDisplayState(
            string attackElement,
            string auraElement)
        {
            _target.ApplyAura(auraElement, 1f, 1);

            bool triggered = QuickenReactionHandler.TryResolve(
                NewContext(attackElement, 2f),
                out QuickenReactionResolution resolution);

            Assert.That(triggered, Is.True);
            Assert.That(resolution.RemainingAttackAmount, Is.EqualTo(1f));
            Assert.That(_target.GetAura(auraElement), Is.Null);
            Assert.That(resolution.State.RemainingRounds, Is.EqualTo(1));
            Assert.That(_target.HasStatus("ST_Quicken"), Is.True);
        }

        [Test]
        public void Reapply_AddsOneRoundUpToThreeAndUsesNewestSnapshot()
        {
            BattleEntity newestSource = NewEntity("Quicken_NewestSource");
            newestSource.Type = BattleEntity.EntityType.Character;

            TriggerQuicken(_source, 1);
            TriggerQuicken(newestSource, 2);
            TriggerQuicken(newestSource, 3);
            TriggerQuicken(newestSource, 4);

            Assert.That(ReactionStateSystem.TryGetEntityState(
                _target,
                ReactionType.Quicken,
                out ReactionStateInstance state), Is.True);
            Assert.That(state.RemainingRounds, Is.EqualTo(3));
            Assert.That(state.SourceSnapshot.SourceEntity, Is.SameAs(newestSource));
            Assert.That(state.OriginPhase, Is.EqualTo((int)TurnPhase.EnemyAction));
            Assert.That(_target.GetStatus("ST_Quicken").RemainingPhaseCount, Is.EqualTo(3));
        }

        [Test]
        public void TickPhase_ExpiresFromLatestApplicationPhase()
        {
            TriggerQuicken(_source, 1);

            ReactionStateSystem.TickPhase((int)TurnPhase.AllyAction);
            Assert.That(ReactionStateSystem.TryGetEntityState(
                _target, ReactionType.Quicken, out _), Is.True);

            ReactionStateSystem.TickPhase((int)TurnPhase.EnemyAction);
            Assert.That(ReactionStateSystem.TryGetEntityState(
                _target, ReactionType.Quicken, out _), Is.False);
            Assert.That(_target.HasStatus("ST_Quicken"), Is.False);
        }

        [TestCase("Electro", ReactionType.Aggravate, 1.15f)]
        [TestCase("Dendro", ReactionType.Spread, 1.25f)]
        public void QuickenDamage_CharacterUsesCurrentAttackerMasteryAndOriginalMultipliers(
            string attackElement,
            ReactionType expectedType,
            float quickenMultiplier)
        {
            ApplyQuickenState(_source);
            ReactionContext context = DamageContext(attackElement, 1f);

            bool triggered = QuickenDamageHandler.TryResolve(
                context,
                100f,
                out QuickenDamageResolution resolution);

            float expected = (200f
                              + quickenMultiplier * 100f
                              * (1f + 5f * 200f / 1400f))
                             * 1.5f * 1.2f * 0.5f * 0.8f;
            Assert.That(triggered, Is.True);
            Assert.That(resolution.Type, Is.EqualTo(expectedType));
            Assert.That(resolution.FinalDamage, Is.EqualTo(expected).Within(0.001f));
        }

        [Test]
        public void QuickenDamage_EnemyFormulaHasNoMasteryInput()
        {
            _source.Type = BattleEntity.EntityType.Enemy;
            _source.TotalEM = 999f;
            ApplyQuickenState(_source);

            QuickenDamageHandler.TryResolve(
                DamageContext("Electro", 1f),
                100f,
                out QuickenDamageResolution resolution);

            float expected = (200f + 1.15f * 100f) * 1.5f * 1.2f * 0.5f * 0.8f;
            Assert.That(resolution.FinalDamage, Is.EqualTo(expected).Within(0.001f));
        }

        [Test]
        public void ZeroElementAmount_DoesNotUseQuickenFormula()
        {
            ApplyQuickenState(_source);

            Assert.That(QuickenDamageHandler.TryResolve(
                DamageContext("Dendro", 0f),
                100f,
                out _), Is.False);
        }

        [Test]
        public void ExclusiveReactionBonus_FiltersReactionAndElement()
        {
            var statuses = new List<StatusInstance>
            {
                NewBonusStatus(1, "超激化", "Electro"),
                NewBonusStatus(2, "蔓激化", "Electro"),
                NewBonusStatus(3, "超激化", "Dendro"),
                NewBonusStatus(4, "", "Electro")
            };
            var effects = new List<StatusEffectData>
            {
                NewBonusEffect(1, 0.2f),
                NewBonusEffect(2, 0.3f),
                NewBonusEffect(3, 0.4f),
                NewBonusEffect(4, 0.5f)
            };

            float bonus = ReactionDamageCalculator.CollectExclusiveReactionDamageBonus(
                statuses,
                effects,
                ReactionType.Aggravate,
                "Electro");

            Assert.That(bonus, Is.EqualTo(0.2f).Within(0.001f));
        }

        private void TriggerQuicken(BattleEntity source, long executionID)
        {
            _target.ApplyAura("Dendro", 1f, 1);
            ReactionContext context = NewContext("Electro", 1f);
            context.SourceEntity = source;
            context.EffectExecutionID = executionID;
            context.ApplicationPhase = (int)TurnPhase.EnemyAction;
            QuickenReactionHandler.TryResolve(context, out _);
        }

        private void ApplyQuickenState(BattleEntity source)
        {
            ReactionStateSystem.SetEntityState(
                _target,
                ReactionType.Quicken,
                1,
                new ReactionSourceSnapshot { SourceEntity = source },
                (int)TurnPhase.AllyAction);
        }

        private ReactionContext NewContext(string element, float amount)
        {
            return new ReactionContext
            {
                SourceEntity = _source,
                Target = _target,
                SourceKind = _source.Type == BattleEntity.EntityType.Enemy
                    ? ReactionSourceKind.EnemySkill
                    : ReactionSourceKind.CharacterSkill,
                SourceEffectID = "SE_Quicken_Test",
                EffectExecutionID = 1,
                ApplicationPhase = (int)TurnPhase.AllyAction,
                AttackElement = element,
                AttackAmount = amount,
                PreReactionDamage = 100f
            };
        }

        private ReactionContext DamageContext(string element, float amount)
        {
            ReactionContext context = NewContext(element, amount);
            context.DamageComponents = new ReactionDamageComponents
            {
                SkillBaseDamage = 200f,
                CriticalMultiplier = 1.5f,
                DamageBonusMultiplier = 1.2f,
                DefenseMultiplier = 0.5f,
                ResistanceMultiplier = 0.8f
            };
            return context;
        }

        private static StatusInstance NewBonusStatus(
            int statusID,
            string reactionType,
            string elementType)
        {
            return new StatusInstance
            {
                IsActive = true,
                StackCount = 1,
                MainData = new StatusMainData
                {
                    StatusID = statusID,
                    StatusID2 = $"ST_QuickenBonus_{statusID}",
                    MultiplierPart1 = "DMGBonus",
                    ApplyReactionType = reactionType,
                    ApplyElementType = elementType
                }
            };
        }

        private static StatusEffectData NewBonusEffect(int statusID, float value)
        {
            return new StatusEffectData
            {
                StatusEffectID = statusID * 100 + 1,
                EffectType = "Buff",
                Param1 = value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
        }

        private BattleEntity NewEntity(string name)
        {
            var gameObject = new GameObject(name);
            _objects.Add(gameObject);
            BattleEntity entity = gameObject.AddComponent<BattleEntity>();
            entity.TotalHP = 10000f;
            entity.CurrentHP = 10000f;
            return entity;
        }
    }
}
