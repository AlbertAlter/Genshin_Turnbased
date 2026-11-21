using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class StatusRemovalActionsTests
    {
        private const string ID = "ST_RemovalProbe", Energy = "STE_RemovalEnergy";
        private static void Setup(BattleFlowTestEnv env)
        {
            env.ConfigureStatus(ID, actionType: "OnDestroy", actionEffectID2: Energy);
            env.DataManager.StatusEffectDict[Energy] = new StatusEffectData {
                StatusEffectID2 = Energy, EffectType = "GainEnergy", Param1 = "1", Param2 = "Flat", TargetType = "Self" };
            env.CreateMinimalBattle(); env.Manager.StartBattle();
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            env.Ally.Entity.MaxEnergy = 100; env.Ally.Entity.CurrentEnergy = 0;
        }
        private static StatusInstance Add(BattleFlowTestEnv env, string id = ID)
            => env.Ally.Entity.AddStatus(id, env.Ally.Entity, 1, 3, 0, env.DataManager.StatusMainDict[id]);
        private static Task Remove(BattleEntity host, StatusInstance status, string reason = "Removed")
            => BattleExecution.Run(async () => { host.RemoveStatusInstance(status, reason); await BattleExecution.Flush(); });

        private static StatusInstance Aim(BattleFlowTestEnv env, BattleEntity owner, int position, string type = "Charge")
        {
            const string aim = "ST_AimProbe", charge = "ST_ChargeProbe";
            env.ConfigureStatus(aim); env.ConfigureStatus(charge);
            var main = env.DataManager.StatusMainDict[aim]; main.StatusID = 4900101;
            var child = env.DataManager.StatusMainDict[charge]; child.StatusID = 4900102;
            child.StatusType = type; child.MaxStack = 1; child.WhenMax = "1";
            env.DataManager.StatusEffectDict["STE_AimBind"] = new StatusEffectData {
                StatusEffectID = 490010101, StatusEffectID2 = "STE_AimBind", EffectType = "BindStatus",
                Param1 = charge, TargetType = "Allies", TargetOverride = "ChargeCaster()" };
            env.DataManager.StatusActionDict[aim] = new List<StatusActionData> {
                new StatusActionData { StatusID2 = aim, ActionType = "OnEnd", Param1 = Energy } };
            return env.Manager.Field.GetSlot(BattleTargetResolver.Opposite(owner.Side), position)
                .AddStatus(aim, owner, 1, 3, 0, main);
        }

        private static void Control(BattleFlowTestEnv env, BattleEntity owner)
        {
            const string id = "ST_ControlProbe"; env.ConfigureStatus(id);
            var main = env.DataManager.StatusMainDict[id]; main.StatusType = "Control";
            owner.AddStatus(id, owner, 1, 3, 0, main);
        }

        [UnityTest] public IEnumerator Control_CancelsOnlyItsChargeAndExactAim_ForBothSides()
            => HookTestEnv.AwaitTask(CancelAim);
        private static async Task CancelAim()
        {
            foreach (bool enemy in new[] { false, true })
            {
                using var env = new BattleFlowTestEnv(); Setup(env);
                var owner = enemy ? env.Enemy.Entity : env.Ally.Entity;
                var other = enemy ? env.CreateEnemy(2).Entity : env.CreateAlly(2).Entity;
                var aim = Aim(env, owner, 1); var otherAim = Aim(env, other, 2);
                var charge = owner.GetStatus("ST_ChargeProbe");
                Assert.That(charge.BindingParent, Is.SameAs(aim));
                await BattleExecution.Run(async () => { Control(env, owner); await BattleExecution.Flush(); });
                Assert.That(charge.IsActive || aim.IsActive, Is.False);
                Assert.That(otherAim.IsActive && other.HasStatus("ST_ChargeProbe"), Is.True);
                Assert.That(env.Ally.Entity.CurrentEnergy, Is.Zero, "打断不执行瞄准标记OnEnd。");
            }
        }

        [Test] public void RemovingChildNormally_DoesNotCancelParent_AndControlIgnoresOrdinaryBindings()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            var aim = Aim(env, env.Ally.Entity, 1);
            env.Ally.Entity.RemoveStatusInstance(env.Ally.Entity.GetStatus("ST_ChargeProbe"));
            Assert.That(aim.IsActive, Is.True);
            var ordinary = Aim(env, env.Ally.Entity, 2, "");
            Control(env, env.Ally.Entity);
            Assert.That(ordinary.IsActive && env.Ally.Entity.HasStatus("ST_ChargeProbe"), Is.True);
        }

        [TestCase(false), TestCase(true)] public void Control_CancelsStandaloneChargeWithoutParent(bool enemy)
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            const string id = "ST_StandaloneCharge"; env.ConfigureStatus(id);
            var main = env.DataManager.StatusMainDict[id]; main.StatusType = "Charge";
            var owner = enemy ? env.Enemy.Entity : env.Ally.Entity;
            var charge = owner.AddStatus(id, owner, 1, 3, 0, main);
            Assert.That(charge.BindingParent, Is.Null);
            Control(env, owner);
            Assert.That(charge.IsActive, Is.False);
        }

        [UnityTest] public IEnumerator RemovedInstance_RunsOnce_AndDoesNotRunNaturalEnd()
            => HookTestEnv.AwaitTask(Removed);
        private static async Task Removed()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            env.DataManager.StatusActionDict[ID].Add(new StatusActionData { StatusID2 = ID, ActionType = "OnEnd", Param1 = Energy });
            var status = Add(env);
            await Remove(env.Ally.Entity, status); await Remove(env.Ally.Entity, status);
            Assert.That(env.Ally.Entity.CurrentEnergy, Is.EqualTo(1));
            Assert.That(status.IsActive, Is.False);
            Assert.That(env.Ally.Entity.HasStatus(ID), Is.False);
        }

        [Test] public void NaturalExpiry_AndSelfRemovalDuringOnEnd_DoNotTriggerDestroy()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            env.DataManager.StatusEffectDict["STE_EndSelfRemove"] = new StatusEffectData {
                StatusEffectID2 = "STE_EndSelfRemove", EffectType = "RemoveStatus", Param1 = ID, TargetType = "Self" };
            env.DataManager.StatusActionDict[ID].Add(new StatusActionData { StatusID2 = ID, ActionType = "OnEnd",
                Param1 = "STE_EndSelfRemove;" + Energy });
            Add(env);
            env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);
            Assert.That(env.Ally.Entity.CurrentEnergy, Is.EqualTo(1), "OnEnd的自移除不能再触发OnDestroy。");
            Assert.That(env.Ally.Entity.HasStatus(ID), Is.False);
        }

        [Test] public void ExpiringParent_DestroysBoundChild_OnlyChildRunsDestroy()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            const string childID = "ST_RemovalChild";
            env.ConfigureStatus(childID, actionType: "OnDestroy", actionEffectID2: Energy);
            var parent = Add(env); var child = Add(env, childID); parent.BoundStatuses.Add(child);
            env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);
            Assert.That(env.Ally.Entity.CurrentEnergy, Is.EqualTo(1));
            Assert.That(parent.IsActive || child.IsActive, Is.False);
        }

        [UnityTest] public IEnumerator Replacement_FieldAndTeamHosts_UseTheSameEventPath()
            => HookTestEnv.AwaitTask(Hosts);
        private static async Task Hosts()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            var main = env.DataManager.StatusMainDict[ID]; main.MaxStack = 1; main.WhenMax = "0";
            var old = Add(env);
            await BattleExecution.Run(async () => { Add(env); await BattleExecution.Flush(); });
            Assert.That(old.IsActive, Is.False); Assert.That(env.Ally.Entity.CurrentEnergy, Is.EqualTo(1));
            var field = new FieldPosition(null, BattleSide.Enemy, 1);
            using var team = new BattleTeamStatusHost(BattleSide.Ally);
            var a = field.AddStatus(ID, env.Ally.Entity, 2, 3, 0, main);
            var b = team.AddStatus(ID, env.Ally.Entity, 2, 3, 0, main);
            await BattleExecution.Run(async () => {
                field.RemoveStatusInstance(a, "ShieldBroken"); team.RemoveStatusInstance(b);
                await BattleExecution.Flush();
            });
            Assert.That(env.Ally.Entity.CurrentEnergy, Is.EqualTo(3));
        }

        [UnityTest] public IEnumerator DeadSource_AndTeardownDoNotExecuteDestroyEffects()
            => HookTestEnv.AwaitTask(Lifetime);
        private static async Task Lifetime()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            var status = Add(env); env.Ally.Entity.CurrentHP = 0;
            await Remove(env.Ally.Entity, status, "CharacterDied");
            Assert.That(env.Ally.Entity.CurrentEnergy, Is.Zero);
            env.Ally.Entity.CurrentHP = 100;
            using var team = new BattleTeamStatusHost(BattleSide.Ally);
            var root = team.AddStatus(ID, env.Ally.Entity, 2, 3, 0, env.DataManager.StatusMainDict[ID]);
            var child = Add(env); root.BoundStatuses.Add(child);
            await BattleExecution.Run(async () => { team.Dispose(); await BattleExecution.Flush(); });
            Assert.That(child.IsActive, Is.False);
            Assert.That(env.Ally.Entity.CurrentEnergy, Is.Zero, "容器释放中的绑定清理也不能产生OnDestroy行动。");
        }

        [UnityTest] public IEnumerator EnemyOwnedStatus_ExecutesConfiguredApplyStatusAfterRemoval()
            => HookTestEnv.AwaitTask(EnemyOwner);
        private static async Task EnemyOwner()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            const string marker = "ST_RemovalResult"; env.ConfigureStatus(marker);
            env.DataManager.StatusEffectDict[Energy] = new StatusEffectData { StatusEffectID2 = Energy,
                EffectType = "ApplyStatus", Param1 = marker, TargetType = "Self", Duration = 2, AddInPhase = 5 };
            var status = env.Enemy.Entity.AddStatus(ID, env.Enemy.Entity, 2, 5, 0, env.DataManager.StatusMainDict[ID]);
            await Remove(env.Enemy.Entity, status);
            Assert.That(env.Enemy.Entity.HasStatus(marker), Is.True);
            Assert.That(env.Enemy.Entity.HasStatus(ID), Is.False);
        }
    }
}
