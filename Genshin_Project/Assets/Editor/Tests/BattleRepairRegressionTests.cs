using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public class BattleRepairRegressionTests
    {
        [TestCase(1)]
        [TestCase(2)]
        public void StatusAction_SemicolonListExecutesInOrderForEveryRound(int repeats)
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureStatus("ST_ListSource", actionType: "OnTrigger",
                    actionEffectID2: " STE_ListFirst ; ; STE_ListSecond; ");
                foreach (string suffix in new[] { "First", "Second" })
                {
                    env.ConfigureStatus("ST_List" + suffix, maxStack: 10);
                    env.DataManager.StatusEffectDict["STE_List" + suffix] = new StatusEffectData
                    {
                        StatusEffectID2 = "STE_List" + suffix, EffectType = "ApplyStatus",
                        Param1 = "ST_List" + suffix, TargetType = "Self"
                    };
                }
                var actions = env.DataManager.StatusActionDict["ST_ListSource"];
                actions[0].ExecuteCount = repeats;
                env.CreateMinimalBattle();
                var source = env.Ally.Entity.AddStatus("ST_ListSource", env.Ally.Entity, 0, 0, 0,
                    env.DataManager.StatusMainDict["ST_ListSource"]);
                BattleExecution.Run(() => env.Ally.ExecuteStatusActionsTask(
                    source, actions, env.Ally.Entity, "OnTrigger")).GetAwaiter().GetResult();

                Assert.That(env.Ally.Entity.GetStatusInstances("ST_ListFirst"), Has.Count.EqualTo(repeats));
                Assert.That(env.Ally.Entity.GetStatusInstances("ST_ListSecond"), Has.Count.EqualTo(repeats));
                var expectedOrder = new List<string>();
                for (int round = 0; round < repeats; round++)
                {
                    expectedOrder.Add("ST_ListFirst");
                    expectedOrder.Add("ST_ListSecond");
                }
                var actualOrder = new List<string>();
                foreach (StatusInstance instance in env.Ally.Entity.GetStatusList())
                {
                    if (instance.StatusID2 != "ST_ListFirst" && instance.StatusID2 != "ST_ListSecond") continue;
                    Assert.That(instance.StackCount, Is.EqualTo(1), "每次效果施加产生一份独立状态");
                    actualOrder.Add(instance.StatusID2);
                }
                CollectionAssert.AreEqual(expectedOrder, actualOrder,
                    "ExecuteCount 应按 First、Second、First、Second 重复整份效果列表");
                Assert.That(source.LifeTriggerCount, Is.EqualTo(1),
                    "ExecuteCount repeats the list, not the trigger itself.");
            }
        }

        [Test]
        public void HookFactory_UnitsBelongToManagerAndAreDestroyedWithIt()
        {
            GameObject characterObject;
            GameObject enemyObject;
            using (var env = new BattleFlowTestEnv())
            {
                var character = HookTestEnv.CreateCharacter(BattleFlowTestEnv.AllyID, 1, 1, env.Manager);
                var enemy = HookTestEnv.CreateEnemy(BattleFlowTestEnv.EnemyID, 1, 1, env.Manager);
                characterObject = character.gameObject;
                enemyObject = enemy.gameObject;
                Assert.That(characterObject.transform.parent, Is.SameAs(env.Manager.transform));
                Assert.That(enemyObject.transform.parent, Is.SameAs(env.Manager.transform));
            }
            Assert.That(characterObject == null, Is.True, "销毁测试管理器时必须一并销毁工厂创建的角色");
            Assert.That(enemyObject == null, Is.True, "销毁测试管理器时必须一并销毁工厂创建的敌人");
        }

        [TestCase(" 101 ;STE_Custom; 102 ", "STE_Weapon_101;STE_Custom;STE_Weapon_102")]
        [TestCase("101", "STE_Weapon_101")]
        [TestCase("STE_Custom", "STE_Custom")]
        [TestCase("", "")]
        [TestCase(null, null)]
        public void WeaponStatusAction_NormalizesSemicolonList(string input, string expected)
        {
            var normalize = typeof(DataManager).GetMethod("NormalizeWeaponEffectList",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(normalize, Is.Not.Null);
            Assert.That(normalize.Invoke(null, new object[] { input }), Is.EqualTo(expected));
        }

        [Test]
        public void EffectOverrides_InheritBlanksPreserveZeroAndDoNotMutateDefinition()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var original = new SkillEffectData { SkillEffectID2 = "SE_Base", EffectType = "Damage",
                    Duration = 5, TargetNumber = 3, Element = "Pyro", Param1 = "1*ATK,1",
                    Param2 = "Splash(0.5;1,1)", ScriptHook = "Check(1009_C1)" };
                var call = new SkillEffectData { EffectType = "ExecuteEffect", Param1 = "SE_Base",
                    Param2 = "2*ATK,0", Duration = 0, ScriptHook = "Check(1009_C2)",
                    AuthoredFields = new HashSet<string> { "Param1", "Param2", "Duration", "ScriptHook" } };
                var result = env.Ally.CreateCalledEffect(original, call);
                Assert.That(result.Duration, Is.EqualTo(0));
                Assert.That(result.TargetNumber, Is.EqualTo(3));
                Assert.That(result.Param1, Is.EqualTo("2*ATK,0"));
                Assert.That(result.Param2, Is.EqualTo(original.Param2));
                Assert.That(result.ScriptHook, Is.Null, "覆盖的条件已由调用行求值，不应再次判断");
                Assert.That(original.Duration, Is.EqualTo(5));
                Assert.That(original.Param1, Is.EqualTo("1*ATK,1"));
                var stateCall = new StatusEffectData { Param1 = "SE_Base", Element = "Cryo",
                    Param2 = "3*ATK,0", Param3 = "Splash(0.2;1,1)" };
                result = env.Ally.CreateCalledEffect(original, stateCall);
                Assert.That(result.Element, Is.EqualTo("Cryo"));
                Assert.That(result.Param1, Is.EqualTo("3*ATK,0"));
                Assert.That(result.Param2, Is.EqualTo("Splash(0.2;1,1)"));
            }
        }

        [TestCase("Physical")]
        [TestCase("Anemo")]
        public void DirectDamage_MultipleBasesWithoutLevelRows_AreAppliedOnce(string element)
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(2, "SK_Direct", "SE_Direct", element, null,
                    param1: "1*TotalATK,0;0.2*TotalHP,0");
                env.CreateMinimalBattle();
                env.Ally.ForcedTargetPositions.Add(1);
                BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_Direct", 2, true))
                    .GetAwaiter().GetResult();
                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(850f).Within(0.01f));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        [Timeout(9000)]
        public void OnEndCanExtendEntityOrFieldStatusOnceThenExpire(bool field)
        {
            using (var env = new BattleFlowTestEnv())
            {
                const string id = "ST_Extend";
                env.ConfigureStatus(id, actionType: "OnEnd", actionEffectID2: "STE_Extend");
                env.DataManager.StatusActionDict[id][0].MaxTimePerLife = 1;
                env.DataManager.StatusEffectDict["STE_Extend"] = new StatusEffectData
                { StatusEffectID2 = "STE_Extend", EffectType = "AddTurn", Param1 = id, Param2 = "1", TargetType = "Self" };
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                var slot = env.Manager.Field.GetSlot(BattleSide.Ally, 1);
                StatusInstance state;
                if (field)
                {
                    state = new StatusInstance { StatusID2 = id, MainData = env.DataManager.StatusMainDict[id],
                        IsActive = true, RemainingPhaseCount = 1, AddInPhase = 3, ApplyOrder = ++BattleEntity._applyOrderCounter };
                    state.RecordSource(env.Ally.Entity);
                    slot.StatusList.Add(state);
                    env.Manager.RegisterStatusTick(state, null, slot);
                }
                else state = env.Ally.Entity.AddStatus(id, env.Ally.Entity, 1, 3, 0, env.DataManager.StatusMainDict[id]);
                env.Manager.EndAllyTurn();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);
                Assert.That(state.RemainingPhaseCount, Is.EqualTo(1));
                Assert.IsTrue(field ? slot.StatusList.Contains(state) : env.Ally.Entity.GetStatus(id) == state);
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Manager.EndAllyTurn();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);
                Assert.IsFalse(field ? slot.StatusList.Contains(state) : env.Ally.Entity.GetStatus(id) == state);
            }
        }

        [Test]
        public void Healing_AddsOutgoingAndIncomingBonusesAndHonorsEffectFilter()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(2, "SK_Heal", "SE_Heal", "Hydro", null,
                    effectType: "Heal", param1: "100", targetType: "Self");
                env.CreateMinimalBattle();
                env.Ally.Entity.CurrentHP = 100;
                env.Ally.Entity.HealBonus = 0.1f;
                env.Ally.Entity.BeHealedBonus = 0.2f;
                AddHealingBuff(env, "ST_Outgoing", 990011, "HealBonus", "0.3", "SE_Heal");
                AddHealingBuff(env, "ST_Incoming", 990012, "BeHealedBonus", "0.4", "SE_Heal");
                AddHealingBuff(env, "ST_Unrelated", 990013, "HealBonus", "10", "SE_Other");
                BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_Heal", 2, true))
                    .GetAwaiter().GetResult();
                Assert.That(env.Ally.Entity.CurrentHP, Is.EqualTo(300f).Within(0.01f));
            }
        }

        private static void AddHealingBuff(BattleFlowTestEnv env, string id, int numericID, string part, string value, string effect)
        {
            var main = new StatusMainData { StatusID = numericID, StatusID2 = id,
                MultiplierPart1 = part, ApplyString = effect, MaxStack = 1 };
            env.DataManager.StatusMainDict[id] = main;
            env.DataManager.StatusEffectDict[id + "_Buff"] = new StatusEffectData
            { StatusEffectID = numericID * 100 + 1, EffectType = "Buff", Param1 = value };
            env.Ally.Entity.AddStatus(id, env.Ally.Entity, 0, 0, 0, main);
        }

        [Test]
        public void DuplicateManagerDestroyDoesNotCancelActiveResolution()
        {
            var extra = new GameObject("DuplicateManagerTest");
            HookTestEnv.ClearBattleManagerSingleton();
            var duplicate = extra.AddComponent<BattleManager>();
            try
            {
                using (var env = new BattleFlowTestEnv())
                {
                    BattleExecution.Run(() =>
                    {
                        typeof(BattleManager).GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance)
                            .Invoke(duplicate, null);
                        Assert.IsFalse(BattleExecution.IsCancelled);
                        Assert.That(BattleManager.Instance, Is.SameAs(env.Manager));
                        return Task.CompletedTask;
                    }).GetAwaiter().GetResult();
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(extra); }
        }

        [Test]
        public void PermanentParentBindsPermanentFieldChildAndExtensionTraversesChildrenOnce()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureStatus("ST_Parent"); env.ConfigureStatus("ST_Child");
                env.DataManager.StatusMainDict["ST_Parent"].StatusID = 990014;
                env.DataManager.StatusEffectDict["STE_BindField"] = new StatusEffectData
                { StatusEffectID = 99001401, EffectType = "BindStatus", Param1 = "ST_Child", TargetType = "AllyField", AddInPhase = 3 };
                env.CreateMinimalBattle();
                var parent = env.Ally.Entity.AddStatus("ST_Parent", env.Ally.Entity, 0, 3, 0,
                    env.DataManager.StatusMainDict["ST_Parent"]);
                Assert.That(parent.BoundStatuses.Count, Is.GreaterThan(0));
                Assert.IsTrue(parent.BoundStatuses[0].IsPermanent);
                var finite = new StatusInstance { RemainingPhaseCount = 0 };
                var child = new StatusInstance { RemainingPhaseCount = 0 };
                finite.BoundStatuses.Add(child); child.BoundStatuses.Add(finite);
                StatusBindingSystem.ExtendDuration(finite, 1);
                Assert.That(finite.RemainingPhaseCount, Is.EqualTo(1));
                Assert.That(child.RemainingPhaseCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void ModifierSeparatorsRespectNestedArgumentsAndAllowTrailingSemicolon()
        {
            string text = "ModifyEffect(SE_Burst_Kaeya, Param1=ST_Kaeya_C6); \n" +
                "InsertEffect(1; SE_Burst_Kaeya; GainEnergy; Param1=15; Param2=Flat; TargetType=Self);";
            var commands = CharacterBuildApplier.SplitModifierCommands(text);
            Assert.That(commands.Count, Is.EqualTo(2));
            var split = typeof(CharacterBuildApplier).GetMethod("SplitArgs", BindingFlags.Static | BindingFlags.NonPublic);
            var args = (List<string>)split.Invoke(null, new object[] { "SE_Burst_Kaeya, Param1=ST_Kaeya_C6" });
            CollectionAssert.AreEqual(new[] { "SE_Burst_Kaeya", "Param1=ST_Kaeya_C6" }, args);
            args = (List<string>)split.Invoke(null, new object[] { "SE_X; Param2=Splash(0.5;1,1); ScriptHook=Check(1009_C1);HitLanded(SE_X)" });
            Assert.That(args.Count, Is.EqualTo(3));
            Assert.That(args[1], Is.EqualTo("Param2=Splash(0.5;1,1)"));
            Assert.That(args[2], Is.EqualTo("ScriptHook=Check(1009_C1);HitLanded(SE_X)"));
            Assert.Throws<FormatException>(() => CharacterBuildApplier.SplitModifierCommands("ModifyEffect(SE_X"));
        }

        [Test]
        public void KaeyaStyleTwoCommandsBothApplyWithoutChangingSourceDefinition()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(3, "SK_Burst_Kaeya", "SE_Burst_Kaeya", "Cryo", null,
                    effectType: "ApplyStatus", param1: "ST_Base");
                env.CreateMinimalBattle();
                string commands = "ModifyEffect(SE_Burst_Kaeya, Param1=ST_Kaeya_C6); " +
                    "InsertEffect(1; SE_Burst_Kaeya; GainEnergy; Param1=15; Param2=Flat; TargetType=Self)";
                typeof(CharacterBuildApplier).GetMethod("ApplyModifiers", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { env.Ally, commands });
                var effects = env.Ally.GetSkillEffects("SK_Burst_Kaeya");
                Assert.That(effects.Count, Is.EqualTo(2));
                Assert.That(effects[0].Param1, Is.EqualTo("ST_Kaeya_C6"));
                Assert.That(effects[1].EffectType, Is.EqualTo("GainEnergy"));
                Assert.That(effects[1].Param1, Is.EqualTo("15"));
                Assert.That(effects[1].Param2, Is.EqualTo("Flat"));
                Assert.That(env.DataManager.SkillEffectDict["SE_Burst_Kaeya"].Param1, Is.EqualTo("ST_Base"));
            }
        }
    }
}
