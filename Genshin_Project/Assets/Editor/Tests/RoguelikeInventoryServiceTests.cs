#if ROGUELIKE_SAVE_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class RoguelikeInventoryServiceTests
    {
        private string root;
        private RoguelikeFlowService flow;
        private RoguelikeInventoryService inventory;
        private FailingReplaceFileSystem fileSystem;
        private int notifications;

        [SetUp]
        public void SetUp()
        {
            root = RoguelikeSaveTestFactory.CreateTemporaryRoot();
            fileSystem = new FailingReplaceFileSystem();
            flow = new RoguelikeFlowService(RoguelikeSaveTestFactory.CreateService(root, fileSystem), new RouteRules());
            Assert.That(flow.StartNewGame(RoguelikeSaveTestFactory.Snapshot(1)).Succeeded, Is.True);
            inventory = new RoguelikeInventoryService(flow, new EquipmentCatalog());
            notifications = 0;
            inventory.Changed += view =>
            {
                Assert.That(inventory.Read().Data.GetItemCount("mora"), Is.EqualTo(view.GetItemCount("mora")));
                notifications++;
            };
        }

        [TearDown]
        public void TearDown() { RoguelikeSaveTestFactory.DeleteTemporaryRoot(root); }

        [Test]
        public void Items_WorkWithoutDefinitions_AndReadResultsAreIndependent()
        {
            Assert.That(inventory.Commit(StepId(), draft => draft.AddItems(Items("unconfigured-item", 10))).Succeeded, Is.True);
            var view = inventory.Read().Data;
            Assert.That(view.GetItemCount("unconfigured-item"), Is.EqualTo(10));
            Assert.That(view.TryGetItemDefinition("unconfigured-item", out _), Is.False);
            view.GetItems()[0].Quantity = 1;
            Assert.That(inventory.Read().Data.GetItemCount("mora"), Is.EqualTo(1000));
            Assert.That(notifications, Is.EqualTo(1));
        }

        [Test]
        public void Costs_AggregateDuplicateIds_AndRemoveEmptyStacks()
        {
            var costs = new[] { Item("mora", 400), Item("mora", 600) };
            Assert.That(inventory.Commit(StepId(), draft => draft.ConsumeItems(costs)).Succeeded, Is.True);
            Assert.That(inventory.Read().Data.GetItemCount("mora"), Is.Zero);
            Assert.That(inventory.Read().Data.GetItems(), Is.Empty);
        }

        [Test]
        public void FailedCost_AbortsEntireBatch_EvenIfCallbackCatchesIt()
        {
            string step = StepId();
            var result = inventory.Commit(step, draft =>
            {
                draft.AddWeapon(15001);
                try { draft.ConsumeItems(new[] { Item("mora", 600), Item("mora", 600) }); }
                catch (InvalidOperationException) { }
            });
            Assert.That(result.Succeeded, Is.False);
            Assert.That(inventory.Read().Data.GetWeapons().Count, Is.EqualTo(1));
            Assert.That(inventory.Read().Data.GetItemCount("mora"), Is.EqualTo(1000));
            Assert.That(StepId(), Is.EqualTo(step));
            Assert.That(notifications, Is.Zero);
        }

        [Test]
        public void ItemOverflow_AndInvalidQuantities_AreRejectedWithoutMutation()
        {
            Assert.That(inventory.Commit(StepId(), draft => draft.AddItems(Items("mora", int.MaxValue))).Succeeded, Is.False);
            Assert.That(inventory.Commit(StepId(), draft => draft.ConsumeItems(Items("mora", -1))).Succeeded, Is.False);
            Assert.That(inventory.Read().Data.GetItemCount("mora"), Is.EqualTo(1000));
        }

        [Test]
        public void SameWeaponTemplate_CreatesSeparateInstances_AndEquipmentRemainsInInventory()
        {
            string first = null, second = null;
            Assert.That(inventory.Commit(StepId(), draft =>
            {
                first = draft.AddWeapon(15001);
                second = draft.AddWeapon(15001);
                draft.EquipWeapon(1009, first);
            }).Succeeded, Is.True);
            var view = inventory.Read().Data;
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(view.GetWeapons(15001).Count, Is.EqualTo(3));
            Assert.That(view.FindWeapon(first).Weapon.Level, Is.EqualTo(1));
            Assert.That(view.GetWeaponOwner(first), Is.EqualTo(1009));
            Assert.That(view.GetWeaponOwner("weapon-1"), Is.Zero);
            view.FindWeapon(first).Weapon.Level = 90;
            Assert.That(inventory.Read().Data.FindWeapon(first).Weapon.Level, Is.EqualTo(1));
        }

        [Test]
        public void MissingTemplate_OrMismatchedWeaponType_DoesNotCommit()
        {
            Assert.That(inventory.Commit(StepId(), draft => draft.AddWeapon(99999)).Succeeded, Is.False);
            Assert.That(inventory.Commit(StepId(), draft =>
                draft.EquipWeapon(1009, draft.AddWeapon(15002))).Succeeded, Is.False);
            Assert.That(inventory.Read().Data.GetWeapons().Count, Is.EqualTo(1));
        }

        [Test]
        public void Artifact_UsesProvidedGeneratedResult_AndReplacesOnlySameSlot()
        {
            var generated = Artifact();
            string id = null;
            Assert.That(inventory.Commit(StepId(), draft =>
            {
                id = draft.AddArtifact(generated);
                draft.EquipArtifact(1009, id);
            }).Succeeded, Is.True);
            generated.MainAttributeValue = 9999;
            var view = inventory.Read().Data;
            Assert.That(view.GetArtifacts(ArtifactSlot.Flower).Count, Is.EqualTo(2));
            Assert.That(view.FindArtifact(id).Artifact.MainAttributeValue, Is.EqualTo(123));
            Assert.That(view.FindArtifact(id).Artifact.SecondaryAttributes[0].Value, Is.EqualTo(7));
            Assert.That(view.GetArtifactOwner(id), Is.EqualTo(1009));
            Assert.That(view.GetArtifactOwner("artifact-1"), Is.Zero);
        }

        [Test]
        public void EquippedInstances_RequireUnequipBeforeRemoval()
        {
            Assert.That(inventory.Commit(StepId(), draft => draft.RemoveWeapon("weapon-1")).Succeeded, Is.False);
            Assert.That(inventory.Commit(StepId(), draft => draft.RemoveArtifact("artifact-1")).Succeeded, Is.False);
            Assert.That(inventory.Commit(StepId(), draft =>
            {
                draft.UnequipWeapon(1009);
                draft.RemoveWeapon("weapon-1");
                draft.UnequipArtifact(1009, ArtifactSlot.Flower);
                draft.RemoveArtifact("artifact-1");
            }).Succeeded, Is.True);
            Assert.That(inventory.Read().Data.GetWeapons(), Is.Empty);
            Assert.That(inventory.Read().Data.GetArtifacts(), Is.Empty);
        }

        [Test]
        public void FailedSave_DoesNotPublishChange_OrLeaveEscapedDraftUsable()
        {
            RoguelikeInventoryTransaction escaped = null;
            string step = StepId();
            fileSystem.FailReplace = true;
            Assert.That(inventory.Commit(step, draft =>
            {
                escaped = draft;
                draft.ConsumeItems(Items("mora", 100));
            }).Succeeded, Is.False);
            Assert.That(inventory.Read().Data.GetItemCount("mora"), Is.EqualTo(1000));
            Assert.That(StepId(), Is.EqualTo(step));
            Assert.That(notifications, Is.Zero);
            Assert.Throws<InvalidOperationException>(() => escaped.ExportSnapshot());
        }

        [Test]
        public void StaleCommit_IsRejectedBeforeExecutingOperation()
        {
            string step = StepId();
            Assert.That(inventory.Commit(step, draft => draft.AddItems(Items("mora", 1))).Succeeded, Is.True);
            bool called = false;
            Assert.That(inventory.Commit(step, draft => called = true).Succeeded, Is.False);
            Assert.That(called, Is.False);
            Assert.That(notifications, Is.EqualTo(1));
        }

        [Test]
        public void Draft_CanBeCombinedWithUpgradeResult_WithoutSavingIntermediateCost()
        {
            var context = flow.Resume().Data;
            var draft = inventory.CreateDraft(context.Snapshot);
            draft.ConsumeItems(Items("mora", 100));
            var result = draft.ExportSnapshot();
            result.Characters[0].Level++;
            Assert.That(inventory.Read().Data.GetItemCount("mora"), Is.EqualTo(1000));
            Assert.That(flow.CommitProgressAction(context.Flow.StepId, result,
                RoguelikeSaveReason.CharacterUpgraded).Succeeded, Is.True);
            draft.Close();
            Assert.That(inventory.Read().Data.GetItemCount("mora"), Is.EqualTo(900));
            Assert.That(flow.Resume().Data.Snapshot.Characters[0].Level, Is.EqualTo(21));
        }

        private string StepId() { return flow.Resume().Data.Flow.StepId; }
        private static RoguelikeItemStackData Item(string id, int quantity)
        {
            return new RoguelikeItemStackData { ItemId = id, Quantity = quantity };
        }
        private static RoguelikeItemStackData[] Items(string id, int quantity) { return new[] { Item(id, quantity) }; }

        private static GeneratedArtifact Artifact()
        {
            return new GeneratedArtifact
            {
                Star = 5, Slot = ArtifactSlot.Flower, MaxLevel = 20, MainAttributeType = "HP", MainAttributeValue = 123,
                SecondaryAttributes = new List<ArtifactSecondaryAttribute>
                {
                    new ArtifactSecondaryAttribute { AttributeType = "ATK", Value = 7, RollCount = 1 },
                },
            };
        }

        private sealed class EquipmentCatalog : IRoguelikeEquipmentCatalog
        {
            public bool TryGetWeapon(int id, out WeaponAttributesData definition)
            {
                definition = id == 15001 || id == 15002
                    ? new WeaponAttributesData { WeaponID = id, WeaponType = id == 15001 ? 0 : 2 }
                    : null;
                return definition != null;
            }
            public bool TryGetCharacterWeaponType(int id, out int type) { type = 0; return id == 1009; }
        }

        private sealed class RouteRules : IRoguelikeRouteRules
        {
            public RoguelikeSaveResult ValidateNodeEntry(RoguelikeProgressSnapshot snapshot, RoguelikeNodeSelection node)
            { return RoguelikeSaveResult.Success(); }
            public RoguelikeSaveResult ValidateChapterCompletion(RoguelikeProgressSnapshot snapshot)
            { return RoguelikeSaveResult.Success(); }
        }
    }
}
#endif
