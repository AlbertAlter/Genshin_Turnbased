using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class ArchaicPetraArtifactTests
    {
        [Test]
        public void ShieldGranted_ReobtainingIdenticalShieldIsNotLostWhenPoolHasNoNetChange()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var grants = new List<ShieldGrantedEventData>();
                int changes = 0;
                using (BattleEventHub.Subscribe<ShieldGrantedEventData>(BattleEventKind.ShieldGranted,
                    (header, data) => grants.Add(data), 0))
                using (BattleEventHub.Subscribe<ShieldChangedEventData>(BattleEventKind.ShieldChanged,
                    (header, data) => changes++, 0))
                {
                    env.Ally.Entity.AddOrReplaceCrystallizeShield(100f, "Pyro", 2, 2);
                    env.Ally.Entity.AddOrReplaceCrystallizeShield(100f, "Pyro", 2, 2);
                }
                Assert.That(grants, Has.Count.EqualTo(2));
                Assert.That(changes, Is.EqualTo(1));
                Assert.That(grants[0].Shield.ShieldId, Is.EqualTo(grants[1].Shield.ShieldId));
                Assert.That(grants[0].Shield.Element, Is.EqualTo("Pyro"));
            }
        }

        [UnityTest]
        public IEnumerator Petra_ReplacesWholeGroupAcrossWearers_RefreshesOnRegrant_AndKeepsCorrectDeathOwner()
            => HookTestEnv.AwaitTask(Petra_ReplacesWholeGroupAcrossWearersAsync);

        private async Task Petra_ReplacesWholeGroupAcrossWearersAsync()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var first = env.Ally.Entity;
                var second = env.CreateAlly(2, false).Entity;
                Configure(env);
                Add(env, first, 453102);
                Add(env, second, 453102);
                first.AddOrReplaceCrystallizeShield(100f, "Pyro", 2, 2);
                await BattleExecution.Flush();
                AssertGroup(env, first, "Pyro");

                int observed = 0;
                using (BattleEventHub.Subscribe<StatusEventData>(BattleEventKind.StatusRemoved, (header, status) =>
                {
                    if (status.StatusId != "ST_Artifact_453104" && status.StatusId != "ST_Artifact_453103") return;
                    observed++;
                    AssertGroup(env, second, "Hydro"); // 不应在移除一半、只重建一半时看到中间组。
                }, long.MaxValue))
                {
                    second.AddOrReplaceCrystallizeShield(100f, "Hydro", 2, 2);
                    await BattleExecution.Flush();
                }
                Assert.That(observed, Is.GreaterThan(0));
                AssertGroup(env, second, "Hydro");
                var old = second.GetStatus("ST_Artifact_453105");
                old.RemainingPhaseCount = 1;
                second.AddOrReplaceCrystallizeShield(100f, "Hydro", 2, 2);
                await BattleExecution.Flush();
                Assert.That(second.GetStatus("ST_Artifact_453105"), Is.Not.SameAs(old));
                Assert.That(second.GetStatus("ST_Artifact_453105").RemainingPhaseCount, Is.EqualTo(3));
                first.TakeDamage(first.TotalHP + 1f,
                    DamageSourceInfo.Create(env.Enemy.Entity, ReactionSourceKind.EnemySkill, "test", "test"));
                Assert.That(second.HasStatus("ST_Artifact_453105"), Is.True, "旧穿戴者阵亡不清新来源");
                second.TakeDamage(second.TotalHP + 1f,
                    DamageSourceInfo.Create(env.Enemy.Entity, ReactionSourceKind.EnemySkill, "test", "test"));
                Assert.That(first.HasStatus("ST_Artifact_453105"), Is.False);
                Assert.That(second.HasStatus("ST_Artifact_453105"), Is.False);
            }
        }

        private static void AssertGroup(BattleFlowTestEnv env, BattleEntity source, string element)
        {
            foreach (var ally in env.Manager.Allies)
            {
                var actor = ally.Entity;
                Assert.That(actor.GetStatus("ST_Artifact_453103")?.Caster, Is.SameAs(source));
                for (int id = 453104; id <= 453107; id++)
                {
                    var status = actor.GetStatus("ST_Artifact_" + id);
                    if (env.DataManager.StatusMainDict["ST_Artifact_" + id].ApplyElementType == element)
                        Assert.That(status?.Caster, Is.SameAs(source));
                    else Assert.That(status, Is.Null);
                }
            }
        }

        private static void Configure(BattleFlowTestEnv env)
        {
            env.DataManager.StatusMainDict[ArchaicPetraArtifactHooks.StatusID] = new StatusMainData
            { StatusID = 453102, StatusID2 = ArchaicPetraArtifactHooks.StatusID, MaxStack = 1, WhenMax = "1" };
            string[] elements = { "Geo", "Pyro", "Hydro", "Electro", "Cryo" };
            for (int index = 0; index < elements.Length; index++)
            {
                int id = 453103 + index;
                var main = new StatusMainData { StatusID = id, StatusID2 = "ST_Artifact_" + id,
                    StatusType = "Buff", MultiplierPart = "DMGBonus", ApplyElementType = elements[index],
                    MaxCount = 1, MaxStack = 1, WhenMax = "0" };
                env.DataManager.StatusMainDict[main.StatusID2] = main;
                env.DataManager.StatusEffectDict["STE_Test_PetraValue" + index] = new StatusEffectData
                { StatusEffectID = id * 100 + 1, EffectType = "Buff", Param1 = "0.35" };
            }
            var actions = new List<StatusActionData>();
            for (int element = 0; element < 4; element++)
            {
                var effects = new List<string>();
                for (int index = 0; index < 6; index++)
                {
                    string id = "STE_Test_Petra" + element + "_" + index;
                    bool remove = index < 4;
                    env.DataManager.StatusEffectDict[id] = new StatusEffectData
                    {
                        StatusEffectID2 = id, EffectType = remove ? "RemoveStatus" : "ApplyStatus",
                        Param1 = "ST_Artifact_" + (remove ? 453104 + index : index == 4 ? 453103 : 453104 + element),
                        Duration = remove ? 0 : 3, AddInPhase = 1, TargetType = "Allies", TargetSelect = "-1"
                    };
                    effects.Add(id);
                }
                actions.Add(new StatusActionData { StatusID2 = ArchaicPetraArtifactHooks.StatusID,
                    ActionType = "OnTrigger", ScriptHook = "Hook(4531020" + (element + 1) + ")",
                    Param1 = string.Join(";", effects) });
            }
            env.DataManager.StatusActionDict[ArchaicPetraArtifactHooks.StatusID] = actions;
        }
        private static StatusInstance Add(BattleFlowTestEnv env, BattleEntity actor, int id)
            => actor.AddStatus("ST_Artifact_" + id, actor, 0, 1, 0, env.DataManager.StatusMainDict["ST_Artifact_" + id]);
    }
}
