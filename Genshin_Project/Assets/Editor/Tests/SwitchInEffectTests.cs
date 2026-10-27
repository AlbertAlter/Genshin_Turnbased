using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class SwitchInEffectTests
    {
        [TestCase("Self", false), TestCase("Enemy", true), TestCase("Allies", true)]
        [TestCase("EnemyField", true), TestCase("AllyTeam", true), TestCase("", true)]
        public void Validation_OnlySupportsSelf(string target, bool invalid)
        {
            TestDelegate validate = () => TableValidator.ValidateEffectRow("SkillsEffect", 2,
                "SwitchIn", "", 0, "", target, 0, 0, "", false);
            if (invalid) Assert.Throws<TableValidationException>(validate); else Assert.DoesNotThrow(validate);
            Assert.Throws<TableValidationException>(() => TableValidator.ValidateEffectRow("EnemySkill_Effect", 2,
                "SwitchIn", "", 0, "", "Self", 0, 0, "", true));
            Assert.Throws<TableValidationException>(() => TableValidator.ValidateSwitchIn("SkillsEffect", 2,
                "5", "", "", "", 0, 0));
        }

        private static StatusMainData Watch(BattleFlowTestEnv env, BattleEntity owner, int id, string hook)
        {
            string key = "ST_SwitchInProbe_" + id, effect = "STE_SwitchInProbe_" + id;
            var main = new StatusMainData { StatusID = id, StatusID2 = key, MaxStack = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[key] = main;
            env.DataManager.StatusActionDict[key] = new List<StatusActionData>
            { new StatusActionData { StatusID = id, StatusID2 = key, ActionType = "OnTrigger", ScriptHook = hook, Param1 = effect } };
            env.DataManager.StatusEffectDict[effect] = new StatusEffectData
            { StatusEffectID = id * 100 + 1, StatusEffectID2 = effect, EffectType = "GainEnergy", Param1 = "1", Param2 = "Flat", TargetType = "Self" };
            owner.AddStatus(key, owner, 0, 2, 0, main); return main;
        }

        [UnityTest]
        public IEnumerator SkillInsideBusyExecution_CommitsFieldBeforeHooks_AndPaysNoSwitchAP()
            => HookTestEnv.AwaitTask(SkillAsync);
        private async Task SkillAsync()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(3, "SK_AutoEntry", "SE_AutoEntry", "", "", 10,
                effectType: "SwitchIn", targetType: "Self");
            env.DataManager.SkillEffectDict["SE_AutoEntry"].DamageType = "";
            env.CreateMinimalBattle(); var incoming = env.CreateAlly(2); incoming.Entity.EntityID = 1016;
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            env.Ally.Entity.CurrentEnergy = incoming.Entity.CurrentEnergy = 0;
            Watch(env, env.Ally.Entity, 49101, "SwitchOut(990001)");
            Watch(env, incoming.Entity, 49102, "SwitchIn(1016)");
            int switches = 0, payments = 0, starts = 0, completions = 0;
            using var sw = BattleEventHub.Subscribe<SwitchEventData>(BattleEventKind.SwitchResolved, (header, fact) =>
            {
                switches++; Assert.That(BattleExecution.IsBusy, Is.True);
                Assert.That(env.Manager.GetActiveAlly(), Is.SameAs(incoming));
                Assert.That(env.Ally.IsActive, Is.False); Assert.That(fact.Subject, Is.SameAs(incoming.Entity));
                Assert.That(fact.Outgoing, Is.SameAs(env.Ally.Entity)); Assert.That(fact.Reason, Is.EqualTo(BattleSwitchReason.SkillAutomatic));
                Assert.That(header.EffectExecutionId, Is.Not.Zero); Assert.That(header.ActionExecutionId, Is.Not.Zero);
            }, 0);
            using var ap = BattleEventHub.Subscribe<APSpentEventData>(BattleEventKind.APSpent, (_, fact) =>
            { payments++; Assert.That(fact.SpendKind, Is.EqualTo(APSpendKind.Skill)); Assert.That(fact.Amount, Is.EqualTo(10)); }, 0);
            using var start = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted, (_, __) => starts++, 0);
            using var end = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted, (_, __) => completions++, 0);
            await BattleExecution.Run(() => incoming.ExecuteSkillByIDAsync("SK_AutoEntry", 3, true));
            Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(90)); Assert.That(payments, Is.EqualTo(1));
            Assert.That(switches, Is.EqualTo(1)); Assert.That(starts, Is.EqualTo(1)); Assert.That(completions, Is.EqualTo(1));
            Assert.That(env.Ally.Entity.CurrentEnergy, Is.EqualTo(1f)); Assert.That(incoming.Entity.CurrentEnergy, Is.EqualTo(1f));
            Assert.That(SwitchInEffect.Execute(new[] { incoming.Entity }), Is.False, "重复切入当前前台不发声明");
            Assert.That(switches, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator StatusSelf_UsesItsRealHost_WithoutAnExtraActionOrPayment()
            => HookTestEnv.AwaitTask(StatusAsync);
        private async Task StatusAsync()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); var incoming = env.CreateAlly(2);
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            var main = new StatusMainData { StatusID = 49103, StatusID2 = "ST_AutoEntry", MaxStack = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            var action = new StatusActionData { StatusID = main.StatusID, StatusID2 = main.StatusID2, ActionType = "OnItsTurn", Param1 = "STE_AutoEntry" };
            env.DataManager.StatusActionDict[main.StatusID2] = new List<StatusActionData> { action };
            env.DataManager.StatusEffectDict["STE_AutoEntry"] = new StatusEffectData
            { StatusEffectID = 4910301, StatusEffectID2 = "STE_AutoEntry", EffectType = "SwitchIn", TargetType = "Self" };
            var status = incoming.Entity.AddStatus(main.StatusID2, env.Ally.Entity, 1, 3, 3, main);
            int switches = 0, extra = 0;
            using var sw = BattleEventHub.Subscribe<SwitchEventData>(BattleEventKind.SwitchResolved, (_, fact) =>
            { switches++; Assert.That(fact.Subject, Is.SameAs(incoming.Entity)); Assert.That(fact.Reason, Is.EqualTo(BattleSwitchReason.SkillAutomatic)); }, 0);
            using var ap = BattleEventHub.Subscribe<APSpentEventData>(BattleEventKind.APSpent, (_, __) => extra++, 0);
            using var start = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted, (_, __) => extra++, 0);
            await BattleExecution.Run(() => env.Ally.ExecuteStatusActionsTask(status, new List<StatusActionData> { action }, incoming.Entity, "OnItsTurn"));
            Assert.That(switches, Is.EqualTo(1)); Assert.That(extra, Is.Zero); Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(100));
            incoming.Entity.CurrentHP = 0;
            Assert.That(SwitchInEffect.Execute(new[] { incoming.Entity }), Is.False);
            Assert.That(SwitchInEffect.Execute(new[] { env.Enemy.Entity }), Is.False);
            Assert.That(SwitchInEffect.Execute(new[] { env.Ally.Entity, incoming.Entity }), Is.False);
        }
    }
}
