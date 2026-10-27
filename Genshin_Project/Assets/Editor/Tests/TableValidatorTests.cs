using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public class TableValidatorTests
    {
        [Test]
        public void APCost_AcceptsOnlyTheImplementedWeaponParameterContract()
        {
            Assert.DoesNotThrow(() => TableValidator.ValidateAPCost("-Index(1)", "Heavy", 432120101, "Hook(432120101)"));
            Assert.DoesNotThrow(() => TableValidator.ValidateAPCost("-5", "SwitchIn"));
            Assert.DoesNotThrow(() => TableValidator.ValidateAPCost("Free", "Heavy"));
            Assert.Throws<TableValidationException>(() => TableValidator.ValidateAPCost("-Index(1)", "Heavy"));
            Assert.Throws<TableValidationException>(() => TableValidator.ValidateAPCost("-Index(1)", "Heavy", 432120102, "Hook(432120101)"));
            Assert.Throws<TableValidationException>(() => TableValidator.ValidateAPCost("-Index(1)", "Heavy", 432120101, "Hook(Unknown)"));
            Assert.Throws<TableValidationException>(() => TableValidator.ValidateAPCost("-Index(2)", "Heavy", 432120101, "Hook(432120101)"));
            Assert.Throws<TableValidationException>(() => TableValidator.ValidateAPCost("-Index(1)", "Unknown", 432120101, "Hook(432120101)"));
        }

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
        [TestCase("Unknown")]
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
