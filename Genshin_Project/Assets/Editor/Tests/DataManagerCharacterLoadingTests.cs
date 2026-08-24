using System.Reflection;
using NUnit.Framework;
using UnityEngine;

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
