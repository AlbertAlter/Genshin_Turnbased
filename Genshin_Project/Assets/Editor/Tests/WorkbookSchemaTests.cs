using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OfficeOpenXml;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public class WorkbookSchemaTests
    {
        private static readonly string RuntimeDataRoot = Path.Combine(Application.streamingAssetsPath, "Data");

        [Test]
        public void RuntimeExcelCopies_AreReadable()
        {
            var errors = new List<string>();
            Dictionary<string, string> runtimeFiles = FindWorkbooks(RuntimeDataRoot, errors);

            foreach (var pair in runtimeFiles.OrderBy(item => item.Key))
            {
                try
                {
                    using FileStream stream = File.OpenRead(pair.Value);
                    if (stream.Length <= 0)
                        errors.Add($"StreamingAssets/Data 配表为空：{pair.Key}");
                }
                catch (Exception exception)
                {
                    errors.Add($"StreamingAssets/Data 配表无法读取：{pair.Key}（{exception.Message}）");
                }
            }

            AssertNoErrors(errors);
        }

        [Test]
        public void CoreWorkbookHeaders_MatchDataManagerReader()
        {
            var errors = new List<string>();

            ValidateHeaders("Characters.xlsx", "Sheet1", errors,
                "CharacterID", "Name");

            ValidateAllSheetHeaders("Character_Growth_Curve.xlsx", errors,
                "Level", "Multiplier");

            ValidateHeaders("StatusData.xlsx", "StatusData_Main", errors,
                "StatusID", "StatusID2", "StatusName", "StatusType", "Display", "Description",
                "MultiplierPart1", "MultiplierPart2", "MultiplierPart3", "ApplyDamageType",
                "ApplyReactionType", "ApplyElementType", "ApplyString", "ScriptHook",
                "MaxCount", "MaxStack", "WhenMax");
            ValidateHeaders("StatusData.xlsx", "Status_Effect", errors, EffectHeaders("StatusEffectID", "StatusEffectID2", "TargetSelect"));
            ValidateHeaders("StatusData.xlsx", "Status_Action", errors, StatusActionHeaders());
            ValidateHeaders("StatusData.xlsx", "Status_Attributes", errors, StatusAttributeHeaders());

            ValidateHeaders("StatusData_Enemy.xlsx", "StatusData_Main_Enemy", errors,
                "StatusID", "StatusID2", "StatusName", "StatusType", "Display", "Description",
                "MultiplierPart1", "MultiplierPart2", "MultiplierPart3", "ApplyDamageType",
                "ApplyReactionType", "ApplyElementType", "ApplyString", "ScriptHook",
                "MaxCount", "MaxStack", "WhenMax");
            ValidateHeaders("StatusData_Enemy.xlsx", "Status_Effect", errors, EffectHeaders("StatusEffectID", "StatusEffectID2", "TargetSelect"));
            ValidateHeaders("StatusData_Enemy.xlsx", "Status_Action_Enemy", errors, StatusActionHeaders());
            ValidateHeaders("StatusData_Enemy.xlsx", "Status_Attributes", errors, StatusAttributeHeaders());

            ValidateHeaders("StatusData_Overall.xlsx", "StatusData_Overall_Main", errors,
                "StatusID", "StatusID2", "StatusName", "StatusType", "Display", "Description",
                "MultiplierPart1", "MultiplierPart2", "MultiplierPart3", "ApplyDamageType",
                "ApplyReactionType", "ApplyElementType", "ApplyString", "ScriptHook",
                "MaxCount", "MaxStack", "WhenMax");
            ValidateHeaders("StatusData_Overall.xlsx", "StatusEffect_Overall", errors,
                EffectHeaders("StatusEffectID", "StatusEffectID2", "TargetSelect"));
            ValidateHeaders("StatusData_Overall.xlsx", "StatusAction_Overall", errors, StatusActionHeaders());
            ValidateHeaders("StatusData_Overall.xlsx", "StatusAttributes_Overall", errors, StatusAttributeHeaders());

            ValidateHeaders("EnemyAttributes.xlsx", "Enemy_Main", errors,
                "EnemyID", "EnemyNameID", "EnemyName", "Threat", "ActsGiven", "Poise", "ATK_Curve",
                "HP_coeff", "ATK_coeff", "PhysicalRes", "PyroRes", "HydroRes", "ElectroRes",
                "CryoRes", "AnemoRes", "DendroRes", "GeoRes", "PhysicalDmgBonus", "PyroDmgBonus",
                "HydroDmgBonus", "ElectroDmgBonus", "CryoDmgBonus", "AnemoDmgBonus",
                "DendroDmgBonus", "GeoDmgBonus");
            ValidateHeaders("EnemyAttributes.xlsx", "Base_HP", errors, "lv", "hp");
            ValidateHeaders("EnemyAttributes.xlsx", "Base_ATK1", errors, "lv", "atk1");
            ValidateHeaders("EnemyAttributes.xlsx", "Base_ATK2", errors, "lv", "atk2");

            ValidateHeaders("EnemySkill.xlsx", "EnemySkill_Main", errors,
                "EnemySkillID", "EnemySkillID2", "EnemySkillName", "Cooldown", "UsePerTurn", "HighThreat", "Description");
            ValidateHeaders("EnemySkill.xlsx", "EnemySkill_Effect", errors, EffectHeaders("SkillEffectID", "SkillEffectID2", "TargetNumber"));
            ValidateHeaders("EnemySkill.xlsx", "EnemySkill_Param", errors,
                "SkillEffectID", "SkillEffectID2", "EffectType", "Hits1", "Hits2", "Hits3",
                "Hits4", "Hits5", "Hits6", "Hits7", "Hits8");
            ValidateHeaders("EnemySkill.xlsx", "EnemyAI", errors,
                "EnemyID", "EnemyNameID", "EnemyName", "PatternID", "PatternType", "NextPatternID",
                "ConditionType", "ConditionParam");
            ValidateHeaders("EnemySkill.xlsx", "EnemyAI_Rule", errors,
                "EnemyID", "PatternID", "PatternType", "SkillID", "SkillIndex", "Weight");

            ValidateHeaders("ReactionLevelCoefficient.xlsx", "Sheet1", errors, "lv", "coefficient");
            ValidateHeaders("EnemyShield.xlsx", "Type1", errors,
                "", "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Dendro", "Geo", "Physical",
                "Overloaded", "Elemental_Hit", "Hit");

            AssertNoErrors(errors);
        }

        [Test]
        public void CharacterWorkbookHeaders_MatchDataManagerReader()
        {
            var errors = new List<string>();
            string characterRoot = Path.Combine(RuntimeDataRoot, "Characters");

            if (!Directory.Exists(characterRoot))
            {
                Assert.Fail($"角色配表目录不存在：{characterRoot}");
                return;
            }

            string[] workbooks = Directory.GetFiles(characterRoot, "*.xlsx")
                .Where(path => !Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal))
                .ToArray();

            if (workbooks.Length == 0)
                errors.Add("Characters 目录中没有角色工作簿");

            foreach (string workbookPath in workbooks)
            {
                using var package = new ExcelPackage(new FileInfo(workbookPath));
                string workbookName = "Characters/" + Path.GetFileName(workbookPath);

                ValidateHeaders(package, workbookName, "Attributes", errors,
                    "CharacterID", "NameID", "Name", "Star", "GrowthCurveID", "WeaponType", "Element",
                    "BaseHP", "BaseATK", "BaseDEF", "MaxEnergy", "Poise", "HitWeight", "NormalAttackID",
                    "HeavyAttackID", "ElementalSkillID", "ElementalBurstID", "BaseEM", "BaseRechargeRate",
                    "CritRate", "CritDMG", "PyroRes", "HydroRes", "ElectroRes", "CryoRes", "AnemoRes",
                    "DendroRes", "GeoRes", "PhysicalRes", "PyroDmgBonus", "HydroDmgBonus",
                    "ElectroDmgBonus", "CryoDmgBonus", "AnemoDmgBonus", "GeoDmgBonus",
                    "DendroDmgBonus", "PhysicalDmgBonus");
                ValidateHeaders(package, workbookName, "Skills", errors,
                    "SkillID", "SkillID2", "SkillName", "APCost", "Cooldown", "EnergyUsed", "MaxCharge",
                    "InitialCharge", "SkillPhase", "ActionType", "Icon", "Description");
                ValidateHeaders(package, workbookName, "SkillsEffect", errors,
                    EffectHeaders("SkillEffectID", "SkillEffectID2", "TargetNumber"));
                ValidateHeaders(package, workbookName, "SkillLevel", errors,
                    "CharacterID", "SkillType", "SkillLevel", "ParamID", "Hits1", "Hits2", "Hits3",
                    "Hits4", "Hits5", "Hits6", "Hits7");
                ValidateHeaders(package, workbookName, "Talent", errors,
                    "CharacterID", "TalentIndex", "TalentName", "UnlockAfter", "Modifiers", "Description");
                ValidateHeaders(package, workbookName, "Constellation", errors,
                    "CharacterID", "ConstellationIndex", "ConstellationName", "Modifiers", "Description");
                ValidateHeaders(package, workbookName, "Ascension", errors,
                    "AscensionLevel", "BaseHPFlat", "BaseATKFlat", "BaseDEFFlat", "HPBonus", "ATKBonus",
                    "DEFBonus", "CritRate", "CritDMG", "EM", "EnergyRechargeRate", "PyroDmgBonus",
                    "HydroDmgBonus", "ElectroDmgBonus", "CryoDmgBonus", "AnemoDmgBonus", "GeoDmgBonus",
                    "DendroDmgBonus", "PhysicalDmgBonus");
            }

            AssertNoErrors(errors);
        }

        private static string[] EffectHeaders(string idHeader, string id2Header, string targetColumn)
        {
            return new[]
            {
                idHeader, id2Header, "EffectIndex", "EffectType", "Element", "DamageType", "Duration",
                "AddInPhase", "TriggerPhase", "Param1", "Param2", "Param3", "TargetType", targetColumn,
                "TargetConsecutive", "TargetOverride", "ScriptHook"
            };
        }

        private static string[] StatusActionHeaders()
        {
            return new[]
            {
                "StatusID", "StatusID2", "ActionType", "Param1", "Param2", "Param3", "ScriptHook",
                "OnAction", "OutgoingHit", "OutgoingDamage", "OnHit", "OnDamage", "OnHealFrom",
                "WhenHealing", "APUsed", "ReactionTriggered", "MaxTimePerTurn", "MaxTimePerLife", "Cooldown"
            };
        }

        private static string[] StatusAttributeHeaders()
        {
            return new[]
            {
                "StatusID", "StatusID2", "TotalHP", "TotalATK", "TotalDEF", "TotalEM", "TotalRechargeRate",
                "CritRate", "CritDMG", "BaseDMGBonusFlat", "DMGBonus", "DEFReduction", "DEFIgnored",
                "ResBonus", "ShieldStrength", "HealBonus", "BeHealedBonus", "Elevation", "BaseDMGBonus",
                "LunarDMGBonus", "StellarDMGBonus"
            };
        }

        private static void ValidateHeaders(string workbookName, string sheetName, List<string> errors, params string[] expectedHeaders)
        {
            string path = Path.Combine(RuntimeDataRoot, workbookName);
            if (!File.Exists(path))
            {
                errors.Add($"缺少工作簿：{workbookName}");
                return;
            }

            using var package = new ExcelPackage(new FileInfo(path));
            ValidateHeaders(package, workbookName, sheetName, errors, expectedHeaders);
        }

        private static void ValidateAllSheetHeaders(string workbookName, List<string> errors, params string[] expectedHeaders)
        {
            string path = Path.Combine(RuntimeDataRoot, workbookName);
            if (!File.Exists(path))
            {
                errors.Add($"缺少工作簿：{workbookName}");
                return;
            }

            using var package = new ExcelPackage(new FileInfo(path));
            if (package.Workbook.Worksheets.Count == 0)
            {
                errors.Add($"{workbookName} 没有工作表");
                return;
            }

            foreach (ExcelWorksheet worksheet in package.Workbook.Worksheets)
                ValidateHeaders(package, workbookName, worksheet.Name, errors, expectedHeaders);
        }


        private static void ValidateHeaders(
            ExcelPackage package,
            string workbookName,
            string sheetName,
            List<string> errors,
            params string[] expectedHeaders)
        {
            ExcelWorksheet sheet = package.Workbook.Worksheets[sheetName];
            if (sheet == null)
            {
                errors.Add($"{workbookName} 缺少工作表 {sheetName}");
                return;
            }

            for (int index = 0; index < expectedHeaders.Length; index++)
            {
                string actual = sheet.Cells[1, index + 1].Text.Trim();
                string expected = expectedHeaders[index];
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                    errors.Add($"{workbookName}/{sheetName} 第 {index + 1} 列应为 {expected}，实际为 [{actual}]");
            }
        }

        private static Dictionary<string, string> FindWorkbooks(string root, List<string> errors)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(root))
            {
                errors.Add($"目录不存在：{root}");
                return result;
            }

            foreach (string path in Directory.GetFiles(root, "*.xlsx", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal))
                    continue;

                string relativePath = path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace(Path.DirectorySeparatorChar, '/');
                result[relativePath] = path;
            }

            return result;
        }

        private static void AssertNoErrors(List<string> errors)
        {
            Assert.That(errors, Is.Empty, string.Join(Environment.NewLine, errors));
        }
    }
}
