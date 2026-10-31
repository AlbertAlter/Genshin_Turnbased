using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    // 内存配表与真实公共入口，不让测试程序读取Charts。
    public sealed class JeanScriptHooksTests
    {
        private static BattleEntity Jean(BattleFlowTestEnv env, int constellation = 6)
        {
            var owner = env.Ally.Entity; owner.EntityID = 1013; owner.ConstellationLevel = constellation;
            owner.MaxEnergy = 80; owner.CurrentEnergy = 0; return owner;
        }
        private static StatusMainData Main(BattleFlowTestEnv env, int id, string key, string type = null)
        {
            var main = new StatusMainData { StatusID = id, StatusID2 = key, StatusType = type, MaxStack = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[key] = main; return main;
        }
        private static void ProbeAction(BattleFlowTestEnv env, StatusMainData main, string hook)
        {
            env.DataManager.StatusActionDict[main.StatusID2] = new List<StatusActionData>
            { new StatusActionData { StatusID2 = main.StatusID2, ActionType = "OnTrigger", ScriptHook = hook, Param1 = "STE_Jean_Probe" } };
            env.DataManager.StatusEffectDict["STE_Jean_Probe"] = new StatusEffectData
            { StatusEffectID = 49999101, StatusEffectID2 = "STE_Jean_Probe", EffectType = "GainEnergy", Param1 = "1", Param2 = "Flat", TargetType = "Self" };
        }
        private static void HitFact(BattleEntity source, BattleEntity target, string skill, string effect,
            ReactionSourceKind kind, bool landed = true)
            => BattleEventHub.Publish(new DamageEventData(new DamageResolvedEvent
            {
                Source = DamageSourceInfo.Create(source, kind, skill, effect), Target = target,
                HitLanded = landed, DamageType = "Normal", Element = "Physical"
            }));

        private static bool AirborneTargetsContain(string hook, BattleFlowTestEnv env, BattleEntity owner, BattleEntity target)
        {
            Assert.That(BattleSpecialRules.TryResolveTargets(hook,env.Manager,owner,out var positions), Is.True);
            return positions.Contains(target.SlotPosition);
        }

        [UnityTest]
        public IEnumerator Airborne_UsesActualHitAndClassification_C1OnlyExpandsSkill()
            => HookTestEnv.AwaitTask(AirborneAsync);
        private async Task AirborneAsync()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); var owner = Jean(env, 0);
            var target = env.Enemy.Entity;
            using (BattleSpecialRules.BeginSkill(owner))
            {
                env.DataManager.EnemyMainDict[BattleFlowTestEnv.EnemyID].EnemyType = "Minion";
                Assert.That(AirborneTargetsContain("Jean_HeavyAirborne()",env,owner,target), Is.False,
                    "继承选中位置不等于该目标已实际命中");
                await BattleExecution.Run(async () =>
                {
                    HitFact(owner,target,"SK_Heavy_Jean","SE_Heavy_Jean",ReactionSourceKind.CharacterSkill);
                    HitFact(owner,target,"SK_Skill_Jean","SE_Skill_Jean1",ReactionSourceKind.CharacterSkill);
                    await BattleExecution.Flush();
                });
                foreach (string category in new[] { "Minion", "Elite", "Boss", "" })
                {
                    env.DataManager.EnemyMainDict[BattleFlowTestEnv.EnemyID].EnemyType = category;
                    foreach (int c in new[] { 0, 1 })
                    {
                        owner.ConstellationLevel = c;
                        Assert.That(AirborneTargetsContain("Jean_HeavyAirborne()",env,owner,target),
                            Is.EqualTo(category == "Minion"));
                        Assert.That(AirborneTargetsContain("Jean_SkillAirborne()",env,owner,target),
                            Is.EqualTo(category == "Minion" || (category == "Elite" && c == 1)));
                    }
                }
                Assert.That(ExecuteCountEvaluator.Evaluate(1,"Jean_SkillEnergy()",owner), Is.EqualTo(1));
                using (BattleSpecialRules.BeginSkill(env.Enemy.Entity))
                {
                    Assert.That(ExecuteCountEvaluator.Evaluate(1,"Jean_SkillEnergy()",owner), Is.Zero);
                    Assert.That(AirborneTargetsContain("Jean_HeavyAirborne()",env,owner,target), Is.False);
                }
                target.CurrentHP = 0;
                env.DataManager.EnemyMainDict[BattleFlowTestEnv.EnemyID].EnemyType = "Minion";
                Assert.That(AirborneTargetsContain("Jean_SkillAirborne()",env,owner,target), Is.False);
                Assert.That(ExecuteCountEvaluator.Evaluate(1,"Jean_SkillEnergy()",owner), Is.EqualTo(1), "致死命中仍产能");
            }
            Assert.That(ExecuteCountEvaluator.Evaluate(1,"Jean_SkillEnergy()",owner), Is.Zero);
        }

        [UnityTest]
        public IEnumerator Airborne_TargetOverrideFromRealSkill_DoesNotExpandActualHits()
            => HookTestEnv.AwaitTask(AirborneOverrideAsync);
        private async Task AirborneOverrideAsync()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureStatus(AirborneStatusHandler.EnemyStatusID);
            env.ConfigureAllyAction(1,"SK_Heavy_Jean","SE_Heavy_Jean","Physical","0.1*TotalATK,0",apCost:0);
            env.DataManager.SkillEffectDict["SE_Heavy_JeanAirborne"] = new SkillEffectData
            {
                SkillEffectID = 10130202, SkillEffectID2 = "SE_Heavy_JeanAirborne", EffectIndex = 1,
                EffectType = "ApplyStatus", Param1 = AirborneStatusHandler.EnemyStatusID,
                Duration = 1, AddInPhase = 2, TargetType = "Enemy", TargetOverride = "Jean_HeavyAirborne()"
            };
            env.DataManager.EnemyMainDict[BattleFlowTestEnv.EnemyID].EnemyType = "Minion";
            env.CreateMinimalBattle(); Jean(env);
            var unselected = env.CreateEnemy(2).Entity;
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager,TurnPhase.AllyAction);
            env.Ally.ForcedTargetPositions.Add(1);
            await BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_Heavy_Jean",1,true));
            Assert.That(env.Enemy.Entity.HasStatus(AirborneStatusHandler.EnemyStatusID), Is.True);
            Assert.That(unselected.HasStatus(AirborneStatusHandler.EnemyStatusID), Is.False);
            Assert.That(unselected.CurrentHP, Is.EqualTo(1000f));
            Assert.That(random.FloatCallCount, Is.EqualTo(1), "选中目标的一次物理hit只进行一次最终暴击判定");
            Assert.That(random.IntCallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Field_RealEnemyMultihitCompletesBeforeOneResponse_AndMissesDoNotRespond()
            => HookTestEnv.AwaitTask(FieldAsync);
        private async Task FieldAsync()
        {
            using var env = new BattleFlowTestEnv(); env.ConfigureEnemyDamageSkill(.1f);
            var p = env.DataManager.EnemySkillParamDict["SE_Enemy_BattleFlow"]; p.Hits2 = ".1,0"; p.Hits3 = ".1,0";
            env.CreateMinimalBattle(); var owner = Jean(env);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            var main = Main(env,4101301,JeanScriptHooks.FieldID); ProbeAction(env,main,JeanScriptHooks.FieldAttackHook);
            owner.AddStatus(main.StatusID2,owner,3,2,0,main);
            var sequence = new List<string>(); long actionID = 0; int hits = 0;
            using var start = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted,(h,a) =>
            { actionID = h.ActionExecutionId; sequence.Add("start"); },0);
            using var damage = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,(h,d) =>
            {
                if (d.Source.SourceKind != ReactionSourceKind.EnemySkill) return;
                Assert.That(h.ActionExecutionId, Is.EqualTo(actionID).And.Not.Zero);
                Assert.That(owner.CurrentEnergy, Is.Zero, "整次行动完成前不反击"); hits++; sequence.Add("hit");
            },0);
            using var complete = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted,(h,a) =>
            { Assert.That(h.ActionExecutionId,Is.EqualTo(actionID)); sequence.Add("complete"); },0);
            await BattleExecution.Run(env.Enemy.TakeEnemyActionAsync);
            Assert.That(hits,Is.EqualTo(3)); Assert.That(sequence,Is.EqualTo(new[]{"start","hit","hit","hit","complete"}));
            Assert.That(owner.CurrentEnergy,Is.EqualTo(1f));
            Assert.That(random.IntCallCount,Is.EqualTo(1), "单目标无选取随机，只有AI选技能一次");
            Assert.That(random.FloatCallCount,Is.Zero);
            start.Dispose(); damage.Dispose(); complete.Dispose();
            foreach (var kind in new[] { ReactionSourceKind.EnemySkill, ReactionSourceKind.StatusEffect, ReactionSourceKind.DerivedReaction })
                await BattleExecution.Run(async () =>
                {
                    using (BattleEventHub.BeginActionExecution())
                    {
                        HitFact(env.Enemy.Entity,owner,"Probe","ProbeDamage",kind,kind != ReactionSourceKind.EnemySkill);
                        await BattleExecution.Flush();
                        BattleEventHub.Publish(new ActionEventData(env.Enemy.Entity,"Probe","EnemySkill",true));
                        await BattleExecution.Flush();
                    }
                });
            Assert.That(owner.CurrentEnergy,Is.EqualTo(1f), "未命中、状态与反应不能制造领域反击");
        }

        [UnityTest]
        public IEnumerator FieldDamage_UsesTheActualCompletedAttacker_NotAnotherEnemyWithTheSameID()
            => HookTestEnv.AwaitTask(FieldDamageAsync);
        private async Task FieldDamageAsync()
        {
            using var env=new BattleFlowTestEnv();env.ConfigureEnemyDamageSkill(.1f);env.CreateMinimalBattle();
            var owner=Jean(env);var other=env.CreateEnemy(2);
            var field=Main(env,4101301,JeanScriptHooks.FieldID);
            env.DataManager.StatusActionDict[field.StatusID2]=new List<StatusActionData>
            { new StatusActionData {StatusID2=field.StatusID2,ActionType="OnTrigger",ScriptHook=JeanScriptHooks.FieldAttackHook,Param1="STE_Jean_FieldDamage"} };
            env.DataManager.StatusEffectDict["STE_Jean_FieldDamage"]=new StatusEffectData
            { StatusEffectID=410130101,StatusEffectID2="STE_Jean_FieldDamage",EffectType="Damage",Element="Anemo",DamageType="Burst",
                Param1="0.784*TotalATK,2",TargetType="Enemy",TargetOverride="Jean_FieldAttacker()" };
            owner.AddStatus(field.StatusID2,owner,3,2,0,field);
            var random=new SequenceBattleRandomSource(new[]{.9f});BattleRandom.SetSource(random);
            int reflected=0;float otherHP=other.Entity.CurrentHP;
            using var listener=BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,(_,hit)=>
            {
                if(hit.Source.SourceEffectID!="STE_Jean_FieldDamage")return;
                reflected++;Assert.That(hit.Subject,Is.SameAs(env.Enemy.Entity));Assert.That(hit.Element,Is.EqualTo("Anemo"));
                Assert.That(hit.DamageType,Is.EqualTo("Burst"));Assert.That(hit.Source.SourceKind,Is.EqualTo(ReactionSourceKind.StatusEffect));
                Assert.That(hit.Source.SourceEntity,Is.SameAs(owner));Assert.That(hit.ElementAmount,Is.EqualTo(2f));
            },0);
            await BattleExecution.Run(env.Enemy.TakeEnemyActionAsync);
            Assert.That(reflected,Is.EqualTo(1));Assert.That(other.Entity.CurrentHP,Is.EqualTo(otherHP));
            Assert.That(random.IntCallCount,Is.EqualTo(1));Assert.That(random.FloatCallCount,Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator C2_UsesRecordedFrontFieldAndActualPositiveEnergy_NotCurrentLayout()
            => HookTestEnv.AwaitTask(C2Async);
        private async Task C2Async()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); var owner = Jean(env,2);
            var main = Main(env,4101304,JeanScriptHooks.EnergyListenerID); ProbeAction(env,main,JeanScriptHooks.EnergyHook);
            // 不用回能探针，避免探针自身获能引出再次响应。
            env.DataManager.StatusEffectDict["STE_Jean_Probe"] = new StatusEffectData
            { StatusEffectID = 410130401, StatusEffectID2 = "STE_Jean_Probe", EffectType = "ApplyStatus", Param1 = "ST_Jean_C2Buff", Duration = 4, TargetType = "Allies", TargetSelect = "-1" };
            Main(env,4101305,"ST_Jean_C2Buff","Buff");
            owner.AddStatus(main.StatusID2,owner,0,2,0,main);
            var fact = new ResourceEventData("Energy","Energy",owner,0,1,1,ResourceChangeReason.Gain,false);
            env.Ally.IsActive = false;
            await BattleExecution.Run(async () => { BattleEventHub.Publish(fact); await BattleExecution.Flush(); });
            var first = owner.GetStatus("ST_Jean_C2Buff"); Assert.That(first,Is.Not.Null);
            first.RemainingPhaseCount = 1;
            await BattleExecution.Run(async () =>
            {
                BattleEventHub.Publish(new ResourceEventData("Energy","Energy",owner,1,2,1,ResourceChangeReason.Gain,false));
                BattleEventHub.Publish(new ResourceEventData("Energy","Energy",owner,80,80,1,ResourceChangeReason.Gain,false));
                await BattleExecution.Flush();
            });
            Assert.That(owner.GetStatus("ST_Jean_C2Buff"),Is.SameAs(first));
            env.Ally.IsActive = true;
            await BattleExecution.Run(async () => { BattleEventHub.Publish(new ResourceEventData("Energy","Energy",owner,2,3,1,ResourceChangeReason.Gain,false)); await BattleExecution.Flush(); });
            Assert.That(owner.GetStatusInstances("ST_Jean_C2Buff"),Has.Count.EqualTo(1));
            Assert.That(owner.GetStatus("ST_Jean_C2Buff").RemainingPhaseCount,Is.EqualTo(4));
            Assert.That(first.IsActive,Is.False);
        }

        private static StatusInstance Residual(BattleFlowTestEnv env, BattleEntity owner)
        {
            var buff = Main(env,4101307,"ST_Jean_C6Buff","Buff"); buff.MultiplierPart = "DMGTaken"; buff.MaxCount=1; buff.MaxStack=0;
            env.DataManager.StatusEffectDict["STE_Jean_C6Reduction"] = new StatusEffectData
            { StatusEffectID=410130701,StatusEffectID2="STE_Jean_C6Reduction",EffectType="Buff",Param1="-0.35" };
            var count = Main(env,4101308,JeanC6ResidualCount.ID,"Count");
            env.DataManager.StatusEffectDict["STE_Jean_ResidualBind"] = new StatusEffectData
            { StatusEffectID=410130801,StatusEffectID2="STE_Jean_ResidualBind",EffectType="BindStatus",Param1=buff.StatusID2,TargetType="AllyTeam" };
            return owner.AddStatus(count.StatusID2,owner,2,2,0,count);
        }

        [UnityTest]
        public IEnumerator C6_OneTeamBudget_ThirdWholeMultihitActionStillReduced_ThenBoundBuffRemoved()
            => HookTestEnv.AwaitTask(C6Async);
        private async Task C6Async()
        {
            using var env = new BattleFlowTestEnv(); env.ConfigureEnemyDamageSkill(.1f);
            env.DataManager.EnemySkillMainDict[990201].UsePerTurn=0;
            env.DataManager.EnemySkillParamDict["SE_Enemy_BattleFlow"].Hits2=".1,0";
            env.DataManager.EnemySkillEffectDict["SE_Enemy_BattleFlow"].TargetNumber=-1;
            env.CreateMinimalBattle(); var owner=Jean(env); var count=Residual(env,owner);
            var member=env.CreateAlly(2);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            Assert.That(env.Manager.AllyTeam.GetStatusInstances("ST_Jean_C6Buff"),Has.Count.EqualTo(1));
            Assert.That(member.Entity.HasStatus(JeanC6ResidualCount.ID),Is.False);
            int hits=0;
            using var hit=BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,(_,d)=>
            {
                if(d.Source.SourceKind!=ReactionSourceKind.EnemySkill)return;
                Assert.That(count.IsActive,Is.True); Assert.That(count.RuntimeIntValue,Is.EqualTo(3-hits/4)); hits++;
                Assert.That(member.Entity.PrepareDMGTaken("x","x","Normal","Physical").GetMultiplier(ReactionType.None),Is.EqualTo(.65f).Within(.00001f));
            },0);
            for(int i=0;i<3;i++) await BattleExecution.Run(env.Enemy.TakeEnemyActionAsync);
            Assert.That(hits,Is.EqualTo(12)); Assert.That(count.RuntimeIntValue,Is.Zero); Assert.That(count.IsActive,Is.False);
            Assert.That(random.IntCallCount,Is.EqualTo(3)); Assert.That(random.FloatCallCount,Is.Zero);
            Assert.That(env.Manager.AllyTeam.HasStatus("ST_Jean_C6Buff"),Is.False);
            Assert.That(owner.PrepareDMGTaken("x","x","Normal","Physical").GetMultiplier(ReactionType.None),Is.EqualTo(1f));
        }

        [Test]
        public void C6_NewFieldRemovesOldResidualAndItsSpecificTeamBinding()
        {
            using var env=new BattleFlowTestEnv();env.CreateMinimalBattle();var owner=Jean(env);var count=Residual(env,owner);
            var field=Main(env,4101301,JeanScriptHooks.FieldID);
            owner.AddStatus(field.StatusID2,owner,3,2,0,field);
            Assert.That(count.IsActive,Is.False); Assert.That(env.Manager.AllyTeam.HasStatus("ST_Jean_C6Buff"),Is.False);
        }

        [Test]
        public void C6_TwoRoundExpiryClearsTeamBuffEvenWithUnusedCharges()
        {
            using var env=new BattleFlowTestEnv(); env.CreateMinimalBattle(); var owner=Jean(env);
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager,TurnPhase.AllyAction);
            var count=Residual(env,owner);
            for(int i=0;i<2;i++)
            {
                env.Manager.EndAllyTurn();
                env.Flow.AdvanceToPhase(env.Manager,TurnPhase.AllyPostTurn);
                env.Flow.AdvanceToPhase(env.Manager,TurnPhase.AllyAction);
            }
            Assert.That(count.RuntimeIntValue,Is.EqualTo(3)); Assert.That(count.IsActive,Is.False);
            Assert.That(env.Manager.AllyTeam.HasStatus("ST_Jean_C6Buff"),Is.False);
        }

        [UnityTest]
        public IEnumerator C6_NoDamageActionAndAllMissesDoNotConsume()
            => HookTestEnv.AwaitTask(C6MissesAsync);
        private async Task C6MissesAsync()
        {
            using var env=new BattleFlowTestEnv(); env.ConfigureEnemyDamageSkill();
            env.DataManager.EnemySkillEffectDict.Clear(); // 等同无伤害动作，不另造攻击行动类型。
            env.CreateMinimalBattle();var owner=Jean(env);var count=Residual(env,owner);
            BattleRandom.SetSource(new SequenceBattleRandomSource());
            await BattleExecution.Run(env.Enemy.TakeEnemyActionAsync);
            Assert.That(count.RuntimeIntValue,Is.EqualTo(3));
            await BattleExecution.Run(async () =>
            {
                using(BattleEventHub.BeginActionExecution())
                {
                    HitFact(env.Enemy.Entity,owner,"Miss","MissDamage",ReactionSourceKind.EnemySkill,false);
                    await BattleExecution.Flush();
                    BattleEventHub.Publish(new ActionEventData(env.Enemy.Entity,"Miss","EnemySkill",true));
                    await BattleExecution.Flush();
                }
            });
            Assert.That(count.RuntimeIntValue,Is.EqualTo(3));
        }
    }
}
