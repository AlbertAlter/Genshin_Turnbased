using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public class WithdrawnEffectTests
    {
        private static StatusMainData Main(BattleFlowTestEnv e, string id, int number)
        {
            var main = new StatusMainData { StatusID = number, StatusID2 = id, MaxStack = 0, WhenMax = "0" };
            e.DataManager.StatusMainDict[id] = main; return main;
        }
        private static StatusInstance Add(BattleEntity host, BattleEntity caster, StatusMainData main)
            => host.AddStatus(main.StatusID2, caster, 0, 2, 0, main);

        [UnityTest] public IEnumerator Character_UsesDeathCleanupAcrossHosts_AndKeepsFreeReplacement()
            => HookTestEnv.AwaitTask(CharacterAsync);
        private async Task CharacterAsync()
        {
            using var e = new BattleFlowTestEnv(); e.CreateMinimalBattle(); var friend = e.CreateAlly(2);
            e.Manager.StartBattle(); e.Flow.AdvanceToPhase(e.Manager, TurnPhase.AllyAction);
            var actor = e.Ally.Entity; const string owned = "ST_WithdrawOwned";
            var main = Main(e, owned, 9924001); var other = Main(e, "ST_WithdrawUnrelated", 9924002);
            e.DataManager.CharacterStatusIDs[actor.EntityID] = new HashSet<string> { owned };
            var states = new[] { Add(actor, friend.Entity, main), Add(friend.Entity, friend.Entity, main),
                Add(e.Enemy.Entity, e.Enemy.Entity, main), e.Manager.AllyTeam.AddStatus(owned, friend.Entity, 0, 2, 0, main),
                e.Manager.EnemyTeam.AddStatus(owned, friend.Entity, 0, 2, 0, main),
                e.Manager.Field.GetSlot(BattleSide.Ally, 4).AddStatus(owned, friend.Entity, 0, 2, 0, main) };
            var kept = Add(friend.Entity, actor, other);
            var equipmentMain = Main(e, "ST_Weapon_WithdrawTest", 9924003);
            var equipment = Add(friend.Entity, actor, equipmentMain); var oldEquipmentSource = equipment.EquipmentSource;
            int deaths = 0, withdrawals = 0;
            using var death = BattleEventHub.Subscribe<DeathEventData>(BattleEventKind.Death, (_, __) => deaths++, 0);
            using var withdraw = BattleEventHub.Subscribe<WithdrawnEventData>(BattleEventKind.Withdrawn, (_, fact) => {
                withdrawals++; Assert.That(fact.Subject, Is.SameAs(actor)); Assert.That(fact.Source, Is.SameAs(e.Enemy.Entity));
            }, 0);
            await BattleExecution.Run(async () => {
                Assert.That(WithdrawnEffect.Execute(new[] { actor, actor }, e.Enemy.Entity), Is.EqualTo(1));
                await BattleExecution.Flush();
            });
            Assert.That(actor.CurrentHP, Is.Zero); Assert.That(actor.HasWithdrawn, Is.True);
            Assert.That(states.All(s => !s.IsActive), Is.True, "按角色表ID清全场，不按施加者过滤。");
            Assert.That(kept.IsActive, Is.True, "普通其他角色状态不能因Caster退场被误删。");
            Assert.That(equipment.IsActive, Is.False); Assert.That(oldEquipmentSource.IsAvailable, Is.False);
            Assert.That(e.Manager.Allies.Any(c => c.Entity == actor), Is.True, "保留编队，沿用失败与免费换人判定。");
            e.Manager.EndAllyTurn();
            e.Flow.AdvanceUntil(e.Manager, () => e.Manager.CurrentPhase != TurnPhase.AllyAction, "leave current ally turn");
            e.Flow.AdvanceToPhase(e.Manager, TurnPhase.AllyAction);
            Assert.That(e.Manager.GetSwitchAPCost(1), Is.Zero);
            Assert.That(e.Manager.TrySwitchActiveAllyWithAPCost(1), Is.True);
            Assert.That(friend.IsActive, Is.True); Assert.That(deaths, Is.Zero); Assert.That(withdrawals, Is.EqualTo(1));
            ReviveEffect.Execute("100", friend.Entity, new[] { actor }); actor.Heal(100);
            Assert.That(actor.CurrentHP, Is.Zero);
        }

        [Test]
        public void MixedBatch_CommitsEveryTargetBeforeVictory_AndDoesNotNotifyDeath()
        {
            using var e = new BattleFlowTestEnv(); e.CreateMinimalBattle();
            int deaths = 0, withdrawals = 0, endings = 0;
            using var death = BattleEventHub.Subscribe<DeathEventData>(BattleEventKind.Death, (_, __) => deaths++, 0);
            using var leave = BattleEventHub.Subscribe<WithdrawnEventData>(BattleEventKind.Withdrawn, (_, __) => {
                withdrawals++; Assert.That(e.Ally.Entity.CurrentHP, Is.Zero); Assert.That(e.Enemy.Entity.CurrentHP, Is.Zero);
            }, 0);
            using var ended = BattleEventHub.Subscribe<BattleEndedEventData>(BattleEventKind.BattleEnded, (_, __) => endings++, 0);
            Assert.That(WithdrawnEffect.Execute(new[] { e.Enemy.Entity, e.Ally.Entity }), Is.EqualTo(2));
            Assert.That(withdrawals, Is.EqualTo(2)); Assert.That(deaths, Is.Zero); Assert.That(endings, Is.EqualTo(1));
            Assert.That(e.Manager.Victory, Is.True, "同组双方归零仍沿用敌方清空优先胜利。");
        }

        [UnityTest] public IEnumerator CharacterSkill_ExecutesWithdrawn() => HookTestEnv.AwaitTask(() => EntryAsync(false, false));
        [UnityTest] public IEnumerator EnemySkill_ExecutesWithdrawn() => HookTestEnv.AwaitTask(() => EntryAsync(true, false));
        [UnityTest] public IEnumerator CharacterStatus_ExecutesWithdrawnOnActualHost() => HookTestEnv.AwaitTask(() => EntryAsync(false, true));
        [UnityTest] public IEnumerator EnemyStatus_ExecutesWithdrawnOnActualHost() => HookTestEnv.AwaitTask(() => EntryAsync(true, true));
        private async Task EntryAsync(bool enemy, bool status)
        {
            using var e = new BattleFlowTestEnv(); e.ConfigureEnemyDamageSkill(); e.CreateMinimalBattle();
            var source = enemy ? e.Enemy.Entity : e.Ally.Entity;
            var target = status && enemy ? e.Ally.Entity : e.Enemy.Entity;
            int deaths = 0, withdrawals = 0;
            using var death = BattleEventHub.Subscribe<DeathEventData>(BattleEventKind.Death, (_, __) => deaths++, 0);
            using var leave = BattleEventHub.Subscribe<WithdrawnEventData>(BattleEventKind.Withdrawn, (_, fact) => {
                withdrawals++; Assert.That(fact.Subject, Is.SameAs(target)); Assert.That(fact.Source, Is.SameAs(source));
            }, 0);
            await BattleExecution.Run(async () => {
                if (status) {
                    var main = Main(e, "ST_WithdrawEntry", 9924010); var instance = Add(target, source, main);
                    const string effect = "STE_WithdrawEntry";
                    e.DataManager.StatusEffectDict[effect] = new StatusEffectData {
                        StatusEffectID = 992401001, StatusEffectID2 = effect, EffectType = "Withdrawn", TargetType = "Self" };
                    var actions = new List<StatusActionData> { new StatusActionData {
                        StatusID = main.StatusID, StatusID2 = main.StatusID2, ActionType = "OnTrigger", Param1 = effect } };
                    if (enemy) await e.Enemy.ExecuteStatusActionsTask(instance, actions, target, "OnTrigger");
                    else await e.Ally.ExecuteStatusActionsTask(instance, actions, target, "OnTrigger");
                } else if (enemy) {
                    var effect = e.DataManager.EnemySkillEffectDict["SE_Enemy_BattleFlow"];
                    effect.EffectType = "Withdrawn"; effect.TargetType = "Self"; effect.Element = null;
                    await e.Enemy.TakeEnemyTurnAsync();
                } else {
                    var effect = new SkillEffectData { SkillEffectID = 992401001, SkillEffectID2 = "SE_WithdrawEntry",
                        EffectType = "Withdrawn", TargetType = "Enemy", TargetNumber = 1, TargetConsecutive = 1 };
                    await (Task)typeof(CharacterBattleController).GetMethod("ExecuteEffectAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(e.Ally, new object[] { effect, 2, null });
                }
                await BattleExecution.Flush();
            });
            Assert.That(target.HasWithdrawn, Is.True); Assert.That(target.CurrentHP, Is.Zero);
            Assert.That(deaths, Is.Zero); Assert.That(withdrawals, Is.EqualTo(1));
        }

        [TestCase("EnemyField")]
        [TestCase("AllyTeam")]
        public void Definition_RejectsNonEntityTargets(string target)
            => Assert.Throws<TableValidationException>(() => TableValidator.ValidateEffectRow("Test", 2, "Withdrawn",
                null, 0, null, target, -1, 1, null, false));
    }
}
