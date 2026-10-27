using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using OfficeOpenXml;

namespace GenshinTurnBased.Tests.EditMode
{
    public class EffectTableParsingTests
    {
        [Test]
        public void FlatEnergy_ElementIsOptionalAcrossEffectTables(
            [Values("SkillsEffect", "Status_Effect", "EnemySkill_Effect")] string sheet,
            [Values(null, "", "None", "none", "Electro")] string element)
        {
            Assert.DoesNotThrow(() => TableValidator.ValidateEffectRow(sheet, 3,
                "GainEnergy", element, 0, "Flat", "Self", 0, 0, "",
                sheet == "EnemySkill_Effect", sheet != "Status_Effect"));
        }

        [TestCase("Damage", "", "")]
        [TestCase("GainEnergy", "", "Based")]
        [TestCase("GainEnergy", null, "Based")]
        [TestCase("GainEnergy", "None", "")]
        [TestCase("GainEnergy", "None", "Unknown")]
        public void OptionalFlatElement_DoesNotRelaxDamageBasedOrModeValidation(
            string effectType, string element, string mode)
        {
            Assert.Throws<TableValidationException>(() => TableValidator.ValidateEffectRow(
                "SkillsEffect", 3, effectType, element, 0, mode, "Self", 0, 0, "", false));
        }

        [TestCase("SkillsEffect", false, true)]
        [TestCase("Status_Effect", false, false)]
        [TestCase("EnemySkill_Effect", true, true)]
        public void ApplyStatus_ZeroDurationIsAcceptedAsPermanent(string sheet, bool enemy, bool checkNumber)
        {
            Assert.DoesNotThrow(() => TableValidator.ValidateEffectRow(sheet, 3,
                "ApplyStatus", "", 0, "", "Enemy", -1, 3, "", enemy, checkNumber));
        }

        [TestCase(null, 1)]
        [TestCase("", 1)]
        [TestCase("3", 3)]
        public void ExecuteCount_DefaultsOnlyWhenOmitted(string raw, int expected)
            => Assert.That(TableValidator.ParseExecuteCount(raw), Is.EqualTo(expected));

        [Test]
        public void ExecuteCount_PreservesSupportedRuntimeHandler()
        {
            Assert.That(TableValidator.ParseExecuteCount("1011_C1()"), Is.EqualTo(1));
            Assert.That(TableValidator.ParseExecuteCountHandler("1011_C1()"), Is.EqualTo("1011_C1()"));
            Assert.That(TableValidator.ParseExecuteCountHandler("3"), Is.Empty);
            Assert.That(ExecuteCountEvaluator.Evaluate(1, "1011_C1()", null), Is.Zero);
        }

        [TestCase(0, 0)]
        [TestCase(3, 3)]
        [TestCase(6, 5)]
        public void LisaC1_CountsAtMostFiveDistinctHitTargets(int hitTargetCount, int expected)
            => Assert.That(LisaScriptHooks.CapC1Count(hitTargetCount), Is.EqualTo(expected));

        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("1.5")]
        [TestCase("All")]
        [TestCase("Unknown()")]
        [TestCase("1011_C1(2)")]
        public void ExecuteCount_RejectsInvalidValues(string raw)
            => Assert.Throws<TableValidationException>(() => TableValidator.ParseExecuteCount(raw));

        [TestCase("", "0.8*TotalDEF,2", "0.8*TotalDEF,2")]
        [TestCase("0.9", "0.8*TotalDEF,2", "0.9*TotalDEF,2")]
        [TestCase("0,0", "0.8*TotalDEF,2", "0*TotalDEF,0")]
        [TestCase("1.1,0", "0.8*TotalATK,2", "1.1*TotalATK,0")]
        [TestCase("1.2", "1.1*TotalATK,0", "1.2*TotalATK,0")]
        public void LevelFields_PreserveExplicitZeroAndLatestInheritance(string current, string previous, string expected)
            => Assert.That(HitDataParser.InheritLevelFields(current, previous), Is.EqualTo(expected));

        [Test]
        public void OptionalEffectColumns_AreReadByHeaderAndEnergyConflictsFail()
        {
            using var package = new ExcelPackage();
            var sheet = package.Workbook.Worksheets.Add("SkillsEffect");
            sheet.Cells[1, 1].Value = "ExecuteCount";
            sheet.Cells[1, 2].Value = "DamageHook";
            sheet.Cells[1, 3].Value = "EnergyGainMode";
            sheet.Cells[1, 4].Value = "Param2";
            sheet.Cells[2, 1].Value = 3;
            sheet.Cells[2, 2].Value = "Lisa_Skill()";
            sheet.Cells[2, 3].Value = "Based";
            Assert.That(Call("ReadExecuteCount", sheet, 2), Is.EqualTo(3));
            Assert.That(Call("ReadOptionalEffectText", sheet, 2, "DamageHook"), Is.EqualTo("Lisa_Skill()"));
            Assert.That(Call("ReadEffectParam2", sheet, 2, "GainEnergy"), Is.EqualTo("Based"));
            sheet.Cells[2, 4].Value = "Flat";
            var error = Assert.Throws<TargetInvocationException>(() => Call("ReadEffectParam2", sheet, 2, "GainEnergy"));
            Assert.That(error.InnerException, Is.TypeOf<TableValidationException>());
        }

