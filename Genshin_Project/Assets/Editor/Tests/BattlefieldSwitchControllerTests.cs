using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class BattlefieldSwitchControllerTests
    {
        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
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
