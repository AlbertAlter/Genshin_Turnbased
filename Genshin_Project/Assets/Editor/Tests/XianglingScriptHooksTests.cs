using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class XianglingScriptHooksTests
    {
        private const string S = "ST_Xiangling_", E = "STE_Xiangling_";
        private static StatusMainData Main(BattleFlowTestEnv e, int n, string key, string kind = "", string part = "")
        {
            var main = new StatusMainData { StatusID = 4102200+n, StatusID2 = S+key, StatusType = kind,
                MultiplierPart2 = part, MaxCount = 1, MaxStack = 1, WhenMax = "0" };
            e.DataManager.StatusMainDict[S+key] = main;
            if (!e.DataManager.CharacterStatusIDs.TryGetValue(1022, out var ids))
                e.DataManager.CharacterStatusIDs[1022] = ids = new HashSet<string>();
            ids.Add(S+key); return main;
        }
        private static StatusEffectData Effect(BattleFlowTestEnv e, int n, int index, string key, string kind,
            string param = null, string target = "Self")
        {
            var effect = new StatusEffectData { StatusEffectID = (4102200+n)*100+index,
                StatusEffectID2 = E+key, EffectIndex = index, EffectType = kind, Param1 = param, TargetType = target };
            e.DataManager.StatusEffectDict[E+key] = effect; return effect;
        }
        private static void Actions(BattleFlowTestEnv e, string key, params StatusActionData[] rows)
        {
            foreach (var row in rows) { row.StatusID2 = S+key; row.StatusID = e.DataManager.StatusMainDict[S+key].StatusID; }
            e.DataManager.StatusActionDict[S+key] = rows.ToList();
        }
        private static void Level(BattleFlowTestEnv e, string key, int type, string value)
        {
            e.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_{type}_{key}"] = new Dictionary<int, SkillLevelData>
            { [1] = new SkillLevelData { CharacterID = BattleFlowTestEnv.AllyID, SkillType = type, SkillLevel = 1,
                ParamID = key, Hits1 = value } };
        }
        private static void Setup(BattleFlowTestEnv e, int constellation = 0, bool t1 = true)
        {
            e.ConfigureAllyAction(0, "SK_Normal_Xiangling", "SE_Normal_Xiangling", "Physical", ".7104*TotalATK,0", 20);
            e.ConfigureAllyAction(1, "SK_Heavy_Xiangling", "SE_Heavy_Xiangling", "Physical", "1.2169*TotalATK,0", 30);
            e.ConfigureAllyAction(2, "SK_Skill_Xiangling", "SE_Skill_Xiangling", null, null, 30, 3,
                "ApplyStatus", S+"Guoba", 2, 2, 3, "Self");
            e.ConfigureAllyAction(3, "SK_Burst_Xiangling", "SE_Burst_Xiangling", "Pyro", ".72*TotalATK,1", 0, 5);
            e.DataManager.SkillEffectDict["SE_Burst_Xiangling"].TargetNumber = 5;
            e.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID].Last().EnergyUsed = 80;
            e.DataManager.SkillEffectDict["SE_Burst_XianglingState"] = new SkillEffectData
            { SkillEffectID2 = "SE_Burst_XianglingState", EffectIndex = 1, EffectType = "ApplyStatus", TargetType = "Self",
                Param1 = S+"Pyronado", Duration = 2, AddInPhase = 2 };
            Main(e, 1, "Guoba"); Main(e, 2, "Pyronado", "Count"); Main(e, 3, "Pepper");
            Main(e, 4, "PepperATK", "Buff", "ATKBonus"); Main(e, 5, "FifthNormal", "Count"); Main(e, 6, "Implode");
            Main(e, 7, "C6Pyro", "Buff", "DMGBonus").ApplyElementType = "Pyro";
            Main(e, 8, "GuobaRes", "Debuff", "ResBonus").ApplyElementType = "Pyro";
            var spray = Effect(e, 1, 1, "GuobaDamage", "Damage", target:"Enemy");
            spray.Element = "Pyro"; spray.DamageType = "Skill"; spray.TargetOverride = "Xiangling_GuobaTargets()";
            Level(e, E+"GuobaDamage", 2, "1.1128*TotalATK,1");
            var energy = Effect(e, 1, 2, "GuobaEnergy", "GainEnergy", "2");
            energy.Element = "Pyro"; energy.Param2 = "Based"; energy.ScriptHook = "Xiangling_GuobaEnergy()";
            energy.TargetType = "Allies"; energy.TargetSelect = "-1";
            var res = Effect(e, 1, 3, "GuobaRes", "ApplyStatus", S+"GuobaRes", "Enemy");
            res.Duration = 2; res.AddInPhase = 2; res.TargetOverride = "Xiangling_GuobaHitTarget()";
            var wheel = Effect(e, 2, 1, "PyronadoDamage", "Damage", target:"Enemy");
            wheel.Element = "Pyro"; wheel.DamageType = "Burst"; wheel.TargetSelect = "-1";
            Level(e, E+"PyronadoDamage", 3, "1.12*TotalATK,1");
            var consume = Effect(e, 2, 2, "PyronadoConsume", "Count", S+"Pyronado");
            consume.Param2 = "-1"; consume.ScriptHook = "Xiangling_Pyronado()";
            Effect(e,2,4,"PyronadoEnd","RemoveStatus",S+"Pyronado").ScriptHook="Xiangling_PyronadoEmpty()";
            Effect(e, 2, 3, "C6Bind", "BindStatus", S+"C6Pyro", "AllyTeam").ScriptHook = "Check(1022_C6)";
            var pepper = Effect(e, 3, 1, "PepperGrant", "ApplyStatus", S+"PepperATK"); pepper.Duration = 3; pepper.AddInPhase = 2;
            Effect(e, 4, 1, "PepperATK", "Buff", ".1");
            var mark = Effect(e, 5, 1, "ImplodeGrant", "ApplyStatus", S+"Implode", "Enemy");
            mark.Duration = 1; mark.AddInPhase = 6; mark.TargetOverride = "Xiangling_FifthTarget()";
            mark.ScriptHook="Xiangling_FifthReached()";
            Effect(e,5,2,"FifthGain","Count",S+"FifthNormal").Param2="1";
            Effect(e,5,3,"FifthReset","Count",S+"FifthNormal").Param2="clear";
            var explode = Effect(e, 6, 1, "ImplodeDamage", "Damage", ".75*TotalATK,1", "Enemy");
            explode.Element = "Pyro"; explode.TargetSelect = "1,1";
            Effect(e, 7, 1, "C6Pyro", "Buff", ".15*min(1,Count(ST_Xiangling_Pyronado))"); Effect(e, 8, 1, "GuobaResBonus", "Buff", "-.15");
            Actions(e, "Guoba", new StatusActionData { ActionType = "OnItsTurn", Param1 = E+"GuobaDamage;"+E+"GuobaEnergy" },
                new StatusActionData { ActionType = "OnTrigger", Param1 = E+"GuobaRes", OutgoingHit = "Skill",
                    ScriptHook = "Xiangling_GuobaHit();Check(1022_C1)" });
            Actions(e, "Pyronado", new StatusActionData { ActionType = "OnTrigger", Param1 = E+"PyronadoDamage;"+E+"PyronadoConsume;"+E+"PyronadoEnd",
                ScriptHook = "Xiangling_Pyronado()" });
            Actions(e, "Pepper", new StatusActionData { ActionType = "OnTrigger", Param1 = E+"PepperGrant", ScriptHook = "Xiangling_Pepper()" });
            Actions(e, "FifthNormal", new StatusActionData { ActionType = "OnItsTurn",Param1=E+"FifthReset" },
                new StatusActionData { ActionType = "OnTrigger", Param1 = E+"FifthGain;"+E+"ImplodeGrant", ScriptHook = "Xiangling_FifthNormal()" });
            Actions(e, "Implode", new StatusActionData { ActionType = "OnEnd", Param1 = E+"ImplodeDamage" });
            foreach (string key in new[] { "Guoba", "Pyronado" }) e.DataManager.StatusAttributesDict[S+key] =
                new StatusAttributesData { StatusID = e.DataManager.StatusMainDict[S+key].StatusID, StatusID2 = S+key };
            e.DataManager.TalentDict[1022] = new List<TalentData>
            { new TalentData { CharacterID = 1022, TalentIndex = 1, UnlockAfter = t1 ? 0 : 20 } };
            e.CreateMinimalBattle(); e.CreateAlly(2); e.CreateEnemy(2, 10000); e.CreateEnemy(3, 10000);
            e.Ally.Entity.EntityID = 1022; e.Ally.Entity.ConstellationLevel = constellation;
            e.Enemy.Entity.CurrentHP = e.Enemy.Entity.TotalHP = 10000;
            e.Manager.StartBattle(); e.Flow.AdvanceToPhase(e.Manager, TurnPhase.AllyAction);
            var owner = e.Ally.Entity; owner.TotalATK = 100; owner.CritRate = owner.CritDMG = 0;
            owner.MaxEnergy = 80; owner.CurrentEnergy = 80; BattleRandom.SetSource(new SequenceBattleRandomSource());
        }
        private static StatusInstance Add(BattleFlowTestEnv e, string key, int duration = 0, int phase = 2, int trigger = 0)
            => e.Ally.Entity.AddStatus(S+key, e.Ally.Entity, duration, phase, trigger, e.DataManager.StatusMainDict[S+key]);
        private static Task Cast(BattleFlowTestEnv e, string kind, int type, CharacterBattleController actor = null)
        {
            e.Manager.APManager.ResetAP();
            return BattleExecution.Run(() => (actor ?? e.Ally).ExecuteSkillByIDAsync("SK_"+kind+"_Xiangling", type, true));
        }
        private static Task Spray(BattleFlowTestEnv e, StatusInstance status)
            => BattleExecution.Run(() => e.Ally.ExecuteStatusActionsTask(status, e.DataManager.StatusActionDict[S+"Guoba"], status.Host, "OnItsTurn"));

        [UnityTest]
        public IEnumerator Guoba_TwoTargets_OneEnergyPayment_C1OnlyHitTargets_SeedRepeats()
            => HookTestEnv.AwaitTask(GuobaAsync);
        private async Task GuobaAsync()
        {
            using var e = new BattleFlowTestEnv(); Setup(e, 1); await Cast(e, "Skill", 2);
            var root = e.Ally.Entity.GetStatus(S+"Guoba"); Assert.That(root.FormulaSnapshot, Is.Not.Null);
            int payments = 0; var targets = new List<int>();
            using var energy = BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged, (_, fact) =>
            { if (fact.Resource == "Energy" && fact.Subject == e.Ally.Entity && fact.Reason == ResourceChangeReason.Gain) payments++; }, 0);
            using var damage = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            { if (fact.Source.SourceEffectID == E+"GuobaDamage") targets.Add(fact.Subject.SlotPosition); }, 0);
            for (int i = 0; i < 2; i++)
            {
                e.Ally.Entity.CurrentEnergy = 0;
                e.Manager.Allies[1].Entity.CurrentEnergy = 0;
                var random = new SequenceBattleRandomSource(intValues:new[] { 2, 1 }); BattleRandom.SetSource(random);
                await Spray(e, root);
                Assert.That(e.Ally.Entity.CurrentEnergy, Is.EqualTo(3f).Within(.001f), "2 Based×同元素前台1.5，不能按两个敌人回两份");
                Assert.That(e.Manager.Allies[1].Entity.CurrentEnergy, Is.EqualTo(2.4f).Within(.001f), "双人队同元素后台2×0.8×1.5");
                Assert.That(random.IntCallCount, Is.EqualTo(2)); Assert.That(random.FloatCallCount, Is.EqualTo(2));
                Assert.That(e.Manager.Enemies[0].Entity.HasStatus(S+"GuobaRes"), Is.False);
                Assert.That(e.Manager.Enemies[1].Entity.GetStatusInstances(S+"GuobaRes").Count, Is.EqualTo(1));
                Assert.That(e.Manager.Enemies[2].Entity.GetStatusInstances(S+"GuobaRes").Count, Is.EqualTo(1));
            }
            Assert.That(payments, Is.EqualTo(2)); Assert.That(targets, Is.EqualTo(new[] { 3, 2, 3, 2 }));
        }

        [UnityTest]
        public IEnumerator Guoba_LockedTalentOneTarget_EmptySprayNoEnergy()
            => HookTestEnv.AwaitTask(EmptyAsync);
        private async Task EmptyAsync()
        {
            using var e = new BattleFlowTestEnv(); Setup(e, t1:false); await Cast(e, "Skill", 2);
            var root = e.Ally.Entity.GetStatus(S+"Guoba"); var hits = new List<DamageEventData>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, f) => hits.Add(f), 0);
            await Spray(e, root); Assert.That(hits.Count, Is.EqualTo(1));
            foreach (var enemy in e.Manager.Enemies) enemy.Entity.CurrentHP = 0;
            e.Ally.Entity.CurrentEnergy = 0; hits.Clear(); await Spray(e, root);
            Assert.That(hits, Is.Empty); Assert.That(e.Ally.Entity.CurrentEnergy, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Pepper_ReplacementAndExpirationGrantOnce_DeathDoesNot()
            => HookTestEnv.AwaitTask(PepperAsync);
        private async Task PepperAsync()
        {
            using var e = new BattleFlowTestEnv(); Setup(e); var owner = e.Ally.Entity;
            int grants = 0; using var listener = BattleEventHub.Subscribe<StatusEventData>(BattleEventKind.StatusAdded, (_, f) =>
            { if (f.StatusId == S+"PepperATK") grants++; }, 0);
            await BattleExecution.Run(async () =>
            {
                Add(e, "Pepper"); var old = Add(e, "Guoba", 2, 2, 3); await BattleExecution.Flush();
                var current = Add(e, "Guoba", 2, 2, 3); await BattleExecution.Flush();
                Assert.That(old.IsActive, Is.False); Assert.That(grants, Is.EqualTo(1));
                Assert.That(owner.GetFinalATK("", "", -1, "Physical"), Is.EqualTo(110f).Within(.001f));
                owner.RemoveStatusInstance(current, "Expired"); await BattleExecution.Flush();
                Assert.That(grants, Is.EqualTo(2)); Assert.That(owner.GetStatusInstances(S+"PepperATK").Count, Is.EqualTo(1));
                Add(e, "Guoba", 2, 2, 3); owner.TakeDamage(100000, DamageSourceInfo.CreateUnknown()); await BattleExecution.Flush();
                Assert.That(grants, Is.EqualTo(2));
            });
        }

        [UnityTest]
        public IEnumerator Pyronado_CompletedNormalCountsOnce_AlliesQualify_HeavyNormalDamageDoesNot()
            => HookTestEnv.AwaitTask(WheelAsync);
        private async Task WheelAsync()
        {
            using var e = new BattleFlowTestEnv(); Setup(e);
            e.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_0"][1].Hits2 = ".2*TotalATK,0";
            await Cast(e, "Burst", 3); var root = e.Ally.Entity.GetStatus(S+"Pyronado");
            Assert.That(root.RuntimeIntValue, Is.EqualTo(6));
            var hits = new List<int>(); var aliveAtLastHit = new List<bool>();
            bool activeWhenZero=false;
            using var counts=BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged,(_,f)=>
            {if(f.PoolId==S+"Pyronado" && f.Reason==ResourceChangeReason.Spend && f.After==0)activeWhenZero=root.IsActive;},0);
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, f) =>
            { if (f.Source.SourceEffectID == E+"PyronadoDamage") { hits.Add(f.Subject.SlotPosition); aliveAtLastHit.Add(root.IsActive); } }, 0);
            e.DataManager.SkillEffectDict["SE_Heavy_Xiangling"].DamageType = "Normal";
            await Cast(e, "Heavy", 1); Assert.That(root.RuntimeIntValue, Is.EqualTo(6));
            await Cast(e, "Normal", 0); Assert.That(root.RuntimeIntValue, Is.EqualTo(5));
            await Cast(e, "Normal", 0, e.Manager.Allies[1]); Assert.That(root.RuntimeIntValue, Is.EqualTo(4));
            for (int i = 0; i < 4; i++) await Cast(e, "Normal", 0);
            Assert.That(hits.Count, Is.EqualTo(18)); Assert.That(aliveAtLastHit.All(v => v), Is.True);
            Assert.That(root.RuntimeIntValue, Is.Zero); Assert.That(root.IsActive, Is.False);
            Assert.That(activeWhenZero,Is.True,"扣至0时仍存在，由下一条配置的RemoveStatus结束。");
            await Cast(e, "Normal", 0); Assert.That(hits.Count, Is.EqualTo(18));
        }

        [UnityTest]
        public IEnumerator CountClear_RetainsPyronadoAndBinding_ButStopsDamageAndC6UntilExplicitRemove()
            => HookTestEnv.AwaitTask(CountClearAsync);
        private async Task CountClearAsync()
        {
            using var e=new BattleFlowTestEnv();Setup(e,6);var owner=e.Ally.Entity;
            await Cast(e,"Burst",3);var root=owner.GetStatus(S+"Pyronado");var child=root.BoundStatuses.Single();
            Assert.That(owner.GetStatusDMGBonus("","",0,"Pyro",e.Enemy.Entity),Is.EqualTo(.15f).Within(.0001));
            CountEffect.Execute(S+"Pyronado","clear",owner,new[]{owner});
            Assert.That(root.RuntimeIntValue,Is.Zero);Assert.That(root.IsActive,Is.True);Assert.That(child.IsActive,Is.True);
            Assert.That(owner.GetStatusDMGBonus("","",0,"Pyro",e.Enemy.Entity),Is.Zero);
            int wheels=0;using var damage=BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,(_,f)=>
            {if(f.Source.SourceEffectID==E+"PyronadoDamage")wheels++;},0);
            await Cast(e,"Normal",0);Assert.That(wheels,Is.Zero);Assert.That(root.IsActive,Is.True);
            await BattleExecution.Run(()=>e.Ally.ExecuteStatusActionsTask(root,
                new List<StatusActionData>{new StatusActionData{StatusID=root.MainData.StatusID,StatusID2=root.StatusID2,
                    ActionType="OnTrigger",Param1=E+"PyronadoEnd"}},owner,"OnTrigger"));
            Assert.That(root.IsActive,Is.False);Assert.That(child.IsActive,Is.False);
        }

        [UnityTest]
        public IEnumerator C4AndC6_SnapshotBeforeTeamBuff_LaterGuobaIncludesIt_ExpiryClearsBinding()
            => HookTestEnv.AwaitTask(SnapshotAsync);
        private async Task SnapshotAsync()
        {
            using var e = new BattleFlowTestEnv(); Setup(e, 6); var owner = e.Ally.Entity;
            await Cast(e, "Burst", 3); var root = owner.GetStatus(S+"Pyronado");
            Assert.That(root.RuntimeIntValue, Is.EqualTo(9)); Assert.That(root.RemainingPhaseCount, Is.EqualTo(3));
            var child = root.BoundStatuses.Single(); Assert.That(child.Host, Is.SameAs(e.Manager.AllyTeam));
            Assert.That(root.FormulaSnapshot.Attribute("TotalATK", E+"PyronadoDamage", "Pyro"), Is.EqualTo(100));
            await Cast(e, "Skill", 2); var guoba = owner.GetStatus(S+"Guoba"); owner.TotalATK = 500;
            var wheel = new List<float>(); var sprays = new List<float>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, f) =>
            { if (f.Source.SourceEffectID == E+"PyronadoDamage") wheel.Add(f.DamageBeforeShield);
              if (f.Source.SourceEffectID == E+"GuobaDamage") sprays.Add(f.DamageBeforeShield); }, 0);
            await Cast(e, "Normal", 0);
            Assert.That(wheel, Has.All.EqualTo(56f).Within(.001f), "当前旋火轮不吃自己的新六命，也不改读500攻击");
            await Spray(e, guoba); Assert.That(sprays, Has.All.EqualTo(63.986f).Within(.001f));
            int spends = 0; using var resources = BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged, (_, f) =>
            { if (f.PoolId == S+"Pyronado" && f.Reason == ResourceChangeReason.Spend) spends++; }, 0);
            owner.RemoveStatusInstance(root, "Expired"); await BattleExecution.Run(BattleExecution.Flush);
            Assert.That(root.RuntimeIntValue, Is.Zero); Assert.That(child.IsActive, Is.False); Assert.That(spends, Is.Zero);
        }

        [UnityTest]
        public IEnumerator C2_FifthFullActionOnly_ImplodesAroundMainTarget_UsesLiveAttack_ResetsNextTurn()
            => HookTestEnv.AwaitTask(ImplodeAsync);
        private async Task ImplodeAsync()
        {
            using var e = new BattleFlowTestEnv(); Setup(e, 2); var owner = e.Ally.Entity;
            var count = Add(e, "FifthNormal");
            e.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_0"][1].Hits2 = ".2*TotalATK,0";
            for (int i = 0; i < 6; i++)
            {
                e.Ally.ForcedTargetPositions.Add(2); await Cast(e, "Normal", 0);
                Assert.That(e.Manager.Enemies[1].Entity.HasStatus(S+"Implode"), Is.EqualTo(i >= 4));
            }
            Assert.That(count.RuntimeIntValue, Is.EqualTo(5));
            var mark = e.Manager.Enemies[1].Entity.GetStatus(S+"Implode");
            Assert.That(mark.FormulaSnapshot, Is.Null); Assert.That(mark.AddInPhase, Is.EqualTo(6));
            owner.TotalATK = 200; var explosions = new List<DamageEventData>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, f) =>
            { if (f.Source.SourceEffectID == E+"ImplodeDamage") explosions.Add(f); }, 0);
            e.Manager.EndAllyTurn(); e.Flow.AdvanceToPhase(e.Manager, TurnPhase.AllyAction);
            Assert.That(explosions.Select(f => f.Subject.SlotPosition), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(explosions.All(f => string.IsNullOrEmpty(f.DamageType)), Is.True);
            Assert.That(explosions.Select(f => f.DamageBeforeShield), Has.All.EqualTo(75f).Within(.001f));
            Assert.That(count.RuntimeIntValue, Is.Zero); Assert.That(mark.IsActive, Is.False);
            Assert.That(count.IsActive,Is.True,"回合开始clear只清计数，不删除大火宽油。");
            await Cast(e, "Normal", 0); Assert.That(count.RuntimeIntValue, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Guoba_TicksTwiceThenExpires_AndDeathClearsOffOwnerMarksAndTeamBuff()
            => HookTestEnv.AwaitTask(LifetimeAsync);
        private async Task LifetimeAsync()
        {
            using var e = new BattleFlowTestEnv(); Setup(e, 6); var owner = e.Ally.Entity;
            Add(e, "Pepper"); await Cast(e, "Skill", 2); var guoba = owner.GetStatus(S+"Guoba"); int sprays = 0;
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, f) =>
            { if (f.Source.SourceEffectID == E+"GuobaDamage") sprays++; }, 0);
            for (int i = 0; i < 2; i++) { e.Manager.EndAllyTurn(); e.Flow.AdvanceToPhase(e.Manager, TurnPhase.AllyAction); }
            Assert.That(sprays, Is.EqualTo(4)); Assert.That(guoba.IsActive, Is.False); Assert.That(owner.HasStatus(S+"PepperATK"), Is.True);
            await Cast(e, "Burst", 3); var wheel = owner.GetStatus(S+"Pyronado"); var child = wheel.BoundStatuses.Single();
            var count = Add(e, "FifthNormal");
            var mark = e.Enemy.Entity.AddStatus(S+"Implode", owner, 1, 6, 0, e.DataManager.StatusMainDict[S+"Implode"]);
            int explosions = 0; using var damage = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, f) =>
            { if (f.Source.SourceEffectID == E+"ImplodeDamage") explosions++; }, 0);
            await BattleExecution.Run(async () => { owner.TakeDamage(100000, DamageSourceInfo.CreateUnknown()); await BattleExecution.Flush(); });
            Assert.That(wheel.IsActive || child.IsActive || mark.IsActive || count.IsActive, Is.False);
            Assert.That(wheel.RuntimeIntValue, Is.Zero); Assert.That(explosions, Is.Zero);
        }
    }
}
