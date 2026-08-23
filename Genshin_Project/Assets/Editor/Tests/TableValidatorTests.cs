using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public class TableValidatorTests
    {
        [TestCase("Based")]
        [TestCase("Flat")]
        public void GainEnergy_AcceptsModeFromParam2(string mode)
        {
            Assert.DoesNotThrow(() => TableValidator.ValidateEffectRow(
                "TestEffect",
                3,
                "GainEnergy",
                "Pyro",
                0,
                mode,
                "Self",
                0,
                0,
                "",
                false
            ));
        }

        [TestCase("")]
        [TestCase("Direct")]
        [TestCase("based")]
        public void GainEnergy_RejectsInvalidModeInParam2(string mode)
        {
            Assert.Throws<TableValidationException>(() => TableValidator.ValidateEffectRow(
                "TestEffect",
                3,
                "GainEnergy",
                "Pyro",
                0,
                mode,
                "Self",
                0,
                0,
                "",
                false
            ));
        }
    }
}
