#if ROGUELIKE_SAVE_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class RoguelikeUpgradeTests
    {
        private static readonly RoguelikeItemStackData[] Empty = new RoguelikeItemStackData[0];

        [Test]
        public void CharacterCost_IsConsumedOnce_AndOverflowSurvivesUntilUnlock()
        {
            var source = Source();
            source.Characters[0].Level = 19;
            source.Characters[0].Experience = 90;
            var draft = Draft(source, 0);
            var result = draft.UpgradeCharacter(1009, Cost("660101", 1));
            Assert.That(result.Level, Is.EqualTo(20));
            Assert.That(result.Experience, Is.EqualTo(15));
            var capped = draft.ExportSnapshot();
            Assert.That(capped.Characters[0].Ascension, Is.Zero);
            Assert.That(Count(capped, "660101"), Is.EqualTo(99));
            Assert.That(source.Characters[0].Level, Is.EqualTo(19));
            Assert.That(Count(source, "660101"), Is.EqualTo(100));
            var unlocked = Draft(capped, 1);
            unlocked.ReconcileUnlockedProgress();
            Assert.That(unlocked.ExportSnapshot().Characters[0].Ascension, Is.EqualTo(1));
            Assert.That(unlocked.ExportSnapshot().Characters[0].Experience, Is.EqualTo(15));
        }

        [Test]
        public void StoredOverflow_CanLevelWithoutAdditionalCost_AndLaterThresholdAutoActivates()
        {
            var source = Source();
            source.Characters[0].Level = 20; source.Characters[0].Experience = 250;
            var draft = Draft(source, 1);
            draft.ReconcileUnlockedProgress();
            var result = draft.ExportSnapshot();
            Assert.That(result.Characters[0].Level, Is.EqualTo(22));
            Assert.That(result.Characters[0].Experience, Is.EqualTo(50));
            Assert.That(result.Characters[0].Ascension, Is.EqualTo(1));
            source.Characters[0].Level = 19; source.Characters[0].Experience = 90;
            draft = Draft(source, 1);
            draft.UpgradeCharacter(1009, Cost("660101", 1));
            Assert.That(draft.ExportSnapshot().Characters[0].Ascension, Is.EqualTo(1));
        }

        [Test]
        public void SelectionLimits_RejectExcess_AndAccountForMixedSizes()
        {
            var source = Source();
            source.Characters[0].Level = 19; source.Characters[0].Experience = 10;
            var draft = Draft(source, 0);
            var target = RoguelikeUpgradeTarget.Character(1009);
            Assert.That(draft.GetAdditionalMaterialLimit(target, Empty, "660101"), Is.EqualTo(4));
            Assert.That(draft.GetAdditionalMaterialLimit(target, Empty, "660102"), Is.EqualTo(1));
            Assert.That(draft.GetAdditionalMaterialLimit(target, Cost("660101", 1), "660102"), Is.Zero);
            Assert.Throws<InvalidOperationException>(() => draft.Preview(target, Cost("660101", 5)));
            Assert.Throws<InvalidOperationException>(() => draft.Preview(target,
                new[] { Stack("660101", 1), Stack("660102", 1) }));
            Assert.That(draft.Preview(target, Cost("660101", 4)).Result.Experience, Is.EqualTo(10));
        }

        [Test]
        public void InvalidMaterialOrCaughtFailure_CannotCommitPartialProgress()
        {
            var source = Source();
            var draft = Draft(source, 1);
            draft.UpgradeCharacter(1009, Cost("660101", 4));
            Assert.Throws<InvalidOperationException>(() => draft.UpgradeCharacter(1009, Cost("660201", 1)));
            Assert.Throws<InvalidOperationException>(() => draft.ExportSnapshot());
            Assert.That(source.Characters[0].Level, Is.EqualTo(1));
            Assert.That(Count(source, "660101"), Is.EqualTo(100));
        }

        [Test]
        public void Duplicates_AreSummed_AndStockCheckedBeforeUpgrade()
        {
            var draft = Draft(Source(), 0);
            draft.UpgradeCharacter(1009, new[] { Stack("660101", 2), Stack("660101", 2) });
            Assert.That(draft.ExportSnapshot().Characters[0].Level, Is.EqualTo(2));
            Assert.That(Count(draft.ExportSnapshot(), "660101"), Is.EqualTo(96));
            Assert.Throws<InvalidOperationException>(() => draft.UpgradeCharacter(1009, Cost("660101", 100)));
        }

        [Test]
        public void Weapon_UsesTemplateStar_AndPreservesInstanceAndRefinement()
        {
            var source = Source();
            source.Inventory.Weapons[0].Weapon.Refinement = 4;
            var draft = Draft(source, 0);
            draft.UpgradeWeapon("weapon-1", Cost("660201", 4));
            var result = draft.ExportSnapshot();
            Assert.That(result.Inventory.Weapons[0].Weapon.Level, Is.EqualTo(2));
            Assert.That(result.Inventory.Weapons[0].Experience, Is.EqualTo(10));
            Assert.That(result.Inventory.Weapons[0].Weapon.Refinement, Is.EqualTo(4));
            Assert.That(result.Characters[0].EquippedWeaponInstanceId, Is.EqualTo("weapon-1"));
            Assert.Throws<InvalidOperationException>(() => draft.UpgradeWeapon("missing", Cost("660201", 1)));
        }

        [Test]
        public void TrueMaximum_DiscardsRemainder_AndCannotConsumeMore()
        {
            var source = Source();
            source.Characters[0].Level = 89; source.Characters[0].Experience = 90;
            var draft = Draft(source, 6);
            draft.UpgradeCharacter(1009, Cost("660101", 1));
            Assert.That(draft.ExportSnapshot().Characters[0].Level, Is.EqualTo(90));
            Assert.That(draft.ExportSnapshot().Characters[0].Experience, Is.Zero);
            Assert.That(draft.ExportSnapshot().Characters[0].Ascension, Is.EqualTo(6));
            Assert.Throws<InvalidOperationException>(() => draft.UpgradeCharacter(1009, Cost("660101", 1)));
        }

        [Test]
        public void ArtifactPreview_DoesNotDraw_UpgradeUsesExistingInstance_AndTwoDrawsPerMilestone()
        {
            var source = Source();
            var random = new SystemBattleRandomSource(42);
            var draft = Draft(source, 0, random);
            var preview = draft.Preview(RoguelikeUpgradeTarget.Artifact("artifact-1"), Cost("660301", 8));
            Assert.That(preview.Result.Level, Is.EqualTo(4));
            Assert.That(random.Step, Is.Zero);
            draft.UpgradeArtifact("artifact-1", Cost("660301", 8));
            var result = draft.ExportSnapshot();
            Assert.That(random.Step, Is.EqualTo(2));
            Assert.That(result.RandomStep, Is.EqualTo(2));
            Assert.That(result.Inventory.Artifacts[0].Artifact.MainAttributeValue, Is.EqualTo(140));
            Assert.That(result.Inventory.Artifacts[0].Artifact.SecondaryAttributes.Count, Is.EqualTo(2));
            Assert.That(result.Inventory.Artifacts[0].Artifact.SecondaryAttributes[0].Value, Is.EqualTo(20));
            Assert.That(result.Inventory.Artifacts[0].ArtifactSetID, Is.EqualTo(15001));
            Assert.That(result.Characters[0].EquippedArtifacts[0].ArtifactInstanceId, Is.EqualTo("artifact-1"));
            var second = Draft(source, 0, new SystemBattleRandomSource(42));
            second.UpgradeArtifact("artifact-1", Cost("660301", 8));
            Assert.That(RoguelikeSaveJson.Serialize(second.ExportSnapshot()),
                Is.EqualTo(RoguelikeSaveJson.Serialize(result)));
        }

        [Test]
        public void ArtifactFullSubstats_ReinforcesWithoutRegenerating_AndRarityCapsApply()
        {
            var source = Source();
            var artifact = source.Inventory.Artifacts[0].Artifact;
            artifact.Star = 3; artifact.MaxLevel = 12; artifact.Level = 11;
            artifact.SecondaryAttributes.Add(new ArtifactSecondaryAttribute { AttributeType = "DEF", Value = 10, RollCount = 1 });
            artifact.SecondaryAttributes.Add(new ArtifactSecondaryAttribute { AttributeType = "EM", Value = 10, RollCount = 1 });
            artifact.SecondaryAttributes.Add(new ArtifactSecondaryAttribute { AttributeType = "CR", Value = 10, RollCount = 1 });
            var random = new SystemBattleRandomSource(7);
            var draft = Draft(source, 6, random);
            draft.UpgradeArtifact("artifact-1", Cost("660301", 2));
            var result = draft.ExportSnapshot().Inventory.Artifacts[0];
            Assert.That(result.Artifact.Level, Is.EqualTo(12));
            Assert.That(result.Experience, Is.Zero);
            Assert.That(result.Artifact.SecondaryAttributes.Count, Is.EqualTo(4));
            int rolls = 0;
            foreach (var stat in result.Artifact.SecondaryAttributes) rolls += stat.RollCount;
            Assert.That(rolls, Is.EqualTo(5));
            Assert.That(random.Step, Is.EqualTo(2));
        }

        [Test]
        public void Constellation_ConsumesOnlyMatchingItem_AndNeverWritesSkillBonusBack()
        {
            var source = Source();
            var draft = Draft(source, 0);
            for (int i = 0; i < 6; i++) draft.ActivateConstellation(1009);
            var result = draft.ExportSnapshot();
            Assert.That(result.Characters[0].ConstellationLevel, Is.EqualTo(6));
            Assert.That(Count(result, "6100901"), Is.Zero);
            Assert.That(result.Characters[0].SkillLevels, Is.EqualTo(new[] { 1, 1, 1, 1 }));
            Assert.Throws<InvalidOperationException>(() => draft.ActivateConstellation(1009));
        }

        [Test]
        public void SkillCosts_AreBaseOneToTwo_NotDisplayedFourToFive()
        {
            var costs = LevelUpCalculator.GetSkillMaterialsToReach(Requirements(), 1, 2);
            Assert.That(costs.Count, Is.EqualTo(1));
            Assert.That(costs[1], Is.EqualTo(3));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                LevelUpCalculator.GetSkillMaterialsToReach(Requirements(), 10, 11));
        }

        internal static RoguelikeProgressSnapshot Source()
        {
            var source = RoguelikeSaveTestFactory.Snapshot(1);
            source.Characters[0].Level = 1; source.Characters[0].Experience = 0;
            source.Characters[0].SkillLevels = new[] { 1, 1, 1, 1 };
            source.Inventory.Weapons[0].Weapon.Level = 1;
            source.Inventory.Weapons[0].Weapon.Ascension = 0;
            source.Inventory.Artifacts[0].Artifact.Level = 0;
            source.Inventory.Artifacts[0].Artifact.MainAttributeValue = 100;
            source.Inventory.Artifacts[0].ArtifactSetID = 15001;
            source.Inventory.Items = new List<RoguelikeItemStackData>
            {
                Stack("660101", 100), Stack("660102", 100), Stack("660201", 100),
                Stack("660301", 100), Stack("6100901", 6),
                Stack("660401", 100), Stack("660402", 100), Stack("660403", 100), Stack("660404", 2),
            };
            return source;
        }

        internal static RoguelikeUpgradeTransaction Draft(RoguelikeProgressSnapshot source, int unlocked,
            IBattleRandomSource random = null)
            => new RoguelikeUpgradeTransaction(source, new EquipmentCatalog(), Items(), Requirements(), Config(), unlocked, random);

        internal static LevelUpRequirementsTable Requirements()
        {
            var table = new LevelUpRequirementsTable();
            for (int level = 2; level <= 90; level++)
            {
                table.AddCharacter(level, 100);
                for (int star = 3; star <= 5; star++) table.AddWeapon(star, level, star * 30);
            }
            for (int star = 3; star <= 5; star++)
                for (int level = 1; level <= (star == 3 ? 12 : star == 4 ? 16 : 20); level++)
                    table.AddArtifact(star, level, 50);
            int[] amounts = { 3, 6, 4, 6, 9, 4, 6, 12, 1 };
            int[] types = { 1, 1, 2, 2, 2, 3, 3, 3, 4 };
            for (int level = 2; level <= 10; level++) table.AddSkill(level, amounts[level - 2], types[level - 2]);
            table.Validate();
            return table;
        }

        internal static ArtifactGenerationConfig Config()
        {
            var config = new ArtifactGenerationConfig();
            for (int star = 3; star <= 5; star++)
            {
                config.AddMaxLevel(star, star == 3 ? 12 : star == 4 ? 16 : 20);
                config.AddMainGrowth("HP", star, 100, 10);
            }
            for (int slot = 1; slot <= 5; slot++) config.AddMainWeight((ArtifactSlot)slot, "HP", 1);
            foreach (string type in new[] { "ATK", "DEF", "EM", "CR", "CD" }) config.AddSecondaryRule(type, 1, 10);
            return config;
        }

        internal static RoguelikeItemCatalog Items() => new RoguelikeItemCatalog(new[]
        {
            Material("660101", "CharacterExperience", 25), Material("660102", "CharacterExperience", 125),
            Material("660201", "WeaponExperience", 40), Material("660301", "ArtifactExperience", 25),
            new RoguelikeItemDefinition { ItemId = "6100901", Name = "命之座", Category = "Constellation", ReferenceID = 1009 },
            TalentMaterial("660401", 1), TalentMaterial("660402", 2),
            TalentMaterial("660403", 3), TalentMaterial("660404", 4),
        });
        internal static RoguelikeItemDefinition TalentMaterial(string id, int type)
            => new RoguelikeItemDefinition { ItemId = id, Name = id, Category = "TalentMaterial", TalentMaterialType = type };
        private static RoguelikeItemDefinition Material(string id, string category, int exp)
            => new RoguelikeItemDefinition { ItemId = id, Name = id, Category = category, ExperienceValue = exp };
        internal static RoguelikeItemStackData Stack(string id, int quantity)
            => new RoguelikeItemStackData { ItemId = id, Quantity = quantity };
        internal static RoguelikeItemStackData[] Cost(string id, int quantity) => new[] { Stack(id, quantity) };
        private static int Count(RoguelikeProgressSnapshot source, string id)
            => source.Inventory.Items.Find(item => item.ItemId == id)?.Quantity ?? 0;

        internal sealed class EquipmentCatalog : IRoguelikeEquipmentCatalog
        {
            public bool TryGetWeapon(int id, out WeaponAttributesData definition)
            {
                definition = id == 15001 ? new WeaponAttributesData { WeaponID = id, WeaponType = 0, Star = 5 } : null;
                return definition != null;
            }
            public bool TryGetCharacterWeaponType(int id, out int type) { type = 0; return id == 1009; }
        }
    }
    public sealed class RoguelikeUpgradeServiceTests
    {
        [Test]
        public void SaveFailure_DoesNotConsumeOrNotify_RetryDoesNotReroll_StaleStepCannotExecute()
        {
            var files = new MemoryFiles();
            var saves = RoguelikeSaveTestFactory.CreateService("upgrade-memory-tests", files);
            var flow = new RoguelikeFlowService(saves, new RouteRules());
            Assert.That(flow.StartNewGame(RoguelikeUpgradeTests.Source()).Succeeded, Is.True);
            var random = new SystemBattleRandomSource(42);
            var upgrades = new RoguelikeUpgradeService(flow, new RoguelikeUpgradeTests.EquipmentCatalog(),
                RoguelikeUpgradeTests.Items(), RoguelikeUpgradeTests.Requirements(), RoguelikeUpgradeTests.Config(),
                new Gate(), random);
            int notifications = 0;
            upgrades.Changed += snapshot =>
            {
                Assert.That(flow.Resume().Data.Snapshot.Inventory.Artifacts[0].Artifact.Level, Is.EqualTo(4));
                notifications++;
            };
            string step = flow.Resume().Data.Flow.StepId;
            RoguelikeUpgradeTransaction escaped = null;
            files.FailReplace = true;
            var failure = upgrades.Commit(step, draft =>
            {
                escaped = draft;
                draft.UpgradeArtifact("artifact-1", RoguelikeUpgradeTests.Cost("660301", 8));
            });
            Assert.That(failure.Succeeded, Is.False);
            Assert.That(notifications, Is.Zero);
            Assert.That(random.Step, Is.EqualTo(2));
            Assert.That(flow.Resume().Data.Snapshot.Inventory.Artifacts[0].Artifact.Level, Is.Zero);
            Assert.That(flow.Resume().Data.Snapshot.Inventory.Items.Find(item => item.ItemId == "660301").Quantity,
                Is.EqualTo(100));
            Assert.That(flow.Resume().Data.Flow.StepId, Is.EqualTo(step));
            Assert.Throws<InvalidOperationException>(() => escaped.ExportSnapshot());

            files.FailReplace = false;
            Assert.That(upgrades.Commit(step, draft => draft.UpgradeArtifact("artifact-1",
                RoguelikeUpgradeTests.Cost("660301", 8))).Succeeded, Is.True);
            Assert.That(random.Step, Is.EqualTo(2));
            Assert.That(notifications, Is.EqualTo(1));
            bool called = false;
            Assert.That(upgrades.Commit(step, draft => called = true).Succeeded, Is.False);
            Assert.That(called, Is.False);
            Assert.That(random.Step, Is.EqualTo(2));
        }

        [Test]
        public void InsufficientMaterials_DoNotDraw_AndBatchFailureDoesNotSaveEarlierUpgrade()
        {
            var flow = new RoguelikeFlowService(
                RoguelikeSaveTestFactory.CreateService("upgrade-memory-tests", new MemoryFiles()), new RouteRules());
            Assert.That(flow.StartNewGame(RoguelikeUpgradeTests.Source()).Succeeded, Is.True);
            var random = new SystemBattleRandomSource(9);
            var upgrades = new RoguelikeUpgradeService(flow, new RoguelikeUpgradeTests.EquipmentCatalog(),
                RoguelikeUpgradeTests.Items(), RoguelikeUpgradeTests.Requirements(), RoguelikeUpgradeTests.Config(),
                new Gate(), random);
            var result = upgrades.Commit(flow.Resume().Data.Flow.StepId, draft =>
            {
                draft.UpgradeCharacter(1009, RoguelikeUpgradeTests.Cost("660101", 4));
                try { draft.UpgradeArtifact("artifact-1", RoguelikeUpgradeTests.Cost("660301", 101)); }
                catch (InvalidOperationException) { }
            });
            Assert.That(result.Succeeded, Is.False);
            Assert.That(random.Step, Is.Zero);
            Assert.That(flow.Resume().Data.Snapshot.Characters[0].Level, Is.EqualTo(1));
        }

        [Test]
        public void SkillUpgrade_SaveFailureRollsBack_AndSuccessCannotBeSubmittedTwice()
        {
            var files = new MemoryFiles();
            var flow = new RoguelikeFlowService(
                RoguelikeSaveTestFactory.CreateService("upgrade-memory-tests", files), new RouteRules());
            Assert.That(flow.StartNewGame(RoguelikeUpgradeTests.Source()).Succeeded, Is.True);
            var random = new SystemBattleRandomSource(19);
            var upgrades = new RoguelikeUpgradeService(flow, new RoguelikeUpgradeTests.EquipmentCatalog(),
                RoguelikeUpgradeTests.Items(), RoguelikeUpgradeTests.Requirements(), RoguelikeUpgradeTests.Config(),
                new Gate(), random);
            int notifications = 0;
            upgrades.Changed += snapshot => notifications++;
            string step = flow.Resume().Data.Flow.StepId;
            files.FailReplace = true;
            Assert.That(upgrades.Commit(step, draft => draft.UpgradeSkill(1009, 2, 2)).Succeeded, Is.False);
            var before = flow.Resume().Data.Snapshot;
            Assert.That(before.Characters[0].SkillLevels[2], Is.EqualTo(1));
            Assert.That(before.Inventory.Items.Find(item => item.ItemId == "660401").Quantity, Is.EqualTo(100));
            Assert.That(notifications, Is.Zero);
            Assert.That(flow.Resume().Data.Flow.StepId, Is.EqualTo(step));
            files.FailReplace = false;
            Assert.That(upgrades.Commit(step, draft => draft.UpgradeSkill(1009, 2, 2)).Succeeded, Is.True);
            var after = flow.Resume().Data.Snapshot;
            Assert.That(after.Characters[0].SkillLevels[2], Is.EqualTo(2));
            Assert.That(after.Inventory.Items.Find(item => item.ItemId == "660401").Quantity, Is.EqualTo(97));
            Assert.That(notifications, Is.EqualTo(1));
            bool invoked = false;
            Assert.That(upgrades.Commit(step, draft => invoked = true).Succeeded, Is.False);
            Assert.That(invoked, Is.False);
            Assert.That(random.Step, Is.Zero);
        }

        private sealed class Gate : IRoguelikeUpgradeGate
        { public int GetUnlockedAscensions(RoguelikeProgressSnapshot snapshot) => 0; }

        private sealed class RouteRules : IRoguelikeRouteRules
        {
            public RoguelikeSaveResult ValidateNodeEntry(RoguelikeProgressSnapshot snapshot, RoguelikeNodeSelection node)
                => RoguelikeSaveResult.Success();
            public RoguelikeSaveResult ValidateChapterCompletion(RoguelikeProgressSnapshot snapshot)
                => RoguelikeSaveResult.Success();
        }

        private sealed class MemoryFiles : IRoguelikeSaveFileSystem
        {
            private readonly Dictionary<string, string> data = new Dictionary<string, string>();
            public bool FailReplace;
            public bool FileExists(string path) => data.ContainsKey(path);
            public void CreateDirectory(string path) { }
            public string ReadAllText(string path) => data[path];
            public void WriteAllText(string path, string contents) { data[path] = contents; }
            public void MoveFile(string source, string destination)
            { data.Add(destination, data[source]); data.Remove(source); }
            public void ReplaceFile(string source, string destination, string backup)
            {
                if (FailReplace) throw new System.IO.IOException("测试保存失败");
                data[backup] = data[destination]; data[destination] = data[source]; data.Remove(source);
            }
            public void DeleteFile(string path) { data.Remove(path); }
        }
    }
}
#endif
