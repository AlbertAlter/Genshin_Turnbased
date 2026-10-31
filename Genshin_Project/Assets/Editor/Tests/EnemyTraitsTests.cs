using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using OfficeOpenXml;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class EnemyTraitsTests
    {
        [Test] public void ImmuneList_TrimsAndDeduplicatesNames()
        {
            Assert.That(EnemyTraits.ParseStatusImmunities(" 冻结 ;禁锢;冻结;; "), Is.EquivalentTo(new[] { "冻结", "禁锢" }));
            Assert.That(EnemyTraits.ParseStatusImmunities(null), Is.Empty);
        }

        [Test] public void IdentitySheet_LoadsFamilyTypeAndNames_AndBlankClearsOldImmunity()
        {
            using var env = new BattleFlowTestEnv();
            using var package = new ExcelPackage();
            var sheet = package.Workbook.Worksheets.Add("Sheet2");
            string[] columns = { "顺序ID", "中文名", "English Name", "EnemyType", "生物种类", "Immune" };
            for (int i = 0; i < columns.Length; i++) sheet.Cells[1, i + 1].Value = columns[i];
            sheet.Cells[2, 1].Value = BattleFlowTestEnv.EnemyID;
            sheet.Cells[2, 4].Value = "Minion"; sheet.Cells[2, 5].Value = "丘丘部族";
            sheet.Cells[2, 6].Value = "冻结;禁锢";
            var method = typeof(DataManager).GetMethod("LoadEnemyIdentitySheet", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Invoke(env.DataManager, new object[] { sheet });
            var data = env.DataManager.EnemyMainDict[BattleFlowTestEnv.EnemyID];
            Assert.That(data.EnemyType, Is.EqualTo("Minion")); Assert.That(data.EnemyFamily, Is.EqualTo("丘丘部族"));
            Assert.That(data.StatusImmunities, Is.EquivalentTo(new[] { "冻结", "禁锢" }));
            sheet.Cells[2, 6].Value = null;
            method.Invoke(env.DataManager, new object[] { sheet });
            Assert.That(data.StatusImmunities, Is.Empty);
        }

        [Test] public void NamedStatusIsRejectedBeforeOnApply_WithoutBlockingOtherStatusesOrCharacters()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            env.ConfigureStatus("ST_TestPrison"); env.ConfigureStatus("ST_TestOther");
            var prison = env.DataManager.StatusMainDict["ST_TestPrison"]; prison.StatusName = "禁锢";
            env.DataManager.EnemyMainDict[BattleFlowTestEnv.EnemyID].StatusImmunities = EnemyTraits.ParseStatusImmunities("禁锢");
            Assert.That(env.Enemy.Entity.AddStatus(prison.StatusID2, env.Ally.Entity, 1, 3, 0, prison), Is.Null);
            Assert.That(env.Enemy.Entity.HasStatus(prison.StatusID2), Is.False);
            Assert.That(env.Ally.Entity.AddStatus(prison.StatusID2, env.Enemy.Entity, 1, 3, 0, prison), Is.Not.Null);
            Assert.That(env.Enemy.Entity.AddStatus("ST_TestOther", env.Ally.Entity, 1, 3, 0,
                env.DataManager.StatusMainDict["ST_TestOther"]), Is.Not.Null);
        }

        [TestCase("冻结")]
        [TestCase("Frozen")]
        [TestCase("ST_Freeze")]
        public void FreezeImmunity_PreservesReactionConsumption_BlocksStateAndShatter(string name)
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var target = env.Enemy.Entity;
            env.DataManager.EnemyMainDict[target.EntityID].StatusImmunities = EnemyTraits.ParseStatusImmunities(name);
            target.ApplyAura("Cryo", 1.5f, 1);
            bool reacted = FrozenReactionHandler.TryResolveFreeze(new ReactionContext {
                SourceEntity = env.Ally.Entity, Target = target, AttackElement = "Hydro", AttackAmount = 2,
                ApplicationPhase = 3, SourceKind = ReactionSourceKind.CharacterSkill }, out var result);
            Assert.That(reacted, Is.True);
            Assert.That(result.RemainingAttackAmount, Is.EqualTo(.5f));
            Assert.That(target.GetAura("Cryo"), Is.Null);
            Assert.That(FrozenReactionHandler.IsFrozen(target), Is.False);
            Assert.That(target.HasStatus("ST_Freeze"), Is.False);
            Assert.That(FrozenReactionHandler.CanShatter(new ReactionContext {
                SourceEntity = env.Ally.Entity, Target = target, AttackElement = "Geo", CanTriggerShatter = true }), Is.False);
        }

        [Test] public void FamilyAndTypeQueries_UseExplicitMetadata_NotNameOrCharacterID()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var data = env.DataManager.EnemyMainDict[BattleFlowTestEnv.EnemyID];
            data.EnemyName = "丘丘人"; data.EnemyFamily = "史莱姆"; data.EnemyType = "Minion";
            Assert.That(Enemy2011To2020Hooks.IsPillarTarget(env.Enemy.Entity), Is.False);
            data.EnemyFamily = "丘丘部族";
            Assert.That(Enemy2011To2020Hooks.IsPillarTarget(env.Enemy.Entity), Is.True);
            data.EnemyType = "Elite";
            Assert.That(Enemy2011To2020Hooks.IsPillarTarget(env.Enemy.Entity), Is.False);
            env.Ally.Entity.EntityID = BattleFlowTestEnv.EnemyID;
            Assert.That(EnemyTraits.HasFamily(env.Ally.Entity, "丘丘部族"), Is.False);
        }
    }
}
