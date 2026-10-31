using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class XingqiuScriptHooksTests
    {
        private const string S="ST_Xingqiu_", F="STE_Xingqiu_";
        private static StatusMainData Main(BattleFlowTestEnv e,int n,string id,string kind="",string part="")
        {
            var m=new StatusMainData{StatusID=4102300+n,StatusID2=S+id,StatusType=kind,MultiplierPart=part,MaxCount=1,MaxStack=1,WhenMax="0"};
            e.DataManager.StatusMainDict[S+id]=m;
            if(!e.DataManager.CharacterStatusIDs.TryGetValue(1023,out var ids))e.DataManager.CharacterStatusIDs[1023]=ids=new HashSet<string>();
            ids.Add(S+id);return m;
        }
        private static StatusEffectData Fx(BattleFlowTestEnv e,int n,int index,string id,string type,string param=null,string target="Self")
        {
            var f=new StatusEffectData{StatusEffectID=(4102300+n)*100+index,StatusEffectID2=F+id,EffectIndex=index,EffectType=type,Param1=param,TargetType=target};
            e.DataManager.StatusEffectDict[F+id]=f;return f;
        }
        private static void Actions(BattleFlowTestEnv e,string id,params StatusActionData[] rows)
        {
            foreach(var a in rows){a.StatusID=e.DataManager.StatusMainDict[S+id].StatusID;a.StatusID2=S+id;}
            e.DataManager.StatusActionDict[S+id]=rows.ToList();
        }
        private static void Level(BattleFlowTestEnv e,int type,string id,string hit,string second=null)
        { e.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_{type}_{id}"]=new Dictionary<int,SkillLevelData>{[1]=new SkillLevelData{CharacterID=BattleFlowTestEnv.AllyID,SkillType=type,SkillLevel=1,ParamID=id,Hits1=hit,Hits2=second}}; }
        private static void Setup(BattleFlowTestEnv e,int constellation=0,int enemies=1,bool attacks=false)
        {
            e.DataManager.CharacterAttributesDict[BattleFlowTestEnv.AllyID].Element="Hydro";
            e.ConfigureAllyAction(0,"SK_Normal_Xingqiu","SE_Normal_Xingqiu","Physical",".3586*TotalATK,0",20);
            e.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_0"][1].Hits2=".3586*TotalATK,0";
            e.ConfigureAllyAction(2,"SK_Skill_Xingqiu","SE_Skill_Xingqiu1","Hydro",null,30,5);
            e.DataManager.SkillEffectDict["SE_Skill_Xingqiu1"].TargetNumber=3;
            e.DataManager.SkillEffectDict["SE_Skill_Xingqiu1"].ScriptHook="Xingqiu_NoC4()";
            Level(e,2,"SE_Skill_Xingqiu1","1.68*TotalATK,1");Level(e,2,"SE_Skill_Xingqiu2","1.912*TotalATK,1");
            for(int i=2;i<=4;i++)e.DataManager.SkillEffectDict["SE_Skill_Xingqiu"+i]=new SkillEffectData
            {SkillEffectID2="SE_Skill_Xingqiu"+i,EffectIndex=i-1,EffectType="Damage",Element="Hydro",DamageType="Skill",TargetType="Enemy",
                TargetOverride=i==3?"Xingqiu_SkillRange()":"Xingqiu_SkillCenter()",ScriptHook=i==2?"Xingqiu_NoC4()":"Xingqiu_C4()",
                Param1=i==2?null:$"%SE_Skill_Xingqiu{(i==3?1:2)}.Hits1.M*1.5,1"};
            e.DataManager.SkillEffectDict["SE_Skill_XingqiuRain"]=new SkillEffectData{SkillEffectID2="SE_Skill_XingqiuRain",EffectIndex=4,EffectType="ApplyStatus",Param1=S+"RainSwords",Duration=4,AddInPhase=2,TargetType="Allies",TargetNumber=-1};
            e.DataManager.SkillEffectDict["SE_Skill_XingqiuEnergy"]=new SkillEffectData{SkillEffectID2="SE_Skill_XingqiuEnergy",EffectIndex=5,EffectType="GainEnergy",Param1="10",Param2="Based",Element="Hydro",TargetType="Allies",TargetNumber=-1,ScriptHook="Xingqiu_SkillHit()"};
            e.ConfigureAllyAction(3,"SK_Burst_Xingqiu","SE_Burst_Xingqiu",null,null,0,5,"ApplyStatus",S+"Rainbow",4,2,0,"Self");
            e.DataManager.SkillEffectDict["SE_Burst_XingqiuRain"]=new SkillEffectData{SkillEffectID2="SE_Burst_XingqiuRain",EffectIndex=1,EffectType="ApplyStatus",Param1=S+"RainSwords",Duration=4,AddInPhase=2,TargetType="Allies",TargetNumber=-1};
            Main(e,1,"RainSwords","Count","DMGTaken");Main(e,2,"Rainbow","Count");Main(e,3,"Hydropathic");
            Main(e,4,"HydroBonus","Buff","DMGBonus").ApplyElementType="Hydro";
            Main(e,5,"HydroRes","Debuff","ResBonus").ApplyElementType="Hydro";
            var c6=Main(e,6,"C6Bonus","Buff","DMGBonus");c6.ApplyDamageType="Burst";c6.ApplyString=F+"SwordDamage";
            Fx(e,1,1,"Reduction","Buff").DamageHook="Xingqiu_Reduction()";Level(e,2,"Xingqiu_Reduction",".2");
            var damage=Fx(e,2,1,"SwordDamage","Damage",target:"Enemy");damage.Element="Hydro";damage.DamageType="Burst";
            damage.TargetOverride="Xingqiu_VolleyTarget()";damage.ExecuteCountHandler="Xingqiu_SwordCount";Level(e,3,F+"SwordDamage",".5427*TotalATK,1");
            var finish=Fx(e,2,2,"Finish","ApplyStatus",S+"Rainbow");finish.Param2="Finish";finish.ScriptHook="Xingqiu_VolleyLanded()";
            Fx(e,2,3,"BindC6","BindStatus",S+"C6Bonus").ScriptHook="Check(1023_C6)";
            var res=Fx(e,2,4,"HydroResGrant","ApplyStatus",S+"HydroRes","Enemy");res.TargetOverride="Xingqiu_HitTarget()";res.Duration=1;res.AddInPhase=2;
            Fx(e,2,5,"C6Energy","GainEnergy","3").Param2="Flat";
            for(int i=1;i<=4;i++)
            {
                var heal=Fx(e,3,i,"Heal"+i,"Heal",(.06m*i).ToString(System.Globalization.CultureInfo.InvariantCulture)+"*Target.TotalHP","Allies");
                heal.TargetOverride="Xingqiu_HealTarget()";heal.ScriptHook="Xingqiu_LostSwords("+i+")";
            }
            Fx(e,4,1,"HydroBonus","Buff",".2");Fx(e,5,1,"HydroRes","Buff","-.15");
            Fx(e,6,1,"C6Bonus","Buff",".5").ScriptHook="Xingqiu_StrongVolley()";
            Actions(e,"Rainbow",new StatusActionData{ActionType="OnTrigger",Param1=F+"SwordDamage;"+F+"Finish",ScriptHook="Xingqiu_Volley()",MaxTimePerTurn=3},
                new StatusActionData{ActionType="OnTrigger",Param1=F+"HydroResGrant",ScriptHook="Xingqiu_SwordHit();Check(1023_C2)",OutgoingHit="Burst"},
                new StatusActionData{ActionType="OnTrigger",Param1=F+"C6Energy",ScriptHook="Xingqiu_StrongHit();Check(1023_C6)",OutgoingHit="Burst",MaxTimePerHit=1});
            Actions(e,"Hydropathic",new StatusActionData{ActionType="OnTrigger",Param1=string.Join(";",Enumerable.Range(1,4).Select(i=>F+"Heal"+i)),ScriptHook="Xingqiu_Healing()"});
            if(attacks){e.ConfigureEnemyDamageSkill(.2f);e.DataManager.EnemySkillParamDict["SE_Enemy_BattleFlow"].Hits2=".2,0";}
            e.CreateMinimalBattle();e.CreateAlly(2);
            for(int p=2;p<=enemies;p++)e.CreateEnemy(p,10000);
            e.Ally.Entity.EntityID=1023;e.Ally.Entity.ConstellationLevel=constellation;
            e.Enemy.Entity.TotalHP=e.Enemy.Entity.CurrentHP=10000;
            e.Manager.StartBattle();e.Flow.AdvanceToPhase(e.Manager,TurnPhase.AllyAction);
            e.Ally.Entity.TotalATK=100;e.Ally.Entity.CritRate=0;e.Ally.Entity.MaxEnergy=80;e.Ally.Entity.CurrentEnergy=0;
            BattleRandom.SetSource(new SequenceBattleRandomSource());
        }
        private static StatusInstance Add(BattleFlowTestEnv e,string id,BattleEntity target=null,int duration=0)
        {target=target??e.Ally.Entity;return target.AddStatus(S+id,e.Ally.Entity,duration,2,0,e.DataManager.StatusMainDict[S+id]);}
        private static Task Cast(BattleFlowTestEnv e,string name,int type,CharacterBattleController actor=null)
        {e.Manager.APManager.ResetAP();return BattleExecution.Run(()=>(actor??e.Ally).ExecuteSkillByIDAsync("SK_"+name+"_Xingqiu",type,true));}

        [UnityTest] public IEnumerator C4_UsesTwoExtraEffectsInOriginalSkill_ReferencesMultiplierOnly_OneCooldown()
            =>HookTestEnv.AwaitTask(C4Async);
        private async Task C4Async()
        {
            using var e=new BattleFlowTestEnv();Setup(e,4,3);Add(e,"Rainbow",duration:4);e.Ally.Entity.BaseDMGBonusFlat=10;
            var hits=new List<DamageEventData>();using var listener=BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,(_,f)=>hits.Add(f),0);
            e.Ally.ForcedTargetPositions.AddRange(new[]{1,2,3});await Cast(e,"Skill",2);
            Assert.That(hits.Count,Is.EqualTo(4));Assert.That(hits.Take(3).All(f=>f.Source.SourceEffectID=="SE_Skill_Xingqiu3"),Is.True);
            Assert.That(hits.Take(3).Select(f=>f.DamageBeforeShield),Has.All.EqualTo(131f).Within(.001f));
            Assert.That(hits.Last().Source.SourceEffectID,Is.EqualTo("SE_Skill_Xingqiu4"));Assert.That(hits.Last().Subject.SlotPosition,Is.EqualTo(2));
            Assert.That(hits.Last().DamageBeforeShield,Is.EqualTo(148.4f).Within(.001f));
            Assert.That(e.Ally.GetCooldownRemaining(2),Is.EqualTo(5));Assert.That(e.Ally.GetBoundSkillID(2),Is.EqualTo("SK_Skill_Xingqiu"));
            Assert.That(e.Ally.GetFirstSelectEffect(2).SkillEffectID2,Is.EqualTo("SE_Skill_Xingqiu1"));
            Assert.That(e.Ally.Entity.CurrentEnergy,Is.EqualTo(15f).Within(.001f));
        }
        [UnityTest] public IEnumerator Rain_WholeEnemyActionConsumesOne_AndHealsOnce_AfterBothHits()
            =>HookTestEnv.AwaitTask(ActionAsync);
        private async Task ActionAsync()
        {
            using var e=new BattleFlowTestEnv();Setup(e,attacks:true);var owner=e.Ally.Entity;
            Add(e,"Hydropathic");var rain=Add(e,"RainSwords",duration:4);owner.CurrentHP=500;
            var hits=new List<DamageEventData>();var heals=new List<HealEventData>();var spends=new List<ResourceEventData>();
            using var a=BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,(_,f)=>{if(f.Subject==owner)hits.Add(f);},0);
            using var b=BattleEventHub.Subscribe<HealEventData>(BattleEventKind.HealResolved,(_,f)=>heals.Add(f),0);
            using var c=BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged,(_,f)=>{if(f.PoolId==S+"RainSwords"&&f.Reason==ResourceChangeReason.Spend)spends.Add(f);},0);
            e.Manager.EndAllyTurn();e.Flow.AdvanceToPhase(e.Manager,TurnPhase.EnemyPostTurn);
            Assert.That(hits.Count,Is.EqualTo(2));Assert.That(spends.Count,Is.EqualTo(1));Assert.That(rain.RuntimeIntValue,Is.EqualTo(2));
            Assert.That(heals.Count,Is.EqualTo(1));Assert.That(owner.CurrentHP,Is.EqualTo(560f-hits.Sum(f=>f.ActualHPDamage)).Within(.001f));
        }
        [UnityTest] public IEnumerator Rain_RainbowRefillsAfterSpend_StillHeals_AndReductionReadsLiveHydro()
            =>HookTestEnv.AwaitTask(RefillAsync);
        private async Task RefillAsync()
        {
            using var e=new BattleFlowTestEnv();Setup(e,1,attacks:true);var owner=e.Ally.Entity;
            Add(e,"Hydropathic");Add(e,"Rainbow",duration:4);var rain=Add(e,"RainSwords",duration:4);
            Assert.That(rain.RuntimeIntValue,Is.EqualTo(4));
            Assert.That(owner.PrepareDMGTaken("","","Normal","Physical").GetMultiplier(ReactionType.None),Is.EqualTo(.8f).Within(.001f));
            Add(e,"HydroBonus");Assert.That(owner.PrepareDMGTaken("","","Normal","Physical").GetMultiplier(ReactionType.None),Is.EqualTo(.76f).Within(.001f));
            owner.HydroDmgBonus=2;Assert.That(owner.PrepareDMGTaken("","","Normal","Physical").GetMultiplier(ReactionType.None),Is.EqualTo(.56f).Within(.001f));
            owner.HydroDmgBonus=0;owner.CurrentHP=500;int heals=0;
            using var listener=BattleEventHub.Subscribe<HealEventData>(BattleEventKind.HealResolved,(_,f)=>{if(f.Subject==owner)heals++;},0);
            e.Manager.EndAllyTurn();e.Flow.AdvanceToPhase(e.Manager,TurnPhase.EnemyPostTurn);
            Assert.That(rain.RuntimeIntValue,Is.EqualTo(4));Assert.That(heals,Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator Rain_ExpirationAggregatesRemainingCount_RefreshAndRemovalDoNotHeal()
            =>HookTestEnv.AwaitTask(ExpiryAsync);
        private async Task ExpiryAsync()
        {
            using var e=new BattleFlowTestEnv();Setup(e);var owner=e.Ally.Entity;Add(e,"Hydropathic");owner.CurrentHP=100;
            var heals=new List<HealEventData>();using var listener=BattleEventHub.Subscribe<HealEventData>(BattleEventKind.HealResolved,(_,f)=>heals.Add(f),0);
            await BattleExecution.Run(async()=>
            {
                var rain=Add(e,"RainSwords",duration:4);Add(e,"RainSwords",duration:4);await BattleExecution.Flush();Assert.That(heals,Is.Empty);
                owner.RemoveStatusInstance(rain,"Expired");await BattleExecution.Flush();
                Assert.That(heals.Count,Is.EqualTo(1));Assert.That(owner.CurrentHP,Is.EqualTo(280f).Within(.001f));
                rain=Add(e,"RainSwords",duration:4);owner.RemoveStatusInstance(rain,"SourceDeath");await BattleExecution.Flush();Assert.That(heals.Count,Is.EqualTo(1));
            });
        }
        [UnityTest] public IEnumerator Rainbow_OneVolleyPerNormalAction_ThirdWaveBonusAndPerSwordEnergy_TurnLimit()
            =>HookTestEnv.AwaitTask(VolleyAsync);
        private async Task VolleyAsync()
        {
            using var e=new BattleFlowTestEnv();Setup(e,6);await Cast(e,"Burst",3);var owner=e.Ally.Entity;var root=owner.GetStatus(S+"Rainbow");
            Assert.That(root.RemainingPhaseCount,Is.EqualTo(5));Assert.That(owner.GetStatus(S+"RainSwords").RemainingPhaseCount,Is.EqualTo(5));
            var hits=new List<DamageEventData>();using var listener=BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,(_,f)=>{if(f.Source.SourceEffectID==F+"SwordDamage")hits.Add(f);},0);
            await Cast(e,"Normal",0);Assert.That(root.RuntimeIntValue,Is.EqualTo(1));
            await Cast(e,"Normal",0,e.Manager.Allies[1]);Assert.That(root.RuntimeIntValue,Is.EqualTo(2));
            owner.CurrentEnergy=0;await Cast(e,"Normal",0);
            Assert.That(root.RuntimeIntValue,Is.Zero);Assert.That(hits.Count,Is.EqualTo(12));Assert.That(owner.CurrentEnergy,Is.EqualTo(12));
            Assert.That(hits.Skip(8).Select(f=>f.DamageBeforeShield),Has.All.EqualTo(43.7551875f).Within(.001f));
            await Cast(e,"Normal",0);Assert.That(hits.Count,Is.EqualTo(12));
            e.Manager.EndAllyTurn();e.Flow.AdvanceToPhase(e.Manager,TurnPhase.AllyAction);await Cast(e,"Normal",0);Assert.That(hits.Count,Is.EqualTo(16));
        }
        [UnityTest] public IEnumerator Death_RemovesPartyRainEnemyResAndRainbow_WithoutHealing()
            =>HookTestEnv.AwaitTask(DeathAsync);
        private async Task DeathAsync()
        {
            using var e=new BattleFlowTestEnv();Setup(e,6);Add(e,"Hydropathic");await Cast(e,"Burst",3);
            var owner=e.Ally.Entity;var other=e.Manager.Allies[1].Entity;var rain=other.GetStatus(S+"RainSwords");var root=owner.GetStatus(S+"Rainbow");
            var debuff=Add(e,"HydroRes",e.Enemy.Entity,1);int heals=0;using var listener=BattleEventHub.Subscribe<HealEventData>(BattleEventKind.HealResolved,(_,f)=>heals++,0);
            await BattleExecution.Run(async()=>{owner.TakeDamage(100000,DamageSourceInfo.CreateUnknown());await BattleExecution.Flush();});
            Assert.That(rain.IsActive||root.IsActive||debuff.IsActive,Is.False);Assert.That(heals,Is.Zero);Assert.That(rain.RuntimeIntValue,Is.Zero);
        }
    }
}
