using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class XiaoScriptHooksTests
    {
        private static void Setup(BattleFlowTestEnv env)
        {
            foreach (var row in new[] { (1,"Mask", ""), (4,"Tamer","Count"), (6,"Cycling","Count"), (10,"Free","") })
                env.DataManager.StatusMainDict["ST_Xiao_" + row.Item2] = new StatusMainData
                { StatusID = 4103600 + row.Item1, StatusID2 = "ST_Xiao_" + row.Item2, StatusType = row.Item3, MaxStack = 1, MaxCount = 1, WhenMax = "0" };
            env.DataManager.StatusEffectDict["STE_Xiao_Drain"] = new StatusEffectData
            { StatusEffectID = 410360105, StatusEffectID2 = "STE_Xiao_Drain", EffectType = "Damage", Element = "True", Param1 = ".1", TargetType = "Self", DamageHook = "Xiao_BurstDrain()" };
            env.DataManager.StatusEffectDict["STE_Xiao_FreeEnd"] = new StatusEffectData
            { StatusEffectID = 410360108, StatusEffectID2 = "STE_Xiao_FreeEnd", EffectType = "RemoveStatus", Param1 = XiaoScriptHooks.Free, TargetType = "Self" };
            env.DataManager.StatusEffectDict["STE_Xiao_Free"] = new StatusEffectData
            { StatusEffectID = 410361001, StatusEffectID2 = "STE_Xiao_Free", EffectType = "APCost", Param1 = "Free", Param2 = "Skill", TargetType = "Self" };
            env.DataManager.StatusActionDict[XiaoScriptHooks.Mask] = new List<StatusActionData>
            {
                new StatusActionData { StatusID2 = XiaoScriptHooks.Mask, ActionType = "OnItsTurn", Param1 = "STE_Xiao_Drain" },
                new StatusActionData { StatusID2 = XiaoScriptHooks.Mask, ActionType = "OnEnd", Param1 = "STE_Xiao_FreeEnd" },
                new StatusActionData { StatusID2 = XiaoScriptHooks.Mask, ActionType = "OnDestroy", Param1 = "STE_Xiao_FreeEnd" }
            };
            env.ConfigureAllyAction(1,"SK_Heavy_Xiao2","SE_Heavy_Xiao2Damage","Physical","1*TotalATK,0", 0);
            env.DataManager.SkillEffectDict["SE_Heavy_Xiao2Damage"].TargetNumber = -1;
            env.DataManager.SkillEffectDict["SE_Heavy_Xiao2Charge"] = new SkillEffectData
            { SkillEffectID2 = "SE_Heavy_Xiao2Charge", EffectIndex = 1, EffectType = "ResetCooldown", Param1 = "SK_Skill_Xiao", ScriptHook = "Xiao_C6Hit()" };
            env.DataManager.SkillEffectDict["SE_Heavy_Xiao2Free"] = new SkillEffectData
            { SkillEffectID2 = "SE_Heavy_Xiao2Free", EffectIndex = 2, EffectType = "ApplyStatus", Param1 = XiaoScriptHooks.Free, TargetType = "Self", ScriptHook = "Xiao_C6Hit()" };
            env.ConfigureAllyAction(2,"SK_Skill_Xiao","SE_Skill_XiaoConsume","", "", 30, 2,
                effectType:"RemoveStatus",param1:XiaoScriptHooks.Free,targetType:"Self");
            var skill = env.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID].Last(); skill.MaxCharge = skill.InitialCharge = 2;
            env.CreateMinimalBattle(); env.Ally.Entity.EntityID = 1036; env.Ally.Entity.ConstellationLevel = 6;
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            env.Ally.Entity.TotalATK = 100; env.Ally.Entity.CritRate = 0;
        }
        private static StatusInstance Add(BattleFlowTestEnv env, string id, int duration = 4)
            => env.Ally.Entity.AddStatus(id, env.Ally.Entity, duration, 2, 0, env.DataManager.StatusMainDict[id]);

        [UnityTest] public IEnumerator Drain_UsesCurrentHP_BypassesShield_StopsAtOne_AndOrdinaryTrueRemainsLethal()
            => HookTestEnv.AwaitTask(Drain);
        private static async Task Drain()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            var mask = Add(env, XiaoScriptHooks.Mask); env.Ally.Entity.AddShield(1000,"Geo",4);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            foreach (var item in new[] { (100f,90f), (1.05f,1f), (1f,1f), (.5f,.5f) })
            {
                env.Ally.Entity.CurrentHP = item.Item1;
                await BattleExecution.Run(() => env.Ally.ExecuteStatusActionsTask(mask,
                    env.DataManager.StatusActionDict[XiaoScriptHooks.Mask], env.Ally.Entity,"OnItsTurn"));
                Assert.That(env.Ally.Entity.CurrentHP, Is.EqualTo(item.Item2).Within(.00001));
                Assert.That(env.Ally.Entity.GetTotalShieldHP(), Is.EqualTo(1000));
            }
            Assert.That(random.FloatCallCount, Is.Zero); Assert.That(random.IntCallCount, Is.Zero);
            await BattleExecution.Run(() => TrueDamageExecutor.ExecuteAsync(new[] { env.Ally.Entity },
                new[] { new HitData(1,0) }, _ => 10f, DamageSourceInfo.CreateUnknown(),"ordinaryTrue", ""));
            Assert.That(env.Ally.Entity.IsAlive, Is.False);
        }

        [UnityTest] public IEnumerator C6_TwoDistinctEnemies_RecoverOneCharge_OneFreeSkill_AndDestroyCleansFree()
            => HookTestEnv.AwaitTask(C6);
        private static async Task C6()
        {
            using var env = new BattleFlowTestEnv(); Setup(env); var mask = Add(env,XiaoScriptHooks.Mask);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            await BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_Skill_Xiao",2,true));
            Assert.That(env.Ally.GetBoundSkillCurrentCharge(2),Is.EqualTo(1));
            await BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_Heavy_Xiao2",1,true));
            Assert.That(env.Ally.Entity.HasStatus(XiaoScriptHooks.Free),Is.False);
            env.CreateEnemy(2,1000).Entity.AddShield(1000,"Geo",3);
            await BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_Heavy_Xiao2",1,true));
            Assert.That(env.Ally.GetBoundSkillCurrentCharge(2),Is.EqualTo(2));
            Assert.That(env.Ally.Entity.GetStatusInstances(XiaoScriptHooks.Free).Count,Is.EqualTo(1));
            int paid = 0;
            using var payments = BattleEventHub.Subscribe<APSpentEventData>(BattleEventKind.APSpent,(_,fact) => paid += fact.Amount,0);
            await BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_Skill_Xiao",2,true));
            Assert.That(paid,Is.Zero); Assert.That(env.Ally.Entity.HasStatus(XiaoScriptHooks.Free),Is.False);
            Add(env,XiaoScriptHooks.Free);
            await BattleExecution.Run(() => { env.Ally.Entity.RemoveStatusInstance(mask,"Removed"); return Task.CompletedTask; });
            Assert.That(env.Ally.Entity.HasStatus(XiaoScriptHooks.Free),Is.False);
            Assert.That(random.FloatCallCount,Is.EqualTo(3));
        }

        [Test] public void Count_ReapplyRefreshesOnly_ChangesCapAtThree_ClearKeepsInstance()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            var state = Add(env,XiaoScriptHooks.Cycling,2);
            CountEffect.Execute(XiaoScriptHooks.Cycling,"2",env.Ally.Entity,new[] { env.Ally.Entity });
            state.RemainingPhaseCount=1;
            Assert.That(Add(env,XiaoScriptHooks.Cycling,2),Is.SameAs(state));
            Assert.That(state.RemainingPhaseCount,Is.EqualTo(2)); Assert.That(state.RuntimeIntValue,Is.EqualTo(2));
            CountEffect.Execute(XiaoScriptHooks.Cycling,"2",env.Ally.Entity,new[] { env.Ally.Entity });
            Assert.That(state.RuntimeIntValue,Is.EqualTo(3));
            CountEffect.Execute(XiaoScriptHooks.Cycling,"clear",env.Ally.Entity,new[] { env.Ally.Entity });
            Assert.That(state.RuntimeIntValue,Is.Zero); Assert.That(env.Ally.Entity.HasStatusInstance(state),Is.True);
        }
        [Test] public void FixedDamageHook_IsRestrictedToRegisteredTrueStatusEffects()
        {
            Assert.DoesNotThrow(() => TableValidator.ValidateDamageProperties("Status_Effect",3,"Damage","True",".1",null,null,"Xiao_BurstDrain()"));
            foreach (string table in new[] {"SkillsEffect","EnemySkill_Effect"})
                Assert.Throws<TableValidationException>(() => TableValidator.ValidateDamageProperties(table,3,"Damage","True",".1",null,null,"Xiao_BurstDrain()"));
            Assert.Throws<TableValidationException>(() => TableValidator.ValidateDamageProperties("Status_Effect",3,"Damage","None",".1",null,null,"Xiao_BurstDrain()"));
            Assert.Throws<TableValidationException>(() => TableValidator.ValidateDamageProperties("Status_Effect",3,"Damage","True","1.1",null,null,"Xiao_BurstDrain()"));
        }
    }
}
