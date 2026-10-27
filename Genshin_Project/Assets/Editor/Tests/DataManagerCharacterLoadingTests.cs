using System.Reflection;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using OfficeOpenXml;

namespace GenshinTurnBased.Tests.EditMode
{
    public class DataManagerCharacterLoadingTests
    {
        private GameObject testObject;
        private TestableDataManager dataManager;

        [SetUp]
        public void SetUp()
        {
            testObject = new GameObject("DataManagerCharacterLoadingTests");
            dataManager = testObject.AddComponent<TestableDataManager>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(testObject);
        }

        [Test]
        public void KaeyaSkillLevel_InheritsGroupColumnsAndKeepsBothBurstParamGroups()
        {
            dataManager.LoadCharacterSheetsForTest();

            var rows = dataManager.SkillLevelDict.Values
                .SelectMany(levels => levels.Values)
                .Where(row => row.CharacterID == 1010)
                .ToList();

            var normal = rows.Where(row => row.ParamID == "SE_Normal_Kaeya").ToList();
            var burst = rows.Where(row => row.ParamID == "SE_Kaeya_BurstTrigger").ToList();
            var burstC6 = rows.Where(row => row.ParamID == "SE_Kaeya_BurstTrigger_C6").ToList();

            Assert.That(normal.Count, Is.EqualTo(15));
            Assert.That(normal.All(row => row.SkillType == 0), Is.True);
            Assert.That(normal.Single(row => row.SkillLevel == 2).Hits1, Is.EqualTo("0.954*TotalATK,1"));
            Assert.That(burst.Count, Is.EqualTo(15));
            Assert.That(burst.All(row => row.SkillType == 3), Is.True);
            Assert.That(burstC6.Count, Is.EqualTo(15));
            Assert.That(burstC6.All(row => row.SkillType == 3), Is.True);
        }

        [Test]
        public void AmberAscension_UsesCharacterIdFromCharacterFile()
        {
            dataManager.LoadCharacterSheetsForTest();

            Assert.That(
                dataManager.CharacterAscensionDict.ContainsKey(1009),
                Is.True,
                "1009_Amber.xlsx 的 Ascension 数据应归入角色 1009，而不是把 AscensionLevel 当作角色 ID。"
            );

            CharacterAscensionData level20 = dataManager.CharacterAscensionDict[1009]
                .Find(data => data.AscensionLevel == 20);

            Assert.That(level20, Is.Not.Null, "安柏应存在 20 级突破数据。");
            Assert.That(level20.BaseHPFlat, Is.EqualTo(592f));
            Assert.That(level20.BaseATKFlat, Is.EqualTo(14f));
            Assert.That(level20.BaseDEFFlat, Is.EqualTo(38f));
        }

        [TestCase("火", "Pyro")]
        [TestCase("水", "Hydro")]
        [TestCase("雷", "Electro")]
        [TestCase("冰", "Cryo")]
        [TestCase("风", "Anemo")]
        [TestCase("草", "Dendro")]
        [TestCase("岩", "Geo")]
        [TestCase("无元素", "None")]
        public void ChineseElement_MapsToExistingCombatToken(string authored, string expected)
            => Assert.That(CharacterMetadataParser.ParseElement(authored), Is.EqualTo(expected));

        [TestCase("单手剑", 0)]
        [TestCase("双手剑", 1)]
        [TestCase("弓箭", 2)]
        [TestCase("长枪", 3)]
        [TestCase("法器", 4)]
        public void ChineseWeaponType_MapsToExistingWeaponNumber(string authored, int expected)
            => Assert.That(CharacterMetadataParser.ParseWeaponType(authored), Is.EqualTo(expected));

