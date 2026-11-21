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
        private RoguelikeGameService CreateGame(IRoguelikeItemCatalog items = null)
            => new RoguelikeGameService(new RoguelikeSaveService(new RoguelikeSaveRepository(paths, fileSystem: files)),
                new Routes(), new Catalog(), items ?? new Catalog(), new Catalog());
        private string Step() => game.Continue().Data.Flow.StepId;
        private RoguelikeInventoryView View() => game.ReadInventory().Data;

        [Test]
        public void WishDraw_CommitsFateRewardsAndPityTogether_AndReusesFailedSaveResult()
        {
            var random = new WishRandom();
            var wish = CreateWish(random, new RoguelikeWishProgress
            {
                CharacterSinceFive = 89, CharacterSinceFour = 9, CharacterFourUpGuaranteed = true,
            });
            string step = wish.Read().Data.StepId;
            files.FailReplace = true;
            Assert.That(wish.Draw(step, "R2001", 10).Succeeded, Is.False);
            Assert.That(random.Draws, Is.EqualTo(17));
            Assert.That(wish.Read().Data.Balance, Is.EqualTo(10));
            Assert.That(View().GetItemCount(RoguelikeWishService.StardustItemId), Is.Zero);
            Assert.That(game.Continue().Data.Snapshot.WishProgress.CharacterSinceFive, Is.EqualTo(89));
            Assert.That(wish.ReadHistory().Data, Is.Empty);
            Assert.That(wish.Draw(step, "R2003", 10).Succeeded, Is.False);
            files.FailReplace = false;

            var result = wish.Draw(step, "R2001", 10);
            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(result.Data.CommittedStepId, Is.EqualTo(wish.Read().Data.StepId).And.Not.EqualTo(step));
            Assert.That(random.Draws, Is.EqualTo(17));
            Assert.That(result.Data.Rewards.Select(reward => reward.Prize.Id), Is.EqualTo(
                new[] { 1012, 1014, 3001, 3001, 3001, 3001, 3001, 3001, 3001, 3001 }));
            Assert.That(result.Data.Rewards[0].IsNew, Is.True);
            var saved = game.Continue().Data.Snapshot;
            Assert.That(saved.RandomStep, Is.EqualTo(17));
            Assert.That(saved.WishProgress.CharacterSinceFive, Is.EqualTo(9));
            Assert.That(saved.WishProgress.WeaponSinceFive, Is.Zero);
            Assert.That(View().GetItemCount(RoguelikeWishService.FateItemId), Is.Zero);
            Assert.That(result.Data.Starglitter, Is.Zero);
            Assert.That(result.Data.Stardust, Is.EqualTo(120));
            Assert.That(View().GetItemCount(RoguelikeWishService.StarglitterItemId), Is.Zero);
            Assert.That(View().GetItemCount(RoguelikeWishService.StardustItemId), Is.EqualTo(120));
            Assert.That(wish.Draw(step, "R2001", 10).Succeeded, Is.False);
            Assert.That(View().GetItemCount(RoguelikeWishService.StardustItemId), Is.EqualTo(120));
            var history = wish.ReadHistory(RoguelikeWishKind.Character).Data;
            Assert.That(history.Select(entry => entry.PrizeId), Is.EqualTo(
                result.Data.Rewards.Select(reward => reward.Prize.Id).Reverse()));
            Assert.That(history.All(entry => entry.WishId == "R2001" && entry.ChapterId == 1), Is.True);
            Assert.That(wish.ReadHistory(RoguelikeWishKind.Weapon).Data, Is.Empty);
        }

        [Test]
        public void WishHistoryAndSharedPityFollowTheChapterSlot()
        {
            var random = new WishRandom();
            var wish = CreateWish(random, new RoguelikeWishProgress
            {
                CharacterSinceFive = 1, CharacterSinceFour = 6, WeaponSinceFive = 1, WeaponSinceFour = 6
            }, new RoguelikeWishRules(characterLimit: 4, weaponLimit: 4));
            Assert.That(wish.Draw(Step(), "R2001", 1).Succeeded, Is.True);
            Assert.That(wish.Draw(Step(), "R2002", 1).Succeeded, Is.True);
            var alternateWeapon = wish.Read().Data.Offers.Single(offer => offer.Pool.Id == "R2003");
            Assert.That(alternateWeapon.UntilFive, Is.EqualTo(2));
            Assert.That(alternateWeapon.UntilFour, Is.EqualTo(3));
            Assert.That(wish.Draw(Step(), "R2003", 1).Succeeded, Is.True);
            Assert.That(game.EnterNode(Step(), new RoguelikeNodeSelection
            { NodeId = "B", StageIndex = 2, ContentId = "Event", Kind = RoguelikeNodeKind.Event }).Succeeded, Is.True);
            var nextCharacter = wish.Read().Data.Offers.Single(offer => offer.Pool.Id == "R2004");
            Assert.That(nextCharacter.UntilFive, Is.EqualTo(2));
            Assert.That(nextCharacter.UntilFour, Is.EqualTo(3));
            Assert.That(wish.Draw(Step(), "R2004", 1).Succeeded, Is.True);
            Assert.That(game.CompleteEvent(Step(), new RoguelikeAcquisition()).Succeeded, Is.True);
            Assert.That(game.CompleteNode(Step(), new RoguelikeAcquisition()).Succeeded, Is.True);
            Assert.That(game.CompleteChapter(Step()).Succeeded, Is.True);
            Assert.That(game.SaveCompletedChapterToSlot(Step(), 1).Succeeded, Is.True);

            string entryPath = paths.GetChapterSlotPath(2, 1), original = files.Contents[entryPath];
            Assert.That(game.StartFromChapterSlot(2, 1).Succeeded, Is.True);
            var inherited = game.Continue().Data.Snapshot.WishProgress;
            Assert.That(inherited.CharacterSinceFive, Is.EqualTo(3));
            Assert.That(inherited.CharacterSinceFour, Is.EqualTo(8));
            Assert.That(inherited.WeaponSinceFive, Is.EqualTo(3));
            Assert.That(inherited.WeaponSinceFour, Is.EqualTo(8));
            Assert.That(wish.ReadHistory().Data.Select(entry => entry.WishId),
                Is.EqualTo(new[] { "R2004", "R2003", "R2002", "R2001" }));
            var result = wish.Draw(Step(), "R2002", 1);
            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(result.Data.Rewards[0].Prize.Stars, Is.EqualTo(5));
            Assert.That(random.Draws, Is.EqualTo(8));
            Assert.That(wish.ReadHistory().Data[0].ChapterId, Is.EqualTo(2));
            Assert.That(game.Continue().Data.Snapshot.WishProgress.CharacterSinceFive, Is.EqualTo(3));
            Assert.That(files.Contents[entryPath], Is.EqualTo(original));
        }

        [Test]
        public void WishPools_FollowPhaseAndSelectedWeapon_AndFullConstellationsConvert()
        {
            var random = new WishRandom();
            var wish = CreateWish(random, new RoguelikeWishProgress(), new RoguelikeWishRules(
                characterLimit: 1, weaponLimit: 1));
            var page = wish.Read().Data;
            Assert.That(page.Offers.Select(offer => offer.Pool.Id), Is.EqualTo(new[] { "R2001", "R2002", "R2003" }));
            var weapon = wish.Draw(page.StepId, "R2003", 1).Data;
            Assert.That(weapon.Rewards[0].Prize.Id, Is.EqualTo(3181));
            Assert.That(weapon.Starglitter, Is.EqualTo(10));
            Assert.That(wish.Draw(Step(), "R2002", 1).Data.Rewards[0].Prize.Id, Is.EqualTo(3155));
            var firstCharacter = wish.Draw(Step(), "R2001", 1).Data;
            Assert.That(firstCharacter.Rewards[0].IsNew, Is.True);
            Assert.That(firstCharacter.Starglitter, Is.Zero);
            for (int i = 0; i < 6; i++)
            {
                var duplicate = wish.Draw(Step(), "R2001", 1).Data;
                Assert.That(duplicate.Rewards[0].ConstellationItemId, Is.EqualTo("6101201"));
                Assert.That(duplicate.Starglitter, Is.EqualTo(10));
            }
            var converted = wish.Draw(Step(), "R2001", 1);
            Assert.That(converted.Data.Starglitter, Is.EqualTo(25));
            Assert.That(View().GetItemCount(RoguelikeWishService.StarglitterItemId), Is.EqualTo(105));
            Assert.That(View().GetItemCount("6101201"), Is.EqualTo(6));
            Assert.That(random.Draws, Is.Zero);
            Assert.That(game.EnterNode(Step(), new RoguelikeNodeSelection
            { NodeId = "B", StageIndex = 2, ContentId = "Event", Kind = RoguelikeNodeKind.Event }).Succeeded, Is.True);
            Assert.That(wish.Read().Data.Offers.Select(offer => offer.Pool.Id),
                Is.EqualTo(new[] { "R2004", "R2005", "R2006" }));
        }

        [Test]
        public void WishDebugBanners_AreReadOnlyAndUseTheActiveBalance()
        {
            var random = new WishRandom();
            var wish = CreateWish(random, new RoguelikeWishProgress(),
                new RoguelikeWishRules(characterLimit: 4, fourStarLimit: 3));
            string saved = files.Contents[paths.ActiveRunPath];
            var result = wish.ReadDebugBanners(new Version(1, 0, 2));
            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(result.Data.StepId, Is.Null);
            Assert.That(result.Data.Balance, Is.EqualTo(10));
            Assert.That(result.Data.Offers.Select(offer => offer.Pool.Id),
                Is.EqualTo(new[] { "R2004", "R2005", "R2006" }));
            Assert.That(result.Data.Offers.Any(offer => result.Data.CanDraw(offer, 1) || result.Data.CanDraw(offer, 10)), Is.False);
            Assert.That(wish.Draw(result.Data.StepId, "R2004", 1).Succeeded, Is.False);
            Assert.That(random.Draws, Is.Zero);
            Assert.That(files.Contents[paths.ActiveRunPath], Is.EqualTo(saved));
        }

        [Test]
        public void WishFiveStarLoss_ExcludesBothPhaseUps_AndGuaranteesTheNextBanner()
        {
            var random = new WishRandom { Index = 3 };
            var wish = CreateWish(random, new RoguelikeWishProgress
            {
                WeaponFivePlan = new RoguelikeWishFivePlan { Draw = 1, IsUp = false },
            }, new RoguelikeWishRules(weaponLimit: 1));
            var first = wish.Draw(Step(), "R2002", 1);
            Assert.That(first.Succeeded, Is.True, first.Message);
            Assert.That(first.Data.Rewards[0].Prize.Id, Is.EqualTo(3182));
            Assert.That(game.Continue().Data.Snapshot.WishProgress.WeaponFivePlan.IsUp, Is.True);
            var next = wish.Draw(Step(), "R2003", 1);
            Assert.That(next.Succeeded, Is.True, next.Message);
            Assert.That(next.Data.Rewards[0].Prize.Id, Is.EqualTo(3181));
            Assert.That(random.Draws, Is.EqualTo(1));
            Assert.That(game.Continue().Data.Snapshot.WishProgress.WeaponFivePlan.Sequence, Is.EqualTo(2));
        }

        [Test]
        public void WishFourStarNinthDraw_UsesIncreasedRate_AndRecordsOffBannerGuarantee()
        {
            var random = new WishRandom { Value = .5f };
            var wish = CreateWish(random, new RoguelikeWishProgress { CharacterSinceFour = 8 });
            var first = wish.Draw(Step(), "R2001", 1);
            Assert.That(first.Succeeded, Is.True, first.Message);
            Assert.That(first.Data.Rewards[0].Prize.Id, Is.EqualTo(1014));
            Assert.That(random.Draws, Is.EqualTo(3));

            random.Value = .9f;
            var save = RoguelikeSaveJson.Deserialize<ActiveRunSaveData>(files.Contents[paths.ActiveRunPath]);
            save.Snapshot.WishProgress.CharacterSinceFour = 9;
            files.Contents[paths.ActiveRunPath] = RoguelikeSaveJson.Serialize(save);
            var loss = wish.Draw(Step(), "R2001", 1);
            Assert.That(loss.Succeeded, Is.True, loss.Message);
            Assert.That(loss.Data.Rewards[0].Prize.Id, Is.EqualTo(1009));
            Assert.That(game.Continue().Data.Snapshot.WishProgress.CharacterFourUpGuaranteed, Is.True);
            Assert.That(random.Draws, Is.EqualTo(5));
        }

        [Test]
        public void NewGame_WithOnlyCorruptWishBackup_DoesNotRerollOrWrite()
        {
            var random = new WishRandom();
            var repository = new RoguelikeSaveRepository(paths, fileSystem: files);
            var saves = new RoguelikeSaveService(repository, wishRules: new RoguelikeWishRules(), wishRandom: random);
            Assert.That(repository.DeleteActiveRun().Succeeded, Is.True);
            var planPath = Path.Combine(paths.RootDirectory, "wish_planning.json");
            var backupPath = RoguelikeSavePaths.GetBackupPath(planPath);
            files.Contents[backupPath] = "{broken";

            var result = saves.StartNewGame(new RoguelikeProgressSnapshot { CurrentChapterId = 1, RunSeed = 22 });

            Assert.That(result.ErrorCode, Is.EqualTo(RoguelikeSaveErrorCode.ValidationFailed));
            Assert.That(random.Draws, Is.Zero);
            Assert.That(files.FileExists(planPath), Is.False);
            Assert.That(files.FileExists(paths.ActiveRunPath), Is.False);
            Assert.That(files.Contents[backupPath], Is.EqualTo("{broken"));
        }

        [Test]
        public void RestartingNewGame_ReusesBothPublicFiveStarPlans()
        {
            var random = new WishRandom();
            var repository = new RoguelikeSaveRepository(paths, fileSystem: files);
            var saves = new RoguelikeSaveService(repository, wishRules: new RoguelikeWishRules(), wishRandom: random);
            Assert.That(repository.DeleteActiveRun().Succeeded, Is.True);
            Assert.That(saves.StartNewGame(new RoguelikeProgressSnapshot { CurrentChapterId = 1, RunSeed = 22 }).Succeeded, Is.True);
            var first = saves.LoadActiveRun().Data.Snapshot.WishProgress;
            Assert.That(repository.DeleteActiveRun().Succeeded, Is.True);
            Assert.That(saves.StartNewGame(new RoguelikeProgressSnapshot { CurrentChapterId = 1, RunSeed = 23 }).Succeeded, Is.True);
            var second = saves.LoadActiveRun().Data.Snapshot.WishProgress;
            Assert.That(second.FivePlanSeed, Is.EqualTo(first.FivePlanSeed));
            Assert.That(second.CharacterFivePlan.Draw, Is.EqualTo(first.CharacterFivePlan.Draw));
            Assert.That(second.CharacterFivePlan.IsUp, Is.EqualTo(first.CharacterFivePlan.IsUp));
            Assert.That(second.WeaponFivePlan.Draw, Is.EqualTo(first.WeaponFivePlan.Draw));
            Assert.That(second.WeaponFivePlan.IsUp, Is.EqualTo(first.WeaponFivePlan.IsUp));
            Assert.That(random.Draws, Is.EqualTo(1));
        }

        [TestCase(0, 1)]
        [TestCase(4, 10)]
        public void WishWithInsufficientFates_DoesNotRollOrWrite(int owned, int count)
        {
            var random = new WishRandom();
            var wish = CreateWish(random, new RoguelikeWishProgress(), fates: owned);
            string saved = files.Contents[paths.ActiveRunPath];
            var page = wish.Read().Data;
            var offer = page.Offers.First(item => item.Pool.Id == "R2001");
            Assert.That(page.CanDraw(offer, 1), Is.EqualTo(owned > 0));
            Assert.That(page.CanDraw(offer, count), Is.False);
            var failed = wish.Draw(page.StepId, offer.Pool.Id, count);
            Assert.That(failed.Succeeded, Is.False);
            Assert.That(failed.Message, Does.Contain($"需 {count} 个，当前有 {owned} 个"));
            Assert.That(random.Draws, Is.Zero);
            Assert.That(files.Contents[paths.ActiveRunPath], Is.EqualTo(saved));
        }

        private RoguelikeWishService CreateWish(WishRandom random, RoguelikeWishProgress progress, RoguelikeWishRules rules = null,
            int fates = 10)
        {
            rules ??= new RoguelikeWishRules();
            progress.FivePlanSeed = 11;
            progress.CharacterFivePlan ??= new RoguelikeWishFivePlan { Draw = rules.CharacterLimit, IsUp = true };
            progress.WeaponFivePlan ??= new RoguelikeWishFivePlan { Draw = rules.WeaponLimit, IsUp = true };
            var save = RoguelikeSaveJson.Deserialize<ActiveRunSaveData>(files.Contents[paths.ActiveRunPath]);
            save.Snapshot.WishProgress = progress;
            if (fates > 0)
                save.Snapshot.Inventory.Items.Add(new RoguelikeItemStackData { ItemId = RoguelikeWishService.FateItemId, Quantity = fates });
            files.Contents[paths.ActiveRunPath] = RoguelikeSaveJson.Serialize(save);
            var flow = new RoguelikeFlowService(new RoguelikeSaveService(new RoguelikeSaveRepository(paths, fileSystem: files)), new Routes());
            var items = RoguelikeItemCatalog.LoadFromStreamingAssets();
            var catalog = new WishTemplates();
            catalog.TryGetWeapon(3001, out var fallback);
            return new RoguelikeWishService(flow, new RoguelikeInventoryService(flow, catalog, items),
                RoguelikeWishCatalog.LoadFromStreamingAssets(), catalog, items, catalog, new[] { fallback },
                selectPhase: snapshot => new Version(1, 0, snapshot.CurrentStageIndex < 2 ? 1 : 2),
                rules: rules, random: random);
        }

        private sealed class WishRandom : IBattleRandomSource
        {
            public int Index;
            public float Value = .9f;
            public int Draws { get; private set; }
            public float NextFloat01() { Draws++; return Value; }
            public int NextInt(int minInclusive, int maxExclusive) { Draws++; return minInclusive + Index % (maxExclusive - minInclusive); }
        }

        private sealed class WishTemplates : IRoguelikeEquipmentCatalog, IRoguelikeCharacterAcquisitionCatalog
        {
            public bool TryGetInitialLimits(int id, out float hp, out float energy) { hp = 100; energy = 60; return true; }
            public bool TryGetCharacterWeaponType(int id, out int type) { type = 0; return true; }
            public bool TryGetWeapon(int id, out WeaponAttributesData value)
            {
                value = new WeaponAttributesData
                {
                    WeaponID = id, WeaponName = "测试武器", WeaponType = 0,
                    Star = id < 3025 ? 3 : id < 3154 ? 4 : 5
                };
                return true;
            }
        }

        [TestCase("670101", "WishItem")]
        [TestCase("680101", "ExchangeCurrency")]
        [TestCase("680102", "ExchangeCurrency")]
        public void PublishedWishItemsAndCurrencies_CanBeGrantedSpentAndReadFromSave(string itemId, string category)
        {
            var items = RoguelikeItemCatalog.LoadFromStreamingAssets();
            game = CreateGame(items);
            Assert.That(View().TryGetItemDefinition(itemId, out var definition), Is.True);
            Assert.That(definition.UsageType, Is.EqualTo(category));
            Assert.That(File.Exists(Path.Combine(UnityEngine.Application.streamingAssetsPath, definition.Icon)), Is.True);

            Assert.That(game.AcquireItem(Step(), itemId, 3).Succeeded, Is.True);
            var costs = new[] { new RoguelikeItemStackData { ItemId = itemId, Quantity = 2 } };
            Assert.That(game.ConsumeItems(Step(), costs).Succeeded, Is.True);
            Assert.That(game.ConsumeItems(Step(), costs).Succeeded, Is.False);
            Assert.That(CreateGame(items).ReadInventory().Data.GetItemCount(itemId), Is.EqualTo(1));
        }

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
