using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class AlbedoScriptHooksTests
    {
        private const string S = "ST_Albedo_", E = "STE_Albedo_";
        private static StatusMainData Main(BattleFlowTestEnv env, int id, string key, string type = null)
        {
            var main = new StatusMainData { StatusID = 4103400 + id, StatusID2 = S + key,
                StatusType = type, MaxCount = 1, MaxStack = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            if (!env.DataManager.CharacterStatusIDs.TryGetValue(1034, out var ids))
                env.DataManager.CharacterStatusIDs[1034] = ids = new HashSet<string>();
            ids.Add(main.StatusID2); return main;
        }
        private static StatusEffectData Effect(BattleFlowTestEnv env, int id, string key, string type,
            string param = null, string target = "Self")
        {
            var effect = new StatusEffectData { StatusEffectID = id, StatusEffectID2 = E + key,
                EffectType = type, Param1 = param, TargetType = target, ExecuteCount = 1 };
            env.DataManager.StatusEffectDict[effect.StatusEffectID2] = effect; return effect;
        }
        private static void Setup(BattleFlowTestEnv env)
        {
            env.ConfigureAllyAction(0, "SK_Normal_AlbedoTest", "SE_Normal_AlbedoTest", "Physical", "1*TotalATK,0", 0);
            env.DataManager.SkillEffectDict["SE_Normal_AlbedoTest"].TargetNumber = 2;
            foreach (var group in env.DataManager.SkillLevelDict.Values)
                foreach (var row in group.Values) if (row.ParamID == "SE_Normal_AlbedoTest") row.Hits2 = "1";
            Main(env, 1, "Solar", "GeoConstruct");
            var silver = Main(env, 2, "Silver"); silver.MaxStack = silver.MaxCount = 2; silver.WhenMax = "1";
            Main(env, 3, "Reckoning", "Count");
            var baseBonus = Main(env, 14, "C6Base", "Buff"); baseBonus.MultiplierPart = "BaseDMGBonusFlat"; baseBonus.ApplyDamageType = "Burst";
            var bonus = Effect(env, 410341401, "C6Base", "Buff", "2.5"); bonus.DamageHook = "Albedo_Defense()";
            var consumed = Main(env, 8, "C2Bonus", "Buff"); consumed.MultiplierPart = "BaseDMGBonusFlat";
            consumed.ApplyDamageType = "Burst"; consumed.ApplyString = "SK_Burst_Albedo";
            Effect(env, 410340801, "C2Bonus", "Buff", ".3").DamageHook = "Albedo_Consumed()";
            Main(env, 9, "C4Rule");
            var normal = Main(env, 11, "C4Normal", "Buff"); normal.MultiplierPart = "DMGBonus";
            normal.ApplyDamageType = "Normal"; normal.ScriptHook = "Albedo_NormalBonus()";
            Effect(env, 410341101, "C4Normal", "Buff", ".3");
            var bind = Effect(env, 410340901, "C4Bind", "BindStatus", S + "C4Normal", "Allies"); bind.TargetSelect = "-1";
            env.CreateMinimalBattle(); env.Ally.Entity.EntityID = 1034;
            env.CreateEnemy(2, 100000); env.Enemy.Entity.CurrentHP = env.Enemy.Entity.TotalHP = 100000;
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            env.Ally.Entity.TotalATK = env.Ally.Entity.TotalDEF = 100;
            env.Ally.Entity.CritRate = env.Ally.Entity.CritDMG = 0;
            BattleRandom.SetSource(new SequenceBattleRandomSource());
        }
        private static StatusInstance Add(BattleFlowTestEnv env, string key, int duration = 7)
            => env.Ally.Entity.AddStatus(S + key, env.Ally.Entity, duration, 2, 0, env.DataManager.StatusMainDict[S + key]);
        private static StatusInstance Solar(BattleFlowTestEnv env, int slot)
            => env.Manager.Field.GetSlot(BattleSide.Enemy, slot).AddStatus(S + "Solar", env.Ally.Entity,
                7, 2, 0, env.DataManager.StatusMainDict[S + "Solar"]);
        private static void Flowers(BattleFlowTestEnv env)
        {
            foreach (string flower in new[] { "Solar", "Silver" })
            {
                int id = flower == "Solar" ? 410340101 : 410340201;
                var damage = Effect(env, id, flower + "Damage", "Damage", ".1*TotalDEF,0", "Enemy");
                damage.Element = "Geo"; damage.DamageType = "Skill"; damage.TargetOverride = "Albedo_FlowerTarget()";
                var action = new StatusActionData { StatusID2 = S + flower, ActionType = "OnTrigger",
                    Param1 = E + flower + "Damage", ScriptHook = "Albedo_FlowerHit()", ExecuteCount = 1 };
                action.SetTriggerLimits("Turn;Action", "2;1");
                env.DataManager.StatusActionDict[S + flower] = new List<StatusActionData> { action };
            }
        }
        private static void Destruction(BattleFlowTestEnv env)
        {
            var refresh = Effect(env, 410340203, "C6CountRefresh", "ApplyStatus", S + "Reckoning");
            refresh.Duration = 7; refresh.AddInPhase = 2;
            Effect(env, 410340204, "C6Count", "Count", S + "Reckoning").Param2 = "4";
            var buff = Effect(env, 410340205, "C6BaseGrant", "ApplyStatus", S + "C6Base");
            buff.Duration = 5; buff.AddInPhase = 2;
            env.DataManager.StatusActionDict[S + "Silver"] = new List<StatusActionData> { new StatusActionData
            { StatusID2 = S + "Silver", ActionType = "OnDestroy", ScriptHook = "Albedo_BurstDestroyedSilver()",
                Param1 = E + "C6CountRefresh;" + E + "C6Count;" + E + "C6BaseGrant", ExecuteCount = 1 } };
        }
        private static float BurstBonus(BattleFlowTestEnv env)
        {
            using var query = DamageQueryContext.Enter("SK_Burst_Albedo", "Burst");
            return env.Ally.Entity.GetStatusBaseDMGBonusFlat("SE_Burst_Albedo", "SK_Burst_Albedo", 3, "Geo", env.Enemy.Entity);
        }

        [Test] public void SolarIsUnique_SilverInstancesKeepIndependentLifetimesAndSurviveSolarReplacement()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            var old = Solar(env, 1); var first = Add(env, "Silver"); first.RemainingPhaseCount = 3;
            var second = Add(env, "Silver"); var rejected = Add(env, "Silver"); var current = Solar(env, 5);
            Assert.That(old.IsActive, Is.False); Assert.That(current.IsActive, Is.True);
            Assert.That(env.Ally.Entity.GetStatusInstances(S + "Silver"), Has.Count.EqualTo(2));
            Assert.That(rejected, Is.SameAs(first)); Assert.That(first.RemainingPhaseCount, Is.EqualTo(3));
            Assert.That(second.RemainingPhaseCount, Is.EqualTo(7));
            StatusHostAccess.Remove(current.Host, current, "Expired");
            Assert.That(first.IsActive && second.IsActive, Is.True);
        }

        [Test] public void Count_SeparatesOrdinaryCapFromC6_AndClearDoesNotRemoveOrRefresh()
        {
            using var env = new BattleFlowTestEnv(); Setup(env); var owner = env.Ally.Entity;
            var count = Add(env, "Reckoning"); count.RemainingPhaseCount = 2;
            for (int i = 0; i < 6; i++) CountEffect.Execute(S + "Reckoning", "1", owner, new[] { owner }, E + "SolarCount");
            Assert.That(count.RuntimeIntValue, Is.EqualTo(4)); Assert.That(count.RemainingPhaseCount, Is.EqualTo(2));
            CountEffect.Execute(S + "Reckoning", "4", owner, new[] { owner }, E + "C6Count");
            CountEffect.Execute(S + "Reckoning", "4", owner, new[] { owner }, E + "C6Count");
            CountEffect.Execute(S + "Reckoning", "1", owner, new[] { owner }, E + "SolarCount");
            Assert.That(count.RuntimeIntValue, Is.EqualTo(12));
            Assert.That(Add(env, "Reckoning"), Is.SameAs(count)); Assert.That(count.RemainingPhaseCount, Is.EqualTo(7));
            CountEffect.Execute(S + "Reckoning", "clear", owner, new[] { owner });
            Assert.That(count.RuntimeIntValue, Is.Zero); Assert.That(owner.HasStatusInstance(count), Is.True);
        }

        [UnityTest] public IEnumerator C6_TwoDestroyedSilversGiveEightPointsButOneBuff_BeforeBurstConsumes()
            => HookTestEnv.AwaitTask(C6Async);
        private async Task C6Async()
        {
            using var env = new BattleFlowTestEnv(); Setup(env); Destruction(env);
            var owner = env.Ally.Entity; owner.ConstellationLevel = 6;
            var count = Add(env, "Reckoning");
            CountEffect.Execute(S + "Reckoning", "4", owner, new[] { owner }, E + "SolarCount");
            Add(env, "C2Bonus", 0); Add(env, "Silver"); Add(env, "Silver");
            await BattleExecution.Run(async () =>
            {
                using var cast = BattleSpecialRules.BeginSkillExecution(owner, new SkillMainData { SkillID2 = "SK_Burst_Albedo" });
                owner.RemoveStatus(S + "Silver", -1); await BattleExecution.Flush();
                Assert.That(count.RuntimeIntValue, Is.EqualTo(12));
                Assert.That(owner.GetStatusInstances(S + "C6Base"), Has.Count.EqualTo(1));
                Assert.That(owner.GetStatus(S + "C6Base").RemainingPhaseCount, Is.EqualTo(5));
                CountEffect.Execute(S + "Reckoning", "clear", owner, new[] { owner }, "SE_Burst_AlbedoConsume");
                Assert.That(count.RuntimeIntValue, Is.Zero); Assert.That(count.IsActive, Is.True);
                Assert.That(BurstBonus(env), Is.EqualTo(610f).Within(.001f)); // (12*.3+2.5)*100DEF
                CountEffect.Execute(S + "Reckoning", "1", owner, new[] { owner }, E + "SolarCount");
                Assert.That(BurstBonus(env), Is.EqualTo(610f).Within(.001f));
                owner.TotalDEF = 200; Assert.That(BurstBonus(env), Is.EqualTo(1220f).Within(.001f));
            });
            Assert.That(BurstBonus(env), Is.EqualTo(500f).Within(.001f)); // 消费记忆仅属于上次爆发，五回合增益仍在。
            var next = Add(env, "Silver");
            await BattleExecution.Run(async () => { owner.RemoveStatusInstance(next, "AlbedoNormalConsumed"); await BattleExecution.Flush(); });
            Assert.That(count.RuntimeIntValue, Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator Flowers_WholeHitCandidates_IndependentActionAndTurnLimits_NoSilverRecursion()
            => HookTestEnv.AwaitTask(FlowersAsync);
        private async Task FlowersAsync()
        {
            using var env = new BattleFlowTestEnv(); Setup(env); Flowers(env);
            Solar(env, 4); Add(env, "Silver"); Add(env, "Silver");
            int solarHits = 0, silverHits = 0;
            using var observer = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
            {
                if (hit.Source.SourceEffectID == E + "SolarDamage") solarHits++;
                if (hit.Source.SourceEffectID == E + "SilverDamage") silverHits++;
            }, 0);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            for (int i = 1; i <= 3; i++)
            {
                env.Ally.ForcedTargetPositions.AddRange(new[] { 1, 2 });
                await BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_Normal_AlbedoTest", 0, true));
                Assert.That(solarHits, Is.EqualTo(System.Math.Min(i, 2)));
                Assert.That(silverHits, Is.EqualTo(2 * System.Math.Min(i, 2)));
            }
            Assert.That(random.IntCallCount, Is.EqualTo(6)); // 两次有效行动，各三份花各随机一次。
        }

        [UnityTest] public IEnumerator C4_ConsumesBeforeFirstHit_AllTargetsAndHitsShareBonus_QueriesDoNotConsume()
            => HookTestEnv.AwaitTask(C4Async);
        private async Task C4Async()
        {
            using var env = new BattleFlowTestEnv(); Setup(env); var owner = env.Ally.Entity;
            Add(env, "C4Rule", 0); Add(env, "Silver"); Add(env, "Silver");
            using (DamageQueryContext.Enter("SK_Normal_AlbedoTest", "Normal"))
                Assert.That(owner.GetStatusDMGBonus("SE_Normal_AlbedoTest", "SK_Normal_AlbedoTest", 0, "Physical", env.Enemy.Entity), Is.Zero);
            Assert.That(owner.GetStatusInstances(S + "Silver"), Has.Count.EqualTo(2));
            var amounts = new List<float>();
            using var observer = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
            {
                if (hit.Source.SourceEffectID != "SE_Normal_AlbedoTest") return;
                amounts.Add(hit.ActualHPDamage);
                Assert.That(owner.GetStatusInstances(S + "Silver"), Has.Count.EqualTo(1));
            }, 0);
            env.Ally.ForcedTargetPositions.AddRange(new[] { 1, 2 });
            await BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_Normal_AlbedoTest", 0, true));
            Assert.That(amounts, Has.Count.EqualTo(4));
            Assert.That(amounts, Is.All.EqualTo(65f).Within(.001f)); // 100*1.3*相同等级防御系数0.5
        }
    }
}
