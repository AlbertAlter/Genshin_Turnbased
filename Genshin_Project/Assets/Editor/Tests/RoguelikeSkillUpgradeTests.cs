#if ROGUELIKE_SAVE_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class RoguelikeSkillUpgradeTests
    {
        [Test]
        public void FourSlots_CanUpgradeIndependently_WithoutDrawingRandom()
        {
            var source = RoguelikeUpgradeTests.Source();
            var random = new SystemBattleRandomSource(123);
            var draft = RoguelikeUpgradeTests.Draft(source, 0, random);
            for (int slot = 0; slot < 4; slot++)
            {
                var preview = draft.PreviewSkillUpgrade(1009, slot, 2);
                Assert.That(preview.SkillIndex, Is.EqualTo(slot));
                Assert.That(preview.Costs[0].Quantity, Is.EqualTo(3));
                draft.UpgradeSkill(1009, slot, 2);
            }
            var result = draft.ExportSnapshot();
            Assert.That(result.Characters[0].SkillLevels, Is.EqualTo(new[] { 2, 2, 2, 2 }));
            Assert.That(Count(result, "660401"), Is.EqualTo(88));
            Assert.That(source.Characters[0].SkillLevels, Is.EqualTo(new[] { 1, 1, 1, 1 }));
            Assert.That(Count(source, "660401"), Is.EqualTo(100));
            Assert.That(random.Step, Is.Zero);
            Assert.That(result.RandomStep, Is.EqualTo(source.RandomStep));
        }

        [Test]
        public void ConstellationBonus_DoesNotChangeBaseCost_OrOverwriteOtherSlots()
        {
            var source = RoguelikeUpgradeTests.Source();
            source.Characters[0].ConstellationLevel = 3;
            var draft = RoguelikeUpgradeTests.Draft(source, 0);
            var preview = draft.PreviewSkillUpgrade(1009, 2, 2);
            Assert.That(preview.PreviousBaseLevel, Is.EqualTo(1));
            Assert.That(preview.TargetBaseLevel, Is.EqualTo(2));
            Assert.That(preview.Costs.Count, Is.EqualTo(1));
            Assert.That(preview.Costs[0].ItemId, Is.EqualTo("660401"));
            Assert.That(preview.Costs[0].Quantity, Is.EqualTo(3));
            // 改动预览不能改写真正交易的计算结果。
            preview.Costs[0].Quantity = 99;
            draft.UpgradeSkill(1009, 2, 2);
            var result = draft.ExportSnapshot();
            Assert.That(result.Characters[0].SkillLevels, Is.EqualTo(new[] { 1, 1, 2, 1 }));
            Assert.That(result.Characters[0].ConstellationLevel, Is.EqualTo(3));
            Assert.That(Count(result, "660401"), Is.EqualTo(97));
        }

        [Test]
        public void MultipleLevels_ConsumeEachTierDirectly_IncludingCrownAtTen()
        {
            var draft = RoguelikeUpgradeTests.Draft(RoguelikeUpgradeTests.Source(), 6);
            var preview = draft.PreviewSkillUpgrade(1009, 3, 10);
            Assert.That(preview.CanAfford, Is.True);
            Assert.That(preview.Costs.Count, Is.EqualTo(4));
            Assert.That(preview.Costs[0].Quantity, Is.EqualTo(9));
            Assert.That(preview.Costs[1].Quantity, Is.EqualTo(19));
            Assert.That(preview.Costs[2].Quantity, Is.EqualTo(22));
            Assert.That(preview.Costs[3].Quantity, Is.EqualTo(1));
            draft.UpgradeSkill(1009, 3, 10);
            var result = draft.ExportSnapshot();
            Assert.That(result.Characters[0].SkillLevels[3], Is.EqualTo(10));
            Assert.That(Count(result, "660401"), Is.EqualTo(91));
            Assert.That(Count(result, "660402"), Is.EqualTo(81));
            Assert.That(Count(result, "660403"), Is.EqualTo(78));
            Assert.That(Count(result, "660404"), Is.EqualTo(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => draft.PreviewSkillUpgrade(1009, 3, 11));
        }

        [Test]
        public void StageCaps_AreTwoThreeFourSixEightNineTen_AndApplyToBaseOnly()
        {
            int[] caps = { 2, 3, 4, 6, 8, 9, 10 };
            for (int stage = 0; stage <= 6; stage++)
            {
                var source = RoguelikeUpgradeTests.Source();
                source.Characters[0].ConstellationLevel = 6;
                var draft = RoguelikeUpgradeTests.Draft(source, stage);
                Assert.That(draft.PreviewSkillUpgrade(1009, 1, caps[stage]).BaseLevelCap, Is.EqualTo(caps[stage]));
                Assert.Throws<ArgumentOutOfRangeException>(() => draft.PreviewSkillUpgrade(1009, 1, caps[stage] + 1));
                draft.UpgradeSkill(1009, 1, caps[stage]);
                Assert.That(draft.ExportSnapshot().Characters[0].SkillLevels[1], Is.EqualTo(caps[stage]));
            }
        }

        [Test]
        public void InsufficientCrown_PreviewStillShowsCost_AndNoPartialConsumptionIsExportable()
        {
            var source = RoguelikeUpgradeTests.Source();
            source.Inventory.Items.RemoveAll(item => item.ItemId == "660404");
            var draft = RoguelikeUpgradeTests.Draft(source, 6);
            var preview = draft.PreviewSkillUpgrade(1009, 0, 10);
            Assert.That(preview.CanAfford, Is.False);
            Assert.That(preview.Costs.Count, Is.EqualTo(4));
            Assert.Throws<InvalidOperationException>(() => draft.UpgradeSkill(1009, 0, 10));
            Assert.Throws<InvalidOperationException>(() => draft.ExportSnapshot());
            Assert.That(Count(source, "660401"), Is.EqualTo(100));
            Assert.That(source.Characters[0].SkillLevels[0], Is.EqualTo(1));
        }

        [Test]
        public void NoCrownRequiredBeforeTen_AndExactCostRemovesEmptyStack()
        {
            var source = RoguelikeUpgradeTests.Source();
            source.Characters[0].SkillLevels[0] = 9;
            source.Inventory.Items.Find(item => item.ItemId == "660404").Quantity = 1;
            var draft = RoguelikeUpgradeTests.Draft(source, 6);
            var preview = draft.PreviewSkillUpgrade(1009, 0, 10);
            Assert.That(preview.Costs.Count, Is.EqualTo(1));
            Assert.That(preview.Costs[0].ItemId, Is.EqualTo("660404"));
            draft.UpgradeSkill(1009, 0, 10);
            Assert.That(Count(draft.ExportSnapshot(), "660404"), Is.Zero);

            source = RoguelikeUpgradeTests.Source();
            source.Inventory.Items.RemoveAll(item => item.ItemId == "660404");
            draft = RoguelikeUpgradeTests.Draft(source, 5);
            Assert.That(draft.PreviewSkillUpgrade(1009, 0, 9).CanAfford, Is.True);
        }

        [Test]
        public void MaterialLookup_UsesConfiguredType_NotHardcodedItemIds()
        {
            var source = RoguelikeUpgradeTests.Source();
            var definitions = new List<RoguelikeItemDefinition>();
            for (int type = 1; type <= 4; type++)
            {
                string id = "66041" + type;
                definitions.Add(RoguelikeUpgradeTests.TalentMaterial(id, type));
                source.Inventory.Items.Add(RoguelikeUpgradeTests.Stack(id, 100));
            }
            var draft = new RoguelikeUpgradeTransaction(source, new RoguelikeUpgradeTests.EquipmentCatalog(),
                new RoguelikeItemCatalog(definitions), RoguelikeUpgradeTests.Requirements(), RoguelikeUpgradeTests.Config(), 6);
            Assert.That(draft.PreviewSkillUpgrade(1009, 0, 2).Costs[0].ItemId, Is.EqualTo("660411"));
            draft.UpgradeSkill(1009, 0, 2);
            Assert.That(Count(draft.ExportSnapshot(), "660411"), Is.EqualTo(97));
            Assert.That(Count(draft.ExportSnapshot(), "660401"), Is.EqualTo(100));
        }

        [Test]
        public void InvalidSlotTargetOwnerOrLegacyEffectiveLevel_AreRejected()
        {
            var source = RoguelikeUpgradeTests.Source();
            var draft = RoguelikeUpgradeTests.Draft(source, 6);
            Assert.Throws<ArgumentOutOfRangeException>(() => draft.PreviewSkillUpgrade(1009, -1, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => draft.PreviewSkillUpgrade(1009, 4, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => draft.PreviewSkillUpgrade(1009, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => draft.PreviewSkillUpgrade(1009, 0, 0));
            Assert.Throws<InvalidOperationException>(() => draft.PreviewSkillUpgrade(9999, 0, 2));
            source.Characters[0].SkillLevels[0] = 13;
            draft = RoguelikeUpgradeTests.Draft(source, 6);
            Assert.Throws<InvalidOperationException>(() => draft.PreviewSkillUpgrade(1009, 0, 10));
        }

        [Test]
        public void MissingMaterialDefinition_AndCaughtUpgradeFailure_CannotSaveEarlierChanges()
        {
            var source = RoguelikeUpgradeTests.Source();
            var draft = new RoguelikeUpgradeTransaction(source, new RoguelikeUpgradeTests.EquipmentCatalog(),
                new RoguelikeItemCatalog(new[] { RoguelikeUpgradeTests.TalentMaterial("660401", 1) }),
                RoguelikeUpgradeTests.Requirements(), RoguelikeUpgradeTests.Config(), 6);
            Assert.Throws<InvalidOperationException>(() => draft.PreviewSkillUpgrade(1009, 0, 4));

            source.Inventory.Items.Find(item => item.ItemId == "660401").Quantity = 3;
            draft = RoguelikeUpgradeTests.Draft(source, 0);
            draft.UpgradeSkill(1009, 0, 2);
            Assert.Throws<InvalidOperationException>(() => draft.UpgradeSkill(1009, 1, 2));
            Assert.Throws<InvalidOperationException>(() => draft.ExportSnapshot());
            Assert.That(source.Characters[0].SkillLevels, Is.EqualTo(new[] { 1, 1, 1, 1 }));
        }

        private static int Count(RoguelikeProgressSnapshot source, string id)
            => source.Inventory.Items.Find(item => item.ItemId == id)?.Quantity ?? 0;
    }
}
#endif
