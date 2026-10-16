using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>同ID独立实例回归；不读取策划源表。需用户手动运行Unity测试。</summary>
    public sealed class IndependentStatusInstanceTests
    {
        private HookTestDataManager _data;
        private BattleManager _battle;
        private BattleEntity _owner;
        private StatusMainData _main;

        [SetUp]
        public void SetUp()
        {
            _data = HookTestEnv.CreateDataManager(false);
            _battle = HookTestEnv.CreateBattleManager();
            _owner = new GameObject("Independent_Status_Owner").AddComponent<BattleEntity>();
            _owner.CurrentHP = _owner.TotalHP = 100f;
            _owner.Side = BattleSide.Ally;
            _main = new StatusMainData
            {
                StatusID = 69001, StatusID2 = "ST_Independent_Test", StatusType = "Buff",
                MaxStack = 3, WhenMax = "0", MultiplierPart1 = "ATKBonus"
            };
            _data.StatusMainDict[_main.StatusID2] = _main;
            _data.StatusEffectDict["STE_Independent_Test"] = new StatusEffectData
            {
                StatusEffectID = 6900101, StatusEffectID2 = "STE_Independent_Test",
                EffectType = "Buff", Param1 = "0.1"
            };
        }

        [TearDown]
        public void TearDown()
        {
            if (_battle != null) _battle.ResetBattle();
            if (_owner != null) Object.DestroyImmediate(_owner.gameObject);
            if (_battle != null) Object.DestroyImmediate(_battle.gameObject);
            HookTestEnv.ClearBattleManagerSingleton();
            HookTestEnv.DestroyDataManager(_data);
        }

        private StatusInstance Add(int duration = 3)
            => _owner.AddStatus(_main.StatusID2, _owner, duration, 2, 0, _main);

        private void Tick()
        {
            var method = typeof(BattleManager).GetMethod("ExecuteTimerSet", BindingFlags.NonPublic | BindingFlags.Instance);
            Drain((IEnumerator)method.Invoke(_battle, new object[] { TurnPhase.AllyAction }));
        }

        private static void Drain(IEnumerator routine)
        {
            while (routine.MoveNext())
                if (routine.Current is IEnumerator nested) Drain(nested);
        }

        [Test]
        public void Reapply_CreatesIndependentInstancesAndTimers()
        {
            var first = Add();
            first.RuntimeElement = "Pyro";
            Tick();
            var second = Add();
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(first.StackCount, Is.EqualTo(1));
            Assert.That(second.StackCount, Is.EqualTo(1));
            Assert.That(first.RemainingPhaseCount, Is.EqualTo(2));
            Assert.That(second.RemainingPhaseCount, Is.EqualTo(3));
            Assert.That(second.RuntimeElement, Is.Empty);
            Assert.That(_owner.GetStatusStackCount(_main.StatusID2), Is.EqualTo(2));
        }

        [Test]
        public void ThreeApplications_ExpireOneByOne()
        {
            var first = Add(); Tick();
            var second = Add(); Tick();
            var third = Add(); Tick();
            Assert.That(_owner.HasStatusInstance(first), Is.False);
            Assert.That(first.IsActive, Is.False);
            CollectionAssert.AreEqual(new[] { second, third }, _owner.GetStatusInstances(_main.StatusID2));
            Assert.That(second.RemainingPhaseCount, Is.EqualTo(1));
            Assert.That(third.RemainingPhaseCount, Is.EqualTo(2));
            Tick();
            CollectionAssert.AreEqual(new[] { third }, _owner.GetStatusInstances(_main.StatusID2));
            Tick();
            Assert.That(_owner.HasStatus(_main.StatusID2), Is.False);
        }

        [Test]
        public void MaxStack_OnlyReplacesOldestInstance()
        {
            var first = Add(8);
            var second = Add(6);
            var third = Add(4);
            var fourth = Add(3);
            Assert.That(first.IsActive, Is.False);
            CollectionAssert.AreEqual(new[] { second, third, fourth }, _owner.GetStatusInstances(_main.StatusID2));
            Assert.That(second.RemainingPhaseCount, Is.EqualTo(6));
            Assert.That(third.RemainingPhaseCount, Is.EqualTo(4));
            Assert.That(_owner.RemoveStatusInstance(first), Is.False);
            Assert.That(_owner.HasStatusInstance(fourth), Is.True);
        }

        [Test]
        public void WhenMaxReject_DoesNotRefreshExistingInstances()
        {
            _main.MaxStack = 1;
            _main.WhenMax = "1";
            var first = Add(2);
            var rejected = Add(99);
            Assert.That(rejected, Is.SameAs(first));
            Assert.That(first.RemainingPhaseCount, Is.EqualTo(2));
            Assert.That(_owner.GetStatusInstances(_main.StatusID2), Has.Count.EqualTo(1));
        }

        [Test]
        public void Removal_CanTargetOneInstanceOrEveryInstance()
        {
            var first = Add(); var second = Add(); var third = Add();
            Assert.That(_owner.RemoveStatus(_main.StatusID2, 1), Is.True);
            Assert.That(first.IsActive, Is.False);
            Assert.That(_owner.RemoveStatusInstance(third), Is.True);
            CollectionAssert.AreEqual(new[] { second }, _owner.GetStatusInstances(_main.StatusID2));
            Assert.That(_owner.RemoveStatus(_main.StatusID2), Is.True);
            Assert.That(_owner.GetStatusInstances(_main.StatusID2), Is.Empty);
        }

        [Test]
        public void MaxCount_CapsContributionNotStoredInstances()
        {
            _main.MaxCount = 2;
            Add(); Add(); Add();
            Assert.That(_owner.GetStatusATKBonus("", "", 0, "None"), Is.EqualTo(.2f).Within(.0001f));
            Assert.That(_owner.GetStatusInstances(_main.StatusID2), Has.Count.EqualTo(3));
        }

        [Test]
        public void FieldAndEntity_WithSameIDRemainSeparate()
        {
            var field = _battle.Field.AllySlots[0];
            _owner.Position = field;
            var onEntity = Add(5);
            var onField = field.AddStatus(_main.StatusID2, _owner, 1, 2, 0, _main);
            Assert.That(_owner.GetEffectiveStatusList(), Has.Count.EqualTo(2));
            Assert.That(_owner.GetStatusATKBonus("", "", 0, "None"), Is.EqualTo(.2f).Within(.0001f));
            Tick();
            Assert.That(field.StatusList.Contains(onField), Is.False);
            Assert.That(_owner.HasStatusInstance(onEntity), Is.True);
            Assert.That(onEntity.RemainingPhaseCount, Is.EqualTo(4));
        }

        [Test]
        public void Freeze_FirstExpiryDoesNotUnfreezeAnotherInstance()
        {
            var controller = _owner.gameObject.AddComponent<CharacterBattleController>();
            controller.Entity = _owner;
            _owner.CharacterCtrl = controller;
            _data.StatusEffectDict["STE_Independent_Freeze"] = new StatusEffectData
            {
                StatusEffectID = 6900102, StatusEffectID2 = "STE_Independent_Freeze",
                EffectType = "ChangeControl", Param1 = "Normal", Param2 = "Freeze"
            };
            var first = Add(1);
            var second = Add(3);
            Assert.That(controller.IsButtonFrozen(0), Is.True);
            Tick();
            Assert.That(first.IsActive, Is.False);
            Assert.That(second.IsActive, Is.True);
            Assert.That(controller.IsButtonFrozen(0), Is.True);
            _owner.RemoveStatusInstance(second);
            Assert.That(controller.IsButtonFrozen(0), Is.False);
        }

        [Test]
        public void Count_ReapplyStillUsesDedicatedHandler()
        {
            _main.StatusID = 4304903;
            _main.StatusID2 = SturdyBoneCountHandler.StatusID;
            _main.StatusType = "Count";
            _main.MaxStack = 1;
            var context = new WeaponRuntimeContext { WeaponID = 3049, Equipper = _owner };
            context.ResolvedParams[1] = .16f;
            var first = _owner.AddStatus(_main.StatusID2, _owner, 2, 2, 0, _main, context);
            first.RuntimeValue = 4;
            var next = _owner.AddStatus(_main.StatusID2, _owner, 2, 2, 0, _main, context);
            Assert.That(next, Is.Not.SameAs(first));
            Assert.That(first.IsActive, Is.False);
            Assert.That(next.RuntimeValue, Is.EqualTo(18));
            Assert.That(_owner.GetStatusInstances(_main.StatusID2), Has.Count.EqualTo(1));
        }
    }
}
