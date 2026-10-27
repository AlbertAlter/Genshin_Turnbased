using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>完整编队、动态属性及纸伞来源隔离的定向回归；不依赖策划源表。</summary>
    public sealed class WeaponRosterAndMarkTests
    {
        private static StatusMainData Main(BattleFlowTestEnv env, int id, string part = null, string hook = null, string type = null)
        {
            var main = new StatusMainData { StatusID = id, StatusID2 = DataManager.WeaponStatusKey(id),
                StatusName = "WeaponRosterTest", StatusType = "Buff", MultiplierPart1 = part, ScriptHook = hook,
                ApplyDamageType = type, MaxCount = 1, MaxStack = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            return main;
        }
        private static StatusEffectData Effect(BattleFlowTestEnv env, int id, string type = "Buff", string parameter = "Index(1)")
        {
            var effect = new StatusEffectData { StatusEffectID = id, StatusEffectID2 = "STE_Weapon_" + id,
                EffectType = type, Param1 = parameter, TargetType = "Self" };
            env.DataManager.StatusEffectDict[effect.StatusEffectID2] = effect;
            return effect;
        }
        private static WeaponRuntimeContext Weapon(BattleEntity owner, int weapon, params float[] parameters)
        {
            var context = new WeaponRuntimeContext { Equipper = owner, WeaponID = weapon, Refinement = 1,
                SourceInstanceID = "WeaponRoster:" + weapon + ":" + owner.EntityID };
            for (int i = 0; i < parameters.Length; i++) context.ResolvedParams[i + 1] = parameters[i];
            return context;
        }
        private static StatusInstance Add(BattleFlowTestEnv env, BattleEntity target, int id, WeaponRuntimeContext weapon, int duration = 0)
            => target.AddStatus(DataManager.WeaponStatusKey(id), weapon.Equipper, duration, 2, 0,
                env.DataManager.StatusMainDict[DataManager.WeaponStatusKey(id)], weapon);
        private static CharacterOverviewData Identity(BattleFlowTestEnv env, BattleEntity entity, int id, string nation, string element)
        {
            entity.EntityID = id;
            var metadata = new CharacterOverviewData { CharacterID = id, Nation = nation, Element = element };
            env.DataManager.CharacterOverviewList.Add(metadata);
            return metadata;
        }
        private static void Start(BattleFlowTestEnv env)
        {
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            BattleRandom.SetSource(new SequenceBattleRandomSource());
        }

        [Test]
        public void ChainBreakerRosterConditions_UseFormalElementsAndUnion_NotFieldOrLivingCount()
        {
            using var env = new BattleFlowTestEnv();
            Main(env, 4310101, "ATKBonus"); Main(env, 4310102, "EMBonus", "Hook(4310102)");
            Main(env, 4312001, "EMBonus", "Hook(4312001)");
            Effect(env, 431010101).DamageHook = "Hook(431010101)";
            Effect(env, 431010201, parameter: "Index(2)"); Effect(env, 431200101);
            env.CreateMinimalBattle(); var owner = env.Ally.Entity;
            var ownID = Identity(env, owner, 990011, "纳塔", "Pyro");
            var second = Identity(env, env.CreateAlly(2).Entity, 990012, "纳塔", "Hydro");
            var third = Identity(env, env.CreateAlly(3).Entity, 990013, "纳塔", "Pyro");
            var dead = env.CreateAlly(4).Entity; Identity(env, dead, 990014, "蒙德", "Cryo"); dead.CurrentHP = 0f;
            var chain = Weapon(owner, 3101, .048f, 24f); var fjord = Weapon(owner, 3120, 120f);
            Add(env, owner, 4310101, chain); Add(env, owner, 4310102, chain); Add(env, owner, 4312001, fjord);
            Assert.That(owner.GetStatusATKBonus("", "", -1, ""), Is.EqualTo(.192f).Within(.000001f),
                "纳塔且异元素的同一人只算一次，装备者为纳塔也算，阵亡后台仍在编队");
            Assert.That(owner.GetFinalEM(), Is.EqualTo(owner.TotalEM + 144f));
            ownID.Nation = third.Nation = "蒙德"; // 只剩水、冰两名满足碎链；峡湾仍有三种元素。
            Assert.That(owner.GetStatusATKBonus("", "", -1, ""), Is.EqualTo(.096f).Within(.000001f));
            Assert.That(owner.GetFinalEM(), Is.EqualTo(owner.TotalEM + 120f));
            second.Element = "Pyro"; // 空白/None不能提供第三种有效元素。
            Assert.That(owner.GetFinalEM(), Is.EqualTo(owner.TotalEM));
            second.Element = "None"; Assert.That(owner.GetFinalEM(), Is.EqualTo(owner.TotalEM));
            second.Element = ""; Assert.That(owner.GetFinalEM(), Is.EqualTo(owner.TotalEM));
        }

        [TestCase(3066), TestCase(3092), TestCase(3117)]
        public void WatatsumiWeapons_SumMaximumEnergyOnceAcrossRoster_ThenCap_OnlyActualBurst(int weapon)
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(0, "SK_Roster", "SE_Roster", "Physical", ".1*TotalATK,0", 0);
            int id = (40000 + weapon) * 100 + 1;
            Main(env, id, "DMGBonus", type: "Burst"); Effect(env, id * 100 + 1).DamageHook = "Hook(" + (id * 100 + 1) + ")";
            env.CreateMinimalBattle(); var owner = env.Ally.Entity; owner.MaxEnergy = 40; owner.CurrentEnergy = 0f;
            var other = env.CreateAlly(2).Entity; other.EntityID = 990012; other.MaxEnergy = 80; other.CurrentEnergy = 0f;
            var dead = env.CreateAlly(3).Entity; dead.EntityID = 990013; dead.MaxEnergy = 90; dead.CurrentHP = 0f;
            Add(env, owner, id, Weapon(owner, weapon, .0012f, .4f));
            using (DamageQueryContext.Enter("SE_Roster", "Burst"))
                Assert.That(owner.GetStatusDMGBonus("SE_Roster", "SK_Roster", 0, "Physical", env.Enemy.Entity),
                    Is.EqualTo(.252f).Within(.000001f));
            other.MaxEnergy = 400;
            using (DamageQueryContext.Enter("SE_Roster", "Burst"))
                Assert.That(owner.GetStatusDMGBonus("SE_Roster", "SK_Roster", 0, "Physical", env.Enemy.Entity), Is.EqualTo(.4f));
            using (DamageQueryContext.Enter("SE_Roster", "Normal"))
                Assert.That(owner.GetStatusDMGBonus("SE_Roster", "SK_Roster", 3, "Physical", env.Enemy.Entity), Is.Zero);
        }

        [Test]
        public void WaveridingHPAndRingNormalBonus_AreDynamic_WithExtraOnlyCapAndFractionalThousands()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(0, "SK_Ring", "SE_Ring", "Physical", ".1*TotalATK,0", 0);
            Main(env, 4315002, "HPBonus"); Effect(env, 431500201).DamageHook = "Hook(431500201)";
            Main(env, 4314802, "DMGBonus", type: "Normal"); Effect(env, 431480201).DamageHook = "Hook(431480201)";
            env.CreateMinimalBattle(); var owner = env.Ally.Entity; Identity(env, owner, 990011, "蒙德", "Pyro");
            var other = env.CreateAlly(2).Entity; var identity = Identity(env, other, 990012, "枫丹", "Hydro");
            var dead = env.CreateAlly(3).Entity; Identity(env, dead, 990013, "枫丹", "Hydro"); dead.CurrentHP = 0;
            var hp = Add(env, owner, 4315002, Weapon(owner, 3150, .2f, .12f, .24f), 3);
            Add(env, owner, 4314802, Weapon(owner, 3148, .006f, .16f), 3);
            const float basis = 1000f; // 内存角色基础生命，无突破和构筑加成；不调用被测HP换算反算期望值。
            float expected = 1440f;
            Assert.That(owner.GetFinalHP(), Is.EqualTo(expected).Within(.001f));
            using (DamageQueryContext.Enter("SE_Ring", "Normal"))
                Assert.That(owner.GetStatusDMGBonus("SE_Ring", "SK_Ring", 3, "Physical", env.Enemy.Entity),
                    Is.EqualTo(expected / 1000f * .006f).Within(.000001f));
            identity.Element = "Pyro";
            Assert.That(owner.GetFinalHP(), Is.EqualTo(owner.TotalHP + basis * .32f).Within(.001f));
            Assert.That(hp.RemainingPhaseCount, Is.EqualTo(3), "只读编队变化不能刷新窗口");
            owner.TotalHP = 40000f;
            using (DamageQueryContext.Enter("SE_Ring", "Normal"))
                Assert.That(owner.GetStatusDMGBonus("SE_Ring", "SK_Ring", 1, "Physical", env.Enemy.Entity), Is.EqualTo(.16f));
            using (DamageQueryContext.Enter("SE_Ring", "Heavy"))
                Assert.That(owner.GetStatusDMGBonus("SE_Ring", "SK_Ring", 0, "Physical", env.Enemy.Entity), Is.Zero);
            owner.CurrentHP = 40100f;
            owner.RemoveStatusInstance(hp, "Expired");
            Assert.That(owner.CurrentHP, Is.EqualTo(40000f), "生命Buff真实移除后裁切到新上限，不由属性查询隐式裁切");
        }

        [Test, Timeout(7000)]
        public void Shigure_MarksRefreshPerSource_AnyKillerOnlyClearsMatchingHostOwnersCooldown()
        {
            using var env = new BattleFlowTestEnv();
            Main(env, 4304202); Main(env, 4304203); Main(env, 4304204, "DMGBonus", "Hook(4304204)");
            Effect(env, 430420401);
            Effect(env, 430420301, "RemoveStatus", "ST_Weapon_4304202");
            env.DataManager.StatusActionDict["ST_Weapon_4304203"] = new List<StatusActionData>
            { new StatusActionData { StatusID2 = "ST_Weapon_4304203", ActionType = "OnTrigger",
                ScriptHook = "Kill(Any);Hook(4304203)", Param1 = "STE_Weapon_430420301" } };
            env.CreateMinimalBattle(); var owner = env.Ally.Entity; var other = env.CreateAlly(2).Entity;
            other.EntityID = 990012; var killer = env.CreateAlly(3).Entity; killer.EntityID = 990013;
            var second = env.CreateEnemy(2).Entity; var firstWeapon = Weapon(owner, 3042, .16f); var secondWeapon = Weapon(other, 3042, .32f);
            Add(env, owner, 4304204, firstWeapon); Add(env, other, 4304204, secondWeapon);
            Add(env, owner, 4304202, firstWeapon, 4); Add(env, other, 4304202, secondWeapon, 4);
            var old = Add(env, env.Enemy.Entity, 4304203, firstWeapon, 3);
            var foreign = Add(env, env.Enemy.Entity, 4304203, secondWeapon, 3);
            var current = Add(env, env.Enemy.Entity, 4304203, firstWeapon, 3);
            Assert.That(env.Enemy.Entity.GetStatusInstances("ST_Weapon_4304203"), Has.Count.EqualTo(2));
            Assert.That(old.IsActive, Is.False); Assert.That(foreign.IsActive, Is.True);
            Assert.That(owner.GetStatusDMGBonus("", "", -1, "Physical", env.Enemy.Entity), Is.EqualTo(.16f));
            Assert.That(other.GetStatusDMGBonus("", "", -1, "Physical", env.Enemy.Entity), Is.EqualTo(.32f));
            Assert.That(owner.GetStatusDMGBonus("", "", -1, "Physical", second), Is.Zero);
            // 另一装备者改为标记另一名敌人；自然移除不重置其冷却。
            env.Enemy.Entity.RemoveStatusInstance(foreign); Add(env, second, 4304203, secondWeapon, 3);
            Assert.That(other.HasStatus("ST_Weapon_4304202"), Is.True);
            Start(env);
            env.Enemy.Entity.TakeDamage(5000f, DamageSourceInfo.Create(killer, ReactionSourceKind.CharacterSkill, "SK_Other", "SE_Other"));
            Assert.That(owner.HasStatus("ST_Weapon_4304202"), Is.False);
            Assert.That(other.HasStatus("ST_Weapon_4304202"), Is.True, "另一死者的标记不能领本次死亡");
            Assert.That(current.IsActive, Is.True, "目标死亡不会提前伪造标记到期；死亡钩子可读取标记");
        }

        [TestCase(3076), TestCase(3121), TestCase(3123), Timeout(7000)]
        public void FixedEnergyHooks_ResolveSourceIndex_AndUseOneFlatFractionalGainDeclaration(int weapon)
        {
            using var env = new BattleFlowTestEnv(); int id = (40000 + weapon) * 100 + 1;
            Main(env, id); var effect = Effect(env, id * 100 + 1, "GainEnergy");
            effect.Param2 = "Flat"; effect.DamageHook = "Hook(" + effect.StatusEffectID + ")";
            env.DataManager.StatusActionDict[DataManager.WeaponStatusKey(id)] = new List<StatusActionData>
            { new StatusActionData { StatusID2 = DataManager.WeaponStatusKey(id), ActionType = "OnItsTurn", Param1 = effect.StatusEffectID2 } };
            env.CreateMinimalBattle(); var owner = env.Ally.Entity; owner.MaxEnergy = 60; owner.CurrentEnergy = 1.25f;
            owner.TotalRechargeRate = 3f; Start(env); int declarations = 0;
            using var listener = BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged, (_, fact) =>
            { if (fact.Resource == "Energy" && fact.Subject == owner) declarations++; }, 0);
            owner.AddStatus(DataManager.WeaponStatusKey(id), owner, 2, 3, 3,
                env.DataManager.StatusMainDict[DataManager.WeaponStatusKey(id)], Weapon(owner, weapon, 8.5f));
            env.Manager.EndAllyTurn();
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);
            Assert.That(owner.CurrentEnergy, Is.EqualTo(9.75f)); Assert.That(declarations, Is.EqualTo(1));
        }
    }
}
