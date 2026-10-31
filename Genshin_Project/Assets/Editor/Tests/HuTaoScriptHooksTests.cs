using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class HuTaoScriptHooksTests
    {
        private static void Setup(BattleFlowTestEnv env)
        {
            env.ConfigureAllyAction(2, "SK_Skill_HuTao", "STE_HuTao_FixedAttack", "", ".04", 0);
            env.ConfigureStatus(HuTaoScriptHooks.Dance);
            env.ConfigureStatus(HuTaoScriptHooks.Blossom);
            env.ConfigureStatus(HuTaoScriptHooks.C6);
            env.ConfigureStatus("ST_HuTao_Embrace");
            env.DataManager.StatusActionDict[HuTaoScriptHooks.C6] = new List<StatusActionData>
            {
                new StatusActionData { StatusID2 = HuTaoScriptHooks.C6, ActionType = "OnTrigger",
                    Param1 = "STE_HuTao_Embrace", ScriptHook = "HuTao_C6Triggered()", Cooldown = 15 }
            };
            env.DataManager.StatusEffectDict["STE_HuTao_Embrace"] = new StatusEffectData
            { StatusEffectID2 = "STE_HuTao_Embrace", EffectType = "ApplyStatus", Param1 = "ST_HuTao_Embrace",
                TargetType = "Self", Duration = 2, AddInPhase = 2 };
            env.CreateMinimalBattle(); env.Ally.Entity.EntityID = 1037; env.Ally.Entity.ConstellationLevel = 6;
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
        }
        private static StatusInstance Add(BattleFlowTestEnv env, string id, BattleEntity host = null)
            => (host ?? env.Ally.Entity).AddStatus(id, env.Ally.Entity, 2, 2, 0, env.DataManager.StatusMainDict[id]);

        [Test] public void FixedValues_BelongToEachInstance_AndReapplicationRecaptures()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            var owner = env.Ally.Entity; owner.TotalHP = 1000;
            var first = Add(env, HuTaoScriptHooks.Dance);
            var effect = new StatusEffectData { EffectType = "Buff", DamageHook = "HuTao_FixedAttack()" };
            Assert.That(BattleSpecialRules.TryResolveBuffScalar(effect, first, out float initial), Is.True);
            Assert.That(initial, Is.EqualTo(40).Within(.001));
            var blossom = Add(env, HuTaoScriptHooks.Blossom, env.Enemy.Entity);
            owner.TotalHP = 2000;
            BattleSpecialRules.TryResolveBuffScalar(effect, first, out float unchanged);
            Assert.That(unchanged, Is.EqualTo(initial));
            Assert.That(blossom.Snapshot["AppliedMaxHP"], Is.EqualTo(1000));
            var second = Add(env, HuTaoScriptHooks.Dance);
            BattleSpecialRules.TryResolveBuffScalar(effect, second, out float updated);
            Assert.That(updated, Is.EqualTo(80).Within(.001));
            Assert.That(first.IsActive, Is.False);
            var refreshed = Add(env, HuTaoScriptHooks.Blossom, env.Enemy.Entity);
            Assert.That(refreshed.Snapshot["AppliedMaxHP"], Is.EqualTo(2000));
            Assert.That(blossom.IsActive, Is.False);
        }

        [UnityTest] public IEnumerator C6_LowHPReservesCooldown_AndDoesNotProtectTheNextLethalHit()
            => HookTestEnv.AwaitTask(LowHP);
        private static async Task LowHP()
        {
            using var env = new BattleFlowTestEnv(); Setup(env); Add(env, HuTaoScriptHooks.C6);
            var owner = env.Ally.Entity; owner.TotalHP = owner.CurrentHP = 1000;
            await Hit(owner, 750); // 恰好25%不触发。
            Assert.That(owner.HasStatus("ST_HuTao_Embrace"), Is.False);
            await Hit(owner, 1);
            Assert.That(owner.CurrentHP, Is.EqualTo(249));
            Assert.That(owner.HasStatus("ST_HuTao_Embrace"), Is.True);
            await Hit(owner, 999);
            Assert.That(owner.IsDead, Is.True);
        }

        [UnityTest] public IEnumerator C6_LethalTrueCommitsLivingResult_WithoutADeathAndRevival()
            => HookTestEnv.AwaitTask(Lethal);
        private static async Task Lethal()
        {
            using var env = new BattleFlowTestEnv(); Setup(env); Add(env, HuTaoScriptHooks.C6);
            var owner = env.Ally.Entity; owner.TotalHP = owner.CurrentHP = 1000;
            owner.AddShield(1000, "Geo", 3);
            await Hit(owner, 2000);
            Assert.That(owner.CurrentHP, Is.EqualTo(1));
            Assert.That(owner.IsAlive, Is.True);
            Assert.That(owner.GetTotalShieldHP(), Is.EqualTo(1000));
            Assert.That(owner.HasStatus("ST_HuTao_Embrace"), Is.True);
        }
        private static Task Hit(BattleEntity target, float amount) => BattleExecution.Run(() =>
            TrueDamageExecutor.ExecuteAsync(new[] { target }, new[] { new HitData(1, 0) }, _ => amount,
                DamageSourceInfo.CreateUnknown(), "HuTaoTestTrue", ""));
    }
}
