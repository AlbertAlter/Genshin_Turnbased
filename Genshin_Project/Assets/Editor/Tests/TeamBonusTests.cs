using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using OfficeOpenXml;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public class TeamBonusTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly Dictionary<BattleEntity, string> elements = new Dictionary<BattleEntity, string>();
        private TeamBonusSystem team;
        private TeamBonusRules rules;

        [SetUp]
        public void Setup()
        {
            using var stream = File.OpenRead(Path.Combine(Application.streamingAssetsPath, "Data", "TeamBonus.xlsx"));
            using var package = new ExcelPackage(stream);
            rules = TeamBonusTableLoader.Load(package.Workbook);
            BattleEventHub.ResetSession();
        }
        [TearDown]
        public void Cleanup()
        {
            team?.Dispose(); team = null; BattleEventHub.ResetSession();
            foreach (GameObject obj in objects) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            objects.Clear(); elements.Clear();
        }
        private BattleEntity Member(int id, string element)
        {
            var obj = new GameObject("TeamBonusTest_" + id); objects.Add(obj);
            var actor = obj.AddComponent<BattleEntity>();
            actor.EntityID = id; actor.Side = BattleSide.Ally;
            actor.TotalHP = actor.CurrentHP = 1000; actor.TotalATK = 1000; actor.TotalDEF = 100;
            actor.TotalEM = 100; actor.TotalRechargeRate = 1f; actor.MaxEnergy = 100;
            elements.Add(actor, element); return actor;
        }
        private void Start(params BattleEntity[] actors) => team = new TeamBonusSystem(actors, rules, a => elements[a]);
        private static TeamBonusParameters Parameters(params (string key, object value)[] values)
        {
            using var p = new ExcelPackage(); var s = p.Workbook.Worksheets.Add("Test");
            s.Cells[1, 1].Value = "Value"; s.Cells[1, 2].Value = "#Name"; s.Cells[1, 3].Value = "Key";
            int row = 2;
            foreach (var value in values) { s.Cells[row, 1].Value = value.value; s.Cells[row++, 3].Value = value.key; }
            return TeamBonusTableLoader.Read(s);
        }
        [Test]
        public void DefaultRostersAreFilled_AndNightSoulDoesNotNeedEffectIDs()
        {
            Assert.That(rules.NightSoul.CharacterIDs().Count,Is.EqualTo(11));
            Assert.That(rules.NightSoul.CharacterIDs(),Does.Contain(1092));
            Assert.That(rules.Moonsign.CharacterIDs().Count,Is.EqualTo(10));
            Assert.That(rules.Moonsign.CharacterIDs(),Does.Contain(1115));
            Assert.That(rules.Hexerei.CharacterIDs().Count,Is.EqualTo(12));
            Assert.That(rules.Hexerei.CharacterIDs(),Does.Contain(1012));
            Assert.That(rules.Hexerei.CharacterIDs().Contains(1011),Is.False);
            Assert.That(rules.NightSoul.Has("DamageEffectIDs"),Is.False);
        }
        [Test]
        public void HeadersAreMovable_DuplicatesAreRejected()
        {
            var p = Parameters(("Enabled", 1), ("PyroATK", .25));
            Assert.That(p.Enabled, Is.True); Assert.That(p.Number("PyroATK"), Is.EqualTo(.25f));
            Assert.Throws<TableValidationException>(() => Parameters(("Enabled", 1), ("Enabled", 0)));
        }
        [Test]
        public void FourPyroDoesNotDuplicateResonance_DeadMemberStillCounts()
        {
            var a = Member(1, "Pyro"); var b = Member(2, "Pyro"); b.CurrentHP = 0;
            Start(a, b, Member(3, "Pyro"), Member(4, "Pyro"));
            Assert.That(team.Resonance.ATKBonus(a), Is.EqualTo(.25f));
            Assert.That(team.Resonance.Has("Protect"), Is.False);
        }
        [Test]
        public void HydroOnlyRegistersMaximumDuringSetup_AndDisposeDoesNotLeavePanelBonus()
        {
            var a = Member(1, "Hydro"); var b = Member(2, "Hydro"); b.CurrentHP = 200; Start(a, b);
            Assert.That(a.TotalHP, Is.EqualTo(1250)); Assert.That(a.CurrentHP, Is.EqualTo(1000),
                "单独构造队伍效果只登记属性，当前HP由BattleManager完成全队初始化后统一结算");
            Assert.That(b.CurrentHP, Is.EqualTo(200)); team.Dispose(); team.Dispose();
            Assert.That(a.TotalHP, Is.EqualTo(1000)); Assert.That(a.CurrentHP, Is.EqualTo(1000));
        }
        [Test]
        public void AnemoOnlyChangesNormalHeavySwitchIn()
        {
            var a = Member(1, "Anemo"); Start(a, Member(2, "Anemo"));
            foreach (string op in new[] { "Normal", "Heavy", "SwitchIn" }) Assert.That(team.Resonance.APDelta(a, op), Is.EqualTo(-1));
            foreach (string op in new[] { "Skill", "Burst", "SwitchOut" }) Assert.That(team.Resonance.APDelta(a, op), Is.Zero);
        }
        [Test]
        public void CryoBonusUsesEachTargetRatherThanMainTarget()
        {
            var a = Member(1, "Cryo"); var b = Member(2, "Cryo"); Start(a, b);
            a.ElementalAuras.Add(new ElementalAura { Element = "Cryo", AuraAmount = 1 });
            Assert.That(team.Resonance.CritBonus(a, a), Is.EqualTo(.15f));
            Assert.That(team.Resonance.CritBonus(a, b), Is.Zero);
        }
        [Test]
        public void DendroGroupsRefreshSeparately_PreserveOtherEMChanges()
        {
            var a = Member(1, "Dendro"); Start(a, Member(2, "Dendro")); team.EnterPhase(2, 1);
            var occurrence = new ReactionOccurrence { Type = ReactionType.Burning, SourceEntity = a };
            team.Resonance.OnReaction(null, new ReactionEventData(occurrence));
            team.Resonance.OnReaction(null, new ReactionEventData(occurrence));
            occurrence.Type = ReactionType.Hyperbloom; team.Resonance.OnReaction(null, new ReactionEventData(occurrence));
            Assert.That(a.TotalEM, Is.EqualTo(200)); a.TotalEM += 7;
            team.EnterPhase(2, 2); team.EnterPhase(2, 3); Assert.That(a.TotalEM, Is.EqualTo(157));
            team.Dispose(); Assert.That(a.TotalEM, Is.EqualTo(107));
        }
        [Test]
        public void MoonsignNewestLowerValueReplacesHigher_ForWholeTeam()
        {
            rules.Moonsign = Parameters(("Enabled",1),("CharacterIDs","1;2"),("GleamCount",1),("AscendantCount",2),
                ("Duration",5),("ATKStep",100),("ATKBonus",.009),("HPStep",1000),("HPBonus",.006),("BonusCap",.36));
            var a = Member(1,"Dendro"); var b = Member(2,"Geo"); var c = Member(3,"Pyro"); var d = Member(4,"Hydro");
            c.TotalATK = 4000; d.TotalHP = 30000; Start(a,b,c,d);
            team.Moonsign.OnAction(null,new ActionEventData(c,"SK_C","Skill",false));
            Assert.That(team.Moonsign.DamageBonus,Is.EqualTo(.36f).Within(.0001f));
            team.Moonsign.OnAction(null,new ActionEventData(d,"SK_D","Burst",false));
            Assert.That(team.Moonsign.DamageBonus,Is.EqualTo(.18f).Within(.0001f));
            Assert.That(team.Moonsign.Bonus.Source,Is.SameAs(d)); Assert.That(team.HasBonus("MoonsignGleam"),Is.True);
            team.Moonsign.OnAction(null,new ActionEventData(a,"SK_A","Skill",false));
            Assert.That(team.Moonsign.Bonus.Source,Is.SameAs(d));
        }
        [Test]
        public void ElectroEnergyUsesSharedPerTurnLimit()
        {
            var a=Member(1,"Electro"); var b=Member(2,"Electro"); Start(a,b);
            var reaction=new ReactionOccurrence { Type=ReactionType.ElectroCharged,SourceEntity=a };
            team.Resonance.OnReaction(null,new ReactionEventData(reaction));
            reaction.SourceEntity=b; team.Resonance.OnReaction(null,new ReactionEventData(reaction));
            Assert.That(a.CurrentEnergy,Is.EqualTo(1),"无CharacterCtrl的测试实体按异色0.5倍Based回能");
            team.EnterPhase(1,2); team.Resonance.OnReaction(null,new ReactionEventData(reaction));
            Assert.That(a.CurrentEnergy,Is.EqualTo(2));
        }
        [Test]
        public void NightSoulBurstUsesOneCooldown_OrdinaryDamageDoesNotTrigger()
        {
            rules.NightSoul=Parameters(("Enabled",1),("CharacterIDs","1;2"),
                ("Cooldown1",4),("Cooldown2",3),("Cooldown3",2));
            var a=Member(1,"Pyro"); var b=Member(2,"Hydro");
            var other=Member(3,"Cryo");
            var enemy=Member(9,"Electro"); enemy.Side=BattleSide.Enemy; Start(a,b,other);
            var hit=new DamageResolvedEvent { Source=DamageSourceInfo.Create(a,ReactionSourceKind.CharacterSkill,"SK","SE_Normal"),
                Target=enemy,HitLanded=true,DamageBeforeShield=10 };
            var data=new DamageEventData(hit);
            team.NightSoul.OnDamage(BattleEventHub.Record(null,data,100).Header,data);
            Assert.That(team.NightSoul.ReadyTurn,Is.EqualTo(1));
            hit.Source.SourceEffectID="SE_Night"; data=new DamageEventData(hit);
            team.NightSoul.OnDamage(BattleEventHub.Record(null,data,101).Header,data);
            Assert.That(team.NightSoul.ReadyTurn,Is.EqualTo(1),"效果名字不能冒充夜魂伤害性质");
            hit.Source=DamageSourceInfo.Create(other,ReactionSourceKind.CharacterSkill,"SK","SE_Any");
            hit.IsNightSoulDamage=true; data=new DamageEventData(hit);
            team.NightSoul.OnDamage(BattleEventHub.Record(null,data,101).Header,data);
            Assert.That(team.NightSoul.ReadyTurn,Is.EqualTo(4));
            team.EnterPhase(1,3); team.NightSoul.OnDamage(BattleEventHub.Record(null,data,102).Header,data);
            Assert.That(team.NightSoul.ReadyTurn,Is.EqualTo(4));
            team.EnterPhase(1,4); team.NightSoul.OnDamage(BattleEventHub.Record(null,data,103).Header,data);
            Assert.That(team.NightSoul.ReadyTurn,Is.EqualTo(7));
        }
        [Test]
        public void HexereiIsQualification_NotUniversalStatBonus()
        {
            rules.Hexerei = Parameters(("Enabled",1),("CharacterIDs","1;2"),("RequiredCount",2));
            var a=Member(1,"Pyro"); Start(a,Member(2,"Hydro"));
            Assert.That(team.HasBonus("Hexerei"),Is.True); Assert.That(a.TotalATK,Is.EqualTo(1000));
        }
        [Test]
        public void StellarCreationExcluded_RefreshRetainsCount_MultipleTargetsCountOnce()
        {
            rules.Stellar = Parameters(("Enabled",1),("CharacterIDs","1"),("Duration",2),("CountCap",12),
                ("PhysicalResDown",.4),("ElementDMG0",.2),("ElementDMG1",.29),("ReactionRate0",1),("ReactionRate1",1.45));
            var a=Member(1,"Cryo"); var enemy=Member(9,"Electro"); enemy.Side=BattleSide.Enemy; Start(a); team.EnterPhase(2,1);
            enemy.ApplyAura("Electro", 2f, a.EntityID, sourceEntity: a);
            var context=new ReactionContext { SourceEntity=a,Target=enemy,AttackElement="Cryo",AttackAmount=1f,LogicalHitID=10 };
            Assert.That(team.Stellar.TryConvertSuperconduct(context),Is.True);
            var domain = team.Stellar.Domain.Status;
            Assert.That(domain.MainData.StatusID, Is.EqualTo(40027));
            Assert.That(team.OpposingHost.GetStatus(TeamPublicStatuses.PolarDomain), Is.SameAs(domain));
            Assert.That(a.HasStatus(TeamPublicStatuses.PolarDomain), Is.False);
            Damage(10,a,enemy); Assert.That(team.Stellar.PendingCount,Is.Zero);
            Damage(11,a,enemy); Damage(11,a,enemy); Assert.That(team.Stellar.PendingCount,Is.EqualTo(1));
            context.LogicalHitID=12; team.Stellar.TryConvertSuperconduct(context); Assert.That(team.Stellar.PendingCount,Is.EqualTo(1));
            Assert.That(team.Stellar.Domain.Status, Is.SameAs(domain), "刷新不复制领域或清空原计数");
            Assert.That(team.Stellar.EffectiveCount,Is.Zero); team.EnterPhase(1,2);
            Assert.That(team.Stellar.ElementDamageBonus,Is.EqualTo(.29f)); Assert.That(team.Stellar.PendingCount,Is.Zero);
            team.EnterPhase(2, 2);
            Assert.That(domain.RemainingPhaseCount, Is.EqualTo(1));
            team.EnterPhase(2, 3);
            Assert.That(team.OpposingHost.Contains(domain), Is.False);
            Assert.That(team.Stellar.IsActive, Is.False);
        }
        private void Damage(long id, BattleEntity source, BattleEntity target)
        {
            var value=new DamageEventData(new DamageResolvedEvent { Source=DamageSourceInfo.Create(source,ReactionSourceKind.CharacterSkill,"SK","SE"),
                Target=target,HitLanded=true,DamageBeforeShield=10,Element="Cryo",ElementAmount=1 });
            team.Stellar.OnDamage(BattleEventHub.Record(null,value,id).Header,value);
        }
    }
}
