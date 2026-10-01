#if ROGUELIKE_SAVE_TESTS
using System;
using System.Linq;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public class RoguelikeSaveServiceTests
    {
        private string root;
        private RoguelikeSaveService service;

        [SetUp]
        public void SetUp()
        {
            root = RoguelikeSaveTestFactory.CreateTemporaryRoot();
            service = RoguelikeSaveTestFactory.CreateService(root);
        }

        [TearDown]
        public void TearDown()
        {
            RoguelikeSaveTestFactory.DeleteTemporaryRoot(root);
        }

        [Test]
        public void ChapterOne_CanOnlyStartAsNewGame()
        {
            Assert.That(service.StartNewGame(RoguelikeSaveTestFactory.Snapshot(1)).Succeeded, Is.True);
            Assert.That(service.LoadChapterSlot(1, 1).Succeeded, Is.False);
        }

        [Test]
        public void EveryEntryChapter_ListsExactlyThreeSlots()
        {
            var result = service.ListChapterSlots(2);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Data.Count, Is.EqualTo(3));
            Assert.That(result.Data.Select(slot => slot.SlotIndex), Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void ActiveRun_PreventsStartingAnotherRun()
        {
            Assert.That(service.StartNewGame(RoguelikeSaveTestFactory.Snapshot(1)).Succeeded, Is.True);

            var secondStart = service.StartNewGame(RoguelikeSaveTestFactory.Snapshot(1, 1));

            Assert.That(secondStart.Succeeded, Is.False);
            Assert.That(secondStart.ErrorCode, Is.EqualTo(RoguelikeSaveErrorCode.ActiveRunAlreadyExists));
        }

        [Test]
        public void AbandonActiveRun_IsRequiredBeforeStartingAgain()
        {
            service.StartNewGame(RoguelikeSaveTestFactory.Snapshot(1));
            Assert.That(service.AbandonActiveRun().Succeeded, Is.True);
            Assert.That(service.HasActiveRun(), Is.False);
            Assert.That(service.StartNewGame(RoguelikeSaveTestFactory.Snapshot(1, 1)).Succeeded, Is.True);
        }

        [Test]
        public void CompletingChapterOne_CreatesChapterTwoEntryAndDeletesActiveRun()
        {
            service.StartNewGame(RoguelikeSaveTestFactory.Snapshot(1));

            var result = service.CompleteChapterAndSaveToNextSlot(
                1,
                2,
                RoguelikeSaveTestFactory.Snapshot(1, 1));

            Assert.That(result.Succeeded, Is.True);
            Assert.That(service.HasActiveRun(), Is.False);
            var entry = service.LoadChapterSlot(2, 2);
            Assert.That(entry.Succeeded, Is.True);
            Assert.That(entry.Data.EntryChapterId, Is.EqualTo(2));
            Assert.That(entry.Data.SourceCompletedChapterId, Is.EqualTo(1));
        }

        [Test]
        public void StartingFromPermanentSlot_DeepCopiesAndAdvancesSnapshot()
        {
            service.StartNewGame(RoguelikeSaveTestFactory.Snapshot(1));
            service.CompleteChapterAndSaveToNextSlot(1, 1, RoguelikeSaveTestFactory.Snapshot(1, 1));
            Assert.That(service.StartFromChapterSlot(2, 1).Succeeded, Is.True);

            var active = service.LoadActiveRun();
            active.Data.Snapshot.Characters[0].Level = 90;
            active.Data.Snapshot.Characters[0].SkillLevels[0] = 15;
            var permanent = service.LoadChapterSlot(2, 1);

            Assert.That(active.Data.CurrentChapterId, Is.EqualTo(2));
            Assert.That(active.Data.Snapshot.CurrentChapterId, Is.EqualTo(2));
            Assert.That(permanent.Data.Snapshot.CurrentChapterId, Is.EqualTo(1));
            Assert.That(permanent.Data.Snapshot.Characters[0].Level, Is.EqualTo(21));
            Assert.That(permanent.Data.Snapshot.Characters[0].SkillLevels[0], Is.EqualTo(2));
            Assert.That(active.Data.Snapshot.CharacterVitals[0].CurrentHealth, Is.EqualTo(1000f));
            Assert.That(active.Data.Snapshot.CharacterVitals[0].CurrentEnergy, Is.EqualTo(80f));
            Assert.That(permanent.Data.Snapshot.CharacterVitals[0].CurrentHealth, Is.EqualTo(799f));
            Assert.That(permanent.Data.Snapshot.CharacterVitals[0].CurrentEnergy, Is.EqualTo(31f));
        }

        [Test]
        public void ExistingTargetSlot_RequiresExplicitOverwriteConfirmation()
        {
            service.StartNewGame(RoguelikeSaveTestFactory.Snapshot(1));
            service.CompleteChapterAndSaveToNextSlot(1, 1, RoguelikeSaveTestFactory.Snapshot(1, 1));
            service.StartNewGame(RoguelikeSaveTestFactory.Snapshot(1, 2));

            var result = service.CompleteChapterAndSaveToNextSlot(
                1,
                1,
                RoguelikeSaveTestFactory.Snapshot(1, 2));

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(RoguelikeSaveErrorCode.OverwriteConfirmationRequired));
            Assert.That(service.HasActiveRun(), Is.True);
            Assert.That(service.CompleteChapterAndSaveToNextSlot(
                1,
                1,
                RoguelikeSaveTestFactory.Snapshot(1, 2),
                true).Succeeded, Is.True);
        }

        [Test]
        public void DifferentSlots_DoNotOverwriteEachOther()
        {
            service.StartNewGame(RoguelikeSaveTestFactory.Snapshot(1));
            service.CompleteChapterAndSaveToNextSlot(1, 1, RoguelikeSaveTestFactory.Snapshot(1, 1));
            service.StartNewGame(RoguelikeSaveTestFactory.Snapshot(1));
            service.CompleteChapterAndSaveToNextSlot(1, 2, RoguelikeSaveTestFactory.Snapshot(1, 2));

            Assert.That(service.LoadChapterSlot(2, 1).Data.Snapshot.CurrentStageIndex, Is.EqualTo(1));
            Assert.That(service.LoadChapterSlot(2, 2).Data.Snapshot.CurrentStageIndex, Is.EqualTo(2));
        }

        [Test]
        public void PermanentSaveFailure_PreservesActiveRun()
        {
            var fileSystem = new FailingReplaceFileSystem();
            service = RoguelikeSaveTestFactory.CreateService(root, fileSystem);
            service.StartNewGame(RoguelikeSaveTestFactory.Snapshot(1));
            fileSystem.FailChapterSlotWrites = true;

            var result = service.CompleteChapterAndSaveToNextSlot(
                1,
                1,
                RoguelikeSaveTestFactory.Snapshot(1, 1));

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(RoguelikeSaveErrorCode.IoFailure));
            Assert.That(service.HasActiveRun(), Is.True);
            Assert.That(service.LoadActiveRun().Data.ResumePoint, Is.EqualTo(RoguelikeResumePoint.ChapterCompleted));
        }

        [Test]
        public void Metadata_UsesLocalIsoTimeWithOffset()
        {
            service.StartNewGame(RoguelikeSaveTestFactory.Snapshot(1));
            string createdAt = service.LoadActiveRun().Data.Metadata.CreatedAt;

            Assert.That(DateTimeOffset.TryParse(createdAt, out var parsed), Is.True);
            Assert.That(parsed.Offset, Is.EqualTo(TimeSpan.FromHours(8)));
        }
    }
}
#endif
