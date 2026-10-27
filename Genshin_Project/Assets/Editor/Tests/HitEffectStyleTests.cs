#if ROGUELIKE_SAVE_TESTS
using NUnit.Framework;

[NonParallelizable]
public sealed class HitEffectStyleTests
{
    private sealed class CountingRandom : IBattleRandomSource
    {
        public int Draws;
        public int NextInt(int min, int max) { Draws++; return min; }
        public float NextFloat01() { Draws++; return .5f; }
    }
    [Test]
    public void RuntimeTableCoversEveryWeaponAttackAndElement()
    {
        var settings = UIHitEffectStyle.Load();
        for (int weapon = 0; weapon < 5; weapon++)
            foreach (bool heavy in new[] { false, true })
                Assert.Greater(settings.Get((HitEffectWeapon)weapon, heavy).Duration, 0);
        foreach (string element in new[] { "Physical", "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Geo", "Dendro" })
            Assert.IsNotNull(settings.GetParticles(element));
    }

    [Test]
    public void FixedVisualSeedRepeatsWithoutCombatRandomDraws()
    {
        var previous = BattleRandom.Source;
        var random = new CountingRandom(); BattleRandom.SetSource(random);
        try
        {
            var profile = UIHitEffectStyle.Load().Get(HitEffectWeapon.Sword, false);
            for (int hit = 0; hit < 100; hit++)
            {
                uint a = HitEffectMath.CueSeed(12345, hit, 0), b = HitEffectMath.CueSeed(12345, hit, 0);
                Assert.AreEqual(a, b);
                Assert.AreEqual(HitEffectMath.Center(profile, a), HitEffectMath.Center(profile, b));
                Assert.AreNotEqual(a, HitEffectMath.CueSeed(12345, hit, 1));
                Assert.IsFalse(HitEffectMath.Critical(a, 0));
                Assert.IsTrue(HitEffectMath.Critical(a, 1));
            }
            Assert.AreEqual(0, random.Draws);
        }
        finally { BattleRandom.SetSource(previous); }
    }

    [Test]
    public void EveryWeaponSamplesItsOwnFullConfiguredRange()
    {
        var settings = UIHitEffectStyle.Load();
        for (int weapon = 0; weapon < 5; weapon++)
            foreach (bool heavy in new[] { false, true })
            {
                var profile = settings.Get((HitEffectWeapon)weapon, heavy);
                for (uint seed = 0; seed < 100; seed++)
                {
                    var point = HitEffectMath.Center(profile, seed);
                    Assert.That(point.x, Is.InRange(profile.Number("CenterXMinRatio"), profile.Number("CenterXMaxRatio")));
                    Assert.That(point.y, Is.InRange(profile.Number("CenterYMinRatio"), profile.Number("CenterYMaxRatio")));
                    Assert.That(HitEffectMath.Scale(profile, seed, 99, 100), Is.GreaterThan(0).And.LessThanOrEqualTo(profile.Number("MaxScaleMultiplier")));
                }
            }
    }

    [Test]
    public void TimeJitterNeverReversesOneTargetsHitOrder()
    {
        for (int target = 0; target < 9; target++)
        {
            float previous = 0;
            for (int hit = 0; hit < 100; hit++)
            {
                float time = HitEffectMath.CueTime(HitEffectMath.CueSeed(12345, hit, target), hit, .05f, .5f, previous);
                Assert.GreaterOrEqual(time, previous); previous = time;
            }
        }
    }
}
#endif
