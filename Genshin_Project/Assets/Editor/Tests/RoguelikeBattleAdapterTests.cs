#if ROGUELIKE_SAVE_TESTS
using System;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class RoguelikeBattleAdapterTests
    {
        private string root;
        private RoguelikeFlowService flow;
        private RoguelikeBattleAdapter adapter;
        private RoguelikeBattlePreparation preparation;
        private Runtime runtime;
        private FailingReplaceFileSystem fileSystem;

        [SetUp]
        public void SetUp()
        {
            root = RoguelikeSaveTestFactory.CreateTemporaryRoot();
            fileSystem = new FailingReplaceFileSystem();
            flow = new RoguelikeFlowService(RoguelikeSaveTestFactory.CreateService(root, fileSystem), new Catalog());
            flow.StartNewGame(RoguelikeSaveTestFactory.Snapshot(1));
            flow.EnterNode(Current().Flow.StepId, new RoguelikeNodeSelection
            { NodeId = "battle-node", StageIndex = 1, ContentId = "encounter", Kind = RoguelikeNodeKind.Battle });
            flow.BeginBattle(Current().Flow.StepId, "battle-id");
            var stage = new StageSetupData();
            stage.Enemies.Add(new StageEnemySetup { EnemyID = 2001, Level = 10, Slot = 3 });
            stage.Allies.Add(new StageAllySetup { CharacterID = 9999, Level = 90 });
            var registry = new RoguelikeEncounterRegistry();
            registry.Register("encounter", stage);
            preparation = new RoguelikeBattlePreparation(registry, new Catalog(), new Catalog());
            runtime = new Runtime();
            adapter = new RoguelikeBattleAdapter(flow, preparation, runtime);
        }

        [TearDown]
        public void TearDown()
        {
            runtime.FailStop = false;
            adapter.Dispose();
            RoguelikeSaveTestFactory.DeleteTemporaryRoot(root);
        }

        [Test]
        public void Preparation_CopiesCompleteBuildAndIgnoresStageAllies()
        {
            var context = Current();
            context.Snapshot.Characters[0].Ascension = 3;
            var result = preparation.Prepare(context);
            Assert.That(result.Succeeded, Is.True, result.Message);
            var build = result.Data.Allies[0];
            Assert.That(build.Character.CharacterID, Is.EqualTo(1009));
            Assert.That(build.Character.Ascension, Is.EqualTo(3));
            Assert.That(build.Character.SkillLevels, Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(build.Weapon.InstanceId, Is.EqualTo("weapon-1"));
            Assert.That(build.Artifacts[0].InstanceId, Is.EqualTo("artifact-1"));
            build.Artifacts[0].Artifact.MainAttributeValue = 999;
            Assert.That(context.Snapshot.Inventory.Artifacts[0].Artifact.MainAttributeValue, Is.EqualTo(100));
        }

        [Test]
        public void EmptyPartyAndUnknownEncounter_HaveNoDefaultFallback()
        {
            var context = Current();
            context.Snapshot.PartyCharacterIds.Clear();
            Assert.That(preparation.Prepare(context).Succeeded, Is.False);
            context = Current(); context.Flow.Node.ContentId = "unconfigured";
            Assert.That(preparation.Prepare(context).Succeeded, Is.False);
        }

        [Test]
        public void Start_UsesPersistedBuildRatherThanCallerChanges()
        {
            var context = Current(); context.Snapshot.Characters[0].Level = 90;
            Assert.That(adapter.TryStart(context).Succeeded, Is.True);
            Assert.That(runtime.Setup.Allies[0].Character.Level, Is.EqualTo(20));
        }

        [Test]
        public void Victory_GoesToSettlementOnceWithoutClaimingReward()
        {
            int ended = 0; adapter.Ended += result => ended++;
            adapter.TryStart(Current()); runtime.Complete(true);
            string step = Current().Flow.StepId; runtime.Complete(true);
            Assert.That(Current().Flow.Step, Is.EqualTo(RoguelikeFlowStep.Settlement));
            Assert.That(Current().Flow.StepId, Is.EqualTo(step));
            Assert.That(Current().Snapshot.ClearedNodeIds, Does.Not.Contain("battle-node"));
            Assert.That(ended, Is.EqualTo(1));
        }

        [Test]
        public void DefeatAndLateCallback_DoNotOverwriteCheckpointOrRetry()
        {
            string step = Current().Flow.StepId;
            adapter.TryStart(Current()); var previous = runtime.Complete; previous(false);
            Assert.That(Current().Flow.StepId, Is.EqualTo(step));
            Assert.That(adapter.StopBattle().Succeeded, Is.True);
            Assert.That(adapter.TryStart(Current()).Succeeded, Is.True);
            previous(true);
            Assert.That(adapter.IsRunning, Is.True);
            Assert.That(Current().Flow.Step, Is.EqualTo(RoguelikeFlowStep.Battle));
        }

        [Test]
        public void SynchronousCompletionAndStartFailure_DoNotCommitVictory()
        {
            string step = Current().Flow.StepId;
            runtime.CompleteDuringStart = true; runtime.FailStart = true;
            Assert.That(adapter.TryStart(Current()).Succeeded, Is.False);
            Assert.That(Current().Flow.StepId, Is.EqualTo(step));
            Assert.That(adapter.LastOutcome, Is.Null);
        }

        [Test]
        public void FailedVictorySave_RetriesCommitWithoutAnotherBattleOrEndEvent()
        {
            int ended = 0; adapter.Ended += result => ended++;
            adapter.TryStart(Current()); fileSystem.FailReplace = true; runtime.Complete(true);
            Assert.That(adapter.LastOutcome.ProgressCommit.Succeeded, Is.False);
            fileSystem.FailReplace = false;
            Assert.That(adapter.RetryVictoryCommit().Succeeded, Is.True);
            Assert.That(adapter.RetryVictoryCommit().Succeeded, Is.False);
            Assert.That(ended, Is.EqualTo(1)); Assert.That(runtime.Starts, Is.EqualTo(1));
        }

        [Test]
        public void FailedCleanup_MustSucceedBeforeStartingAgain()
        {
            adapter.TryStart(Current()); runtime.FailStop = true;
            Assert.That(adapter.StopBattle().Succeeded, Is.False);
            Assert.That(adapter.TryStart(Current()).Succeeded, Is.False);
            runtime.FailStop = false;
            Assert.That(adapter.StopBattle().Succeeded, Is.True);
            Assert.That(adapter.TryStart(Current()).Succeeded, Is.True);
        }

        private RoguelikeFlowContext Current() { return flow.Resume().Data; }

        private sealed class Runtime : IRoguelikeBattleRuntime
        {
            public Action<bool> Complete;
            public RoguelikeBattleSetup Setup;
            public bool CompleteDuringStart, FailStart, FailStop;
            public int Starts;
            public RoguelikeSaveResult StartBattle(RoguelikeBattleSetup setup, Action<bool> onEnded)
            {
                Starts++; Setup = setup; Complete = onEnded;
                if (CompleteDuringStart) onEnded(true);
                return FailStart ? RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InvalidOperation, "启动失败")
                    : RoguelikeSaveResult.Success();
            }
            public void StopBattle() { if (FailStop) throw new InvalidOperationException("清理失败"); }
        }

        private sealed class Catalog : IRoguelikeEquipmentCatalog, IRoguelikeEnemyCatalog, IRoguelikeRouteRules
        {
            public bool TryGetWeapon(int id, out WeaponAttributesData definition)
            { definition = new WeaponAttributesData { WeaponID = id, WeaponType = 0 }; return id == 15001; }
            public bool TryGetCharacterWeaponType(int id, out int type) { type = 0; return id == 1009; }
            public bool ContainsEnemy(int id) { return id == 2001; }
            public RoguelikeSaveResult ValidateNodeEntry(RoguelikeProgressSnapshot snapshot, RoguelikeNodeSelection node)
            { return RoguelikeSaveResult.Success(); }
            public RoguelikeSaveResult ValidateChapterCompletion(RoguelikeProgressSnapshot snapshot)
            { return RoguelikeSaveResult.Success(); }
        }
    }
}
#endif
