using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>真实ReloadStage入口的构筑顺序；纯内存配表，测试只定义不自动运行。</summary>
    public sealed class BattleSetupInitializationTests
    {
        [Test]
        public void ReloadStage_RegistersBothFullRostersBeforeApplyingInitialStatuses()
        {
            using var env = new BattleFlowTestEnv();
            env.DataManager.CharacterAscensionDict[BattleFlowTestEnv.AllyID] = new List<CharacterAscensionData>
            { new CharacterAscensionData { AscensionLevel = 20, BaseHPFlat = 100f, ATKBonus = .2f } };
            var root = Main(env, 990690, "ST_SetupRoot");
            Main(env, 990691, "ST_SetupSelfHP", "HPBonus");
            Main(env, 990692, "ST_SetupPartyATK", "ATKBonus", true);
            Main(env, 990693, "ST_SetupEnemyMark", null, true);
            Effect(env, 99069001, "ApplyStatus", "ST_SetupSelfHP", "Self");
            Effect(env, 99069002, "BindStatus", "ST_SetupPartyATK", "Allies");
            Effect(env, 99069003, "BindStatus", "ST_SetupEnemyMark", "Enemy");
            Effect(env, 99069101, "Buff", ".2", "Self");
            Effect(env, 99069201, "Buff", ".1", "Self");
            env.DataManager.StatusActionDict[root.StatusID2] = new List<StatusActionData>
            {
                new StatusActionData { StatusID = root.StatusID, StatusID2 = root.StatusID2,
                    ActionType = "OnApply", Param1 = "STE_Setup_99069001" }
            };
            env.DataManager.TalentDict[BattleFlowTestEnv.AllyID] = new List<TalentData>
            {
                new TalentData { CharacterID = BattleFlowTestEnv.AllyID, TalentIndex = 1,
                    UnlockAfter = 1, Modifiers = "InitiateStatus(ST_SetupRoot)" }
            };
            var config = new StageSetupData { InitialEnergyMode = "Fixed", InitialEnergyValue = 13.25f };
            for (int i = 0; i < 4; i++)
                config.Allies.Add(new StageAllySetup { CharacterID = BattleFlowTestEnv.AllyID,
                    Level = 20, Constellation = i, IsAscended = i > 0 });
            config.Enemies.Add(new StageEnemySetup { EnemyID = BattleFlowTestEnv.EnemyID, Level = 1, Slot = 3 });
            var objects = new List<GameObject>();
            var runner = new GameObject("BattleSetupInitializationTest");
            try
            {
                runner.AddComponent<BattleTester>().ReloadStage(config);
                Assert.That(env.Manager.Allies, Has.Count.EqualTo(4));
                Assert.That(env.Manager.Enemies, Has.Count.EqualTo(1));
                for (int i = 0; i < env.Manager.Allies.Count; i++)
                {
                    var ally = env.Manager.Allies[i]; var entity = ally.Entity;
                    Assert.That(entity.SlotPosition, Is.EqualTo(i + 1));
                    Assert.That(entity.Position.Occupant, Is.SameAs(entity));
                    Assert.That(ally.IsActive, Is.EqualTo(i == 0));
                    Assert.That(entity.ConstellationLevel, Is.EqualTo(i));
                    Assert.That(entity.IsAscended, Is.EqualTo(i > 0));
                    Assert.That(entity.GetStatusInstances("ST_SetupSelfHP"), Has.Count.EqualTo(1),
                        "Self的OnApply必须找到已注册的本人，不能在SlotPosition=0时丢失");
                    Assert.That(entity.GetStatusInstances("ST_SetupPartyATK"), Has.Count.EqualTo(4),
                        "每个初始父状态都必须看到全队，而不是创建时的半队");
                    Assert.That(entity.GetStatusATKBonus("", "", -1, ""), Is.EqualTo(.1f).Within(.0001f),
                        "保留四个真实来源，但MaxCount=1只聚合一份");
                    Assert.That(entity.GetFinalHP(), Is.EqualTo(i == 0 ? 1200f : 1320f).Within(.001f));
                    Assert.That(entity.CurrentHP, Is.EqualTo(i == 0 ? 1200f : 1320f).Within(.001f));
                    Assert.That(entity.GetFinalATK("", "", -1, ""), Is.EqualTo(i == 0 ? 110f : 130f).Within(.001f));
                    Assert.That(entity.CurrentEnergy, Is.EqualTo(13.25f));
                    Assert.That(BattleAttributeInitialization.IsComplete(entity), Is.True);
                }
                Assert.That(env.Manager.Enemies[0].Entity.GetStatusInstances("ST_SetupEnemyMark"),
                    Has.Count.EqualTo(4), "敌方也必须在构筑前就绪，否则初始绑定会丢目标");
            }
            finally
            {
                foreach (var ally in env.Manager.Allies) if (ally != null) objects.Add(ally.gameObject);
                foreach (var enemy in env.Manager.Enemies) if (enemy != null) objects.Add(enemy.gameObject);
                env.Manager.ResetBattle();
                foreach (var gameObject in objects) if (gameObject != null) Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(runner);
            }
        }

        private static StatusMainData Main(BattleFlowTestEnv env, int id, string key, string part = null, bool independent = false)
        {
            var main = new StatusMainData { StatusID = id, StatusID2 = key, StatusType = "Buff",
                MultiplierPart1 = part, MaxStack = independent ? 0 : 1, MaxCount = 1 };
            env.DataManager.StatusMainDict[key] = main; return main;
        }

        private static void Effect(BattleFlowTestEnv env, int id, string type, string param, string target)
        {
            var key = "STE_Setup_" + id;
            env.DataManager.StatusEffectDict[key] = new StatusEffectData { StatusEffectID = id,
                StatusEffectID2 = key, EffectType = type, EffectIndex = id % 100, Param1 = param,
                TargetType = target, TargetSelect = target == "Self" ? null : "-1" };
        }
    }
}
