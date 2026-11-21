#if ROGUELIKE_SAVE_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

public sealed class PlayerAppearanceServiceTests
{
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    [Test]
    public void BossVictoryAutomaticallyAwardsOnlyTheCapturedPartyAndWeapon()
    {
        var f = new Fixture(); string step = f.BeginBoss(true);
        Check(f.Flow.RecordBattleVictory(step).Succeeded, "victory");
        var appearance = f.Appearance.Read(); Check(appearance.Succeeded, "appearance read");
        Check(appearance.Data.CharacterFrameLevel(1009) == 1 && appearance.Data.WeaponFrameLevel(3001) == 1, "party award");
        Check(appearance.Data.CharacterFrameLevel(1010) == 0 && appearance.Data.WeaponFrameLevel(3002) == 0, "reserve must not receive award");
        Check(!f.Flow.RecordBattleVictory(step).Succeeded, "stale callback accepted");
        Check(f.Appearance.Read().Data.CharacterFrameLevel(1009) == 1, "duplicate increment");
    }
    [Test]
    public void DeletingRunAndReallyClearingAgainIncrementsGlobalCount()
    {
        var f = new Fixture(); Check(f.Flow.RecordBattleVictory(f.BeginBoss(true)).Succeeded, "first clear");
        Check(f.Repository.DeleteActiveRun().Succeeded, "delete run");
        Check(!f.Paths.AppearancePath.StartsWith(f.Paths.RootDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "appearance saved inside run folder");
        Check(f.Flow.RecordBattleVictory(f.BeginBoss(true)).Succeeded, "second clear");
        var read = new PlayerAppearanceService(f.Repository).Read();
        Check(read.Succeeded && read.Data.CharacterFrameLevel(1009) == 2 && read.Data.WeaponFrameLevel(3001) == 2, "global counts reset");
    }
    [Test]
    public void NonChapterBossDoesNotAwardFrames()
    {
        var f = new Fixture(); Check(f.Flow.RecordBattleVictory(f.BeginBoss(false)).Succeeded, "ordinary boss victory");
        Check(f.Appearance.Read().Data.CharacterFrameLevel(1009) == 0, "ordinary boss awarded frames");
    }
    [Test]
    public void IndependentPublicCommandDeduplicatesCompletionAndSharedWeaponTemplate()
    {
        var f = new Fixture();
        var participants = new[] { new AppearanceBattleParticipant { CharacterId = 1009, WeaponId = 3001 },
            new AppearanceBattleParticipant { CharacterId = 1010, WeaponId = 3001 } };
        Check(f.Appearance.RecordChapterBossVictory("clear-1", participants).Succeeded, "manual clear");
        Check(f.Appearance.RecordChapterBossVictory("clear-1", participants).Succeeded, "idempotent retry");
        Check(f.Appearance.GetCharacterFrameLevel(1009) == 1 && f.Appearance.GetWeaponFrameLevel(3001) == 1, "shared template counted twice");
        Check(f.Appearance.RecordChapterBossVictory("clear-2", participants).Succeeded, "real repeated clear");
        Check(f.Appearance.GetCharacterFrameLevel(1010) == 2 && f.Appearance.GetWeaponFrameLevel(3001) == 2, "real clears not counted");
    }
    [Test]
    public void CardBackCanBeSelectedPersistedAndResetWithoutAnyUI()
    {
        var f = new Fixture(); int changes = 0; f.Appearance.Changed += () => changes++;
        Check(f.Appearance.Refresh().Succeeded, "initial read");
        Check(!f.Appearance.SelectCardBack("missing").Succeeded, "unknown back");
        Check(f.Appearance.SelectCardBack("test").Succeeded, "select back");
        Check(f.Appearance.SelectedCardBackPath == "art_assets/CardBack/test.png", "selected path");
        var next = new PlayerAppearanceService(f.Repository, f.Catalog); Check(next.Read().Succeeded, "reload");
        Check(next.SelectedCardBackId == "test", "choice not saved");
        Check(f.Appearance.SelectCardBack("").Succeeded && f.Appearance.SelectedCardBackPath == "", "reset default");
        Check(changes == 3, "change events");
    }
    [Test]
    public void CosmeticWriteFailureLeavesRecoverableVictoryJournal()
    {
        var f = new Fixture(); string step = f.BeginBoss(true); f.Files.FailAppearance = true;
        var victory = f.Flow.RecordBattleVictory(step); Check(victory.Succeeded, "committed victory reported as rolled back");
        Check(!f.Appearance.Read().Succeeded, "unpersisted cosmetic reported ready");
        f.Files.FailAppearance = false;
        Check(f.Flow.Resume().Succeeded, "resume did not replay journal");
        Check(f.Appearance.Read().Data.CharacterFrameLevel(1009) == 1, "lost journal award");
        Check(f.Appearance.Read().Data.CharacterFrameLevel(1009) == 1, "journal replay doubled award");
    }
    [Test]
    public void FailedVictorySaveDoesNotGrantAnyFrame()
    {
        var f = new Fixture(); string step = f.BeginBoss(true); f.Files.FailActive = true;
        Check(!f.Flow.RecordBattleVictory(step).Succeeded, "failed checkpoint accepted");
        Check(f.Appearance.Read().Data.CharacterFrameLevel(1009) == 0, "award before victory commit");
        f.Files.FailActive = false;
        Check(f.Flow.RecordBattleVictory(step).Succeeded && f.Appearance.Read().Data.CharacterFrameLevel(1009) == 1, "retry");
    }
    [Test]
    public void FramesStopAtPrismaticAndReadsAreDetached()
    {
        var f = new Fixture();
        for (int i = 0; i < 5; i++) Check(f.Appearance.RecordChapterBossVictory("clear-" + i,
            new[] { new AppearanceBattleParticipant { CharacterId = 1009, WeaponId = 3001 } }).Succeeded, "clear count");
        var copy = f.Appearance.Read().Data; copy.Characters[0].Level = 999;
        Check(f.Appearance.GetCharacterFrameLevel(1009) == 2, "frame cap or detached read failed");
        Check(UICardFactory.FramePathAtLevel(1, new[] { "ordinary", "gold", "prismatic" }) == "gold", "second tier mapping");
        Check(UICardFactory.FramePathAtLevel(2, new[] { "ordinary", "gold", "prismatic" }) == "prismatic", "third tier mapping");
        Check(f.Appearance.GetWeaponFrameLevel(3001) == 2, "weapon exceeded prismatic");
        Check(f.Appearance.Read().Data.GrantedCompletions.Count == 5, "completion receipts were not retained");
    }
    [Test]
    public void FutureCosmeticSchemaIsNotOverwritten()
    {
        var f = new Fixture();
        f.Files.Data[f.Paths.AppearancePath] = RoguelikeSaveJson.Serialize(new PlayerAppearanceData { SchemaVersion = 999 });
        Check(f.Appearance.Read().ErrorCode == RoguelikeSaveErrorCode.UnsupportedSchema, "future schema accepted");
        Check(!f.Appearance.SelectCardBack("test").Succeeded, "future schema overwritten");
    }
    [Test]
    public void ExistingSchemaTwoRunWithoutCosmeticJournalStillLoads()
    {
        var f = new Fixture(); f.BeginBoss(false);
        var active = f.Repository.LoadActiveRun().Data;
        active.Metadata.SchemaVersion = 2;
        active.Snapshot.AppearanceAwards = null;
        f.Files.Data[f.Paths.ActiveRunPath] = RoguelikeSaveJson.Serialize(active);
        Check(f.Flow.Resume().Succeeded, "legacy save blocked");
        Check(f.Appearance.Read().Data.CharacterFrameLevel(1009) == 0, "legacy save granted frames");
    }
    private sealed class Fixture
    {
        public readonly MemoryFiles Files = new MemoryFiles();
        public readonly RoguelikeSavePaths Paths = new RoguelikeSavePaths("E:/Genshin_TurnBased/Genshin_Project/Assets/SaveData/TestTemp/AppearanceMemory/" + Guid.NewGuid().ToString("N"));
        public readonly RoguelikeSaveRepository Repository;
        public readonly RoguelikeFlowService Flow;
        public readonly PlayerAppearanceService Appearance;
        public readonly CardBackCatalog Catalog = new CardBackCatalog(new[] { new CardBackDefinition { Id = "test", RelativePath = "art_assets/CardBack/test.png" } });
        public Fixture()
        {
            Repository = new RoguelikeSaveRepository(Paths, fileSystem: Files);
            Flow = new RoguelikeFlowService(new RoguelikeSaveService(Repository, "test"), new Routes());
            Appearance = new PlayerAppearanceService(Repository, Catalog);
        }
        public string BeginBoss(bool final)
        {
            var snapshot = new RoguelikeProgressSnapshot { CurrentChapterId = 1 };
            foreach (int id in new[] { 1009, 1010 })
            {
                string weapon = "w-" + id; int weaponId = id == 1009 ? 3001 : 3002;
                snapshot.Characters.Add(new RoguelikeCharacterProgressData { CharacterID = id, EquippedWeaponInstanceId = weapon });
                snapshot.CharacterVitals.Add(new RoguelikeCharacterVitalData { CharacterID = id, MaxHealth = 100, CurrentHealth = 100 });
                snapshot.Inventory.Weapons.Add(new RoguelikeWeaponInstanceData { InstanceId = weapon, Weapon = new WeaponLoadout { WeaponID = weaponId, Level = 1, Refinement = 1 } });
            }
            snapshot.PartyCharacterIds.Add(1009);
            Check(Flow.StartNewGame(snapshot).Succeeded, "start run");
            Check(Flow.EnterNode(Flow.Resume().Data.Flow.StepId, new RoguelikeNodeSelection { NodeId = "boss", ContentId = "boss-content", Kind = RoguelikeNodeKind.Boss, IsChapterFinalBoss = final }).Succeeded, "enter boss");
            Check(Flow.BeginBattle(Flow.Resume().Data.Flow.StepId, "battle").Succeeded, "begin boss");
            return Flow.Resume().Data.Flow.StepId;
        }
    }
    private sealed class Routes : IRoguelikeRouteRules
    {
        public RoguelikeSaveResult ValidateNodeEntry(RoguelikeProgressSnapshot snapshot, RoguelikeNodeSelection node) => RoguelikeSaveResult.Success();
        public RoguelikeSaveResult ValidateChapterCompletion(RoguelikeProgressSnapshot snapshot) => RoguelikeSaveResult.Success();
    }
    private sealed class MemoryFiles : IRoguelikeSaveFileSystem
    {
        public readonly Dictionary<string, string> Data = new Dictionary<string, string>();
        public bool FailAppearance, FailActive;
        public bool FileExists(string path) => Data.ContainsKey(path);
        public void CreateDirectory(string path) { }
        public string ReadAllText(string path) => Data[path];
        public void WriteAllText(string path, string contents) => Data[path] = contents;
        public void DeleteFile(string path) => Data.Remove(path);
        public void MoveFile(string source, string destination) { Fail(destination); Data.Add(destination, Data[source]); Data.Remove(source); }
        public void ReplaceFile(string source, string destination, string backup)
        { Fail(destination); if (backup != null) Data[backup] = Data[destination]; Data[destination] = Data[source]; Data.Remove(source); }
        private void Fail(string destination)
        { if ((FailAppearance && destination.EndsWith("player_appearance.json")) || (FailActive && destination.EndsWith("active_run.json"))) throw new IOException("Injected write failure"); }
    }
}
#endif
