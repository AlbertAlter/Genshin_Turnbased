using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public class FrozenReactionHandlerTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private BattleEntity _source;
        private BattleEntity _target;

        [SetUp]
        public void SetUp()
        {
            ReactionStateSystem.ClearAll();
            ReactionStateSystem.SetDisplayAdapter(new StatusDataReactionStateDisplayAdapter());
            _source = NewEntity("Frozen_Source", BattleEntity.EntityType.Character, BattleSide.Ally);
            _source.Level = 80;
            _source.TotalEM = 200f;
            _target = NewEntity("Frozen_Target", BattleEntity.EntityType.Enemy, BattleSide.Enemy);
        }

        [TearDown]
        public void TearDown()
        {
            ReactionStateSystem.ClearAll();
            ReactionStateSystem.SetDisplayAdapter(null);
            for (int i = _objects.Count - 1; i >= 0; i--)
                Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
        }

        [Test]
        public void TryResolveFreeze_ConsumesOneToOneAndKeepsIncomingRemainder()
        {
            _target.ApplyAura("Cryo", 1.5f, 1);

            bool triggered = FrozenReactionHandler.TryResolveFreeze(
                NewContext("Hydro", 2f, 20f),
                out FrozenReactionResolution result);

            Assert.That(triggered, Is.True);
            Assert.That(result.RemainingAttackAmount, Is.EqualTo(0.5f));
            Assert.That(_target.GetAura("Cryo"), Is.Null);
            Assert.That(ReactionStateSystem.TryGetEntityState(
                _target, ReactionType.Frozen, out ReactionStateInstance state), Is.True);
            Assert.That(state.RemainingRounds, Is.EqualTo(2));
            Assert.That(state.ReactionElementAmount, Is.EqualTo(1.5f));
            Assert.That(_target.HasStatus("ST_Freeze"), Is.True);
        }

        [Test]
        public void ConsumeFrozenAura_KeepsExactAmountAndRecalculatesDuration()
        {
            _target.ApplyAura("Cryo", 1.5f, 1);
            FrozenReactionHandler.TryResolveFreeze(
                NewContext("Hydro", 1.5f, 20f),
                out _);

            float consumed = FrozenReactionHandler.ConsumeFrozenAura(_target, 0.6f);

            Assert.That(consumed, Is.EqualTo(0.6f).Within(0.001f));
            Assert.That(FrozenReactionHandler.GetFrozenAuraAsCryo(_target),
                Is.EqualTo(0.9f).Within(0.001f));
            ReactionStateSystem.TryGetEntityState(_target, ReactionType.Frozen, out var state);
            Assert.That(state.RemainingRounds, Is.EqualTo(1));
        }

        [Test]
        public void TryResolveFreeze_ExistingFreezeIsNotRefreshedOrReplaced()
        {
            var originalSnapshot = new ReactionSourceSnapshot { SourceEntityID = 123 };
            ReactionStateInstance original = ReactionStateSystem.SetEntityState(
                _target, ReactionType.Frozen, 3, originalSnapshot, -1, true, 3f);
            _target.ApplyAura("Hydro", 1f, 1);

            FrozenReactionHandler.TryResolveFreeze(
                NewContext("Cryo", 1f, 20f),
                out _);

            ReactionStateSystem.TryGetEntityState(_target, ReactionType.Frozen, out var current);
            Assert.That(current, Is.SameAs(original));
            Assert.That(current.RemainingRounds, Is.EqualTo(3));
            Assert.That(current.SourceSnapshot, Is.SameAs(originalSnapshot));
        }

        [Test]
        public void TickPhase_UsesFreezeApplicationPhaseAsTimerOrigin()
        {
            BattleManager battle = NewObject("Frozen_Battle").AddComponent<BattleManager>();
            SetPhase(battle, TurnPhase.AllyAction);
            _target.ApplyAura("Cryo", 1f, 1);
            ReactionContext context = NewContext("Hydro", 1f, 20f);
            context.ApplicationPhase = (int)TurnPhase.AllyAction;
            FrozenReactionHandler.TryResolveFreeze(context, out _);

            ReactionStateSystem.TickPhase((int)TurnPhase.EnemyAction);
            Assert.That(FrozenReactionHandler.IsFrozen(_target), Is.True);

            ReactionStateSystem.TickPhase((int)TurnPhase.AllyAction);
            Assert.That(FrozenReactionHandler.IsFrozen(_target), Is.False);
            Assert.That(_target.HasStatus("ST_Freeze"), Is.False);
        }

        [Test]
        public void CanShatter_OneHundredPoiseDamageTriggersRegardlessOfCurrentPoise()
        {
            ReactionStateSystem.SetEntityState(
                _target, ReactionType.Frozen, 1,
                ReactionSourceSnapshot.Capture(NewContext("Hydro", 1f, 100f)), -1, true, 1f);
            _target.Poise = 500f;

            Assert.That(FrozenReactionHandler.CanShatter(
                NewContext("None", 0f, 100f)), Is.True);
        }

        [Test]
        public void LowerPoiseHit_UsesNormalPoiseCalculationWithoutRemovingFreeze()
        {
            ReactionStateSystem.SetEntityState(
                _target, ReactionType.Frozen, 1,
                ReactionSourceSnapshot.Capture(NewContext("Hydro", 1f, 20f)), -1, true, 1f);

            PoiseSystem.ApplyPoiseDamage(_target, 20f, 1f, _source);

            Assert.That(_target.Poise, Is.EqualTo(80f));
            Assert.That(FrozenReactionHandler.IsFrozen(_target), Is.True);
        }

        [Test]
        public void FinalizePrimaryHit_NoHpDamageKeepsFreezeAndPoise()
        {
            ReactionStateSystem.SetEntityState(
                _target, ReactionType.Frozen, 1,
                ReactionSourceSnapshot.Capture(NewContext("Hydro", 1f, 100f)), -1, true, 1f);
            ReactionResult result = NewPendingShatterResult(300f);

            ReactionEffectExecutor.FinalizePrimaryHit(result, 0f);

            Assert.That(FrozenReactionHandler.IsFrozen(_target), Is.True);
            Assert.That(_target.Poise, Is.EqualTo(100f));
            Assert.That(result.DerivedHits, Is.Empty);
        }

        [Test]
        public void FinalizePrimaryHit_RemovesFreezeDealsPhysicalAndBreaksPoise()
        {
            ReactionStateSystem.SetEntityState(
                _target, ReactionType.Frozen, 1,
                ReactionSourceSnapshot.Capture(NewContext("Hydro", 1f, 100f)), -1, true, 1f);
            ReactionResult result = NewPendingShatterResult(300f);

            ReactionEffectExecutor.FinalizePrimaryHit(result, 1f);

            Assert.That(FrozenReactionHandler.IsFrozen(_target), Is.False);
            Assert.That(_target.Poise, Is.Zero);
            Assert.That(_target.HasStatus(PoiseSystem.EnemyKnockdownStatusID), Is.True);
            Assert.That(result.DerivedHits, Has.Count.EqualTo(1));
            Assert.That(result.DerivedHits[0].DamageElement, Is.EqualTo("Physical"));
            Assert.That(result.DerivedHits[0].ElementAmount, Is.Zero);
            Assert.That(result.TriggeredReactions[0].Type, Is.EqualTo(ReactionType.Shatter));
        }

        [Test]
        public void BuildPendingShatter_UsesCharacterAndEnemyFormulas()
        {
            ReactionContext context = NewContext("None", 0f, 100f);
            float resistance = ReactionDamageCalculator.GetResistanceMultiplier(_target, "Physical");

            PendingShatterEffect character = FrozenReactionHandler.BuildPendingShatter(context, 100f);
            float expectedCharacter = ReactionDamageCalculator.CalculateCharacterTransformative(
                100f, 3f, 200f, 0f, 0f, resistance);
            Assert.That(character.Damage, Is.EqualTo(expectedCharacter).Within(0.001f));

            _source.Type = BattleEntity.EntityType.Enemy;
            _source.DMGBonus = 0.2f;
            context.SourceKind = ReactionSourceKind.EnemySkill;
            PendingShatterEffect enemy = FrozenReactionHandler.BuildPendingShatter(context, 100f);
            float expectedEnemy = ReactionDamageCalculator.CalculateEnemyTransformative(
                100f, 3f, 0.2f, resistance);
            Assert.That(enemy.Damage, Is.EqualTo(expectedEnemy).Within(0.001f));
        }

        [Test]
        public void TryThaw_AllyCharacterConsumesTenAPAndClearsFrozenElement()
        {
            BattleEntity ally = NewEntity(
                "Frozen_Ally",
                BattleEntity.EntityType.Character,
                BattleSide.Ally);
            ReactionStateSystem.SetEntityState(
                ally,
                ReactionType.Frozen,
                2,
                new ReactionSourceSnapshot(),
                -1,
                true,
                1.5f);
            var actionPoints = new ActionPointManager();
            actionPoints.ResetAP();

            bool thawed = FrozenReactionHandler.TryThaw(ally, actionPoints);

            Assert.That(thawed, Is.True);
            Assert.That(actionPoints.CurrentAP, Is.EqualTo(90));
            Assert.That(FrozenReactionHandler.IsFrozen(ally), Is.False);
            Assert.That(ally.HasStatus("ST_Freeze"), Is.False);
        }

        private ReactionContext NewContext(string element, float amount, float poiseDamage)
        {
            return new ReactionContext
            {
                SourceEntity = _source,
                Target = _target,
                SourceKind = ReactionSourceKind.CharacterSkill,
                SourceEffectID = "SE_Frozen_Test",
                AttackElement = element,
                AttackAmount = amount,
                PreReactionDamage = 100f,
                PoiseDamage = poiseDamage
            };
        }

        private ReactionResult NewPendingShatterResult(float damage)
        {
            return new ReactionResult
            {
                PendingShatter = new PendingShatterEffect
                {
                    Source = _source,
                    Target = _target,
                    SourceEffectID = "SE_Shatter_Test",
                    Damage = damage
                }
            };
        }

        private BattleEntity NewEntity(
            string name,
            BattleEntity.EntityType type,
            BattleSide side)
        {
            BattleEntity entity = NewObject(name).AddComponent<BattleEntity>();
            entity.Type = type;
            entity.Side = side;
            entity.EntityID = type == BattleEntity.EntityType.Enemy ? 20001 : 10001;
            entity.Level = 80;
            entity.TotalHP = 10000f;
            entity.CurrentHP = 10000f;
            entity.MaxPoise = 100f;
            entity.Poise = 100f;
            return entity;
        }

        private GameObject NewObject(string name)
        {
            var gameObject = new GameObject(name);
            _objects.Add(gameObject);
            return gameObject;
        }

        private static void SetPhase(BattleManager battle, TurnPhase phase)
        {
            typeof(BattleManager)
                .GetProperty("CurrentPhase", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SetValue(battle, phase);
        }
    }
}
