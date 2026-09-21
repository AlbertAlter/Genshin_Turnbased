using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public class CharacterSkillIconResolverTests
    {
        [TestCase("2_1_Pyro", "art_assets/Skill_Icon/#Normal/2_1_Pyro.png")]
        [TestCase("2_2_Pyro.png", "art_assets/Skill_Icon/#Normal/2_2_Pyro.png")]
        [TestCase("1009_T01", "art_assets/Skill_Icon/1009/1009_T01.png")]
        [TestCase("1010_T02.png", "art_assets/Skill_Icon/1010/1010_T02.png")]
        [TestCase("Skill_Icon/1009/1009_T01", "art_assets/Skill_Icon/1009/1009_T01.png")]
        [TestCase("art_assets/Skill_Icon/1009/1009_T02.png", "art_assets/Skill_Icon/1009/1009_T02.png")]
        public void UsesTheConfiguredIconWithoutReconstructingItsName(
            string configured,
            string expected)
        {
            Assert.That(
                CharacterSkillIconResolver.TryGetRelativePath(configured, out string actual),
                Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void EmptyIconDoesNotProduceAPath()
        {
            Assert.That(
                CharacterSkillIconResolver.TryGetRelativePath("", out _),
                Is.False);
        }
    }
}
