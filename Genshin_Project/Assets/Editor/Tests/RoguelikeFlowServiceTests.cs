#if ROGUELIKE_SAVE_TESTS
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using OfficeOpenXml;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class RoguelikeFlowServiceTests
    {
        private string root;
        private RoguelikeSaveService saves;
        private RoguelikeFlowService flow;
        private RouteRules routes;

        [SetUp]
        public void SetUp()
        {
            root = RoguelikeSaveTestFactory.CreateTemporaryRoot();
            saves = RoguelikeSaveTestFactory.CreateService(root);
            routes = new RouteRules();
            flow = new RoguelikeFlowService(saves, routes);
            Assert.That(flow.StartNewGame(RoguelikeSaveTestFactory.Snapshot(1)).Succeeded, Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            RoguelikeSaveTestFactory.DeleteTemporaryRoot(root);
        }

        [Test]
        public void BattleSettlement_AdvancesOnlyAfterResultCommit_AndRejectsDuplicateCallbacks()
        {
            Enter(RoguelikeNodeKind.Battle);
            string nodeStep = Current().Flow.StepId;
            Assert.That(flow.BeginBattle(nodeStep, "battle-1").Succeeded, Is.True);
            string battleStep = Current().Flow.StepId;
            Assert.That(flow.RecordBattleVictory(battleStep).Succeeded, Is.True);
            Assert.That(flow.RecordBattleVictory(battleStep).Succeeded, Is.False);
            Assert.That(Current().Snapshot.ClearedNodeIds, Does.Not.Contain("node-1"));

            var settlement = Current();
            settlement.Snapshot.Inventory.Items[0].Quantity += 5;
            Assert.That(flow.CompleteNode(settlement.Flow.StepId, settlement.Snapshot).Succeeded, Is.True);
            Assert.That(flow.CompleteNode(settlement.Flow.StepId, settlement.Snapshot).Succeeded, Is.False);
            Assert.That(Current().Flow.Step, Is.EqualTo(RoguelikeFlowStep.Route));
            Assert.That(Current().Snapshot.ClearedNodeIds, Does.Contain("node-1"));
            Assert.That(Current().Snapshot.Inventory.Items[0].Quantity, Is.EqualTo(1005));
        }

        [Test]
        public void BattleCheckpoint_RestoresSameNodeAndStep_WithoutSavingTransientChanges()
        {
            Enter(RoguelikeNodeKind.Elite);
            Assert.That(flow.BeginBattle(Current().Flow.StepId, "elite-1").Succeeded, Is.True);
            var battle = Current();
            battle.Snapshot.Inventory.Items[0].Quantity = 1;
            battle.Snapshot.CharacterVitals[0].CurrentHealth = 1;
            Assert.That(flow.CommitProgressAction(battle.Flow.StepId, battle.Snapshot,
                RoguelikeSaveReason.EquipmentChanged).Succeeded, Is.False);

            var restarted = new RoguelikeFlowService(saves, routes).Resume().Data;
            Assert.That(restarted.Flow.StepId, Is.EqualTo(battle.Flow.StepId));
            Assert.That(restarted.Flow.Node.ContentId, Is.EqualTo("content-1"));
            Assert.That(restarted.BattleId, Is.EqualTo("elite-1"));
            Assert.That(restarted.Snapshot.CharacterVitals[0].CurrentHealth, Is.EqualTo(800));
            Assert.That(restarted.Snapshot.Inventory.Items[0].Quantity, Is.EqualTo(1000));
        }

        [Test]
        public void EnterNode_FiltersByRangeAndRemembersUsedContentAfterLocalFallback()
        {
            var catalog = CreateNodeCatalog();
            flow = new RoguelikeFlowService(saves, routes, catalog);
            int firstStep = Current().Snapshot.RandomStep;

            Assert.That(EnterConfiguredBattle(0), Is.EqualTo("RB101"));
            flow = new RoguelikeFlowService(saves, routes, catalog);
            Assert.That(EnterConfiguredBattle(1), Is.EqualTo("RB102"));
            Assert.That(Current().Snapshot.RandomStep, Is.EqualTo(firstStep));

            Assert.That(EnterConfiguredBattle(2), Is.EqualTo("RB101").Or.EqualTo("RB102"));
            Assert.That(Current().Snapshot.RandomStep, Is.EqualTo(firstStep + 1));
            Assert.That(EnterConfiguredBattle(3), Is.EqualTo("RB103"));
            Assert.That(Current().Snapshot.RandomStep, Is.EqualTo(firstStep + 1));
            Assert.That(Current().Snapshot.UsedNodeContentIds, Is.EqualTo(new[] { "RB101", "RB102", "RB103" }));
        }

        [TestCase(5, "Event")]
        [TestCase(6, "Elite")]
        [TestCase(7, "Boss")]
        public void ReadConfig_RejectsStageTypeWithoutCandidates(int weightColumn, string kind)
        {
            var error = Assert.Throws<InvalidDataException>(() => CreateNodeCatalog(weightColumn));
            Assert.That(error.Message, Does.Contain("R102").And.Contain(kind));
        }

        private string EnterConfiguredBattle(int stageIndex)
        {
            var node = new RoguelikeNodeSelection
            {
                NodeId = "configured-" + stageIndex, StageIndex = stageIndex, Kind = RoguelikeNodeKind.Battle,
            };
            var entered = flow.EnterNode(Current().Flow.StepId, node);
            Assert.That(entered.Succeeded, Is.True, entered.Message);
            string contentId = Current().Flow.Node.ContentId;
            Assert.That(flow.BeginBattle(Current().Flow.StepId, node.NodeId).Succeeded, Is.True);
            Assert.That(flow.RecordBattleVictory(Current().Flow.StepId).Succeeded, Is.True);
            var settlement = Current();
            Assert.That(flow.CompleteNode(settlement.Flow.StepId, settlement.Snapshot).Succeeded, Is.True);
            return contentId;
        }

        private RoguelikeNodeContentCatalog CreateNodeCatalog(int unassignedWeightColumn = 0)
        {
            string battleFolder = Path.Combine(root, "Battle");
            Directory.CreateDirectory(battleFolder);
            using (var stages = new ExcelPackage())
            {
                var sheet = stages.Workbook.Worksheets.Add("Chapter1");
                sheet.Cells["A1:J1"].LoadFromArrays(new[] { new object[]
                    { "StageID", "MinEncounter", "MaxEncounter", "Battle", "Event", "Elite", "Boss", "EnemyLevel", "HPMultiplier", "ATKMultiplier" } });
                for (int i = 1; i <= 4; i++)
                    sheet.Cells[i + 1, 1].LoadFromArrays(new[] { new object[] { "R10" + i, 1, 1, 100, 0, 0, 0, i, 1, 1 } });
                if (unassignedWeightColumn > 0) sheet.Cells[3, unassignedWeightColumn].Value = 10;
                stages.SaveAs(new FileInfo(Path.Combine(root, "StageConfig.xlsx")));
            }
            using (var battles = new ExcelPackage())
            {
                var main = battles.Workbook.Worksheets.Add("Main");
                main.Cells["A1"].LoadFromArrays(new object[][]
                {
                    new object[] { "BattleID", "BattleType", "AvailableStageFrom", "AvailableStageTo", "BattleBG" },
                    new object[] { "RB101", "Battle", "R101", "R104" },
                    new object[] { "RB102", "Battle", "R102", "R103" },
                    new object[] { "RB103", "Battle", "R104", "R104" },
                });
                var enemies = battles.Workbook.Worksheets.Add("Battle_Enemy");
                enemies.Cells["A1"].LoadFromArrays(new object[][]
                {
                    new object[] { "BattleID", "Wave", "Slot5", "Slot4", "Slot3", "Slot2", "Slot1", "Slot5", "Slot4", "Slot3", "Slot2", "Slot1" },
                    new object[] { "RB101", 1, null, null, 2001 },
                    new object[] { "RB102", 1, null, null, 2002 },
                    new object[] { "RB103", 1, null, null, 2003 },
                });
                battles.SaveAs(new FileInfo(Path.Combine(battleFolder, "BattleConfig1.xlsx")));
            }
            return RoguelikeNodeContentCatalog.Load(root);
        }

        [Test]
        public void Event_UsesSeparateCompletionInterface_AndPersistsOutcome()
        {
            Enter(RoguelikeNodeKind.Event);
            var node = Current();
            Assert.That(flow.BeginBattle(node.Flow.StepId, "wrong-battle").Succeeded, Is.False);
            node.Snapshot.Inventory.Items[0].Quantity += 10;
            Assert.That(flow.CompleteEvent(node.Flow.StepId, node.Snapshot).Succeeded, Is.True);
            Assert.That(flow.CompleteEvent(node.Flow.StepId, node.Snapshot).Succeeded, Is.False);
            Assert.That(Current().Flow.Step, Is.EqualTo(RoguelikeFlowStep.Settlement));
            Assert.That(Current().Snapshot.Inventory.Items[0].Quantity, Is.EqualTo(1010));
        }

        [Test]
        public void PendingReward_CannotBeOverwritten_AndInvalidatesOldSettlementContext()
        {
            Enter(RoguelikeNodeKind.Event);
            var node = Current();
            flow.CompleteEvent(node.Flow.StepId, node.Snapshot);
            var old = Current();
            Assert.That(flow.SavePendingReward(old.Flow.StepId, new PendingRewardData { RewardId = "reward-1" }).Succeeded,
                Is.True);
            Assert.That(flow.CompleteNode(old.Flow.StepId, old.Snapshot).Succeeded, Is.False);
            var pending = Current();
            Assert.That(flow.SavePendingReward(pending.Flow.StepId,
                new PendingRewardData { RewardId = "reroll" }).Succeeded, Is.False);
            Assert.That(flow.CompleteNode(pending.Flow.StepId, pending.Snapshot).Succeeded, Is.False);
            pending.Snapshot.PendingRewardData = null;
            Assert.That(flow.CompleteNode(pending.Flow.StepId, pending.Snapshot).Succeeded, Is.True);
        }

        [Test]
        public void ProgressAction_PreservesFlowAndRequiresFreshContext()
        {
            var original = Current();
            original.Snapshot.Characters[0].Level = 21;
            original.Snapshot.ClearedNodeIds.Add("forged-node");
            Assert.That(flow.CommitProgressAction(original.Flow.StepId, original.Snapshot,
                RoguelikeSaveReason.CharacterUpgraded).Succeeded, Is.True);
            Assert.That(Current().Snapshot.Characters[0].Level, Is.EqualTo(21));
            Assert.That(Current().Snapshot.ClearedNodeIds, Does.Not.Contain("forged-node"));
            Assert.That(flow.EnterNode(original.Flow.StepId, Selection(RoguelikeNodeKind.Battle)).Succeeded, Is.False);
        }

        [Test]
        public void RouteRules_AreRequired_AndDeniedEntryDoesNotChangeStep()
        {
            routes.AllowEntry = false;
            string original = Current().Flow.StepId;
            Assert.That(flow.EnterNode(original, Selection(RoguelikeNodeKind.Battle)).Succeeded, Is.False);
            Assert.That(Current().Flow.StepId, Is.EqualTo(original));
            Assert.That(flow.CompleteChapter(original).Succeeded, Is.False);
        }

        [Test]
        public void ChapterSlot_StartsFreshRouteButKeepsOwnedData()
        {
            Enter(RoguelikeNodeKind.Boss);
            flow.BeginBattle(Current().Flow.StepId, "boss-1");
            flow.RecordBattleVictory(Current().Flow.StepId);
            var settlement = Current();
            flow.CompleteNode(settlement.Flow.StepId, settlement.Snapshot);
            routes.AllowCompletion = true;
            Assert.That(flow.CompleteChapter(Current().Flow.StepId).Succeeded, Is.True);
            Assert.That(flow.SaveCompletedChapterToSlot(Current().Flow.StepId, 1).Succeeded, Is.True);
            Assert.That(flow.StartFromChapterSlot(2, 1).Succeeded, Is.True);
            var next = Current();
            Assert.That(next.Flow.Step, Is.EqualTo(RoguelikeFlowStep.Route));
            Assert.That(next.Snapshot.CurrentChapterId, Is.EqualTo(2));
            Assert.That(next.Snapshot.CurrentNodeId, Is.Empty);
            Assert.That(next.Snapshot.ClearedNodeIds, Is.Empty);
            Assert.That(next.Snapshot.SelectedRouteNodeIds, Is.Empty);
            Assert.That(next.Snapshot.Inventory.Weapons.Count, Is.EqualTo(1));
        }

        [Test]
        public void EventSettlement_PreservesShopPurchasesAndPriorChapterCountsOnReload()
        {
            routes.AllowCompletion = true;
            int firstChapterNodes = Current().Snapshot.CurrentStageIndex + 1;
            Assert.That(flow.CompleteChapter(Current().Flow.StepId).Succeeded, Is.True);
            Assert.That(flow.SaveCompletedChapterToSlot(Current().Flow.StepId, 1).Succeeded, Is.True);
            Assert.That(flow.StartFromChapterSlot(2, 1).Succeeded, Is.True);

            var purchase = Current();
            purchase.Snapshot.ShopPurchaseCounts = new Dictionary<string, int> { ["limited-item"] = 2 };
            Assert.That(flow.CommitProgressAction(purchase.Flow.StepId, purchase.Snapshot,
                RoguelikeSaveReason.ShopPurchased).Succeeded, Is.True);
            Enter(RoguelikeNodeKind.Event);
            var node = Current();
            node.Snapshot.ShopPurchaseCounts = null;
            node.Snapshot.CompletedChapterNodeCounts = null;
            Assert.That(flow.CompleteEvent(node.Flow.StepId, node.Snapshot).Succeeded, Is.True);
            var settlement = Current();
            Assert.That(flow.CompleteNode(settlement.Flow.StepId, settlement.Snapshot).Succeeded, Is.True);

            var loaded = RoguelikeSaveTestFactory.CreateService(root).LoadActiveRun();
            Assert.That(loaded.Succeeded, Is.True, loaded.Message);
            Assert.That(loaded.Data.Snapshot.ShopPurchaseCounts["limited-item"], Is.EqualTo(2));
            Assert.That(loaded.Data.Snapshot.CompletedChapterNodeCounts[1], Is.EqualTo(firstChapterNodes));
        }

        [Test]
        public void InvalidBusinessResult_DoesNotAdvancePersistedStep()
        {
            Enter(RoguelikeNodeKind.Event);
            var node = Current();
            node.Snapshot.Inventory.Items[0].Quantity = -1;
            Assert.That(flow.CompleteEvent(node.Flow.StepId, node.Snapshot).Succeeded, Is.False);
            Assert.That(Current().Flow.StepId, Is.EqualTo(node.Flow.StepId));
            Assert.That(Current().Snapshot.Inventory.Items[0].Quantity, Is.EqualTo(1000));
        }

        [TestCase(0f, 0f)]
        [TestCase(60f, 30f)]
        [TestCase(81f, 40.5f)]
        public void InitialEnergy_DependsOnlyOnMaxEnergy(float maximum, float expected)
        {
            Assert.That(RoguelikeBattleEntryRules.GetInitialEnergy(maximum), Is.EqualTo(expected));
        }

        private RoguelikeFlowContext Current()
        {
            var result = flow.Resume();
            Assert.That(result.Succeeded, Is.True, result.Message);
            return result.Data;
        }

        private void Enter(RoguelikeNodeKind kind)
        {
            Assert.That(flow.EnterNode(Current().Flow.StepId, Selection(kind)).Succeeded, Is.True);
        }

        private static RoguelikeNodeSelection Selection(RoguelikeNodeKind kind)
        {
            return new RoguelikeNodeSelection { NodeId = "node-1", StageIndex = 1, Kind = kind, ContentId = "content-1" };
        }

        private sealed class RouteRules : IRoguelikeRouteRules
        {
            public bool AllowEntry = true;
            public bool AllowCompletion;

            public RoguelikeSaveResult ValidateNodeEntry(RoguelikeProgressSnapshot snapshot, RoguelikeNodeSelection node)
            {
                return AllowEntry ? RoguelikeSaveResult.Success() : Denied();
            }

            public RoguelikeSaveResult ValidateChapterCompletion(RoguelikeProgressSnapshot snapshot)
            {
                return AllowCompletion ? RoguelikeSaveResult.Success() : Denied();
            }

            private static RoguelikeSaveResult Denied()
            {
                return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InvalidOperation, "尚未满足路线条件。");
            }
        }
    }
}
#endif
