using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class BattlefieldSwitchControllerTests
    {
        private EditorSceneTestScope _scenes;

        [SetUp]
        public void SetUp()
        {
            _scenes = new EditorSceneTestScope();
        }

        [TearDown]
        public void TearDown()
        {
            _scenes?.Dispose();
            _scenes = null;
        }

        [Test]
        [Timeout(9000)]
        public void SwitchHooks_UseCommittedFieldStateAndRespectPerTurnLimit()
        {
            using (var env = new BattleFlowTestEnv())
            {
                const string listener = "ST_Lisa_C6";
                const string mark = "ST_Lisa_Conductive";
                env.ConfigureStatus(mark, maxStack: 3);
                env.ConfigureStatus(listener, actionType: "OnTrigger", actionEffectID2: "STE_SwitchMark");
                var action = env.DataManager.StatusActionDict[listener][0];
                action.ScriptHook = "SwitchIn(1011);OnField(1011);OffField(1009)";
                action.MaxTimePerTurn = 1;
                env.DataManager.StatusEffectDict["STE_SwitchMark"] = new StatusEffectData
                {
                    StatusEffectID2 = "STE_SwitchMark", EffectType = "ApplyStatus", Param1 = mark,
                    TargetType = "Enemy", TargetSelect = "-1", Duration = 0
                };
                env.ConfigureStatus("ST_SwitchOut", actionType: "OnTrigger", actionEffectID2: "STE_SwitchEnergy");
                env.DataManager.StatusActionDict["ST_SwitchOut"][0].ScriptHook = "SwitchOut(1009)";
                env.DataManager.StatusEffectDict["STE_SwitchEnergy"] = new StatusEffectData
                {
                    StatusEffectID2 = "STE_SwitchEnergy", EffectType = "GainEnergy", Param1 = "1",
                    Param2 = "Flat", TargetType = "Self"
                };
                env.CreateMinimalBattle();
                env.Ally.Entity.EntityID = 1009;
                env.Ally.Entity.CurrentEnergy = 0;
                var lisa = env.CreateAlly(2);
                lisa.Entity.EntityID = 1011;
                var secondEnemy = env.CreateEnemy(2);
                lisa.Entity.AddStatus(listener, lisa.Entity, 0, 2, 0, env.DataManager.StatusMainDict[listener]);
                env.Ally.Entity.AddStatus("ST_SwitchOut", env.Ally.Entity, 0, 2, 0,
                    env.DataManager.StatusMainDict["ST_SwitchOut"]);
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                Assert.That(env.Enemy.Entity.HasStatus(mark), Is.False, "初始化出战状态不等于切换上场");
                Assert.That(ScriptHookEvaluator.IsEventDriven("SwitchIn(1011)"), Is.True);
                Assert.That(ScriptHookEvaluator.Evaluate("SwitchIn(1011)", lisa.Entity), Is.False);
                Assert.That(ScriptHookEvaluator.Evaluate("OffField(1011)", lisa.Entity), Is.True);
                Assert.That(ScriptHookEvaluator.Evaluate("OffField(999999)", lisa.Entity), Is.False);

                int beforeAP = env.Manager.APManager.CurrentAP;
                Assert.That(env.Manager.TrySwitchActiveAllyWithAPCost(1), Is.True);
                Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(beforeAP - BattleManager.SwitchAPCost));
                Assert.That(env.Enemy.Entity.GetStatusStackCount(mark), Is.EqualTo(1));
                Assert.That(secondEnemy.Entity.GetStatus(mark).IsPermanent, Is.True);
                Assert.That(env.Ally.Entity.CurrentEnergy, Is.EqualTo(1));
                Assert.That(ScriptHookEvaluator.Evaluate("OnField(1011)", lisa.Entity), Is.True);
                Assert.That(env.Manager.TrySwitchActiveAllyWithAPCost(1), Is.False);
                Assert.That(env.Manager.TrySwitchActiveAlly(0), Is.True);
                Assert.That(env.Manager.TrySwitchActiveAlly(1), Is.True);
                Assert.That(env.Enemy.Entity.GetStatusStackCount(mark), Is.EqualTo(1), "本回合上场第二次不再施加");
                Assert.That(env.Ally.Entity.CurrentEnergy, Is.EqualTo(2));

                int turn = env.Manager.TurnCount;
                env.Manager.EndAllyTurn();
                env.Flow.AdvanceUntil(env.Manager,
                    () => env.Manager.TurnCount > turn && env.Manager.CurrentPhase == TurnPhase.AllyAction
                        && env.Manager.IsCurrentPhaseReady, "next ally turn");
                Assert.That(env.Enemy.Entity.HasStatus(mark), Is.True, "永久引雷不能因阶段推进过期");
                Assert.That(env.Manager.TrySwitchActiveAlly(0), Is.True);
                env.Manager.APManager.ConsumeAP(env.Manager.APManager.CurrentAP);
                Assert.That(env.Manager.TrySwitchActiveAllyWithAPCost(1), Is.False);
                Assert.That(env.Ally.IsActive, Is.True);
                Assert.That(env.Enemy.Entity.GetStatusStackCount(mark), Is.EqualTo(1));
                Assert.That(env.Manager.TrySwitchActiveAlly(1), Is.True);
                Assert.That(env.Enemy.Entity.GetStatusStackCount(mark), Is.EqualTo(2));
                Assert.That(env.Enemy.Entity.GetStatusInstances(mark), Has.Count.EqualTo(2));
                foreach (var instance in env.Enemy.Entity.GetStatusInstances(mark))
                    Assert.That(instance.StackCount, Is.EqualTo(1), "每次施加保留独立状态实例，不合并旧实例层数");
            }
        }

        [Test]
        [Timeout(5000)]
        public void NormalSwitch_UsesModalSelectionAndConsumesAP()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                CharacterBattleController reserve = env.CreateAlly(2, false);
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);

                GameObject runtime = new GameObject("BattleRuntime_InputTest");
                try
                {
                    BattleInputController input = runtime.AddComponent<BattleInputController>();
                    BattlefieldSwitchController switcher = runtime.AddComponent<BattlefieldSwitchController>();
                    BindTestControls(switcher, input);
                    int apBefore = env.Manager.APManager.CurrentAP;

                    Assert.That(switcher.UIBeginSwitch(), Is.True);
                    Assert.That(switcher.IsSwitching, Is.True);
                    Assert.That(switcher.SelectedSlot, Is.EqualTo(0));
                    Assert.That(input.enabled, Is.False);

                    switcher.UIMoveSwitch(1);
                    Assert.That(switcher.SelectedSlot, Is.EqualTo(1));
                    Assert.That(switcher.UIConfirmSwitch(), Is.True);
                    Assert.That(reserve.IsActive, Is.True);
                    Assert.That(env.Manager.APManager.CurrentAP,
                        Is.EqualTo(apBefore - BattleManager.SwitchAPCost));

                    // 输入延迟至 LateUpdate 恢复，避免确认用的 Space 同帧结束回合。
                    Assert.That(input.enabled, Is.False);
                    InvokeLifecycle(switcher, "LateUpdate");
                    Assert.That(input.enabled, Is.True);
                }
                finally
                {
                    Object.DestroyImmediate(runtime);
                }
            }
        }

        [Test]
        [Timeout(5000)]
        public void DeathSwitch_AutoEntersCannotCancelAndIsFree()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                CharacterBattleController reserve = env.CreateAlly(2, false);
                env.Ally.Entity.CurrentHP = 0f;
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);

                GameObject runtime = new GameObject("BattleRuntime_DeathSwitchTest");
                try
                {
                    BattleInputController input = runtime.AddComponent<BattleInputController>();
                    BattlefieldSwitchController switcher = runtime.AddComponent<BattlefieldSwitchController>();
                    BindTestControls(switcher, input);
                    int apBefore = env.Manager.APManager.CurrentAP;

                    InvokeLifecycle(switcher, "Update");
                    Assert.That(switcher.IsSwitching, Is.True);
                    Assert.That(switcher.IsForcedDeathSwitch, Is.True);
                    Assert.That(switcher.SelectedSlot, Is.EqualTo(1));
                    Assert.That(input.enabled, Is.False);

                    switcher.UICancelSwitch();
                    Assert.That(switcher.IsSwitching, Is.True);
                    Assert.That(switcher.UIConfirmSwitch(), Is.True);
                    Assert.That(reserve.IsActive, Is.True);
                    Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(apBefore));
                }
                finally
                {
                    Object.DestroyImmediate(runtime);
                }
            }
        }

        private static void BindTestControls(BattlefieldSwitchController controller, BattleInputController input)
        {
            // 保留用户场景时不能依赖全局查找；只使用该测试的输入和按钮。
            var hitArea = new GameObject("TestSwitchButton", typeof(RectTransform), typeof(Image), typeof(Button));
            hitArea.transform.SetParent(controller.transform, false);
            foreach (var binding in new[]
            {
                new { Name = "_input", Value = (object)input },
                new { Name = "_button", Value = (object)hitArea.GetComponent<Button>() }
            })
            {
                FieldInfo field = typeof(BattlefieldSwitchController).GetField(binding.Name,
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null);
                field.SetValue(controller, binding.Value);
            }
        }

        private static void InvokeLifecycle(BattlefieldSwitchController controller, string methodName)
        {
            MethodInfo method = typeof(BattlefieldSwitchController).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(controller, null);
        }
    }
}
