#if ROGUELIKE_SAVE_TESTS
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class UIStatusNamePopupTests
    {
        private static UIStatusNamePopupStyle Geometry() => new UIStatusNamePopupStyle
        {
            AnchorY = 1, CardGapHeightRatio = .03f, StackGapHeightRatio = .015f, RiseHeightRatio = .12f,
            Lifetime = .9f, FadeIn = .12f, FadeOut = .22f, FontSize = 20, LineHeight = 1.36f,
            MaxWidth = 362, PaddingX = 80, PaddingY = 11,
        };

        [Test]
        public void NamesGrowThenWrapWithinTheAuthoredMaximum()
        {
            var style = Geometry();
            Assert.That(style.Size(40, 1).x, Is.EqualTo(200).Within(.001f));
            var wrapped = style.Size(1000, 3);
            Assert.That(wrapped.x, Is.EqualTo(362));
            Assert.That(wrapped.y, Is.EqualTo(103.6f).Within(.001f));
        }
        [Test]
        public void LaterNameStartsAboveTheEarlierNamesCurrentTop()
        {
            var style = Geometry();
            float first = style.Bottom(200, 338);
            Assert.That(first, Is.EqualTo(210.14f).Within(.001f));
            float top = first + 338 * style.RiseHeightRatio * style.RiseProgress(.3f) + style.Size(40, 1).y;
            Assert.That(style.Bottom(200, 338, top), Is.EqualTo(277.93f).Within(.001f));
        }
        [Test]
        public void TheHtmlClockFadesBothEndsAndRisesLinearly()
        {
            var style = Geometry();
            Assert.That(style.Alpha(0), Is.Zero);
            Assert.That(style.Alpha(.06f), Is.EqualTo(.5f).Within(.001f));
            Assert.That(style.Alpha(.79f), Is.EqualTo(.5f).Within(.001f));
            Assert.That(style.Alpha(.9f), Is.Zero);
            Assert.That(style.RiseProgress(.45f), Is.EqualTo(.5f).Within(.001f));
        }
        [Test]
        public void PublishedPreviewValuesAreAcceptedByTheRuntimeReader()
        {
            var style = UIStatusNamePopupStyle.Load();
            Assert.That(style.FontSize, Is.GreaterThan(0));
            Assert.That(style.PaddingX * 2 + style.FontSize, Is.LessThanOrEqualTo(style.MaxWidth));
            Assert.That(style.GoldBorderOpacity, Is.InRange(0, 1));
        }
        [Test]
        public void APreviewWorkbookWithMissingKeysIsRejected()
            => Assert.Throws<InvalidDataException>(() => UIStatusNamePopupStyle.Parse(new Dictionary<string, string>()));
    }
}
#endif
