#if ROGUELIKE_SAVE_TESTS
using System.IO;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public class RoguelikeSaveRepositoryTests
    {
        private string root;
        private RoguelikeSavePaths paths;
        private RoguelikeSaveRepository repository;

        [SetUp]
        public void SetUp()
        {
            root = RoguelikeSaveTestFactory.CreateTemporaryRoot();
            paths = new RoguelikeSavePaths(root);
            repository = new RoguelikeSaveRepository(paths);
        }

        [TearDown]
        public void TearDown()
        {
            RoguelikeSaveTestFactory.DeleteTemporaryRoot(root);
        }

        [Test]
        public void ChapterRuntimeData_AllFieldsRoundTrip()
        {
            var save = RoguelikeSaveTestFactory.ChapterEntry(2, 1, 2);

            Assert.That(repository.SaveChapterSlot(save).Succeeded, Is.True);
            var loaded = repository.LoadChapterSlot(2, 1);

            Assert.That(loaded.Succeeded, Is.True);
            var character = loaded.Data.Snapshot.Characters[0];
            Assert.That(character.CharacterID, Is.EqualTo(1009));
            Assert.That(character.Level, Is.EqualTo(22));
            Assert.That(character.Experience, Is.EqualTo(102));
            Assert.That(character.Ascension, Is.EqualTo(2));
            Assert.That(character.ConstellationLevel, Is.EqualTo(2));
            Assert.That(character.SkillLevels, Is.EqualTo(new[] { 3, 4, 5, 6 }));
            Assert.That(character.EquippedWeaponInstanceId, Is.EqualTo("weapon-1"));
            Assert.That(character.EquippedArtifacts[0].ArtifactInstanceId, Is.EqualTo("artifact-1"));
            Assert.That(loaded.Data.Snapshot.CharacterVitals[0].CurrentHealth, Is.EqualTo(798f));
            Assert.That(loaded.Data.Snapshot.PartyCharacterIds, Is.EqualTo(new[] { 1009 }));
            Assert.That(loaded.Data.Snapshot.Inventory.Items[0].Quantity, Is.EqualTo(1002));
            Assert.That(loaded.Data.Snapshot.Inventory.Weapons[0].Weapon.WeaponID, Is.EqualTo(15001));
            Assert.That(loaded.Data.Snapshot.Inventory.Artifacts[0].Artifact.Slot, Is.EqualTo(ArtifactSlot.Flower));
        }

        [Test]
        public void RunSeedAndRandomStep_RoundTrip()
        {
            var save = RoguelikeSaveTestFactory.ChapterEntry(2, 1, 4);
            repository.SaveChapterSlot(save);

            var loaded = repository.LoadChapterSlot(2, 1);

            Assert.That(loaded.Data.Snapshot.RunSeed, Is.EqualTo(123456));
            Assert.That(loaded.Data.Snapshot.RandomStep, Is.EqualTo(4));
        }

        [Test]
        public void CorruptedFormalFile_ReturnsBackupAvailableWithoutOverwriting()
        {
            var first = RoguelikeSaveTestFactory.ChapterEntry(2, 1, 0);
            var second = RoguelikeSaveTestFactory.ChapterEntry(2, 1, 1);
            Assert.That(repository.SaveChapterSlot(first).Succeeded, Is.True);
            Assert.That(repository.SaveChapterSlot(second).Succeeded, Is.True);
            File.WriteAllText(paths.GetChapterSlotPath(2, 1), "{broken-json");

            var result = repository.LoadChapterSlot(2, 1);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(RoguelikeSaveErrorCode.BackupAvailable));
            Assert.That(result.Data.Snapshot.CurrentStageIndex, Is.EqualTo(0));
            Assert.That(File.ReadAllText(paths.GetChapterSlotPath(2, 1)), Is.EqualTo("{broken-json"));
        }

        [Test]
        public void CorruptedFormalAndBackup_ReturnExplicitFailure()
        {
            repository.SaveChapterSlot(RoguelikeSaveTestFactory.ChapterEntry(2, 1, 0));
            repository.SaveChapterSlot(RoguelikeSaveTestFactory.ChapterEntry(2, 1, 1));
            string formalPath = paths.GetChapterSlotPath(2, 1);
            File.WriteAllText(formalPath, "broken-formal");
            File.WriteAllText(RoguelikeSavePaths.GetBackupPath(formalPath), "broken-backup");

            var result = repository.LoadChapterSlot(2, 1);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(RoguelikeSaveErrorCode.BothCopiesCorrupted));
        }

        [Test]
        public void AtomicReplaceFailure_PreservesOldFormalFile()
        {
            var failingFileSystem = new FailingReplaceFileSystem();
            repository = new RoguelikeSaveRepository(paths, fileSystem: failingFileSystem);
            Assert.That(repository.SaveChapterSlot(RoguelikeSaveTestFactory.ChapterEntry(2, 1, 0)).Succeeded, Is.True);
            failingFileSystem.FailReplace = true;

            var failedSave = repository.SaveChapterSlot(RoguelikeSaveTestFactory.ChapterEntry(2, 1, 1));
            failingFileSystem.FailReplace = false;
            var loaded = repository.LoadChapterSlot(2, 1);

            Assert.That(failedSave.Succeeded, Is.False);
            Assert.That(failedSave.ErrorCode, Is.EqualTo(RoguelikeSaveErrorCode.IoFailure));
            Assert.That(loaded.Succeeded, Is.True);
            Assert.That(loaded.Data.Snapshot.CurrentStageIndex, Is.EqualTo(0));
        }

        [Test]
        public void NewerSchemaFile_IsNotMisreadByOlderCode()
        {
            var save = RoguelikeSaveTestFactory.ChapterEntry(2, 1);
            save.Metadata.SchemaVersion = RoguelikeSaveValidator.CurrentSchemaVersion + 1;
            Directory.CreateDirectory(paths.GetChapterDirectory(2));
            File.WriteAllText(paths.GetChapterSlotPath(2, 1), RoguelikeSaveJson.Serialize(save));

            var result = repository.LoadChapterSlot(2, 1);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(RoguelikeSaveErrorCode.UnsupportedSchema));
        }
    }
}
#endif
