#if ROGUELIKE_SAVE_TESTS
using NUnit.Framework;
using UnityEngine;

[NonParallelizable]
public sealed class HitShakeTests
{
    private sealed class CountingRandom : IBattleRandomSource
    {
        public int Draws;
        public int NextInt(int min, int max) { Draws++; return min; }
        public float NextFloat01() { Draws++; return .5f; }
    }
    [Test]
    public void DefaultsAreLocalOnlyAndNonBasicDamageDoesNotShake()
    {
        var style = HitShakeStyle.Load();
        Assert.AreEqual("CardNormal", style.Resolve("Normal", "", 1).Card.ID);
        Assert.AreEqual("CardHeavy", style.Resolve("Heavy", "", 1).Card.ID);
        Assert.IsNull(style.Resolve("Normal", "", 1).Screen);
        Assert.IsNull(style.Resolve("Burst", "", 1).Card);
    }
    [Test]
    public void VisualSeedRepeatsStaysBoundedAndExpiresWithoutBusinessRandom()
    {
        var previous = BattleRandom.Source; var random = new CountingRandom(); BattleRandom.SetSource(random);
        try
        {
            var p = HitShakeStyle.Load().Get("CardNormal"); var size = new Vector2(197, 338);
            for (int i = 0; i < 100; i++)
            {
                float age = p.Duration * i / 100;
                var a = HitShakeMath.Sample(p, size, 12345, age); var b = HitShakeMath.Sample(p, size, 12345, age);
                Assert.AreEqual(a.Offset, b.Offset); Assert.AreEqual(a.Rotation, b.Rotation);
                Assert.LessOrEqual(a.Offset.magnitude, Mathf.Min(size.x, size.y) * p.MaxTranslation + .0001f);
            }
            Assert.AreEqual(Vector2.zero, HitShakeMath.Sample(p, size, 12345, p.Duration).Offset);
            Assert.AreEqual(0, random.Draws);
        }
        finally { BattleRandom.SetSource(previous); }
    }
    [Test]
    public void DenseHitsClampTheirCombinedOffset()
    {
        var p = HitShakeStyle.Load().Get("CardHeavy"); var size = new Vector2(197, 338);
        var sum = new HitShakePose();
        for (int i = 0; i < 50; i++)
        { var pose = HitShakeMath.Sample(p, size, (uint)i, i * .001f); sum.Offset += pose.Offset; sum.Rotation += pose.Rotation; }
        sum = HitShakeMath.Clamp(sum, size, p.MaxTranslation, p.MaxRotation);
        Assert.LessOrEqual(sum.Offset.magnitude, 197 * p.MaxTranslation + .0001f);
        Assert.LessOrEqual(Mathf.Abs(sum.Rotation), p.MaxRotation);
    }
    [Test]
    public void SharedHitShakesScreenOnceAndCancelRestoresOriginalLayout()
    {
        var go = new GameObject("ShakeTest", typeof(RectTransform));
        try
        {
            var root = (RectTransform)go.transform; root.sizeDelta = new Vector2(1920, 1080);
            root.anchoredPosition = new Vector2(10, 20);
            var controller = go.AddComponent<UIHitShakeController>(); controller.AutomaticClock = false; controller.BindScreen(root);
            var p = HitShakeStyle.Load().Get("ScreenDefault");
            for (int target = 0; target < 5; target++) controller.PulseScreen(1, 1, 1, "Effect", 1, p, (uint)target);
            Assert.AreEqual(1, controller.ActiveScreenImpulses);
            controller.PulseScreen(1, 1, 1, "Effect", 2, p, 123);
            Assert.AreEqual(2, controller.ActiveScreenImpulses);
            controller.Advance(.01f); Assert.AreEqual(new Vector2(10, 20), root.anchoredPosition);
            controller.Cancel(1); Assert.AreEqual(0, controller.ActiveScreenImpulses);
            Assert.AreEqual(new Vector2(10, 20), root.anchoredPosition);
        }
        finally { Object.DestroyImmediate(go); }
    }
}
#endif