        [TestCase(false)]
        [TestCase(true)]
        public void OverviewMetadata_ReadsMovedHeadersAndChineseValues(bool chineseHeaders)
        {
            using var package = new ExcelPackage();
            var sheet = package.Workbook.Worksheets.Add("CharacterMetadata");
            sheet.Cells[1, 1].Value = chineseHeaders ? "武器类型" : "WeaponType";
            sheet.Cells[1, 2].Value = "Name";
            sheet.Cells[1, 3].Value = chineseHeaders ? "国家" : "Nation";
            sheet.Cells[1, 4].Value = "CharacterID";
            sheet.Cells[1, 5].Value = chineseHeaders ? "元素" : "Element";
            sheet.Cells[1, 6].Value = "NameID";
            sheet.Cells[2, 1].Value = "单手剑";
            sheet.Cells[2, 2].Value = "丝柯克";
            sheet.Cells[2, 3].Value = "无归属";
            sheet.Cells[2, 4].Value = 1106;
            sheet.Cells[2, 5].Value = "冰";
            sheet.Cells[2, 6].Value = "Skirk";
            var method = typeof(DataManager).GetMethod("ReadCharacterOverviewRow", BindingFlags.Static | BindingFlags.NonPublic);
            var result = (CharacterOverviewData)method.Invoke(null, new object[] { sheet, 2 });
            Assert.That(result.CharacterID, Is.EqualTo(1106));
            Assert.That(result.NameID, Is.EqualTo("Skirk"));
            Assert.That(result.Nation, Is.EqualTo("无归属"));
            Assert.That(result.Element, Is.EqualTo("Cryo"));
            Assert.That(result.WeaponType, Is.Zero);
        }

        [Test]
        public void MissingMetadata_IsNotInventedAsUnaffiliatedOrSword()
        {
            Assert.That(CharacterMetadataParser.ParseNation(""), Is.Empty);
            Assert.That(CharacterMetadataParser.ParseNation("无归属"), Is.EqualTo("无归属"));
            Assert.That(CharacterMetadataParser.ParseElement(""), Is.Empty);
            Assert.That(CharacterMetadataParser.ParseWeaponType(""), Is.EqualTo(-1));
            Assert.Throws<TableValidationException>(() => CharacterMetadataParser.ParseNation("未知国籍"));
            Assert.Throws<TableValidationException>(() => CharacterMetadataParser.ParseElement("未知元素"));
            Assert.Throws<TableValidationException>(() => CharacterMetadataParser.ParseWeaponType("未知武器"));
        }

        [Test]
        public void NationQuery_DeduplicatesRosterIDsAndRequiresFormalMetadata()
        {
            dataManager.CharacterOverviewList.Add(new CharacterOverviewData { CharacterID = 1022, Nation = "璃月" });
            dataManager.CharacterOverviewList.Add(new CharacterOverviewData { CharacterID = 1072, Nation = "璃月" });
            dataManager.CharacterOverviewList.Add(new CharacterOverviewData { CharacterID = 1106, Nation = "无归属" });
            Assert.That(dataManager.CountCharactersFromNation(new[] { 1022, 1072, 1022, 1106 }, "璃月"), Is.EqualTo(2));
            Assert.That(dataManager.CountCharactersFromNation(new[] { 1106 }, "璃月"), Is.Zero);
            Assert.Throws<System.InvalidOperationException>(() => dataManager.CountCharactersFromNation(new[] { 9999 }, "璃月"));
        }

        [Test]
        public void OverviewIdentity_AppliesToExistingCharacterAttributesWithoutRewritingSkills()
        {
            dataManager.CharacterOverviewList.Add(new CharacterOverviewData
                { CharacterID = 1084, Nation = "璃月", Element = "Anemo", WeaponType = 4 });
            var attributes = new CharacterAttributesData { CharacterID = 1084, Element = "Pyro", WeaponType = 0, BaseHP = 100f };
            var method = typeof(DataManager).GetMethod("ApplyCharacterOverviewIdentity", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Invoke(dataManager, new object[] { attributes });
            Assert.That(attributes.Nation, Is.EqualTo("璃月"));
            Assert.That(attributes.Element, Is.EqualTo("Anemo"));
            Assert.That(attributes.WeaponType, Is.EqualTo(4));
            Assert.That(attributes.BaseHP, Is.EqualTo(100f));
        }

    }

    internal sealed class TestableDataManager : DataManager
    {
        protected override void Awake()
        {
            // EditMode 测试只加载角色表，不注册全局单例，也不自动加载其他配表。
        }

        public void LoadCharacterSheetsForTest()
        {
            MethodInfo loadMethod = typeof(DataManager).GetMethod(
                "LoadCharacterSheets",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

            Assert.That(loadMethod, Is.Not.Null, "找不到 DataManager.LoadCharacterSheets。读取入口可能已改名，请同步更新测试。");
            loadMethod.Invoke(this, null);
        }
    }
}
