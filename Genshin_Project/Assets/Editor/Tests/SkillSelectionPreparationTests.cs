using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public class SkillSelectionPreparationTests
    {
        private const string Skill = "SK_Prepare";
        private const string Before = "ST_PrepareBefore";
        private const string First = "ST_PrepareFirst";
        private const string Second = "ST_PrepareSecond";

        private static SkillEffectData Status(string id, string status, string target = "Enemy", bool manual = true)
        {
            return new SkillEffectData
            {
                SkillEffectID2 = id, EffectType = "ApplyStatus", Param1 = status,
                TargetType = target, TargetNumber = 1, TargetConsecutive = 1,
                TargetConsecutiveSet = manual
            };
        }

        private static void Configure(BattleFlowTestEnv env)
        {
            env.ConfigureAllyAction(2, Skill, "SE_Prepare", "Physical", null, cooldown: 2,
                effectType: "ApplyStatus", param1: First);
            var skill = env.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID][0];
            skill.EnergyUsed = 10;
            skill.MaxCharge = 2;
            skill.InitialCharge = 2;
            foreach (string id in new[] { Before, First, Second }) env.ConfigureStatus(id, maxStack: 10);
        }

        private static BattleInputController Start(BattleFlowTestEnv env, params SkillEffectData[] effects)
        {
            env.CreateMinimalBattle();
            if (effects.Length > 0) env.Ally.SetSkillEffects(Skill, new List<SkillEffectData>(effects));
            env.Manager.StartBattle();
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            env.Ally.Entity.CurrentEnergy = 30;
            BattleRandom.SetSource(new SequenceBattleRandomSource());
            return env.Manager.gameObject.AddComponent<BattleInputController>();
        }

        private static void Choose(BattleInputController input, int position)
        {
            input.Selector.CurrentSelection = new List<int> { position };
            input.UIConfirmSelection();
        }

        private static void AssertUncommitted(BattleFlowTestEnv env)
        {
            Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(100));
            Assert.That(env.Ally.Entity.CurrentEnergy, Is.EqualTo(30));
            Assert.That(env.Ally.GetCooldownRemaining(2), Is.Zero);
            Assert.That(env.Ally.GetBoundSkillCurrentCharge(2), Is.EqualTo(2));
            Assert.That(env.Ally.Entity.HasStatus(Before), Is.False);
            Assert.That(env.Enemy.Entity.HasStatus(First), Is.False);
            Assert.That(env.Enemy.Entity.HasStatus(Second), Is.False);
            Assert.That(BattleExecution.IsBusy, Is.False);
        }

        // 当前表的四组选段：雷泽/班尼特/优菈战技、甘雨重击。
        [TestCase(1, 30, 50, 0)]
        [TestCase(2, 15, 30, 2)]
        [TestCase(2, 10, 30, 3)]
        public void RepeatedButtonClick_SelectsPhaseAndPaysOnlyConfirmedPhase(int button, int firstCost, int nextCost, int cooldown)
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureStatus(First); env.ConfigureStatus(Second);
            env.ConfigureAllyAction(button, "SK_ClickFirst", "SE_ClickFirst", "Physical", null,
                apCost: firstCost, effectType: "ApplyStatus", param1: First);
            env.ConfigureAllyAction(button, "SK_ClickNext", "SE_ClickNext", "Physical", null,
                apCost: nextCost, cooldown: cooldown, effectType: "ApplyStatus", param1: Second);
            var skills = env.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID];
            skills[0].SkillID = 1; skills[1].SkillID = 2;
            skills[0].SkillPhase = skills[1].SkillPhase = 1;
            if (button == 1) env.DataManager.CharacterAttributesDict[BattleFlowTestEnv.AllyID].HeavyAttackID = "SK_ClickFirst";
            else env.DataManager.CharacterAttributesDict[BattleFlowTestEnv.AllyID].ElementalSkillID = "SK_ClickFirst";
            var input = Start(env);
            input.UIEnterSelectSkill(button);
            input.UIEnterSelectSkill(button); // 实际鼠标入口；以前直接 return。
            Assert.That(env.Ally.GetBoundSkillID(button), Is.EqualTo("SK_ClickNext"));
            Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(100));
            input.UIEnterSelectSkill(button); // 最后一段不自动施放。
            Assert.That(input.IsSelecting, Is.True);
            input.UICancelSelection();
            Assert.That(env.Ally.GetBoundSkillID(button), Is.EqualTo("SK_ClickFirst"));
            Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(100));
            input.UIEnterSelectSkill(button); input.UIEnterSelectSkill(button);
            input.UIConfirmSelection();
            Assert.That(env.Enemy.Entity.HasStatus(First), Is.False);
            Assert.That(env.Enemy.Entity.HasStatus(Second), Is.True);
            Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(100 - nextCost));
            Assert.That(env.Ally.GetBoundSkillID(button), Is.EqualTo("SK_ClickFirst"));
            Assert.That(env.Ally.GetCooldownRemaining(button), Is.EqualTo(cooldown));
        }

        [Test]
        public void BurstSlots_CanSwitchCasterDuringSelection_AndOrdinaryButtonUsesActiveAlly()
        {
            using var env = new BattleFlowTestEnv(); Configure(env);
            env.ConfigureAllyAction(3, "SK_ClickBurst", "SE_ClickBurst", "Physical", null,
                effectType: "ApplyStatus", param1: Second, targetType: "Self");
            var input = Start(env, Status("SE_Prepare", First));
            var other = env.CreateAlly(2);
            input.UIEnterSelectSkill(2);
            input.UIEnterBurstBySlot(1);
            Assert.That(input.PendingAlly, Is.SameAs(other));
            Assert.That(input.Selector.CurrentSelection, Is.EqualTo(new[] { 2 }));
            input.UIEnterBurstBySlot(0);
            Assert.That(input.PendingAlly, Is.SameAs(env.Ally));
            input.UIEnterBurstBySlot(1);
            input.UIEnterSelectSkill(2);
            Assert.That(input.PendingAlly, Is.SameAs(env.Ally));
            Assert.That(input.PendingSkill, Is.EqualTo(2));
            AssertUncommitted(env);
        }

        [Test]
        public void DisabledInputAndWrongPhase_BlockMouseCommands_WithoutLosingPreparedSelection()
        {
            using var env = new BattleFlowTestEnv(); Configure(env);
            var input = Start(env, Status("SE_Prepare", First));
            input.UIEnterSelectSkill(2);
            input.enabled = false;
            input.UIEnterSelectSkill(0); input.UIEnterBurstBySlot(0); input.UIConfirmSelection();
            Assert.That(input.IsSelecting, Is.True);
            AssertUncommitted(env);
            input.UICancelSelection(); // 换人控制器可在输入停用时清理旧预选。
            input.enabled = true;
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyAction);
            input.UIEnterSelectSkill(2);
            Assert.That(input.IsSelecting, Is.False);
            Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(100));
        }

        [Test]
        public void MultipleSelections_KeepCostPreview_AndAvailableChargeIsNotExhausted()
        {
            using var env = new BattleFlowTestEnv(); Configure(env);
            var input = Start(env, Status("SE_Prepare_1", First), Status("SE_Prepare_2", Second));
            input.UIEnterSelectSkill(2); input.UIConfirmSelection();
            Assert.That(input.IsSelecting, Is.True);
            Assert.That(BattleUiCombatHudSource.GetPreviewCost(env.Manager, input, null), Is.EqualTo(20));
            input.UIConfirmSelection();
            var hud = env.Manager.gameObject.AddComponent<BattleUiCombatHudSource>(); hud.battle = env.Manager;
            Assert.That(hud.GetAction(0, 2).Cooldown, Is.EqualTo(2));
            Assert.That(hud.GetAction(0, 2).HasAvailableCharge, Is.True);
            Assert.That(env.Ally.CanUseSkill(), Is.True);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ButtonAvailability_UsesBoundSkillEnergyForEverySlot(int slot)
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureStatus(First);
            env.ConfigureAllyAction(slot, Skill, "SE_Prepare", "Physical", null,
                effectType: "ApplyStatus", param1: First);
            env.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID][0].EnergyUsed = 40;
            var input = Start(env);
            Func<bool>[] usable = { env.Ally.CanUseNormalAttack, env.Ally.CanUseHeavyAttack,
                env.Ally.CanUseSkill, env.Ally.CanUseBurst };
            Assert.That(usable[slot](), Is.False);
            input.UIEnterSelectSkill(slot);
            Assert.That(input.IsSelecting, Is.False);
            env.Ally.Entity.CurrentEnergy = 40;
            Assert.That(usable[slot](), Is.True);
            input.UIEnterSelectSkill(slot);
            Assert.That(input.IsSelecting, Is.True);
            input.UICancelSelection();
        }

        [Test]
        public void ExplicitWholeEnemyConfirmation_DoesNotSelectFollowingAutomaticSelfBuff()
        {
            using var env = new BattleFlowTestEnv(); Configure(env);
            var all = Status("SE_Prepare_All", First); all.TargetNumber = -1;
            var self = Status("SE_Prepare_Self", Second, "Self", false);
            var input = Start(env, all, self);
            var other = env.CreateEnemy(3);
            input.UIEnterSelectSkill(2);
            Assert.That(input.IsSelecting, Is.True);
            Assert.That(input.Selector.Side, Is.EqualTo(BattleSide.Enemy));
            Assert.That(input.Selector.CurrentSelection, Is.EqualTo(new[] { 1, 3 }));
            AssertUncommitted(env);
            input.UIConfirmSelection();
            Assert.That(input.IsSelecting, Is.False);
            Assert.That(env.Enemy.Entity.HasStatus(First), Is.True);
            Assert.That(other.Entity.HasStatus(First), Is.True);
            Assert.That(env.Ally.Entity.HasStatus(Second), Is.True);
        }

        [Test]
        public void AutomaticTeamEnergy_DoesNotAddAnAllySelectionAfterEnemyConfirmation()
        {
            using var env = new BattleFlowTestEnv(); Configure(env);
            var energy = new SkillEffectData { SkillEffectID2 = "SE_Prepare_Energy", EffectType = "GainEnergy",
                TargetType = "Allies", TargetNumber = -1, Param1 = "7", Param2 = "Based", Element = "Anemo" };
            var input = Start(env, Status("SE_Prepare_Target", First), energy);
            Assert.That(env.Ally.EffectNeedsSelection(energy), Is.False);
            input.UIEnterSelectSkill(2);
            Assert.That(input.Selector.Side, Is.EqualTo(BattleSide.Enemy));
            input.UIConfirmSelection();
            Assert.That(env.Ally.PendingSelectEffect, Is.Null);
            Assert.That(input.IsSelecting, Is.False);
            Assert.That(env.Enemy.Entity.HasStatus(First), Is.True);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void NoEnemyTargets_ReportsLangKey_WithoutSelectionOrPayment(int consecutive)
        {
            using var env = new BattleFlowTestEnv(); Configure(env);
            var effect = Status("SE_Prepare_1", First); effect.TargetConsecutive = consecutive;
            var input = Start(env, Status("SE_Prepare_0", Before, "Self", false), effect);
            env.Enemy.Entity.CurrentHP = 0; // 仅构造空目标，不提交死亡或胜利。
            var tips = new List<string>(); input.TipRequested += tips.Add;
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            input.UIEnterSelectSkill(2);
            CollectionAssert.AreEqual(new[] { BattleTipKeys.NoValidTarget }, tips);
            Assert.That(input.IsSelecting, Is.False);
            Assert.That(env.Ally.HasPendingEffectSequence, Is.False);
            Assert.That(env.Ally.PendingSelectEffect, Is.Null);
            AssertUncommitted(env);
            Assert.That(random.IntCallCount, Is.Zero); Assert.That(random.FloatCallCount, Is.Zero);
        }

        [Test]
        public void SelectedTargetBecomesUnavailable_ReportsTipAndCancelsWithoutPayment()
        {
            using var env = new BattleFlowTestEnv(); Configure(env);
            var input = Start(env, Status("SE_Prepare_1", First));
            var tips = new List<string>(); input.TipRequested += tips.Add;
            input.UIEnterSelectSkill(2);
            Assert.That(input.IsSelecting, Is.True);
            env.Enemy.Entity.CurrentHP = 0;
            input.UIConfirmSelection();
            Assert.That(input.IsSelecting, Is.False);
            CollectionAssert.AreEqual(new[] { BattleTipKeys.NoValidTarget }, tips);
            AssertUncommitted(env);
        }

        [TestCase("Self")]
        [TestCase("EnemyField")]
        public void NoEnemyUnits_DoesNotBlockSelfOrEmptyFieldSelection(string target)
        {
            using var env = new BattleFlowTestEnv(); Configure(env);
            var input = Start(env, Status("SE_Prepare_1", First, target));
            env.Enemy.Entity.CurrentHP = 0;
            var tips = new List<string>(); input.TipRequested += tips.Add;
            input.UIEnterSelectSkill(2);
            Assert.That(input.IsSelecting, Is.True); Assert.That(tips, Is.Empty);
            input.UICancelSelection(); AssertUncommitted(env);
        }

        [Test]
        public void AllEnemiesOffField_UsesNoTargetTip_WithoutPayment()
        {
            using var env = new BattleFlowTestEnv(); Configure(env);
            var input = Start(env, Status("SE_Prepare_1", First));
            OffFieldTests.Leave(env, env.Enemy.Entity);
            var tips = new List<string>(); input.TipRequested += tips.Add;
            input.UIEnterSelectSkill(2);
            Assert.That(input.IsSelecting, Is.False);
            Assert.That(tips, Is.EqualTo(new[] { BattleTipKeys.NoValidTarget }));
            AssertUncommitted(env);
        }

        [Test]
        public void EarlierConfirmedTargetLeavesBeforeFinalChoice_CancelsWholePreparation()
        {
            using var env = new BattleFlowTestEnv(); Configure(env);
            var input = Start(env, Status("SE_Prepare_1", First), Status("SE_Prepare_2", Second, "Self"));
            var tips = new List<string>(); input.TipRequested += tips.Add;
            input.UIEnterSelectSkill(2); Choose(input, 1);
            Assert.That(input.IsSelecting, Is.True);
            OffFieldTests.Leave(env, env.Enemy.Entity);
            Choose(input, 1);
            Assert.That(input.IsSelecting, Is.False);
            Assert.That(tips, Is.EqualTo(new[] { BattleTipKeys.NoValidTarget }));
            AssertUncommitted(env);
        }

        [Test]
        public void CasterLeavesBeforeConfirm_CancelsWithoutPayment()
        {
            using var env = new BattleFlowTestEnv(); Configure(env);
            var input = Start(env, Status("SE_Prepare_1", First));
            input.UIEnterSelectSkill(2);
            OffFieldTests.Leave(env, env.Ally.Entity);
            input.UIConfirmSelection();
            Assert.That(input.IsSelecting, Is.False);
            AssertUncommitted(env);
        }

        [Test]
        public void AutomaticPrefix_CancelBeforeSingleChoice_HasNoSideEffects()
        {
            using var env = new BattleFlowTestEnv();
            Configure(env);
            var input = Start(env, Status("SE_Prepare_0", Before, "Self", false),
                Status("SE_Prepare_1", First));
            input.UIEnterSelectSkill(2);
            Assert.That(input.IsSelecting, Is.True);
            AssertUncommitted(env);
            input.UICancelSelection();
            AssertUncommitted(env);
            Assert.That(input.IsSelecting, Is.False);
            Assert.That(env.Ally.PendingSelectEffect, Is.Null);
            Assert.That(env.Ally.ForcedTargetPositions, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TwoChoices_CommitOnceInOrderOrCancelEntirePreparation(bool cancel)
        {
            using var env = new BattleFlowTestEnv();
            Configure(env);
            var input = Start(env, Status("SE_Prepare_0", Before, "Self", false),
                Status("SE_Prepare_1", First), Status("SE_Prepare_2", Second, "Allies"));
            var otherAlly = env.CreateAlly(2);
            input.UIEnterSelectSkill(2);
            Choose(input, 1);
            AssertUncommitted(env);
            Assert.That(input.IsSelecting, Is.True);
            Assert.That(input.Selector.Side, Is.EqualTo(BattleSide.Ally));
            Assert.That(env.Ally.PendingSelectEffect.SkillEffectID2, Is.EqualTo("SE_Prepare_2"));
            if (cancel)
            {
                input.UICancelSelection();
                AssertUncommitted(env);
                Assert.That(otherAlly.Entity.HasStatus(Second), Is.False);
                Assert.That(env.Ally.HasPendingEffectSequence, Is.False);
                return;
            }
            Choose(input, 2);
            Assert.That(input.IsSelecting, Is.False);
            Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(80));
            Assert.That(env.Ally.Entity.CurrentEnergy, Is.EqualTo(20));
            Assert.That(env.Ally.GetCooldownRemaining(2), Is.EqualTo(2));
            Assert.That(env.Ally.GetBoundSkillCurrentCharge(2), Is.EqualTo(1));
            Assert.That(env.Enemy.Entity.HasStatus(First), Is.True);
            Assert.That(otherAlly.Entity.HasStatus(Second), Is.True);
            Assert.That(env.Ally.Entity.HasStatus(Second), Is.False);
            Assert.That(env.Ally.Entity.GetStatus(Before).ApplyOrder,
                Is.LessThan(env.Enemy.Entity.GetStatus(First).ApplyOrder));
            Assert.That(env.Enemy.Entity.GetStatus(First).ApplyOrder,
                Is.LessThan(otherAlly.Entity.GetStatus(Second).ApplyOrder));
            Assert.That(env.Ally.ForcedTargetPositions, Is.Empty);
        }

        [Test]
        public void InheritedTarget_DoesNotAskAgainAndUsesFirstChoice()
        {
            using var env = new BattleFlowTestEnv();
            Configure(env);
            var inherited = Status("SE_Prepare_2", Second);
            inherited.TargetOverride = "0,0";
            var input = Start(env, Status("SE_Prepare_1", First), inherited);
            var other = env.CreateEnemy(2);
            input.UIEnterSelectSkill(2);
            Choose(input, 2);
            Assert.That(input.IsSelecting, Is.False);
            Assert.That(other.Entity.HasStatus(First), Is.True);
            Assert.That(other.Entity.HasStatus(Second), Is.True);
            Assert.That(env.Enemy.Entity.HasStatus(First), Is.False);
            Assert.That(env.Enemy.Entity.HasStatus(Second), Is.False);
        }

        [TestCase("ExecuteSkill")]
        [TestCase("ExecuteEffect")]
        public void ExplicitNestedCalls_CollectSeparateChoicesBeforeAnyExecution(string callType)
        {
            using var env = new BattleFlowTestEnv();
            Configure(env);
            env.ConfigureAllyAction(0, "SK_Child", "SE_Child", "Physical", null, apCost: 0,
                effectType: "ApplyStatus", param1: First);
            string called = callType == "ExecuteSkill" ? "SK_Child" : "SE_Child";
            var input = Start(env, Status("SE_Prepare_0", Before, "Self", false),
                new SkillEffectData { SkillEffectID2 = "SE_Prepare_1", EffectType = callType, Param1 = called },
                new SkillEffectData { SkillEffectID2 = "SE_Prepare_2", EffectType = callType, Param1 = called });
            var other = env.CreateEnemy(2);
            input.UIEnterSelectSkill(2);
            Assert.That(env.Ally.PendingSelectEffect.SkillEffectID2, Is.EqualTo("SE_Child"));
            Choose(input, 1);
            AssertUncommitted(env);
            Choose(input, 2);
            Assert.That(input.IsSelecting, Is.False);
            Assert.That(env.Enemy.Entity.HasStatus(First), Is.True);
            Assert.That(other.Entity.HasStatus(First), Is.True);
            Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(80));
        }

        [Test]
        public void ProbabilityAndRepeat_AreEvaluatedOnlyAfterFinalConfirmation()
        {
            using var env = new BattleFlowTestEnv();
            Configure(env);
            var repeated = Status("SE_Prepare_1", First);
            repeated.ScriptHook = "Chance(0.5)";
            repeated.ExecuteCount = 2;
            var input = Start(env, repeated, Status("SE_Prepare_2", Second));
            var random = new SequenceBattleRandomSource(new[] { 0.1f, 0.9f });
            BattleRandom.SetSource(random);
            input.UIEnterSelectSkill(2);
            Choose(input, 1);
            Assert.That(random.FloatCallCount, Is.Zero);
            AssertUncommitted(env);
            Choose(input, 1);
            Assert.That(random.FloatCallCount, Is.EqualTo(2));
            Assert.That(env.Enemy.Entity.GetStatus(First).StackCount, Is.EqualTo(1));
            Assert.That(env.Enemy.Entity.HasStatus(Second), Is.True);
        }

        [Test]
        public void SwitchSkill_DiscardsEarlierChoicesAndEffects()
        {
            using var env = new BattleFlowTestEnv();
            Configure(env);
            env.ConfigureAllyAction(0, "SK_Other", "SE_Other", "Physical", null, apCost: 5,
                effectType: "ApplyStatus", param1: Second);
            var input = Start(env, Status("SE_Prepare_0", Before, "Self", false),
                Status("SE_Prepare_1", First), Status("SE_Prepare_2", Second));
            input.UIEnterSelectSkill(2);
            Choose(input, 1);
            input.UISelectSkillKey(0);
            AssertUncommitted(env);
            Assert.That(input.PendingSkill, Is.Zero);
            Choose(input, 1);
            Assert.That(env.Ally.Entity.HasStatus(Before), Is.False);
            Assert.That(env.Enemy.Entity.HasStatus(First), Is.False);
            Assert.That(env.Enemy.Entity.HasStatus(Second), Is.True);
            Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(95));
        }

        [Test]
        public void SkillPhaseUpgrade_DiscardsEarlierChoicesAndRestoresBindingOnCancel()
        {
            using var env = new BattleFlowTestEnv();
            Configure(env);
            env.ConfigureAllyAction(0, "SK_Upgrade", "SE_Upgrade", "Physical", null, apCost: 5,
                effectType: "ApplyStatus", param1: Second);
            var skills = env.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID];
            skills[0].SkillID = 10;
            skills[0].SkillPhase = 1;
            skills[1].SkillID = 11;
            skills[1].SkillPhase = 1;
            var input = Start(env, Status("SE_Prepare_1", First), Status("SE_Prepare_2", Second));
            input.UIEnterSelectSkill(2);
            Choose(input, 1);
            input.UISelectSkillKey(2);
            Assert.That(env.Ally.GetBoundSkillID(2), Is.EqualTo("SK_Upgrade"));
            Assert.That(env.Ally.HasPendingEffectSequence, Is.False);
            Assert.That(env.Ally.PendingSelectEffect.SkillEffectID2, Is.EqualTo("SE_Upgrade"));
            input.UICancelSelection();
            Assert.That(env.Ally.GetBoundSkillID(2), Is.EqualTo(Skill));
            AssertUncommitted(env);
        }

        [Test]
        public void TwoDamageEffects_KeepDistinctTargetsAndDoNotDamageOnFirstConfirm()
        {
            using var env = new BattleFlowTestEnv();
            Configure(env);
            var first = Status("SE_Prepare_1", First);
            first.EffectType = "Damage";
            first.Element = "Physical";
            first.DamageType = "Skill";
            first.Param1 = "1*TotalATK,0";
            var second = Status("SE_Prepare_2", Second);
            second.EffectType = "Damage";
            second.Element = "Physical";
            second.DamageType = "Skill";
            second.Param1 = "2*TotalATK,0";
            var input = Start(env, first, second);
            var other = env.CreateEnemy(2);
            input.UIEnterSelectSkill(2);
            Choose(input, 1);
            AssertUncommitted(env);
            Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(1000));
            Assert.That(other.Entity.CurrentHP, Is.EqualTo(1000));
            Choose(input, 2);
            Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(950).Within(0.01f));
            Assert.That(other.Entity.CurrentHP, Is.EqualTo(900).Within(0.01f));
            Assert.That(input.IsSelecting, Is.False);
        }

        [Test]
        public void OneByOneTargets_FinishCurrentEffectThenCollectNextEffect()
        {
            using var env = new BattleFlowTestEnv();
            Configure(env);
            var first = Status("SE_Prepare_1", First);
            first.TargetNumber = 2;
            first.TargetConsecutive = 0;
            var input = Start(env, Status("SE_Prepare_0", Before, "Self", false),
                first, Status("SE_Prepare_2", Second));
            var other = env.CreateEnemy(2);
            input.UIEnterSelectSkill(2);
            Choose(input, 1);
            AssertUncommitted(env);
            Choose(input, 2);
            AssertUncommitted(env);
            Assert.That(env.Ally.PendingSelectEffect.SkillEffectID2, Is.EqualTo("SE_Prepare_2"));
            Choose(input, 2);
            Assert.That(env.Enemy.Entity.HasStatus(First), Is.True);
            Assert.That(other.Entity.HasStatus(First), Is.True);
            Assert.That(env.Enemy.Entity.HasStatus(Second), Is.False);
            Assert.That(other.Entity.HasStatus(Second), Is.True);
        }

        [TestCase("Self", 1)]
        [TestCase("Allies", 4)]
        [TestCase("Enemy", 5)]
        public void FixedSelfOrWholeSide_EntersOneCancelableConfirmationBeforeAnyCommit(string target, int count)
        {
            using var env = new BattleFlowTestEnv();
            Configure(env);
            var effect = Status("SE_Prepare_Confirm", First, target);
            effect.TargetNumber = count;
            effect.TargetConsecutive = 1;
            var input = Start(env, effect);
            input.UIEnterSelectSkill(2);
            Assert.That(input.IsSelecting, Is.True);
            Assert.That(input.Selector.CurrentSelection.Count, Is.EqualTo(count));
            AssertUncommitted(env);
            input.UICancelSelection();
            Assert.That(input.IsSelecting, Is.False);
            Assert.That(env.Ally.Entity.HasStatus(First), Is.False);
            AssertUncommitted(env);
        }

        [Test]
        public void FourAllyPositions_ConfirmOnceAndApplyOnlyToActualLivingMembers()
        {
            using var env = new BattleFlowTestEnv();
            Configure(env);
            var effect = Status("SE_Prepare_Party", First, "Allies");
            effect.TargetNumber = 4;
            var input = Start(env, effect);
            var other = env.CreateAlly(2);
            input.UIEnterSelectSkill(2);
            Assert.That(input.Selector.CurrentSelection, Is.EqualTo(new[] { 1, 2, 3, 4 }));
            AssertUncommitted(env);
            input.UIConfirmSelection();
            Assert.That(input.IsSelecting, Is.False);
            Assert.That(env.Ally.Entity.HasStatus(First), Is.True);
            Assert.That(other.Entity.HasStatus(First), Is.True);
            Assert.That(env.Enemy.Entity.HasStatus(First), Is.False);
            Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(80));
            Assert.That(env.Ally.Entity.CurrentEnergy, Is.EqualTo(20));
        }

        [Test]
        public void RandomTarget_WholeEnemyPreviewDoesNotForceAnActualWholeEnemyApplication()
        {
            using var env = new BattleFlowTestEnv();
            Configure(env);
            var effect = Status("SE_Prepare_Random", First);
            effect.TargetConsecutive = 3;
            var input = Start(env, effect);
            var second = env.CreateEnemy(2);
            var third = env.CreateEnemy(3);
            var random = new SequenceBattleRandomSource(intValues: new[] { 2 });
            BattleRandom.SetSource(random);
            input.UIEnterSelectSkill(2);
            Assert.That(input.Selector.CurrentSelection, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(random.IntCallCount, Is.Zero, "全体预览不提前抽实际随机目标");
            AssertUncommitted(env);
            input.UIConfirmSelection();
            Assert.That(input.IsSelecting, Is.False);
            Assert.That(env.Enemy.Entity.HasStatus(First), Is.False);
            Assert.That(second.Entity.HasStatus(First), Is.False);
            Assert.That(third.Entity.HasStatus(First), Is.True);
            Assert.That(random.IntCallCount, Is.EqualTo(1));
            Assert.That(random.FloatCallCount, Is.Zero);
        }

        [Test]
        public void SkillPhaseUpgrade_WithWholeEnemyTarget_StillWaitsForConfirmationAndCanCancel()
        {
            using var env = new BattleFlowTestEnv();
            Configure(env);
            env.ConfigureAllyAction(0, "SK_UpgradeAll", "SE_UpgradeAll", "Physical", null,
                apCost: 30, cooldown: 2, effectType: "ApplyStatus", param1: Second);
            var skills = env.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID];
            skills[0].SkillID = 10; skills[0].SkillPhase = 1;
            skills[1].SkillID = 11; skills[1].SkillPhase = 1; skills[1].ActionType = "Skill";
            skills[1].MaxCharge = 2; skills[1].InitialCharge = 2;
            env.DataManager.SkillEffectDict["SE_UpgradeAll"].TargetNumber = 5;
            var input = Start(env, Status("SE_Prepare_1", First));
            input.UIEnterSelectSkill(2);
            input.UISelectSkillKey(2);
            Assert.That(env.Ally.GetBoundSkillID(2), Is.EqualTo("SK_UpgradeAll"));
            Assert.That(input.IsSelecting, Is.True);
            Assert.That(env.Ally.PendingSelectEffect.SkillEffectID2, Is.EqualTo("SE_UpgradeAll"));
            Assert.That(input.Selector.CurrentSelection, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
            AssertUncommitted(env);
            input.UICancelSelection();
            Assert.That(env.Ally.GetBoundSkillID(2), Is.EqualTo(Skill));
            Assert.That(env.Ally.Entity.HasStatus(Second), Is.False);
            AssertUncommitted(env);
        }

        [Test]
        public void CyclicExplicitCall_IsRejectedBeforeSpendingOrExecuting()
        {
            using var env = new BattleFlowTestEnv();
            Configure(env);
            Start(env, new SkillEffectData
                { SkillEffectID2 = "SE_Prepare_1", EffectType = "ExecuteSkill", Param1 = Skill });
            Assert.Throws<InvalidOperationException>(() => env.Ally.GetFirstSelectEffect(2));
            AssertUncommitted(env);
        }
    }
}
