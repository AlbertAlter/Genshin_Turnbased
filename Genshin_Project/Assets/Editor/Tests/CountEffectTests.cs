using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class CountEffectTests
    {
        private const string IntegerID = "ST_Test_CountEffect_Integer";
        private const string FloatID = "ST_Test_CountEffect_Float";
        private const string PrivateID = "ST_Test_CountEffect_Private";
        private HookTestDataManager data;
        private BattleManager battle;
        private readonly List<GameObject> objects = new List<GameObject>();
        private IntegerHandler integerHandler;

        private sealed class IntegerHandler : CountStatusHandler
        {
            internal int Created, Reapplied;
            public override bool SupportsEffectChange => true;
            public override bool EffectUsesIntegerValue => true;
            public override void OnCreated(StatusInstance status, object host)
            {
                Created++;
                // Editor测试程序集不能直接调用运行时的internal setter。
                typeof(StatusInstance).GetProperty(nameof(StatusInstance.RuntimeIntValue)).SetValue(status, 1);
            }
            public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
            { Reapplied++; status.RemainingPhaseCount = 99; return status; }
            public override void ChangeFromEffect(StatusInstance status, BattleEntity host, CountChangeRequest request)
            {
                ValidateEffectChange(status, host, request);
                request.ApplyInteger(status, host, 0, host.ConstellationLevel == 1 ? 7 : 3);
            }
        }

        private sealed class FloatHandler : CountStatusHandler
        {
            public override bool SupportsEffectChange => true;
            public override void OnCreated(StatusInstance status, object host) { status.RuntimeValue = .25f; }
            public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
                => status;
            public override void ChangeFromEffect(StatusInstance status, BattleEntity host, CountChangeRequest request)
            {
                ValidateEffectChange(status, host, request);
                request.ApplyFloat(status, host, 0f, 4f);
            }
        }

        private sealed class PrivateHandler : CountStatusHandler
        {
            public override void OnCreated(StatusInstance status, object host) { status.RuntimeValue = 2f; }
            public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
                => status;
        }

        [SetUp]
        public void SetUp()
        {
            BattleEventHub.ResetSession();
            data = HookTestEnv.CreateDataManager(false);
            battle = HookTestEnv.CreateBattleManager();
            integerHandler = new IntegerHandler();
            Register(IntegerID, integerHandler, 9800001);
            Register(FloatID, new FloatHandler(), 9800002);
            Register(PrivateID, new PrivateHandler(), 9800003);
        }

        private void Register(string id, CountStatusHandler handler, int numeric)
        {
            typeof(CountStatusSystem).GetMethod("Register", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { id, handler });
            data.StatusMainDict[id] = new StatusMainData { StatusID = numeric, StatusID2 = id, StatusType = "Count" };
        }

        [TearDown]
        public void TearDown()
        {
            BattleEventHub.ResetSession();
            if (battle != null) battle.ResetBattle();
            foreach (var go in objects) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            objects.Clear();
            var handlers = (Dictionary<string, CountStatusHandler>)typeof(CountStatusSystem)
                .GetField("Handlers", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            foreach (string id in new[] { IntegerID, FloatID, PrivateID }) handlers.Remove(id);
            HookTestEnv.ClearBattleManagerSingleton();
            if (battle != null) UnityEngine.Object.DestroyImmediate(battle.gameObject);
            HookTestEnv.DestroyDataManager(data);
        }

        private BattleEntity Entity(int constellation = 0)
        {
            var go = new GameObject("CountEffect_Host"); objects.Add(go);
            var entity = go.AddComponent<BattleEntity>();
            entity.EntityID = 9801000 + objects.Count; entity.ConstellationLevel = constellation;
            entity.CurrentHP = entity.TotalHP = 100; entity.Side = BattleSide.Ally;
            return entity;
        }

        private StatusInstance Attach(BattleEntity host, string id = IntegerID)
            => host.AddStatus(id, host, 3, 2, 0, data.StatusMainDict[id]);

        [UnityTest]
        public IEnumerator AddFullClear_UseEachHostsBalanceAndCap_WithoutReapplyingOrRefreshing()
            => HookTestEnv.AwaitTask(FullClearAsync);
        private async Task FullClearAsync()
        {
            var first = Entity(); var second = Entity(1);
            var a = Attach(first); var b = Attach(second);
            long order = a.ApplyOrder;
            await BattleExecution.Run(async()=>
            { CountEffect.Execute("9800001", "1", first, new[] { first }); await BattleExecution.Flush(); });
            var facts = new List<ResourceEventData>();
            using (BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged, (_, fact) => facts.Add(fact), 0))
            {
                await BattleExecution.Run(async()=>
                { CountEffect.Execute(IntegerID, "full", first, new[] { first, second }); await BattleExecution.Flush(); });
                Assert.That(a.RuntimeIntValue, Is.EqualTo(3)); Assert.That(b.RuntimeIntValue, Is.EqualTo(7));
                Assert.That(facts.Count, Is.EqualTo(2));
                Assert.That(facts[0].Added, Is.EqualTo(1)); Assert.That(facts[1].Added, Is.EqualTo(6));
                await BattleExecution.Run(async()=>
                { CountEffect.Execute(IntegerID, "clear", first, new[] { first, second }); await BattleExecution.Flush(); });
                Assert.That(a.RuntimeIntValue, Is.Zero); Assert.That(b.RuntimeIntValue, Is.Zero);
                Assert.That(facts[2].Reason, Is.EqualTo(ResourceChangeReason.Clear));
                Assert.That(facts[2].Removed, Is.EqualTo(3)); Assert.That(facts[3].Removed, Is.EqualTo(7));
            }
            Assert.That(a.RemainingPhaseCount, Is.EqualTo(3)); Assert.That(a.ApplyOrder, Is.EqualTo(order));
            Assert.That(integerHandler.Created, Is.EqualTo(2)); Assert.That(integerHandler.Reapplied, Is.Zero);
            Assert.That(first.GetStatus(IntegerID), Is.SameAs(a));
            first.AddStatus(IntegerID, first, 3, 2, 0, data.StatusMainDict[IntegerID]);
            Assert.That(integerHandler.Reapplied, Is.EqualTo(1));
            Assert.That(a.RemainingPhaseCount, Is.EqualTo(99));
        }

        [UnityTest]
        public IEnumerator RepeatedTarget_IsNotDeduplicated_AndDeclaresActualDeltaAndActor()
            => HookTestEnv.AwaitTask(RepeatedTargetAsync);
        private async Task RepeatedTargetAsync()
        {
            var host = Entity(); var actor = Entity(); var status = Attach(host);
            var facts = new List<ResourceEventData>();
            using (BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged, (_, fact) => facts.Add(fact), 0))
            {
                await BattleExecution.Run(async()=>
                { CountEffect.Execute(IntegerID, "1", actor, new[] { host, host, host }); await BattleExecution.Flush(); });
                Assert.That(status.RuntimeIntValue, Is.EqualTo(3));
                Assert.That(facts.Count, Is.EqualTo(2)); // 第三次满值，没有伪造获得。
                Assert.That(facts[0].Subject, Is.SameAs(host)); Assert.That(facts[0].Actor, Is.SameAs(actor));
                await BattleExecution.Run(async()=>
                { CountEffect.Execute(IntegerID, "-10", actor, new[] { host }); await BattleExecution.Flush(); });
                Assert.That(facts[2].RequestedDelta, Is.EqualTo(-10));
                Assert.That(facts[2].Removed, Is.EqualTo(3));
                Assert.That(facts[2].Reason, Is.EqualTo(ResourceChangeReason.Spend));
            }
        }

        [Test]
        public void MissingInstance_IsNotCreated_AndFractionalIntegerRequestCannotChangeIt()
        {
            var host = Entity();
            CountEffect.Execute(IntegerID, "full", host, new[] { host });
            Assert.That(host.HasStatus(IntegerID), Is.False);
            var status = Attach(host);
            Assert.Throws<FormatException>(() => CountEffect.Execute(IntegerID, ".5", host, new[] { host }));
            Assert.That(status.RuntimeIntValue, Is.EqualTo(1));
        }

        [Test]
        public void FloatCounter_PreservesFraction_AndCapsWithoutRefreshing()
        {
            var host = Entity(); var status = Attach(host, FloatID);
            CountEffect.Execute(FloatID, ".5", host, new[] { host });
            Assert.That(status.RuntimeValue, Is.EqualTo(.75f));
            CountEffect.Execute(FloatID, "full", host, new[] { host });
            Assert.That(status.RuntimeValue, Is.EqualTo(4f));
            Assert.That(status.RemainingPhaseCount, Is.EqualTo(3));
        }

        [Test]
        public void IntegerCalculation_RemainsExactBeyondFloatPrecision_AndCannotOverflow()
        {
            Assert.That(CountChangeRequest.Parse("1").IntegerAfter(16777216, 0, int.MaxValue), Is.EqualTo(16777217));
            Assert.That(CountChangeRequest.Parse("2147483647").IntegerAfter(2, 0, int.MaxValue), Is.EqualTo(int.MaxValue));
            Assert.That(CountChangeRequest.Parse("-2147483648").IntegerAfter(1, 0, int.MaxValue), Is.Zero);
        }

        [TestCase("")]
        [TestCase("NaN")]
        [TestCase("Infinity")]
        [TestCase("1e100")]
        [TestCase("Gain3")]
        [TestCase("Consume")]
        [TestCase("1*Count(X)")]
        public void UnapprovedSyntax_IsRejected(string value)
            => Assert.Throws<FormatException>(() => CountChangeRequest.Parse(value));

        [Test]
        public void Definitions_RejectPrivateHandlersWrongTypesAndIntegerFractions()
        {
            var effect = new SkillEffectData { EffectType = "Count", SkillEffectID2 = "SE_Test_Count",
                Param1 = PrivateID, Param2 = "1", TargetType = "Self" };
            data.SkillEffectDict[effect.SkillEffectID2] = effect;
            Assert.Throws<TableValidationException>(() => CountEffect.ValidateDefinitions(data));
            effect.Param1 = IntegerID; effect.Param2 = ".5";
            Assert.Throws<TableValidationException>(() => CountEffect.ValidateDefinitions(data));
            effect.Param2 = "full";
            Assert.DoesNotThrow(() => CountEffect.ValidateDefinitions(data));
            data.StatusMainDict[IntegerID].StatusType = "NightSoul";
            Assert.Throws<TableValidationException>(() => CountEffect.ValidateDefinitions(data));
        }

        [Test]
        public void AmbiguousInstances_AreRejectedRatherThanSelectingFirst()
        {
            var host = Entity(); var first = Attach(host);
            host.StatusDict.Add(new StatusInstance { StatusID2 = IntegerID, MainData = data.StatusMainDict[IntegerID], IsActive = true });
            Assert.Throws<InvalidOperationException>(() => CountEffect.Execute(IntegerID, "1", host, new[] { host }));
            Assert.That(first.RuntimeIntValue, Is.EqualTo(1));
        }
    }
}
