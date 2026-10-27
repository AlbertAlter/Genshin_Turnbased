#if ROGUELIKE_SAVE_TESTS
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public class RoguelikeSaveValidationTests
    {
        private RoguelikeSaveValidator validator;

        [SetUp]
        public void SetUp()
        {
            validator = new RoguelikeSaveValidator();
        }

        [Test]
        public void ChapterOnePermanentEntry_IsRejected()
        {
            var save = RoguelikeSaveTestFactory.ChapterEntry(2, 1);
            save.EntryChapterId = 1;
            save.SourceCompletedChapterId = 0;
            save.Snapshot.CurrentChapterId = 1;

            var result = validator.Validate(save);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(RoguelikeSaveErrorCode.ValidationFailed));
        }

        [TestCase(0)]
        [TestCase(4)]
        public void ChapterSlotOutsideOneToThree_IsRejected(int slotIndex)
        {
            var save = RoguelikeSaveTestFactory.ChapterEntry(2, 1);
            save.SlotIndex = slotIndex;
            Assert.That(validator.Validate(save).Succeeded, Is.False);
        }

        [Test]
        public void ChapterTwoEntry_MustComeFromChapterOne()
        {
            var save = RoguelikeSaveTestFactory.ChapterEntry(2, 1);
            save.SourceCompletedChapterId = 2;
            Assert.That(validator.Validate(save).Succeeded, Is.False);
        }

        [Test]
        public void DuplicateCharacterId_IsRejected()
        {
            var snapshot = RoguelikeSaveTestFactory.Snapshot(1);
            snapshot.Characters.Add(RoguelikeSaveJson.DeepClone(snapshot.Characters[0]));
            Assert.That(validator.Validate(snapshot).Succeeded, Is.False);
        }

        [TestCase(0)]
        [TestCase(91)]
        public void CharacterLevelOutsideRange_IsRejected(int level)
        {
            var snapshot = RoguelikeSaveTestFactory.Snapshot(1);
            snapshot.Characters[0].Level = level;
            Assert.That(validator.Validate(snapshot).Succeeded, Is.False);
        }

        [Test]
        public void CharacterSkillLevels_MustContainExactlyFourValues()
        {
            var snapshot = RoguelikeSaveTestFactory.Snapshot(1);
            snapshot.Characters[0].SkillLevels = new[] { 1, 1, 1 };
            Assert.That(validator.Validate(snapshot).Succeeded, Is.False);
        }

        [Test]
        public void EveryOwnedCharacter_RequiresVitals()
        {
            var snapshot = RoguelikeSaveTestFactory.Snapshot(1);
            snapshot.CharacterVitals.Clear();

            Assert.That(validator.Validate(snapshot).Succeeded, Is.False);
        }

        [Test]
        public void PartyMember_MustBeOwned()
        {
            var snapshot = RoguelikeSaveTestFactory.Snapshot(1);
            snapshot.PartyCharacterIds.Add(9999);

            Assert.That(validator.Validate(snapshot).Succeeded, Is.False);
        }

        [Test]
        public void WeaponInstance_CannotBeEquippedByTwoCharacters()
        {
            var snapshot = RoguelikeSaveTestFactory.Snapshot(1);
            var second = RoguelikeSaveJson.DeepClone(snapshot.Characters[0]);
            second.CharacterID = 1010;
            snapshot.Characters.Add(second);
            snapshot.CharacterVitals.Add(new RoguelikeCharacterVitalData
            {
                CharacterID = 1010,
                CurrentHealth = 500f,
                MaxHealth = 500f,
                CurrentEnergy = 60f,
                MaxEnergy = 60f,
            });

            Assert.That(validator.Validate(snapshot).Succeeded, Is.False);
        }

        [Test]
        public void ArtifactEquipmentSlot_MustMatchInstanceSlot()
        {
            var snapshot = RoguelikeSaveTestFactory.Snapshot(1);
            snapshot.Characters[0].EquippedArtifacts[0].Slot = ArtifactSlot.Plume;

            Assert.That(validator.Validate(snapshot).Succeeded, Is.False);
        }

        [Test]
        public void NewerSchema_IsRejectedExplicitly()
        {
            var save = RoguelikeSaveTestFactory.ChapterEntry(2, 1);
            save.Metadata.SchemaVersion = RoguelikeSaveValidator.CurrentSchemaVersion + 1;

            var result = validator.Validate(save);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(RoguelikeSaveErrorCode.UnsupportedSchema));
        }

        [Test]
        public void SchemaOne_IsRejectedAfterChapterDataUpgrade()
        {
            var save = RoguelikeSaveTestFactory.ChapterEntry(2, 1);
            save.Metadata.SchemaVersion = 1;

            Assert.That(validator.Validate(save).Succeeded, Is.False);
        }

        [Test]
        public void EmptyPendingReward_IsAllowedButEmptyRewardObjectIsRejected()
        {
            var snapshot = RoguelikeSaveTestFactory.Snapshot(1);
            Assert.That(validator.Validate(snapshot).Succeeded, Is.True);

            snapshot.PendingRewardData = new PendingRewardData();
            Assert.That(validator.Validate(snapshot).Succeeded, Is.False);
        }
    }
}
#endif
