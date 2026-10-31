using System;
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
    }
}
