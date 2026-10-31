using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class StatusDeathActionsTests
    {
        private const string ID = "ST_DeathProbe", Dependency = "ST_DeathDependency", Effect = "STE_DeathDependencyDamage";
        private static void Configure(BattleFlowTestEnv env)
        {
            env.ConfigureStatus(ID, actionType: "OnDeath", actionEffectID2: Effect);
            env.ConfigureStatus(Dependency);
            env.DataManager.StatusEffectDict[Effect] = new StatusEffectData {
                StatusEffectID2 = Effect, EffectType = "Damage", Element = "Physical", DamageType = "Skill",
                Param1 = "1*ATK,0", TargetType = "Enemy", TargetSelect = "1", TargetConsecutive = 1 };
            env.CreateMinimalBattle(); env.CreateAlly(2); env.CreateEnemy(2);
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            BattleRandom.SetSource(new SystemBattleRandomSource(20261029));
        }
        private static StatusInstance Add(BattleFlowTestEnv env, BattleEntity host, string id = ID)
            => host.AddStatus(id, host, 0, 5, 0, env.DataManager.StatusMainDict[id]);
        private static void Kill(BattleEntity target, BattleEntity source)
            => target.TakeDamage(100000, DamageSourceInfo.Create(source, ReactionSourceKind.EnemySkill, "SK_Test", "SE_Test"));

        [UnityTest] public IEnumerator EnemyDeath_UsesForeignEffectDefinition_KeepsDependenciesUntilFinished()
            => HookTestEnv.AwaitTask(EnemyDeath);
        private static async Task EnemyDeath()
        {
            using var env = new BattleFlowTestEnv(); Configure(env);
            var owner = env.Enemy.Entity;
            var action = Add(env, owner); var dependency = Add(env, owner, Dependency);
            var external = env.Manager.Field.GetSlot(BattleSide.Enemy, 2)
                .AddStatus(Dependency, owner, 0, 5, 0, env.DataManager.StatusMainDict[Dependency]);
            int hits = 0;
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) => {
                if (fact.Source.SourceEffectID != Effect) return;
                hits++;
                Assert.That(owner.IsAlive, Is.False);
                Assert.That(action.IsActive && dependency.IsActive, Is.True);
                Assert.That(fact.Source.SourceStatusID, Is.EqualTo(ID), "借效果定义不更换来源状态。");
            }, 0);
            await BattleExecution.Run(async () => { Kill(owner, env.Ally.Entity); await BattleExecution.Flush(); });
            Assert.That(hits, Is.EqualTo(1));
            Assert.That(action.IsActive || dependency.IsActive, Is.False);
            Assert.That(external.IsActive, Is.True, "敌人死亡不按Caster清掉外部场地状态。");
            await BattleExecution.Run(async () => { Kill(owner, env.Ally.Entity); await BattleExecution.Flush(); });
            Assert.That(hits, Is.EqualTo(1), "对尸体重复伤害不再触发死亡。");
        }

        [UnityTest] public IEnumerator CharacterAndEquipmentDeath_ExecuteBeforeCharacterCleanup()
            => HookTestEnv.AwaitTask(CharacterDeath);
        private static async Task CharacterDeath()
        {
            foreach (bool equipment in new[] { false, true })
            {
                using var env = new BattleFlowTestEnv(); Configure(env);
                var owner = env.Ally.Entity;
                env.DataManager.CharacterStatusIDs[owner.EntityID] = new HashSet<string> { ID, Dependency };
                var action = Add(env, owner); var dependency = Add(env, owner, Dependency);
                if (equipment) action.EquipmentSource = new EquipmentEffectSource(owner);
                int hits = 0;
                using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) => {
                    if (fact.Source.SourceEffectID != Effect) return;
                    hits++; Assert.That(action.IsActive && dependency.IsActive, Is.True);
                }, 0);
                await BattleExecution.Run(async () => { Kill(owner, env.Enemy.Entity); await BattleExecution.Flush(); });
                Assert.That(hits, Is.EqualTo(1));
                Assert.That(action.IsActive || dependency.IsActive, Is.False);
                Assert.That(EquipmentStatusLifetime.CanExecute(action), Is.EqualTo(!equipment));
            }
        }

        [UnityTest] public IEnumerator FieldAndTeam_UseDeadOccupantAndEachMemberOfSameBatch()
            => HookTestEnv.AwaitTask(Hosts);
        private static async Task Hosts()
        {
            using var env = new BattleFlowTestEnv(); Configure(env);
            var first = env.Enemy.Entity; var second = env.Manager.GetEntityByPosition(BattleSide.Enemy, 2);
            var caster = env.CreateEnemy(3).Entity;
            var main = env.DataManager.StatusMainDict[ID];
            var field = first.Position.AddStatus(ID, caster, 0, 5, 0, main);
            var emptyField = env.Manager.Field.GetSlot(BattleSide.Enemy, 5).AddStatus(ID, caster, 0, 5, 0, main);
            var team = env.Manager.EnemyTeam.AddStatus(ID, caster, 0, 5, 0, main);
            var wrongTeam = env.Manager.AllyTeam.AddStatus(ID, env.Ally.Entity, 0, 3, 0, main);
            await KillBatch(env, first, second);
            Assert.That(field.ActionRuntime.Values.Sum(s => s.LifeTriggerCount), Is.EqualTo(1));
            Assert.That(team.ActionRuntime.Values.Sum(s => s.LifeTriggerCount), Is.EqualTo(2));
            Assert.That(emptyField.ActionRuntime == null || emptyField.ActionRuntime.Count == 0, Is.True);
            Assert.That(wrongTeam.ActionRuntime == null || wrongTeam.ActionRuntime.Count == 0, Is.True);
            Assert.That(field.IsActive && team.IsActive, Is.True, "成员死亡不等于场地或队伍状态自动销毁。");
        }
        private static Task KillBatch(BattleFlowTestEnv env, params BattleEntity[] targets)
            => BattleExecution.Run(async () => {
                using (var batch = new HitBatch())
                    foreach (var dead in targets) batch.Resolve(new HitRequest {
                        Target = dead, DamageBeforeShield = 100000, Element = "Physical",
                        Source = DamageSourceInfo.Create(env.Ally.Entity, ReactionSourceKind.EnemySkill, "SK_Test", "SE_Test") });
                await BattleExecution.Flush();
            });

        [UnityTest] public IEnumerator TeamActionLimit_IsSharedAcrossSimultaneousDeaths()
            => HookTestEnv.AwaitTask(TeamLimit);
        private static async Task TeamLimit()
        {
            using var env = new BattleFlowTestEnv(); Configure(env);
            env.DataManager.StatusActionDict[ID][0].MaxTimePerTurn = 1;
            var team = env.Manager.EnemyTeam.AddStatus(ID, env.Enemy.Entity, 0, 5, 0, env.DataManager.StatusMainDict[ID]);
            await KillBatch(env, env.Enemy.Entity, env.Manager.GetEntityByPosition(BattleSide.Enemy, 2));
            Assert.That(team.ActionRuntime.Values.Sum(s => s.LifeTriggerCount), Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator ChainDeath_PreservesOuterDependenciesAndFinishesBeforeCleanup()
            => HookTestEnv.AwaitTask(ChainDeath);
        private static async Task ChainDeath()
        {
            using var env = new BattleFlowTestEnv(); Configure(env);
            env.DataManager.StatusEffectDict[Effect].TargetSelect = "-1";
            env.Ally.Entity.CurrentHP = 1;
            var enemyAction = Add(env, env.Enemy.Entity);
            var dependency = Add(env, env.Enemy.Entity, Dependency);
            var allyAction = Add(env, env.Ally.Entity);
            env.DataManager.CharacterStatusIDs[env.Ally.Entity.EntityID] = new HashSet<string> { ID, Dependency };
            int chainHits = 0;
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) => {
                if (fact.Source.SourceEffectID != Effect || fact.Source.SourceEntity != env.Ally.Entity) return;
                chainHits++;
                Assert.That(dependency.IsActive && enemyAction.IsActive && allyAction.IsActive, Is.True);
                Assert.That(env.Manager.IsBattleOver, Is.False);
            }, 0);
            await BattleExecution.Run(async () => { Kill(env.Enemy.Entity, env.Ally.Entity); await BattleExecution.Flush(); });
            Assert.That(chainHits, Is.GreaterThan(0));
            Assert.That(dependency.IsActive || enemyAction.IsActive || allyAction.IsActive, Is.False);
        }

        [UnityTest] public IEnumerator RemovedBeforeDeath_AndOrdinaryEntrypointsCannotTriggerOnDeath()
            => HookTestEnv.AwaitTask(InvalidTriggers);
        private static async Task InvalidTriggers()
        {
            using var env = new BattleFlowTestEnv(); Configure(env);
            var status = Add(env, env.Ally.Entity); var action = env.DataManager.StatusActionDict[ID][0];
            Assert.That(env.Ally.TryExecuteEventStatusAction(status, action, env.Ally.Entity, new ScriptHookContext()), Is.False);
            await env.Ally.ExecuteStatusActionsTask(status, new List<StatusActionData> { action }, env.Ally.Entity, "OnDeath");
            Assert.That(status.LifeTriggerCount, Is.Zero);
            await BattleExecution.Run(async () => {
                env.Ally.Entity.RemoveStatusInstance(status, "Removed");
                Kill(env.Ally.Entity, env.Enemy.Entity); await BattleExecution.Flush();
            });
            Assert.That(status.LifeTriggerCount, Is.Zero);
        }
    }
}
