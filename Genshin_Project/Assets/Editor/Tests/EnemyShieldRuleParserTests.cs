using System;
using NUnit.Framework;
using OfficeOpenXml;

namespace GenshinTurnBased.Tests.EditMode
{
    public class EnemyShieldRuleParserTests
    {
        [TestCase("12U", 12f)]
        [TestCase(" 0.5u ", 0.5f)]
        public void ParseInitialGauge_RequiresNumericUValue(string raw, float expected)
        {
            Assert.That(EnemyShieldRuleParser.ParseInitialGauge(raw), Is.EqualTo(expected));
        }

        [TestCase("")]
        [TestCase("12")]
        [TestCase("0U")]
        [TestCase("1*TotalHP")]
        public void ParseInitialGauge_RejectsNonGaugeDefinitions(string raw)
        {
            Assert.Throws<FormatException>(() => EnemyShieldRuleParser.ParseInitialGauge(raw));
        }

        [TestCase("2", EnemyShieldRuleKind.ElementAmountMultiplier, 3f)]
        [TestCase("1U", EnemyShieldRuleKind.FixedGauge, 1f)]
        [TestCase("0.5+0.01U*PoiseDMG", EnemyShieldRuleKind.ElementAmountPlusPoiseFormula, 1.75f)]
        [TestCase("0.25U+0.01U*PoiseDMG", EnemyShieldRuleKind.FixedGaugePlusPoiseFormula, 1.25f)]
        [TestCase("0.01U*PoiseDMG", EnemyShieldRuleKind.FixedGaugePlusPoiseFormula, 1f)]
        [TestCase("0", EnemyShieldRuleKind.ElementAmountMultiplier, 0f)]
        public void Parse_RecognizesSupportedGrammar(string raw, EnemyShieldRuleKind kind, float expected)
        {
            EnemyShieldRule rule = EnemyShieldRuleParser.Parse(raw, "Pyro", "Hydro");

            Assert.That(rule.Kind, Is.EqualTo(kind));
            Assert.That(rule.Evaluate(1.5f, 100f, 999f), Is.EqualTo(expected).Within(0.0001f));
            Assert.That(rule.RawText, Is.EqualTo(raw));
        }

        [Test]
        public void Parse_Blank_ReturnsNoRule()
        {
            EnemyShieldRule rule = EnemyShieldRuleParser.Parse("  ", "Pyro", "Hit");

            Assert.That(rule.Kind, Is.EqualTo(EnemyShieldRuleKind.None));
            Assert.That(rule.HasEffect, Is.False);
        }

        [Test]
        public void PhysicalBareNumber_RemainsElementAmountMultiplier()
        {
            EnemyShieldRule rule = EnemyShieldRuleParser.Parse("0.25", "Dendro", "Physical");

            Assert.That(rule.Kind, Is.EqualTo(EnemyShieldRuleKind.ElementAmountMultiplier));
            Assert.That(rule.Evaluate(0f, 100f, 100f), Is.Zero);
        }

        [Test]
        public void Evaluate_ClampsToCurrentShieldGauge()
        {
            EnemyShieldRule rule = EnemyShieldRuleParser.Parse("2", "Pyro", "Hydro");

            Assert.That(rule.Evaluate(4f, 0f, 3f), Is.EqualTo(3f));
        }

        [TestCase("-1")]
        [TestCase("NaN")]
        [TestCase("1U+bad")]
        [TestCase("0.5+0.01*PoiseDMG")]
        [TestCase("eval(1)")]
        public void Parse_RejectsUnsupportedOrInvalidText(string raw)
        {
            Assert.Throws<FormatException>(() => EnemyShieldRuleParser.Parse(raw, "Pyro", "Hydro"));
        }

        [Test]
        public void TableLoader_ParsesRulesAndIgnoresBlankCells()
        {
            using var package = CreateValidWorkbook();

            EnemyShieldRuleTable table = EnemyShieldRuleTableLoader.Load(package.Workbook.Worksheets["Type1"]);

            Assert.That(table.TryGet("pyro", "hydro", out EnemyShieldRule rule), Is.True);
            Assert.That(rule.RawText, Is.EqualTo("2"));
            Assert.That(table.TryGet("Pyro", "Hit", out _), Is.False);
        }

        [Test]
        public void TableLoader_RejectsUnknownColumn()
        {
            using var package = CreateValidWorkbook();
            package.Workbook.Worksheets["Type1"].Cells[1, 2].Value = "Unknown";

            Assert.Throws<TableValidationException>(() =>
                EnemyShieldRuleTableLoader.Load(package.Workbook.Worksheets["Type1"]));
        }

        [Test]
        public void TableLoader_RejectsUnknownShieldRow()
        {
            using var package = CreateValidWorkbook();
            package.Workbook.Worksheets["Type1"].Cells[2, 1].Value = "Unknown";

            Assert.Throws<TableValidationException>(() =>
                EnemyShieldRuleTableLoader.Load(package.Workbook.Worksheets["Type1"]));
        }

        [Test]
        public void TableLoader_RejectsDuplicateAttackColumn()
        {
            using var package = CreateValidWorkbook();
            package.Workbook.Worksheets["Type1"].Cells[1, 3].Value = "Pyro";

            Assert.Throws<TableValidationException>(() =>
                EnemyShieldRuleTableLoader.Load(package.Workbook.Worksheets["Type1"]));
        }

        [Test]
        public void TableLoader_RejectsDuplicateRow()
        {
            using var package = CreateValidWorkbook();
            package.Workbook.Worksheets["Type1"].Cells[3, 1].Value = "Pyro";

            Assert.Throws<TableValidationException>(() =>
                EnemyShieldRuleTableLoader.Load(package.Workbook.Worksheets["Type1"]));
        }

        [Test]
        public void TableLoader_RejectsInvalidRuleCell()
        {
            using var package = CreateValidWorkbook();
            package.Workbook.Worksheets["Type1"].Cells[2, 2].Value = "1+Unknown";

            Assert.Throws<TableValidationException>(() =>
                EnemyShieldRuleTableLoader.Load(package.Workbook.Worksheets["Type1"]));
        }

        [Test]
        public void EmptyOptionalSheets_DoNotAffectType1Loading()
        {
            using var package = CreateValidWorkbook();
            package.Workbook.Worksheets.Add("Type2");
            package.Workbook.Worksheets.Add("Sheet3");

            Assert.DoesNotThrow(() =>
                EnemyShieldRuleTableLoader.Load(package.Workbook.Worksheets["Type1"]));
        }

        private static ExcelPackage CreateValidWorkbook()
        {
            var package = new ExcelPackage();
            ExcelWorksheet sheet = package.Workbook.Worksheets.Add("Type1");
            for (int index = 0; index < EnemyShieldRuleTableLoader.HitColumns.Length; index++)
                sheet.Cells[1, index + 2].Value = EnemyShieldRuleTableLoader.HitColumns[index];
            for (int index = 0; index < EnemyShieldRuleTableLoader.ShieldRows.Length; index++)
                sheet.Cells[index + 2, 1].Value = EnemyShieldRuleTableLoader.ShieldRows[index];
            sheet.Cells[2, 3].Value = "2";
            return package;
        }
    }
}
