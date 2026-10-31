using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using OfficeOpenXml;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class FischlScriptHooksTests
    {
        private const string S = "ST_Fischl_", E = "STE_Fischl_";
        private static StatusMainData Main(BattleFlowTestEnv e, int n, string key, string type = "", string part = "")
        {
            var m = new StatusMainData { StatusID = 4102400+n, StatusID2 = S+key, StatusType = type,
                MultiplierPart = part, MaxStack = 1, MaxCount = 1, WhenMax = "0" };
            e.DataManager.StatusMainDict[m.StatusID2] = m;
            if (!e.DataManager.CharacterStatusIDs.TryGetValue(1024, out var ids))
                e.DataManager.CharacterStatusIDs[1024] = ids = new HashSet<string>();
            ids.Add(m.StatusID2); return m;
        }
        private static StatusEffectData Effect(BattleFlowTestEnv e, int id, string key, string type, string param = null, string target = "Self")
        {
            var f = new StatusEffectData { StatusEffectID = id, StatusEffectID2 = E+key, EffectType = type,
                Param1 = param, TargetType = target };
            e.DataManager.StatusEffectDict[f.StatusEffectID2] = f; return f;
        }
        private static void Action(BattleFlowTestEnv e, string key, string type, string effects, string hook = null)
        {
            if (!e.DataManager.StatusActionDict.TryGetValue(S+key, out var actions))
                e.DataManager.StatusActionDict[S+key] = actions = new List<StatusActionData>();
            actions.Add(new StatusActionData { StatusID = e.DataManager.StatusMainDict[S+key].StatusID,
                StatusID2 = S+key, ActionType = type, Param1 = effects, ScriptHook = hook });
        }
        private static void Setup(BattleFlowTestEnv e, int c = 0, bool magic = false)
        {
            e.ConfigureAllyAction(0, "SK_Normal_FischlTest", "SE_Normal_FischlTest", "Physical", "1*TotalATK,0", 0);
            e.ConfigureAllyAction(2, "SK_Skill_FischlTest", "SE_Skill_FischlTest", "Electro", "1*TotalATK,1", 0);
            foreach (string key in new[] { "SE_Normal_FischlTest", "SE_Skill_FischlTest" })
            { e.DataManager.SkillEffectDict[key].TargetNumber = 2; e.DataManager.SkillEffectDict[key].TargetConsecutive = 1; }
            foreach (var group in e.DataManager.SkillLevelDict.Values) foreach (var level in group.Values)
                if (level.ParamID == "SE_Normal_FischlTest" || level.ParamID == "SE_Skill_FischlTest") level.Hits2 = "1";
            Main(e,3,"Oz"); Main(e,4,"OzEnemyEnd"); Main(e,5,"C1");
            Main(e,6,"HexereiATK","Buff","ATKBonus"); Main(e,7,"HexereiEM","Buff","EMBonus"); Main(e,8,"HexereiBoost");
            Main(e,9,"OrdinaryATK","Buff","ATKBonus");
            Effect(e,410240601,"HexereiATK","Buff",".225"); Effect(e,410240701,"HexereiEM","Buff","90");
            Effect(e,410240901,"OrdinaryATK","Buff",".1");
            var bind = Effect(e,410240303,"OzEnemyEndBind","BindStatus",S+"OzEnemyEnd","EnemyField");
            bind.TargetSelect="0,0"; bind.AddInPhase=2; bind.TriggerPhase=6;
            var shot = Effect(e,410240301,"OzDamage","Damage",".888*TotalATK,1","Enemy");
            shot.Element="Electro";shot.DamageType="Skill";shot.TargetOverride="Fischl_OzTarget()";
            var energy=Effect(e,410240302,"OzEnergy","GainEnergy","2","Allies");energy.Element="Electro";
            energy.Param2="Based";energy.TargetSelect="4";energy.ScriptHook="Fischl_OzHit();Chance(.5)";
            foreach(string key in new[]{"Oz","OzEnemyEnd"}) Action(e,key,"OnItsTurn",E+"OzDamage;"+E+"OzEnergy;"+E+"OzDamage;"+E+"OzEnergy");
            var a4=Effect(e,410240305,"A4Damage","Damage",".8*TotalATK,1","Enemy");a4.Element="Electro";a4.TargetOverride="Fischl_A4Target()";
            var c6=Effect(e,410240306,"C6Damage","Damage",".3*TotalATK,1","Enemy");c6.Element="Electro";c6.TargetOverride="Fischl_C6Target()";
            var window=Effect(e,410240307,"HexereiBoost","ApplyStatus",S+"HexereiBoost","AllyTeam");
            window.Duration=2;window.AddInPhase=2;window.ScriptHook="Fischl_C6Attacked()";
            Action(e,"Oz","OnTrigger",E+"A4Damage","Fischl_A4()");
            Action(e,"Oz","OnTrigger",E+"C6Damage;"+E+"HexereiBoost","Fischl_C6()");
            foreach(var pair in new[]{new[]{"GrantATK","HexereiATK"},new[]{"GrantEM","HexereiEM"}})
            {
                var grant=Effect(e,pair[0]=="GrantATK"?410240308:410240309,pair[0],"ApplyStatus",S+pair[1],"AllyTeam");
                grant.Duration=3;grant.AddInPhase=2;
                Action(e,"Oz","OnTrigger",E+pair[0],"Fischl_"+pair[1]+"()");
            }
            e.DataManager.TalentDict[1024]=new List<TalentData>{new TalentData{CharacterID=1024,TalentIndex=2,UnlockAfter=0}};
            e.DataManager.HexereiStatusIDs.UnionWith(new[]{S+"HexereiATK",S+"HexereiEM"});
            if(magic)
            {
                var add=typeof(TeamBonusParameters).GetMethod("Add",BindingFlags.Instance|BindingFlags.NonPublic);
                foreach(var pair in new[]{new[]{"Enabled","1"},new[]{"RequiredCount","2"},new[]{"CharacterIDs","1024;990001"}})
                    add.Invoke(e.DataManager.TeamBonusRules.Hexerei,new object[]{pair[0],pair[1]});
            }
            e.CreateMinimalBattle();e.CreateAlly(2);e.CreateEnemy(2,100000);e.CreateEnemy(3,100000);
            e.Ally.Entity.EntityID=1024;e.Ally.Entity.ConstellationLevel=c;
            e.Enemy.Entity.CurrentHP=e.Enemy.Entity.TotalHP=100000;
            e.Manager.StartBattle();e.Flow.AdvanceToPhase(e.Manager,TurnPhase.AllyAction);
            foreach(var ally in e.Manager.Allies){ally.Entity.TotalATK=100;ally.Entity.CritRate=ally.Entity.CritDMG=0;}
            BattleRandom.SetSource(new SequenceBattleRandomSource());
        }
        private static StatusInstance Oz(BattleFlowTestEnv e,int pos=2)
            =>e.Manager.Field.GetSlot(BattleSide.Enemy,pos).AddStatus(S+"Oz",e.Ally.Entity,3,2,3,e.DataManager.StatusMainDict[S+"Oz"]);

        [Test]
        public void Resummon_MovesOneOzAndRemovesItsBoundEnemyEnd_C6ExtendsOnlyOnce()
        {
            using var e=new BattleFlowTestEnv();Setup(e,6);var old=Oz(e,2);var child=old.BoundStatuses.Single();
            Assert.That(old.RemainingPhaseCount,Is.EqualTo(4));Assert.That(child.RemainingPhaseCount,Is.EqualTo(4));
            Assert.That(child.TriggerPhase,Is.EqualTo(6));var next=Oz(e,4);
            Assert.That(StatusHostAccess.Contains(old.Host,old),Is.False);Assert.That(StatusHostAccess.Contains(child.Host,child),Is.False);
            Assert.That(next.Host,Is.SameAs(e.Manager.Field.GetSlot(BattleSide.Enemy,4)));
            Assert.That(next.BoundStatuses.Single().RemainingPhaseCount,Is.EqualTo(4));
        }

        [Test]
        public void Hexerei_NewInstancesCaptureStrength_OldInstancesAndOrdinaryBuffsDoNotChange()
        {
            using var e=new BattleFlowTestEnv();Setup(e,6,true);var owner=e.Ally.Entity;var team=e.Manager.AllyTeam;
            var old=team.AddStatus(S+"HexereiATK",owner,3,2,0,e.DataManager.StatusMainDict[S+"HexereiATK"]);
            Assert.That(old.HexereiMultiplier,Is.EqualTo(1));
            var window=team.AddStatus(S+"HexereiBoost",owner,2,2,0,e.DataManager.StatusMainDict[S+"HexereiBoost"]);
            var fresh=team.AddStatus(S+"HexereiEM",owner,3,2,0,e.DataManager.StatusMainDict[S+"HexereiEM"]);
            var ordinary=team.AddStatus(S+"OrdinaryATK",owner,3,2,0,e.DataManager.StatusMainDict[S+"OrdinaryATK"]);
            Assert.That(fresh.HexereiMultiplier,Is.EqualTo(2));Assert.That(old.HexereiMultiplier,Is.EqualTo(1));
            Assert.That(ordinary.HexereiMultiplier,Is.EqualTo(1));Assert.That(fresh.RemainingPhaseCount,Is.EqualTo(3));
            team.RemoveStatusInstance(window,"Expired");
            Assert.That(HexereiStatusEnhancement.TryResolveScalar(e.DataManager.StatusEffectDict[E+"HexereiEM"],fresh,out float value),Is.True);
            Assert.That(value,Is.EqualTo(180));Assert.That(fresh.HexereiMultiplier,Is.EqualTo(2));
            var normal=team.AddStatus(S+"HexereiEM",owner,3,2,0,e.DataManager.StatusMainDict[S+"HexereiEM"]);
            Assert.That(normal.HexereiMultiplier,Is.EqualTo(1));
        }

        [TestCase(0,true)]
        [TestCase(6,false)]
        public void Hexerei_RequiresRealSixthConstellationAndDoubleHexerei(int constellation,bool magic)
        {
            using var e=new BattleFlowTestEnv();Setup(e,constellation,magic);var owner=e.Ally.Entity;var team=e.Manager.AllyTeam;
            team.AddStatus(S+"HexereiBoost",owner,2,2,0,e.DataManager.StatusMainDict[S+"HexereiBoost"]);
            var status=team.AddStatus(S+"HexereiATK",owner,3,2,0,e.DataManager.StatusMainDict[S+"HexereiATK"]);
            Assert.That(status.HexereiMultiplier,Is.EqualTo(1));
        }

        [Test]
        public void Hexerei_GlobalSheetReadsMovedHeaders_RejectsCountAndDuplicates()
        {
            using var e=new BattleFlowTestEnv();Main(e,6,"HexereiATK","Buff","ATKBonus");Main(e,10,"Counter","Count");
            using var package=new ExcelPackage();var sheet=package.Workbook.Worksheets.Add("Status");
            sheet.Cells[1,1].Value="StatusID2";sheet.Cells[1,3].Value="StatusID";
            sheet.Cells[2,1].Value=S+"HexereiATK";sheet.Cells[2,3].Value=4102406;
            var load=typeof(DataManager).GetMethod("LoadHexereiStatusSheet",BindingFlags.Instance|BindingFlags.NonPublic);
            load.Invoke(e.DataManager,new object[]{sheet});Assert.That(e.DataManager.HexereiStatusIDs,Does.Contain(S+"HexereiATK"));
            Assert.That(Assert.Throws<TargetInvocationException>(()=>load.Invoke(e.DataManager,new object[]{sheet})).InnerException,Is.TypeOf<TableValidationException>());
            e.DataManager.HexereiStatusIDs.Clear();sheet.Cells[2,1].Value=S+"Counter";sheet.Cells[2,3].Value=4102410;
            Assert.That(Assert.Throws<TargetInvocationException>(()=>load.Invoke(e.DataManager,new object[]{sheet})).InnerException,Is.TypeOf<TableValidationException>());
        }

        [UnityTest] public IEnumerator Oz_TwoIndependentShots_TwoChanceRolls_OnlyOnePayment()=>HookTestEnv.AwaitTask(ShotsAsync);
        private async Task ShotsAsync()
        {
            using var e=new BattleFlowTestEnv();Setup(e,2);var root=Oz(e);var targets=new List<int>();int payments=0;
            using var damage=BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,(_,f)=>{if(f.Source.SourceEffectID==E+"OzDamage")targets.Add(f.Subject.SlotPosition);},0);
            using var energy=BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged,(_,f)=>{if(f.Resource=="Energy"&&f.Subject==e.Ally.Entity&&f.Added>0)payments++;},0);
            e.Ally.Entity.CurrentEnergy=0;
            var random=new SequenceBattleRandomSource(new[]{.9f,.1f,.9f,.9f},new[]{2,0});BattleRandom.SetSource(random);
            await BattleExecution.Run(()=>e.Ally.ExecuteStatusActionsTask(root,e.DataManager.StatusActionDict[S+"Oz"],root.Host,"OnItsTurn"));
            Assert.That(targets,Is.EqualTo(new[]{3,1}));Assert.That(payments,Is.EqualTo(1));
            Assert.That(random.IntCallCount,Is.EqualTo(2));Assert.That(random.FloatCallCount,Is.EqualTo(4));
        }

        [UnityTest] public IEnumerator A4_MultiHitAndMultiTarget_OncePerEffect_NoSelfRecursion()=>HookTestEnv.AwaitTask(A4Async);
        private async Task A4Async()
        {
            using var e=new BattleFlowTestEnv();Setup(e);Oz(e);
            foreach(var enemy in e.Manager.Enemies)enemy.Entity.ApplyAura("Pyro",20,e.Ally.Entity.EntityID);
            int procs=0;var originalTargets=new HashSet<BattleEntity>();
            using var listener=BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,(_,f)=>
            {if(f.Source.SourceEffectID==E+"A4Damage"){procs++;Assert.That(f.DamageType,Is.Null.Or.Empty);Assert.That(originalTargets,Does.Contain(f.Subject));}
             if(f.Source.SourceEffectID=="SE_Skill_FischlTest")originalTargets.Add(f.Subject);},0);
            var random=new SequenceBattleRandomSource();BattleRandom.SetSource(random);
            e.Ally.ForcedTargetPositions.AddRange(new[]{1,2});
            await BattleExecution.Run(()=>e.Ally.ExecuteSkillByIDAsync("SK_Skill_FischlTest",2,true));
            Assert.That(procs,Is.EqualTo(1));Assert.That(random.IntCallCount,Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator C6_OtherAllyWholeActionOnce_ExcludesOwner_ThenEnhancesNewMagic()=>HookTestEnv.AwaitTask(C6Async);
        private async Task C6Async()
        {
            using var e=new BattleFlowTestEnv();Setup(e,6,true);Oz(e);var other=e.Manager.Allies[1];
            int procs=0;using var damage=BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,(_,f)=>
            {if(f.Source.SourceEffectID==E+"C6Damage"){procs++;Assert.That(f.DamageType,Is.Null.Or.Empty);Assert.That(f.Subject.SlotPosition,Is.InRange(1,2));}},0);
            var random=new SequenceBattleRandomSource();BattleRandom.SetSource(random);
            e.Ally.ForcedTargetPositions.AddRange(new[]{1,2});
            await BattleExecution.Run(()=>e.Ally.ExecuteSkillByIDAsync("SK_Normal_FischlTest",0,true));
            Assert.That(procs,Is.Zero);
            other.ForcedTargetPositions.AddRange(new[]{1,2});
            await BattleExecution.Run(()=>other.ExecuteSkillByIDAsync("SK_Normal_FischlTest",0,true));
            Assert.That(procs,Is.EqualTo(1));Assert.That(random.IntCallCount,Is.EqualTo(1));
            var window=e.Manager.AllyTeam.GetStatusInstances(S+"HexereiBoost").Single();Assert.That(window.RemainingPhaseCount,Is.EqualTo(2));
            foreach(var enemy in e.Manager.Enemies)enemy.Entity.ApplyAura("Pyro",20,other.Entity.EntityID);
            await BattleExecution.Run(()=>other.ExecuteSkillByIDAsync("SK_Skill_FischlTest",2,true));
            var atk=e.Manager.AllyTeam.GetStatusInstances(S+"HexereiATK").Single();
            Assert.That(atk.HexereiMultiplier,Is.EqualTo(2));Assert.That(atk.RemainingPhaseCount,Is.EqualTo(3));
            Assert.That(HexereiStatusEnhancement.TryResolveScalar(e.DataManager.StatusEffectDict[E+"HexereiATK"],atk,out float amount),Is.True);
            Assert.That(amount,Is.EqualTo(.45f).Within(.00001f));
        }
    }
}
