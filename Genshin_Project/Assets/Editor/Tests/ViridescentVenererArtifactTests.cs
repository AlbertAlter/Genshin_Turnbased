using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class ViridescentVenererArtifactTests
    {
        [UnityTest]
        public IEnumerator RealSwirl_DebuffsDamagedEnemy_NotAdjacentPropagationOnlyEnemy()
            => HookTestEnv.AwaitTask(RealSwirl_DebuffsDamagedEnemyAsync);

        private async Task RealSwirl_DebuffsDamagedEnemyAsync()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var adjacent = env.CreateEnemy(2).Entity;
                var far = env.CreateEnemy(4).Entity;
                Configure(env);
                var owner = env.Ally.Entity;
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Enemy.Entity.ApplyAura("Pyro", 1f, owner.EntityID, "seed", "seed");
                float before = env.Enemy.Entity.CurrentHP;
                var swirl = SwirlReactionHandler.ResolveBatch(new[] { new ReactionContext
                {
                    SourceEntity = owner, SourceKind = ReactionSourceKind.CharacterSkill,
                    SourceSkillID = "SK_Test_Wind", SourceEffectID = "SE_Test_Wind",
                    Target = env.Enemy.Entity, AttackElement = "Anemo", AttackAmount = 1f,
                    DamageType = "Skill", ApplicationPhase = 2,
                    EffectExecutionID = BattleEventHub.NextIdentity(), LogicalHitID = BattleEventHub.NextIdentity()
                } });
                StatusOnHitHookSystem.NotifyReactions(swirl.Reaction.TriggeredReactions);
                await BattleExecution.Flush();

                Assert.That(env.Enemy.Entity.CurrentHP, Is.LessThan(before));
                Assert.That(adjacent.GetAura("Pyro")?.AuraAmount, Is.GreaterThan(0f));
                Assert.That(adjacent.CurrentHP, Is.EqualTo(adjacent.TotalHP), "只接受传播元素，没有扩散伤害");
                Assert.That(env.Enemy.Entity.GetStatusResBonus("Pyro"), Is.EqualTo(-.4f).Within(.00001f));
                Assert.That(env.Enemy.Entity.GetStatusResBonus("Hydro"), Is.Zero);
                Assert.That(env.Enemy.Entity.GetStatusInstances("ST_Artifact_451905"), Has.Count.EqualTo(1),
                    "反应声明与伤害声明不能各施加一次");
                Assert.That(adjacent.HasStatus("ST_Artifact_451905"), Is.False);
                Assert.That(far.HasStatus("ST_Artifact_451905"), Is.False);
            }
        }

        [UnityTest]
        public IEnumerator DamageFacts_IsolateTargetsElementsAndSources_AndRejectZeroDamageOrReplays()
            => HookTestEnv.AwaitTask(DamageFacts_IsolateTargetsElementsAndSourcesAsync);

        private async Task DamageFacts_IsolateTargetsElementsAndSourcesAsync()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var first = env.Enemy.Entity;
                var second = env.CreateEnemy(2).Entity; // 同EnemyID，不可按配置ID合并。
                var shielded = env.CreateEnemy(3).Entity;
                var teammate = env.CreateAlly(2, false).Entity;
                var root = Configure(env);
                var owner = env.Ally.Entity;
                DamageResolvedEvent original;
                using (var batch = new HitBatch())
                {
                    original = batch.Resolve(Request(owner, first, "Pyro", 10f)).DamageEvent;
                    batch.Resolve(Request(owner, second, "Hydro", 10f));
                    batch.Resolve(Request(owner, shielded, "Pyro", 0f));
                }
                await BattleExecution.Flush();
                var originalDebuff = first.GetStatus("ST_Artifact_451905");
                Assert.That(original.HitLanded, Is.False, "派生反应不是普通攻击命中，但实际扣血仍可触发");
                Assert.That(originalDebuff?.Caster, Is.SameAs(owner));
                Assert.That(second.HasStatus("ST_Artifact_451906"), Is.True);
                Assert.That(second.HasStatus("ST_Artifact_451905"), Is.False);
                Assert.That(shielded.HasStatus("ST_Artifact_451905"), Is.False);

                BattleEventHub.Publish(new DamageEventData(original), original);
                await BattleExecution.Flush();
                Assert.That(first.GetStatus("ST_Artifact_451905"), Is.SameAs(originalDebuff), "同声明重放不能刷新");
                first.GetStatus("ST_Artifact_451905").RemainingPhaseCount = 1;
                using (var batch = new HitBatch()) batch.Resolve(Request(owner, first, "Pyro", 10f));
                await BattleExecution.Flush();
                Assert.That(first.GetStatus("ST_Artifact_451905"), Is.Not.SameAs(originalDebuff));
                Assert.That(first.GetStatus("ST_Artifact_451905").RemainingPhaseCount, Is.EqualTo(3));

                shielded.AddShield(100f, "Geo", 2f);
                using (var batch = new HitBatch()) batch.Resolve(Request(owner, shielded, "Electro", 10f));
                await BattleExecution.Flush();
                Assert.That(shielded.CurrentHP, Is.EqualTo(shielded.TotalHP));
                Assert.That(shielded.HasStatus("ST_Artifact_451907"), Is.True, "已由盾承受的扩散伤害仍是伤害事实");

                using (var batch = new HitBatch())
                {
                    var direct = Request(owner, second, "Pyro", 10f);
                    direct.Source = DamageSourceInfo.Create(owner, ReactionSourceKind.CharacterSkill, "skill", "effect");
                    direct.DamageType = "Skill";
                    batch.Resolve(direct);
                    batch.Resolve(Request(teammate, second, "Pyro", 10f));
                    var overloaded = Request(owner, second, "Pyro", 10f);
                    overloaded.Source.ReactionType = ReactionType.Overloaded;
                    batch.Resolve(overloaded);
                }
                StatusOnHitHookSystem.NotifyReactions(new[] { new ReactionOccurrence
                {
                    Type = ReactionType.Swirl, SourceEntity = owner, Target = second,
                    InvolvedElements = new List<string> { "Anemo", "Cryo" }
                } });
                await BattleExecution.Flush();
                Assert.That(second.HasStatus("ST_Artifact_451905"), Is.False, "直伤、队友伤害和其他派生反应不能冒充扩散");
                Assert.That(second.HasStatus("ST_Artifact_451908"), Is.False, "仅有反应声明而没有伤害不施加");
                owner.RemoveStatusInstance(root);
                using (var batch = new HitBatch()) batch.Resolve(Request(owner, second, "Cryo", 10f));
                await BattleExecution.Flush();
                Assert.That(second.HasStatus("ST_Artifact_451908"), Is.False, "原初始化实例移除即停止监听");
            }
        }

        private static HitRequest Request(BattleEntity owner, BattleEntity target, string element, float damage)
            => new HitRequest
            {
                Target = target, Element = element, DamageBeforeShield = damage, DamageType = "Reaction",
                Source = DamageSourceInfo.Create(owner, ReactionSourceKind.DerivedReaction,
                    "SK_Test_Wind", "SE_Test_Wind", ReactionType.Swirl, false)
            };

        private static StatusInstance Configure(BattleFlowTestEnv env)
        {
            var root = new StatusMainData { StatusID = 451904, StatusID2 = ViridescentVenererArtifactHooks.StatusID,
                MaxStack = 1, WhenMax = "1" };
            env.DataManager.StatusMainDict[root.StatusID2] = root;
            var actions = new List<StatusActionData>();
            string[] elements = { "Pyro", "Hydro", "Electro", "Cryo" };
            for (int index = 0; index < elements.Length; index++)
            {
                int id = 451905 + index;
                var main = new StatusMainData { StatusID = id, StatusID2 = "ST_Artifact_" + id,
                    StatusType = "Debuff", MultiplierPart = "ResBonus", ApplyElementType = elements[index],
                    MaxCount = 1, MaxStack = 1, WhenMax = "0" };
                env.DataManager.StatusMainDict[main.StatusID2] = main;
                string applyID = "STE_Artifact_4519040" + (index + 1);
                env.DataManager.StatusEffectDict[applyID] = new StatusEffectData
                {
                    StatusEffectID = 45190401 + index, StatusEffectID2 = applyID, EffectIndex = index + 1,
                    EffectType = "ApplyStatus", Param1 = main.StatusID2, Duration = 3, AddInPhase = 1,
                    TargetType = "Enemy", TargetSelect = "-1", TargetOverride = "Hook(45190410)"
                };
                env.DataManager.StatusEffectDict["STE_Artifact_" + id + "01"] = new StatusEffectData
                { StatusEffectID = id * 100 + 1, EffectType = "Buff", Param1 = "-0.4" };
                actions.Add(new StatusActionData { StatusID2 = root.StatusID2, ActionType = "OnTrigger",
                    Param1 = applyID, ReactionTriggered = "Swirl", ScriptHook = "Hook(4519040" + (index + 1) + ")" });
            }
            env.DataManager.StatusActionDict[root.StatusID2] = actions;
            return env.Ally.Entity.AddStatus(root.StatusID2, env.Ally.Entity, 0, 1, 0, root);
        }
    }
}
