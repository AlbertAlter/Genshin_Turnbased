using NUnit.Framework;

namespace GenshinTurnBased.Editor.Tests
{
    public sealed class LevelUpCalculatorTests
    {
        private LevelUpRequirementsTable _requirements;

        [SetUp]
        public void SetUp()
        {
            _requirements = CreateRequirements();
        }

        [Test]
        public void ApplyExperience_UsesCurrentLevelExperience()
        {
            LevelUpResult result = LevelUpCalculator.ApplyExperience(
                _requirements, LevelUpTarget.Character, 1, 40, 100, 90);

            Assert.That(result.Level, Is.EqualTo(2));
            Assert.That(result.Experience, Is.EqualTo(40));
        }

        [Test]
        public void ApplyExperience_RetainsOverflowAtTemporaryLevelCap()
        {
            LevelUpResult result = LevelUpCalculator.ApplyExperience(
                _requirements, LevelUpTarget.Character, 19, 0, 1000, 20);

            Assert.That(result.Level, Is.EqualTo(20));
            Assert.That(result.Experience, Is.EqualTo(630));
            Assert.That(result.ReachedLevelCap, Is.True);
            Assert.That(result.IsMaximumLevel, Is.False);
        }

        [Test]
        public void ApplyExperience_UsesStoredOverflowAfterCapIsRaised()
        {
            LevelUpResult result = LevelUpCalculator.ApplyExperience(
                _requirements, LevelUpTarget.Character, 20, 630, 0, 40);

            Assert.That(result.Level, Is.EqualTo(26));
            Assert.That(result.Experience, Is.EqualTo(30));
        }

        [Test]
        public void ApplyExperience_DiscardsExperienceAtMaximumLevel()
        {
            LevelUpResult result = LevelUpCalculator.ApplyExperience(
                _requirements, LevelUpTarget.Character, 89, 0, 250, 90);

            Assert.That(result.Level, Is.EqualTo(90));
            Assert.That(result.Experience, Is.Zero);
            Assert.That(result.IsMaximumLevel, Is.True);
        }

        [Test]
        public void GetExperienceToReach_HandlesArtifactLevelZero()
        {
            int required = LevelUpCalculator.GetExperienceToReach(
                _requirements, LevelUpTarget.Artifact, 0, 0, 2, 3);

            Assert.That(required, Is.EqualTo(63));
        }

        [Test]
        public void GetSkillMaterialsToReach_GroupsByConfiguredMaterialType()
        {
            var required = LevelUpCalculator.GetSkillMaterialsToReach(_requirements, 1, 9);

            Assert.That(required[1], Is.EqualTo(9));
            Assert.That(required[2], Is.EqualTo(19));
            Assert.That(required[3], Is.EqualTo(22));
        }

        private static LevelUpRequirementsTable CreateRequirements()
        {
            var table = new LevelUpRequirementsTable();
            for (int level = 2; level <= 90; level++)
            {
                table.AddCharacter(level, level == 20 ? 370 : 100);
                table.AddWeapon(3, level, 100);
                table.AddWeapon(4, level, 160);
                table.AddWeapon(5, level, 240);
            }

            for (int level = 1; level <= 20; level++)
            {
                if (level <= 12) table.AddArtifact(3, level, 27 + level * 3);
                if (level <= 16) table.AddArtifact(4, level, 36 + level * 4);
                table.AddArtifact(5, level, 45 + level * 5);
            }

            table.AddSkill(2, 3, 1);
            table.AddSkill(3, 6, 1);
            table.AddSkill(4, 4, 2);
            table.AddSkill(5, 6, 2);
            table.AddSkill(6, 9, 2);
            table.AddSkill(7, 4, 3);
            table.AddSkill(8, 6, 3);
            table.AddSkill(9, 12, 3);
            table.AddSkill(10, 1, 4);
            table.Validate();
            return table;
        }
    }
}
