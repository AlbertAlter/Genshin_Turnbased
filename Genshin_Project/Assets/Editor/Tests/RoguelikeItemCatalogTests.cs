#if ROGUELIKE_SAVE_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class RoguelikeItemCatalogTests
    {
        [Test]
        public void UpgradeMaterials_UseConfiguredValues_AndRejectCrossTargetExperience()
        {
            var book = new RoguelikeItemDefinition
            { ItemId = "660103", Name = "大英雄的经验", Category = "CharacterExperience", ExperienceValue = 500 };
            var crown = new RoguelikeItemDefinition
            { ItemId = "660404", Name = "智识之冕", Category = "TalentMaterial", TalentMaterialType = 4 };
            var catalog = new RoguelikeItemCatalog(new[] { book, crown });
            Assert.That(catalog.TryGetExperienceValue(book.ItemId, LevelUpTarget.Character, out int exp), Is.True);
            Assert.That(exp, Is.EqualTo(500));
            Assert.That(catalog.TryGetExperienceValue(book.ItemId, LevelUpTarget.Weapon, out exp), Is.False);
            Assert.That(exp, Is.Zero);
            Assert.That(catalog.TryGetExperienceValue(crown.ItemId, LevelUpTarget.Character, out _), Is.False);
            Assert.That(catalog.TryGetTalentMaterial(4, out var found), Is.True);
            Assert.That(found.ItemId, Is.EqualTo("660404"));
            found.TalentMaterialType = 1;
            catalog.TryGetTalentMaterial(4, out found);
            Assert.That(found.TalentMaterialType, Is.EqualTo(4));
            Assert.That(catalog.TryGetTalentMaterial(5, out _), Is.False);
        }

        [Test]
        public void UpgradeMaterials_RejectZeroExperience_AndMixedOrDuplicateTalentTypes()
        {
            var item = new RoguelikeItemDefinition
            { ItemId = "660101", Name = "流浪者的经验", Category = "CharacterExperience" };
            Assert.Throws<ArgumentException>(() => new RoguelikeItemCatalog(new[] { item }));
            item.Category = "TalentMaterial";
            item.TalentMaterialType = 1;
            item.ExperienceValue = 25;
            Assert.Throws<ArgumentException>(() => new RoguelikeItemCatalog(new[] { item }));
            item.ExperienceValue = 0;
            var duplicate = new RoguelikeItemDefinition
            { ItemId = "660402", Name = "重复种类", Category = "TalentMaterial", TalentMaterialType = 1 };
            Assert.Throws<InvalidDataException>(() => new RoguelikeItemCatalog(new[] { item, duplicate }));
        }

        [TestCase(ArtifactSlot.Flower, "650101")]
        [TestCase(ArtifactSlot.Plume, "650102")]
        [TestCase(ArtifactSlot.Sands, "650103")]
        [TestCase(ArtifactSlot.Goblet, "650104")]
        [TestCase(ArtifactSlot.Circlet, "650105")]
        public void ArtifactId_UsesTwoDigitSlotSuffix(ArtifactSlot slot, string expected)
        {
            Assert.That(RoguelikeItemIds.Artifact(501, slot), Is.EqualTo(expected));
            Assert.That(RoguelikeItemIds.Constellation(1009), Is.EqualTo("6100901"));
            Assert.That(RoguelikeItemIds.Weapon(3001), Is.EqualTo("63001"));
        }

        [Test]
        public void Catalog_RejectsDuplicateIds_AndInvalidTypeOrStacking()
        {
            Assert.Throws<InvalidDataException>(() => new RoguelikeItemCatalog(new[] { Weapon(), Weapon() }));
            var invalid = Weapon();
            invalid.Stackable = true;
            Assert.Throws<ArgumentException>(() => new RoguelikeItemCatalog(new[] { invalid }));
            invalid = Flower();
            invalid.Slot = 2;
            Assert.Throws<ArgumentException>(() => new RoguelikeItemCatalog(new[] { invalid }));
        }

        [Test]
        public void Catalog_DoesNotExposeMutableDefinitions()
        {
            var definition = Weapon();
            var catalog = new RoguelikeItemCatalog(new[] { definition });
            definition.Name = "modified";
            Assert.That(catalog.TryGetItem("63001", out var first), Is.True);
            Assert.That(first.Name, Is.EqualTo("飞天御剑"));
            first.Name = "modified again";
            catalog.TryGetItem("63001", out var second);
            Assert.That(second.Name, Is.EqualTo("飞天御剑"));
            Assert.That(catalog.TryGetItem("unknown", out _), Is.False);
        }

        [Test]
        public void Inventory_StacksConstellation_AndKeepsEquipmentSeparate()
        {
            var source = new RoguelikeProgressSnapshot { CurrentChapterId = 1 };
            var draft = new RoguelikeInventoryTransaction(source, new Equipment(), Catalog());
            draft.AddItems(new[] { Stack(2), Stack(3) });
            draft.ConsumeItems(new[] { Stack(1) });
            string w1 = draft.AddWeapon(3001), w2 = draft.AddWeapon(3001);
            string a1 = draft.AddArtifact(501, Generated()), a2 = draft.AddArtifact(501, Generated());
            var view = draft.Read();
            Assert.That(view.GetItemCount("6100901"), Is.EqualTo(4));
            Assert.That(view.GetEntries().Count, Is.EqualTo(5));
            Assert.That(w1, Is.Not.EqualTo(w2));
            Assert.That(a1, Is.Not.EqualTo(a2));
            Assert.That(view.FindArtifact(a1).ArtifactSetID, Is.EqualTo(501));
            Assert.That(view.GetEntries().Count(e => e.ItemId == "650101"), Is.EqualTo(2));
            view.GetEntries()[0].Quantity = 999;
            Assert.That(draft.Read().GetItemCount("6100901"), Is.EqualTo(4));
        }

        [TestCase("63001")]
        [TestCase("650101")]
        [TestCase("699999")]
        public void Inventory_RejectsEquipmentOrUnknownIdsInStacks(string itemId)
        {
            var source = new RoguelikeProgressSnapshot { CurrentChapterId = 1 };
            var draft = new RoguelikeInventoryTransaction(source, new Equipment(), Catalog());
            Assert.Throws<InvalidOperationException>(() => draft.AddItems(new[]
            { new RoguelikeItemStackData { ItemId = itemId, Quantity = 2 } }));
            Assert.Throws<InvalidOperationException>(() => draft.ExportSnapshot());
            Assert.That(source.Inventory.Items, Is.Empty);
        }

        [Test]
        public void Inventory_NewCatalogRequiresArtifactSet_ButLegacyDoesNotInventOne()
        {
            var source = new RoguelikeProgressSnapshot { CurrentChapterId = 1 };
            var draft = new RoguelikeInventoryTransaction(source, new Equipment(), Catalog());
            Assert.Throws<ArgumentOutOfRangeException>(() => draft.AddArtifact(Generated()));
            var legacy = new RoguelikeInventoryTransaction(source, new Equipment());
            string id = legacy.AddArtifact(Generated());
            Assert.That(legacy.Read().FindArtifact(id).ArtifactSetID, Is.Zero);
            Assert.That(legacy.Read().GetEntries()[0].ItemId, Is.Null);
        }

        private static RoguelikeItemCatalog Catalog()
        {
            return new RoguelikeItemCatalog(new[]
            {
                new RoguelikeItemDefinition
                {
                    ItemId = "6100901", Name = "安柏命之座", Category = "Constellation",
                    ItemType = RoguelikeItemType.Stack, ReferenceID = 1009, Stackable = true,
                }, Weapon(), Flower(),
            });
        }
        private static RoguelikeItemDefinition Weapon()
        {
            return new RoguelikeItemDefinition
            {
                ItemId = "63001", Name = "飞天御剑", Category = "Weapon", ItemType = RoguelikeItemType.Weapon,
                ReferenceID = 3001, Stackable = false, Rarity = 3,
            };
        }
        private static RoguelikeItemDefinition Flower()
        {
            return new RoguelikeItemDefinition
            {
                ItemId = "650101", Name = "行者之心·生之花", Category = "Artifact", ItemType = RoguelikeItemType.Artifact,
                ReferenceID = 501, Slot = 1, Stackable = false,
            };
        }
        private static RoguelikeItemStackData Stack(int amount)
        { return new RoguelikeItemStackData { ItemId = "6100901", Quantity = amount }; }
        private static GeneratedArtifact Generated()
        {
            return new GeneratedArtifact
            {
                Star = 4, Slot = ArtifactSlot.Flower, MaxLevel = 16,
                MainAttributeType = "HPBonusFlat", MainAttributeValue = 645,
            };
        }
        private sealed class Equipment : IRoguelikeEquipmentCatalog
        {
            public bool TryGetWeapon(int id, out WeaponAttributesData definition)
            {
                definition = id == 3001 ? new WeaponAttributesData { WeaponID = 3001, WeaponType = 0 } : null;
                return definition != null;
            }
            public bool TryGetCharacterWeaponType(int id, out int type) { type = 0; return id == 1009; }
        }
    }
}
#endif
