using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using OfficeOpenXml;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    // 独立内存数据走正式施放/Count/资源/状态伤害入口；不读策划源表，不伪造声明。
    public sealed class RazorScriptHooksTests
    {
        private static StatusMainData Main(BattleFlowTestEnv e, int n, string id, string type = null, string part = null)
        {
            var m = new StatusMainData { StatusID = 4101900+n, StatusID2 = id, StatusName = id,
                StatusType = type, MultiplierPart2 = part, MaxCount = 1, MaxStack = 1, WhenMax = "0" };
            e.DataManager.StatusMainDict[id] = m;
            if (!e.DataManager.CharacterStatusIDs.TryGetValue(1019, out var ids))
                e.DataManager.CharacterStatusIDs[1019] = ids = new HashSet<string>();
            ids.Add(id); return m;
        }
        private static StatusEffectData Effect(BattleFlowTestEnv e, int n, int index, string id, string type, string p = null)
        {
            var f = new StatusEffectData { StatusEffectID = (4101900+n)*100+index, StatusEffectID2 = id,
                EffectIndex = index, EffectType = type, Param1 = p, TargetType = "Self" };
            e.DataManager.StatusEffectDict[id] = f; return f;
        }
        private static void Actions(BattleFlowTestEnv e, string id, params StatusActionData[] a)
        {
            foreach(var r in a) { r.StatusID = e.DataManager.StatusMainDict[id].StatusID; r.StatusID2 = id; }
            e.DataManager.StatusActionDict[id] = a.ToList();
        }
        private static SkillEffectData SkillEffect(BattleFlowTestEnv e, string id, int index, string type, string p, string hook = null)
        {
            var f = new SkillEffectData { SkillEffectID = 10190000+e.DataManager.SkillEffectDict.Count,
                SkillEffectID2 = id, EffectIndex = index, EffectType = type, Param1 = p,
                TargetType = "Self", ScriptHook = hook };
            e.DataManager.SkillEffectDict[id] = f; return f;
        }
        private static StatusInstance Add(BattleFlowTestEnv e, BattleEntity owner, string id, int rounds = 0)
            => owner.AddStatus(id, owner, rounds, 2, 0, e.DataManager.StatusMainDict[id]);
        private static StatusInstance GainSigils(BattleFlowTestEnv e,BattleEntity owner,int amount)
        {
            var status=Add(e,owner,RazorScriptHooks.Sigils,5);
            CountEffect.Execute(RazorScriptHooks.Sigils,amount.ToString(),owner,new[]{owner});
            return status;
        }
        private static void SpendSigils(BattleEntity owner)
            => CountEffect.Execute(RazorScriptHooks.Sigils,"-3",owner,new[]{owner});

        private static BattleEntity Setup(BattleFlowTestEnv e, bool hexerei = false, int constellation = 0)
        {
            e.ConfigureAllyAction(0,"SK_Normal_Razor","SE_Normal_Razor","Physical","1.3605*TotalATK,0",25);
            e.ConfigureAllyAction(2,RazorScriptHooks.Press,"SE_Skill_Razor1","Electro",null,15,1,param1:"1.992*TotalATK,2");
            e.ConfigureAllyAction(2,RazorScriptHooks.Hold,"SE_Skill_Razor2","Electro",null,30,2,param1:"2.952*TotalATK,2");
            e.ConfigureAllyAction(3,"SK_Burst_Razor","SE_Burst_RazorDamage","Electro",null,0,5,param1:"1.6*TotalATK,2");
            var burst=e.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID].Single(s=>s.SkillID2=="SK_Burst_Razor");
            burst.EnergyUsed=80;burst.MaxCharge=burst.InitialCharge=1;
            e.DataManager.SkillEffectDict["SE_Burst_RazorDamage"].EffectIndex=2;
            SkillEffect(e,"SE_Burst_RazorSwitch",1,"SwitchIn",null);
            var grantWolf=SkillEffect(e,"SE_Burst_RazorWolf",3,"ApplyStatus",RazorScriptHooks.Wolf);grantWolf.Duration=4;grantWolf.AddInPhase=2;
            var burstConsume=SkillEffect(e,"SE_Burst_RazorConsume",4,"Count",RazorScriptHooks.Sigils,"Razor_HasSigils()");burstConsume.Param2="-3";
            for(int n=1;n<=3;n++)
            {
                var f=SkillEffect(e,"SE_Burst_RazorEnergy"+n,4+n,"GainEnergy",(n*5).ToString(),$"Razor_Consumed({n})");f.Param2="Flat";
            }
            SkillEffect(e,"SE_Burst_RazorReset",8,"ResetCooldown","Skill","Check(1019_T1)");
            e.DataManager.CharacterAttributesDict[BattleFlowTestEnv.AllyID].ElementalSkillID = RazorScriptHooks.Press;
            var skills = e.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID];
            skills.Single(s=>s.SkillID2==RazorScriptHooks.Press).SkillID = 101903;
            skills.Single(s=>s.SkillID2==RazorScriptHooks.Hold).SkillID = 101904;
            foreach(var s in skills.Where(s=>s.ActionType=="Skill"))s.SkillPhase = 1019;
            var init = SkillEffect(e,"SE_Skill_Razor1Init",1,"ApplyStatus",RazorScriptHooks.Sigils,"Razor_PressGain()");
            init.Duration=5;init.AddInPhase=2;
            var gain = SkillEffect(e,"SE_Skill_Razor1Gain",2,"Count",RazorScriptHooks.Sigils,"Razor_PressBaseGain()");
            gain.Param2="1";
            gain = SkillEffect(e,"SE_Skill_Razor1C6Gain",3,"Count",RazorScriptHooks.Sigils,"Check(1019_C6);Razor_PressHit()");
            gain.Param2 = "3";
            var extra = SkillEffect(e,"SE_Skill_Razor1Extra",4,"Damage","1.5*TotalATK,1","Razor_Overflow()");
            extra.Element = "Electro"; extra.TargetType = "Enemy"; extra.TargetOverride = "Razor_PressTargets()";
            var energy = SkillEffect(e,"SE_Skill_Razor1Energy",5,"GainEnergy","7","Razor_Overflow()"); energy.Param2 = "Flat";
            var consume = SkillEffect(e,"SE_Skill_Razor2Consume",1,"Count",RazorScriptHooks.Sigils,"Razor_HasSigils()"); consume.Param2 = "-3";
            for(int n=1;n<=3;n++)
            {
                var f=SkillEffect(e,"SE_Skill_Razor2Energy"+n,n+1,"GainEnergy",(n*5).ToString(),$"Razor_Consumed({n})"); f.Param2="Flat";
            }
            Main(e,1,RazorScriptHooks.Sigils,"Count","RechargeBonus");
            Effect(e,1,1,"STE_Razor_SigilsBuff","Buff","0.2*Count(ST_Razor_Sigils)");
            Main(e,2,RazorScriptHooks.Wolf);
            var cost=Effect(e,2,1,"STE_Razor_WolfCost","APCost","-5");cost.Param2="Normal";
            var wolf=Effect(e,2,2,"STE_Razor_WolfDamage","Damage");
            wolf.Element="Electro";wolf.TargetType="Enemy";wolf.TargetOverride="Razor_ActionTargets()";wolf.DamageHook="Razor_WolfMultiplier()";
            var refund=Effect(e,2,3,"STE_Razor_WolfRefund4","GainEnergy","12"); refund.Param2="Flat";refund.ScriptHook="Razor_WolfRounds(4)";
            Effect(e,2,4,"STE_Razor_WolfEnd","RemoveStatus",RazorScriptHooks.Wolf);
            Actions(e,RazorScriptHooks.Wolf,
                new StatusActionData { ActionType="OnTrigger",Param1=wolf.StatusEffectID2,OnAction="Normal",ScriptHook="Razor_WolfAction()" },
                new StatusActionData { ActionType="OnTrigger",Param1=refund.StatusEffectID2+";STE_Razor_WolfEnd",ScriptHook="SwitchOut(Self)" });
            Main(e,4,"ST_Razor_C1"); Main(e,5,"ST_Razor_C1Buff","Buff","DMGBonus");
            var c1=Effect(e,4,1,"STE_Razor_C1Gain","ApplyStatus","ST_Razor_C1Buff");c1.Duration=2;c1.AddInPhase=2;
            Effect(e,5,1,"STE_Razor_C1Buff","Buff","0.1");
            Actions(e,"ST_Razor_C1",new StatusActionData {ActionType="OnTrigger",Param1=c1.StatusEffectID2,ScriptHook="Razor_EnergyGained()"});
            Main(e,9,"ST_Razor_C6");Main(e,10,"ST_Razor_C6Buff","Buff","CritRate");Main(e,11,"ST_Razor_C6CritDMG","Buff","CritDMG");
            var spent=Effect(e,9,1,"STE_Razor_C6Gain","ApplyStatus","ST_Razor_C6Buff");spent.Duration=4;spent.AddInPhase=2;
            var c6Damage=Effect(e,9,2,"STE_Razor_C6Damage","Damage","1*TotalATK,1");
            c6Damage.Element="Electro";c6Damage.TargetType="Enemy";c6Damage.TargetOverride="Razor_HitTarget()";
            var c6Init=Effect(e,9,4,"STE_Razor_C6SigilsInit","ApplyStatus",RazorScriptHooks.Sigils);
            c6Init.Duration=5;c6Init.AddInPhase=2;c6Init.ScriptHook="Razor_C6OutsideWolf()";
            var c6Sigils=Effect(e,9,3,"STE_Razor_C6Sigils","Count",RazorScriptHooks.Sigils);
            c6Sigils.Param2="3";c6Sigils.ScriptHook="Razor_C6OutsideWolf()";
            Effect(e,10,1,"STE_Razor_C6CritRate","Buff","0.1");
            Effect(e,10,2,"STE_Razor_C6BindCritDMG","BindStatus","ST_Razor_C6CritDMG");
            Effect(e,11,1,"STE_Razor_C6CritDMG","Buff","0.5");
            Actions(e,"ST_Razor_C6",
                new StatusActionData {ActionType="OnTrigger",Param1=spent.StatusEffectID2,ScriptHook="Razor_SigilsSpent()"},
                new StatusActionData {ActionType="OnTrigger",Param1="STE_Razor_C6Damage;STE_Razor_C6SigilsInit;STE_Razor_C6Sigils",ScriptHook="Razor_NormalHit()",OutgoingHit="Normal",Cooldown=2});
            e.DataManager.SkillLevelDict["1019_0_Query"] = new Dictionary<int,SkillLevelData>
            { [1]=new SkillLevelData {CharacterID=1019,SkillType=0,SkillLevel=1,ParamID="SE_Normal_Razor",Hits1="1.3605*TotalATK,0"},
              [2]=new SkillLevelData {CharacterID=1019,SkillType=0,SkillLevel=2,ParamID="SE_Normal_Razor",Hits1="1.4532"} };
            e.DataManager.SkillLevelDict["1019_3_Query"] = new Dictionary<int,SkillLevelData>
            { [1]=new SkillLevelData {CharacterID=1019,SkillType=3,SkillLevel=1,ParamID=wolf.StatusEffectID2,Hits1=".24*TotalATK,1"},
              [2]=new SkillLevelData {CharacterID=1019,SkillType=3,SkillLevel=2,ParamID=wolf.StatusEffectID2,Hits1=".258"} };
            e.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_0"][2]=new SkillLevelData
                {CharacterID=BattleFlowTestEnv.AllyID,SkillType=0,SkillLevel=2,ParamID="SE_Normal_Razor",Hits1="1.4532"};
            using(var p = new ExcelPackage())
            {
                var sheet=p.Workbook.Worksheets.Add("Hexerei");sheet.Cells[1,1].Value="Key";sheet.Cells[1,2].Value="Value";
                sheet.Cells[2,1].Value="Enabled";sheet.Cells[2,2].Value=hexerei?1:0;
                sheet.Cells[3,1].Value="CharacterIDs";sheet.Cells[3,2].Value="1019;990001";
                sheet.Cells[4,1].Value="RequiredCount";sheet.Cells[4,2].Value=2;
                e.DataManager.TeamBonusRules.Hexerei=TeamBonusTableLoader.Read(sheet);
            }
            e.CreateMinimalBattle();var owner=e.Ally.Entity;owner.EntityID=1019;owner.MaxEnergy=80;owner.CurrentEnergy=0;owner.CritRate=0;
            e.DataManager.CharacterAttributesDict[1019]=new CharacterAttributesData {CharacterID=1019,Element="Electro"};
            e.Enemy.Entity.TotalHP=e.Enemy.Entity.CurrentHP=10000;BattleAttributeInitialization.Prepare(e.Enemy.Entity);
            e.CreateAlly(2);
            e.Manager.StartBattle();e.Flow.AdvanceToPhase(e.Manager,TurnPhase.AllyAction);
            owner.ConstellationLevel=constellation;BattleRandom.SetSource(new SequenceBattleRandomSource());return owner;
        }

        [UnityTest] public IEnumerator Sigils_CapRefreshAndReadOnlyRecharge_AndZeroSpendDoesNotBuff()
            => HookTestEnv.AwaitTask(SigilsAsync);
        private async Task SigilsAsync()
        {
            using var e=new BattleFlowTestEnv();var owner=Setup(e);
            Add(e,owner,"ST_Razor_C6");Assert.That(owner.GetStatusRechargeBonus(),Is.Zero);
            Assert.That(owner.HasStatus(RazorScriptHooks.Sigils),Is.False);
            var a=GainSigils(e,owner,1);Assert.That(a.RuntimeIntValue,Is.EqualTo(1));
            a.RemainingPhaseCount=2;
            Assert.That(GainSigils(e,owner,3),Is.SameAs(a));
            Assert.That(a.RuntimeIntValue,Is.EqualTo(3));Assert.That(a.RemainingPhaseCount,Is.EqualTo(5));
            for(int i=0;i<3;i++)Assert.That(owner.GetStatusRechargeBonus(),Is.EqualTo(.6f).Within(.0001));
            Assert.That(a.RuntimeIntValue,Is.EqualTo(3));
            await BattleExecution.Run(async()=>{SpendSigils(owner);await BattleExecution.Flush();});
            Assert.That(a.RuntimeIntValue,Is.Zero);Assert.That(owner.GetStatusRechargeBonus(),Is.Zero);
            Assert.That(a.IsActive,Is.True);Assert.That(owner.GetStatus(RazorScriptHooks.Sigils),Is.SameAs(a));
            Assert.That(owner.HasStatus("ST_Razor_C6Buff"),Is.True);Assert.That(owner.HasStatus("ST_Razor_C6CritDMG"),Is.True);
            var buff=owner.GetStatus("ST_Razor_C6Buff");buff.RemainingPhaseCount=1;
            await BattleExecution.Run(async()=>{SpendSigils(owner);await BattleExecution.Flush();});
            Assert.That(owner.GetStatus("ST_Razor_C6Buff"),Is.SameAs(buff));Assert.That(buff.RemainingPhaseCount,Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator Press_C6ZeroBecomesThreeWithoutOverflow_ExistingOneOverflowsOnce()
            => HookTestEnv.AwaitTask(()=>OverflowAsync(true,true));
        [Test] public void SigilValues_DoNotReapplyRefreshOrRemove_WhileApplyStatusOnlyRefreshes()
        {
            using var e=new BattleFlowTestEnv();var owner=Setup(e);
            Add(e,owner,"ST_Razor_C6");
            var status=Add(e,owner,RazorScriptHooks.Sigils,5);
            Assert.That(status.RuntimeIntValue,Is.Zero);
            status.RemainingPhaseCount=2;
            CountEffect.Execute(RazorScriptHooks.Sigils,"3",owner,new[]{owner});
            Assert.That(status.RuntimeIntValue,Is.EqualTo(3));Assert.That(status.RemainingPhaseCount,Is.EqualTo(2));
            Assert.That(Add(e,owner,RazorScriptHooks.Sigils,5),Is.SameAs(status));
            Assert.That(status.RuntimeIntValue,Is.EqualTo(3));Assert.That(status.RemainingPhaseCount,Is.EqualTo(5));
            CountEffect.Execute(RazorScriptHooks.Sigils,"clear",owner,new[]{owner});
            Assert.That(status.RuntimeIntValue,Is.Zero);Assert.That(status.IsActive,Is.True);
            Assert.That(owner.HasStatus("ST_Razor_C6Buff"),Is.False,"clear不是Spend，不能伪造消费增益。");
            Assert.That(owner.GetStatus(RazorScriptHooks.Sigils),Is.SameAs(status));
            owner.RemoveStatus(RazorScriptHooks.Sigils);
            Assert.That(owner.HasStatus(RazorScriptHooks.Sigils),Is.False);
        }
        [UnityTest] public IEnumerator Press_OverflowRequiresDoubleHexerei()
            => HookTestEnv.AwaitTask(()=>OverflowAsync(false,true));
        [UnityTest] public IEnumerator Press_OverflowRequiresWolf()
            => HookTestEnv.AwaitTask(()=>OverflowAsync(true,false));
        private async Task OverflowAsync(bool hexerei,bool wolf)
        {
            using var e=new BattleFlowTestEnv();var owner=Setup(e,hexerei,6);
            if(wolf)Add(e,owner,RazorScriptHooks.Wolf,4);
            int extras=0;using var hits=BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,(_,h)=>
            {if(h.Source.SourceEffectID=="SE_Skill_Razor1Extra")extras++;},0);
            Assert.That(e.Manager.UseSkillBySlot(0),Is.True);await BattleExecution.Flush();
            Assert.That(owner.GetStatus(RazorScriptHooks.Sigils).RuntimeIntValue,Is.EqualTo(3));
            Assert.That(extras,Is.Zero);Assert.That(owner.CurrentEnergy,Is.Zero);
            e.Ally.ResetSkillCooldown("Skill");
            // 用正式Count入口支付，再重新施加并加1，不直接修改内部余额。
            SpendSigils(owner);GainSigils(e,owner,1);
            Assert.That(e.Manager.UseSkillBySlot(0),Is.True);await BattleExecution.Flush();
            Assert.That(owner.GetStatus(RazorScriptHooks.Sigils).RuntimeIntValue,Is.EqualTo(3));
            Assert.That(extras,Is.EqualTo(hexerei&&wolf?1:0));
            Assert.That(owner.CurrentEnergy,Is.EqualTo(hexerei&&wolf?7:0));
        }
        [UnityTest] public IEnumerator Hold_ConsumesThreeForOneFifteenEnergyFact_AndRestoredFirstButtonKeepsCooldown()
            => HookTestEnv.AwaitTask(HoldAsync);
        [UnityTest] public IEnumerator Hold_PaysActualOneTwoOrThree_WithOneEnergyFactAndZeroInstanceRetained()
            => HookTestEnv.AwaitTask(ActualPaymentsAsync);
        private async Task ActualPaymentsAsync()
        {
            for(int amount=1;amount<=3;amount++)
            {
                using var e=new BattleFlowTestEnv();var owner=Setup(e);var count=GainSigils(e,owner,amount);
                var payments=new List<ResourceEventData>();var gains=new List<ResourceEventData>();
                using var resources=BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged,(_,fact)=>
                {
                    if(fact.Subject!=owner)return;
                    if(fact.PoolId==RazorScriptHooks.Sigils && fact.Reason==ResourceChangeReason.Spend)payments.Add(fact);
                    if(fact.Resource=="Energy" && fact.Added>0)gains.Add(fact);
                },0);
                e.Ally.AdvanceSkillPhase(2);
                Assert.That(e.Manager.UseSkillBySlot(0),Is.True);await BattleExecution.Flush();
                Assert.That(payments.Count,Is.EqualTo(1));Assert.That(payments[0].RequestedDelta,Is.EqualTo(-3));
                Assert.That(payments[0].Removed,Is.EqualTo(amount));
                Assert.That(gains.Count,Is.EqualTo(1));Assert.That(gains[0].Added,Is.EqualTo(amount*5));
                Assert.That(count.RuntimeIntValue,Is.Zero);Assert.That(count.IsActive,Is.True);
            }
        }
        private async Task HoldAsync()
        {
            using var e=new BattleFlowTestEnv();var owner=Setup(e);GainSigils(e,owner,3);
            Add(e,owner,"ST_Razor_C1");var gains=new List<ResourceEventData>();
            using var listener=BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged,(_,r)=>
            {if(r.Subject==owner&&r.Resource=="Energy"&&r.Added>0)gains.Add(r);},0);
            int before=e.Manager.APManager.CurrentAP;
            Assert.That(e.Ally.AdvanceSkillPhase(2),Is.EqualTo(RazorScriptHooks.Hold));
            Assert.That(e.Manager.APManager.CurrentAP,Is.EqualTo(before));
            Assert.That(e.Manager.UseSkillBySlot(0),Is.True);await BattleExecution.Flush();
            e.Ally.RevertSkillPhase(2,RazorScriptHooks.Press);
            Assert.That(e.Manager.APManager.CurrentAP,Is.EqualTo(before-30));Assert.That(e.Ally.GetCooldownRemaining(2),Is.EqualTo(2));
            Assert.That(e.Manager.UseSkillBySlot(0),Is.False);Assert.That(gains.Count,Is.EqualTo(1));Assert.That(gains[0].Added,Is.EqualTo(15));
            Assert.That(owner.HasStatus("ST_Razor_C1Buff"),Is.True);Assert.That(owner.GetStatusRechargeBonus(),Is.Zero);
            e.Ally.TickSkillCooldowns();Assert.That(e.Ally.GetCooldownRemaining(2),Is.EqualTo(1));
            e.Ally.ResetSkillCooldown(RazorScriptHooks.Hold);Assert.That(e.Ally.GetCooldownRemaining(2),Is.Zero);
        }
        [UnityTest] public IEnumerator Wolf_UsesIndependentDamageAndCurrentTalentProduct_NotNormalDamageType()
            => HookTestEnv.AwaitTask(()=>WolfAsync(false));
        [UnityTest] public IEnumerator Wolf_DoubleHexereiAddsSeventyPercentATKToBase_NotDamageBonus()
            => HookTestEnv.AwaitTask(()=>WolfAsync(true));
        private async Task WolfAsync(bool hexerei)
        {
            using var e=new BattleFlowTestEnv();var owner=Setup(e,hexerei);Add(e,owner,RazorScriptHooks.Wolf,4);
            e.Ally.SkillLevels[0]=2;e.Ally.SkillLevels[3]=2;
            var damage=new List<DamageEventData>();int actions=0;
            using var hits=BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,(_,h)=>damage.Add(h),0);
            using var starts=BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted,(_,a)=>{if(a.Subject==owner)actions++;},0);
            var random=new SequenceBattleRandomSource();BattleRandom.SetSource(random);
            Assert.That(e.Manager.UseNormalAttackBySlot(0),Is.True);await BattleExecution.Flush();
            var wolf=damage.Single(d=>d.Source.SourceEffectID=="STE_Razor_WolfDamage");
            Assert.That(wolf.DamageType,Is.Null.Or.Empty);Assert.That(wolf.DamageBeforeShield,Is.EqualTo((1.4532f*.258f+(hexerei?.7f:0f))*50f).Within(.003));
            Assert.That(damage.Count,Is.EqualTo(2));Assert.That(actions,Is.EqualTo(1));
            Assert.That(e.Manager.APManager.CurrentAP,Is.EqualTo(80));Assert.That(random.FloatCallCount,Is.EqualTo(2));Assert.That(random.IntCallCount,Is.Zero);
            Assert.That(e.DataManager.StatusEffectDict["STE_Razor_WolfDamage"].Param1,Is.Null);
        }
        [UnityTest] public IEnumerator C6_NormalHitAddsThreeOnce_ExtraDamageDoesNotRetriggerItself()
            => HookTestEnv.AwaitTask(C6NormalAsync);
        private async Task C6NormalAsync()
        {
            using var e=new BattleFlowTestEnv();var owner=Setup(e,false,6);Add(e,owner,"ST_Razor_C6");
            var damage=new List<DamageEventData>();var random=new SequenceBattleRandomSource();BattleRandom.SetSource(random);
            using var listener=BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,(_,h)=>damage.Add(h),0);
            Assert.That(e.Manager.UseNormalAttackBySlot(0),Is.True);await BattleExecution.Flush();
            Assert.That(owner.GetStatus(RazorScriptHooks.Sigils).RuntimeIntValue,Is.EqualTo(3));
            Assert.That(e.Manager.UseNormalAttackBySlot(0),Is.True);await BattleExecution.Flush();
            var extra=damage.Single(d=>d.Source.SourceEffectID=="STE_Razor_C6Damage");
            Assert.That(extra.DamageType,Is.Null.Or.Empty);Assert.That(damage.Count,Is.EqualTo(3));
            Assert.That(random.FloatCallCount,Is.EqualTo(3));Assert.That(random.IntCallCount,Is.Zero);
        }
        [UnityTest] public IEnumerator Wolf_SwitchOutRefundsRemainingRounds_ButDeathDoesNot()
            => HookTestEnv.AwaitTask(EndWolfAsync);
        private async Task EndWolfAsync()
        {
            using var e=new BattleFlowTestEnv();var owner=Setup(e);Add(e,owner,RazorScriptHooks.Wolf,4);
            Assert.That(e.Manager.TrySwitchInFromEffect(e.Manager.Allies.Single(a=>a.Entity.SlotPosition==2).Entity),Is.True);
            await BattleExecution.Flush();Assert.That(owner.CurrentEnergy,Is.EqualTo(12));Assert.That(owner.HasStatus(RazorScriptHooks.Wolf),Is.False);
            Assert.That(e.Manager.TrySwitchInFromEffect(owner),Is.True);await BattleExecution.Flush();
            Add(e,owner,RazorScriptHooks.Wolf,4);
            await BattleExecution.Run(async()=>
            {
                using(var batch=new HitBatch())batch.Resolve(new HitRequest {Target=owner,DamageBeforeShield=999999,Element="Physical",DamageType="Normal",
                    Source=DamageSourceInfo.Create(e.Enemy.Entity,ReactionSourceKind.EnemySkill,"SK_Enemy_BattleFlow","SE_Enemy_BattleFlow")});
                await BattleExecution.Flush();
            });
            Assert.That(owner.IsDead,Is.True);Assert.That(owner.HasStatus(RazorScriptHooks.Wolf),Is.False);Assert.That(owner.CurrentEnergy,Is.EqualTo(12));
        }
        [UnityTest] public IEnumerator Burst_PaysEightyThenConsumesActualSigils_AndTalentResetsBothSkillVariants()
            => HookTestEnv.AwaitTask(BurstAsync);
        private async Task BurstAsync()
        {
            using var e=new BattleFlowTestEnv();var owner=Setup(e,false,6);Add(e,owner,"ST_Razor_C6");Add(e,owner,"ST_Razor_C1");
            // 只隔离重置效果，实际突破门槛由公共CharacterAscensionRules测试覆盖。
            e.DataManager.TalentDict[1019]=new List<TalentData> {new TalentData {CharacterID=1019,TalentIndex=1,UnlockAfter=0}};
            Assert.That(e.Manager.UseSkillBySlot(0),Is.True);await BattleExecution.Flush();
            Assert.That(owner.GetStatus(RazorScriptHooks.Sigils).RuntimeIntValue,Is.EqualTo(3));
            owner.CurrentEnergy=80;var gains=new List<ResourceEventData>();
            using var listener=BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged,(_,r)=>
            {if(r.Subject==owner&&r.Resource=="Energy")gains.Add(r);},0);
            Assert.That(e.Manager.UseBurstBySlot(0),Is.True);await BattleExecution.Flush();
            Assert.That(owner.CurrentEnergy,Is.EqualTo(15));Assert.That(gains.Count(r=>r.Removed==80),Is.EqualTo(1));
            Assert.That(gains.Count(r=>r.Added==15),Is.EqualTo(1));Assert.That(owner.GetStatus(RazorScriptHooks.Sigils).RuntimeIntValue,Is.Zero);
            Assert.That(owner.HasStatus(RazorScriptHooks.Wolf),Is.True);Assert.That(owner.HasStatus("ST_Razor_C6Buff"),Is.True);
            Assert.That(owner.HasStatus("ST_Razor_C1Buff"),Is.True);Assert.That(e.Ally.GetCooldownRemaining(2),Is.Zero);
            e.Ally.AdvanceSkillPhase(2);Assert.That(e.Ally.GetCooldownRemaining(2),Is.Zero);
        }
        [Test] public void PassiveThresholds_AreStrictPerTarget_AndQueriesDoNotPublishResources()
        {
            using var e=new BattleFlowTestEnv();var owner=Setup(e);
            var t2=Main(e,3,"ST_Razor_T2","Buff","RechargeBonus");t2.ScriptHook="Razor_LowEnergy()";
            Effect(e,3,1,"STE_Razor_T2Buff","Buff",".3");Add(e,owner,t2.StatusID2);
            var c2=Main(e,6,"ST_Razor_C2","Buff","CritRate");c2.ScriptHook="Razor_LowTargetHP()";
            Effect(e,6,1,"STE_Razor_C2Buff","Buff",".1");Add(e,owner,c2.StatusID2);
            var other=e.CreateEnemy(2).Entity;int resources=0;
            using var listener=BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged,(_,r)=>resources++,0);
            owner.CurrentEnergy=40;Assert.That(owner.GetStatusRechargeBonus(),Is.Zero);
            owner.CurrentEnergy=39.5f;Assert.That(owner.GetStatusRechargeBonus(),Is.EqualTo(.3f).Within(.0001));
            e.Enemy.Entity.CurrentHP=2999;other.CurrentHP=300;
            Assert.That(owner.GetStatusCritRate("SE_Normal_Razor","SK_Normal_Razor",0,"Physical",e.Enemy.Entity),Is.EqualTo(.1f).Within(.0001));
            Assert.That(owner.GetStatusCritRate("SE_Normal_Razor","SK_Normal_Razor",0,"Physical",other),Is.Zero);
            Assert.That(resources,Is.Zero);
        }
    }
}
