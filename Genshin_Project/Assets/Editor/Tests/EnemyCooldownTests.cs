using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using OfficeOpenXml;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public class EnemyCooldownTests
    {
        [Test]
        public void Tags_TrimDeduplicateAndMatchWholeCaseSensitiveLabels()
        {
            var tags = EnemySkillTags.Parse(" Armor ;Support;;Armor; ");
            CollectionAssert.AreEqual(new[] { "Armor", "Support" }, tags);
            var skill = new EnemySkillMainData { EnemySkillID = 123401, EnemySkillID2 = "SK_Test", SkillTags = tags };
            Assert.That(EnemySkillTags.Matches(skill, "Tag(Armor)"), Is.True);
            Assert.That(EnemySkillTags.Matches(skill, "Tag(Arm)"), Is.False);
            Assert.That(EnemySkillTags.Matches(skill, "Tag(armor)"), Is.False);
            Assert.That(EnemySkillTags.Matches(skill, "SK_Test"), Is.True);
            Assert.That(EnemySkillTags.Matches(skill, "123401"), Is.True);
            Assert.That(EnemySkillTags.Parse(null), Is.Empty);
        }

        [TestCase("Tag()")]
        [TestCase("Tag(Armor;Support)")]
        [TestCase("Skill")]
        [TestCase("Normal")]
        public void EnemyReference_RejectsInvalidSyntaxAndCharacterButtons(string reference)
            => Assert.Throws<TableValidationException>(() => TableValidator.ValidateCooldown("Enemy", 2, reference, "-2", true));

        [Test]
        public void Validation_RequiresEnemyTarget_AndPreservesCharacterRules()
        {
            Assert.DoesNotThrow(() => TableValidator.ValidateCooldown("Enemy", 2, "Tag(Armor)", "-2", true));
            Assert.DoesNotThrow(() => TableValidator.ValidateCooldown("Character", 2, "Skill", "-2"));
            Assert.Throws<TableValidationException>(() => TableValidator.ValidateCooldown("Character", 2, "Tag(Armor)", "-2"));
            Assert.Throws<TableValidationException>(() => TableValidator.ValidateEffectRow("Enemy", 2,
                "Cooldown", null, 0, "-2", null, 0, 0, null, isEnemy: true));
            Assert.DoesNotThrow(() => TableValidator.ValidateEffectRow("Enemy", 2,
                "Cooldown", null, 0, "-2", "Self", 0, 0, null, isEnemy: true));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Loader_ReadsTagsAndAcceptsLegacyMissingColumn(bool withTags)
        {
            using var env = new BattleFlowTestEnv();
            using var book = new ExcelPackage();
            var sheet = book.Workbook.Worksheets.Add("EnemySkill_Main");
            string[] headers = { "EnemySkillID", "EnemySkillID2", "EnemySkillName", "Cooldown", "UsePerTurn", "HighThreat", "Description", "SkillTags" };
            for (int i = 0; i < (withTags ? headers.Length : headers.Length - 1); i++) sheet.Cells[1, i + 1].Value = headers[i];
            sheet.Cells[2, 1].Value = 987601;
            sheet.Cells[2, 2].Value = "SK_TestTags";
            if (withTags) sheet.Cells[2, 8].Value = " Armor ;Support;;Armor ";
            typeof(DataManager).GetMethod("LoadEnemySkillWorkbook", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(env.DataManager, new object[] { book });
            CollectionAssert.AreEqual(withTags ? new[] { "Armor", "Support" } : Array.Empty<string>(),
                env.DataManager.EnemySkillMainDict[987601].SkillTags);
        }

        [Test]
        public void TagCooldown_MatchesOwnSkillsAcrossPatterns_AndClampsAtZero()
        {
            using var env = new BattleFlowTestEnv(); env.ConfigureEnemyDamageSkill(); env.CreateMinimalBattle();
            var first = AddSkill(env, env.Enemy, 1, "Armor;Support");
            var second = AddSkill(env, env.Enemy, 2, "Armor");
            var untagged = AddSkill(env, env.Enemy, 3, "Support");
            var foreign = new EnemySkillMainData { EnemySkillID = 987601, SkillTags = EnemySkillTags.Parse("Armor") };
            env.DataManager.EnemySkillMainDict[foreign.EnemySkillID] = foreign;
            var cooldowns = Cooldowns(env.Enemy);
            cooldowns[first.EnemySkillID] = 3; cooldowns[second.EnemySkillID] = 1;
            cooldowns[untagged.EnemySkillID] = 4; cooldowns[foreign.EnemySkillID] = 6;
            Assert.That(env.Enemy.ChangeSkillCooldown("Tag(Armor)", -2), Is.True);
            Assert.That(cooldowns[first.EnemySkillID], Is.EqualTo(1));
            Assert.That(cooldowns.ContainsKey(second.EnemySkillID), Is.False);
            Assert.That(cooldowns[untagged.EnemySkillID], Is.EqualTo(4));
            Assert.That(cooldowns[foreign.EnemySkillID], Is.EqualTo(6));
            Assert.That(first.Cooldown, Is.EqualTo(3));
            Assert.That(env.Enemy.ChangeSkillCooldown("Tag(Missing)", -2), Is.False);
            Assert.That(env.Enemy.ChangeSkillCooldown(second.EnemySkillID2, 2), Is.True);
            Assert.That(cooldowns[second.EnemySkillID], Is.EqualTo(2));
            env.Enemy.TickSkillCooldowns();
            Assert.That(cooldowns.ContainsKey(first.EnemySkillID), Is.False);
            Assert.That(cooldowns[second.EnemySkillID], Is.EqualTo(1));
        }

        [Test]
        public void Cooldown_PositiveOverflowRejectsWholeTarget_AndExtremeNegativeClamps()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var a = AddSkill(env, env.Enemy, 1, "Armor"); var b = AddSkill(env, env.Enemy, 2, "Armor");
            var cd = Cooldowns(env.Enemy); cd[a.EnemySkillID] = 1; cd[b.EnemySkillID] = int.MaxValue;
            Assert.That(env.Enemy.ChangeSkillCooldown("Tag(Armor)", 1), Is.False);
            Assert.That(cd[a.EnemySkillID], Is.EqualTo(1));
            Assert.That(env.Enemy.ChangeSkillCooldown("Tag(Armor)", int.MinValue), Is.True);
            Assert.That(cd.ContainsKey(a.EnemySkillID), Is.False); Assert.That(cd.ContainsKey(b.EnemySkillID), Is.False);
        }

        [UnityTest]
        public IEnumerator Effect_UsesSelectedEnemyHost_NotCaster_AndKeepsCharacterCooldownUntouched()
            => HookTestEnv.AwaitTask(TargetEffectAsync);

        private async Task TargetEffectAsync()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var other = env.CreateEnemy(2);
            var skill = AddSkill(env, env.Enemy, 1, "Armor");
            Cooldowns(env.Enemy)[skill.EnemySkillID] = 4; Cooldowns(other)[skill.EnemySkillID] = 3;
            env.ConfigureStatus("ST_TestCooldownHost");
            var status = other.Entity.AddStatus("ST_TestCooldownHost", env.Enemy.Entity, 0, 6, 0,
                env.DataManager.StatusMainDict["ST_TestCooldownHost"]);
            var effect = new EnemySkillEffectData { SkillEffectID2 = "SE_TestCooldown", EffectType = "Cooldown",
                Param1 = "Tag(Armor)", Param2 = "-2", TargetType = "Self", ExecuteCount = 1 };
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            await BattleExecution.Run(() => (Task)typeof(EnemyBattleController)
                .GetMethod("ExecuteEnemyEffectAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(env.Enemy, new object[] { effect, status }));
            Assert.That(Cooldowns(env.Enemy)[skill.EnemySkillID], Is.EqualTo(4));
            Assert.That(Cooldowns(other)[skill.EnemySkillID], Is.EqualTo(1));
            Assert.That(random.IntCallCount, Is.Zero); Assert.That(random.FloatCallCount, Is.Zero);
        }

        private static EnemySkillMainData AddSkill(BattleFlowTestEnv env, EnemyBattleController owner, int index, string tags)
        {
            int id = owner.EnemyID * 100 + 80 + index;
            var skill = new EnemySkillMainData { EnemySkillID = id, EnemySkillID2 = "SK_TagTest" + id,
                Cooldown = 3, SkillTags = EnemySkillTags.Parse(tags) };
            env.DataManager.EnemySkillMainDict[id] = skill;
            return skill;
        }

        private static Dictionary<int, int> Cooldowns(EnemyBattleController enemy)
            => (Dictionary<int, int>)typeof(EnemyBattleController)
                .GetField("_skillCooldownLeft", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(enemy);
    }
}
