using System;
using NUnit.Framework;
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
            oldCollection.Add(new CharacterOverviewData { CharacterID = 1009, Name = "旧数据" });

            dataManager.RunAtomicForTest(() =>
            {
                dataManager.CharacterOverviewList.Add(new CharacterOverviewData { CharacterID = 1010, Name = "新数据" });
            });

            Assert.That(dataManager.IsLoaded, Is.True);
            Assert.That(dataManager.CharacterOverviewList, Is.Not.SameAs(oldCollection));
            Assert.That(dataManager.CharacterOverviewList.Count, Is.EqualTo(1));
            Assert.That(dataManager.CharacterOverviewList[0].CharacterID, Is.EqualTo(1010));
        }

        [Test]
        public void FailedReload_RestoresPreviousCollections()
        {
            dataManager.RunAtomicForTest(() =>
            {
                dataManager.CharacterOverviewList.Add(new CharacterOverviewData { CharacterID = 1009, Name = "已提交数据" });
            });
            var committedCollection = dataManager.CharacterOverviewList;

            Assert.Throws<DataLoadException>(() => dataManager.RunAtomicForTest(() =>
            {
                dataManager.CharacterOverviewList.Add(new CharacterOverviewData { CharacterID = 1010, Name = "未完成数据" });
                throw new InvalidOperationException("模拟中途加载失败");
            }));

            Assert.That(dataManager.IsLoaded, Is.True);
            Assert.That(dataManager.CharacterOverviewList, Is.SameAs(committedCollection));
            Assert.That(dataManager.CharacterOverviewList.Count, Is.EqualTo(1));
            Assert.That(dataManager.CharacterOverviewList[0].CharacterID, Is.EqualTo(1009));
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
    }
}
