using System;
using System.Collections.Generic;
using System.Reflection;
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
            EnemyShieldRuleTable committedShieldRules = dataManager.EnemyShieldRules;

            Assert.Throws<DataLoadException>(() => dataManager.RunAtomicForTest(() =>
            {
                dataManager.CharacterOverviewList.Add(new CharacterOverviewData { CharacterID = 1010, Name = "未完成数据" });
                dataManager.EnemyShieldRules.Add(CreateShieldRule("Cryo"));
                dataManager.CharacterStatusIDs[1010] = new HashSet<string> { "ST_UncommittedCharacter" };
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
