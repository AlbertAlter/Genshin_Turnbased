#if ROGUELIKE_SAVE_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    internal static class RoguelikeSaveTestFactory
    {
        internal static readonly DateTimeOffset FixedNow =
            new DateTimeOffset(2026, 8, 18, 20, 30, 0, TimeSpan.FromHours(8));

        internal static string CreateTemporaryRoot()
        {
            return Path.Combine(
                Application.dataPath,
                RoguelikeSavePaths.ProjectSaveDirectoryName,
                "TestTemp",
                Guid.NewGuid().ToString("N"));
        }

        internal static RoguelikeSaveService CreateService(
            string root,
            IRoguelikeSaveFileSystem fileSystem = null)
        {
            int nextId = 0;
            var repository = new RoguelikeSaveRepository(
                new RoguelikeSavePaths(root),
                new RoguelikeSaveValidator(),
                fileSystem);
            return new RoguelikeSaveService(
                repository,
                "test-version",
                () => FixedNow.AddMinutes(nextId),
                () => "test-id-" + (++nextId));
        }

        internal static RoguelikeProgressSnapshot Snapshot(int chapterId, int marker = 0)
        {
            return new RoguelikeProgressSnapshot
            {
                Characters = new List<RoguelikeCharacterProgressData>
                {
                    new RoguelikeCharacterProgressData
                    {
                        CharacterID = 1009,
                        Level = 20 + marker,
                        Experience = 100 + marker,
                        Ascension = marker % 7,
                        ConstellationLevel = marker % 7,
                        SkillLevels = new[] { 1 + marker, 2 + marker, 3 + marker, 4 + marker },
                        EquippedWeaponInstanceId = "weapon-1",
                        EquippedArtifacts = new List<RoguelikeArtifactEquipmentData>
                        {
                            new RoguelikeArtifactEquipmentData
                            {
                                Slot = ArtifactSlot.Flower,
                                ArtifactInstanceId = "artifact-1",
                            },
                        },
                    },
                },
                CharacterVitals = new List<RoguelikeCharacterVitalData>
                {
                    new RoguelikeCharacterVitalData
                    {
                        CharacterID = 1009,
                        CurrentHealth = 800f - marker,
                        MaxHealth = 1000f,
                        CurrentEnergy = 30f + marker,
                        MaxEnergy = 80f,
                    },
                },
                PartyCharacterIds = new List<int> { 1009 },
                Inventory = new RoguelikeInventoryData
                {
                    Items = new List<RoguelikeItemStackData>
                    {
                        new RoguelikeItemStackData { ItemId = "mora", Quantity = 1000 + marker },
                    },
                    Weapons = new List<RoguelikeWeaponInstanceData>
                    {
                        new RoguelikeWeaponInstanceData
                        {
                            InstanceId = "weapon-1",
                            Experience = marker,
                            Weapon = new WeaponLoadout
                            {
                                WeaponID = 15001,
                                Level = 20,
                                Ascension = 1,
                                Refinement = 1,
                            },
                        },
                    },
                    Artifacts = new List<RoguelikeArtifactInstanceData>
                    {
                        new RoguelikeArtifactInstanceData
                        {
                            InstanceId = "artifact-1",
                            Experience = marker,
                            Artifact = new GeneratedArtifact
                            {
                                Star = 5,
                                Slot = ArtifactSlot.Flower,
                                Level = 4,
                                MaxLevel = 20,
                                MainAttributeType = "HP",
                                MainAttributeValue = 1000f,
                                SecondaryAttributes = new List<ArtifactSecondaryAttribute>
                                {
                                    new ArtifactSecondaryAttribute
                                    {
                                        AttributeType = "ATK",
                                        Value = 20f,
                                        RollCount = 1,
                                    },
                                },
                            },
                        },
                    },
                },
                CurrentChapterId = chapterId,
                CurrentStageIndex = marker,
                CurrentNodeId = "node-" + marker,
                ClearedNodeIds = new List<string> { "cleared-" + marker },
                SelectedRouteNodeIds = new List<string> { "route-" + marker },
                PendingRewardData = null,
                RunSeed = 123456,
                RandomStep = marker,
            };
        }

        internal static SaveMetadata Metadata(int schemaVersion = RoguelikeSaveValidator.CurrentSchemaVersion)
        {
            string timestamp = FixedNow.ToString("O");
            return new SaveMetadata
            {
                SchemaVersion = schemaVersion,
                SaveId = Guid.NewGuid().ToString("N"),
                CreatedAt = timestamp,
                UpdatedAt = timestamp,
                GameVersion = "test-version",
            };
        }

        internal static ChapterEntrySaveData ChapterEntry(int entryChapterId, int slotIndex, int marker = 0)
        {
            int completedChapterId = entryChapterId - 1;
            return new ChapterEntrySaveData
            {
                Metadata = Metadata(),
                EntryChapterId = entryChapterId,
                SlotIndex = slotIndex,
                SourceCompletedChapterId = completedChapterId,
                Snapshot = Snapshot(completedChapterId, marker),
            };
        }

        internal static void DeleteTemporaryRoot(string root)
        {
            if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    internal sealed class FailingReplaceFileSystem : IRoguelikeSaveFileSystem
    {
        private readonly SystemRoguelikeSaveFileSystem inner = new SystemRoguelikeSaveFileSystem();
        internal bool FailReplace { get; set; }
        internal bool FailChapterSlotWrites { get; set; }

        public bool FileExists(string path) { return inner.FileExists(path); }
        public void CreateDirectory(string path) { inner.CreateDirectory(path); }
        public string ReadAllText(string path) { return inner.ReadAllText(path); }
        public void WriteAllText(string path, string contents) { inner.WriteAllText(path, contents); }
        public void MoveFile(string sourcePath, string destinationPath)
        {
            if (FailChapterSlotWrites && destinationPath.Contains(Path.DirectorySeparatorChar + "chapters" + Path.DirectorySeparatorChar))
                throw new IOException("测试注入：永久槽首次提交失败。");
            inner.MoveFile(sourcePath, destinationPath);
        }
        public void DeleteFile(string path) { inner.DeleteFile(path); }

        public void ReplaceFile(string sourcePath, string destinationPath, string backupPath)
        {
            if (FailReplace || (FailChapterSlotWrites && destinationPath.Contains(Path.DirectorySeparatorChar + "chapters" + Path.DirectorySeparatorChar)))
                throw new IOException("测试注入：原子替换失败。");
            inner.ReplaceFile(sourcePath, destinationPath, backupPath);
        }
    }
}
#endif
