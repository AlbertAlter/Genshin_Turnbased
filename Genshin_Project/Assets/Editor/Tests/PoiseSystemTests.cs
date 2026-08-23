using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public class PoiseSystemTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
                Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
        }

        [Test]
        public void ApplyPoiseDamage_UsesBreakPhaseAndAppliesCharacterStatus()
        {
            BattleManager battle = NewObject("Poise_Battle").AddComponent<BattleManager>();
            SetPhase(battle, TurnPhase.EnemyAction);
            BattleEntity target = NewEntity(BattleEntity.EntityType.Character, 100f);

            bool knockedDown = PoiseSystem.ApplyPoiseDamage(target, 120f, 1f);

            Assert.That(knockedDown, Is.True);
            Assert.That(target.Poise, Is.Zero);
            Assert.That(target.HasStatus(PoiseSystem.CharacterKnockdownStatusID), Is.True);
            Assert.That(target.GetStatus(PoiseSystem.CharacterKnockdownStatusID).AddInPhase,
                Is.EqualTo((int)TurnPhase.EnemyAction));
        }

        [Test]
        public void ApplyPoiseDamage_NoActualHpDamageDoesNotReducePoise()
        {
            BattleEntity target = NewEntity(BattleEntity.EntityType.Character, 100f);

            bool knockedDown = PoiseSystem.ApplyPoiseDamage(target, 100f, 0f);

            Assert.That(knockedDown, Is.False);
            Assert.That(target.Poise, Is.EqualTo(100f));
            Assert.That(PoiseSystem.IsKnockedDown(target), Is.False);
        }

        [Test]
        public void TryStandUp_ConsumesTenApAndActivelyRemovesStatus()
        {
            BattleEntity target = NewEntity(BattleEntity.EntityType.Character, 100f);
            PoiseSystem.BreakPoise(target);
            var actionPoints = new ActionPointManager();
            actionPoints.ResetAP();

            bool stoodUp = PoiseSystem.TryStandUp(target, actionPoints);

            Assert.That(stoodUp, Is.True);
            Assert.That(actionPoints.CurrentAP, Is.EqualTo(90));
            Assert.That(target.HasStatus(PoiseSystem.CharacterKnockdownStatusID), Is.False);
        }

        [Test]
        public void ConsumeEnemyFirstAction_RemovesEnemyStatusAndSkipsOnlyOnce()
        {
            BattleEntity target = NewEntity(BattleEntity.EntityType.Enemy, 100f);
            PoiseSystem.BreakPoise(target);

            Assert.That(PoiseSystem.ConsumeEnemyFirstAction(target), Is.True);
            Assert.That(target.HasStatus(PoiseSystem.EnemyKnockdownStatusID), Is.False);
            Assert.That(PoiseSystem.ConsumeEnemyFirstAction(target), Is.False);
        }

        [Test]
        public void RestoreAtTurnStart_DoesNotRestoreWhileKnockedDown()
        {
            BattleEntity target = NewEntity(BattleEntity.EntityType.Enemy, 100f);
            PoiseSystem.BreakPoise(target);

            PoiseSystem.RestoreAtTurnStart(target);
            Assert.That(target.Poise, Is.Zero);

            target.RemoveStatus(PoiseSystem.EnemyKnockdownStatusID);
            PoiseSystem.RestoreAtTurnStart(target);
            Assert.That(target.Poise, Is.EqualTo(100f));
        }

        [Test]
        public void ApplyPoiseDamage_NegativeMaxPoiseIsUnbreakable()
        {
            BattleEntity target = NewEntity(BattleEntity.EntityType.Enemy, -1f);
            target.Poise = -1f;

            bool knockedDown = PoiseSystem.ApplyPoiseDamage(target, 999f, 100f);

            Assert.That(knockedDown, Is.False);
            Assert.That(target.Poise, Is.EqualTo(-1f));
            Assert.That(PoiseSystem.IsKnockedDown(target), Is.False);
        }

        private BattleEntity NewEntity(BattleEntity.EntityType type, float maxPoise)
        {
            BattleEntity entity = NewObject($"Poise_{type}").AddComponent<BattleEntity>();
            entity.Type = type;
            entity.EntityID = type == BattleEntity.EntityType.Enemy ? 20001 : 10001;
            entity.TotalHP = 1000f;
            entity.CurrentHP = 1000f;
            entity.MaxPoise = maxPoise;
            entity.Poise = maxPoise;
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