        [Test]
        public void RepeatedContent_SharesEffectLimitsAndReactionIdentity()
        {
            var status = new StatusInstance();
            var action = new StatusActionData { ActionKey = "repeat-regression", MaxTimePerEffect = 1 };
            var identities = new HashSet<long>();
            int attempts = 0, collected = 0;
            BattleExecution.Run(() => BattleExecution.RepeatEffect(3, null, id =>
            {
                attempts++;
                identities.Add(id);
                if (BattleResolutionContext.TryCollect(status, action)) collected++;
                return Task.CompletedTask;
            })).GetAwaiter().GetResult();
            Assert.That(attempts, Is.EqualTo(3));
            Assert.That(collected, Is.EqualTo(1));
            Assert.That(identities.Count, Is.EqualTo(1));
            Assert.That(BattleResolutionContext.CurrentEffect, Is.Null);
        }

        [TestCase("Hit", "1", 1, 0, 0, 0, 0)]
        [TestCase("Turn;Effect", "2;1", 0, 1, 0, 2, 0)]
        [TestCase(" hit ; ACTION ; life ", "2;1;3", 2, 0, 1, 0, 3)]
        [TestCase("", "", 0, 0, 0, 0, 0)]
        public void TriggerLimits_TwoColumnsPreserveIndependentScopes(string scopes, string counts,
            int hit, int effect, int action, int turn, int life)
        {
            var data = new StatusActionData();
            data.SetTriggerLimits(scopes, counts);
            Assert.That(data.MaxTimePerHit, Is.EqualTo(hit));
            Assert.That(data.MaxTimePerEffect, Is.EqualTo(effect));
            Assert.That(data.MaxTimePerAction, Is.EqualTo(action));
            Assert.That(data.MaxTimePerTurn, Is.EqualTo(turn));
            Assert.That(data.MaxTimePerLife, Is.EqualTo(life));
        }

        [TestCase("Hit;Turn", "1")]
        [TestCase("Hit;Hit", "1;2")]
        [TestCase("Target", "1")]
        [TestCase("Hit", "1.5")]
        [TestCase("Hit", "-1")]
        [TestCase("Hit", "")]
        [TestCase("", "1")]
        public void TriggerLimits_InvalidPairsFailInsteadOfSilentlyDroppingLimits(string scopes, string counts)
            => Assert.Throws<TableValidationException>(() => new StatusActionData().SetTriggerLimits(scopes, counts));

