using System;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    internal sealed class OutOfRangeBattleRandomSource : IBattleRandomSource
    {
        public int NextInt(int minInclusive, int maxExclusive)
        {
            return minInclusive;
        }

        public float NextFloat01()
        {
            return 1f;
        }
    }

    public class ArtifactGeneratorTests
    {
        [SetUp]
        public void SetUp()
        {
            BattleRandom.ResetSource();
        }

        [TearDown]
        public void TearDown()
        {
            BattleRandom.ResetSource();
        }

        [Test]
        public void Generate_SharedBattleRandomSameSeedProducesIdenticalArtifactAndStep()
        {
            ArtifactGenerationConfig config = CreateConfig();
            var firstRandom = new SystemBattleRandomSource(123456);
            BattleRandom.SetSource(firstRandom);
            var firstGenerator = new ArtifactGenerator(config);

            GeneratedArtifact first = firstGenerator.Generate(5, targetLevel: 20);

            var secondRandom = new SystemBattleRandomSource(123456);
            BattleRandom.SetSource(secondRandom);
            var secondGenerator = new ArtifactGenerator(config);
            GeneratedArtifact second = secondGenerator.Generate(5, targetLevel: 20);

            AssertArtifactsEqual(first, second);
            Assert.That(firstRandom.Step, Is.EqualTo(secondRandom.Step));
            Assert.That(firstRandom.Step, Is.GreaterThan(0));
        }

        [Test]
        public void Generate_SharedInjectedSequenceControlsEveryDecisionAndCallCount()
        {
            var random = new SequenceBattleRandomSource(new[]
            {
                0f, 0f, 0f,
                0f, 0f,
                0f, 0f,
                0f, 0f,
                0f, 0f
            });
            BattleRandom.SetSource(random);
            var generator = new ArtifactGenerator(CreateConfig());

            GeneratedArtifact artifact = generator.Generate(5, targetLevel: 4);

            Assert.That(artifact.Slot, Is.EqualTo(ArtifactSlot.Flower));
            Assert.That(artifact.MainAttributeType, Is.EqualTo("HP"));
            Assert.That(artifact.MainAttributeValue, Is.EqualTo(140f));
            Assert.That(artifact.SecondaryAttributes.Count, Is.EqualTo(4));
            Assert.That(artifact.SecondaryAttributes[0].AttributeType, Is.EqualTo("ATK"));
            Assert.That(artifact.SecondaryAttributes[1].AttributeType, Is.EqualTo("DEF"));
            Assert.That(artifact.SecondaryAttributes[2].AttributeType, Is.EqualTo("ElementalMastery"));
            Assert.That(artifact.SecondaryAttributes[3].AttributeType, Is.EqualTo("CritRate"));
            Assert.That(random.FloatCallCount, Is.EqualTo(11));
        }

        [Test]
        public void Generate_RejectsRandomValuesOutsideUnitInterval()
        {
            var generator = new ArtifactGenerator(
                CreateConfig(),
                new OutOfRangeBattleRandomSource());

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => generator.Generate(5, ArtifactSlot.Flower));

            StringAssert.Contains("[0, 1)", exception.Message);
        }

        private static ArtifactGenerationConfig CreateConfig()
        {
            var config = new ArtifactGenerationConfig();
            config.AddMaxLevel(5, 20);
            config.AddMainGrowth("HP", 5, 100f, 10f);
            for (int slot = (int)ArtifactSlot.Flower; slot <= (int)ArtifactSlot.Circlet; slot++)
                config.AddMainWeight((ArtifactSlot)slot, "HP", 1f);

            config.AddSecondaryRule("ATK", 1f, 10f);
            config.AddSecondaryRule("DEF", 1f, 10f);
            config.AddSecondaryRule("ElementalMastery", 1f, 10f);
            config.AddSecondaryRule("CritRate", 1f, 10f);
            config.AddSecondaryRule("CritDMG", 1f, 10f);
            return config;
        }

        private static void AssertArtifactsEqual(GeneratedArtifact first, GeneratedArtifact second)
        {
            Assert.That(first.Star, Is.EqualTo(second.Star));
            Assert.That(first.Slot, Is.EqualTo(second.Slot));
            Assert.That(first.Level, Is.EqualTo(second.Level));
            Assert.That(first.MaxLevel, Is.EqualTo(second.MaxLevel));
            Assert.That(first.MainAttributeType, Is.EqualTo(second.MainAttributeType));
            Assert.That(first.MainAttributeValue, Is.EqualTo(second.MainAttributeValue));
            Assert.That(first.SecondaryAttributes.Count, Is.EqualTo(second.SecondaryAttributes.Count));

            for (int index = 0; index < first.SecondaryAttributes.Count; index++)
            {
                ArtifactSecondaryAttribute firstAttribute = first.SecondaryAttributes[index];
                ArtifactSecondaryAttribute secondAttribute = second.SecondaryAttributes[index];
                Assert.That(firstAttribute.AttributeType, Is.EqualTo(secondAttribute.AttributeType));
                Assert.That(firstAttribute.Value, Is.EqualTo(secondAttribute.Value));
                Assert.That(firstAttribute.RollCount, Is.EqualTo(secondAttribute.RollCount));
            }
        }
    }
}
