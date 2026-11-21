using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    // 正式表只由DataManager从StreamingAssets/Data加载；这些用例留给发布后的Unity验收。
    public class Enemy2067To2070IntegrationTests
    {
        [UnityTest]
        public IEnumerator LastMimic_DropsOceanidToZero_VictoryCancelsLethalDeathExplosion()
            => HookTestEnv.AwaitTask(LastMimicVictoryAsync);

        private async Task LastMimicVictoryAsync()
        {
            using var env = new BattleFlowTestEnv();
            env.CreateMinimalBattle(); env.DataManager.ReloadAllData();
            // 测试布阵：只有纯水精灵及其召唤物参与存活判断。
            env.Enemy.Entity.CurrentHP = 0f;
            env.Ally.Entity.TotalHP = env.Ally.Entity.CurrentHP = 1000000f;
            await BattleExecution.Run(async () => {
                BattleRandom.SetSource(new SystemBattleRandomSource(207012));
                var oceanid = env.Manager.SummonEnemy(2070, 1, 2);
                await BattleExecution.Flush();
                await oceanid.TakeEnemyTurnAsync();
                var mimics = env.Manager.Enemies.Select(c => c.Entity)
                    .Where(e => e.IsAlive && EnemyTraits.HasFamily(e, Enemy2067To2070Hooks.MimicFamily)).ToList();
                async Task Kill(BattleEntity target)
                {
                    await TrueDamageExecutor.ExecuteAsync(new[] { target }, new[] { new HitData(1f, 0f) },
                        _ => target.GetFinalHP() * 100f,
                        DamageSourceInfo.Create(env.Ally.Entity, ReactionSourceKind.CharacterSkill, "SK_LastMimic", "SE_LastMimic"),
                        "SE_LastMimic", null);
                    await BattleExecution.Flush();
                }
                await Kill(mimics[0]);
                Assert.That(env.Manager.IsBattleOver, Is.False);
                oceanid.Entity.CurrentHP = oceanid.Entity.GetFinalHP() * .18f;
                env.Ally.Entity.CurrentHP = 1f; // 任意一发死亡爆炸都足以团灭。
                int explosions = 0, endings = 0;
                using var damage = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) => {
                    if (hit.Source.SourceEffectID == "STE_HydroMimic_Death") explosions++;
                }, long.MaxValue);
                using var ended = BattleEventHub.Subscribe<BattleEndedEventData>(BattleEventKind.BattleEnded,
                    (_, result) => { endings++; Assert.That(result.Victory, Is.True); }, long.MaxValue);
                await Kill(mimics[1]);
                Assert.That(env.Manager.IsBattleOver, Is.True);
                Assert.That(env.Manager.Victory, Is.True);
                Assert.That(env.Ally.Entity.CurrentHP, Is.EqualTo(1f));
                Assert.That(explosions, Is.Zero);
                Assert.That(endings, Is.EqualTo(1));
                Assert.That(env.Manager.Enemies.All(c => !c.Entity.IsAlive), Is.True);
                // 战斗已结束时直接发起另一段伤害同样不得落地。
                await TrueDamageExecutor.ExecuteAsync(new[] { env.Ally.Entity }, new[] { new HitData(1f, 0f) },
                    _ => 1000f, DamageSourceInfo.Create(mimics[1], ReactionSourceKind.StatusEffect, null, "SE_TooLate"),
                    "SE_TooLate", null);
                Assert.That(env.Ally.Entity.CurrentHP, Is.EqualTo(1f));
                Assert.That(endings, Is.EqualTo(1));
            });
        }

        [UnityTest]
        public IEnumerator Oceanid_SummonsDistinctFamilies_AndEachDeathCostsEighteenPercent()
            => HookTestEnv.AwaitTask(OceanidAsync);

        private async Task OceanidAsync()
        {
            using var env = new BattleFlowTestEnv();
            env.CreateMinimalBattle(); env.DataManager.ReloadAllData();
            await BattleExecution.Run(async () => {
                var random = new SystemBattleRandomSource(2070);
                BattleRandom.SetSource(random);
                var oceanid = env.Manager.SummonEnemy(2070, 1, 2);
                await BattleExecution.Flush();
                float full = oceanid.Entity.GetFinalHP();
                await oceanid.TakeEnemyTurnAsync();
                var mimics = env.Manager.Enemies.Select(c => c.Entity)
                    .Where(e => e.IsAlive && EnemyTraits.HasFamily(e, Enemy2067To2070Hooks.MimicFamily)).ToList();
                Assert.That(mimics.Count, Is.EqualTo(2));
                Assert.That(mimics.Select(e => e.EntityID).Distinct().Count(), Is.EqualTo(2));
                Assert.That(mimics.All(e => e.Summoner == oceanid.Entity), Is.True);
                Assert.That(random.Step, Is.EqualTo(5), "技能权重一次、两个种类和两个空位各抽取一次。");
                foreach (var mimic in mimics)
                    await TrueDamageExecutor.ExecuteAsync(new[] { mimic }, new[] { new HitData(1f, 0f) },
                        _ => mimic.GetFinalHP() * 100f,
                        DamageSourceInfo.Create(env.Ally.Entity, ReactionSourceKind.CharacterSkill, "SK_MimicTest", "SE_MimicTest"),
                        "SE_MimicTest", null);
                await BattleExecution.Flush();
                Assert.That(oceanid.Entity.CurrentHP, Is.EqualTo(full * .64f).Within(.001f));
            });
        }

        [UnityTest]
        public IEnumerator Wolf_ThirdMultiHitAwardsAfterSettlement_FourthConsumesAndClearsField()
            => HookTestEnv.AwaitTask(WolfAsync);

        private async Task WolfAsync()
        {
            using var env = new BattleFlowTestEnv();
            env.CreateMinimalBattle(); env.DataManager.ReloadAllData();
            await BattleExecution.Run(async () => {
                var wolf = env.Manager.SummonEnemy(2069, 1, 2).Entity;
                await BattleExecution.Flush();
                var actor = env.Ally.Entity;
                var skill = new SkillMainData { SkillID = 990000, SkillID2 = "SK_WolfTest", ActionType = "Normal" };
                var settle = typeof(BattleSpecialRules).GetMethod("CompleteSkillSettlement", BindingFlags.Static | BindingFlags.NonPublic);
                for (int action = 0; action < 4; action++)
                {
                    using var execution = BattleEventHub.BeginActionExecution();
                    using var scope = BattleSpecialRules.BeginSkillExecution(actor, skill);
                    if (action == 3)
                    {
                        var main = env.DataManager.StatusMainDict["ST_Andrius_Ground"];
                        actor.Position.AddStatus(main.StatusID2, wolf, 2, 6, 3, main);
                    }
                    for (int hit = 0; hit < 2; hit++)
                    {
                        using (var batch = new HitBatch()) batch.Resolve(new HitRequest {
                            Target = wolf, Element = "Physical", DamageBeforeShield = 0f,
                            Source = DamageSourceInfo.Create(actor, ReactionSourceKind.CharacterSkill, skill.SkillID2, "SE_WolfTest") });
                        await BattleExecution.Flush();
                        if (action == 2) Assert.That(actor.HasStatus(Enemy2067To2070Hooks.Approval), Is.False);
                    }
                    var completed = BattleEventHub.Publish(new ActionEventData(actor, skill.SkillID2, "Normal", true));
                    settle.Invoke(null, new object[] { actor, skill, completed });
                    await BattleExecution.Flush();
                    if (action == 2) Assert.That(actor.HasStatus(Enemy2067To2070Hooks.Approval), Is.True);
                    if (action == 3)
                    {
                        Assert.That(actor.HasStatus(Enemy2067To2070Hooks.Approval), Is.False);
                        Assert.That(actor.Position.StatusList.Any(s => s.StatusID2 == "ST_Andrius_Ground"), Is.False);
                        Assert.That(actor.GetStatusInstances(Enemy2067To2070Hooks.Trial).Count, Is.EqualTo(1));
                    }
                }
            });
        }

        [UnityTest]
        public IEnumerator Wolf_ThresholdGuardsLethalHit_AndReleasesForNextFormalAction()
            => HookTestEnv.AwaitTask(ThresholdAsync);

        private async Task ThresholdAsync()
        {
            using var env = new BattleFlowTestEnv();
            env.CreateMinimalBattle(); env.DataManager.ReloadAllData();
            await BattleExecution.Run(async () => {
                var wolf = env.Manager.SummonEnemy(2069, 1, 2);
                await BattleExecution.Flush();
                float full = wolf.Entity.GetFinalHP();
                await TrueDamageExecutor.ExecuteAsync(new[] { wolf.Entity }, new[] { new HitData(1f, 0f) }, _ => full * 100,
                    DamageSourceInfo.Create(env.Ally.Entity, ReactionSourceKind.CharacterSkill, "SK_WolfThreshold", "SE_WolfThreshold"),
                    "SE_WolfThreshold", null);
                await BattleExecution.Flush();
                Assert.That(wolf.Entity.CurrentHP, Is.EqualTo(full * .55f).Within(.001f));
                Assert.That(wolf.CurrentPatternID, Is.EqualTo(2));
                Assert.That(wolf.Entity.HasStatus(Enemy2067To2070Hooks.Interlude), Is.True);
                await wolf.TakeEnemyTurnAsync();
                Assert.That(wolf.Entity.HasStatus(Enemy2067To2070Hooks.Interlude), Is.False);
            });
        }
    }
}
