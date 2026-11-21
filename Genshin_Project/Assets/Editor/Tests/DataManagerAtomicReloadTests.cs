using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using OfficeOpenXml;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public class DataManagerAtomicReloadTests
    {
        private GameObject testObject;
        private AtomicTestDataManager dataManager;

        [SetUp]
        public void SetUp()
        {
            testObject = new GameObject("DataManagerAtomicReloadTests");
            dataManager = testObject.AddComponent<AtomicTestDataManager>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(testObject);
        }

        [Test]
        public void SuccessfulReload_CommitsNewCollections()
        {
            var oldCollection = dataManager.CharacterOverviewList;
            var oldStatusIDs = dataManager.CharacterStatusIDs;
            oldStatusIDs[1009] = new HashSet<string> { "ST_OldCharacter" };
            EnemyShieldRuleTable oldShieldRules = dataManager.EnemyShieldRules;
            oldCollection.Add(new CharacterOverviewData { CharacterID = 1009, Name = "旧数据" });

            dataManager.RunAtomicForTest(() =>
            {
                dataManager.CharacterOverviewList.Add(new CharacterOverviewData { CharacterID = 1010, Name = "新数据" });
                dataManager.EnemyShieldRules.Add(CreateShieldRule("Hydro"));
                dataManager.CharacterStatusIDs[1010] = new HashSet<string> { "ST_NewCharacter" };
            });

            Assert.That(dataManager.IsLoaded, Is.True);
            Assert.That(dataManager.CharacterOverviewList, Is.Not.SameAs(oldCollection));
            Assert.That(dataManager.CharacterOverviewList.Count, Is.EqualTo(1));
            Assert.That(dataManager.CharacterOverviewList[0].CharacterID, Is.EqualTo(1010));
            Assert.That(dataManager.EnemyShieldRules, Is.Not.SameAs(oldShieldRules));
            Assert.That(dataManager.EnemyShieldRules.TryGet("Pyro", "Hydro", out _), Is.True);
            Assert.That(dataManager.CharacterStatusIDs, Is.Not.SameAs(oldStatusIDs));
            Assert.That(dataManager.CharacterStatusIDs.ContainsKey(1009), Is.False);
            Assert.That(dataManager.CharacterStatusIDs[1010], Does.Contain("ST_NewCharacter"));
        }

        [Test]
        public void FailedReload_RestoresPreviousCollections()
        {
            dataManager.RunAtomicForTest(() =>
            {
                dataManager.CharacterOverviewList.Add(new CharacterOverviewData { CharacterID = 1009, Name = "已提交数据" });
                dataManager.CharacterStatusIDs[1009] = new HashSet<string> { "ST_CommittedCharacter" };
            });
            var committedCollection = dataManager.CharacterOverviewList;
            var committedStatusIDs = dataManager.CharacterStatusIDs;
            var committedPhases = dataManager.EnemyPhaseDict;
            EnemyShieldRuleTable committedShieldRules = dataManager.EnemyShieldRules;

            Assert.Throws<DataLoadException>(() => dataManager.RunAtomicForTest(() =>
            {
                dataManager.CharacterOverviewList.Add(new CharacterOverviewData { CharacterID = 1010, Name = "未完成数据" });
                dataManager.EnemyShieldRules.Add(CreateShieldRule("Cryo"));
                dataManager.CharacterStatusIDs[1010] = new HashSet<string> { "ST_UncommittedCharacter" };
                dataManager.EnemyPhaseDict[2073] = new Dictionary<int, EnemyMainData> { [1] = new EnemyMainData { EnemyID = 2073, Phase = 1 } };
                throw new InvalidOperationException("模拟中途加载失败");
            }));

            Assert.That(dataManager.IsLoaded, Is.True);
            Assert.That(dataManager.CharacterOverviewList, Is.SameAs(committedCollection));
            Assert.That(dataManager.CharacterOverviewList.Count, Is.EqualTo(1));
            Assert.That(dataManager.CharacterOverviewList[0].CharacterID, Is.EqualTo(1009));
            Assert.That(dataManager.EnemyShieldRules, Is.SameAs(committedShieldRules));
            Assert.That(dataManager.EnemyShieldRules.TryGet("Pyro", "Cryo", out _), Is.False);
            Assert.That(dataManager.CharacterStatusIDs, Is.SameAs(committedStatusIDs));
            Assert.That(dataManager.CharacterStatusIDs.ContainsKey(1010), Is.False);
            Assert.That(dataManager.CharacterStatusIDs[1009], Does.Contain("ST_CommittedCharacter"));
            Assert.That(dataManager.EnemyPhaseDict, Is.SameAs(committedPhases));
            Assert.That(dataManager.EnemyPhaseDict.ContainsKey(2073), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EnemyPhase_BlankAndLegacyMissingColumnStayUnphased(bool withPhase)
        {
            using var package = new ExcelPackage();
            var sheet = EnemyCoefficients(package, withPhase);
            CoefficientRow(sheet, 2, null, 2, 3);
            InvokeEnemyLoader("LoadEnemyCoefficientSheet", sheet);
            Assert.That(dataManager.EnemyMainDict[2073].Phase, Is.Zero);
            Assert.That(dataManager.GetEnemyPhaseCount(2073), Is.Zero);
            Assert.That(dataManager.TryGetEnemyPhaseData(2073, 1, out _), Is.False);
        }

        [Test]
        public void EnemyPhase_MovedHeadersAndReverseRowsKeepDistinctCoefficientsAndResistances()
        {
            using var package = new ExcelPackage();
            var coeff = EnemyCoefficients(package, true);
            CoefficientRow(coeff, 2, 2, 6, 14);
            CoefficientRow(coeff, 3, 1, 6, 10);
            InvokeEnemyLoader("LoadEnemyCoefficientSheet", coeff);
            var main = EnemyAttributes(package);
            AttributeRow(main, 2, 2, 0, .5);
            AttributeRow(main, 3, 1, .5, 0);
            InvokeEnemyLoader("LoadEnemyAttributeSheet", main);
            Assert.That(dataManager.EnemyMainDict[2073].Phase, Is.EqualTo(1));
            Assert.That(dataManager.GetEnemyPhaseCount(2073), Is.EqualTo(2));
            Assert.That(dataManager.TryGetEnemyPhaseData(2073, 2, out var second), Is.True);
            Assert.That(second.ATK_coeff, Is.EqualTo(14));
            Assert.That(second.ElectroRes, Is.EqualTo(.5));
            Assert.That(second.HydroRes, Is.Zero);
            Assert.That(dataManager.EnemyMainDict[2073].ATK_coeff, Is.EqualTo(10));
            Assert.That(dataManager.EnemyMainDict[2073].HydroRes, Is.EqualTo(.5));
            Assert.That(dataManager.TryGetEnemyPhaseData(2073, 3, out _), Is.False);
        }

        [TestCase(-1)]
        [TestCase(.5)]
        [TestCase("bad")]
        public void EnemyPhase_RejectsInvalidPhaseRatherThanTruncating(object phase)
        {
            using var package = new ExcelPackage();
            var sheet = EnemyCoefficients(package, true);
            CoefficientRow(sheet, 2, phase, 6, 10);
            Assert.Throws<TableValidationException>(() => InvokeEnemyLoader("LoadEnemyCoefficientSheet", sheet));
        }

        [TestCase(null, 1)]
        [TestCase(1, 1)]
        [TestCase(1, 3)]
        public void EnemyPhase_RejectsMixedDuplicateAndMissingStages(object first, object second)
        {
            using var package = new ExcelPackage();
            var sheet = EnemyCoefficients(package, true);
            CoefficientRow(sheet, 2, first, 6, 10);
            CoefficientRow(sheet, 3, second, 6, 14);
            Assert.Throws<TableValidationException>(() => InvokeEnemyLoader("LoadEnemyCoefficientSheet", sheet));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EnemyPhase_MainRequiresExactlyTheConfiguredStages(bool duplicate)
        {
            using var package = new ExcelPackage();
            var coeff = EnemyCoefficients(package, true);
            CoefficientRow(coeff, 2, 1, 6, 10);
            CoefficientRow(coeff, 3, 2, 6, 14);
            InvokeEnemyLoader("LoadEnemyCoefficientSheet", coeff);
            var main = EnemyAttributes(package);
            AttributeRow(main, 2, 1, .5, 0);
            if (duplicate) AttributeRow(main, 3, 1, .5, 0);
            Assert.Throws<TableValidationException>(() => InvokeEnemyLoader("LoadEnemyAttributeSheet", main));
        }

        private void InvokeEnemyLoader(string name, ExcelWorksheet sheet)
        {
            try { typeof(DataManager).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dataManager, new object[] { sheet }); }
            catch (TargetInvocationException error) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); }
        }

        private static ExcelWorksheet EnemyCoefficients(ExcelPackage package, bool withPhase)
        {
            var sheet = package.Workbook.Worksheets.Add("Coeff");
            string[] columns = { "备注", "中文名", "ATK_Curve", "ATK_coeff", "HP_coeff", "EnemyID", "English Name", withPhase ? " phase " : "其他" };
            for (int i = 0; i < columns.Length; i++) sheet.Cells[1, i + 1].Value = columns[i];
            return sheet;
        }

        private static void CoefficientRow(ExcelWorksheet sheet, int row, object phase, double hp, double atk)
        {
            sheet.Cells[row, 2].Value = "公子"; sheet.Cells[row, 3].Value = 1;
            sheet.Cells[row, 4].Value = atk; sheet.Cells[row, 5].Value = hp;
            sheet.Cells[row, 6].Value = 2073; sheet.Cells[row, 7].Value = "Childe";
            sheet.Cells[row, 8].Value = phase;
        }

        private static ExcelWorksheet EnemyAttributes(ExcelPackage package)
        {
            var sheet = package.Workbook.Worksheets.Add("Enemy_Main");
            string[] columns = { "Phase", "EnemyID", "EnemyName", "EnemyNameID", "Threat", "ActsGiven", "PhysicalRes", "PyroRes", "HydroRes", "ElectroRes", "CryoRes", "AnemoRes", "DendroRes", "GeoRes", "PhysicalDmgBonus", "PyroDmgBonus", "HydroDmgBonus", "ElectroDmgBonus", "CryoDmgBonus", "AnemoDmgBonus", "DendroDmgBonus", "GeoDmgBonus" };
            for (int i = 0; i < columns.Length; i++) sheet.Cells[1, i + 1].Value = columns[i];
            return sheet;
        }

        private static void AttributeRow(ExcelWorksheet sheet, int row, object phase, double hydro, double electro)
        {
            sheet.Cells[row, 1].Value = phase; sheet.Cells[row, 2].Value = 2073;
            sheet.Cells[row, 3].Value = "公子"; sheet.Cells[row, 4].Value = "Childe";
            sheet.Cells[row, 9].Value = hydro; sheet.Cells[row, 10].Value = electro;
        }

        [Test]
        public void ValidationFailure_PreservesWorkbookRowAndEffectIdentity()
        {
            const string source = "Characters/1012_Venti.xlsx/SkillsEffect (SkillEffectID=10120304, ID2=SE_Skill_Venti4)";
            var failure = Assert.Throws<DataLoadException>(() => dataManager.RunAtomicForTest(() =>
            {
                dataManager.CollectValidationForTest(source, () => TableValidator.ValidateEffectRow(
                    "SkillsEffect", 9, "ApplyStatus", "", 2, "", "", 0, 0, "", false));
                // 和ReloadAllData一样，在收集完错误后终止提交，不以错误数据继续战斗。
                throw new InvalidOperationException("模拟统一报告校验失败");
            }));
            Assert.That(failure.Errors.Count, Is.EqualTo(2));
            Assert.That(failure.Errors[0], Does.Contain("Characters/1012_Venti.xlsx/SkillsEffect"));
            Assert.That(failure.Errors[0], Does.Contain("SkillEffectID=10120304"));
            Assert.That(failure.Errors[0], Does.Contain("ID2=SE_Skill_Venti4"));
            Assert.That(failure.Errors[0], Does.Contain("第9行"));
            Assert.That(failure.Errors[0], Does.Contain("TargetType"));
            Assert.That(dataManager.IsLoaded, Is.False);
        }

        [Test]
        public void RealProjectData_LoadsAsOneCompleteSnapshot()
        {
            Assert.DoesNotThrow(() => dataManager.ReloadAllData());

            Assert.That(dataManager.IsLoaded, Is.True);
            Assert.That(dataManager.CharacterAttributesDict.ContainsKey(1009), Is.True, "缺少安柏角色属性");
            Assert.That(dataManager.EnemyMainDict, Is.Not.Empty, "敌人主表没有加载出数据");
            Assert.That(dataManager.StatusMainDict, Is.Not.Empty, "状态主表没有加载出数据");
            Assert.That(dataManager.ReactionLevelCoefficientDict, Is.Not.Empty, "反应等级系数没有加载出数据");
            Assert.That(dataManager.EnemyShieldRules.Count, Is.GreaterThan(0), "敌方元素盾规则没有加载出数据");
        }

        private static EnemyShieldRule CreateShieldRule(string hitKind)
        {
            return EnemyShieldRuleParser.Parse("1U", "Pyro", hitKind);
        }
    }

    internal sealed class AtomicTestDataManager : DataManager
    {
        protected override void Awake()
        {
            // EditMode 测试不注册全局单例，也不自动读取真实配表。
        }

        internal void RunAtomicForTest(Action loadAction)
        {
            LoadDataAtomically(loadAction);
        }

        internal void CollectValidationForTest(string source, Action validate)
        {
            typeof(DataManager).GetMethod("TryValidate", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(this, new object[] { source, validate });
        }
    }
}
