using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public class TableValidatorTests
    {
        [TestCase(0)]
        [TestCase(2)]
        public void Shield_RejectsMissingOrDuplicateShieldEffects(int count)
        {
            var main = new StatusMainData { StatusID = 98971, StatusID2 = "ST_ShieldValidation", StatusType = "Shield" };
            var effects = new System.Collections.Generic.List<StatusEffectData>();
            for (int i = 0; i < count; i++) effects.Add(new StatusEffectData {
                StatusEffectID = main.StatusID * 100 + i + 1, EffectType = "Shield", ScriptHook = "Chance(0.5)" });
            var error = Assert.Throws<TableValidationException>(() => TableValidator.ValidateShieldStatusDefinition(main, effects));
            StringAssert.Contains(main.StatusID2, error.Message);
            StringAssert.Contains("实际" + count + "条", error.Message);
        }

        [Test]
        public void Shield_AllowsOtherEffects_AndOnlyCountsItsOwnShield()
        {
            var main = new StatusMainData { StatusID = 98971, StatusID2 = "ST_ShieldValidation", StatusType = "Shield" };
            var effects = new[] {
                new StatusEffectData { StatusEffectID = 9897101, EffectType = "Shield" },
                new StatusEffectData { StatusEffectID = 9897102, EffectType = "ChangeControl" },
                new StatusEffectData { StatusEffectID = 9897103, EffectType = "Buff" },
                new StatusEffectData { StatusEffectID = 9897104, EffectType = "BindStatus" },
                new StatusEffectData { StatusEffectID = 9897201, EffectType = "Shield" }
            };
            Assert.DoesNotThrow(() => TableValidator.ValidateShieldStatusDefinition(main, effects));
            Assert.Throws<TableValidationException>(() => TableValidator.ValidateShieldStatusDefinition(main, new[] { effects[4] }));
            main.StatusType = "OverallShield";
            Assert.DoesNotThrow(() => TableValidator.ValidateShieldStatusDefinition(main, new StatusEffectData[0]));
        }

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
