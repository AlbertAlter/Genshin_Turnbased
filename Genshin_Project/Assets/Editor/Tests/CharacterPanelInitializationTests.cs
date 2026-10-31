using System.Collections.Generic;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>角色裸面板与最终属性的分界；突破按门槛筛选，不在测试中读取策划源表。</summary>
    public sealed class CharacterPanelInitializationTests
    {
        [TestCase(1, false, 0), TestCase(1, true, 0)]
        [TestCase(19, false, 0), TestCase(20, false, 0), TestCase(20, true, 1)]
        [TestCase(21, false, 1), TestCase(39, false, 1), TestCase(40, false, 1)]
        [TestCase(40, true, 2), TestCase(41, false, 2)]
        public void Ascension_InitialRowAlwaysApplies_FutureRowsDoNot_ExactBoundaryNeedsFlag(int level, bool ascended, int tier)
        {
            using var env = new BattleFlowTestEnv(); Ascensions(env); env.CreateMinimalBattle();
            env.Ally.Init(BattleFlowTestEnv.AllyID, level, ascended); var entity = env.Ally.Entity;
            Assert.That(entity.IsAscended, Is.EqualTo(ascended));
            Assert.That(entity.TotalATK, Is.EqualTo(new[] { 110f, 130f, 170f }[tier]), "裸面板只保存角色攻击白值");
            Assert.That(entity.CharacterATKBonus, Is.EqualTo(new[] { .1f, .3f, .7f }[tier]).Within(.0001f));
            Assert.That(entity.GetFinalATK("", "", -1, ""), Is.EqualTo(new[] { 121f, 169f, 289f }[tier]).Within(.001f));
            Assert.That(entity.GetFinalHP(), Is.EqualTo(new[] { 1210f, 1690f, 2890f }[tier]).Within(.001f));
            Assert.That(entity.CurrentHP, Is.EqualTo(entity.GetFinalHP()).Within(.001f));
            Assert.That(entity.GetFinalDEF(), Is.EqualTo(new[] { 132f, 195f, 340f }[tier]).Within(.001f));
            Assert.That(entity.GetFinalEM(), Is.EqualTo(new[] { 5f, 20f, 50f }[tier]));
            Assert.That(entity.GetFinalRechargeRate(), Is.EqualTo(new[] { 1.03f, 1.1f, 1.2f }[tier]).Within(.0001f));
            Assert.That(entity.HealBonus, Is.EqualTo(new[] { .01f, .0654f, .1208f }[tier]).Within(.0001f));
            Assert.That(entity.CritRate, Is.EqualTo(new[] { .01f, .03f, .06f }[tier]).Within(.0001f));
            Assert.That(entity.CritDMG, Is.EqualTo(new[] { .02f, .06f, .1f }[tier]).Within(.0001f));
            Assert.That(entity.PyroDmgBonus, Is.EqualTo(new[] { .04f, .1f, .3f }[tier]).Within(.0001f));
            Assert.That(entity.PhysicalDmgBonus, Is.EqualTo(new[] { .05f, .12f, .42f }[tier]).Within(.0001f));
            Assert.That(entity.HydroDmgBonus, Is.EqualTo(tier == 2 ? .4f : 0f));
            Assert.That(entity.ElectroDmgBonus, Is.EqualTo(tier == 2 ? .5f : 0f));
            Assert.That(entity.CryoDmgBonus, Is.EqualTo(tier == 2 ? .6f : 0f));
            Assert.That(entity.AnemoDmgBonus, Is.EqualTo(tier == 2 ? .7f : 0f));
            Assert.That(entity.GeoDmgBonus, Is.EqualTo(tier == 2 ? .8f : 0f));
            Assert.That(entity.DendroDmgBonus, Is.EqualTo(tier == 2 ? .9f : 0f));
            Assert.That(env.DataManager.CharacterAscensionDict[BattleFlowTestEnv.AllyID], Has.Count.EqualTo(3),
                "只筛选本角色本次构筑，不能裁剪共享配表");
            env.Ally.Init(BattleFlowTestEnv.AllyID, 1); // 两参数兼容入口默认当前门槛未突破；初始行仍生效。
            Assert.That(entity.GetFinalATK("", "", -1, ""), Is.EqualTo(121f).Within(.001f));
            Assert.That(entity.GetFinalHP(), Is.EqualTo(1210f).Within(.001f));
            Assert.That(entity.HealBonus, Is.EqualTo(.01f).Within(.0001f));
            Assert.That(entity.IsAscended, Is.False);
        }

        [Test]
        public void GrowthAppliesBeforeAscensionFlats_AndHPDEFAttackBuffsUseTheSameEligibleBase()
        {
            using var env = new BattleFlowTestEnv(); Ascensions(env);
            env.DataManager.GrowthCurveDict[1] = new Dictionary<int, float> { [20] = 2f };
            env.CreateMinimalBattle(); env.Ally.Init(BattleFlowTestEnv.AllyID, 20, true);
            var entity = env.Ally.Entity;
            Assert.That(entity.GetFinalHP(), Is.EqualTo(2990f).Within(.001f)); // (1000*2+100+200)*1.3
            Assert.That(entity.GetFinalDEF(), Is.EqualTo(325f).Within(.001f)); // (100*2+20+30)*1.3
            Assert.That(entity.GetFinalATK("", "", -1, ""), Is.EqualTo(299f).Within(.001f));
            var health = Buff(env, 459977, "HPBonus", ".2");
            var defense = Buff(env, 459978, "DEFBonus", ".2");
            var attack = Buff(env, 459979, "ATKBonus", ".2");
            foreach (var main in new[] { health, defense, attack })
                entity.AddStatus(main.StatusID2, entity, 0, 2, 0, main);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            env.Manager.StartBattle();
            Assert.That(entity.GetFinalHP(), Is.EqualTo(3450f).Within(.001f));
            Assert.That(entity.CurrentHP, Is.EqualTo(3450f).Within(.001f));
            Assert.That(entity.GetFinalDEF(), Is.EqualTo(375f).Within(.001f));
            Assert.That(entity.GetFinalATK("", "", -1, ""), Is.EqualTo(345f).Within(.001f));
            Assert.That(random.FloatCallCount, Is.Zero); Assert.That(random.IntCallCount, Is.Zero);
        }

        [TestCase(true), TestCase(false)]
        public void AscensionWeaponAndArtifactPercentages_AddTogether_OnCombinedWhiteATK_FlatATKStaysOutside(bool weaponFirst)
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(0, "SK_PanelAttack", "SE_PanelAttack", "Physical", "1*TotalATK,0", 0);
            env.DataManager.CharacterAscensionDict[BattleFlowTestEnv.AllyID] = new List<CharacterAscensionData>
            { new CharacterAscensionData { AscensionLevel = 1, ATKBonus = .3f },
              new CharacterAscensionData { AscensionLevel = 80, BaseATKFlat = 999f, ATKBonus = 9f } };
            env.DataManager.WeaponAttributesDict[3001] = new WeaponAttributesData
            { WeaponID = 3001, WeaponType = 0, BaseATK = 40f, SecondaryAttribute = "ATKBonus", BaseSecondaryAttribute = .2f };
            var percent = Buff(env, 459975, "ATKBonus", ".5"); var flat = Buff(env, 459976, "ATKBonusFlat", "10");
            env.DataManager.ArtifactInitiateStatusDict[501] = new Dictionary<int, List<string>>
            { [2] = new List<string> { percent.StatusID2, flat.StatusID2 } };
            env.CreateMinimalBattle();
            if (weaponFirst) Assert.That(env.Ally.EquipWeapon(new WeaponLoadout { WeaponID = 3001 }), Is.True);
            env.Ally.InitializeArtifactSetStatuses(new Dictionary<int, int> { [501] = 2 });
            if (!weaponFirst) Assert.That(env.Ally.EquipWeapon(new WeaponLoadout { WeaponID = 3001 }), Is.True);
            var entity = env.Ally.Entity; var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            Assert.That(entity.TotalATK, Is.EqualTo(100f)); Assert.That(entity.WeaponATK, Is.EqualTo(40f));
            Assert.That(entity.GetFinalATK("", "", -1, ""), Is.EqualTo(290f).Within(.001f),
                "(100+40)*(1+.3+.2+.5)+10=290，固定攻击不受百分比再放大");
            float dealt = -1f;
            using var hit = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            { if (fact.Source.SourceEffectID == "SE_PanelAttack") dealt = fact.DamageBeforeShield; }, 0);
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            Assert.That(dealt, Is.EqualTo(145f).Within(.001f));
            Assert.That(random.FloatCallCount, Is.EqualTo(1)); Assert.That(random.IntCallCount, Is.Zero);
            var temporary = Buff(env, 459977, "ATKBonus", ".1");
            var status = entity.AddStatus(temporary.StatusID2, entity, 1, 2, 0, temporary);
            Assert.That(entity.GetFinalATK("", "", -1, ""), Is.EqualTo(304f).Within(.001f));
            entity.RemoveStatusInstance(status);
            Assert.That(entity.GetFinalATK("", "", -1, ""), Is.EqualTo(290f).Within(.001f));
            Assert.That(entity.TotalATK, Is.EqualTo(100f), "查询和临时Buff不能反复改写白值");
        }

        [Test]
        public void ExtraBaseStats_AreInsideAllPercentages_WhileFlatBonusesStayOutside()
        {
            using var env = new BattleFlowTestEnv();
            env.DataManager.CharacterAscensionDict[BattleFlowTestEnv.AllyID] = new List<CharacterAscensionData>
            { new CharacterAscensionData { AscensionLevel = 1, HPBonus = .1f, DEFBonus = .2f, ATKBonus = .3f } };
            env.DataManager.WeaponAttributesDict[3001] = new WeaponAttributesData
            { WeaponID = 3001, WeaponType = 0, BaseATK = 40f, SecondaryAttribute = "HPBonus", BaseSecondaryAttribute = .2f };
            env.CreateMinimalBattle(); var owner = env.Ally.Entity;
            Assert.That(env.Ally.EquipWeapon(new WeaponLoadout { WeaponID = 3001 }), Is.True);
            string[] parts = { "ExtraBaseATK", "ExtraBaseHP", "ExtraBaseDEF", "ATKBonus", "HPBonus", "DEFBonus",
                "ATKBonusFlat", "HPBonusFlat", "DEFBonusFlat" };
            string[] values = { "10", "100", "20", ".5", ".4", ".3", "7", "7", "7" };
            for (int i = 0; i < parts.Length; i++)
            {
                var main = Buff(env, 459960 + i, parts[i], values[i]);
                owner.AddStatus(main.StatusID2, owner, 0, 2, 0, main);
            }
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            env.Manager.StartBattle();
            Assert.That(owner.GetTotalBaseATK(), Is.EqualTo(150f));
            for (int i = 0; i < 2; i++)
            {
                Assert.That(owner.GetFinalATK("", "", -1, ""), Is.EqualTo(277f).Within(.001f));
                Assert.That(owner.GetFinalHP(), Is.EqualTo(1877f).Within(.001f));
                Assert.That(owner.GetFinalDEF(), Is.EqualTo(187f).Within(.001f));
            }
            Assert.That(owner.CurrentHP, Is.EqualTo(1877f).Within(.001f));
            Assert.That(owner.TotalATK, Is.EqualTo(100f)); Assert.That(owner.TotalDEF, Is.EqualTo(120f).Within(.001f));
            Assert.That(owner.TotalHP, Is.EqualTo(1300f).Within(.001f));
            Assert.That(random.FloatCallCount, Is.Zero); Assert.That(random.IntCallCount, Is.Zero);
        }

        [Test]
        public void ExtraBaseHP_ReplacementDoesNotMomentarilyCutHP_AndRemovalClampsWithoutHealing()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); var owner = env.Ally.Entity;
            var main = Buff(env, 459960, "ExtraBaseHP", "100");
            owner.AddStatus(main.StatusID2, owner, 0, 2, 0, main);
            env.Manager.StartBattle();
            Assert.That(owner.CurrentHP, Is.EqualTo(1100f));
            owner.AddStatus(main.StatusID2, owner, 0, 2, 0, main);
            Assert.That(owner.CurrentHP, Is.EqualTo(1100f));
            owner.RemoveStatus(main.StatusID2);
            Assert.That(owner.GetFinalHP(), Is.EqualTo(1000f)); Assert.That(owner.CurrentHP, Is.EqualTo(1000f));
            owner.AddStatus(main.StatusID2, owner, 0, 2, 0, main);
            Assert.That(owner.GetFinalHP(), Is.EqualTo(1100f)); Assert.That(owner.CurrentHP, Is.EqualTo(1000f));
        }

        [Test]
        public void ExtraBaseStats_TeamAndFieldUseRealRecipients_EnemyQueriesWorkToo()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var reserve = env.CreateAlly(2, false).Entity;
            env.Manager.StartBattle();
            var extraHP = Buff(env, 459960, "ExtraBaseHP", "100");
            var extraDEF = Buff(env, 459961, "ExtraBaseDEF", "20");
            var team = env.Manager.AllyTeam.AddStatus(extraHP.StatusID2, env.Ally.Entity, 0, 2, 0, extraHP);
            env.Ally.Entity.Position.AddStatus(extraDEF.StatusID2, env.Ally.Entity, 0, 2, 0, extraDEF);
            Assert.That(env.Ally.Entity.GetFinalHP(), Is.EqualTo(1100f));
            Assert.That(reserve.GetFinalHP(), Is.EqualTo(1100f));
            Assert.That(env.Ally.Entity.GetFinalDEF(), Is.EqualTo(120f));
            Assert.That(reserve.GetFinalDEF(), Is.EqualTo(100f));
            reserve.CurrentHP = 1100f;
            env.Manager.AllyTeam.RemoveStatusInstance(team);
            Assert.That(reserve.CurrentHP, Is.EqualTo(1000f));
            var enemy = env.Enemy.Entity;
            enemy.AddStatus(extraHP.StatusID2, enemy, 0, 2, 0, extraHP);
            Assert.That(enemy.GetFinalHP(), Is.EqualTo(1100f));
            enemy.AddStatus(extraDEF.StatusID2, enemy, 0, 2, 0, extraDEF);
            Assert.That(enemy.GetFinalDEF(), Is.EqualTo(enemy.TotalDEF + 20f));
        }

        private static void Ascensions(BattleFlowTestEnv env)
        {
            env.DataManager.CharacterAscensionDict[BattleFlowTestEnv.AllyID] = new List<CharacterAscensionData>
            {
                new CharacterAscensionData { AscensionLevel = 1, BaseHPFlat = 100f, BaseATKFlat = 10f, BaseDEFFlat = 20f,
                    HPBonus = .1f, ATKBonus = .1f, DEFBonus = .1f, CritRate = .01f, CritDMG = .02f,
                    EM = 5f, EnergyRechargeRate = .03f, HealBonus = .01f, PyroDmgBonus = .04f, PhysicalDmgBonus = .05f },
                new CharacterAscensionData { AscensionLevel = 20, BaseHPFlat = 200f, BaseATKFlat = 20f, BaseDEFFlat = 30f,
                    HPBonus = .2f, ATKBonus = .2f, DEFBonus = .2f, CritRate = .02f, CritDMG = .04f,
                    EM = 15f, EnergyRechargeRate = .07f, HealBonus = .0554f, PyroDmgBonus = .06f, PhysicalDmgBonus = .07f },
                new CharacterAscensionData { AscensionLevel = 40, BaseHPFlat = 400f, BaseATKFlat = 40f, BaseDEFFlat = 50f,
                    HPBonus = .4f, ATKBonus = .4f, DEFBonus = .4f, CritRate = .03f, CritDMG = .04f,
                    EM = 30f, EnergyRechargeRate = .1f, HealBonus = .0554f, PyroDmgBonus = .2f, PhysicalDmgBonus = .3f,
                    HydroDmgBonus = .4f, ElectroDmgBonus = .5f, CryoDmgBonus = .6f, AnemoDmgBonus = .7f,
                    GeoDmgBonus = .8f, DendroDmgBonus = .9f }
            };
        }

        private static StatusMainData Buff(BattleFlowTestEnv env, int id, string part, string scalar)
        {
            var main = new StatusMainData { StatusID = id, StatusID2 = "ST_Artifact_" + id, StatusType = "Buff",
                MultiplierPart = part, MaxStack = 1, MaxCount = 1 };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            var effect = new StatusEffectData { StatusEffectID = id*100+1, StatusEffectID2 = "STE_Panel_"+id,
                EffectType = "Buff", Param1 = scalar, TargetType = "Self" };
            env.DataManager.StatusEffectDict[effect.StatusEffectID2] = effect; return main;
        }
    }
}
