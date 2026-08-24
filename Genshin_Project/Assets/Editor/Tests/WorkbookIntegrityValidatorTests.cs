using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OfficeOpenXml;

namespace GenshinTurnBased.Tests.EditMode
{
    public class WorkbookIntegrityValidatorTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "genshin-workbook-validator-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_root) && Directory.Exists(_root))
                Directory.Delete(_root, true);
        }

        [Test]
        public void Validate_FindsDuplicateEnumReferenceAndMissingRelation()
        {
            CreateWorkbook("EnemyAttributes.xlsx", package =>
            {
                AddSheet(package, "Enemy_Main",
                    new[] { "EnemyID", "EnemyName" },
                    new[] { "1001", "Slime A" },
                    new[] { "1001", "Slime B" });
            });
            CreateWorkbook("EnemySkill.xlsx", package =>
            {
                AddSheet(package, "EnemySkill_Main",
                    new[] { "EnemySkillID", "EnemySkillName" },
                    new[] { "2001", "Attack" });
                AddSheet(package, "EnemySkill_Effect",
                    new[] { "SkillEffectID", "SkillEffectID2", "EffectType", "Element" },
                    new[] { "999901", "BadEffect", "Damage", "Fire" });
                AddSheet(package, "EnemySkill_Param",
                    new[] { "SkillEffectID2", "Param1" },
                    new[] { "MissingEffect", "1" });
                AddSheet(package, "EnemyAI",
                    new[] { "EnemyID", "PatternID", "PatternType" },
                    new[] { "1001", "Default", "Random" });
                AddSheet(package, "EnemyAI_Rule",
                    new[] { "EnemyID", "PatternID", "SkillID", "Weight" },
                    new[] { "1001", "MissingPattern", "404", "1" });
            });

            IReadOnlyList<WorkbookValidationIssue> issues = WorkbookIntegrityValidator.Validate(_root);
            string[] codes = issues.Select(issue => issue.Code).ToArray();

            CollectionAssert.Contains(codes, "DUPLICATE_ID");
            CollectionAssert.Contains(codes, "INVALID_ENUM");
            CollectionAssert.Contains(codes, "INVALID_REFERENCE");
            CollectionAssert.Contains(codes, "MISSING_RELATED_DATA");
        }

        [Test]
        public void Validate_SkipsSchemaTypeRows()
        {
            string characterDirectory = Path.Combine(_root, "Characters");
            Directory.CreateDirectory(characterDirectory);
            using (var package = new ExcelPackage())
            {
                AddSheet(package, "Skills",
                    new[] { "SkillID", "SkillID2", "ActionType" },
                    new[] { "int", "string", "string" },
                    new[] { "3001", "NormalAttack", "Normal" });
                package.SaveAs(new FileInfo(Path.Combine(characterDirectory, "1001_Test.xlsx")));
            }

            IReadOnlyList<WorkbookValidationIssue> issues = WorkbookIntegrityValidator.Validate(_root);

            Assert.That(issues.Any(issue => issue.Code == "INVALID_ENUM" && issue.Location.EndsWith("row 2")), Is.False);
            Assert.That(issues.Any(issue => issue.Code == "DUPLICATE_ID" && issue.Location.EndsWith("row 2")), Is.False);
        }

        private void CreateWorkbook(string relativePath, Action<ExcelPackage> populate)
        {
            string path = Path.Combine(_root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? _root);
            using (var package = new ExcelPackage())
            {
                populate(package);
                package.SaveAs(new FileInfo(path));
            }
        }

        private static void AddSheet(ExcelPackage package, string name, string[] headers, params string[][] rows)
        {
            ExcelWorksheet sheet = package.Workbook.Worksheets.Add(name);
            for (int column = 0; column < headers.Length; column++)
                sheet.Cells[1, column + 1].Value = headers[column];
            for (int row = 0; row < rows.Length; row++)
            {
                for (int column = 0; column < rows[row].Length; column++)
                    sheet.Cells[row + 2, column + 1].Value = rows[row][column];
            }
        }
    }
}
