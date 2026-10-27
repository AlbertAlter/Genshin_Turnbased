using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class EnemyFirstTenIntegrationTests
    {
        [Test]
        public void RuntimeWorkbooks_LoadFirstTenAndTheirStatusDefinitions()
        {
            using var env = new BattleFlowTestEnv();
            env.DataManager.ReloadAllData();
            for (int id = 2001; id <= 2010; id++)
            {
                Assert.That(env.DataManager.EnemyMainDict.ContainsKey(id), Is.True);
                Assert.That(env.DataManager.EnemyAIRuleDict[id], Is.Not.Empty);
            }
            Assert.That(env.DataManager.EnemySkillMainDict.ContainsKey(200101), Is.False, "分表替换旧丘丘人跳舞");
            Assert.That(env.DataManager.EnemyMainDict[2002].HP_coeff, Is.EqualTo(.8f));
            Assert.That(env.DataManager.EnemyMainDict[2006].ATK_coeff, Is.EqualTo(3.6f));
            Assert.That(env.DataManager.GetEnemyBaseHP(1), Is.EqualTo(73f), "保留现有等级HP曲线");
            Assert.That(env.DataManager.GetEnemyBaseATK(1, 1), Is.EqualTo(25f), "保留现有等级ATK曲线");
            Assert.That(env.DataManager.StatusEffectDict["STE_RockGuard_ShieldReduction"].Param1, Is.EqualTo("-0.8"));
            Assert.That(env.DataManager.StatusEffectDict["STE_WoodGuard_ShieldReduction"].Param1, Is.EqualTo("-0.8"));
            Assert.That(env.DataManager.StatusEffectDict["STE_Fighter_ChargeFreeze"].Param2, Is.EqualTo("Freeze"));
            Assert.That(env.DataManager.EnemyAIDict[2005].Exists(p => p.PatternID == 2), Is.True);
            Assert.That(env.DataManager.EnemyAIRuleDict[2005].FindAll(r => r.PatternID == 2).Count, Is.EqualTo(1));
            Assert.That(env.DataManager.EnemySkillEffectDict["SE_Fighter_RestorePattern"].Param1, Is.EqualTo("1"));
        }

        [Test]
        public void Freeze_PreservesSharedContributionAndHasIndependentEnemyHandler()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureEnemyDamageSkill(); env.CreateMinimalBattle();
            env.DataManager.EnemyMainDict[BattleFlowTestEnv.EnemyID].ActsGiven = 2;
            var other = env.CreateEnemy(2);
            env.Enemy.Entity.Threat = 10; other.Entity.Threat = 1;
            var pool = new EnemyTurnActionPool(new[] { env.Enemy, other });
            Assert.That(pool.TryTakeNext(out var first), Is.True);
            Assert.That(first, Is.SameAs(env.Enemy));
            Assert.That(pool.LastSelectionIsLastGuaranteedAction, Is.False);
            var freeze = Freeze(env);
            Assert.That(env.Enemy.CanAct, Is.True, "Freeze不冒充受控，不扣贡献");
            Assert.That(env.Enemy.CanReceiveOrdinaryAction, Is.False);
            Assert.That(env.Ally.IsAnyButtonFrozen(), Is.False, "敌人状态不进入角色按钮处理器");
            int count = 1;
            while (pool.TryTakeNext(out var next)) { Assert.That(next, Is.SameAs(other)); count++; }
            Assert.That(count, Is.EqualTo(4));
            env.Enemy.Entity.RemoveStatusInstance(freeze);
            Assert.That(env.Enemy.CanReceiveOrdinaryAction, Is.True);
        }

        private static StatusInstance Freeze(BattleFlowTestEnv env)
        {
            env.ConfigureStatus("ST_TestEnemyCharge");
            var main = env.DataManager.StatusMainDict["ST_TestEnemyCharge"]; main.StatusID = 4999001;
            env.DataManager.StatusEffectDict["STE_TestEnemyFreeze"] = new StatusEffectData {
                StatusEffectID = 499900101, StatusEffectID2 = "STE_TestEnemyFreeze", EffectType = "ChangeControl", Param1 = "All", Param2 = "Freeze"
            };
            return env.Enemy.Entity.AddStatus(main.StatusID2, env.Enemy.Entity, 1, 5, 0, main);
        }

        [Test]
        public void WeightedSingle_UsesHitWeightAndKeepsRandomModesUnweighted()
        {
            var random = new SequenceBattleRandomSource(new[] { 0f, .25f, .99f }); BattleRandom.SetSource(random);
            var request = new TargetResolutionRequest {
                Side = BattleSide.Ally, Domain = BattleTargetDomain.Unit, MaxPosition = 2,
                TargetNumber = 1, TargetConsecutive = 1, IsEligible = _ => true, Score = p => p == 1 ? 9999f : 1f,
                SelectionWeight = p => p == 1 ? 1f : 3f
            };
            Assert.That(TargetResolver.Resolve(request).Positions, Is.EqualTo(new[] { 1 }));
            Assert.That(TargetResolver.Resolve(request).Positions, Is.EqualTo(new[] { 2 }));
            Assert.That(TargetResolver.Resolve(request).Positions, Is.EqualTo(new[] { 2 }));
            Assert.That(random.FloatCallCount, Is.EqualTo(3));
            request.TargetConsecutive = 2; request.RandomIndex = _ => 0;
            request.SelectionWeight = _ => throw new System.Exception("模式2不能读取受击权重");
            request.TauntPositions = new List<int> { 2 };
            Assert.That(TargetResolver.Resolve(request).Positions, Is.EqualTo(new[] { 1 }));
        }

        [UnityTest]
        public IEnumerator ChargeFreeze_EndsThenPatternReleaseConsumesOneSharedAction()
            => HookTestEnv.AwaitTask(ChargeAsync);
        private async Task ChargeAsync()
        {
            using var env = new BattleFlowTestEnv(); env.ConfigureEnemyDamageSkill(.01f);
            env.DataManager.EnemyAIDict[BattleFlowTestEnv.EnemyID].Add(new EnemyAIData {
                EnemyID = BattleFlowTestEnv.EnemyID, PatternID = 2, PatternType = "Weighted" });
            env.DataManager.EnemyAIRuleDict[BattleFlowTestEnv.EnemyID].Add(new EnemyAIRuleData {
                EnemyID = BattleFlowTestEnv.EnemyID, PatternID = 2, PatternType = "Weighted", SkillID = 990290, Weight = 1 });
            env.DataManager.EnemySkillMainDict[990290] = new EnemySkillMainData { EnemySkillID = 990290, EnemySkillID2 = "SK_Enemy_TestRelease" };
            env.DataManager.EnemySkillEffectDict["SE_TestRelease"] = new EnemySkillEffectData {
                SkillEffectID = 99029001, SkillEffectID2 = "SE_TestRelease", EffectType = "Damage", Element = "Physical",
                TargetType = "Enemy", TargetNumber = 1, TargetConsecutive = 1
            };
            env.DataManager.EnemySkillParamDict["SE_TestRelease"] = new EnemySkillParamData {
                SkillEffectID = 99029001, SkillEffectID2 = "SE_TestRelease", EffectType = "Damage", Hits1 = "0.02,0"
            };
            env.DataManager.EnemySkillEffectDict["SE_TestRestorePattern"] = new EnemySkillEffectData {
                SkillEffectID = 99029002, SkillEffectID2 = "SE_TestRestorePattern", EffectIndex = 1,
                EffectType = "SwitchPattern", Param1 = "1"
            };
            env.CreateMinimalBattle();
            env.Enemy.SwitchPattern(2);
            var freeze = Freeze(env);
            var effects = new List<string>();
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,
                (_, hit) => { if (hit.Source.SourceKind == ReactionSourceKind.EnemySkill) effects.Add(hit.Source.SourceEffectID); }, 0);
            var frozenPool = new EnemyTurnActionPool(new[] { env.Enemy });
            Assert.That(frozenPool.TryTakeNext(out _), Is.False);
            Assert.That(effects, Is.Empty, "蓄力期间不分配行动、不提前释放");
            env.Enemy.Entity.RemoveStatusInstance(freeze);
            Assert.That(env.Enemy.CurrentPatternID, Is.EqualTo(2), "蓄力状态结束不自动切回主模式");
            var pool = new EnemyTurnActionPool(new[] { env.Enemy });
            Assert.That(pool.TryTakeNext(out var actor), Is.True);
            Assert.That(pool.RemainingActions, Is.Zero);
            await BattleExecution.Run(() => actor.TakeEnemyActionAsync(pool.LastSelectionIsLastGuaranteedAction));
            Assert.That(effects, Is.EqualTo(new[] { "SE_TestRelease" }), "模式2只有重击，执行占用已分配的这一次行动");
            Assert.That(env.Enemy.CurrentPatternID, Is.EqualTo(1), "重击末尾SwitchPattern恢复主模式");
            Assert.That(pool.TryTakeNext(out _), Is.False, "没有额外赠送普通行动");
            Assert.That(random.IntCallCount, Is.EqualTo(1), "只抽一次重击技能，分配与单个目标不额外抽取");
            Assert.That(random.FloatCallCount, Is.Zero);
        }
    }
}
