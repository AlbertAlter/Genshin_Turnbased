#if ROGUELIKE_SAVE_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class RoguelikeGameServiceTests
    {
        private MemoryFiles files;
        private RoguelikeSavePaths paths;
        private RoguelikeGameService game;
        private int notifications;
        [SetUp]
        public void SetUp()
        {
            files = new MemoryFiles();
            paths = new RoguelikeSavePaths(Path.Combine(UnityEngine.Application.dataPath, "SaveData", "TestTemp", "GameServiceInMemory"));
            game = CreateGame();
            Assert.That(game.StartNewGame(new RoguelikeProgressSnapshot { CurrentChapterId = 1, RunSeed = 12345 }).Succeeded, Is.True);
            notifications = 0; game.Changed += () => notifications++;
        }
        private RoguelikeGameService CreateGame()
            => new RoguelikeGameService(new RoguelikeSaveService(new RoguelikeSaveRepository(paths, fileSystem: files)),
                new Routes(), new Catalog(), new Catalog(), new Catalog());
        private string Step() => game.Continue().Data.Flow.StepId;
        private RoguelikeInventoryView View() => game.ReadInventory().Data;

        [Test]
        public void AcquireCharacterAndDuplicate_PersistOneCharacterAndOneConstellationItem()
        {
            Assert.That(game.AcquireCharacter(Step(), 1009).Succeeded, Is.True);
            string stale = Step();
            Assert.That(game.AcquireCharacter(stale, 1009).Succeeded, Is.True);
            Assert.That(game.AcquireCharacter(stale, 1009).Succeeded, Is.False);
            Assert.That(View().GetCharacters().Count, Is.EqualTo(1));
            Assert.That(View().FindCharacter(1009).ConstellationLevel, Is.Zero);
            Assert.That(View().GetItemCount(RoguelikeItemIds.Constellation(1009)), Is.EqualTo(1));
            Assert.That(CreateGame().ReadInventory().Data.GetItemCount(RoguelikeItemIds.Constellation(1009)), Is.EqualTo(1));
            Assert.That(notifications, Is.EqualTo(2));
        }
        [Test]
        public void OneBatchCanContainTheSameCharacterMoreThanOnce()
        {
            Assert.That(game.Acquire(Step(), new RoguelikeAcquisition { Characters = new List<int> { 1009, 1009, 1009 } }).Succeeded, Is.True);
            Assert.That(View().GetCharacters().Count, Is.EqualTo(1));
            Assert.That(View().GetItemCount(RoguelikeItemIds.Constellation(1009)), Is.EqualTo(2));
        }
        [Test]
        public void BatchFailureDoesNotKeepEarlierCharacterOrItemAwards()
        {
            string step = Step();
            var result = game.Acquire(step, new RoguelikeAcquisition
            {
                Characters = new List<int> { 1009 },
                Items = new List<RoguelikeItemStackData> { new RoguelikeItemStackData { ItemId = "unknown", Quantity = 1 } }
            });
            Assert.That(result.Succeeded, Is.False); Assert.That(View().GetCharacters(), Is.Empty);
            Assert.That(Step(), Is.EqualTo(step)); Assert.That(notifications, Is.Zero);
        }
        [Test]
        public void SaveFailureRollsBackAwardsAndCanBeRetriedOnce()
        {
            string step = Step(); files.FailReplace = true;
            Assert.That(game.AcquireCharacter(step, 1009).Succeeded, Is.False);
            Assert.That(View().GetCharacters(), Is.Empty); Assert.That(notifications, Is.Zero);
            files.FailReplace = false;
            Assert.That(game.AcquireCharacter(step, 1009).Succeeded, Is.True);
            Assert.That(View().GetItemCount(RoguelikeItemIds.Constellation(1009)), Is.Zero);
        }
        [Test]
        public void EquipmentTransferRequiresConfirmationAndPersistsBothDirections()
        {
            Assert.That(game.Acquire(Step(), new RoguelikeAcquisition { Characters = new List<int> { 1009, 1010 } }).Succeeded, Is.True);
            Assert.That(game.AcquireWeapon(Step(), 15001).Succeeded, Is.True);
            string id = View().GetWeapons()[0].InstanceId;
            Assert.That(game.EquipWeapon(Step(), 1009, id).Succeeded, Is.True);
            Assert.That(game.EquipWeapon(Step(), 1010, id).Succeeded, Is.False);
            Assert.That(game.EquipWeapon(Step(), 1010, id, true).Succeeded, Is.True);
            var restored = CreateGame().ReadInventory().Data;
            Assert.That(restored.GetWeaponOwner(id), Is.EqualTo(1010));
            Assert.That(restored.GetEquippedWeapon(1009), Is.Null);
            Assert.That(restored.GetEquippedWeapon(1010).InstanceId, Is.EqualTo(id));
        }
        [Test]
        public void InvalidTransferDoesNotUnequipTheOriginalOwner()
        {
            game.AcquireCharacter(Step(), 1009); game.AcquireWeapon(Step(), 15001);
            string id = View().GetWeapons()[0].InstanceId; game.EquipWeapon(Step(), 1009, id);
            Assert.That(game.EquipWeapon(Step(), 9999, id, true).Succeeded, Is.False);
            Assert.That(View().GetWeaponOwner(id), Is.EqualTo(1009));
        }
        [Test]
        public void ArtifactTransferAndUnequipKeepTheOriginalGeneratedInstance()
        {
            game.Acquire(Step(), new RoguelikeAcquisition { Characters = new List<int> { 1009, 1010 } });
            var artifact = new GeneratedArtifact { Slot = ArtifactSlot.Flower, Star = 5, MaxLevel = 20,
                MainAttributeType = "HP", MainAttributeValue = 1234, SecondaryAttributes = new List<ArtifactSecondaryAttribute>() };
            Assert.That(game.AcquireArtifact(Step(), 42, artifact).Succeeded, Is.True);
            string id = View().GetArtifacts()[0].InstanceId; game.EquipArtifact(Step(), 1009, id);
            Assert.That(game.EquipArtifact(Step(), 1010, id).Succeeded, Is.False);
            Assert.That(game.EquipArtifact(Step(), 1010, id, true).Succeeded, Is.True);
            Assert.That(View().GetEquippedArtifacts(1009), Is.Empty);
            Assert.That(View().GetArtifactOwner(id), Is.EqualTo(1010));
            Assert.That(game.UnequipArtifact(Step(), 1010, ArtifactSlot.Flower).Succeeded, Is.True);
            Assert.That(View().GetArtifactOwner(id), Is.Zero);
            Assert.That(View().FindArtifact(id).Artifact.MainAttributeValue, Is.EqualTo(1234));
        }
        [Test]
        public void CostAndOutcomesAreCommittedTogether()
        {
            game.AcquireItem(Step(), "mora", 100);
            var costs = new[] { new RoguelikeItemStackData { ItemId = "mora", Quantity = 30 } };
            var outcome = new RoguelikeAcquisition { Characters = new List<int> { 1009 } };
            Assert.That(game.CommitAcquisition(Step(), costs, outcome, RoguelikeSaveReason.GachaCompleted).Succeeded, Is.True);
            Assert.That(View().GetItemCount("mora"), Is.EqualTo(70)); Assert.That(View().FindCharacter(1009), Is.Not.Null);
        }
        [Test]
        public void EquipmentAndInventoryCannotOverwriteTheBeforeBattleCheckpoint()
        {
            game.AcquireCharacter(Step(), 1009);
            game.EnterNode(Step(), new RoguelikeNodeSelection { NodeId = "A", StageIndex = 1, ContentId = "Battle", Kind = RoguelikeNodeKind.Battle });
            game.BeginBattle(Step(), "Battle"); string step = Step();
            Assert.That(game.AcquireCharacter(step, 1009).Succeeded, Is.False);
            Assert.That(game.SetParty(step, new[] { 1009 }).Succeeded, Is.False);
            Assert.That(game.SaveCurrentProgress().Succeeded, Is.True);
            Assert.That(game.RetryCurrentBattle().Data.BattleId, Is.EqualTo("Battle"));
            Assert.That(View().GetItemCount(RoguelikeItemIds.Constellation(1009)), Is.Zero);
        }
        [Test]
        public void BackupRestoreIsExplicitAndPreservesTheGoodBackup()
        {
            game.AcquireCharacter(Step(), 1009);
            game.AcquireItem(Step(), "mora", 10);
            string backupPath = RoguelikeSavePaths.GetBackupPath(paths.ActiveRunPath);
            string backup = files.Contents[backupPath]; files.Contents[paths.ActiveRunPath] = "broken";
            Assert.That(game.ReadActiveSummary().ErrorCode, Is.EqualTo(RoguelikeSaveErrorCode.BackupAvailable));
            Assert.That(game.RestoreActiveBackup(false).Succeeded, Is.False);
            Assert.That(files.Contents[paths.ActiveRunPath], Is.EqualTo("broken"));
            Assert.That(game.RestoreActiveBackup(true).Succeeded, Is.True);
            Assert.That(files.Contents[backupPath], Is.EqualTo(backup));
            Assert.That(View().FindCharacter(1009), Is.Not.Null);
            Assert.That(game.RestoreActiveBackup(true).Succeeded, Is.False); // no rollback of healthy saves
        }
        [Test]
        public void SummaryAndReadResultsAreDetachedAndAbandonChecksRunIdentity()
        {
            game.AcquireCharacter(Step(), 1009); game.SetParty(Step(), new[] { 1009 });
            game.ReadActiveSummary().Data.PartyCharacterIds.Clear();
            View().FindCharacter(1009).Level = 90;
            Assert.That(game.Continue().Data.Snapshot.PartyCharacterIds.Count, Is.EqualTo(1));
            Assert.That(View().FindCharacter(1009).Level, Is.EqualTo(1));
            Assert.That(game.Abandon("old-run", true).Succeeded, Is.False);
            Assert.That(game.Abandon(game.Continue().Data.RunId, false).Succeeded, Is.False);
        }

        [Test]
        public void ChapterSlotRoundTripPreservesEquipmentAndDoesNotMutateTheEntrySave()
        {
            game.AcquireCharacter(Step(), 1009); game.AcquireWeapon(Step(), 15001);
            string id = View().GetWeapons()[0].InstanceId;
            game.EquipWeapon(Step(), 1009, id); game.SetParty(Step(), new[] { 1009 });
            Assert.That(game.CompleteChapter(Step()).Succeeded, Is.True);
            Assert.That(game.SaveCompletedChapterToSlot(Step(), 1).Succeeded, Is.True);
            Assert.That(game.ReadActiveSummary().ErrorCode, Is.EqualTo(RoguelikeSaveErrorCode.NotFound));
            string entryPath = paths.GetChapterSlotPath(2, 1), original = files.Contents[entryPath];
            Assert.That(game.ListChapterSlots(2).Data[0].Summary.CharacterCount, Is.EqualTo(1));
            Assert.That(game.StartFromChapterSlot(2, 1).Succeeded, Is.True);
            Assert.That(game.Continue().Data.Snapshot.CurrentChapterId, Is.EqualTo(2));
            Assert.That(View().GetWeaponOwner(id), Is.EqualTo(1009));
            Assert.That(game.AcquireCharacter(Step(), 1009).Succeeded, Is.True);
            Assert.That(files.Contents[entryPath], Is.EqualTo(original));
        }

        [Test]
        public void ChapterWriteFailureKeepsTheCompletedActiveRun()
        {
            game.CompleteChapter(Step()); string step = Step(); files.FailSlotWrites = true;
            Assert.That(game.SaveCompletedChapterToSlot(step, 1).Succeeded, Is.False);
            Assert.That(game.Continue().Data.Flow.Step, Is.EqualTo(RoguelikeFlowStep.ChapterCompleted));
            Assert.That(files.FileExists(paths.ActiveRunPath), Is.True);
        }

        [Test]
        public void RewardAndNodeCompletionCommitTogetherAndOldCallbackCannotAwardAgain()
        {
            game.EnterNode(Step(), new RoguelikeNodeSelection { NodeId = "A", StageIndex = 1, ContentId = "Battle", Kind = RoguelikeNodeKind.Battle });
            game.BeginBattle(Step(), "Battle"); game.RecordBattleVictory(Step());
            game.SavePendingReward(Step(), new PendingRewardData { RewardId = "reward-A" });
            string step = Step(); var outcome = new RoguelikeAcquisition { Characters = new List<int> { 1009 } };
            Assert.That(game.CompleteNode(step, outcome).Succeeded, Is.True);
            Assert.That(game.CompleteNode(step, outcome).Succeeded, Is.False);
            Assert.That(game.Continue().Data.Snapshot.ClearedNodeIds.Contains("A"), Is.True);
            Assert.That(game.Continue().Data.Snapshot.PendingRewardData, Is.Null);
            Assert.That(View().FindCharacter(1009), Is.Not.Null);
            Assert.That(View().GetItemCount(RoguelikeItemIds.Constellation(1009)), Is.Zero);
        }

        [Test]
        public void TransferReleasesTheTargetsOldWeaponWithoutDeletingIt()
        {
            game.Acquire(Step(), new RoguelikeAcquisition { Characters = new List<int> { 1009, 1010 } });
            game.AcquireWeapon(Step(), 15001); game.AcquireWeapon(Step(), 15001);
            var weapons = View().GetWeapons(); string a = weapons[0].InstanceId, b = weapons[1].InstanceId;
            game.EquipWeapon(Step(), 1009, a); game.EquipWeapon(Step(), 1010, b);
            Assert.That(game.EquipWeapon(Step(), 1010, a, true).Succeeded, Is.True);
            Assert.That(View().GetWeaponOwner(a), Is.EqualTo(1010)); Assert.That(View().GetWeaponOwner(b), Is.Zero);
            Assert.That(View().FindWeapon(b), Is.Not.Null); Assert.That(View().GetWeapons().Count, Is.EqualTo(2));
        }

        [Test]
        public void DuplicateAfterSixConstellationsStillAwardsAnItemWithoutResettingProgress()
        {
            game.AcquireCharacter(Step(), 1009);
            var save = RoguelikeSaveJson.Deserialize<ActiveRunSaveData>(files.Contents[paths.ActiveRunPath]);
            save.Snapshot.Characters[0].ConstellationLevel = 6;
            save.Snapshot.Characters[0].Level = 20; save.Snapshot.Characters[0].Experience = 33;
            files.Contents[paths.ActiveRunPath] = RoguelikeSaveJson.Serialize(save);
            Assert.That(game.AcquireCharacter(Step(), 1009).Succeeded, Is.True);
            var character = View().FindCharacter(1009);
            Assert.That(character.ConstellationLevel, Is.EqualTo(6)); Assert.That(character.Level, Is.EqualTo(20));
            Assert.That(character.Experience, Is.EqualTo(33));
            Assert.That(View().GetItemCount(RoguelikeItemIds.Constellation(1009)), Is.EqualTo(1));
        }

        [Test]
        public void TwoServiceInstancesCannotCommitTheSameStepTwice()
        {
            var other = CreateGame(); string step = Step();
            var a = System.Threading.Tasks.Task.Run(() => game.AcquireCharacter(step, 1009));
            var b = System.Threading.Tasks.Task.Run(() => other.AcquireCharacter(step, 1009));
            System.Threading.Tasks.Task.WaitAll(a, b);
            Assert.That((a.Result.Succeeded ? 1 : 0) + (b.Result.Succeeded ? 1 : 0), Is.EqualTo(1));
            Assert.That(View().GetCharacters().Count, Is.EqualTo(1));
            Assert.That(View().GetItemCount(RoguelikeItemIds.Constellation(1009)), Is.Zero);
        }

        private sealed class Routes : IRoguelikeRouteRules
        {
            public RoguelikeSaveResult ValidateNodeEntry(RoguelikeProgressSnapshot a, RoguelikeNodeSelection b) => RoguelikeSaveResult.Success();
            public RoguelikeSaveResult ValidateChapterCompletion(RoguelikeProgressSnapshot a) => RoguelikeSaveResult.Success();
        }
        private sealed class Catalog : IRoguelikeItemCatalog, IRoguelikeEquipmentCatalog, IRoguelikeCharacterAcquisitionCatalog
        {
            public bool TryGetInitialLimits(int id, out float hp, out float energy) { hp = 100; energy = 0; return id == 1009 || id == 1010; }
            public bool TryGetCharacterWeaponType(int id, out int type) { type = 0; return id == 1009 || id == 1010; }
            public bool TryGetWeapon(int id, out WeaponAttributesData value) { value = new WeaponAttributesData { WeaponID = id, WeaponType = 0 }; return id == 15001; }
            public bool TryGetItem(string id, out RoguelikeItemDefinition value)
            {
                value = null;
                if (id == "mora") value = new RoguelikeItemDefinition { ItemId = id };
                if (id == RoguelikeItemIds.Weapon(15001)) value = new RoguelikeItemDefinition { ItemId = id, ItemType = RoguelikeItemType.Weapon, Stackable = false, ReferenceID = 15001 };
                if (id == RoguelikeItemIds.Artifact(42, ArtifactSlot.Flower)) value = new RoguelikeItemDefinition { ItemId = id, ItemType = RoguelikeItemType.Artifact, Stackable = false, ReferenceID = 42 };
                foreach (int character in new[] { 1009, 1010 })
                    if (id == RoguelikeItemIds.Constellation(character)) value = new RoguelikeItemDefinition { ItemId = id, Category = "Constellation", ReferenceID = character };
                return value != null;
            }
        }
        private sealed class MemoryFiles : IRoguelikeSaveFileSystem
        {
            public readonly Dictionary<string, string> Contents = new Dictionary<string, string>();
            public bool FailReplace;
            public bool FailSlotWrites;
            public bool FileExists(string path) => Contents.ContainsKey(path);
            public void CreateDirectory(string path) { }
            public string ReadAllText(string path) => Contents[path];
            public void WriteAllText(string path, string text) => Contents[path] = text;
            public void DeleteFile(string path) => Contents.Remove(path);
            public void MoveFile(string source, string target)
            {
                if (FailSlotWrites && target.Contains("chapters")) throw new IOException("Injected chapter write failure");
                Contents.Add(target, Contents[source]); Contents.Remove(source);
            }
            public void ReplaceFile(string source, string target, string backup)
            {
                if (FailReplace) throw new IOException("Injected failure");
                if (backup != null) Contents[backup] = Contents[target];
                Contents[target] = Contents[source]; Contents.Remove(source);
            }
        }
    }
}
#endif
