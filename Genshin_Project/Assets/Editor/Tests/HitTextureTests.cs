#if ROGUELIKE_SAVE_TESTS
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class HitTextureTests
{
    [Test]
    public void RuntimeProfilesHaveEveryPublishedTextureAndIndependentWeaponSettings()
    {
        var table = HitEffectTextureStyle.Load();
        var impact = UIHitEffectStyle.Load();
        foreach (string file in table.Files)
        {
            Assert.IsTrue(HitEffectTextureStyle.ValidFileName(file));
            Assert.IsTrue(File.Exists(Path.Combine(Application.streamingAssetsPath, HitEffectTextureStyle.ArtRoot, file)), file);
        }
        for (int weapon = 0; weapon < 5; weapon++) foreach (bool heavy in new[] { false, true })
        {
            var p = table.Get((HitEffectWeapon)weapon, heavy);
            Assert.IsNotEmpty(p.Bodies);
            Assert.Greater(p.Duration(impact.Get((HitEffectWeapon)weapon, heavy)), 0);
            Assert.AreEqual(weapon == 2 || weapon == 4, p.Number("FlightSeconds") > 0);
        }
        Assert.AreEqual(2, table.Get(HitEffectWeapon.Sword, false).Bodies.Length);
        Assert.AreNotEqual(table.Get(HitEffectWeapon.Sword, false).Bodies[0], table.Get(HitEffectWeapon.Claymore, false).Bodies[0]);
    }
    [Test]
    public void VisualSeedRepeatsVariantsAndDoesNotConsumeCombatRandom()
    {
        var original = BattleRandom.Source; var random = new CountingRandom(); BattleRandom.SetSource(random);
        try
        {
            bool first = false, second = false;
            for (uint seed = 0; seed < 512; seed++)
            {
                int a = HitTextureMath.Variant(seed, 2);
                Assert.AreEqual(a, HitTextureMath.Variant(seed, 2));
                Assert.That(a, Is.InRange(0, 1)); first |= a == 0; second |= a == 1;
                Assert.AreEqual(0, HitTextureMath.Variant(seed, 1));
            }
            Assert.IsTrue(first && second); Assert.AreEqual(0, random.Draws);
        }
        finally { BattleRandom.SetSource(original); }
    }
    [Test]
    public void AlphaBoundsIgnoreTransparentPaddingAndSubthresholdNoise()
    {
        var pixels = new Color32[8 * 6];
        pixels[0] = new Color32(255, 255, 255, 1);
        pixels[2 + 8 * 1] = new Color32(255, 255, 255, 8);
        pixels[5 + 8 * 4] = new Color32(255, 255, 255, 255);
        Assert.AreEqual(new RectInt(2, 1, 4, 4), HitTextureMath.AlphaBounds(pixels, 8, 6));
        Assert.AreEqual(new RectInt(0, 0, 8, 6), HitTextureMath.AlphaBounds(new Color32[48], 8, 6));
    }
    [Test]
    public void RevealEraseAndFlightRespectTheirOwnClocks()
    {
        Assert.AreEqual(0, HitTextureMath.Reveal(-1, .04f));
        Assert.AreEqual(1, HitTextureMath.Reveal(.04f, .04f));
        Assert.AreEqual(0, HitTextureMath.Erase(.04f, .08f, .12f));
        Assert.That(HitTextureMath.Erase(.14f, .08f, .12f), Is.EqualTo(.5f).Within(.00001f));
        Assert.AreEqual(1, HitTextureMath.Erase(.21f, .08f, .12f));
        var start = new Vector2(-300, 100); var end = new Vector2(400, -70);
        Assert.AreEqual(start, HitTextureMath.Flight(start, end, -1, .2f));
        Assert.AreEqual(end, HitTextureMath.Flight(start, end, .2f, .2f));
        Assert.AreEqual(end, HitTextureMath.Flight(start, end, 20, .2f));
    }
    [TestCase("../other.png")]
    [TestCase("C:/image.png")]
    [TestCase("folder/image.png")]
    [TestCase("image.jpg")]
    public void PathsCannotEscapeComponentFolder(string file) => Assert.IsFalse(HitEffectTextureStyle.ValidFileName(file));
    private sealed class CountingRandom : IBattleRandomSource
    {
        public int Draws;
        public int NextInt(int min, int max) { Draws++; return min; }
        public float NextFloat01() { Draws++; return .5f; }
    }
}
#endif
