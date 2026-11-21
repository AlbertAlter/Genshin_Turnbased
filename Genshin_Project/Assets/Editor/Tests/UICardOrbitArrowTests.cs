#if ROGUELIKE_SAVE_TESTS
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class UICardOrbitArrowTests
    {
        [Test]
        public void EveryArrowKeepsItsLaneAndMovesOnlyUpOrDown()
        {
            var style = UICardOrbitArrowStyle.Load();
            var rect = new Rect(-98.5f, -169, 197, 338);
            for (int i = 0; i < style.ArrowCount; i++)
            {
                float start = i * style.SpawnStagger;
                var a = UICardOrbitArrowMath.Sample(style, rect, i, start + style.TravelSeconds * .3f, UICardStatusFlow.Buff);
                var b = UICardOrbitArrowMath.Sample(style, rect, i, start + style.TravelSeconds * .6f, UICardStatusFlow.Buff);
                var c = UICardOrbitArrowMath.Sample(style, rect, i, start + style.TravelSeconds * .3f, UICardStatusFlow.Debuff);
                var d = UICardOrbitArrowMath.Sample(style, rect, i, start + style.TravelSeconds * .6f, UICardStatusFlow.Debuff);
                Assert.That(b.Position.y, Is.GreaterThan(a.Position.y));
                Assert.That(d.Position.y, Is.LessThan(c.Position.y));
                Assert.That(a.Position.x, Is.EqualTo(b.Position.x));
                Assert.That(c.Position.x, Is.EqualTo(d.Position.x));
                Assert.That(a.Depth, Is.EqualTo(b.Depth));
                Assert.That(b.Position.y - a.Position.y, Is.EqualTo(c.Position.y - d.Position.y).Within(.001f));
            }
        }
        [Test]
        public void ArrowsAreHiddenBeforeSpawningAndNeverRepeatAfterTheirSinglePass()
        {
            var s = UICardOrbitArrowStyle.Load(); var rect = new Rect(0, 0, 197, 338);
            for (int i = 0; i < s.ArrowCount; i++)
            {
                float start = i * s.SpawnStagger;
                Assert.That(UICardOrbitArrowMath.Sample(s, rect, i, start - .1f, UICardStatusFlow.Buff).Opacity, Is.Zero);
                Assert.That(UICardOrbitArrowMath.Sample(s, rect, i, start + s.TravelSeconds * .5f, UICardStatusFlow.Buff).Opacity, Is.GreaterThan(0));
                Assert.That(UICardOrbitArrowMath.Sample(s, rect, i, start + s.TravelSeconds * 2.5f, UICardStatusFlow.Buff).Opacity, Is.Zero);
            }
        }
        [Test]
        public void AppliedStatusKeepsItsBuffTypeSnapshotAndRemovalDoesNotReplay()
        {
            var main = new StatusMainData { StatusType = "Debuff" };
            var instance = new StatusInstance { MainData = main };
            var applied = new StatusEventData(instance, null, true, "Applied");
            main.StatusType = "Buff";
            Assert.That(BattleUiStatusRiseView.TryGetFlow(applied, out var flow), Is.True);
            Assert.That(flow, Is.EqualTo(UICardStatusFlow.Debuff));
            Assert.That(BattleUiStatusRiseView.TryGetFlow(new StatusEventData(instance, null, false, "Removed"), out _), Is.False);
        }
        [Test]
        public void InvalidPreviewEditsAreRejectedWithoutMutatingTheLoadedStyle()
        {
            var s = UICardOrbitArrowStyle.Load(); float before = s.TravelSeconds;
            Assert.Throws<InvalidDataException>(() => s.WithNumbers(new Dictionary<string, float> { ["TravelSeconds"] = 0 }));
            Assert.Throws<InvalidDataException>(() => s.WithNumbers(new Dictionary<string, float> { ["ArrowCount"] = 2.5f }));
            Assert.That(s.TravelSeconds, Is.EqualTo(before));
        }
    }
}
#endif
