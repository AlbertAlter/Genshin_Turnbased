#if ROGUELIKE_SAVE_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class RoguelikeChapterRuntimeTests
    {
        [Test]
        public void BasicOperations_ExportCompleteIndependentSnapshot()
        {
            var runtime = new RoguelikeChapterRuntime(new RoguelikeProgressSnapshot
            {
                CurrentChapterId = 1,
            });

            runtime.AddCharacter(1009, 1000f, 80f);
            runtime.SetParty(new[] { 1009 });
            runtime.UpdateVitals(1009, 750f, 40f);
            runtime.UpdateCharacterProgress(1009, 20, 50, 1, 2, new[] { 2, 3, 4, 5 });
            Assert.That(runtime.AdjustItem("mora", 1000), Is.EqualTo(1000));
            runtime.AddWeapon("weapon-1", new WeaponLoadout
            {
                WeaponID = 15001,
                Level = 20,
                Ascension = 1,
                Refinement = 1,
            });
            runtime.EquipWeapon(1009, "weapon-1");
            runtime.AddArtifact("artifact-1", CreateArtifact(ArtifactSlot.Flower));
            runtime.EquipArtifact(1009, "artifact-1");

            RoguelikeProgressSnapshot exported = runtime.ExportSnapshot();

            Assert.That(exported.Characters[0].Level, Is.EqualTo(20));
            Assert.That(exported.CharacterVitals[0].CurrentHealth, Is.EqualTo(750f));
            Assert.That(exported.PartyCharacterIds, Is.EqualTo(new[] { 1009 }));
            Assert.That(exported.Inventory.Items[0].Quantity, Is.EqualTo(1000));
            Assert.That(exported.Characters[0].EquippedWeaponInstanceId, Is.EqualTo("weapon-1"));
            Assert.That(exported.Characters[0].EquippedArtifacts[0].ArtifactInstanceId, Is.EqualTo("artifact-1"));

            exported.Inventory.Items[0].Quantity = 1;
            Assert.That(runtime.ExportSnapshot().Inventory.Items[0].Quantity, Is.EqualTo(1000));
        }

        [Test]
        public void NewCharacter_StartsAtLevelOneWithFullVitals()
        {
            var runtime = new RoguelikeChapterRuntime(new RoguelikeProgressSnapshot
            {
                CurrentChapterId = 1,
            });

            runtime.AddCharacter(1009, 900f, 60f);
            RoguelikeProgressSnapshot snapshot = runtime.ExportSnapshot();

            Assert.That(snapshot.Characters[0].Level, Is.EqualTo(1));
            Assert.That(snapshot.CharacterVitals[0].CurrentHealth, Is.EqualTo(900f));
            Assert.That(snapshot.CharacterVitals[0].CurrentEnergy, Is.EqualTo(60f));
        }

        [Test]
        public void EquipmentInstance_CannotBeEquippedByMultipleCharacters()
        {
            var runtime = new RoguelikeChapterRuntime(new RoguelikeProgressSnapshot
            {
                CurrentChapterId = 1,
            });
            runtime.AddCharacter(1009, 900f, 60f);
            runtime.AddCharacter(1010, 1000f, 80f);
            runtime.AddWeapon("weapon-1", new WeaponLoadout
            {
                WeaponID = 15001,
                Level = 1,
                Refinement = 1,
            });
            runtime.AddArtifact("artifact-1", CreateArtifact(ArtifactSlot.Flower));
            runtime.EquipWeapon(1009, "weapon-1");
            runtime.EquipArtifact(1009, "artifact-1");

            Assert.Throws<InvalidOperationException>(() => runtime.EquipWeapon(1010, "weapon-1"));
            Assert.Throws<InvalidOperationException>(() => runtime.EquipArtifact(1010, "artifact-1"));
        }

        [Test]
        public void VitalLimitChange_PreservesFullHealthOtherwiseClampsAbsoluteValue()
        {
            var runtime = new RoguelikeChapterRuntime(new RoguelikeProgressSnapshot
            {
                CurrentChapterId = 1,
            });
            runtime.AddCharacter(1009, 1000f, 80f);

            runtime.UpdateVitalLimits(1009, 1200f, 60f);
            Assert.That(runtime.ExportSnapshot().CharacterVitals[0].CurrentHealth, Is.EqualTo(1200f));

            runtime.UpdateVitals(1009, 700f, 50f);
            runtime.UpdateVitalLimits(1009, 600f, 40f);
            RoguelikeCharacterVitalData vital = runtime.ExportSnapshot().CharacterVitals[0];
            Assert.That(vital.CurrentHealth, Is.EqualTo(600f));
            Assert.That(vital.CurrentEnergy, Is.EqualTo(40f));
        }

        private static GeneratedArtifact CreateArtifact(ArtifactSlot slot)
        {
            return new GeneratedArtifact
            {
                Star = 5,
                Slot = slot,
                Level = 0,
                MaxLevel = 20,
                MainAttributeType = "HP",
                MainAttributeValue = 100f,
                SecondaryAttributes = new List<ArtifactSecondaryAttribute>
                {
                    new ArtifactSecondaryAttribute
                    {
                        AttributeType = "ATK",
                        Value = 10f,
                        RollCount = 1,
                    },
                },
            };
        }
    }
}
#endif
