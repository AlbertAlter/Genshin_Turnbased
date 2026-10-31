using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class KeqingScriptHooksTests
    {
        private const string S = "ST_Keqing_", F = "STE_Keqing_";
        private static StatusMainData Main(BattleFlowTestEnv e, int n, string key, string type = "", string part = "")
        {
            var main = new StatusMainData { StatusID = 4102900 + n, StatusID2 = S + key, StatusType = type,
                MultiplierPart = part, MaxCount = 1, MaxStack = 1, WhenMax = "0" };
            e.DataManager.StatusMainDict[main.StatusID2] = main;
            if (!e.DataManager.CharacterStatusIDs.TryGetValue(1029, out var ids)) e.DataManager.CharacterStatusIDs[1029] = ids = new HashSet<string>();
            ids.Add(main.StatusID2); return main;
        }
        private static StatusEffectData Fx(BattleFlowTestEnv e, string key, int n, string name, string type, string value = null, string target = "Self")
        {
            var effect = new StatusEffectData { StatusEffectID = e.DataManager.StatusMainDict[S + key].StatusID * 100 + n,
                StatusEffectID2 = F + name, EffectIndex = n, EffectType = type, Param1 = value, TargetType = target };
            e.DataManager.StatusEffectDict[effect.StatusEffectID2] = effect; return effect;
        }
        private static void Action(BattleFlowTestEnv e, string key, string hook, string effects)
        {
            if (!e.DataManager.StatusActionDict.TryGetValue(S + key, out var rows)) e.DataManager.StatusActionDict[S + key] = rows = new List<StatusActionData>();
            rows.Add(new StatusActionData { StatusID = e.DataManager.StatusMainDict[S + key].StatusID, StatusID2 = S + key,
                ActionType = "OnTrigger", ScriptHook = hook, Param1 = effects, MaxTimePerLife = 1 });
        }
        private static void Setup(BattleFlowTestEnv e)
        {
            e.DataManager.CharacterAttributesDict[BattleFlowTestEnv.AllyID].Element = "Electro";
            e.ConfigureAllyAction(0, "SK_Normal_Keqing", "SE_Normal_Keqing", "Physical", "1*TotalATK,0", 0);
            e.ConfigureAllyAction(1, "SK_Heavy_Keqing", "SE_Heavy_Keqing", "Physical", "1*TotalATK,0", 0);
            e.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_1"][1].Hits2 = "1*TotalATK,0";
            e.DataManager.SkillEffectDict["SE_Heavy_Keqing"].Param2 = "Splash(0.5;1,1)";
            e.ConfigureAllyAction(2, "SK_Skill_Keqing1", "SE_Skill_Keqing1Mark", "", null, 20, 2, "ApplyStatus", S + "Stiletto", 2, 2, targetType: "EnemyField");
            e.ConfigureAllyAction(2, "SK_Skill_Keqing2", "SE_Skill_Keqing2Damage", "Electro", "1*TotalATK,2", 10);
            e.DataManager.CharacterAttributesDict[BattleFlowTestEnv.AllyID].ElementalSkillID = "SK_Skill_Keqing1";
            e.DataManager.SkillEffectDict["SE_Skill_Keqing2Damage"].TargetOverride = "Keqing_Area()";
            e.DataManager.SkillEffectDict["SE_Skill_Keqing2Clear"] = new SkillEffectData { SkillEffectID = 10290502,
                SkillEffectID2 = "SE_Skill_Keqing2Clear", EffectIndex = 1, EffectType = "RemoveStatus", Param1 = S + "StilettoControl",
                TargetType = "Self", TargetConsecutive = 1, TargetConsecutiveSet = true };
            Main(e, 1, "Stiletto"); Main(e, 2, "StilettoControl"); Main(e, 3, "Enchant", "Enchant");
            Main(e, 7, "C1"); Main(e, 8, "C2"); Main(e, 14, "C6Clear", "Buff", "DMGBonus").ApplyElementType = "Electro";
            var slash = Fx(e, "Stiletto", 1, "RecastDamage", "Damage", "1*TotalATK,2", "Enemy");
            slash.Element = "Electro"; slash.DamageType = "Skill"; slash.TargetOverride = "Keqing_Area()";
            var thunder = Fx(e, "Stiletto", 2, "Thunderclap", "Damage", "1*TotalATK,2", "Enemy");
            thunder.Element = "Electro"; thunder.DamageType = "Skill"; thunder.TargetOverride = "Keqing_Center()"; thunder.ExecuteCount = 2;
            Fx(e, "Stiletto", 3, "Clear", "RemoveStatus", S + "Stiletto", "EnemyField").TargetOverride = "Keqing_Center()";
            var energy = Fx(e, "Stiletto", 4, "Energy", "GainEnergy", "6", "Allies");
            energy.Element = "Electro"; energy.Param2 = "Based"; energy.TargetSelect = "-1"; energy.ScriptHook = "Keqing_ActiveClear()";
            var enchant = Fx(e, "Stiletto", 5, "Enchant", "ApplyStatus", S + "Enchant", "Allies");
            enchant.Element = "Electro"; enchant.Param2 = "1"; enchant.Param3 = "0"; enchant.Duration = 2; enchant.AddInPhase = 2;
            enchant.TargetOverride = "Keqing_Owner()"; enchant.ScriptHook = "Keqing_ActiveClear()";
            Fx(e, "Stiletto", 7, "ControlBind", "BindStatus", S + "StilettoControl", "Allies").TargetOverride = "Keqing_Owner()";
            var control = Fx(e, "StilettoControl", 1, "Control", "ChangeControl", "Skill"); control.Param2 = "SK_Skill_Keqing2"; control.Param3 = "1";
            var cost = Fx(e, "C1", 1, "Cost", "APCost", "-10"); cost.Param2 = "Skill"; cost.ScriptHook = "Keqing_FirstSkill()";
            var c2 = Fx(e, "C2", 1, "C2Energy", "GainEnergy", "2", "Allies"); c2.Param2 = "Based"; c2.Element = "Electro"; c2.TargetSelect = "-1";
            Action(e, "Stiletto", "Keqing_Manual()", F + "Clear");
            Action(e, "Stiletto", "Keqing_Switch()", F + "RecastDamage;" + F + "Clear");
            Action(e, "Stiletto", "Keqing_Heavy()", F + "Thunderclap;" + F + "Clear");
            e.DataManager.StatusActionDict[S + "Stiletto"].Add(new StatusActionData { StatusID = 4102901, StatusID2 = S + "Stiletto",
                ActionType = "OnDestroy", ScriptHook = "Keqing_ActiveClear()", Param1 = F + "Energy;" + F + "Enchant", MaxTimePerLife = 1 });
            e.DataManager.StatusActionDict[S + "C2"] = new List<StatusActionData> { new StatusActionData { StatusID = 4102908, StatusID2 = S + "C2",
                ActionType = "OnTrigger", OutgoingHit = "Normal;Heavy", ScriptHook = "Keqing_C2Hit();Chance(0.5)", Param1 = F + "C2Energy", MaxTimePerTurn = 1 } };
            e.CreateMinimalBattle(); e.Ally.Entity.EntityID = 1029; e.CreateAlly(2); e.CreateEnemy(2, 10000); e.CreateEnemy(3, 10000);
            e.Enemy.Entity.CurrentHP = e.Enemy.Entity.TotalHP = 10000;
            e.Manager.StartBattle(); e.Flow.AdvanceToPhase(e.Manager, TurnPhase.AllyAction);
            e.Ally.Entity.TotalATK = 100; e.Ally.Entity.CritRate = e.Ally.Entity.CritDMG = 0;
            foreach (var ally in e.Manager.Allies) { ally.Entity.MaxEnergy = 100; ally.Entity.CurrentEnergy = 0; ally.Entity.TotalRechargeRate = 1; }
            BattleRandom.SetSource(new SequenceBattleRandomSource());
        }
        private static StatusInstance Mark(BattleFlowTestEnv e, int position = 2)
            => e.Manager.Field.GetSlot(BattleSide.Enemy, position).AddStatus(S + "Stiletto", e.Ally.Entity, 2, 2, 0, e.DataManager.StatusMainDict[S + "Stiletto"]);

        [Test] public void C1_QueriesActualSkillIdentity_AndFirstStageDoesNotIncludeSecondStageEffects()
        {
            using var e = new BattleFlowTestEnv(); Setup(e);
            e.Ally.Entity.AddStatus(S + "C1", e.Ally.Entity, 0, 2, 0, e.DataManager.StatusMainDict[S + "C1"]);
            Assert.That(APCostResolver.ForSkill(e.Ally.Entity, new SkillMainData { SkillID2 = "SK_Skill_Keqing1", APCost = 20 }, 2), Is.EqualTo(10));
            Assert.That(APCostResolver.ForSkill(e.Ally.Entity, new SkillMainData { SkillID2 = "SK_Skill_Keqing2", APCost = 10 }, 2), Is.EqualTo(10));
            Assert.That(e.Ally.GetSkillEffects("SK_Skill_Keqing1").Select(x => x.SkillEffectID2), Is.EqualTo(new[] { "SE_Skill_Keqing1Mark" }));
        }
        [UnityTest] public IEnumerator ManualRecast_DamagesBeforeClear_ThenGrantsPartyEnergyAndEnchant_AndKeepsSelfConfirmation() => HookTestEnv.AwaitTask(Manual);
        private async Task Manual()
        {
            using var e = new BattleFlowTestEnv(); Setup(e); var root = Mark(e); var events = new List<string>();
            Assert.That(e.Ally.GetFirstSelectEffect(2).SkillEffectID2, Is.EqualTo("SE_Skill_Keqing2Clear"));
            using var damage = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            {
                if (fact.Source.SourceEffectID != "SE_Skill_Keqing2Damage") return;
                Assert.That(root.IsActive, Is.True); Assert.That(e.Ally.Entity.HasStatus(S + "Enchant"), Is.False); events.Add("damage");
            }, 0);
            using var removed = BattleEventHub.Subscribe<StatusEventData>(BattleEventKind.StatusRemoved, (_, fact) => { if (fact.InstanceId == root.ApplyOrder) events.Add("removed"); }, 0);
            using var energy = BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged, (_, fact) =>
            { if (fact.Resource == "Energy" && fact.Subject == e.Ally.Entity)
                { Assert.That(root.IsActive, Is.False); Assert.That(root.BoundStatuses, Is.Empty); events.Add("energy"); } }, 0);
            await BattleExecution.Run(() => e.Ally.ExecuteSkillByIDAsync("SK_Skill_Keqing2", 2, true));
            Assert.That(events, Is.EqualTo(new[] { "damage", "damage", "damage", "removed", "energy" }));
            Assert.That(e.Ally.Entity.CurrentEnergy, Is.EqualTo(9).Within(.001)); Assert.That(e.Manager.Allies[1].Entity.CurrentEnergy, Is.GreaterThan(0));
            Assert.That(e.Ally.Entity.HasStatus(S + "Enchant"), Is.True); Assert.That(e.Ally.GetBoundSkillID(2), Is.EqualTo("SK_Skill_Keqing1"));
        }
        [UnityTest] public IEnumerator SwitchRecast_IsFreeAndAutomatic_AndNaturalRemovalHasNoRewards() => HookTestEnv.AwaitTask(Switch);
        private async Task Switch()
        {
            using var e = new BattleFlowTestEnv(); Setup(e); var expired = Mark(e);
            StatusHostAccess.Remove(expired.Host, expired, "Expired");
            Assert.That(e.Ally.Entity.CurrentEnergy, Is.Zero); Assert.That(e.Ally.Entity.HasStatus(S + "Enchant"), Is.False);
            await BattleExecution.Run(async () =>
            {
                var ordinary = Mark(e); StatusHostAccess.Remove(ordinary.Host, ordinary); await BattleExecution.Flush();
                var replaced = Mark(e); Mark(e); await BattleExecution.Flush();
                Assert.That(replaced.IsActive, Is.False);
                Assert.That(e.Ally.Entity.CurrentEnergy, Is.Zero);
                Assert.That(e.Ally.Entity.HasStatus(S + "Enchant"), Is.False);
            });
            var root = Mark(e); int hits = 0, actions = 0; int ap = e.Manager.APManager.CurrentAP;
            using var damage = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) => { if (fact.Source.SourceEffectID == F + "RecastDamage") hits++; }, 0);
            using var action = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted, (_, fact) => actions++, 0);
            await BattleExecution.Run(() => { SwitchInEffect.Execute(new[] { e.Manager.Allies[1].Entity }); return Task.CompletedTask; });
            await BattleExecution.Run(() => { Assert.That(SwitchInEffect.Execute(new[] { e.Ally.Entity }), Is.True); return Task.CompletedTask; });
            Assert.That(hits, Is.EqualTo(3)); Assert.That(actions, Is.Zero); Assert.That(e.Manager.APManager.CurrentAP, Is.EqualTo(ap));
            Assert.That(root.IsActive, Is.False); Assert.That(e.Ally.Entity.CurrentEnergy, Is.EqualTo(9).Within(.001));
        }
        [UnityTest] public IEnumerator HeavySplash_DetonatesOnlyOwnersMark_AndLaterHitReadsNewEnchant() => HookTestEnv.AwaitTask(Heavy);
        private async Task Heavy()
        {
            using var e = new BattleFlowTestEnv(); Setup(e); var root = Mark(e); var marked = e.Manager.Enemies[1].Entity;
            await BattleExecution.Run(async () =>
            {
                using (var batch = new HitBatch())
                using (BattleSpecialRules.BeginHit(batch.RuntimeHitID, e.Manager.Allies[1].Entity, new[] { marked }))
                    batch.Resolve(new HitRequest { Target = marked, Element = "Physical", DamageType = "Heavy", DamageBeforeShield = 1,
                        Source = DamageSourceInfo.Create(e.Manager.Allies[1].Entity, ReactionSourceKind.CharacterSkill, "other", "other-heavy") });
                await BattleExecution.Flush();
            });
            Assert.That(root.IsActive, Is.True);
            var elements = new List<string>(); int thunder = 0;
            using var damage = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            {
                if (fact.Source.SourceEffectID == "SE_Heavy_Keqing" && fact.Subject == e.Enemy.Entity) elements.Add(fact.Element);
                if (fact.Source.SourceEffectID == F + "Thunderclap") { thunder++; Assert.That(root.IsActive, Is.True); }
            }, 0);
            e.Ally.ForcedTargetPositions.Add(1);
            await BattleExecution.Run(() => e.Ally.ExecuteSkillByIDAsync("SK_Heavy_Keqing", 1, true));
            Assert.That(elements, Is.EqualTo(new[] { "Physical", "Electro" })); Assert.That(thunder, Is.EqualTo(2));
            Assert.That(root.IsActive, Is.False); Assert.That(e.Ally.Entity.CurrentEnergy, Is.EqualTo(9).Within(.001));
        }
        [UnityTest] public IEnumerator C2_RollsPerHitPerEnemy_AndStopsAfterFirstSuccessInTheTurn() => HookTestEnv.AwaitTask(SecondConstellation);
        private async Task SecondConstellation()
        {
            using var e = new BattleFlowTestEnv(); Setup(e);
            e.Ally.Entity.AddStatus(S + "C2", e.Ally.Entity, 0, 2, 0, e.DataManager.StatusMainDict[S + "C2"]);
            var targets = e.Manager.Enemies.Take(2).Select(x => x.Entity).ToArray();
            foreach (var target in targets) target.ApplyAura("Electro", 10, e.Ally.Entity.EntityID);
            var random = new SequenceBattleRandomSource(new[] { .8f, .2f }); BattleRandom.SetSource(random);
            async Task Hit()
            {
                using (var batch = new HitBatch())
                using (BattleSpecialRules.BeginHit(batch.RuntimeHitID, e.Ally.Entity, targets))
                    for (int i = 0; i < targets.Length; i++) batch.Resolve(new HitRequest { Target = targets[i], TargetIndex = i,
                        Element = "Physical", DamageType = "Normal", DamageBeforeShield = 1,
                        Source = DamageSourceInfo.Create(e.Ally.Entity, ReactionSourceKind.CharacterSkill, "normal", "normal-hit") });
                await BattleExecution.Flush();
            }
            await BattleExecution.Run(Hit); Assert.That(random.FloatCallCount, Is.EqualTo(2));
            Assert.That(e.Ally.Entity.CurrentEnergy, Is.EqualTo(3).Within(.001)); Assert.That(e.Manager.Allies[1].Entity.CurrentEnergy, Is.GreaterThan(0));
            await BattleExecution.Run(Hit); Assert.That(random.FloatCallCount, Is.EqualTo(2)); Assert.That(e.Ally.Entity.CurrentEnergy, Is.EqualTo(3).Within(.001));
        }
    }
}
