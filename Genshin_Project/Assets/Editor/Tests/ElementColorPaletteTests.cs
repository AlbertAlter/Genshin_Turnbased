#if ROGUELIKE_SAVE_TESTS
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class ElementColorPaletteTests
    {
        [Test]
        public void PaletteIsSharedByElementKey_WithoutChangingConfiguredColor()
        {
            var palette = ElementColorPalette.Parse(Entries());
            var electro = palette.GetColor("Electro");
            Assert.That(electro.r, Is.EqualTo(222f / 255));
            Assert.That(electro.g, Is.EqualTo(186f / 255));
            Assert.That(electro.b, Is.EqualTo(1));
            Assert.That(electro.a, Is.EqualTo(1));
            Assert.That(palette.GetColor("None"), Is.EqualTo(Color.white));
            Assert.That(palette.GetColor(null), Is.EqualTo(Color.white));
            Assert.That(palette.GetColor("Physical"), Is.EqualTo(Color.white));
            Assert.That(palette.TryGetColor("Unknown", out _), Is.False);
        }

        [Test]
        public void MissingDuplicateAndUnknownKeys_AreRejected()
        {
            var entries = Entries(); entries.RemoveAt(0);
            Assert.Throws<InvalidDataException>(() => ElementColorPalette.Parse(entries));
            entries = Entries(); entries.Add(entries[0]);
            Assert.Throws<InvalidDataException>(() => ElementColorPalette.Parse(entries));
            entries = Entries(); entries[0] = new KeyValuePair<string, string>("Fire", "#FFFFFF");
            Assert.Throws<InvalidDataException>(() => ElementColorPalette.Parse(entries));
        }

        [Test]
        public void BodyColorsMustBeSixDigitRgb_AlphaAndNamedColorsAreNotAnotherAuthority()
        {
            foreach (string value in new[] { "red", "#ABC", "#FF000080", "#GG0000", "123456", "" })
            {
                var entries = Entries(); entries[0] = new KeyValuePair<string, string>("Pyro", value);
                Assert.Throws<InvalidDataException>(() => ElementColorPalette.Parse(entries));
            }
        }

        [Test]
        public void ColorLookup_DoesNotDrawBattleRandom()
        {
            var previous = BattleRandom.Source;
            var random = new SystemBattleRandomSource(7);
            BattleRandom.SetSource(random);
            try
            {
                var palette = ElementColorPalette.Parse(Entries());
                for (int i = 0; i < 100; i++) palette.GetColor("Electro");
                Assert.That(random.Step, Is.Zero);
            }
            finally { BattleRandom.SetSource(previous); }
        }

        private static List<KeyValuePair<string, string>> Entries()
        {
            var entries = new List<KeyValuePair<string, string>>();
            foreach (string key in new[] { "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Geo", "Dendro", "Physical" })
                entries.Add(new KeyValuePair<string, string>(key, key == "Electro" ? "#DEBAFF" : "#FFFFFF"));
            return entries;
        }
    }
}
#endif
