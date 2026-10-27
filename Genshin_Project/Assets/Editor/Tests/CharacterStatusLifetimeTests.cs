using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using OfficeOpenXml;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class CharacterStatusLifetimeTests
    {
        private const string Owned = "ST_NotNamedAfterAnyCharacter", Child = "ST_PublicBoundChild";
        private static StatusMainData Main(BattleFlowTestEnv env, string id, int number, int max = 0)
        {
            var main = new StatusMainData { StatusID = number, StatusID2 = id, MaxStack = max, WhenMax = "0" };
            env.DataManager.StatusMainDict[id] = main; return main;
        }
        private static BattleEntity Start(BattleFlowTestEnv env)
        {
            env.CreateMinimalBattle(); env.CreateAlly(2);
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            return env.Ally.Entity;
        }
        private static void Lethal(BattleFlowTestEnv env, BattleEntity target)
            => target.TakeDamage(100000, DamageSourceInfo.Create(env.Enemy.Entity,
                ReactionSourceKind.EnemySkill, "SK_DeathTest", "SE_DeathTest"));

        [UnityTest]
        public IEnumerator Death_RemovesEveryMatchingIDAcrossAllHosts_RegardlessOfCaster_WithoutOnEnd()
            => HookTestEnv.AwaitTask(AllHostsAsync);
        private async Task AllHostsAsync()
        {
            using var env = new BattleFlowTestEnv(); var owner = Start(env);
            var reserve = env.Manager.GetEntityByPosition(BattleSide.Ally, 2);
            var main = Main(env, Owned, 9911001);
            env.DataManager.CharacterStatusIDs[owner.EntityID] = new HashSet<string> { Owned };
            var childMain = Main(env, Child, 9911002);
            var unrelated = Main(env, "ST_OtherCharacter", 9911003);
            var kept = env.Enemy.Entity.AddStatus(unrelated.StatusID2, owner, 0, 2, 0, unrelated);
            var states = new List<StatusInstance>
            {
                owner.AddStatus(Owned, reserve, 3, 3, 0, main),
                reserve.AddStatus(Owned, reserve, 3, 3, 0, main),
                reserve.AddStatus(Owned, owner, 3, 3, 0, main),
                env.Enemy.Entity.AddStatus(Owned, env.Enemy.Entity, 3, 3, 0, main),
                env.Manager.AllyTeam.AddStatus(Owned, reserve, 3, 3, 0, main),
                env.Manager.EnemyTeam.AddStatus(Owned, reserve, 3, 3, 0, main),
                env.Manager.Field.GetSlot(BattleSide.Ally, 4).AddStatus(Owned, reserve, 3, 3, 0, main),
                env.Manager.Field.GetSlot(BattleSide.Enemy, 5).AddStatus(Owned, reserve, 3, 3, 0, main)
            };
            var child = reserve.AddStatus(Child, owner, 0, 2, 0, childMain);
            states[0].BoundStatuses.Add(child);
            env.DataManager.StatusEffectDict["STE_UnexpectedExpiration"] = new StatusEffectData
            { StatusEffectID2 = "STE_UnexpectedExpiration", EffectType = "GainEnergy", Param1 = "7", Param2 = "Flat", TargetType = "Self" };
            env.DataManager.StatusActionDict[Owned] = new List<StatusActionData>
            { new StatusActionData { StatusID2 = Owned, ActionType = "OnEnd", Param1 = "STE_UnexpectedExpiration" } };
            reserve.CurrentEnergy = 0; var removed = new List<long>();
            using var listener = BattleEventHub.Subscribe<StatusEventData>(BattleEventKind.StatusRemoved, (_, fact) =>
            { if (fact.StatusId == Owned) { removed.Add(fact.InstanceId); Assert.That(fact.Reason, Is.EqualTo("CharacterDied")); } }, 0);
            await BattleExecution.Run(async () => { Lethal(env, owner); await BattleExecution.Flush(); });
            Assert.That(states.All(s => !s.IsActive), Is.True);
            Assert.That(removed, Is.EquivalentTo(states.Select(s => s.ApplyOrder)));
            Assert.That(child.IsActive, Is.False, "非本表的具体绑定子状态仍随父清理");
            Assert.That(kept.IsActive, Is.True, "不能按死者Caster误删其他角色表的状态");
            Assert.That(reserve.CurrentEnergy, Is.Zero, "强制移除不是自然到期");
            owner.CurrentHP = owner.GetFinalHP();
            Assert.That(owner.HasStatus(Owned), Is.False, "直接写HP不重建；正式Revive也只恢复登记的初始化状态");
        }

        [UnityTest]
        public IEnumerator Removal_CancelsRemovedStatusPendingAction_ButLivingWatcherCanRespondNormally()
            => HookTestEnv.AwaitTask(ResponsesAsync);
        private async Task ResponsesAsync()
        {
            using var env = new BattleFlowTestEnv(); var owner = Start(env);
            var reserve = env.Manager.GetEntityByPosition(BattleSide.Ally, 2);
            var main = Main(env, Owned, 9911001);
            env.DataManager.CharacterStatusIDs[owner.EntityID] = new HashSet<string> { Owned };
            var removed = reserve.AddStatus(Owned, reserve, 0, 2, 0, main);
            var watcherMain = Main(env, "ST_LivingWatcher", 9911004);
            var watcher = reserve.AddStatus(watcherMain.StatusID2, reserve, 0, 2, 0, watcherMain);
            foreach (int value in new[] { 1, 7 })
                env.DataManager.StatusEffectDict["STE_DeathEnergy" + value] = new StatusEffectData
                { StatusEffectID2 = "STE_DeathEnergy" + value, EffectType = "GainEnergy", Param1 = value.ToString(),
                    Param2 = "Flat", TargetType = "Self" };
            var pending = new StatusActionData { StatusID2 = Owned, ActionType = "OnTrigger", Param1 = "STE_DeathEnergy7" };
            var response = new StatusActionData { StatusID2 = watcherMain.StatusID2, ActionType = "OnTrigger", Param1 = "STE_DeathEnergy1" };
            reserve.CurrentEnergy = 0;
            using var listener = BattleEventHub.Subscribe<StatusEventData>(BattleEventKind.StatusRemoved, (_, fact) =>
            {
                if (fact.StatusId == Owned)
                    reserve.CharacterCtrl.TryExecuteEventStatusAction(watcher, response, reserve,
                        new ScriptHookContext { Caster = reserve, Status = watcher, StatusHost = reserve, Declaration = BattleEventHub.Current });
            }, watcher.ApplyOrder, () => watcher.IsActive);
            await BattleExecution.Run(async () =>
            {
                using (BattleResolutionContext.BeginCollection())
                {
                    Assert.That(reserve.CharacterCtrl.TryExecuteEventStatusAction(removed, pending, reserve,
                        new ScriptHookContext { Caster = reserve, Status = removed, StatusHost = reserve }), Is.True);
                    Lethal(env, owner);
                }
                await BattleExecution.Flush();
            });
            Assert.That(removed.LifeTriggerCount, Is.Zero);
            Assert.That(watcher.LifeTriggerCount, Is.EqualTo(1)); Assert.That(reserve.CurrentEnergy, Is.EqualTo(1));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void MissingIndexOrEnemyDeath_DoesNotGuessOwnership(bool enemyDeath)
        {
            using var env = new BattleFlowTestEnv(); var owner = Start(env);
            var main = Main(env, Owned, 9911001);
            var status = owner.AddStatus(Owned, owner, 0, 2, 0, main);
            if (enemyDeath) env.DataManager.CharacterStatusIDs[env.Enemy.Entity.EntityID] = new HashSet<string> { Owned };
            Lethal(env, enemyDeath ? env.Enemy.Entity : owner);
            Assert.That(status.IsActive, Is.True);
        }

        private static ExcelPackage Workbook(params StatusMainData[] statuses)
        {
            var package = new ExcelPackage();
            var sheet = package.Workbook.Worksheets.Add("StatusData_Main");
            var fields = Enumerable.Reverse(typeof(StatusMainData).GetFields(BindingFlags.Public | BindingFlags.Instance)).ToArray();
            for (int column = 0; column < fields.Length; column++)
            {
                sheet.Cells[1, column + 1].Value = fields[column].Name;
                for (int row = 0; row < statuses.Length; row++) sheet.Cells[row + 2, column + 1].Value = fields[column].GetValue(statuses[row]);
            }
            foreach (string name in new[] { "Status_Effect", "Status_Action", "Status_Attributes" })
                package.Workbook.Worksheets.Add(name).Cells[1, 1].Value = "StatusID";
            return package;
        }
        private static void Load(DataManager data, ExcelPackage package, int id)
        {
            try
            {
                typeof(DataManager).GetMethod("LoadCharacterStatusWorkbook", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(data, new object[] { package, id, $"CharacterStatus/{id}_Test.xlsx" });
            }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }

        [Test]
        public void WorkbookIndex_UsesRealRowsAndHeaders_NotNamesOrNumberPatterns_OverridesLegacyWithoutDuplicateActions()
        {
            using var env = new BattleFlowTestEnv();
            var old = Main(env, Owned, 17);
            env.DataManager.StatusEffectDict["STE_Legacy"] = new StatusEffectData { StatusEffectID = 1701 };
            env.DataManager.StatusActionDict[Owned] = new List<StatusActionData> { new StatusActionData { StatusID2 = Owned } };
            var first = new StatusMainData { StatusID = 123456, StatusID2 = Owned, StatusName = "新定义" };
            var second = new StatusMainData { StatusID = 987654, StatusID2 = "ST_AnotherUnrelatedName", StatusName = "另一个" };
            using var package = Workbook(first, second);
            Load(env.DataManager, package, 1020);
            Assert.That(env.DataManager.CharacterStatusIDs[1020], Is.EquivalentTo(new[] { first.StatusID2, second.StatusID2 }));
            Assert.That(env.DataManager.StatusMainDict[Owned].StatusID, Is.EqualTo(123456));
            Assert.That(env.DataManager.StatusEffectDict.ContainsKey("STE_Legacy"), Is.False);
            Assert.That(env.DataManager.StatusActionDict.ContainsKey(Owned), Is.False);
            Assert.Throws<TableValidationException>(() => Load(env.DataManager, package, 1019), "不同角色不允许共用同一状态ID");
        }
    }
}
