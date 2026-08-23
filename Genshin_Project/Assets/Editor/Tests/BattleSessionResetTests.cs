using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public class BattleSessionResetTests
    {
        private GameObject _managerObject;
        private GameObject _allyObject;
        private BattleManager _manager;

        [SetUp]
        public void SetUp()
        {
            _managerObject = new GameObject("BattleManager_Reset_Test");
            _manager = _managerObject.AddComponent<BattleManager>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_allyObject != null) Object.DestroyImmediate(_allyObject);
            if (_managerObject != null) Object.DestroyImmediate(_managerObject);
            PreDamageHookSystem.ClearAll();
            BattleEntity._applyOrderCounter = 0;
        }

        [Test]
        public void ResetBattle_ClearsPreviousSessionState()
        {
            _allyObject = new GameObject("Ally_Reset_Test");
            var ally = _allyObject.AddComponent<CharacterBattleController>();
            var entity = _allyObject.AddComponent<BattleEntity>();
            ally.Entity = entity;
            entity.Side = BattleSide.Ally;
            entity.SlotPosition = 1;
            entity.TotalHP = 100f;
            entity.CurrentHP = 0f;
            _manager.RegisterAlly(ally);

            _manager.PendingActionTargetPositions.Add(3);
            _manager.Field.GetSlot(BattleSide.Ally, 1).StatusList.Add(new StatusInstance());
            BattleEntity._applyOrderCounter = 12;
            _manager.CheckBattleEnd();

            Assert.That(_manager.IsBattleOver, Is.True, "测试前应先进入失败状态");

            _manager.ResetBattle();

            Assert.That(_manager.IsBattleOver, Is.False);
            Assert.That(_manager.Victory, Is.False);
            Assert.That(_manager.TurnCount, Is.EqualTo(1));
            Assert.That(_manager.CurrentPhase, Is.EqualTo(TurnPhase.AllyPreTurn));
            Assert.That(_manager.APManager.CurrentAP, Is.EqualTo(_manager.APManager.MaxAP));
            Assert.That(_manager.PendingActionTargetPositions, Is.Empty);
            Assert.That(_manager.Allies, Is.Empty);
            Assert.That(_manager.Enemies, Is.Empty);
            Assert.That(_manager.Field.GetSlot(BattleSide.Ally, 1).Occupant, Is.Null);
            Assert.That(_manager.Field.GetSlot(BattleSide.Ally, 1).StatusList, Is.Empty);
            Assert.That(BattleEntity._applyOrderCounter, Is.Zero);
        }

        [Test]
        public void TargetSelector_Reset_ClearsSelection()
        {
            var selector = new TargetSelector();
            selector.Init(
                BattleSide.Enemy,
                1,
                1,
                _ => 100f,
                _ => true);

            Assert.That(selector.CurrentSelection, Is.Not.Empty);

            selector.Reset();

            Assert.That(selector.CurrentSelection, Is.Empty);
            Assert.That(selector.IsSelectionValid(), Is.False);
        }
    }
}
