using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class APCostResolverTests
    {
        [TestCase("-2", "Normal; Skill;SwitchIn")]
        [TestCase("0", "Burst")]
        [TestCase("3", "Heavy;SwitchOut")]
        [TestCase("*0.5", "Skill")]
        [TestCase(" *1.25 ", "Normal;Heavy")]
        [TestCase("*0", "SwitchIn")]
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
        [TestCase("*-0.5", "Normal")]
        [TestCase("*NaN", "Normal")]
        [TestCase("*Infinity", "Normal")]
        [TestCase("*", "Normal")]
        [TestCase("*0.5;*0.8", "Normal")]
        public void Validation_RejectsInvalidParameters(string delta, string operations)
            => Assert.Throws<TableValidationException>(() => TableValidator.ValidateAPCost(delta, operations));

        [TestCase(15, "*0.5", "*1", 0, 8)]
        [TestCase(30, "*0.5", "*0.8", -1, 11)]
        [TestCase(100, "*0.2", "*0.8", 0, 16)]
        [TestCase(15, "*0", "*1", 0, 2)]
        [TestCase(15, "*0.5", "*1", -99, 0)]
        [TestCase(0, "*0.5", "*1", 3, 3)]
        public void Percentages_MultiplyBeforeFlats_AndCeilOnlyTheFinalResult(
            int basis, string first, string second, int flat, int expected)
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            AddParameter(env, env.Ally.Entity, 70601, first, "Skill");
            AddParameter(env, env.Ally.Entity, 70602, second, "Skill");
            AddModifier(env, env.Ally.Entity, 70603, flat, "Skill");
            var skill = new SkillMainData { APCost = basis, ActionType = "" };
            Assert.That(APCostResolver.ForSkill(env.Ally.Entity, skill, 2), Is.EqualTo(expected));
            Assert.That(APCostResolver.ForSkill(env.Ally.Entity, skill, 2), Is.EqualTo(expected));
            Assert.That(APCostResolver.ForSkill(env.Ally.Entity, skill, -1), Is.EqualTo(basis));
        }

        [Test]
        public void Percentages_ClampOnlyTheWholeProduct_AndRespectAggregationAndFree()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var owner = env.Ally.Entity;
            AddParameter(env, owner, 70601, "*0.05", "Skill");
            AddParameter(env, owner, 70602, "*0.2", "Skill");
            AddParameter(env, owner, 70603, "*10", "Skill");
            var skill = new SkillMainData { APCost = 100 };
            Assert.That(APCostResolver.ForSkill(owner, skill, 2), Is.EqualTo(10));
            var capped = AddParameter(env, owner, 70604, "*0.5", "Heavy");
            capped.StackCount = 3; capped.MainData.MaxCount = 2;
            skill.APCost = 30;
            Assert.That(APCostResolver.ForSkill(owner, skill, 1), Is.EqualTo(8));
            capped.IsActive = false;
            Assert.That(APCostResolver.ForSkill(owner, skill, 1), Is.EqualTo(30));
            AddParameter(env, owner, 70605, "*10", "Skill");
            AddModifier(env, owner, 70606, 100, "Skill");
            AddParameter(env, owner, 70607, "Free", "Skill");
            Assert.That(APCostResolver.ForSkill(owner, skill, 2), Is.Zero);
        }

        [Test]
        public void Switching_CombinesBothSidesBeforeThePercentFloorOrCeiling()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var incoming = env.CreateAlly(2).Entity;
            AddParameter(env, env.Ally.Entity, 70601, "*0.05", "SwitchOut");
            AddParameter(env, incoming, 70602, "*4", "SwitchIn");
            Assert.That(APCostResolver.ForSwitch(env.Ally.Entity, incoming, 5, false), Is.EqualTo(1));
            AddModifier(env, incoming, 70603, 2, "SwitchIn");
            Assert.That(APCostResolver.ForSwitch(env.Ally.Entity, incoming, 5, false), Is.EqualTo(3));
            Assert.That(APCostResolver.ForSwitch(env.Ally.Entity, incoming, 5, true), Is.Zero);
            AddParameter(env, incoming, 70604, "Free", "SwitchIn");
            Assert.That(APCostResolver.ForSwitch(env.Ally.Entity, incoming, 5, false), Is.Zero);
        }

        [Test]
        public void PercentageCost_PreviewAvailabilityPaymentAndDeclarationAgree()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(2, "SK_PercentAP", "SE_PercentAP", "Physical", "1*TotalATK,0", 15);
            env.CreateMinimalBattle();
            AddParameter(env, env.Ally.Entity, 70601, "*0.5", "Skill");
            AddModifier(env, env.Ally.Entity, 70602, -1, "Skill");
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            var random = new SequenceBattleRandomSource(new[] { .9f }); BattleRandom.SetSource(random);
            int payments = 0, paid = 0;
            using var subscription = BattleEventHub.Subscribe<APSpentEventData>(BattleEventKind.APSpent,
                (_, fact) => { if (fact.Subject == env.Ally.Entity) { payments++; paid += fact.Amount; } }, 0);
            Assert.That(env.Ally.GetBoundSkillAPCost(2), Is.EqualTo(7));
            Assert.That(env.Ally.GetBoundSkillAPCost(2), Is.EqualTo(7));
            Assert.That(random.FloatCallCount, Is.Zero);
            env.Manager.APManager.ConsumeAP(93);
            Assert.That(env.Ally.GetBlockReason(2), Is.Null);
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            Assert.That(env.Manager.APManager.CurrentAP, Is.Zero);
            Assert.That(payments, Is.EqualTo(1)); Assert.That(paid, Is.EqualTo(7));
            Assert.That(random.FloatCallCount, Is.EqualTo(1));
        }

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
            Assert.That(APCostResolver.ForSkill(env.Ally.Entity, skill, 2), Is.EqualTo(18));
            skill.ActionType = "Normal";
            Assert.That(APCostResolver.ForSkill(env.Ally.Entity, skill, 0), Is.EqualTo(14));
            skill.ActionType = "Heavy";
            Assert.That(APCostResolver.ForSkill(env.Ally.Entity, skill, 1), Is.EqualTo(20));
            discount.IsActive = false;
            skill.ActionType = "Skill";
            Assert.That(APCostResolver.ForSkill(env.Ally.Entity, skill, 2), Is.EqualTo(24));
        }

        [TestCase(0, "Normal"), TestCase(1, "Heavy"), TestCase(2, "Skill"), TestCase(3, "Burst")]
        public void SlotDiscount_DoesNotCreateAnAction_OrDependOnActionType(int slot, string operation)
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            AddModifier(env, env.Ally.Entity, 70501, -5, operation);
            var skill = new SkillMainData { APCost = 30, ActionType = "" };
            Assert.That(APCostResolver.ForSkill(env.Ally.Entity, skill, slot), Is.EqualTo(25));
            Assert.That(ActiveActionQuery.IsActiveAction(env.Ally.Entity, skill), Is.False);
            var copy = env.Ally.CloneSkill(skill);
            Assert.That(copy.ActionType, Is.Empty);
            Assert.That(APCostResolver.ForSkill(env.Ally.Entity, copy, slot), Is.EqualTo(25));
            skill.ActionType = "Burst";
            Assert.That(APCostResolver.ForSkill(env.Ally.Entity, skill, slot), Is.EqualTo(25), "费用只跟本次按钮槽位");
            Assert.That(APCostResolver.ForSkill(env.Ally.Entity, skill, -1), Is.EqualTo(30), "没有按钮来源时不借ActionType猜费用类别");
        }

        [TestCase(0, "Skill", "Skill", "Normal")]
        [TestCase(1, "Normal", "Burst", "Heavy")]
        public void ChangedButtonSkill_UsesSlotDiscountWithoutRewritingActionOrDamage(
            int slot, string actionType, string damageType, string operation)
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(slot, "SK_ChangedSlot", "SE_ChangedSlot", "Physical", "1*TotalATK,0", 20);
            env.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID][0].ActionType = actionType;
            env.DataManager.SkillEffectDict["SE_ChangedSlot"].DamageType = damageType;
            env.CreateMinimalBattle();
            AddModifier(env, env.Ally.Entity, 70503, -5, operation);
            AddModifier(env, env.Ally.Entity, 70504, 50, actionType);
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            int starts = 0, hits = 0;
            using var start = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted,
                (_, fact) => { if (fact.Subject == env.Ally.Entity)
                    { starts++; Assert.That(fact.ActionType, Is.EqualTo(actionType)); } }, 0);
            using var damage = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,
                (_, fact) => { if (fact.Source.SourceEffectID == "SE_ChangedSlot")
                    { hits++; Assert.That(fact.DamageType, Is.EqualTo(damageType)); } }, 0);
            var random = new SequenceBattleRandomSource(new[] { .9f }); BattleRandom.SetSource(random);
            Assert.That(env.Ally.GetBoundSkillAPCost(slot), Is.EqualTo(15));
            Assert.That(slot == 0 ? env.Manager.UseNormalAttackBySlot(0)
                : env.Manager.UseHeavyAttackBySlot(0), Is.True);
            Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(85));
            Assert.That(starts, Is.EqualTo(1)); Assert.That(hits, Is.EqualTo(1));
            Assert.That(random.FloatCallCount, Is.EqualTo(1)); Assert.That(random.IntCallCount, Is.Zero);
        }

        [TestCase(0, false, 30)]
        [TestCase(-5, false, 25)]
        [TestCase(7, false, 37)]
        [TestCase(0, true, 0)]
        public void ChargeWithoutAction_PaysOnceAndReleasesHeavyDamageWithoutActionEvents(
            int delta, bool free, int expected)
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(1, "SK_Charge", "SE_Charge", "Physical", "", 30,
                effectType: "ApplyStatus", param1: "ST_Charge", duration: 1,
                addInPhase: 1, triggerPhase: 1, targetType: "Self");
            var charge = env.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID][0];
            charge.ActionType = "";
            env.ConfigureStatus("ST_Charge", actionType: "OnItsTurn", actionEffectID2: "SK_Release");
            env.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID].Add(new SkillMainData
            { SkillID = 990105, SkillID2 = "SK_Release", APCost = 0, ActionType = "" });
            env.DataManager.SkillEffectDict["SE_Release"] = new SkillEffectData
            { SkillEffectID = 99010501, SkillEffectID2 = "SE_Release", EffectType = "Damage",
                Element = "Physical", DamageType = "Heavy", Param1 = "1*TotalATK,0",
                TargetType = "Enemy", TargetNumber = 1, TargetConsecutive = 1 };
            env.CreateMinimalBattle();
            AddModifier(env, env.Ally.Entity, 70502, delta, "Heavy");
            if (free) env.DataManager.StatusEffectDict["STE_AP_70502"].Param1 = "Free";
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            int starts = 0, completes = 0, payments = 0, paid = 0, hits = 0;
            using var start = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted,
                (_, fact) => { if (fact.Subject == env.Ally.Entity) starts++; }, 0);
            using var complete = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted,
                (_, fact) => { if (fact.Subject == env.Ally.Entity) completes++; }, 0);
            using var payment = BattleEventHub.Subscribe<APSpentEventData>(BattleEventKind.APSpent,
                (_, fact) => { if (fact.Subject == env.Ally.Entity) { payments++; paid += fact.Amount; } }, 0);
            using var damage = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,
                (_, fact) => { if (fact.Source.SourceEffectID == "SE_Release")
                    { hits++; Assert.That(fact.DamageType, Is.EqualTo("Heavy")); } }, 0);
            var random = new SequenceBattleRandomSource(new[] { .9f }); BattleRandom.SetSource(random);
            Assert.That(env.Ally.GetBoundSkillAPCost(1), Is.EqualTo(expected));
            Assert.That(env.Manager.UseHeavyAttackBySlot(0), Is.True);
            Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(100 - expected));
            Assert.That(hits, Is.Zero); Assert.That(starts + completes, Is.Zero);
            Assert.That(random.FloatCallCount, Is.Zero);
            env.Manager.EndAllyTurn();
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyPreTurn);
            Assert.That(hits, Is.EqualTo(1));
            Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(950f).Within(.001f));
            Assert.That(paid, Is.EqualTo(expected)); Assert.That(payments, Is.EqualTo(expected == 0 ? 0 : 1));
            Assert.That(starts + completes, Is.Zero, "起手和无行为释放容器均不补发主动行为");
            Assert.That(random.FloatCallCount, Is.EqualTo(1)); Assert.That(random.IntCallCount, Is.Zero);
        }

        [TestCase(0, "Normal", -50, 0)]
        [TestCase(1, "Heavy", -7, 13)]
        [TestCase(2, "Skill", 5, 25)]
        [TestCase(3, "Burst", 5, 25)]
        public void Action_PreviewAvailabilityAndPaymentAgree(int type, string operation, int delta, int expected)
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(type, "SK_AP", "SE_AP", "Physical", "1*TotalATK,0", 20);
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
            env.ConfigureAllyAction(3, "SK_APBurst", "SE_APBurst", "Physical", "1*TotalATK,0", 0);
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
                Assert.That(BattleUiCombatHudSource.GetPreviewCost(env.Manager, null, switcher), Is.EqualTo(expected));
                Assert.That(BattleUiCombatHudSource.GetPreviewCost(env.Manager, null, switcher), Is.EqualTo(expected));
                Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(100));
                env.Manager.APManager.ConsumeAP(100 - expected);
                Assert.That(switcher.UIConfirmSwitch(), Is.True);
                Assert.That(reserve.IsActive, Is.True);
                Assert.That(env.Manager.APManager.CurrentAP, Is.Zero);
            }
            finally { Object.DestroyImmediate(runtime); }
        }

        [Test]
        public void PaidSwitch_RecordsTheOldLayoutButRunsResponsesAfterTheNewLayoutIsCommitted()
        {
            using var env = new BattleFlowTestEnv();
            env.CreateMinimalBattle();
            var reserve = env.CreateAlly(2);
            env.Manager.StartBattle();
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            APSpentEventData payment = null;
            bool responseSawNewLayout = false;
            using var subscription = BattleEventHub.Subscribe<APSpentEventData>(BattleEventKind.APSpent,
                (header, fact) =>
                {
                    payment = fact;
                    responseSawNewLayout = reserve.IsActive && !env.Ally.IsActive;
                }, 0);
            Assert.That(env.Manager.TrySwitchActiveAllyWithAPCost(1), Is.True);
            Assert.That(payment, Is.Not.Null);
            Assert.That(payment.Amount, Is.EqualTo(BattleManager.SwitchAPCost));
            Assert.That(payment.WasOnField(env.Ally.Entity), Is.True);
            Assert.That(payment.WasOnField(reserve.Entity), Is.False);
            Assert.That(responseSawNewLayout, Is.True);
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
            => AddParameter(env, actor, id, delta.ToString(System.Globalization.CultureInfo.InvariantCulture), operations);

        private static StatusInstance AddParameter(BattleFlowTestEnv env, BattleEntity actor,
            int id, string parameter, string operations)
        {
            string key = "ST_AP_" + id;
            env.ConfigureStatus(key, maxStack: 10);
            var main = env.DataManager.StatusMainDict[key];
            main.StatusID = id;
            env.DataManager.StatusEffectDict["STE_AP_" + id] = new StatusEffectData
            {
                StatusEffectID = id * 100 + 1, StatusEffectID2 = "STE_AP_" + id,
                EffectType = "APCost", Param1 = parameter, Param2 = operations
            };
            actor.AddStatus(key, actor, 0, 2, 0, main);
            return actor.GetStatus(key);
        }
    }
}
