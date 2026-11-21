using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public class CharacterSkillIconResolverTests
    {
        [TestCase("2_1_Pyro", "art_assets/Skill_Icon/#Normal/2_1.png")]
        [TestCase("2_2_Pyro.png", "art_assets/Skill_Icon/#Normal/2_2.png")]
        [TestCase("4_2", "art_assets/Skill_Icon/#Normal/4_2.png")]
        [TestCase("Skill_Icon/#Normal/0_1_Cryo.png", "art_assets/Skill_Icon/#Normal/0_1.png")]
        [TestCase("1009_T01", "art_assets/Skill_Icon/1009/1009_T01.png")]
        [TestCase("1015_S04", "art_assets/Skill_Icon/1015/1015_S04.png")]
        [TestCase("1015_C06", "art_assets/Skill_Icon/1015/1015_C06.png")]
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

        [TestCase("1015_S04", "Pyro", "art_assets/Skill_Icon/#Others/Pyro_BG.png")]
        [TestCase("Skill_Icon/1029/1029_S03.png", "Electro", "art_assets/Skill_Icon/#Others/Electro_BG.png")]
        [TestCase("2_1_Pyro", "Pyro", "art_assets/Skill_Icon/#Others/Pyro_BG.png")]
        [TestCase("0_2", "Hydro", "art_assets/Skill_Icon/#Others/Hydro_BG.png")]
        [TestCase("1015_T01", "Pyro", null)]
        public void ResolvesElementLayerForTransparentButtonArt(string icon, string element, string expected)
        {
            Assert.That(CharacterSkillIconResolver.ElementBackground(icon, element), Is.EqualTo(expected));
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
