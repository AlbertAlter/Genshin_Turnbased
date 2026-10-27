using System;
using System.Globalization;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public class HitDataParserTests
    {
        [TestCase("0.3656*TotalATK,1", 0.3656f, 1f)]
        [TestCase("0.3656*TotalATK;1", 0.3656f, 1f)]
        [TestCase("3.2*TotalATK,2", 3.2f, 2f)]
        [TestCase("0.48", 0.48f, 0f)]
        [TestCase(null, 0f, 0f)]
        [TestCase("", 0f, 0f)]
        public void Parse_AcceptsMEAndMultiplierOnly(string raw, float multiplier, float aura)
        {
            AssertHit(HitDataParser.Parse(raw), multiplier, aura);
        }

        [TestCase("0.393", "0.3656*TotalATK,1", 0.393f, 1f)]
        [TestCase("", "0.3656*TotalATK,1", 0.3656f, 1f)]
        [TestCase("0.393*TotalATK,0", "0.3656*TotalATK,1", 0.393f, 0f)]
        [TestCase("0.393*TotalATK,2", "0.3656*TotalATK,1", 0.393f, 2f)]
        [TestCase("0,0", "0.3656*TotalATK,1", 0f, 0f)]
        public void Merge_InheritsOmittedFieldsButPreservesExplicitZero(
            string current, string levelOne, float multiplier, float aura)
        {
            AssertHit(HitDataParser.MergeWithLevel1(current, levelOne), multiplier, aura);
        }

        [TestCase("%SE_Test.Hits1.M*3,E", 6f, 1f)]
        [TestCase("%SE_Test.Hits1.M*3,2", 6f, 2f)]
        [TestCase("%SE_Test.Hits1.M*0.2,0", 0.4f, 0f)]
        [TestCase("%STE_Test.Hits2.M,E*0.5", 2f, 0.5f)]
        public void ResolveReference_UsesME(string raw, float multiplier, float aura)
        {
            AssertHit(HitDataParser.ResolveReferencedHit(raw, new HitData(2f, 1f)),
                multiplier, aura);
        }

        [TestCase("1*TotalATK,20,1")]
        [TestCase("1*TotalATK;20;1")]
        public void Parse_RejectsRetiredMPE(string raw)
        {
            Assert.Throws<FormatException>(() => HitDataParser.Parse(raw));
        }

        [TestCase("%SE_Test.Hits1.M*3,20,1")]
        [TestCase("%SE_Test.Hits1.P,1")]
        [TestCase("%SE_Test.Hits1.M,P")]
        public void ResolveReference_RejectsRetiredPoise(string raw)
        {
            Assert.Throws<FormatException>(() =>
                HitDataParser.ResolveReferencedHit(raw, new HitData(2f, 1f)));
        }

        [Test]
        public void ParseReference_PreservesHitIndex()
        {
            var reference = HitDataParser.ParseReference("%STE_Test.Hits2.M*3,E");
            Assert.That(reference.effectId, Is.EqualTo("STE_Test"));
            Assert.That(reference.hitIndex, Is.EqualTo(1));
            Assert.That(reference.scale, Is.EqualTo(3f));
        }

        [Test]
        public void Parse_UsesInvariantDecimalSeparator()
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                AssertHit(HitDataParser.Parse("0.3656*TotalATK,1"), 0.3656f, 1f);
            }
            finally { CultureInfo.CurrentCulture = original; }
        }

        private static void AssertHit(HitData hit, float multiplier, float aura)
        {
            Assert.That(hit.Multiplier, Is.EqualTo(multiplier).Within(0.000001f));
            Assert.That(hit.ElementAura, Is.EqualTo(aura).Within(0.000001f));
        }
    }
}