        [Test]
        public void TriggerLimits_ReaderSupportsNewColumnsAndLegacy_WithoutOverlayingTheTwoFormats()
        {
            using var package = new ExcelPackage();
            var modern = package.Workbook.Worksheets.Add("Modern");
            modern.Cells[1, 1].Value = "MaxTime";
            modern.Cells[1, 2].Value = "MaxTimePerTurn";
            modern.Cells[1, 3].Value = "MaxTimePer";
            modern.Cells[2, 1].Value = "1;2";
            modern.Cells[2, 2].Value = 99;
            modern.Cells[2, 3].Value = "Hit;Turn";
            var legacy = package.Workbook.Worksheets.Add("Legacy");
            legacy.Cells[1, 1].Value = "MaxTimePerEffect";
            legacy.Cells[1, 2].Value = "MaxTimePerTurn";
            legacy.Cells[2, 1].Value = 1;
            legacy.Cells[2, 2].Value = 2;
            var missingCount = package.Workbook.Worksheets.Add("MissingMaxTime");
            missingCount.Cells[1, 1].Value = "MaxTimePer";
            missingCount.Cells[2, 1].Value = "Hit";
            var missingScope = package.Workbook.Worksheets.Add("MissingMaxTimePer");
            missingScope.Cells[1, 1].Value = "MaxTime";
            missingScope.Cells[2, 1].Value = 1;
            var dm = HookTestEnv.CreateDataManager(false);
            try
            {
                var method = typeof(DataManager).GetMethod("ReadStatusTriggerLimits", BindingFlags.Instance | BindingFlags.NonPublic);
                var data = new StatusActionData();
                method.Invoke(dm, new object[] { modern, 2, data });
                Assert.That(data.MaxTimePerHit, Is.EqualTo(1));
                Assert.That(data.MaxTimePerTurn, Is.EqualTo(2));
                modern.Cells[2, 1].Value = null;
                modern.Cells[2, 3].Value = null;
                method.Invoke(dm, new object[] { modern, 2, data });
                Assert.That(data.MaxTimePerHit, Is.Zero);
                Assert.That(data.MaxTimePerTurn, Is.Zero, "新列空白不重新拾取旧列的99次限制");
                method.Invoke(dm, new object[] { legacy, 2, data });
                Assert.That(data.MaxTimePerTurn, Is.EqualTo(2));
                Assert.That(data.MaxTimePerEffect, Is.EqualTo(1));
                foreach (var broken in new[] { missingCount, missingScope })
                {
                    var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(dm, new object[] { broken, 2, data }));
                    Assert.That(error.InnerException, Is.TypeOf<TableValidationException>());
                    Assert.That(error.InnerException.Message, Does.Contain("MaxTimePer / MaxTime 必须同时存在"));
                }
            }
            finally { HookTestEnv.DestroyDataManager(dm); }
        }

        [Test]
        public void HitLimits_ReserveOncePerLogicalHitAndRemainIndependentFromTargetsRowsAndNestedHits()
        {
            var status = new StatusInstance();
            var otherStatus = new StatusInstance();
            var action = new StatusActionData { ActionKey = "hit-limit" };
            var otherRow = new StatusActionData { ActionKey = "other-row" };
            action.SetTriggerLimits("Hit", "1");
            otherRow.SetTriggerLimits("Hit", "1");
            var first = new ScriptHookContext { DamageEvent = new DamageResolvedEvent { RuntimeHitID = 101, TargetIndex = 1 } };
            var secondTarget = new ScriptHookContext { DamageEvent = new DamageResolvedEvent { RuntimeHitID = 101, TargetIndex = 2 } };
            var next = new ScriptHookContext { DamageEvent = new DamageResolvedEvent { RuntimeHitID = 102, TargetIndex = 1 } };
            using (BattleResolutionContext.BeginEffect())
            {
                Assert.That(BattleResolutionContext.TryCollect(status, action, first), Is.True);
                Assert.That(BattleResolutionContext.TryCollect(status, action, secondTarget), Is.False);
                Assert.That(BattleResolutionContext.TryCollect(otherStatus, action, secondTarget), Is.True);
                Assert.That(BattleResolutionContext.TryCollect(status, otherRow, secondTarget), Is.True);
                using (BattleResolutionContext.BeginEffect())
                    Assert.That(BattleResolutionContext.TryCollect(status, action, next), Is.True);
                Assert.That(BattleResolutionContext.TryCollect(status, action, first), Is.False,
                    "子 hit 不能清空父 hit 已预留的额度");
                Assert.That(BattleResolutionContext.HasApplicableLimit(action), Is.False,
                    "只配Hit不得取消旧重入保护，新的追加hit有新ID，不能作为允许无限递归的依据");
            }
            using (BattleResolutionContext.BeginEffect())
                Assert.That(BattleResolutionContext.TryCollect(status, action, first), Is.True,
                    "计数由本次根范围持有，不留跨结算静态缓存");
        }

        [Test]
        public void HitAndEffectLimits_CombineWithoutChangingTheRootEffectBoundary()
        {
            var status = new StatusInstance();
            var action = new StatusActionData { ActionKey = "combined-limit" };
            action.SetTriggerLimits("Hit;Effect", "1;2");
            using (BattleResolutionContext.BeginEffect())
            {
                foreach (long hitID in new long[] { 201, 202 })
                {
                    var context = new ScriptHookContext { DamageEvent = new DamageResolvedEvent { RuntimeHitID = hitID } };
                    Assert.That(BattleResolutionContext.TryCollect(status, action, context), Is.True);
                    Assert.That(BattleResolutionContext.TryCollect(status, action, context), Is.False);
                }
                var third = new ScriptHookContext { DamageEvent = new DamageResolvedEvent { RuntimeHitID = 203 } };
                Assert.That(BattleResolutionContext.TryCollect(status, action, third), Is.False);
            }
        }

        [Test]
        public void UnknownDamageHook_FailsInsteadOfApplyingUnmodifiedDamage()
        {
            bool executed = false;
            Assert.Throws<NotSupportedException>(() =>
                BattleExecution.Run(() => BattleExecution.RepeatEffect(1, "Lisa_Skill()", id =>
                {
                    executed = true;
                    return Task.CompletedTask;
                })).GetAwaiter().GetResult());
            Assert.That(executed, Is.False);
            Assert.That(BattleExecution.IsBusy, Is.False);
        }

        private static object Call(string name, params object[] arguments)
            => typeof(DataManager).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, arguments);
    }
}
