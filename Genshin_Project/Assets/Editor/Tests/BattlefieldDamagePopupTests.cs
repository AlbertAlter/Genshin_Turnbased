#if ROGUELIKE_SAVE_TESTS
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class BattlefieldStatusPopupTests
    {
        [Test]
        public void PublishedTable_LoadsWhiteFadeInAndUpwardMotion()
        {
            var style = BattlefieldStatusPopupStyle.Load();
            Assert.That(style.Color, Is.EqualTo(UnityEngine.Color.white));
            Assert.That(style.FadeIn, Is.GreaterThan(0));
            Assert.That(style.RiseHeightRatio, Is.GreaterThan(0));
            Assert.That(style.Alpha(0), Is.Zero);
            Assert.That(style.Alpha(style.FadeIn), Is.EqualTo(1).Within(.0001f));
            Assert.That(style.Alpha(style.Lifetime - style.FadeOut * .5f), Is.EqualTo(.5f).Within(.0001f));
            Assert.That(style.Alpha(style.Lifetime), Is.Zero);
        }

        [TestCase(1, "冻结", "冻结")]
        [TestCase(0, "隐藏状态", "")]
        [TestCase(2, "非1不显示", "")]
        [TestCase(1, null, "")]
        [TestCase(1, "   ", "")]
        public void OnlyVisibleNonEmptyName_IsDisplayed(int display, string name, string expected)
        {
            var status = new StatusInstance { StatusID2 = "ST_Test", MainData = new StatusMainData { Display = display, StatusName = name } };
            Assert.That(BattlefieldStatusPopupStyle.TextFor(new StatusEventData(status, null, true, "Applied")), Is.EqualTo(expected));
            Assert.That(BattlefieldStatusPopupStyle.TextFor(new StatusEventData(status, null, false, "Removed")), Is.Empty);
        }

        [Test]
        public void AppliedFact_PreservesNameDisplayAndHostInsteadOfCaster()
        {
            var main = new StatusMainData { Display = 1, StatusName = "冻结" };
            var host = new FieldPosition(null, BattleSide.Enemy, 3);
            var status = new StatusInstance { StatusID2 = "ST_Test", MainData = main };
            var fact = new StatusEventData(status, host, true, "Applied");
            main.StatusName = "后来更改"; main.Display = 0; host.SlotIndex = 5;
            Assert.That(BattlefieldStatusPopupStyle.TextFor(fact), Is.EqualTo("冻结"));
            Assert.That(fact.HostSide, Is.EqualTo(BattleSide.Enemy));
            Assert.That(fact.HostSlot, Is.EqualTo(3));
        }

        [Test]
        public void BoundsAndStacking_StayAboveCard()
        {
            var style = BattlefieldStatusPopupStyle.Load();
            float first = style.CenterAbove(200, 300, 60);
            Assert.That(first - 30, Is.GreaterThan(200));
            float second = style.CenterAbove(200, 300, 60, first + 30);
            Assert.That(second - 30, Is.GreaterThan(first + 30));
        }

        [Test]
        public void NegativeRiseAndOverlappingFades_AreRejected()
        {
            var values = new Dictionary<string, string>
            {
                ["Enabled"] = "1", ["FontHeightRatio"] = ".1", ["LifetimeSeconds"] = ".9",
                ["FadeInSeconds"] = ".12", ["FadeOutSeconds"] = ".22", ["RiseHeightRatio"] = ".12",
                ["CardGapHeightRatio"] = ".03", ["StackGapHeightRatio"] = ".015", ["MaxVisible"] = "64",
                ["Color"] = "#FFFFFFFF",
            };
            values["RiseHeightRatio"] = "-1";
            Assert.Throws<InvalidDataException>(() => BattlefieldStatusPopupStyle.Parse(values));
            values["RiseHeightRatio"] = ".1"; values["FadeInSeconds"] = "1";
            Assert.Throws<InvalidDataException>(() => BattlefieldStatusPopupStyle.Parse(values));
        }
    }

    public sealed class BattlefieldDamagePopupTests
    {
        [TestCase(0, false)] [TestCase(0, true)]
        [TestCase(1, false)] [TestCase(1, true)]
        public void NoHPLoss_NeverShowsDamage_EvenWhenShieldDamageOrZeroDisplayIsEnabled(int mode, bool showZero)
        {
            var style = new BattlefieldDamagePopupStyle { AmountMode = mode, ShowZero = showZero };
            foreach (var hit in new[]
            {
                new DamageResolvedEvent { DamageBeforeShield = 100f, ActualHPDamage = 0f, ShieldAbsorbed = 100f },
                new DamageResolvedEvent { DamageBeforeShield = 100f, ActualHPDamage = 0f, IsImmune = true, ImmuneDamage = 100f },
                new DamageResolvedEvent { DamageBeforeShield = 0f, ActualHPDamage = 0f }
            })
                Assert.That(style.TryGetAmount(new DamageEventData(hit), out _), Is.False);
        }

        [TestCase(0, 150f)] [TestCase(1, 50f)]
        public void HPShieldOverflow_ShowsConfiguredAmountOnlyAfterRealHPLoss(int mode, float expected)
        {
            var style = new BattlefieldDamagePopupStyle { AmountMode = mode };
            var fact = new DamageEventData(new DamageResolvedEvent
            { DamageBeforeShield = 150f, ActualHPDamage = 50f, ShieldAbsorbed = 100f });
            Assert.That(style.TryGetAmount(fact, out float amount), Is.True);
            Assert.That(amount, Is.EqualTo(expected));
        }

        [Test]
        public void PublishedTable_LoadsWhiteAndCardRelativeRanges()
        {
            var style = BattlefieldDamagePopupStyle.Load();
            Assert.That(style.XMin, Is.EqualTo(.25f));
            Assert.That(style.XMax, Is.EqualTo(.75f));
            Assert.That(style.YMin, Is.EqualTo(.6f));
            Assert.That(style.YMax, Is.EqualTo(.65f));
            Assert.That(style.Color, Is.EqualTo(UnityEngine.Color.white));
        }

        [Test]
        public void SpatialSample_IsReproducibleWithinConfiguredRange_WithoutBattleRandomDraws()
        {
            var style = BattlefieldDamagePopupStyle.Load();
            var source = new CountingSource();
            var previous = BattleRandom.Source;
            BattleRandom.SetSource(source);
            try
            {
                var first = BattlefieldDamagePopupLayout.Sample(style, 1, 1, 1, 1);
                Assert.That(BattlefieldDamagePopupLayout.Sample(style, 1, 1, 1, 1), Is.EqualTo(first));
                var positions = new HashSet<UnityEngine.Vector2>();
                for (int i = 1; i <= 1000; i++)
                {
                    var point = BattlefieldDamagePopupLayout.Sample(style, 1, i, 10, i % 5);
                    Assert.That(point.x, Is.InRange(style.XMin, style.XMax));
                    Assert.That(point.y, Is.InRange(style.YMin, style.YMax));
                    positions.Add(point);
                }
                Assert.That(positions.Count, Is.GreaterThan(950));
                Assert.That(source.Draws, Is.Zero);
            }
            finally { BattleRandom.SetSource(previous); }
        }

        [Test]
        public void FixedSpatialRange_ProducesExactlyConfiguredPoint()
        {
            var style = BattlefieldDamagePopupStyle.Load();
            style.XMin = style.XMax = .5f;
            style.YMin = style.YMax = .7f;
            var point = BattlefieldDamagePopupLayout.Sample(style, 1, 20, 30, 4);
            Assert.That(point.x, Is.EqualTo(.5f)); Assert.That(point.y, Is.EqualTo(.7f));
        }

        [Test]
        public void CriticalFontSizeMultiplier_AffectsOnlyCriticalDamage()
        {
            var style = BattlefieldDamagePopupStyle.Load();
            Assert.That(style.CriticalFontSizeMultiplier, Is.EqualTo(1.5f));
            Assert.That(style.FontSizeMultiplier(true), Is.EqualTo(1.5f));
            Assert.That(style.FontSizeMultiplier(false), Is.EqualTo(1));
            Assert.That(BattlefieldDamagePopupStyle.Parse(Values()).CriticalFontSizeMultiplier, Is.EqualTo(1));
            foreach (string value in new[] { "0", "-1", "NaN", "Infinity" })
            {
                var values = Values(); values["CriticalFontSizeMultiplier"] = value;
                Assert.Throws<InvalidDataException>(() => BattlefieldDamagePopupStyle.Parse(values));
            }
        }

        [TestCase(1234.4f, 0, "1234")]
        [TestCase(1234.5f, 0, "1235")]
        [TestCase(12.25f, 1, "12.3")]
        public void Format_UsesConfiguredPrecision(float amount, int decimals, string expected)
        {
            Assert.That(BattlefieldDamagePopupLayout.Format(amount, decimals), Is.EqualTo(expected));
        }

        [Test]
        public void InvalidRangesAndUnknownFields_AreRejected()
        {
            var values = Values(); values["CenterXMinRatio"] = "0.8";
            Assert.Throws<InvalidDataException>(() => BattlefieldDamagePopupStyle.Parse(values));
            values = Values(); values["FadeOutSeconds"] = "2";
            Assert.Throws<InvalidDataException>(() => BattlefieldDamagePopupStyle.Parse(values));
            values = Values(); values["FontHeightRatio"] = "0";
            Assert.Throws<InvalidDataException>(() => BattlefieldDamagePopupStyle.Parse(values));
            values = Values(); values["TimeRandom"] = "1";
            Assert.Throws<InvalidDataException>(() => BattlefieldDamagePopupStyle.Parse(values));
        }

        private static Dictionary<string, string> Values()
        {
            return new Dictionary<string, string>
            {
                ["Enabled"] = "1", ["CenterXMinRatio"] = ".25", ["CenterXMaxRatio"] = ".75",
                ["CenterYMinRatio"] = ".6", ["CenterYMaxRatio"] = ".65", ["FontHeightRatio"] = ".12",
                ["LifetimeSeconds"] = ".8", ["FadeOutSeconds"] = ".2", ["RiseHeightRatio"] = ".1",
                ["MaxVisible"] = "64", ["Decimals"] = "0", ["AmountMode"] = "0",
                ["ShowZeroDamage"] = "0", ["Color"] = "#FFFFFFFF",
            };
        }

        private sealed class CountingSource : IBattleRandomSource
        {
            public int Draws;
            public int NextInt(int min, int max) { Draws++; return min; }
            public float NextFloat01() { Draws++; return 0; }
        }
    }
}
#endif
