using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class LangTextFormatterTests
    {
        private static string Color(int id) => id == 0 ? "#FFD780" : "#123456";

        [Test]
        public void InlineMarkers_ParametersAndLineBreaksWorkTogether()
        {
            const string template = "造成#1火元素伤害#\\n倍率#0{0}倍#，持续{1}回合。";
            Assert.That(LangTextFormatter.Format(template, new object[] { 1.5, 2 }),
                Is.EqualTo("造成火元素伤害\n倍率1.5倍，持续2回合。"));
            Assert.That(LangTextFormatter.Format(template, new object[] { 1.5, 2 }, true, Color),
                Is.EqualTo("造成<color=#123456>火元素伤害</color>\n倍率<color=#FFD780>1.5倍</color>，持续2回合。"));
        }

        [Test]
        public void ReorderedAndRepeatedArguments_DoNotRequireWholeTextMatching()
        {
            Assert.That(LangTextFormatter.Format("{1} / {0} / {1}", new object[] { "A", "B" }), Is.EqualTo("B / A / B"));
            Assert.That(LangTextFormatter.Format("#0150%##1火元素#", richText: true, color: Color),
                Is.EqualTo("<color=#FFD780>150%</color><color=#123456>火元素</color>"));
        }

        [Test]
        public void Escapes_AndRealNewlinesRemainLiteral()
        {
            Assert.That(LangTextFormatter.Format(@"\#1火\# {{0}} \\n" + "\n下一行"),
                Is.EqualTo("#1火# {0} \\n\n下一行"));
        }

        [Test]
        public void ArgumentText_IsNotParsedAgainOrAllowedToInjectRichText()
        {
            const string value = "#1{2}#\\n</color><b>";
            Assert.That(LangTextFormatter.Format("#0{0}#", new object[] { value }), Is.EqualTo(value));
            Assert.That(LangTextFormatter.Format("#0{0}#", new object[] { value }, true, Color),
                Is.EqualTo("<color=#FFD780>#1{2}#\\n<noparse><</noparse>/color><noparse><</noparse>b></color>"));
        }

        [TestCase("#1未结束")]
        [TestCase("#9未知颜色#")]
        [TestCase("{1}")]
        [TestCase("{name}")]
        [TestCase("}")]
        public void InvalidTemplate_ReportsItsKey(string template)
        {
            var error = Assert.Throws<FormatException>(() => LangTextFormatter.Format(template,
                new object[] { 1 }, context: "tutorial_test"));
            Assert.That(error.Message, Does.Contain("tutorial_test"));
        }

        [Test]
        public void InvalidRichColor_IsRejectedAndPlainTextDoesNotNeedPalette()
        {
            Assert.Throws<FormatException>(() => LangTextFormatter.Format("#1火#", richText: true, color: _ => "red"));
            Assert.That(LangTextFormatter.Format("#1火#"), Is.EqualTo("火"));
        }

        [Test]
        public void ColorIds_AreStableAndDoNotInspectWords()
        {
            string[] elements = { null, "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Geo", "Dendro", "Physical" };
            for (int id = 0; id < elements.Length; id++)
                Assert.That(LangTextFormatter.GetColorElement(id), Is.EqualTo(elements[id]));
            Assert.That(LangTextFormatter.Format("#1任意内容#", richText: true, color: Color),
                Is.EqualTo("<color=#123456>任意内容</color>"));
        }

        [Test]
        public void DescriptionLink_PreservesColorAndParametersAndUnderlinesOnlyItsLabel()
        {
            const string template = "获得[text:status_test_desc|#0印记{0}#]。";
            Assert.That(LangTextFormatter.Format(template, new object[] { 2 }), Is.EqualTo("获得印记2。"));
            Assert.That(LangTextFormatter.Format(template, new object[] { 2 }, true, Color),
                Is.EqualTo("获得<link=\"text:status_test_desc\"><u><color=#FFD780>印记2</color></u></link>。"));
            Assert.That(LangTextFormatter.Format(@"[text:status_test_desc|符号\]与\|]"), Is.EqualTo("符号]与|"));
        }

        [TestCase("[text:status_test_desc|]")]
        [TestCase("[text:status_test_desc|说明")]
        [TestCase("[text:bad\"key|说明]")]
        [TestCase("[text:outer|[text:inner|说明]]")]
        public void InvalidDescriptionLink_ReportsItsContext(string template)
        {
            var error = Assert.Throws<FormatException>(() => LangTextFormatter.Format(template, context: "skill_test"));
            Assert.That(error.Message, Does.Contain("skill_test"));
        }

        [Test]
        public void SkillDescription_UsesActualStageKeyAndLeavesOriginalReadingTextUnchanged()
        {
            using var env = new BattleFlowTestEnv();
            env.DataManager.LangDict.Add("skill_101505_desc", new LangEntry
            { Chinese = "二段[text:status_test_desc|印记]", English = "Second strike" });
            const string original = "原技能表完整说明";
            Assert.That(env.DataManager.GetSkillDescriptionText(101505, original), Is.EqualTo("二段印记"));
            Assert.That(env.DataManager.GetSkillDescriptionText(101505, original, english: true), Is.EqualTo("Second strike"));
            Assert.That(env.DataManager.GetSkillDescriptionText(101503, original), Is.EqualTo(original));
            Assert.That(env.DataManager.GetRawLangText("skill_101505_desc"), Is.EqualTo("二段[text:status_test_desc|印记]"));
        }

        [Test]
        public void DataManager_UsesSelectedLanguageWithoutChangingStoredTemplate()
        {
            using var env = new BattleFlowTestEnv();
            env.DataManager.LangDict.Add("format_test", new LangEntry
            { Chinese = "伤害#0{0}#\\n{1}", English = "{1}: #0{0}#" });
            Assert.That(env.DataManager.GetLangText("format_test", false, 12, "A"), Is.EqualTo("伤害12\nA"));
            Assert.That(env.DataManager.GetLangRichText("format_test", true, 12, "A"),
                Is.EqualTo("A: <color=#FFD780>12</color>"));
            Assert.That(env.DataManager.GetRawLangText("format_test"), Is.EqualTo("伤害#0{0}#\\n{1}"));
            Assert.That(env.DataManager.FormatLangText("任意字段#0{0}#", false, 3), Is.EqualTo("任意字段3"));
            Assert.That(env.DataManager.GetLangText("tip_unknown"), Is.EqualTo("tip_unknown"));
        }

        [Test]
        public void InfoAdditions_KeepBaseFirstSortActiveStablyAndDimWholeInactiveEntry()
        {
            using var env = new BattleFlowTestEnv();
            var data = env.DataManager;
            var entry = new LangEntry { Chinese = "基础", English = "Base" };
            data.LangDict.Add("skill_test_desc", entry);
            string[] keys = { "inactive", "active1", "active2", "other_character" };
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                // 激活项只用通用强调色；未激活项的元素色由灰色覆盖，不读取外部调色表。
                data.LangDict.Add(key, new LangEntry { Chinese = key + (i == 0 ? "#1" : "#0") + "火元素#[text:status_test_desc|#0状态#]", English = key });
                entry.Additions.Add(new LangInfoAddition
                { TextKey = key, CharacterID = i == 3 ? 1011 : 1010, UnlockType = "Constellation", UnlockIndex = i });
            }
            var calls = new List<string>();
            string actual = data.GetInfoDescriptionText("skill_test_desc", null, 1010, a =>
            { calls.Add(a.TextKey); return a.TextKey.StartsWith("active", StringComparison.Ordinal); });
            Assert.That(calls, Is.EqualTo(new[] { "inactive", "active1", "active2" }));
            Assert.That(actual, Does.StartWith("基础\n\nactive1"));
            Assert.That(actual.IndexOf("active2", StringComparison.Ordinal), Is.GreaterThan(actual.IndexOf("active1", StringComparison.Ordinal)));
            Assert.That(actual, Does.Contain("\n\n<color=#808080>inactive<color=#808080>火元素</color>"));
            Assert.That(actual, Does.Contain("<link=\"text:status_test_desc\"><u><color=#808080>状态</color></u></link>"));
            Assert.That(actual, Does.Not.Contain("other_character"));
            Assert.That(data.GetInfoDescriptionText("skill_test_desc", null, 1010, a => true, english: true), Does.StartWith("Base\n\ninactive"));
        }

        [Test]
        public void StateDescriptions_KeepGenericLinkSupportAndFollowConfiguredText()
        {
            using var env = new BattleFlowTestEnv();
            var data = env.DataManager;
            var entry = new LangEntry { Chinese = "涉及#8其他状态#", English = "Other state" };
            data.LangDict.Add("status_test_desc", entry);
            Assert.That(data.GetInfoDescriptionText("status_test_desc", null, 1010, a => false, richText: false), Is.EqualTo("涉及其他状态"));
            entry.Additions.Add(new LangInfoAddition { TextKey = "state_addition", CharacterID = 1010 });
            data.LangDict.Add("state_addition", new LangEntry { Chinese = "[text:another_desc|另一状态]", English = "Other" });
            entry.Chinese = "[text:another_desc|另一状态]";
            Assert.That(data.GetInfoDescriptionText("status_test_desc", null, 1010, a => true),
                Is.EqualTo("<link=\"text:another_desc\"><u>另一状态</u></link>\n\n<link=\"text:another_desc\"><u>另一状态</u></link>"));
            Assert.That(data.GetInfoDescriptionText("status_test_desc", null, 1010, a => false, richText: false),
                Is.EqualTo("另一状态\n\n另一状态"));
        }
    }
}
