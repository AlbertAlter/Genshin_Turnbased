using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class APCostResolverTests
    {
        [TestCase("-2", "Normal; Skill;SwitchIn")]
        [TestCase("0", "Burst")]
        [TestCase("3", "Heavy;SwitchOut")]
        public void Validation_AllowsSignedIntegerAndMultipleOperations(string delta, string operations)
        {
            Assert.DoesNotThrow(() => TableValidator.ValidateAPCost(delta, operations));
            Assert.DoesNotThrow(() => TableValidator.ValidateEffectRow("Status_Effect", 3,
                "APCost", "", 0, operations, "", 0, 0, "", false, false));
        }

        [TestCase("", "Normal")]
        [TestCase("1.5", "Normal")]
        [TestCase("2", "")]
        [TestCase("2", "Normal,Burst")]
        [TestCase("2", "Unknown")]
        public void Validation_RejectsInvalidParameters(string delta, string operations)
            => Assert.Throws<TableValidationException>(() => TableValidator.ValidateAPCost(delta, operations));

        [Test]
        public void Modifiers_RespectOperationActiveStateStackCapAndFieldHost()
        {
            using var env = new BattleFlowTestEnv();
            env.CreateMinimalBattle();
            var discount = AddModifier(env, env.Ally.Entity, 70001, -3, "Normal;Skill;Normal");
            discount.StackCount = 3;
            discount.MainData.MaxCount = 2;
            var field = AddModifier(env, env.Ally.Entity, 70002, 4, "Skill");
            env.Ally.Entity.StatusDict.Remove(field.StatusID2);
            env.Ally.Entity.Position.StatusList.Add(field);
            AddModifier(env, env.Ally.Entity, 70003, 100, "Burst");
            AddModifier(env, env.Ally.Entity, 70004, 90, "Skill").IsActive = false;
            var skill = new SkillMainData { APCost = 20, ActionType = "Skill" };
            Assert.That(APCostResolver.ForSkill(env.Ally.Entity, skill), Is.EqualTo(18));
            skill.ActionType = "Normal";
            Assert.That(APCostResolver.ForSkill(env.Ally.Entity, skill), Is.EqualTo(14));
            skill.ActionType = "Heavy";
            Assert.That(APCostResolver.ForSkill(env.Ally.Entity, skill), Is.EqualTo(20));
            discount.IsActive = false;
            skill.ActionType = "Skill";
            Assert.That(APCostResolver.ForSkill(env.Ally.Entity, skill), Is.EqualTo(24));
        }

        [TestCase(0, "Normal", -50, 0)]
        [TestCase(1, "Heavy", -7, 13)]
        [TestCase(2, "Skill", 5, 25)]
        [TestCase(3, "Burst", 5, 25)]
        public void Action_PreviewAvailabilityAndPaymentAgree(int type, string operation, int delta, int expected)
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(type, "SK_AP", "SE_AP", "None", "1*TotalATK,0", 20);
            env.CreateMinimalBattle();
            AddModifier(env, env.Ally.Entity, 70101, delta, operation);
            env.ConfigureStatus("ST_APUsed", actionType: "OnTrigger", actionEffectID2: "STE_APUsed");
            env.DataManager.StatusActionDict["ST_APUsed"][0].APUsed = "All";
            env.DataManager.StatusEffectDict["STE_APUsed"] = new StatusEffectData
            {
                StatusEffectID2 = "STE_APUsed", EffectType = "GainEnergy", Param1 = "1",
                Param2 = "Flat", TargetType = "Self"
            };
            env.Ally.Entity.AddStatus("ST_APUsed", env.Ally.Entity, 0, 2, 0,
                env.DataManager.StatusMainDict["ST_APUsed"]);
            env.Ally.Entity.CurrentEnergy = 0;
            env.Manager.StartBattle();
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            var random = new SequenceBattleRandomSource(new[] { 0.9f });
            BattleRandom.SetSource(random);
            Assert.That(env.Ally.GetBoundSkillAPCost(type), Is.EqualTo(expected));
            env.Manager.APManager.ConsumeAP(100 - expected);
            Assert.That(env.Ally.GetBlockReason(type), Is.Null);
            Assert.That(random.FloatCallCount, Is.Zero, "查询费用不能取随机数");
            bool success = type == 0 ? env.Manager.UseNormalAttackBySlot(0)
                : type == 1 ? env.Manager.UseHeavyAttackBySlot(0)
                : type == 2 ? env.Manager.UseSkillBySlot(0) : env.Manager.UseBurstBySlot(0);
            Assert.That(success, Is.True);
            Assert.That(env.Manager.APManager.CurrentAP, Is.Zero);
            Assert.That(env.Enemy.Entity.CurrentHP, Is.LessThan(1000));
            Assert.That(env.Ally.Entity.CurrentEnergy, Is.EqualTo(expected > 0 ? 1 : 0),
                "实际零消耗不能触发 APUsed，正消耗只触发一次");
            Assert.That(random.FloatCallCount, Is.EqualTo(1), "只有实际伤害进行一次暴击判定");
        }

        [Test]
        public void BurstSurcharge_CanBlockAnOriginallyFreeBurst()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(3, "SK_APBurst", "SE_APBurst", "None", "1*TotalATK,0", 0);
            env.CreateMinimalBattle();
            AddModifier(env, env.Ally.Entity, 70201, 6, "Burst");
            env.Manager.StartBattle();
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            env.Manager.APManager.ConsumeAP(95);
            Assert.That(env.Ally.CanUseBurst(), Is.False);
            Assert.That(env.Ally.GetBlockReason(3), Does.Contain("需要6"));
            Assert.That(env.Manager.UseBurstBySlot(0), Is.False);
            Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(5));
            Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(1000));
        }

        [TestCase(-10, 7, 2)]
        [TestCase(-10, -10, 0)]
        [TestCase(2, 3, 10)]
        public void Switch_CombinesBothSidesAndPreviewsWithoutSpending(int outgoingDelta, int incomingDelta, int expected)
        {
            using var env = new BattleFlowTestEnv();
            env.CreateMinimalBattle();
            var reserve = env.CreateAlly(2);
            AddModifier(env, env.Ally.Entity, 70301, outgoingDelta, "SwitchOut");
            AddModifier(env, reserve.Entity, 70302, incomingDelta, "SwitchIn");
            AddModifier(env, env.Ally.Entity, 70303, 100, "SwitchIn");
            AddModifier(env, reserve.Entity, 70304, 100, "SwitchOut");
            env.Manager.StartBattle();
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            var runtime = new GameObject("APCost_SwitchPreviewTest");
            try
            {
                var switcher = runtime.AddComponent<BattlefieldSwitchController>();
                Assert.That(switcher.UIBeginSwitch(), Is.True);
                switcher.UIMoveSwitch(1);
                Assert.That(BattlefieldApView.GetPreviewCost(env.Manager, null, switcher), Is.EqualTo(expected));
                Assert.That(BattlefieldApView.GetPreviewCost(env.Manager, null, switcher), Is.EqualTo(expected));
                Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(100));
                env.Manager.APManager.ConsumeAP(100 - expected);
                Assert.That(switcher.UIConfirmSwitch(), Is.True);
                Assert.That(reserve.IsActive, Is.True);
                Assert.That(env.Manager.APManager.CurrentAP, Is.Zero);
            }
            finally { Object.DestroyImmediate(runtime); }
        }

        [Test]
        public void ForcedDeathSwitch_RemainsFreeDespiteSurcharges()
        {
            using var env = new BattleFlowTestEnv();
            env.CreateMinimalBattle();
            var reserve = env.CreateAlly(2);
            AddModifier(env, env.Ally.Entity, 70401, 99, "SwitchOut");
            AddModifier(env, reserve.Entity, 70402, 99, "SwitchIn");
            env.Ally.Entity.CurrentHP = 0;
            env.Manager.StartBattle();
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            Assert.That(env.Manager.IsFreeDeathSwitchPending, Is.True);
            Assert.That(env.Manager.GetSwitchAPCost(1), Is.Zero);
            int before = env.Manager.APManager.CurrentAP;
            Assert.That(env.Manager.TrySwitchActiveAllyWithAPCost(1), Is.True);
            Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(before));
        }

        private static StatusInstance AddModifier(BattleFlowTestEnv env, BattleEntity actor,
            int id, int delta, string operations)
        {
            string key = "ST_AP_" + id;
            env.ConfigureStatus(key, maxStack: 10);
            var main = env.DataManager.StatusMainDict[key];
            main.StatusID = id;
            env.DataManager.StatusEffectDict["STE_AP_" + id] = new StatusEffectData
            {
                StatusEffectID = id * 100 + 1, StatusEffectID2 = "STE_AP_" + id,
                EffectType = "APCost", Param1 = delta.ToString(), Param2 = operations
            };
            actor.AddStatus(key, actor, 0, 2, 0, main);
            return actor.GetStatus(key);
        }
    }
}
