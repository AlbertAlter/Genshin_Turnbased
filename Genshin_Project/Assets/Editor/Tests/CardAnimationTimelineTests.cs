using System;
using NUnit.Framework;

public sealed class CardAnimationTimelineTests
{
    [Test]
    public void UnevenFrames_KeepTheirOwnDuration_AndLoopWithoutChangingSpeed()
    {
        var timeline = new CardAnimationTimeline(new[] { .08f, .04f, .12f });
        Assert.AreEqual(0, timeline.FrameAt(.07));
        Assert.AreEqual(1, timeline.FrameAt(.09));
        Assert.AreEqual(2, timeline.FrameAt(.13));
        Assert.AreEqual(0, timeline.FrameAt(timeline.Duration));
        Assert.AreEqual(1, timeline.FrameAt(timeline.Duration * 100 + .09));
        for (int i = 0; i < timeline.Count; i++) Assert.AreEqual(i, timeline.FrameAt(timeline.Start(i)));
    }

    [Test]
    public void SourceRateIsPreserved_AndInvalidTimingIsRejected()
    {
        var durations = new float[80];
        for (int i = 0; i < durations.Length; i++) durations[i] = .08f;
        var timeline = new CardAnimationTimeline(durations);
        Assert.AreEqual(6.4, timeline.Duration, .00001);
        Assert.AreEqual(12.5, timeline.Count / timeline.Duration, .00001);
        Assert.Throws<ArgumentException>(() => new CardAnimationTimeline(new[] { 0f }));
        Assert.Throws<ArgumentException>(() => new CardAnimationTimeline(new[] { float.NaN }));
        Assert.Throws<ArgumentException>(() => new CardAnimationTimeline(Array.Empty<float>()));
    }
}
