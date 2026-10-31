using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class XinyanScriptHooksTests
    {
        private static StatusMainData Main(BattleFlowTestEnv env, int id, string key, string type = null)
        {
            var main = new StatusMainData { StatusID = id, StatusID2 = key, StatusType = type,
                MaxStack = 1, MaxCount = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[key] = main; return main;
        }
        private static StatusMainData ConfigureShield(BattleFlowTestEnv env)
        {
            var main = Main(env, 4103301, XinyanScriptHooks.Shield, "Shield");
            var ids = new List<string>();
            for (int tier = 1; tier <= 3; tier++)
            {
                string key = "STE_Xinyan_Shield" + tier; ids.Add(key);
                env.DataManager.StatusEffectDict[key] = new StatusEffectData
                { StatusEffectID = 410330100 + tier, StatusEffectID2 = key, EffectType = "Shield", Element = "Pyro",
                    Param1 = (tier * 100).ToString(), Duration = 3, TargetType = "Self", ExecuteCount = 1,
                    ScriptHook = "Xinyan_ShieldTier(" + tier + ")" };
            }
            env.DataManager.StatusActionDict[main.StatusID2] = new List<StatusActionData> { new StatusActionData
            { StatusID2 = main.StatusID2, ActionType = "OnApply", Param1 = string.Join(";", ids), ExecuteCount = 1 } };
            return main;
        }

        [TestCase(0, false, 1)] [TestCase(2, false, 1)] [TestCase(3, false, 2)] [TestCase(4, false, 3)]
        [TestCase(0, true, 1)] [TestCase(1, true, 1)] [TestCase(2, true, 2)] [TestCase(3, true, 3)]
        public void ShieldThresholds_FollowProjectRatherThanOriginalGame(int hits, bool talent, int tier)
            => Assert.That(XinyanScriptHooks.SelectShieldTier(hits, talent), Is.EqualTo(tier));

        [Test] public void Shield_CountsDistinctCommittedTargets_AndC2ReplacesWithIndependentPartyShields()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var owner = env.Ally.Entity; owner.EntityID = 1033;
            var ally = env.CreateAlly(2).Entity;
            var targets = new[] { env.Enemy.Entity, env.CreateEnemy(2).Entity, env.CreateEnemy(3).Entity };
            var main = ConfigureShield(env); StatusInstance first = null, upgraded = null, allyShield = null;
            BattleExecution.Run(async () =>
            {
                using (BattleSpecialRules.BeginSkillExecution(owner, new SkillMainData { SkillID2 = "SK_Skill_Xinyan" }))
                {
                    foreach (var target in targets.Concat(new[] { targets[0] }))
                        BattleEventHub.Publish(new DamageEventData(new DamageResolvedEvent
                        { Target = target, HitLanded = true, ShieldAbsorbed = 10, ActualHPDamage = 0,
                            Source = DamageSourceInfo.Create(owner, ReactionSourceKind.CharacterSkill,
                                "SK_Skill_Xinyan", "SE_Skill_Xinyan") }));
                    await BattleExecution.Flush();
                    first = owner.AddStatus(main.StatusID2, owner, 3, 2, 3, main);
                    await BattleExecution.Flush();
                    Assert.That(first.RuntimeIntValue, Is.EqualTo(2));
                    Assert.That(owner.Shields.Single().Value, Is.EqualTo(200));
                }
                owner.ConstellationLevel = 2;
                using (BattleSpecialRules.BeginSkillExecution(owner, new SkillMainData { SkillID2 = "SK_Burst_Xinyan" }))
                {
                    upgraded = owner.AddStatus(main.StatusID2, owner, 3, 2, 3, main);
                    allyShield = ally.AddStatus(main.StatusID2, owner, 3, 2, 3, main);
                    await BattleExecution.Flush();
                }
                Assert.That(first.IsActive, Is.False);
                Assert.That(owner.Shields.Single().Value, Is.EqualTo(300));
                Assert.That(ally.Shields.Single().Value, Is.EqualTo(300));
                Assert.That(ScriptHookEvaluator.Evaluate("Xinyan_OwnShield()", new ScriptHookContext
                { Caster = owner, Status = allyShield, StatusHost = ally }), Is.False);
                using (var hit = new HitBatch())
                    hit.Resolve(new HitRequest { Target = owner, DamageBeforeShield = 400, Element = "Physical",
                        Source = DamageSourceInfo.Create(targets[0], ReactionSourceKind.EnemySkill, "enemy", "enemy") });
                await BattleExecution.Flush();
            }).GetAwaiter().GetResult();
            Assert.That(upgraded.IsActive, Is.False);
            Assert.That(allyShield.IsActive, Is.True);
            Assert.That(ally.Shields.Single().Value, Is.EqualTo(300));
        }

        [Test] public void C6_DefenseConversionIsRealtimeAndHeavyOnly_AndCostsStack()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); var owner = env.Ally.Entity; owner.EntityID = 1033;
            var main = Main(env, 4103307, "ST_Xinyan_C6", "Buff"); main.MultiplierPart = "ATKBonusFlat"; main.ApplyDamageType = "Heavy";
            var speed = Main(env, 4103305, "ST_Xinyan_C1Speed");
            env.DataManager.StatusEffectDict["STE_Xinyan_C6Attack"] = new StatusEffectData
            { StatusEffectID = 410330701, StatusEffectID2 = "STE_Xinyan_C6Attack", EffectType = "Buff", Param1 = "0.5", DamageHook = "Xinyan_Defense()" };
            env.DataManager.StatusEffectDict["STE_Xinyan_C6Cost"] = new StatusEffectData
            { StatusEffectID = 410330702, StatusEffectID2 = "STE_Xinyan_C6Cost", EffectType = "APCost", Param1 = "-5", Param2 = "Heavy" };
            env.DataManager.StatusEffectDict["STE_Xinyan_C1Cost"] = new StatusEffectData
            { StatusEffectID = 410330501, StatusEffectID2 = "STE_Xinyan_C1Cost", EffectType = "APCost", Param1 = "-5", Param2 = "Normal;Heavy" };
            owner.AddStatus(main.StatusID2, owner, 0, 2, 0, main); owner.AddStatus(speed.StatusID2, owner, 1, 2, 0, speed);
            float Query(string type)
            {
                using var scope = DamageQueryContext.Enter("test", type);
                return owner.GetStatusATKBonusFlat("test", "", 1, "Physical");
            }
            Assert.That(Query("Heavy"), Is.EqualTo(owner.GetFinalDEF() * .5f));
            Assert.That(Query("Normal"), Is.Zero); Assert.That(Query("Skill"), Is.Zero);
            owner.TotalDEF += 120;
            Assert.That(Query("Heavy"), Is.EqualTo(owner.GetFinalDEF() * .5f));
            Assert.That(APCostResolver.ForSkill(owner, new SkillMainData { APCost = 35 }, 1), Is.EqualTo(25));
            Assert.That(APCostResolver.ForSkill(owner, new SkillMainData { APCost = 25 }, 0), Is.EqualTo(20));
        }

        [Test] public void C2_OnlyForcesItsPhysicalBurst_WithoutChangingPanel()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); var owner = env.Ally.Entity; owner.EntityID = 1033;
            owner.ConstellationLevel = 2; owner.CritRate = .17f;
            Assert.That(XinyanScriptHooks.ForcesCritical(owner, "SE_Burst_Xinyan", "Physical"), Is.True);
            Assert.That(XinyanScriptHooks.ForcesCritical(owner, "STE_Xinyan_BurstPulse", "Pyro"), Is.False);
            Assert.That(XinyanScriptHooks.ForcesCritical(owner, "SE_Normal_Xinyan", "Physical"), Is.False);
            Assert.That(owner.CritRate, Is.EqualTo(.17f));
            owner.ConstellationLevel = 1;
            Assert.That(XinyanScriptHooks.ForcesCritical(owner, "SE_Burst_Xinyan", "Physical"), Is.False);
        }
    }
}
