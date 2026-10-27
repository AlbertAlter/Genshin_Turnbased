using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class ChongyunScriptHooksTests
    {
        private const string S="ST_Chongyun_",E="STE_Chongyun_",Probe="SK_Chongyun_CooldownProbe";
        private static StatusMainData Main(BattleFlowTestEnv e,int n,string key,string type="",string part="")
        {
            var main=new StatusMainData{StatusID=4102500+n,StatusID2=S+key,StatusType=type,MultiplierPart2=part,MaxStack=1,MaxCount=1,WhenMax="0"};
            e.DataManager.StatusMainDict[main.StatusID2]=main;
            if(!e.DataManager.CharacterStatusIDs.TryGetValue(1025,out var ids))e.DataManager.CharacterStatusIDs[1025]=ids=new HashSet<string>();
            ids.Add(main.StatusID2);return main;
        }
        private static StatusEffectData Effect(BattleFlowTestEnv e,int id,string key,string type,string param=null,string target="Self")
        {
            var f=new StatusEffectData{StatusEffectID=id,StatusEffectID2=E+key,EffectType=type,Param1=param,TargetType=target};
            e.DataManager.StatusEffectDict[f.StatusEffectID2]=f;return f;
        }
        private static void Action(BattleFlowTestEnv e,string key,string type,string effects,string hook=null)
        {
            if(!e.DataManager.StatusActionDict.TryGetValue(S+key,out var list))e.DataManager.StatusActionDict[S+key]=list=new List<StatusActionData>();
            list.Add(new StatusActionData{StatusID=e.DataManager.StatusMainDict[S+key].StatusID,StatusID2=S+key,ActionType=type,Param1=effects,ScriptHook=hook});
        }
        private static StatusInstance Add(BattleFlowTestEnv e,string key)=>e.Ally.Entity.AddStatus(S+key,e.Ally.Entity,0,1,0,e.DataManager.StatusMainDict[S+key]);
        private static void Setup(BattleFlowTestEnv e,int constellation=0,bool multiNormal=false,bool pyroNormal=false)
        {
            e.ConfigureAllyAction(0,"SK_Normal_Chongyun","SE_Normal_Chongyun",pyroNormal?"Pyro":"Physical",pyroNormal?"1*TotalATK,5":"1*TotalATK,0",0);
            e.ConfigureAllyAction(2,"SK_Skill_Chongyun","SE_Skill_ChongyunDamage","Cryo","1.7204*TotalATK,2",0,4);
            e.DataManager.SkillEffectDict["SE_Skill_ChongyunDamage"].TargetNumber=3;
            e.ConfigureAllyAction(3,"SK_Burst_Chongyun","SE_Burst_ChongyunDamage","Cryo","1.424*TotalATK,1",0,3);
            var burst=e.DataManager.SkillEffectDict["SE_Burst_ChongyunDamage"];burst.TargetNumber=3;burst.ExecuteCountHandler="Chongyun_BurstHits()";
            if(multiNormal)foreach(var levels in e.DataManager.SkillLevelDict.Values)foreach(var level in levels.Values)
                if(level.ParamID=="SE_Normal_Chongyun")level.Hits2="1";
            e.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID].Add(new SkillMainData{SkillID=990099,SkillID2=Probe,SkillName=Probe,ActionType="Skill",Cooldown=6,MaxCharge=2,InitialCharge=2});
            e.DataManager.SkillEffectDict["SE_Chongyun_CooldownProbe"]=new SkillEffectData{SkillEffectID=99009901,SkillEffectID2="SE_Chongyun_CooldownProbe",EffectType="Damage",Element="Physical",DamageType="Skill",Param1=".01*TotalATK,0",TargetType="Enemy",TargetNumber=1,TargetConsecutive=1};
            Main(e,1,"Frost");Main(e,2,"Enchant","Enchant");Main(e,3,"APDiscount","Buff");Main(e,4,"Rimechaser");
            Main(e,5,"CryoRes","Debuff","ResBonus").ApplyElementType="Cryo";Main(e,6,"ThirdNormal","Count");Main(e,7,"C4");
            var c6=Main(e,8,"C6","Buff","DMGBonus");c6.ApplyDamageType="Burst";c6.ApplyString="SE_Burst_ChongyunDamage";
            var enchant=Effect(e,410250101,"EnchantBind","BindStatus",S+"Enchant","Allies");
            enchant.Element="Cryo";enchant.Param2="1";enchant.Param3="0";enchant.TargetOverride="Chongyun_MeleeAllies()";
            var discount=Effect(e,410250102,"DiscountBind","BindStatus",S+"APDiscount","Allies");discount.TargetOverride="Chongyun_MeleeAllies()";discount.ScriptHook="Check(1025_T1)";
            var end=Effect(e,410250103,"EndDamage","Damage","%SE_Skill_ChongyunDamage.Hits1.M,1","Enemy");end.Element="Cryo";end.DamageType="Skill";end.TargetOverride="Chongyun_EndPositions()";
            var ap=Effect(e,410250301,"APDiscount","APCost","-5");ap.Param2="Normal;Heavy";
            var res=Effect(e,410250401,"EndRes","ApplyStatus",S+"CryoRes","Enemy");res.Duration=2;res.AddInPhase=2;res.TargetOverride="Chongyun_HitTarget()";
            Effect(e,410250501,"CryoRes","Buff","-.1");
            Effect(e,410250601,"ThirdGain","Count",S+"ThirdNormal").Param2="1";
            var extra=Effect(e,410250602,"ThirdDamage","Damage",".5*TotalATK,1","Enemy");extra.Element="Cryo";extra.ScriptHook="Chongyun_Third()";extra.TargetOverride="Chongyun_ThirdArea()";
            Effect(e,410250603,"ThirdReset","Count",S+"ThirdNormal").Param2="clear";
            var energy=Effect(e,410250701,"C4Energy","GainEnergy","1");energy.Element="Cryo";energy.Param2="Flat";
            Effect(e,410250801,"C6Bonus","Buff",".15").ScriptHook="Chongyun_C6HP()";
            Action(e,"Frost","OnEnd",E+"EndDamage","Chongyun_CommittedEnd();Check(1025_T2)");
            Action(e,"Rimechaser","OnTrigger",E+"EndRes","Chongyun_EndHit()");e.DataManager.StatusActionDict[S+"Rimechaser"][0].OutgoingHit="Skill";
            Action(e,"ThirdNormal","OnTrigger",E+"ThirdGain;"+E+"ThirdDamage","Chongyun_Normal()");Action(e,"ThirdNormal","OnItsTurn",E+"ThirdReset");
            Action(e,"C4","OnTrigger",E+"C4Energy","Chongyun_C4Hit()");e.DataManager.StatusActionDict[S+"C4"][0].MaxTimePerTurn=1;
            e.DataManager.StatusAttributesDict[S+"Frost"]=new StatusAttributesData{StatusID=4102501,StatusID2=S+"Frost"};
            e.DataManager.TalentDict[1025]=new List<TalentData>{new TalentData{CharacterID=1025,TalentIndex=1,UnlockAfter=0},new TalentData{CharacterID=1025,TalentIndex=2,UnlockAfter=0}};
            var field=new SkillEffectData{SkillEffectID=e.DataManager.SkillEffectDict["SE_Skill_ChongyunDamage"].SkillEffectID+1,SkillEffectID2="SE_Skill_ChongyunField",EffectIndex=1,EffectType="ApplyStatus",Param1=S+"Frost",Duration=2,AddInPhase=2,TargetType="Self"};
            e.DataManager.SkillEffectDict[field.SkillEffectID2]=field;
            e.CreateMinimalBattle();e.DataManager.CharacterAttributesDict[BattleFlowTestEnv.AllyID].ElementalSkillID=Probe;e.CreateAlly(2);
            e.CreateEnemy(2,100000);e.CreateEnemy(3,100000);e.CreateEnemy(4,100000);
            e.Ally.Entity.EntityID=1025;e.Ally.Entity.ConstellationLevel=constellation;e.Ally.Entity.WeaponType=1;
            e.Enemy.Entity.CurrentHP=e.Enemy.Entity.TotalHP=100000;
            e.Manager.StartBattle();e.Flow.AdvanceToPhase(e.Manager,TurnPhase.AllyAction);
            e.Ally.Entity.TotalATK=100;e.Ally.Entity.TotalHP=e.Ally.Entity.CurrentHP=1000;e.Ally.Entity.CritRate=e.Ally.Entity.CritDMG=0;
            e.Ally.Entity.MaxEnergy=100;e.Ally.Entity.CurrentEnergy=0;Add(e,"Rimechaser");
            if(constellation>=1)Add(e,"ThirdNormal");if(constellation>=4)Add(e,"C4");if(constellation>=6)Add(e,"C6");
            BattleRandom.SetSource(new SequenceBattleRandomSource());
        }
        private static Task Cast(BattleFlowTestEnv e,string key,int type,int[] positions,CharacterBattleController actor=null)
        {
            var ctrl=actor??e.Ally;e.Manager.APManager.ResetAP();ctrl.ForcedTargetPositions.Clear();ctrl.ForcedTargetPositions.AddRange(positions);
            return BattleExecution.Run(()=>ctrl.ExecuteSkillByIDAsync(key,type,true));
        }

        [UnityTest] public IEnumerator EndAttack_UsesOldSnapshotAndPositions_OneEffectOneHit_AfterReplacement()=>HookTestEnv.AwaitTask(SnapshotAsync);
        private async Task SnapshotAsync()
        {
            using var e=new BattleFlowTestEnv();Setup(e);var initial=new Dictionary<int,float>();var ends=new List<DamageEventData>();var executions=new HashSet<long>();var hitIDs=new HashSet<long>();
            using var listen=BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,(h,f)=>
            {if(f.Source.SourceEffectID=="SE_Skill_ChongyunDamage"&&!initial.ContainsKey(f.Subject.SlotPosition))initial[f.Subject.SlotPosition]=f.DamageBeforeShield;
             if(f.Source.SourceEffectID==E+"EndDamage"){ends.Add(f);executions.Add(h.EffectExecutionId);hitIDs.Add(h.HitId);}},0);
            await Cast(e,"SK_Skill_Chongyun",2,new[]{1,2,3});var old=e.Ally.Entity.GetStatus(S+"Frost");Assert.That(old.FormulaSnapshot,Is.Not.Null);
            e.Ally.Entity.TotalATK=500;e.Ally.Entity.CritRate=1;e.Ally.Entity.CritDMG=9;
            e.Ally.ResetSkillCooldown("SK_Skill_Chongyun");await Cast(e,"SK_Skill_Chongyun",2,new[]{2,3,4});
            Assert.That(ends.Select(f=>f.Subject.SlotPosition),Is.EqualTo(new[]{1,2,3}));Assert.That(executions.Count,Is.EqualTo(1));Assert.That(hitIDs.Count,Is.EqualTo(1));
            foreach(var f in ends){Assert.That(f.Source.SourceStatus,Is.SameAs(old));Assert.That(f.DamageBeforeShield,Is.EqualTo(initial[f.Subject.SlotPosition]).Within(.01f));Assert.That(f.DamageType,Is.EqualTo("Skill"));Assert.That(f.Subject.HasStatus(S+"CryoRes"),Is.True);}
            Assert.That(old.IsActive,Is.False);Assert.That(e.Ally.Entity.GetStatus(S+"Frost"),Is.Not.SameAs(old));
            var current=e.Ally.Entity.GetStatus(S+"Frost");ends.Clear();
            await BattleExecution.Run(async()=>
            {
                await e.Ally.ExecuteStatusActionsTask(current,e.DataManager.StatusActionDict[S+"Frost"],e.Ally.Entity,"OnEnd");
                Assert.That(ends,Is.Empty,"普通到期步骤不先执行一次，再在移除时重复执行");
                e.Ally.Entity.RemoveStatusInstance(current,"Expired");await BattleExecution.Flush();
            });
            Assert.That(ends.Select(f=>f.Subject.SlotPosition),Is.EqualTo(new[]{2,3,4}));
            e.Ally.ResetSkillCooldown("SK_Skill_Chongyun");await Cast(e,"SK_Skill_Chongyun",2,new[]{1,2,3});
            var last=e.Ally.Entity.GetStatus(S+"Frost");ends.Clear();e.Ally.Entity.CurrentHP=0;
            await BattleExecution.Run(async()=>{e.Ally.Entity.RemoveStatusInstance(last,"CharacterDeath");await BattleExecution.Flush();});
            Assert.That(ends,Is.Empty,"死亡清理不执行结束伤害");
        }

        [UnityTest] public IEnumerator ThirdNormal_CountsWholeActions_ResetsByCountEffect_ZeroPersists()=>HookTestEnv.AwaitTask(ThirdAsync);
        private async Task ThirdAsync()
        {
            using var e=new BattleFlowTestEnv();Setup(e,1,true);var count=e.Ally.Entity.GetStatus(S+"ThirdNormal");int extra=0;
            using var listen=BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,(_,f)=>{if(f.Source.SourceEffectID==E+"ThirdDamage"){extra++;Assert.That(f.DamageType,Is.Null.Or.Empty);}},0);
            var random=new SequenceBattleRandomSource();BattleRandom.SetSource(random);
            for(int n=0;n<4;n++)await Cast(e,"SK_Normal_Chongyun",0,new[]{2});
            Assert.That(extra,Is.EqualTo(3));Assert.That(count.RuntimeIntValue,Is.EqualTo(3));
            await BattleExecution.Run(()=>e.Ally.ExecuteStatusActionsTask(count,e.DataManager.StatusActionDict[S+"ThirdNormal"],e.Ally.Entity,"OnItsTurn"));
            Assert.That(count.RuntimeIntValue,Is.Zero);Assert.That(e.Ally.Entity.GetStatus(S+"ThirdNormal"),Is.SameAs(count));
            for(int n=0;n<3;n++)await Cast(e,"SK_Normal_Chongyun",0,new[]{2});
            Assert.That(extra,Is.EqualTo(6));Assert.That(random.IntCallCount,Is.Zero);Assert.That(random.FloatCallCount,Is.EqualTo(20));
        }

        [UnityTest] public IEnumerator C2_OnlyNewCastCooldown_ExistingTimerAndAutomaticRechargeStayUnmodified()=>HookTestEnv.AwaitTask(CooldownAsync);
        private async Task CooldownAsync()
        {
            using var e=new BattleFlowTestEnv();Setup(e,2);await Cast(e,"SK_Skill_Chongyun",2,new[]{1,2,3});var other=e.Manager.Allies[1];
            await Cast(e,Probe,2,new[]{1},other);Assert.That(other.GetCooldownRemaining(2),Is.EqualTo(5));
            await Cast(e,Probe,2,new[]{1},other);Assert.That(other.GetCooldownRemaining(2),Is.EqualTo(5));
            for(int n=0;n<5;n++)other.TickSkillCooldowns();
            Assert.That(other.GetCooldownRemaining(2),Is.EqualTo(6));Assert.That(other.GetBoundSkillCurrentCharge(2),Is.EqualTo(1));
            Assert.That(BattleSpecialRules.ResolveCastCooldown(other.Entity,new SkillMainData{ActionType="Normal"},6),Is.EqualTo(6));
            Assert.That(BattleSpecialRules.ResolveCastCooldown(other.Entity,new SkillMainData{ActionType="Burst"},5),Is.EqualTo(4));
            Assert.That(BattleSpecialRules.ResolveCastCooldown(other.Entity,new SkillMainData{ActionType="Skill"},4),Is.EqualTo(4));
        }

        [UnityTest] public IEnumerator C4_PreHitCryo_ConsumesOldAuraButNewApplicationDoesNotQualify()=>HookTestEnv.AwaitTask(CryoAsync);
        private async Task CryoAsync()
        {
            using var e=new BattleFlowTestEnv();Setup(e,4,pyroNormal:true);int payments=0;
            using var listener=BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged,(_,f)=>{if(f.Resource=="Energy"&&f.Subject==e.Ally.Entity&&Math.Abs(f.Added-1)<.0001)payments++;},0);
            await Cast(e,"SK_Skill_Chongyun",2,new[]{1,2,3});Assert.That(payments,Is.Zero);
            await Cast(e,"SK_Normal_Chongyun",0,new[]{1});Assert.That(ElementQuery.HasAura(e.Enemy.Entity,"Cryo"),Is.False);Assert.That(payments,Is.EqualTo(1));
            await Cast(e,"SK_Normal_Chongyun",0,new[]{2});Assert.That(payments,Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator C6_RechecksHealthEachBlade_AndAddsFourthBlade()=>HookTestEnv.AwaitTask(BurstAsync);
        private async Task BurstAsync()
        {
            using var e=new BattleFlowTestEnv();Setup(e,6);e.Ally.Entity.TotalATK=500;e.Ally.Entity.CurrentHP=500;
            foreach(var enemy in e.Manager.Enemies){enemy.Entity.TotalHP=10000;enemy.Entity.CurrentHP=5100;}
            var values=new List<float>();using var listener=BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,(_,f)=>{if(f.Source.SourceEffectID=="SE_Burst_ChongyunDamage"&&f.Subject==e.Enemy.Entity)values.Add(f.DamageBeforeShield);},0);
            await Cast(e,"SK_Burst_Chongyun",3,new[]{1,2,3});
            Assert.That(values.Count,Is.EqualTo(4));Assert.That(values[1],Is.EqualTo(values[0]*1.15f).Within(.01f));
        }
    }
}
