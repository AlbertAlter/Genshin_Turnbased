using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class ZhongliScriptHooksTests
    {
        private static StatusMainData Main(BattleFlowTestEnv env, int id, string key, string type = null)
        {
            var main = new StatusMainData { StatusID = id, StatusID2 = key, StatusType = type, MaxStack = 1, WhenMax = "0", MaxCount = 1 };
            env.DataManager.StatusMainDict[key] = main; return main;
        }
        private static StatusInstance Stele(BattleFlowTestEnv env, int slot)
        {
            var main = Main(env, 4103201, ZhongliScriptHooks.Stele, "GeoConstruct");
            return env.Manager.Field.GetSlot(BattleSide.Enemy, slot).AddStatus(main.StatusID2, env.Ally.Entity, 5, 2, 3, main);
        }

        [Test] public void Stele_RefreshesSameEmptySlot_AndC1ReplacesOldestAcrossSlots()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); env.Ally.Entity.EntityID = 1032;
            var first = Stele(env, 3); var replacement = Stele(env, 3);
            Assert.That(first.IsActive, Is.False); Assert.That(replacement.IsActive, Is.True);
            var second = Stele(env, 4); Assert.That(replacement.IsActive, Is.False);
            env.Ally.Entity.ConstellationLevel = 1;
            var third = Stele(env, 2); Assert.That(second.IsActive, Is.True);
            var fourth = Stele(env, 5);
            Assert.That(second.IsActive, Is.False); Assert.That(third.IsActive && fourth.IsActive, Is.True);
        }

        [Test] public void JadeShield_GenerationUsesLivingParty_AndDoesNotRecomputeExistingValue()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); env.Ally.Entity.EntityID = 1032;
            var ally2 = env.CreateAlly(2).Entity; var ally3 = env.CreateAlly(3).Entity;
            ally3.CurrentHP = 0;
            var main = Main(env, 4103202, ZhongliScriptHooks.Jade, "OverallShield");
            env.DataManager.StatusEffectDict["STE_Zhongli_CreateShield"] = new StatusEffectData
            { StatusEffectID = 410320201, StatusEffectID2 = "STE_Zhongli_CreateShield", EffectType = "Shield",
                Param1 = "100", Element = "Geo", TargetType = "Self", Duration = 5, ExecuteCount = 1 };
            env.DataManager.StatusActionDict[main.StatusID2] = new List<StatusActionData> { new StatusActionData
            { StatusID2 = main.StatusID2, ActionType = "OnApply", Param1 = "STE_Zhongli_CreateShield", ExecuteCount = 1 } };
            BattleExecution.Run(() => { env.Ally.Entity.AddStatus(main.StatusID2, env.Ally.Entity, 5, 2, 0, main); return Task.CompletedTask; })
                .GetAwaiter().GetResult();
            Assert.That(env.Ally.Entity.Shields.Single().Value, Is.EqualTo(200));
            ally2.CurrentHP = 0;
            Assert.That(env.Ally.Entity.Shields.Single().Value, Is.EqualTo(200));
            BattleExecution.Run(() => { env.Ally.Entity.AddStatus(main.StatusID2, env.Ally.Entity, 5, 2, 0, main); return Task.CompletedTask; })
                .GetAwaiter().GetResult();
            Assert.That(env.Ally.Entity.Shields.Single().Value, Is.EqualTo(100));
        }

        [Test] public void Fortify_MultiTargetHitAddsOnce_AfterPayment_ZeroRetainsCount()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var owner = env.Ally.Entity; owner.EntityID = 1032; var ally = env.CreateAlly(2).Entity;
            var jadeMain = Main(env, 4103202, ZhongliScriptHooks.Jade, "OverallShield");
            var countMain = Main(env, 4103204, ZhongliScriptHooks.Fortify, "Count");
            var bonusMain = Main(env, 4103205, "ST_Zhongli_FortifyBonus", "Buff"); bonusMain.MultiplierPart = "ShieldStrength";
            env.DataManager.StatusEffectDict["STE_Zhongli_FortifyGain"] = new StatusEffectData
            { StatusEffectID = 410320402, StatusEffectID2 = "STE_Zhongli_FortifyGain", EffectType = "Count",
                Param1 = countMain.StatusID2, Param2 = "1", TargetType = "Self", ExecuteCount = 1 };
            env.DataManager.StatusEffectDict["STE_Zhongli_FortifyBonus"] = new StatusEffectData
            { StatusEffectID = 410320501, StatusEffectID2 = "STE_Zhongli_FortifyBonus", EffectType = "Buff",
                Param1 = "0.05*Count(ST_Zhongli_Fortify)" };
            env.DataManager.StatusActionDict[countMain.StatusID2] = new List<StatusActionData> { new StatusActionData
            { StatusID2 = countMain.StatusID2, ActionType = "OnTrigger", Param1 = "STE_Zhongli_FortifyGain",
                ScriptHook = "Zhongli_ShieldPaid()", MaxTimePerHit = 1, ExecuteCount = 1 } };
            var jade = owner.AddStatus(jadeMain.StatusID2, owner, 5, 2, 0, jadeMain);
            owner.AddShield(1000, "None", 5, sourceStatus: jade);
            var count = owner.AddStatus(countMain.StatusID2, owner, 5, 2, 0, countMain); jade.BoundStatuses.Add(count);
            var bonus = owner.AddStatus(bonusMain.StatusID2, owner, 5, 2, 0, bonusMain); count.BoundStatuses.Add(bonus);
            void Hit(params BattleEntity[] targets)
            {
                using var batch = new HitBatch();
                foreach (var target in targets) batch.Resolve(new HitRequest { Target = target, DamageBeforeShield = 100,
                    Element = "Physical", Source = DamageSourceInfo.Create(env.Enemy.Entity, ReactionSourceKind.EnemySkill,
                        "SK_Test", "SE_Test", ReactionType.None) });
            }
            BattleExecution.Run(async () =>
            {
                Hit(owner, ally); await BattleExecution.Flush();
                Assert.That(count.RuntimeIntValue, Is.EqualTo(1));
                Assert.That(owner.Shields.Single().Value, Is.EqualTo(800).Within(.001));
                Hit(ally); await BattleExecution.Flush();
                Assert.That(count.RuntimeIntValue, Is.EqualTo(2));
                Assert.That(owner.Shields.Single().Value, Is.EqualTo(800 - 100 / 1.05f).Within(.001));
                var handler = new ZhongliFortifyCount();
                handler.ChangeFromEffect(count, owner, CountChangeRequest.Parse("full")); Assert.That(count.RuntimeIntValue, Is.EqualTo(5));
                handler.ChangeFromEffect(count, owner, CountChangeRequest.Parse("clear")); Assert.That(count.IsActive, Is.True);
                Assert.That(count.RuntimeIntValue, Is.Zero);
                owner.RemoveStatusInstance(jade); Assert.That(count.IsActive || bonus.IsActive, Is.False);
            }).GetAwaiter().GetResult();
        }

        [Test] public void ResonanceEnergy_FailedRollCannotRetryOtherTargets_AndTwoSuccessesCapAllSteles()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); env.Ally.Entity.EntityID = 1032;
            var other = env.CreateEnemy(2).Entity;
            var main = Main(env, 4103208, ZhongliScriptHooks.Rules);
            env.DataManager.StatusEffectDict["STE_Zhongli_Energy"] = new StatusEffectData
            { StatusEffectID = 410320801, StatusEffectID2 = "STE_Zhongli_Energy", EffectType = "GainEnergy", Element = "Geo",
                Param1 = "2", Param2 = "Based", TargetType = "Allies", TargetSelect = "-1", ExecuteCount = 1 };
            env.DataManager.StatusActionDict[main.StatusID2] = new List<StatusActionData> { new StatusActionData
            { StatusID2 = main.StatusID2, ActionType = "OnTrigger", Param1 = "STE_Zhongli_Energy",
                ScriptHook = "Zhongli_SteleEnergy()", MaxTimePerTurn = 2, ExecuteCount = 1 } };
            var rules = env.Ally.Entity.AddStatus(main.StatusID2, env.Ally.Entity, 0, 2, 0, main);
            var pillar = Stele(env, 1);
            var random = new SequenceBattleRandomSource(new[] { .9f, .1f, .1f }); BattleRandom.SetSource(random);
            BattleExecution.Run(async () =>
            {
                for (int i = 0; i < 4; i++)
                {
                    await GeoConstructStatusSystem.TryExecuteAsync(pillar, env.Ally.Entity, targets =>
                    {
                        using var batch = new HitBatch();
                        foreach (var target in targets) batch.Resolve(new HitRequest { Target = target, DamageBeforeShield = 1, Element = "Geo",
                            Source = DamageSourceInfo.FromStatus(pillar, ZhongliScriptHooks.Resonance) });
                        return Task.CompletedTask;
                    });
                    await BattleExecution.Flush();
                }
            }).GetAwaiter().GetResult();
            Assert.That(random.FloatCallCount, Is.EqualTo(3), "每hit一次，达到两次成功后不再抽取");
            Assert.That(rules.TurnTriggerCount, Is.EqualTo(2));
            Assert.That(other.CurrentHP, Is.EqualTo(996));
        }

        [Test] public void T2_UsesRealtimeHP_AndNeverAddsOriginalGameBurstHPBonus()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var owner = env.Ally.Entity; owner.EntityID = 1032;
            var normal = Main(env, 4103206, "ST_Zhongli_T2Normal", "Buff");
            normal.MultiplierPart = "BaseDMGBonusFlat"; normal.ApplyDamageType = "Normal;Heavy";
            var skill = Main(env, 4103207, "ST_Zhongli_T2Skill", "Buff");
            skill.MultiplierPart = "BaseDMGBonusFlat"; skill.ApplyString = "SE_Skill_Zhongli";
            var resonance = Main(env, 4103211, "ST_Zhongli_T2Resonance", "Buff");
            resonance.MultiplierPart = "BaseDMGBonusFlat"; resonance.ApplyString = "STE_Zhongli_Resonance";
            foreach (var pair in new[] { (normal, "0.0139"), (skill, "0.019"), (resonance, "0.019") })
            {
                string id = pair.Item1.StatusID2 + "_Effect";
                env.DataManager.StatusEffectDict[id] = new StatusEffectData
                { StatusEffectID = pair.Item1.StatusID * 100 + 1, StatusEffectID2 = id, EffectType = "Buff",
                    Param1 = pair.Item2, DamageHook = "Zhongli_MaxHPFlat()" };
                owner.AddStatus(pair.Item1.StatusID2, owner, 0, 2, 0, pair.Item1);
            }
            float Bonus(string id, string type)
            {
                using var query = DamageQueryContext.Enter(id, type);
                return owner.GetStatusBaseDMGBonusFlat(id, "", -1, "Geo", env.Enemy.Entity);
            }
            Assert.That(Bonus("SE_Normal_Zhongli", "Normal"), Is.EqualTo(owner.GetFinalHP() * .0139f).Within(.001));
            Assert.That(Bonus("SE_Skill_Zhongli", "Skill"), Is.EqualTo(owner.GetFinalHP() * .019f).Within(.001));
            Assert.That(Bonus("STE_Zhongli_Resonance", "Skill"), Is.EqualTo(owner.GetFinalHP() * .019f).Within(.001));
            Assert.That(Bonus("SE_Burst_Zhongli", "Burst"), Is.Zero);
            owner.TotalHP += 500;
            Assert.That(Bonus("SE_Heavy_Zhongli", "Heavy"), Is.EqualTo(owner.GetFinalHP() * .0139f).Within(.001));
        }
    }
}
