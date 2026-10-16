using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using OfficeOpenXml;

namespace GenshinTurnBased.Tests.EditMode
{
    public class EffectTableParsingTests
    {
        [Test]
        public void FlatEnergy_ElementIsOptionalAcrossEffectTables(
            [Values("SkillsEffect", "Status_Effect", "EnemySkill_Effect")] string sheet,
            [Values(null, "", "None", "none", "Electro")] string element)
        {
            Assert.DoesNotThrow(() => TableValidator.ValidateEffectRow(sheet, 3,
                "GainEnergy", element, 0, "Flat", "Self", 0, 0, "",
                sheet == "EnemySkill_Effect", sheet != "Status_Effect"));
        }

        [TestCase("Damage", "", "")]
        [TestCase("GainEnergy", "", "Based")]
        [TestCase("GainEnergy", null, "Based")]
        [TestCase("GainEnergy", "None", "")]
        [TestCase("GainEnergy", "None", "Unknown")]
        public void OptionalFlatElement_DoesNotRelaxDamageBasedOrModeValidation(
            string effectType, string element, string mode)
        {
            Assert.Throws<TableValidationException>(() => TableValidator.ValidateEffectRow(
                "SkillsEffect", 3, effectType, element, 0, mode, "Self", 0, 0, "", false));
        }

        [TestCase("SkillsEffect", false, true)]
        [TestCase("Status_Effect", false, false)]
        [TestCase("EnemySkill_Effect", true, true)]
        public void ApplyStatus_ZeroDurationIsAcceptedAsPermanent(string sheet, bool enemy, bool checkNumber)
        {
            Assert.DoesNotThrow(() => TableValidator.ValidateEffectRow(sheet, 3,
                "ApplyStatus", "", 0, "", "Enemy", -1, 3, "", enemy, checkNumber));
        }

        [TestCase(null, 1)]
        [TestCase("", 1)]
        [TestCase("3", 3)]
        public void ExecuteCount_DefaultsOnlyWhenOmitted(string raw, int expected)
            => Assert.That(TableValidator.ParseExecuteCount(raw), Is.EqualTo(expected));

        [Test]
        public void ExecuteCount_PreservesSupportedRuntimeHandler()
        {
            Assert.That(TableValidator.ParseExecuteCount("1011_C1()"), Is.EqualTo(1));
            Assert.That(TableValidator.ParseExecuteCountHandler("1011_C1()"), Is.EqualTo("1011_C1()"));
            Assert.That(TableValidator.ParseExecuteCountHandler("3"), Is.Empty);
            Assert.That(ExecuteCountEvaluator.Evaluate(1, "1011_C1()", null), Is.Zero);
        }

        [TestCase(0, 0)]
        [TestCase(3, 3)]
        [TestCase(6, 5)]
        public void LisaC1_CountsAtMostFiveDistinctHitTargets(int hitTargetCount, int expected)
            => Assert.That(LisaScriptHooks.CapC1Count(hitTargetCount), Is.EqualTo(expected));

        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("1.5")]
        [TestCase("All")]
        [TestCase("Unknown()")]
        [TestCase("1011_C1(2)")]
        public void ExecuteCount_RejectsInvalidValues(string raw)
            => Assert.Throws<TableValidationException>(() => TableValidator.ParseExecuteCount(raw));

        [TestCase("", "0.8*TotalDEF,2", "0.8*TotalDEF,2")]
        [TestCase("0.9", "0.8*TotalDEF,2", "0.9*TotalDEF,2")]
        [TestCase("0,0", "0.8*TotalDEF,2", "0*TotalDEF,0")]
        [TestCase("1.1,0", "0.8*TotalATK,2", "1.1*TotalATK,0")]
        [TestCase("1.2", "1.1*TotalATK,0", "1.2*TotalATK,0")]
        public void LevelFields_PreserveExplicitZeroAndLatestInheritance(string current, string previous, string expected)
            => Assert.That(HitDataParser.InheritLevelFields(current, previous), Is.EqualTo(expected));

        [Test]
        public void OptionalEffectColumns_AreReadByHeaderAndEnergyConflictsFail()
        {
            using var package = new ExcelPackage();
            var sheet = package.Workbook.Worksheets.Add("SkillsEffect");
            sheet.Cells[1, 1].Value = "ExecuteCount";
            sheet.Cells[1, 2].Value = "DamageHook";
            sheet.Cells[1, 3].Value = "EnergyGainMode";
            sheet.Cells[1, 4].Value = "Param2";
            sheet.Cells[2, 1].Value = 3;
            sheet.Cells[2, 2].Value = "Lisa_Skill()";
            sheet.Cells[2, 3].Value = "Based";
            Assert.That(Call("ReadExecuteCount", sheet, 2), Is.EqualTo(3));
            Assert.That(Call("ReadOptionalEffectText", sheet, 2, "DamageHook"), Is.EqualTo("Lisa_Skill()"));
            Assert.That(Call("ReadEffectParam2", sheet, 2, "GainEnergy", 11), Is.EqualTo("Based"));
            sheet.Cells[2, 4].Value = "Flat";
            var error = Assert.Throws<TargetInvocationException>(() => Call("ReadEffectParam2", sheet, 2, "GainEnergy", 11));
            Assert.That(error.InnerException, Is.TypeOf<TableValidationException>());
        }

        [Test]
        public void RepeatedContent_SharesEffectLimitsAndReactionIdentity()
        {
            var status = new StatusInstance();
            var action = new StatusActionData { ActionKey = "repeat-regression", MaxTimePerEffect = 1 };
            var identities = new HashSet<long>();
            int attempts = 0, collected = 0;
            BattleExecution.Run(() => BattleExecution.RepeatEffect(3, null, id =>
            {
                attempts++;
                identities.Add(id);
                if (BattleResolutionContext.TryCollect(status, action)) collected++;
                return Task.CompletedTask;
            })).GetAwaiter().GetResult();
            Assert.That(attempts, Is.EqualTo(3));
            Assert.That(collected, Is.EqualTo(1));
            Assert.That(identities.Count, Is.EqualTo(1));
            Assert.That(BattleResolutionContext.CurrentEffect, Is.Null);
        }

        [Test]
        public void UnknownDamageHook_FailsInsteadOfApplyingUnmodifiedDamage()
        {
            bool executed = false;
            Assert.Throws<NotSupportedException>(() =>
                BattleExecution.Run(() => BattleExecution.RepeatEffect(1, "Lisa_Skill()", id =>
                {
                    executed = true;
                    return Task.CompletedTask;
                })).GetAwaiter().GetResult());
            Assert.That(executed, Is.False);
            Assert.That(BattleExecution.IsBusy, Is.False);
        }

        private static object Call(string name, params object[] arguments)
            => typeof(DataManager).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, arguments);
    }
}
