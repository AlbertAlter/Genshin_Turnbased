using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class BarbaraRingTests
    {
        private static StatusMainData Main(BattleFlowTestEnv env, int id, string name, string type = null)
        {
            var main = new StatusMainData { StatusID = id, StatusID2 = name, StatusType = type, MaxStack = 1, MaxCount = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[name] = main;
            return main;
        }

        private static void UnlockForMechanismTest(BattleFlowTestEnv env)
        {
            // 解锁门槛由共用CharacterAscensionRules测试覆盖；这里显式解锁以隔离环本身的机制。
            env.DataManager.TalentDict[1014] = new List<TalentData>
            {
                new TalentData { TalentIndex = 1, UnlockAfter = 0 },
                new TalentData { TalentIndex = 2, UnlockAfter = 0 }
            };
        }

        [Test]
        public void SwitchDiscount_BindsPartyAndCountsOnlyOutgoing_ThenDisappearsWithRing()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); UnlockForMechanismTest(env);
            var owner = env.Ally.Entity; owner.EntityID = 1014;
            var second = env.CreateAlly(2).Entity; var third = env.CreateAlly(3).Entity;
            var ring = Main(env, 4101401, "ST_Barbara_Ring");
            Main(env, 4101402, "ST_Barbara_T1", "Buff");
            env.DataManager.StatusEffectDict["STE_Barbara_RingT1"] = new StatusEffectData
            { StatusEffectID = 410140101, StatusEffectID2 = "STE_Barbara_RingT1", EffectType = "BindStatus",
                Param1 = "ST_Barbara_T1", TargetType = "Allies", ScriptHook = "Check(1014_T1)" };
            env.DataManager.StatusEffectDict["STE_Barbara_T1"] = new StatusEffectData
            { StatusEffectID = 410140201, StatusEffectID2 = "STE_Barbara_T1", EffectType = "APCost", Param1 = "-5", Param2 = "SwitchOut" };
            var parent = owner.AddStatus(ring.StatusID2, owner, 5, 2, 3, ring);
            Assert.That(APCostResolver.ForSwitch(second, third, 20, false), Is.EqualTo(15), "不是只给芭芭拉本人，也不能双方各减5");
            Assert.That(APCostResolver.ForSkill(second, new SkillMainData { APCost = 20 }, 1), Is.EqualTo(20));
            owner.RemoveStatusInstance(parent);
            Assert.That(APCostResolver.ForSwitch(second, third, 20, false), Is.EqualTo(20));
            Assert.That(second.HasStatus("ST_Barbara_T1"), Is.False);
            env.DataManager.TalentDict[1014].Clear();
            owner.AddStatus(ring.StatusID2, owner, 5, 2, 3, ring);
            Assert.That(second.HasStatus("ST_Barbara_T1"), Is.False, "没有解锁天赋时不绑定减费");
        }

        [Test]
        public void EndExtension_UsesCurrentFractionalEnergy_RetainsSameBindings_AndStopsWhenFull()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); UnlockForMechanismTest(env);
            var owner = env.Ally.Entity; owner.EntityID = 1014; owner.MaxEnergy = 80; owner.CurrentEnergy = 79.5f;
            var main = Main(env, 4101401, "ST_Barbara_Ring");
            Main(env, 4101408, "ST_Barbara_RingHit");
            env.DataManager.StatusEffectDict["STE_Barbara_RingHit"] = new StatusEffectData
            { StatusEffectID = 410140104, StatusEffectID2 = "STE_Barbara_RingHit", EffectType = "BindStatus",
                Param1 = "ST_Barbara_RingHit", TargetType = "Self" };
            env.DataManager.StatusEffectDict["STE_Barbara_T2"] = new StatusEffectData
            { StatusEffectID = 410140103, StatusEffectID2 = "STE_Barbara_T2", EffectType = "AddTurn",
                Param1 = main.StatusID2, Param2 = "1", TargetType = "Self" };
            env.DataManager.StatusActionDict[main.StatusID2] = new List<StatusActionData>
            { new StatusActionData { StatusID2 = main.StatusID2, ActionType = "OnEnd", Param1 = "STE_Barbara_T2",
                ScriptHook = "Check(1014_T2);Barbara_T2()" } };
            var parent = owner.AddStatus(main.StatusID2, owner, 1, 2, 0, main);
            var child = owner.GetStatus("ST_Barbara_RingHit");
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            Assert.That(owner.GetStatus(main.StatusID2), Is.SameAs(parent));
            Assert.That(parent.RemainingPhaseCount, Is.EqualTo(1));
            Assert.That(owner.GetStatus(child.StatusID2), Is.SameAs(child));
            Assert.That(child.RemainingPhaseCount, Is.EqualTo(1));
            env.Manager.EndAllyTurn();
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            Assert.That(owner.GetStatus(main.StatusID2), Is.SameAs(parent), "不额外编造仅能续一次的限制");
            owner.CurrentEnergy = 80;
            env.Manager.EndAllyTurn();
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            Assert.That(owner.HasStatus(main.StatusID2), Is.False);
            Assert.That(owner.HasStatus(child.StatusID2), Is.False);
        }

        [UnityTest]
        public IEnumerator StatusHealing_ReadsCurrentSkillLevelIncludingGrowingFlat_AndHeavyIsFourTimes()
            => HookTestEnv.AwaitTask(HealingAsync);
        private async Task HealingAsync()
        {
            using var env = new BattleFlowTestEnv();
            env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_2"] = new Dictionary<int, SkillLevelData>
            {
                [1] = new SkillLevelData { SkillType = 2, SkillLevel = 1, ParamID = "STE_Barbara_RingNormalHeal", Hits1 = "0.0075*TotalHP+72.2" },
                [2] = new SkillLevelData { SkillType = 2, SkillLevel = 2, ParamID = "STE_Barbara_RingNormalHeal", Hits1 = "0.0081*TotalHP+79.44" },
                [3] = new SkillLevelData { SkillType = 2, SkillLevel = 1, ParamID = "STE_Barbara_RingHeavyHeal", Hits1 = "0.03*TotalHP+288.8" },
                [4] = new SkillLevelData { SkillType = 2, SkillLevel = 2, ParamID = "STE_Barbara_RingHeavyHeal", Hits1 = "0.0324*TotalHP+317.76" }
            };
            env.CreateMinimalBattle(); env.Ally.SkillLevels[2] = 2;
            var main = Main(env, 4101408, "ST_Barbara_RingHit");
            var status = env.Ally.Entity.AddStatus(main.StatusID2, env.Ally.Entity, 5, 2, 0, main);
            env.Ally.Entity.TotalHP = 1000;
            foreach (string kind in new[] { "Normal", "Heavy" })
            {
                string id = "STE_Barbara_Ring" + kind + "Heal";
                env.DataManager.StatusEffectDict[id] = new StatusEffectData
                { StatusEffectID = kind == "Normal" ? 410140801 : 410140802, StatusEffectID2 = id,
                    EffectType = "Heal", TargetType = "Self" };
                env.Ally.Entity.CurrentHP = 100;
                await BattleExecution.Run(() => env.Ally.ExecuteStatusActionsTask(status,
                    new List<StatusActionData> { new StatusActionData { ActionType = "OnTrigger", Param1 = id } },
                    env.Ally.Entity, "OnTrigger"));
                Assert.That(env.Ally.Entity.CurrentHP, Is.EqualTo(100 + (kind == "Normal" ? 87.54f : 350.16f)).Within(.001f));
            }
        }
    }
}
