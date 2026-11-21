using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    // 正式配表验收：由DataManager读取已发布到StreamingAssets/Data的表。
    public class HypostasisIntegrationTests
    {
        [UnityTest]
        public IEnumerator LastExposure_RemainsEffectiveForWholeExecution_AndTargetsAreIndependent()
            => HookTestEnv.AwaitTask(ExposureAsync);

        private async Task ExposureAsync()
        {
            using var env = new BattleFlowTestEnv();
            env.CreateMinimalBattle();
            env.DataManager.ReloadAllData();
            await BattleExecution.Run(async () => {
                var wind = env.Manager.SummonEnemy(2064, 1, 2).Entity;
                var electro = env.Manager.SummonEnemy(2065, 1, 3).Entity;
                await BattleExecution.Flush();
                CountEffect.Execute(HypostasisPublicStates.Exposure, "1", wind, new[] { wind }, targetType: "Self");
                var reduction = new StatusMainData { StatusID = 991234, StatusID2 = "ST_TestOtherReduction", MultiplierPart = "DMGTaken" };
                env.DataManager.StatusMainDict[reduction.StatusID2] = reduction;
                env.DataManager.StatusEffectDict["STE_TestOtherReduction"] = new StatusEffectData {
                    StatusEffectID = 99123401, StatusEffectID2 = "STE_TestOtherReduction", EffectType = "Buff", Param1 = "-.05" };
                wind.AddStatus(reduction.StatusID2, wind, 0, 5, 0, reduction);

                async Task<float> Hit(BattleEntity target, bool countsAsHit = true)
                {
                    HitResolution result;
                    using (var batch = new HitBatch()) result = batch.Resolve(new HitRequest {
                        Target = target, Element = "Physical", DamageBeforeShield = countsAsHit ? 0f : 1f,
                        Source = DamageSourceInfo.Create(env.Ally.Entity,
                            countsAsHit ? ReactionSourceKind.CharacterSkill : ReactionSourceKind.DerivedReaction,
                            "SK_TestExposure", "SE_TestExposure", countsAsHit: countsAsHit) });
                    await BattleExecution.Flush();
                    return result.DMGTakenMultiplier;
                }

                using (BattleResolutionContext.BeginSkillAction(true))
                using (BattleResolutionContext.BeginEffect())
                {
                    Assert.That(await Hit(wind), Is.EqualTo(.95f).Within(.0001f));
                    Assert.That(wind.GetStatus(HypostasisPublicStates.Exposure).RuntimeIntValue, Is.Zero);
                    Assert.That(await Hit(wind), Is.EqualTo(.95f).Within(.0001f));
                    Assert.That(await Hit(wind, false), Is.EqualTo(.95f).Within(.0001f));
                    Assert.That(await Hit(electro), Is.EqualTo(.1f).Within(.0001f));
                }
                using (BattleResolutionContext.BeginSkillAction(true))
                using (BattleResolutionContext.BeginEffect())
                    Assert.That(await Hit(wind), Is.EqualTo(.05f).Within(.0001f));
                CountEffect.Execute(HypostasisPublicStates.Exposure, "25", wind, new[] { wind }, targetType: "Self");
                Assert.That(wind.GetStatus(HypostasisPublicStates.Exposure).RuntimeIntValue, Is.EqualTo(25));
                using (BattleResolutionContext.BeginSkillAction(true))
                using (BattleResolutionContext.BeginEffect())
                    Assert.That(await Hit(wind, false), Is.EqualTo(.95f).Within(.0001f));
                Assert.That(wind.GetStatus(HypostasisPublicStates.Exposure).RuntimeIntValue, Is.EqualTo(24));
            });
        }

        [UnityTest]
        public IEnumerator Recovery_StopsAtFivePercent_ConsumesOnePrismPerEffect_AndOnlyActivatesOnce()
            => HookTestEnv.AwaitTask(RecoveryAsync);

        private async Task RecoveryAsync()
        {
            using var env = new BattleFlowTestEnv();
            env.CreateMinimalBattle();
            env.DataManager.ReloadAllData();
            await BattleExecution.Run(async () => {
                var enemy = env.Manager.SummonEnemy(2064, 1, 2);
                await BattleExecution.Flush();
                var owner = enemy.Entity;
                float hp = owner.GetFinalHP();
                using (var hit = new HitBatch()) hit.Resolve(new HitRequest {
                    Target = owner, Element = "Physical", DamageBeforeShield = hp * 100f,
                    Source = DamageSourceInfo.Create(env.Ally.Entity, ReactionSourceKind.CharacterSkill,
                        "SK_TestRecovery", "SE_TestRecovery") });
                await BattleExecution.Flush();
                Assert.That(owner.CurrentHP, Is.EqualTo(hp * .05f).Within(.001f));
                Assert.That(owner.HasStatus(HypostasisPublicStates.Recovery), Is.True);
                Assert.That(enemy.CanReceiveOrdinaryAction || enemy.CanContributeActions, Is.False);
                Assert.That(DamageImmunity.Resolve(owner, "Physical", "SE_TestRecovery"), Is.True);
                Assert.That(owner.GetStatus(HypostasisPublicStates.WindPrisms).RuntimeIntValue, Is.EqualTo(4));

                async Task React()
                {
                    ApplyElementEffect.Execute("Pyro", "1", env.Ally.Entity, new[] { owner },
                        ReactionSourceKind.CharacterSkill, "SK_TestRecovery", "SE_TestRecovery",
                        ReactionResolver.BeginEffectExecution());
                    await BattleExecution.Flush();
                }
                using (BattleResolutionContext.BeginEffect()) { await React(); await React(); }
                Assert.That(owner.GetStatus(HypostasisPublicStates.WindPrisms).RuntimeIntValue, Is.EqualTo(3));
                for (int i = 0; i < 3; i++) using (BattleResolutionContext.BeginEffect()) await React();
                Assert.That(owner.HasStatus(HypostasisPublicStates.Recovery), Is.False);
                Assert.That(owner.HasStatus(HypostasisPublicStates.WindPrisms), Is.False);
                Assert.That(owner.CurrentHP, Is.EqualTo(hp * .05f).Within(.001f));
                Assert.That(enemy.CanReceiveOrdinaryAction && enemy.CanContributeActions, Is.True);
                using (var hit = new HitBatch()) hit.Resolve(new HitRequest {
                    Target = owner, Element = "Physical", DamageBeforeShield = hp * 100f,
                    Source = DamageSourceInfo.Create(env.Ally.Entity, ReactionSourceKind.CharacterSkill,
                        "SK_TestRecovery", "SE_TestRecovery") });
                await BattleExecution.Flush();
                Assert.That(owner.IsAlive, Is.False);
            });
        }

        [UnityTest]
        public IEnumerator Pillar_UsesSummonerOwnership_AndShieldRemovalDestroysOnlyThatPillar()
            => HookTestEnv.AwaitTask(PillarAsync);

        private async Task PillarAsync()
        {
            using var env = new BattleFlowTestEnv();
            env.CreateMinimalBattle();
            env.DataManager.ReloadAllData();
            await BattleExecution.Run(async () => {
                var geo = env.Manager.SummonEnemy(2066, 1, 2).Entity;
                var pillar = env.Manager.SummonEnemy(2609, 1, 3, geo);
                await BattleExecution.Flush();
                Assert.That(pillar.Entity.Summoner, Is.SameAs(geo));
                Assert.That(pillar.ActionContribution, Is.Zero);
                Assert.That(pillar.CanReceiveOrdinaryAction, Is.False);
                Assert.That(pillar.Entity.GetEnemyElementalShield().Value, Is.EqualTo(24f));
                pillar.Entity.RemoveStatus(Enemy2064To2066Hooks.PillarShield);
                await BattleExecution.Flush();
                Assert.That(pillar.Entity.IsAlive, Is.False);
                Assert.That(geo.IsAlive, Is.True);
            });
        }
    }
}
