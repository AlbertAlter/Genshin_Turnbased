#if ROGUELIKE_SAVE_TESTS
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public class RoguelikeSaveLifecycleTests
    {
        private string root;
        private RoguelikeSaveService service;

        [SetUp]
        public void SetUp()
        {
            root = RoguelikeSaveTestFactory.CreateTemporaryRoot();
            service = RoguelikeSaveTestFactory.CreateService(root);
            Assert.That(service.StartNewGame(RoguelikeSaveTestFactory.Snapshot(1)).Succeeded, Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            RoguelikeSaveTestFactory.DeleteTemporaryRoot(root);
        }

        [Test]
        public void SaveBeforeBattle_PersistsLatestProgress()
        {
            var beforeBattle = RoguelikeSaveTestFactory.Snapshot(1, 1);

            Assert.That(service.SaveBeforeBattle(beforeBattle, "battle-001").Succeeded, Is.True);
            var loaded = service.LoadActiveRun();

            Assert.That(loaded.Data.ResumePoint, Is.EqualTo(RoguelikeResumePoint.BeforeBattle));
            Assert.That(loaded.Data.PendingBattleId, Is.EqualTo("battle-001"));
            Assert.That(loaded.Data.Snapshot.CurrentStageIndex, Is.EqualTo(1));
        }

        [Test]
        public void RetryBattle_ReturnsUnmodifiedBeforeBattleSnapshot()
        {
            var beforeBattle = RoguelikeSaveTestFactory.Snapshot(1, 1);
            service.SaveBeforeBattle(beforeBattle, "battle-001");
            beforeBattle.Characters[0].Level = 90;
            beforeBattle.CurrentStageIndex = 99;

            var retry = service.RetryCurrentBattle();

            Assert.That(retry.Succeeded, Is.True);
            Assert.That(retry.Data.Snapshot.Characters[0].Level, Is.EqualTo(21));
            Assert.That(retry.Data.Snapshot.CurrentStageIndex, Is.EqualTo(1));
        }

        [Test]
        public void BattleFailureOrExit_LeavesBeforeBattleSaveAndDoesNotDeleteIt()
        {
            service.SaveBeforeBattle(RoguelikeSaveTestFactory.Snapshot(1, 2), "battle-002");

            var exitResult = service.ExitBattleToMenu();
            var loaded = service.LoadActiveRun();

            Assert.That(exitResult.Succeeded, Is.True);
            Assert.That(service.HasActiveRun(), Is.True);
            Assert.That(loaded.Data.ResumePoint, Is.EqualTo(RoguelikeResumePoint.BeforeBattle));
            Assert.That(loaded.Data.PendingBattleId, Is.EqualTo("battle-002"));
            Assert.That(loaded.Data.Snapshot.CurrentStageIndex, Is.EqualTo(2));
        }

        [Test]
        public void BattleVictory_ImmediatelyRefreshesActiveRun()
        {
            service.SaveBeforeBattle(RoguelikeSaveTestFactory.Snapshot(1, 1), "battle-001");

            var result = service.SaveAfterBattleVictory(
                RoguelikeSaveTestFactory.Snapshot(1, 2),
                "battle-001");
            var loaded = service.LoadActiveRun();

            Assert.That(result.Succeeded, Is.True);
            Assert.That(loaded.Data.ResumePoint, Is.EqualTo(RoguelikeResumePoint.BattleVictory));
            Assert.That(loaded.Data.LastSaveReason, Is.EqualTo(RoguelikeSaveReason.BattleVictory));
            Assert.That(loaded.Data.Snapshot.CurrentStageIndex, Is.EqualTo(2));
        }

        [TestCase(RoguelikeSaveReason.RewardChosen)]
        [TestCase(RoguelikeSaveReason.GachaCompleted)]
        [TestCase(RoguelikeSaveReason.CharacterUpgraded)]
        [TestCase(RoguelikeSaveReason.EquipmentChanged)]
        [TestCase(RoguelikeSaveReason.RouteChosen)]
        [TestCase(RoguelikeSaveReason.EventCompleted)]
        public void ProgressAction_ImmediatelyRefreshesActiveRun(RoguelikeSaveReason reason)
        {
            var changed = RoguelikeSaveTestFactory.Snapshot(1, 3);

            var result = service.SaveAfterProgressAction(changed, reason);
            var loaded = service.LoadActiveRun();

            Assert.That(result.Succeeded, Is.True);
            Assert.That(loaded.Data.LastSaveReason, Is.EqualTo(reason));
            Assert.That(loaded.Data.Snapshot.CurrentStageIndex, Is.EqualTo(3));
        }

        [Test]
        public void ProgressAction_DuringBattleIsRejectedAndBeforeBattleSaveRemains()
        {
            service.SaveBeforeBattle(RoguelikeSaveTestFactory.Snapshot(1, 1), "battle-001");

            var result = service.SaveAfterProgressAction(
                RoguelikeSaveTestFactory.Snapshot(1, 5),
                RoguelikeSaveReason.CharacterUpgraded);
            var loaded = service.LoadActiveRun();

            Assert.That(result.Succeeded, Is.False);
            Assert.That(loaded.Data.ResumePoint, Is.EqualTo(RoguelikeResumePoint.BeforeBattle));
            Assert.That(loaded.Data.Snapshot.CurrentStageIndex, Is.EqualTo(1));
        }

        [Test]
        public void PendingReward_CanBeAbsentPersistedAndCleared()
        {
            Assert.That(service.LoadActiveRun().Data.Snapshot.PendingRewardData, Is.Null);
            var snapshot = RoguelikeSaveTestFactory.Snapshot(1, 1);

            Assert.That(service.SavePendingReward(
                snapshot,
                new PendingRewardData { RewardId = "reward-001" }).Succeeded, Is.True);
            Assert.That(service.LoadActiveRun().Data.Snapshot.PendingRewardData.RewardId, Is.EqualTo("reward-001"));

            snapshot.PendingRewardData = null;
            Assert.That(service.SaveAfterProgressAction(snapshot, RoguelikeSaveReason.RewardChosen).Succeeded, Is.True);
            Assert.That(service.LoadActiveRun().Data.Snapshot.PendingRewardData, Is.Null);
        }

        [Test]
        public void EmptyPendingRewardIdentifier_IsRejected()
        {
            var result = service.SavePendingReward(
                RoguelikeSaveTestFactory.Snapshot(1),
                new PendingRewardData());

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(RoguelikeSaveErrorCode.ValidationFailed));
        }

        [Test]
        public void ManualExit_RefreshesWithoutDeletingActiveRun()
        {
            var changed = RoguelikeSaveTestFactory.Snapshot(1, 4);

            Assert.That(service.SaveActiveRun(
                changed,
                RoguelikeResumePoint.ChapterRoute,
                RoguelikeSaveReason.ManualExit).Succeeded, Is.True);

            Assert.That(service.HasActiveRun(), Is.True);
            Assert.That(service.LoadActiveRun().Data.LastSaveReason, Is.EqualTo(RoguelikeSaveReason.ManualExit));
            Assert.That(service.LoadActiveRun().Data.Snapshot.CurrentStageIndex, Is.EqualTo(4));
        }
    }
}
#endif
