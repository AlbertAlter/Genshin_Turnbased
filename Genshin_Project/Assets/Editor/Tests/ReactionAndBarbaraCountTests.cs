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
    public sealed class ReactionAndBarbaraCountTests
    {
        private HookTestDataManager data;
        private readonly List<GameObject> objects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            HookTestEnv.ResetStaticState();
            HookTestEnv.ClearBattleManagerSingleton();
            BattleEventHub.ResetSession();
            data = HookTestEnv.CreateDataManager(false);
        }

        [TearDown]
        public void TearDown()
        {
            BattleEventHub.ResetSession();
            foreach (GameObject obj in objects) UnityEngine.Object.DestroyImmediate(obj);
            objects.Clear();
            HookTestEnv.ResetStaticState();
            HookTestEnv.DestroyDataManager(data);
        }

        private BattleEntity Entity(int id, BattleSide side)
        {
            var obj = new GameObject("CounterTest_" + id);
            objects.Add(obj);
            var entity = obj.AddComponent<BattleEntity>();
            entity.EntityID = id;
            entity.Side = side;
            entity.CurrentHP = entity.TotalHP = 100f;
            return entity;
        }

        private static void Field(CharacterBattleController controller, string name, object value)
            => typeof(CharacterBattleController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(controller, value);

        private static void PublishHit(BattleEntity caster, BattleEntity target, bool landed = true,
            string effect = BarbaraScriptHooks.HeavyEffectID,
            ReactionSourceKind kind = ReactionSourceKind.CharacterSkill)
        {
            BattleEventHub.Publish(new DamageEventData(new DamageResolvedEvent
            {
                Source = DamageSourceInfo.Create(caster, kind, BarbaraScriptHooks.HeavySkillID, effect),
                Target = target, HitLanded = landed, HPBefore = 100f, HPAfter = 100f,
                ActualHPDamage = 0f, ShieldAbsorbed = 10f, DamageType = "Heavy", Element = "Hydro"
            }));
        }

        [UnityTest]
        public IEnumerator Barbara_CountsCommittedEnemiesOnceIncludingShieldAndDeath_WithoutFiveTargetCap()
            => HookTestEnv.AwaitTask(Barbara_CountsCommittedEnemiesOnceIncludingShieldAndDeath_WithoutFiveTargetCapAsync);

        private async Task Barbara_CountsCommittedEnemiesOnceIncludingShieldAndDeath_WithoutFiveTargetCapAsync()
        {
            var barbara = Entity(1014, BattleSide.Ally);
            barbara.ConstellationLevel = 4;
            await BattleExecution.Run(async () =>
            {
                using (BattleSpecialRules.BeginSkill(barbara))
                {
                    for (int i = 0; i < 6; i++)
                    {
                        var enemy = Entity(2000+i, BattleSide.Enemy);
                        PublishHit(barbara, enemy);
                        PublishHit(barbara, enemy); // 重复hit不重复计同一名敌人。
                        if (i == 0) enemy.CurrentHP = 0f; // 已提交的命中不因随后死亡丢失。
                    }
                    await BattleExecution.Flush();
                    Assert.That(ExecuteCountEvaluator.Evaluate(1, "1014_C4()", barbara), Is.EqualTo(6));
                    Assert.That(BarbaraScriptHooks.GetC4ExecuteCount(barbara), Is.EqualTo(6)); // 查询只读。
                }
                Assert.That(BarbaraScriptHooks.GetC4ExecuteCount(barbara), Is.Zero);
            });
        }

        [UnityTest]
        public IEnumerator Barbara_IgnoresMissesAlliesOtherEffectsAndDerivedDamage_AndIsolatesNestedSkills()
            => HookTestEnv.AwaitTask(Barbara_IgnoresMissesAlliesOtherEffectsAndDerivedDamage_AndIsolatesNestedSkillsAsync);

        private async Task Barbara_IgnoresMissesAlliesOtherEffectsAndDerivedDamage_AndIsolatesNestedSkillsAsync()
        {
            var barbara = Entity(1014, BattleSide.Ally);
            barbara.ConstellationLevel = 4;
            var enemy = Entity(2000, BattleSide.Enemy);
            await BattleExecution.Run(async () =>
            {
                using (BattleSpecialRules.BeginSkill(barbara))
                {
                    PublishHit(barbara, enemy, false);
                    PublishHit(barbara, barbara);
                    PublishHit(barbara, enemy, effect: "SE_Weapon_Test");
                    PublishHit(barbara, enemy, kind: ReactionSourceKind.DerivedReaction);
                    await BattleExecution.Flush();
                    Assert.That(BarbaraScriptHooks.GetC4ExecuteCount(barbara), Is.Zero);
                    using (BattleSpecialRules.BeginSkill(barbara))
                    {
                        PublishHit(barbara, enemy);
                        await BattleExecution.Flush();
                        Assert.That(BarbaraScriptHooks.GetC4ExecuteCount(barbara), Is.EqualTo(1));
                    }
                    Assert.That(BarbaraScriptHooks.GetC4ExecuteCount(barbara), Is.Zero);
                    using (BattleSpecialRules.BeginSkill(Entity(1011, BattleSide.Ally)))
                    {
                        PublishHit(barbara, enemy);
                        await BattleExecution.Flush();
                    }
                    Assert.That(BarbaraScriptHooks.GetC4ExecuteCount(barbara), Is.Zero);
                }
                barbara.ConstellationLevel = 3;
                using (BattleSpecialRules.BeginSkill(barbara))
                {
                    PublishHit(barbara, enemy);
                    await BattleExecution.Flush();
                    Assert.That(BarbaraScriptHooks.GetC4ExecuteCount(barbara), Is.Zero);
                }
            });
        }

        [Test]
        public void ThunderingFury_SpendsFourKeepsInstanceAndUsesCurrentlyBoundSkill()
        {
            var owner = Entity(1009, BattleSide.Ally);
            var controller = owner.gameObject.AddComponent<CharacterBattleController>();
            typeof(CharacterBattleController).GetProperty("Entity").SetValue(controller, owner);
            owner.CharacterCtrl = controller;
            var skills = new Dictionary<string, SkillMainData>
            {
                ["SK_ORIGINAL"] = new SkillMainData { SkillID2 = "SK_ORIGINAL" },
                ["SK_CURRENT"] = new SkillMainData { SkillID2 = "SK_CURRENT" }
            };
            var cooldowns = new Dictionary<string, int> { ["SK_ORIGINAL"] = 7, ["SK_CURRENT"] = 3 };
            Field(controller, "_skillById2", skills);
            Field(controller, "_cooldownRemaining", cooldowns);
            Field(controller, "_boundSkills", new[] { "", "", "SK_CURRENT", "" });
            HookTestEnv.InjectStatusEffect(data, ThunderingFuryCountHandler.PayoutEffectID,
                "ReduceCooldown", "Skill", "1");
            var main = new StatusMainData { StatusID2 = ThunderingFuryCountHandler.StatusID, StatusType = "Count" };
            var status = new StatusInstance { StatusID2 = main.StatusID2, MainData = main, IsActive = true };
            status.RecordSource(owner);
            var handler = new ThunderingFuryCountHandler();
            handler.OnCreated(status, owner);
            var application = new CountStatusApplication { Caster = owner, MainData = main };
            for (int cycle = 0; cycle < 2; cycle++)
            {
                int gains = cycle == 0 ? 3 : 4; // 首次创建已获1点，下一轮从0开始。
                for (int i = 0; i < gains; i++)
                    Assert.That(handler.OnReapplied(status, owner, application), Is.SameAs(status));
                Assert.That(status.RuntimeValue, Is.Zero);
                Assert.That(status.StackCount, Is.EqualTo(1));
                Assert.That(status.IsPermanent, Is.True);
            }
            Assert.That(cooldowns["SK_CURRENT"], Is.EqualTo(1));
            Assert.That(cooldowns["SK_ORIGINAL"], Is.EqualTo(7));
            Assert.That(CountStatusSystem.IsRegistered(main.StatusID2), Is.True);
        }

        [Test]
        public void ThunderingFury_ActionQuotaIsReservedForTheGain_NotForTheFourthPayout()
        {
            var owner = Entity(1009, BattleSide.Ally);
            var status = new StatusInstance { IsActive = true };
            status.RecordSource(owner);
            var gain = new StatusActionData { StatusID2 = "ST_Artifact_452206", ActionType = "OnTrigger",
                Param1 = "STE_Artifact_45220602", MaxTimePerAction = 1 };
            for (int i = 0; i < 4; i++)
            {
                using (BattleResolutionContext.EnterAction(new BattleResolutionContext.CounterScope()))
                using (BattleResolutionContext.BeginEffect())
                {
                    Assert.That(BattleResolutionContext.TryCollect(status, gain), Is.True);
                    Assert.That(BattleResolutionContext.TryCollect(status, gain), Is.False);
                }
            }
        }
    }
}
