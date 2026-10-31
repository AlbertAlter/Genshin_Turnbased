using System.IO;
using System.Reflection;
using NUnit.Framework;
using OfficeOpenXml;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class LangTableTests
    {
        private static void Load(DataManager data, ExcelWorksheet sheet)
            => typeof(DataManager).GetMethod("LoadLangSheet", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(data, new object[] { sheet });

        [Test]
        public void RuntimeLang_ProvidesBothTranslationsAndStableTipKey()
        {
            using var env = new BattleFlowTestEnv();
            using var book = new ExcelPackage(new FileInfo(Path.Combine(Application.streamingAssetsPath, "Data", "lang.xlsx")));
            Load(env.DataManager, book.Workbook.Worksheets["lang"]);
            Assert.That(env.DataManager.GetLangText(BattleTipKeys.NoValidTarget), Is.EqualTo("无合法目标，无法释放"));
            Assert.That(env.DataManager.GetLangText(BattleTipKeys.NoValidTarget, true), Is.EqualTo("No valid target. Cannot use this skill."));
            Assert.That(env.DataManager.GetLangText(BattleTipKeys.NoActiveCharacter), Is.EqualTo("当前没有出战角色"));
            Assert.That(env.DataManager.GetLangText(BattleTipKeys.NoActiveCharacter, true), Is.EqualTo("No active character."));
            Assert.That(env.DataManager.GetLangText("tip_unknown"), Is.EqualTo("tip_unknown"));
        }

        [Test]
        public void DuplicateKey_IsRejectedInsteadOfSilentlyOverwritingTranslation()
        {
            using var env = new BattleFlowTestEnv(); using var book = new ExcelPackage();
            var sheet = book.Workbook.Worksheets.Add("lang");
            sheet.Cells[1, 1].Value = "key"; sheet.Cells[1, 2].Value = "chinese"; sheet.Cells[1, 3].Value = "english";
            for (int row = 2; row <= 3; row++)
            {
                sheet.Cells[row, 1].Value = "tip_test";
                sheet.Cells[row, 2].Value = "测试"; sheet.Cells[row, 3].Value = "Test";
            }
            var exception = Assert.Throws<TargetInvocationException>(() => Load(env.DataManager, sheet));
            Assert.That(exception.InnerException, Is.TypeOf<TableValidationException>());
        }
    }
}
