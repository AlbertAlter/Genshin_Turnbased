using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    // 独立内存配表走正式技能/护盾/受击/阶段入口；不读取Charts、Docs或改写内部Count余额。
    public sealed class BeidouScriptHooksTests
    {
        private static StatusMainData Main(BattleFlowTestEnv env, int n, string id, string type = null)
        {
            var main = new StatusMainData { StatusID = 4101800 + n, StatusID2 = id, StatusName = id,
                StatusType = type, MaxCount = 1, MaxStack = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[id] = main;
            if (!env.DataManager.CharacterStatusIDs.TryGetValue(1018, out var ids))
                env.DataManager.CharacterStatusIDs[1018] = ids = new HashSet<string>();
            ids.Add(id); return main;
        }
        private static StatusEffectData Effect(BattleFlowTestEnv env, int n, int index, string id, string type,
            string param = null, string target = "Self")
        {
            var effect = new StatusEffectData { StatusEffectID = (4101800 + n) * 100 + index, StatusEffectID2 = id,
                EffectIndex = index, EffectType = type, Param1 = param, TargetType = target };
            env.DataManager.StatusEffectDict[id] = effect; return effect;
        }
        private static void Actions(BattleFlowTestEnv env, int n, string id, params StatusActionData[] actions)
        {
            foreach (var a in actions) { a.StatusID = 4101800 + n; a.StatusID2 = id; }
            env.DataManager.StatusActionDict[id] = new List<StatusActionData>(actions);
        }

        private static BattleEntity Setup(BattleFlowTestEnv env, string shield = "10000", bool talent = false)
        {
            env.ConfigureEnemyDamageSkill(.01f); env.DataManager.EnemySkillMainDict[990201].UsePerTurn = 0;
            env.DataManager.EnemySkillEffectDict["SE_Enemy_BattleFlow"].TargetConsecutive = 2;
            env.ConfigureAllyAction(0,"SK_Normal_Beidou","SE_Normal_Beidou","Physical","1.1214*TotalATK,0",25);
            env.ConfigureAllyAction(2,"SK_Skill_Beidou","SE_Skill_Beidou",null,null,30,2,"ApplyStatus",
                BeidouScriptHooks.GuardID,1,6,6,"Self");
            Main(env,1,BeidouScriptHooks.GuardID,"Count"); Main(env,2,BeidouScriptHooks.ShieldID,"Shield");
            var taunt = new StatusMainData { StatusID=40038,StatusID2="Taunt",MaxStack=1,WhenMax="0" };
            env.DataManager.StatusMainDict["Taunt"] = taunt;
            var bind = Effect(env,1,1,"STE_Test_BeidouBindShields","BindStatus",BeidouScriptHooks.ShieldID,"Allies");
            bind.TargetSelect="1,1";
            Effect(env,1,2,"STE_Test_BeidouBindTaunt","BindStatus","Taunt");
            var stack = Effect(env,1,3,"STE_Test_BeidouStack","ApplyStatus",BeidouScriptHooks.GuardID);
            stack.Param2="Stack"; stack.ScriptHook="Beidou_EnemyHit()";
            var counter = new List<string>();
            for(int layer=0;layer<=2;layer++)
            {
                string damageID="STE_Test_BeidouCounter"+layer, energyID="STE_Test_BeidouEnergy"+layer;
                var damage=Effect(env,1,4+layer*2,damageID,"Damage",$"{(1.216f+1.6f*layer).ToString(System.Globalization.CultureInfo.InvariantCulture)}*TotalATK,2","Enemy");
                damage.Element="Electro"; damage.DamageType="Skill"; damage.TargetSelect="-1";
                damage.ScriptHook=$"Beidou_CounterLayer({layer})";
                var energy=Effect(env,1,5+layer*2,energyID,"GainEnergy",(4+2*layer).ToString());
                energy.Param2="Based";energy.Element="Electro";energy.ScriptHook=$"Beidou_CounterLayer({layer})";
                counter.Add(damageID);counter.Add(energyID);
            }
            Effect(env,1,11,"STE_Test_BeidouEnd","RemoveStatus",BeidouScriptHooks.GuardID);counter.Add("STE_Test_BeidouEnd");
            Actions(env,1,BeidouScriptHooks.GuardID,
                new StatusActionData { ActionType="OnTrigger",Param1=stack.StatusEffectID2,ScriptHook="Beidou_EnemyHit()",OnHit="All",MaxTimePerAction=1 },
                new StatusActionData { ActionType="OnTrigger",Param1=string.Join(";",counter),ScriptHook=BeidouScriptHooks.BrokenHook },
                new StatusActionData { ActionType="OnItsTurn",Param1=string.Join(";",counter),ScriptHook="Beidou_GuardReady()" });
            var shieldEffect=Effect(env,2,1,"STE_Test_BeidouShield","Shield",shield);shieldEffect.Element="Electro";
            Actions(env,2,BeidouScriptHooks.ShieldID,new StatusActionData { ActionType="OnApply",Param1=shieldEffect.StatusEffectID2 });
            if(talent) env.DataManager.TalentDict[1018]=new List<TalentData>
            { new TalentData { CharacterID=1018,TalentIndex=1,UnlockAfter=0 } }; // 隔离破盾满档规则；突破门槛另有公共测试。
            env.DataManager.CharacterAttributesDict[BattleFlowTestEnv.AllyID].Element="Electro";
            env.CreateMinimalBattle();var owner=env.Ally.Entity;owner.EntityID=1018;owner.CritRate=0;
            owner.Position.Occupant=null;owner.SlotPosition=2;owner.Position=env.Manager.Field.GetSlot(BattleSide.Ally,2);owner.Position.Occupant=owner;
            env.CreateAlly(1);env.CreateAlly(3);
            env.Manager.StartBattle();env.Flow.AdvanceToPhase(env.Manager,TurnPhase.AllyAction);
            BattleRandom.SetSource(new SequenceBattleRandomSource());return owner;
        }

        [UnityTest]
        public IEnumerator Guard_OneRealEnemyActionWithThreeHits_AddsOnlyOne_AndCapsAtTwo()
            => HookTestEnv.AwaitTask(StackAsync);
        private async Task StackAsync()
        {
            using var env=new BattleFlowTestEnv();var owner=Setup(env);
            env.DataManager.EnemySkillParamDict["SE_Enemy_BattleFlow"].Hits2=".01,0";
            env.DataManager.EnemySkillParamDict["SE_Enemy_BattleFlow"].Hits3=".01,0";
            Assert.That(env.Manager.UseSkillBySlot(0),Is.True);var guard=owner.GetStatus(BeidouScriptHooks.GuardID);
            var bindings=guard.BoundStatuses.ToArray();Assert.That(bindings.Count(s=>s.StatusID2==BeidouScriptHooks.ShieldID),Is.EqualTo(3));
            for(int action=1;action<=3;action++)
            {
                await BattleExecution.Run(env.Enemy.TakeEnemyActionAsync);
                Assert.That(guard.RuntimeIntValue,Is.EqualTo(Math.Min(2,action)));
                Assert.That(guard.RemainingPhaseCount,Is.EqualTo(1));
                Assert.That(guard.BoundStatuses,Is.EqualTo(bindings),"叠层不能重施盾或重绑Taunt");
            }
        }

        [UnityTest]
        public IEnumerator Guard_EnemyRoundEndAutomaticallyReleasesZeroTier_WithoutChoosingAnotherSkill()
            => HookTestEnv.AwaitTask(EndPhaseAsync);
        private async Task EndPhaseAsync()
        {
            using var env=new BattleFlowTestEnv();var owner=Setup(env);
            env.DataManager.EnemySkillEffectDict["SE_Enemy_BattleFlow"].ScriptHook="OnField(999999)";
            var counter=new List<DamageEventData>();
            using var listener=BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,(_,hit)=>
            { if(hit.Source.SourceEffectID.StartsWith("STE_Test_BeidouCounter"))counter.Add(hit); },0);
            Assert.That(env.Manager.UseSkillBySlot(0),Is.True);Assert.That(counter,Is.Empty);
            int turn=env.Manager.TurnCount;env.Manager.EndAllyTurn();
            env.Flow.AdvanceUntil(env.Manager,()=>env.Manager.TurnCount>turn
                && env.Manager.CurrentPhase==TurnPhase.AllyAction && env.Manager.IsCurrentPhaseReady,"next ally action phase");
            await BattleExecution.Flush();
            Assert.That(counter.Count,Is.EqualTo(1));Assert.That(counter[0].Source.SourceEffectID,Does.EndWith("0"));
            Assert.That(counter[0].DamageBeforeShield,Is.EqualTo(60.8f).Within(.001));
            Assert.That(owner.CurrentEnergy,Is.EqualTo(6f).Within(.001),"4点Based乘同元素1.5");
            Assert.That(owner.HasStatus(BeidouScriptHooks.GuardID),Is.False);Assert.That(owner.HasStatus("Taunt"),Is.False);
        }

        [UnityTest]
        public IEnumerator Guard_ThreeShieldsBreakingSameHit_CountsOwnerHitFirst_AndCounterattacksOnlyOnce()
            => HookTestEnv.AwaitTask(()=>BrokenAsync(false));
        [UnityTest]
        public IEnumerator Guard_TalentForcesFullTierIncludingEightEnergy_WhenAnyBoundShieldBreaks()
            => HookTestEnv.AwaitTask(()=>BrokenAsync(true));
        private async Task BrokenAsync(bool talent)
        {
            using var env=new BattleFlowTestEnv();var owner=Setup(env,"110",talent);
            env.DataManager.EnemySkillEffectDict["SE_Enemy_BattleFlow"].TargetNumber=3;
            env.DataManager.EnemySkillParamDict["SE_Enemy_BattleFlow"].Hits1="3,0";
            var counter=new List<DamageEventData>();
            using var listener=BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,(_,hit)=>
            {if(hit.Source.SourceEffectID.StartsWith("STE_Test_BeidouCounter"))counter.Add(hit);},0);
            Assert.That(env.Manager.UseSkillBySlot(0),Is.True);
            owner.AddShield(10000,"None",2); // 无关来源的盾并行承伤，但不能被捉浪结束误清。
            await BattleExecution.Run(env.Enemy.TakeEnemyActionAsync);
            Assert.That(counter.Count,Is.EqualTo(1));
            Assert.That(counter[0].Source.SourceEffectID,Does.EndWith(talent?"2":"1"));
            Assert.That(counter[0].DamageBeforeShield,Is.EqualTo(talent?220.8f:140.8f).Within(.002));
            Assert.That(owner.CurrentEnergy,Is.EqualTo(talent?12f:9f).Within(.001));
            Assert.That(owner.HasStatus(BeidouScriptHooks.GuardID),Is.False);Assert.That(owner.HasStatus("Taunt"),Is.False);
            Assert.That(owner.Shields.Count,Is.EqualTo(1));
            foreach(var ally in env.Manager.Allies)Assert.That(ally.Entity.HasStatus(BeidouScriptHooks.ShieldID),Is.False);
        }

        [UnityTest]
        public IEnumerator Guard_RefreshReplacesOldBindings_ButIsNotShieldBreak()
            => HookTestEnv.AwaitTask(RefreshAsync);
        private async Task RefreshAsync()
        {
            using var env=new BattleFlowTestEnv();var owner=Setup(env);int counters=0;
            using var listener=BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,(_,hit)=>
            {if(hit.Source.SourceEffectID.StartsWith("STE_Test_BeidouCounter"))counters++;},0);
            Assert.That(env.Manager.UseSkillBySlot(0),Is.True);var old=owner.GetStatus(BeidouScriptHooks.GuardID);
            StatusInstance replacement=null;
            await BattleExecution.Run(async()=>
            {replacement=owner.AddStatus(BeidouScriptHooks.GuardID,owner,1,6,6,old.MainData);await BattleExecution.Flush();});
            Assert.That(old.IsActive,Is.False);Assert.That(replacement,Is.Not.SameAs(old));
            Assert.That(counters,Is.Zero);Assert.That(replacement.RuntimeIntValue,Is.Zero);
            Assert.That(replacement.BoundStatuses.Count,Is.EqualTo(4));
            foreach(var ally in env.Manager.Allies)Assert.That(ally.Entity.Shields.Count,Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Guard_OwnerDiesOnBreakingHit_CleansBoundShieldsWithoutCounterattack()
            => HookTestEnv.AwaitTask(DeathAsync);
        private async Task DeathAsync()
        {
            using var env=new BattleFlowTestEnv();var owner=Setup(env);int counters=0;
            using var listener=BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,(_,hit)=>
            {if(hit.Source.SourceEffectID?.StartsWith("STE_Test_BeidouCounter")==true)counters++;},0);
            Assert.That(env.Manager.UseSkillBySlot(0),Is.True);
            await BattleExecution.Run(async()=>
            {
                using(var batch=new HitBatch())batch.Resolve(new HitRequest {Target=owner,DamageBeforeShield=999999,
                    Element="Physical",DamageType="Normal",Source=DamageSourceInfo.Create(env.Enemy.Entity,
                        ReactionSourceKind.EnemySkill,"SK_Enemy_BattleFlow","SE_Enemy_BattleFlow")});
                await BattleExecution.Flush();
            });
            Assert.That(owner.IsDead,Is.True);Assert.That(counters,Is.Zero);
            Assert.That(owner.HasStatus(BeidouScriptHooks.GuardID),Is.False);Assert.That(owner.HasStatus("Taunt"),Is.False);
            foreach(var ally in env.Manager.Allies)Assert.That(ally.Entity.Shields,Is.Empty);
        }

        [UnityTest]
        public IEnumerator Burst_SingleEnemyCanReceiveAllThreeBounces_C4DoesNotCreateExtraBounceOrAction()
            => HookTestEnv.AwaitTask(() => BurstAsync(0));
        [UnityTest]
        public IEnumerator Burst_ConstellationTwo_SingleEnemyCanReceiveAllFiveBounces()
            => HookTestEnv.AwaitTask(() => BurstAsync(2));
        private async Task BurstAsync(int constellation)
        {
            using var env=new BattleFlowTestEnv();
            var burst=Main(env,3,BeidouScriptHooks.BurstID,"Buff");
            var bounce=Effect(env,3,2,"STE_Test_BeidouBounce","Damage",".96*TotalATK,1","Enemy");
            bounce.Element="Electro";bounce.DamageType="Burst";bounce.TargetSelect="1";bounce.TargetConsecutive=3;
            bounce.ExecuteCountHandler="Beidou_BurstBounces";
            Actions(env,3,burst.StatusID2,new StatusActionData {ActionType="OnTrigger",Param1=bounce.StatusEffectID2,
                ScriptHook="Beidou_BurstHit()",OutgoingHit="Normal;Heavy",MaxTimePerHit=1});
            var follow=Main(env,7,"ST_Beidou_C4Follow");
            var extra=Effect(env,7,1,"STE_Test_BeidouC4Damage","Damage","%SE_Normal_Beidou.Hits1.M*0.2,1","Enemy");
            extra.Element="Electro";extra.TargetOverride="Beidou_HitTarget()";
            Actions(env,7,follow.StatusID2,new StatusActionData {ActionType="OnTrigger",Param1=extra.StatusEffectID2,
                ScriptHook="Beidou_NormalHit()",OutgoingHit="Normal"});
            var owner=Setup(env);owner.ConstellationLevel=constellation;
            owner.AddStatus(burst.StatusID2,owner,4,2,0,burst);owner.AddStatus(follow.StatusID2,owner,2,2,0,follow);
            var damage=new List<DamageEventData>();int actions=0;
            using var hits=BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,(_,hit)=>damage.Add(hit),0);
            using var starts=BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted,(_,a)=>{if(a.Subject==owner)actions++;},0);
            var random=new SequenceBattleRandomSource();BattleRandom.SetSource(random);
            Assert.That(env.Manager.UseNormalAttackBySlot(0),Is.True);await BattleExecution.Flush();
            int bounces=constellation>=2?5:3;
            Assert.That(damage.Count(d=>d.Source.SourceEffectID==bounce.StatusEffectID2),Is.EqualTo(bounces));
            var c4=damage.Single(d=>d.Source.SourceEffectID==extra.StatusEffectID2);
            Assert.That(c4.DamageType,Is.Null.Or.Empty);Assert.That(c4.DamageBeforeShield,Is.EqualTo(11.214f).Within(.002));
            Assert.That(actions,Is.EqualTo(1));Assert.That(random.IntCallCount,Is.Zero);Assert.That(random.FloatCallCount,Is.EqualTo(bounces+2));
        }
    }
}
