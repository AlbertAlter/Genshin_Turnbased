using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>Splash是同hit的扩展目标，不是中心最终伤害的复制；按项目规则只编写、不自动运行。</summary>
    public sealed class SplashTargetRegressionTests
    {
        private const string Effect = "SE_SplashTargets";
        private const string Status = "ST_SplashTargets";
        private static void Configure(BattleFlowTestEnv env, bool status, string element, string splash, string targetCount = "1")
        {
            if (!status)
            {
                env.ConfigureAllyAction(2, "SK_SplashTargets", Effect, element, "1*TotalATK,1", 10);
                env.DataManager.SkillEffectDict[Effect].Param2 = splash;
                if (targetCount == "-1") env.DataManager.SkillEffectDict[Effect].TargetNumber = -1;
                return;
            }
            env.ConfigureStatus(Status, actionType: "OnTrigger", actionEffectID2: Effect);
            env.DataManager.StatusEffectDict[Effect] = new StatusEffectData
            {
                StatusEffectID2 = Effect, EffectType = "Damage", Element = element, DamageType = "Skill",
                Param1 = "1*TotalATK,1", Param2 = splash, TargetType = "Enemy", TargetSelect = targetCount,
                TargetConsecutive = 1
            };
        }
        private static void Start(BattleFlowTestEnv env)
        { env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction); }
        private static void Execute(BattleFlowTestEnv env, bool status)
        {
            if (!status) { Assert.That(env.Manager.UseSkillBySlot(0), Is.True); return; }
            var instance = env.Ally.Entity.AddStatus(Status, env.Ally.Entity, 0, 2, 0, env.DataManager.StatusMainDict[Status]);
            Assert.That(env.Ally.TryExecuteEventStatusAction(instance, env.DataManager.StatusActionDict[Status][0],
                env.Ally.Entity, new ScriptHookContext { Caster = env.Ally.Entity }), Is.True);
        }

        [TestCase(false), TestCase(true), Timeout(7000)]
        public void Splash_UsesOwnResistanceDefenseReactionAndCrit_ButKeepsFullAuraAndOneHit(bool status)
        {
            using var env = new BattleFlowTestEnv(); Configure(env, status, "Pyro", "Splash(0.5;0,1)");
            env.CreateMinimalBattle(); var adjacent = env.CreateEnemy(2).Entity;
            adjacent.Level = 101; adjacent.PyroRes = .2f;
            env.Ally.Entity.CritRate = .5f; env.Ally.Entity.CritDMG = 1f;
            Start(env); env.Enemy.Entity.ApplyAura("Hydro", 1f, env.Ally.Entity.EntityID);
            // 使用既有冷刃条件钩子：仅中心水附着满足20%增伤，不能传给无附着的邻格。
            var main = new StatusMainData { StatusID = 4300201, StatusID2 = "ST_Weapon_4300201", StatusType = "Buff",
                MultiplierPart1 = "DMGBonus", ScriptHook = "Hook(4300201)", MaxStack = 1 };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            env.DataManager.StatusEffectDict["STE_Weapon_430020101"] = new StatusEffectData
            { StatusEffectID = 430020101, StatusEffectID2 = "STE_Weapon_430020101", EffectType = "Buff", Param1 = "Index(1)" };
            var weapon = new WeaponRuntimeContext { WeaponID = 3002, Equipper = env.Ally.Entity, Refinement = 1,
                SourceInstanceID = "SplashCondition" }; weapon.ResolvedParams[1] = .2f;
            env.Ally.Entity.AddStatus(main.StatusID2, env.Ally.Entity, 0, 2, 0, main, weapon);
            var damages = new List<DamageEventData>(); var headers = new List<BattleEventHeader>();
            var contacts = new List<ElementContactEventData>();
            using var damageListener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (header, damage) =>
            { if (damage.Source.SourceEffectID == Effect && damage.Source.SourceKind != ReactionSourceKind.DerivedReaction)
                { damages.Add(damage); headers.Add(header); } }, 0);
            using var contactListener = BattleEventHub.Subscribe<ElementContactEventData>(BattleEventKind.ElementContactResolved,
                (_, contact) => { if (contact.EffectId == Effect) contacts.Add(contact); }, 0);
            var random = new SequenceBattleRandomSource(new[] { .9f, .1f }); BattleRandom.SetSource(random);
            Execute(env, status);
            Assert.That(damages, Has.Count.EqualTo(2));
            Assert.That(damages[0].DamageBeforeShield, Is.EqualTo(90f).Within(.001f), "中心20%增伤后蒸发，不暴击");
            // 邻格50基础×101/(101+201)防御×0.8抗性×2暴击；不能继承中心蒸发或防抗。
            Assert.That(damages[1].DamageBeforeShield, Is.EqualTo(50f * 101f / 302f * .8f * 2f).Within(.001f));
            Assert.That(damages[0].Source.ReactionType, Is.EqualTo(ReactionType.Vaporize));
            Assert.That(damages[1].Source.ReactionType, Is.EqualTo(ReactionType.None));
            Assert.That(contacts, Has.Count.EqualTo(2));
            Assert.That(contacts.Select(x => x.RequestedAmount), Is.All.EqualTo(1f));
            Assert.That(damages.Select(x => x.ElementAmount), Is.All.EqualTo(1f));
            Assert.That(adjacent.GetAura("Pyro").AuraAmount, Is.EqualTo(1f).Within(.001f));
            Assert.That(headers[1].HitId, Is.EqualTo(headers[0].HitId).And.GreaterThan(0));
            Assert.That(headers.Select(x => x.TargetIndex), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(headers[1].EffectExecutionId, Is.EqualTo(headers[0].EffectExecutionId).And.GreaterThan(0));
            Assert.That(random.FloatCallCount, Is.EqualTo(2));
        }

        [TestCase(false), TestCase(true), Timeout(7000)]
        public void Splash_PreparesItsOwnBaseAddition_WithoutCopyingOrScalingTheCentersContribution(bool status)
        {
            using var env = new BattleFlowTestEnv(); Configure(env, status, "Physical", "Splash(0.5;0,1)");
            env.CreateMinimalBattle(); env.CreateEnemy(2); env.Ally.Entity.TotalDEF = 100f; Start(env);
            var main = new StatusMainData { StatusID = 4303801, StatusID2 = "ST_Weapon_4303801", StatusType = "Buff",
                MultiplierPart1 = "BaseDMGBonusFlat", ScriptHook = "Hook(4303801)", ApplyDamageType = "Skill", MaxStack = 1 };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            env.DataManager.StatusEffectDict["STE_Weapon_430380101"] = new StatusEffectData
            { StatusEffectID = 430380101, StatusEffectID2 = "STE_Weapon_430380101", EffectType = "Buff",
                Param1 = "Index(1)*TotalDEF", DamageHook = "Hook(430380101)" };
            var weapon = new WeaponRuntimeContext { WeaponID = 3038, Equipper = env.Ally.Entity, Refinement = 1,
                SourceInstanceID = "SplashAddition" }; weapon.ResolvedParams[1] = .4f;
            env.Ally.Entity.AddStatus(main.StatusID2, env.Ally.Entity, 0, 2, 0, main, weapon);
            var direct = new List<DamageEventData>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, damage) =>
            { if (damage.Source.SourceEffectID == Effect) direct.Add(damage); }, 0);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random); Execute(env, status);
            // 中心(100+40)*0.5=70；邻格(100*0.5+40)*0.5=45。每个CountDamagePlan独立提供40。
            Assert.That(direct.Select(x => x.DamageBeforeShield), Is.EqualTo(new[] { 70f, 45f }));
            Assert.That(random.FloatCallCount, Is.EqualTo(2));
        }

        [TestCase(false), TestCase(true), Timeout(7000)]
        public void Splash_AnemoUsesExpandedTargetsAndScaledDirectDamage_WithIndependentSwirlBatch(bool status)
        {
            using var env = new BattleFlowTestEnv(); Configure(env, status, "Anemo", "Splash(0.5;0,1)");
            env.CreateMinimalBattle(); var adjacent = env.CreateEnemy(2).Entity;
            Start(env); env.Enemy.Entity.ApplyAura("Pyro", 1f, env.Ally.Entity.EntityID);
            adjacent.ApplyAura("Hydro", 1f, env.Ally.Entity.EntityID);
            var direct = new List<DamageEventData>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, damage) =>
            { if (damage.Source.SourceEffectID == Effect && damage.Source.SourceKind != ReactionSourceKind.DerivedReaction) direct.Add(damage); }, 0);
            var random = new SequenceBattleRandomSource(new[] { .9f, .9f }); BattleRandom.SetSource(random);
            Execute(env, status);
            Assert.That(direct.Select(x => x.Subject), Is.EqualTo(new[] { env.Enemy.Entity, adjacent }));
            Assert.That(direct.Select(x => x.DamageBeforeShield), Is.EqualTo(new[] { 50f, 25f }));
            Assert.That(direct.Select(x => x.ElementAmount), Is.All.EqualTo(1f));
            Assert.That(direct.Select(x => x.Source.SourceEntity), Is.All.SameAs(env.Ally.Entity));
            Assert.That(random.FloatCallCount, Is.EqualTo(2), "派生扩散不额外判直伤暴击");
        }

        [TestCase(false), TestCase(true), Timeout(7000)]
        public void Splash_OverlappingRangesDoNotDuplicatePrimaryTargetsOrAura(bool status)
        {
            using var env = new BattleFlowTestEnv(); Configure(env, status, "Hydro", "Splash(0.5;1,1)", "-1");
            env.CreateMinimalBattle(); var second = env.CreateEnemy(2).Entity; var third = env.CreateEnemy(3).Entity;
            Start(env); var direct = new List<DamageEventData>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, damage) =>
            { if (damage.Source.SourceEffectID == Effect) direct.Add(damage); }, 0);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random); Execute(env, status);
            Assert.That(direct.Select(x => x.Subject), Is.EquivalentTo(new[] { env.Enemy.Entity, second, third }));
            Assert.That(direct.Select(x => x.DamageBeforeShield), Is.All.EqualTo(50f), "已是主目标的单位保留全倍率");
            Assert.That(direct.Select(x => x.Subject.GetAura("Hydro").AuraAmount), Is.All.EqualTo(1f));
            Assert.That(random.FloatCallCount, Is.EqualTo(3));
        }

        [Test, Timeout(7000)]
        public void EnemySplash_ExpandsOnActualTargetSideAndKeepsAura()
        {
            using var env = new BattleFlowTestEnv(); env.ConfigureEnemyDamageSkill(1f, "Hydro", 1f);
            env.DataManager.EnemySkillEffectDict["SE_Enemy_BattleFlow"].Param2 = "Splash(0.5;0,1)";
            env.CreateMinimalBattle(); var adjacent = env.CreateAlly(2).Entity; adjacent.HydroRes = .2f;
            Start(env); var direct = new List<DamageEventData>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, damage) =>
            { if (damage.Source.SourceEffectID == "SE_Enemy_BattleFlow") direct.Add(damage); }, 0);
            BattleRandom.SetSource(new SequenceBattleRandomSource()); env.Manager.EndAllyTurn();
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPostTurn);
            Assert.That(direct.Select(x => x.Subject), Is.EqualTo(new[] { env.Ally.Entity, adjacent }));
            Assert.That(direct[1].DamageBeforeShield, Is.EqualTo(direct[0].DamageBeforeShield * .5f * .8f).Within(.001f));
            Assert.That(direct.Select(x => x.ElementAmount), Is.All.EqualTo(1f));
            Assert.That(adjacent.GetAura("Hydro").AuraAmount, Is.EqualTo(1f));
        }
    }
}
