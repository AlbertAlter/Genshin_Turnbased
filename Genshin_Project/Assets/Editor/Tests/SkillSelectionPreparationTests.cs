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
            env.ConfigureAllyAction(2, Skill, "SE_Prepare", "None", null, cooldown: 2,
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
            env.Ally.SetSkillEffects(Skill, new List<SkillEffectData>(effects));
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
            env.ConfigureAllyAction(0, "SK_Child", "SE_Child", "None", null, apCost: 0,
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
            env.ConfigureAllyAction(0, "SK_Other", "SE_Other", "None", null, apCost: 5,
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
            env.ConfigureAllyAction(0, "SK_Upgrade", "SE_Upgrade", "None", null, apCost: 5,
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
            first.Element = "None";
            first.DamageType = "Skill";
            first.Param1 = "1*TotalATK,0";
            var second = Status("SE_Prepare_2", Second);
            second.EffectType = "Damage";
            second.Element = "None";
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
