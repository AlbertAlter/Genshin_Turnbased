using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>灼灼付款只记账，完整我方行动（含结束声明派生）或换人结清后才通过表中OnTrigger产印。</summary>
    public sealed class YanfeiSettlementTests
    {
        private static void Setup(BattleFlowTestEnv env)
        {
            env.ConfigureAllyAction(1, "SK_Heavy_Yanfei", "SE_Heavy_YanfeiDamage", "Pyro", "", 22,
                param1: ".1*TotalATK,1");
            env.DataManager.SkillEffectDict["SE_Heavy_YanfeiDamage"].EffectIndex = 1;
            env.DataManager.SkillEffectDict["SE_Heavy_YanfeiConsume"] = new SkillEffectData
            {
                SkillEffectID = 10390201, SkillEffectID2 = "SE_Heavy_YanfeiConsume", EffectIndex = 0,
                EffectType = "Count", Param1 = YanfeiScriptHooks.Seals, Param2 = "clear", TargetType = "Self",
                ScriptHook = "Yanfei_ConsumeSeals()"
            };
            env.ConfigureAllyAction(0, "SK_Normal_YanfeiChain", "SE_Normal_YanfeiChainDamage", "Pyro", "", 20,
                param1: ".1*TotalATK,1");
            var ratios = new[] { "1.1556", "1.329", "1.5023", "1.6757" };
            for (int n = 1; n <= 4; n++)
                env.DataManager.SkillLevelDict[BattleFlowTestEnv.AllyID + "_1_Yanfei_HeavySeals" + n]
                    = new Dictionary<int, SkillLevelData> { [1] = new SkillLevelData
                    { CharacterID = BattleFlowTestEnv.AllyID, SkillType = 1, SkillLevel = 1,
                        ParamID = "Yanfei_HeavySeals" + n, Hits1 = ratios[n - 1] } };
            env.ConfigureStatus(YanfeiScriptHooks.Seals);
            env.ConfigureStatus(YanfeiScriptHooks.Brilliance, 1, "0", "OnTrigger", "STE_Yanfei_APInit;STE_Yanfei_APGain");
            env.DataManager.StatusMainDict[YanfeiScriptHooks.Seals].StatusType = "Count";
            env.DataManager.StatusMainDict[YanfeiScriptHooks.Brilliance].StatusType = "Count";
            var reward = env.DataManager.StatusActionDict[YanfeiScriptHooks.Brilliance][0];
            reward.ScriptHook = "Yanfei_APReward()"; reward.ExecuteCountHandler = "Yanfei_APAwards";
            env.DataManager.StatusEffectDict["STE_Yanfei_APInit"] = new StatusEffectData
            {
                StatusEffectID2 = "STE_Yanfei_APInit", EffectType = "ApplyStatus", Param1 = YanfeiScriptHooks.Seals,
                TargetType = "Self", Duration = 3, AddInPhase = 2
            };
            env.DataManager.StatusEffectDict["STE_Yanfei_APGain"] = new StatusEffectData
            {
                StatusEffectID2 = "STE_Yanfei_APGain", EffectType = "Count", Param1 = YanfeiScriptHooks.Seals,
                Param2 = "1", TargetType = "Self"
            };
            env.CreateMinimalBattle(); env.CreateAlly(2);
            env.Ally.Entity.EntityID = 1039;
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            var owner = env.Ally.Entity;
            owner.AddStatus(YanfeiScriptHooks.Seals, owner, 3, 2, 0, env.DataManager.StatusMainDict[YanfeiScriptHooks.Seals]);
            owner.AddStatus(YanfeiScriptHooks.Brilliance, owner, 4, 2, 0, env.DataManager.StatusMainDict[YanfeiScriptHooks.Brilliance]);
            Change(env, YanfeiScriptHooks.Seals, "2");
        }
        private static void Change(BattleFlowTestEnv env, string id, string amount)
            => CountEffect.Execute(id, amount, env.Ally.Entity, new[] { env.Ally.Entity }, "YanfeiSettlementTest");
        private static int Seals(BattleFlowTestEnv env) => env.Ally.Entity.GetStatus(YanfeiScriptHooks.Seals)?.RuntimeIntValue ?? 0;
        private static int AP(BattleFlowTestEnv env) => env.Ally.Entity.GetStatus(YanfeiScriptHooks.Brilliance).RuntimeIntValue;

        [Test]
        public void SealCount_ReapplyRefreshesWithoutGain_CountDoesNotRefresh_ZeroPersists_AndC6CapsAtFour()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            var owner = env.Ally.Entity; var state = owner.GetStatus(YanfeiScriptHooks.Seals);
            state.RemainingPhaseCount = 1;
            Change(env, YanfeiScriptHooks.Seals, "1");
            Assert.That(Seals(env), Is.EqualTo(3)); Assert.That(state.RemainingPhaseCount, Is.EqualTo(1));
            var reapplied = owner.AddStatus(YanfeiScriptHooks.Seals, owner, 3, 2, 0,
                env.DataManager.StatusMainDict[YanfeiScriptHooks.Seals]);
            Assert.That(reapplied, Is.SameAs(state)); Assert.That(Seals(env), Is.EqualTo(3));
            Assert.That(state.RemainingPhaseCount, Is.EqualTo(3));
            Change(env, YanfeiScriptHooks.Seals, "clear");
            Assert.That(owner.GetStatus(YanfeiScriptHooks.Seals), Is.SameAs(state)); Assert.That(Seals(env), Is.Zero);
            owner.ConstellationLevel = 6; Change(env, YanfeiScriptHooks.Seals, "full");
            Assert.That(Seals(env), Is.EqualTo(4)); Change(env, YanfeiScriptHooks.Seals, "1");
            Assert.That(Seals(env), Is.EqualTo(4));
        }

        [Test]
        public void HeavyRatio_ReadsTheConsumedSealTier_AfterCountWasCleared()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            var owner = env.Ally.Entity; owner.ConstellationLevel = 6;
            var expected = new[] { .9823f, 1.1556f, 1.329f, 1.5023f, 1.6757f };
            BattleSpecialRules.ValidateDamageHook("Yanfei_HeavyRatio()", owner, "Damage");
            for (int n = 0; n <= 4; n++)
            {
                Change(env, YanfeiScriptHooks.Seals, "clear"); Change(env, YanfeiScriptHooks.Seals, n.ToString());
                using (BattleSpecialRules.BeginSkillExecution(owner, new SkillMainData
                    { SkillID2 = "SK_Heavy_Yanfei", ActionType = "Heavy" }))
                {
                    Assert.That(ScriptHookEvaluator.Evaluate("Yanfei_ConsumeSeals()", new ScriptHookContext { Caster = owner }), Is.True);
                    Change(env, YanfeiScriptHooks.Seals, "clear");
                    var hit = BattleSpecialRules.ModifyHit("Yanfei_HeavyRatio()", owner, env.Enemy.Entity, new HitData(.9823f, 1f), 1);
                    Assert.That(hit.Multiplier, Is.EqualTo(expected[n]).Within(.000001f)); Assert.That(hit.ElementAura, Is.EqualTo(1f));
                }
            }
        }

        [UnityTest]
        public IEnumerator Heavy_UsesOldSeals_AndWaitsForActionCompletedDerivedSkill()
            => HookTestEnv.AwaitTask(HeavyAsync);
        private static async Task HeavyAsync()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            Change(env, YanfeiScriptHooks.Brilliance, "18");
            bool heavyHit = false, childHit = false, ended = false;
            using var hits = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, damage) =>
            {
                if (damage.Source.SourceEntity != env.Ally.Entity) return;
                Assert.That(Seals(env), Is.Zero, "AP收益不能进入原重击或它的追加行为");
                if (damage.Source.SourceEffectID == "SE_Heavy_YanfeiDamage")
                {
                    heavyHit = true; Assert.That(AP(env), Is.EqualTo(40));
                    Assert.That(ScriptHookEvaluator.Evaluate("Yanfei_HeavySeals(2)",
                        new ScriptHookContext { Caster = env.Ally.Entity }), Is.True);
                }
                if (damage.Source.SourceEffectID == "SE_Normal_YanfeiChainDamage") childHit = true;
            }, 0);
            using var ending = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted, (_, action) =>
            {
                if (action.Subject != env.Ally.Entity || action.SkillId != "SK_Heavy_Yanfei") return;
                ended = true; Assert.That(Seals(env), Is.Zero);
                BattleExecution.Enqueue(async () =>
                {
                    await Task.Yield();
                    await env.Ally.ExecuteSkillByIDAsync("SK_Normal_YanfeiChain", 0);
                    Assert.That(Seals(env), Is.Zero, "内层行为完成仍须等外层结束响应结清");
                });
            }, long.MaxValue);
            await BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_Heavy_Yanfei", 1, true));
            Assert.That(heavyHit && childHit && ended, Is.True);
            Assert.That(Seals(env), Is.EqualTo(3)); Assert.That(AP(env), Is.Zero);
        }

        [Test]
        public void Switch_WaitsForAllSwitchResponses_ThenAwardsWhileYanfeiIsOffField()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            Change(env, YanfeiScriptHooks.Seals, "clear"); Change(env, YanfeiScriptHooks.Brilliance, "15");
            bool observed = false;
            using var switched = BattleEventHub.Subscribe<SwitchEventData>(BattleEventKind.SwitchResolved, (_, fact) =>
            {
                observed = true; Assert.That(Seals(env), Is.Zero);
                Assert.That(AP(env), Is.EqualTo(20)); Assert.That(env.Ally.IsActive, Is.False);
            }, long.MaxValue);
            Assert.That(env.Manager.TrySwitchActiveAllyWithAPCost(1), Is.True);
            Assert.That(observed, Is.True); Assert.That(Seals(env), Is.EqualTo(1)); Assert.That(AP(env), Is.Zero);
        }

        [UnityTest]
        public IEnumerator NonActionPayment_OnlyAccumulates_UntilNextAllyAction()
            => HookTestEnv.AwaitTask(NonActionAsync);
        private static async Task NonActionAsync()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            env.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID]
                .Single(s => s.SkillID2 == "SK_Normal_YanfeiChain").ActionType = "";
            Change(env, YanfeiScriptHooks.Seals, "clear");
            await BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_Normal_YanfeiChain", 0, true));
            Assert.That(AP(env), Is.EqualTo(20)); Assert.That(Seals(env), Is.Zero);
            await BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_Heavy_Yanfei", 1, true));
            Assert.That(AP(env), Is.EqualTo(2)); Assert.That(Seals(env), Is.EqualTo(2));
        }
    }
}
