using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class NoelleScriptHooksTests
    {
        private const string S = "ST_Noelle_";
        private static StatusMainData Main(BattleFlowTestEnv e, int index, string key, string kind = "", string part = "")
        {
            var m = new StatusMainData { StatusID = 4102100 + index, StatusID2 = key, StatusType = kind,
                MultiplierPart2 = part, MaxStack = 1, MaxCount = 1, WhenMax = "0" };
            e.DataManager.StatusMainDict[key] = m;
            if (!e.DataManager.CharacterStatusIDs.TryGetValue(1021, out var ids))
                e.DataManager.CharacterStatusIDs[1021] = ids = new HashSet<string>();
            ids.Add(key); return m;
        }
        private static StatusEffectData Effect(BattleFlowTestEnv e, int parent, int index, string key, string kind, string param = null, string target = "Self")
        {
            var effect = new StatusEffectData { StatusEffectID = (4102100 + parent) * 100 + index,
                StatusEffectID2 = key, EffectIndex = index, EffectType = kind, Param1 = param, TargetType = target };
            e.DataManager.StatusEffectDict[key] = effect; return effect;
        }
        private static void Actions(BattleFlowTestEnv e, string status, params StatusActionData[] rows)
        {
            foreach (var row in rows) { row.StatusID2 = status; row.StatusID = e.DataManager.StatusMainDict[status].StatusID; }
            e.DataManager.StatusActionDict[status] = rows.ToList();
        }
        private static void Level(BattleFlowTestEnv e, string id, int type, string first, string fourth)
        {
            e.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_{type}_{id}"] = new Dictionary<int, SkillLevelData>
            { [1] = new SkillLevelData { CharacterID = BattleFlowTestEnv.AllyID, SkillType = type, SkillLevel = 1, ParamID = id, Hits1 = first },
              [4] = new SkillLevelData { CharacterID = BattleFlowTestEnv.AllyID, SkillType = type, SkillLevel = 4, ParamID = id, Hits1 = fourth } };
        }
        private static void Setup(BattleFlowTestEnv e, int constellation = 0)
        {
            e.ConfigureAllyAction(0, "SK_Normal_Noelle", "SE_Normal_Noelle", "Physical", "1.1343*TotalATK,0", 25);
            e.ConfigureAllyAction(1, "SK_Heavy_Noelle", "SE_Heavy_Noelle", "Physical", ".5074*TotalATK,0", 35);
            e.DataManager.SkillEffectDict["SE_Heavy_Noelle"].TargetNumber = 3;
            e.ConfigureAllyAction(2, "SK_Skill_Noelle", "SE_Skill_NoelleDamage", "Geo", "1.2*TotalDEF,2", 30, 6, param1: "M*TotalDEF");
            e.ConfigureAllyAction(3, "SK_Burst_Noelle", "SE_Burst_NoelleDamage", "Geo", ".672*TotalATK,1", 0, 3);
            e.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID].Last().EnergyUsed = 60;
            foreach (string name in new[] { "Skill", "Burst" })
            {
                e.DataManager.SkillEffectDict["SE_" + name + "_NoelleDamage"].TargetNumber = 5;
                e.DataManager.SkillEffectDict["SE_" + name + "_NoelleState"] = new SkillEffectData
                { SkillEffectID2 = "SE_" + name + "_NoelleState", EffectIndex = 2, EffectType = "ApplyStatus", TargetType = "Self",
                    Param1 = name == "Skill" ? NoelleScriptHooks.Breastplate : NoelleScriptHooks.SweepingTime, Duration = 3, AddInPhase = 2 };
            }
            foreach (var type in new[] { 0, 1 })
            {
                string name = type == 0 ? "SweepNormal" : "SweepHeavy";
                e.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID].Add(new SkillMainData
                { SkillID2 = "SK_" + name + "_Noelle", APCost = type == 0 ? 25 : 35, ActionType = type == 0 ? "Normal" : "Heavy" });
                e.DataManager.SkillEffectDict["SE_" + name + "_Noelle"] = new SkillEffectData
                { SkillEffectID2 = "SE_" + name + "_Noelle", EffectIndex = 1, EffectType = "Damage", Element = "Physical",
                    DamageType = type == 0 ? "Normal" : "Heavy", TargetType = "Enemy", TargetNumber = type == 0 ? 3 : 5,
                    TargetConsecutive = 1, TargetConsecutiveSet = true };
                Level(e, "SE_" + name + "_Noelle", type, type == 0 ? "1.1343*TotalATK,0" : ".5074*TotalATK,0", type == 0 ? "1.4509" : ".649");
            }
            Main(e, 1, NoelleScriptHooks.Breastplate, "Shield"); Main(e, 2, NoelleScriptHooks.SweepingTime, "Buff", "ATKBonusFlat");
            Main(e, 3, NoelleScriptHooks.Devotion); Main(e, 4, NoelleScriptHooks.CleanActions, "Count");
            Main(e, 5, S + "C2", "Buff", "DMGBonus").ApplyDamageType = "Heavy";
            Main(e, 6, S + "BurstCost"); Main(e, 7, S + "Enchant", "Enchant");
            Main(e, 8, S + "DevotionShield", "Shield"); Main(e, 9, NoelleScriptHooks.C4);
            var shield = Effect(e, 1, 1, "STE_Noelle_Shield", "Shield"); shield.Element = "Geo"; shield.Duration = 3;
            Effect(e, 1, 2, "STE_Noelle_Heal", "Heal", target: "Allies").TargetSelect = "-1";
            Level(e, "STE_Noelle_Shield", 2, "1.6*TotalDEF+769", "2*TotalDEF+1020");
            Level(e, "STE_Noelle_Heal", 2, ".2128*TotalDEF+102", ".266*TotalDEF+136");
            Level(e, "Noelle_HealingChance", 2, ".5", ".53");
            Effect(e, 2, 1, "STE_Noelle_BurstATK", "Buff").DamageHook = "Noelle_Defense()";
            Level(e, "STE_Noelle_BurstATK", 3, ".4", ".5");
            Effect(e, 2, 2, "STE_Noelle_BindCost", "BindStatus", S + "BurstCost");
            var enchant = Effect(e, 2, 3, "STE_Noelle_BindEnchant", "BindStatus", S + "Enchant");
            enchant.Element = "Geo"; enchant.Param2 = "1"; enchant.Param3 = "1";
            foreach (var type in new[] { "Normal", "Heavy" })
            {
                var change = Effect(e, 2, type == "Normal" ? 4 : 5, "STE_Noelle_" + type + "Control", "ChangeControl", type);
                change.Param2 = "SK_Sweep" + type + "_Noelle"; change.Param3 = "0";
            }
            Effect(e, 2, 6, "STE_Noelle_Extend", "AddTurn", NoelleScriptHooks.SweepingTime).Param2 = "1";
            var grant = Effect(e, 3, 1, "STE_Noelle_DevotionShield", "ApplyStatus", S + "DevotionShield", "Allies");
            grant.TargetOverride = "Noelle_DevotionTarget()"; grant.Duration = 5; grant.AddInPhase = 2;
            Effect(e, 4, 1, "STE_Noelle_ReduceCooldown", "ReduceCooldown", "Skill").Param2 = "1";
            Effect(e, 5, 1, "STE_Noelle_C2Damage", "Buff", ".15");
            Effect(e, 5, 2, "STE_Noelle_C2Cost", "APCost", "-5").Param2 = "Heavy";
            Effect(e, 6, 1, "STE_Noelle_BurstCost", "APCost", "-5").Param2 = "Normal;Heavy";
            var devotionShield = Effect(e, 8, 1, "STE_Noelle_CreateDevotionShield", "Shield", "4*TotalDEF");
            devotionShield.Element = "Geo"; devotionShield.Duration = 5;
            var c4 = Effect(e, 9, 1, "STE_Noelle_C4Damage", "Damage", "4*TotalATK,2", "Enemy");
            c4.Element = "Geo"; c4.DamageType = "Skill"; c4.TargetSelect = "-1";
            Actions(e, NoelleScriptHooks.Breastplate,
                new StatusActionData { ActionType = "OnApply", Param1 = "STE_Noelle_Shield" },
                new StatusActionData { ActionType = "OnTrigger", Param1 = "STE_Noelle_Heal", OutgoingHit = "Normal;Heavy",
                    ScriptHook = "Noelle_HealingHit()", MaxTimePerHit = 1 });
            Actions(e, NoelleScriptHooks.SweepingTime, new StatusActionData { ActionType = "OnTrigger", Param1 = "STE_Noelle_Extend",
                ScriptHook = "Kill(Self);Check(1021_C6)", MaxTimePerLife = 2 });
            Actions(e, NoelleScriptHooks.Devotion,
                new StatusActionData { ActionType = "OnItsTurn", Param1 = "STE_Noelle_DevotionShield", ScriptHook = "Noelle_Devotion()", Cooldown = 15 },
                new StatusActionData { ActionType = "OnTrigger", Param1 = "STE_Noelle_DevotionShield", ScriptHook = "Noelle_Devotion()", Cooldown = 15 });
            Actions(e, NoelleScriptHooks.CleanActions, new StatusActionData { ActionType = "OnTrigger", Param1 = "STE_Noelle_ReduceCooldown", ScriptHook = "Noelle_FourActions()" });
            Actions(e, S + "DevotionShield", new StatusActionData { ActionType = "OnApply", Param1 = "STE_Noelle_CreateDevotionShield" });
            Actions(e, NoelleScriptHooks.C4, new StatusActionData { ActionType = "OnTrigger", Param1 = "STE_Noelle_C4Damage", ScriptHook = "Noelle_C4ShieldEnd()" });
            e.CreateMinimalBattle(); e.Ally.Entity.EntityID = 1021; e.Ally.Entity.ConstellationLevel = constellation;
            e.Ally.Entity.TotalDEF = e.Ally.Entity.TotalATK = 100; e.Ally.Entity.CritRate = 0;
            e.CreateAlly(2); e.CreateEnemy(2); e.CreateEnemy(3);
            e.Manager.StartBattle(); e.Flow.AdvanceToPhase(e.Manager, TurnPhase.AllyAction);
            e.Ally.Entity.MaxEnergy = 60; e.Ally.Entity.CurrentEnergy = 60;
            BattleRandom.SetSource(new SequenceBattleRandomSource());
        }
        private static StatusInstance Add(BattleFlowTestEnv e, string id, int duration = 0)
            => e.Ally.Entity.AddStatus(id, e.Ally.Entity, duration, 2, 0, e.DataManager.StatusMainDict[id]);
        private static Task Cast(BattleFlowTestEnv e, string id, int type)
            => BattleExecution.Run(() => e.Ally.ExecuteSkillByIDAsync(id, type, true));

        [UnityTest]
        public IEnumerator Skill_UsesDefense_NoEnergy_ShieldAndHealingShareRealLifetime()
            => HookTestEnv.AwaitTask(SkillAsync);
        private async Task SkillAsync()
        {
            using var e = new BattleFlowTestEnv(); Setup(e); var owner = e.Ally.Entity;
            owner.TotalATK = 200; owner.CurrentEnergy = 0;
            await Cast(e, "SK_Skill_Noelle", 2);
            Assert.That(e.Manager.Enemies.All(enemy => Math.Abs(enemy.Entity.CurrentHP - 940f) < .001f), Is.True);
            Assert.That(owner.CurrentEnergy, Is.Zero); Assert.That(e.Ally.GetCooldownRemaining(2), Is.EqualTo(6));
            Assert.That(owner.GetStatus(NoelleScriptHooks.Breastplate).RemainingPhaseCount, Is.EqualTo(3));
            Assert.That(owner.Shields.Single().Value, Is.EqualTo(929f));
            await BattleExecution.Run(async () => { owner.TakeDamage(2500, DamageSourceInfo.CreateUnknown()); await BattleExecution.Flush(); });
            Assert.That(owner.IsAlive, Is.True); Assert.That(owner.HasStatus(NoelleScriptHooks.Breastplate), Is.False);
            Assert.That(owner.Shields.Count, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Healing_ThreeTargetHit_DrawsOneChance_AndC1GuaranteesWithoutExtraRoll()
            => HookTestEnv.AwaitTask(HealingAsync);
        private async Task HealingAsync()
        {
            using var e = new BattleFlowTestEnv(); Setup(e); var owner = e.Ally.Entity;
            await BattleExecution.Run(async () => { Add(e, NoelleScriptHooks.Breastplate, 3); await BattleExecution.Flush(); });
            owner.CurrentHP = e.Manager.Allies[1].Entity.CurrentHP = 400;
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            int heals = 0;
            using var listener = BattleEventHub.Subscribe<HealEventData>(BattleEventKind.HealResolved, (_, fact) => heals++, 0);
            await Cast(e, "SK_Heavy_Noelle", 1);
            Assert.That(heals, Is.EqualTo(2)); Assert.That(random.FloatCallCount, Is.EqualTo(4));
            Assert.That(owner.CurrentHP, Is.EqualTo(523.28f).Within(.001f));
            owner.ConstellationLevel = 1; Add(e, NoelleScriptHooks.SweepingTime, 3);
            random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random); heals = 0;
            await Cast(e, "SK_SweepHeavy_Noelle", 1);
            Assert.That(heals, Is.EqualTo(2)); Assert.That(random.FloatCallCount, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator CleanActions_CountsCompletedActions_NotHits_AndCountsMisses()
            => HookTestEnv.AwaitTask(ActionsAsync);
        private async Task ActionsAsync()
        {
            using var e = new BattleFlowTestEnv(); Setup(e); var count = Add(e, NoelleScriptHooks.CleanActions);
            await Cast(e, "SK_Skill_Noelle", 2); e.Manager.APManager.ResetAP();
            for (int i = 0; i < 4; i++) await Cast(e, "SK_Normal_Noelle", 0);
            Assert.That(e.Ally.GetCooldownRemaining(2), Is.EqualTo(5)); Assert.That(count.RuntimeIntValue, Is.Zero);
            var normal = e.Ally.GetSkillEffects("SK_Normal_Noelle"); normal[0].ScriptHook = "OnField(999999)";
            e.Ally.SetSkillEffects("SK_Normal_Noelle", normal); e.Manager.APManager.ResetAP();
            for (int i = 0; i < 4; i++) await Cast(e, "SK_Normal_Noelle", 0);
            Assert.That(e.Ally.GetCooldownRemaining(2), Is.EqualTo(4)); Assert.That(count.RuntimeIntValue, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Devotion_SelectsAbsoluteLowestHP_SameBatch_AndSharesCooldown()
            => HookTestEnv.AwaitTask(DevotionAsync);
        private async Task DevotionAsync()
        {
            using var e = new BattleFlowTestEnv(); Setup(e); var owner = e.Ally.Entity; var other = e.Manager.Allies[1].Entity;
            Add(e, NoelleScriptHooks.Devotion); owner.CurrentHP = other.CurrentHP = 100; other.TotalHP = 10000;
            await BattleExecution.Run(async () =>
            {
                using (var batch = new HitBatch())
                {
                    batch.Resolve(new HitRequest { Target = owner, DamageBeforeShield = 50, Source = DamageSourceInfo.CreateUnknown() });
                    batch.Resolve(new HitRequest { Target = other, DamageBeforeShield = 40, Source = DamageSourceInfo.CreateUnknown() });
                }
                await BattleExecution.Flush();
            });
            Assert.That(owner.HasStatus(S + "DevotionShield"), Is.True, "50点HP优先于60点，即使60点的生命比例更低");
            Assert.That(other.HasStatus(S + "DevotionShield"), Is.False);
            Assert.That(owner.Shields.Single().Value, Is.EqualTo(400f));
            await BattleExecution.Run(async () => { other.TakeDamage(30, DamageSourceInfo.CreateUnknown()); await BattleExecution.Flush(); });
            Assert.That(other.HasStatus(S + "DevotionShield"), Is.False, "15回合内不能因第二次HP变化再次授盾");
        }

        [UnityTest]
        public IEnumerator Devotion_TiedHP_UsesExactlyOneUniformIntegerDraw()
            => HookTestEnv.AwaitTask(TieAsync);
        private async Task TieAsync()
        {
            using var e = new BattleFlowTestEnv(); Setup(e); var owner = e.Ally.Entity; var other = e.Manager.Allies[1].Entity;
            Add(e, NoelleScriptHooks.Devotion); owner.CurrentHP = other.CurrentHP = 100;
            var random = new SequenceBattleRandomSource(intValues: new[] { 1 }); BattleRandom.SetSource(random);
            await BattleExecution.Run(async () =>
            {
                using (var batch = new HitBatch()) foreach (var target in new[] { owner, other })
                    batch.Resolve(new HitRequest { Target = target, DamageBeforeShield = 50, Source = DamageSourceInfo.CreateUnknown() });
                await BattleExecution.Flush();
            });
            Assert.That(other.HasStatus(S + "DevotionShield"), Is.True); Assert.That(owner.HasStatus(S + "DevotionShield"), Is.False);
            Assert.That(random.IntCallCount, Is.EqualTo(1)); Assert.That(random.FloatCallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Burst_BindsLiveDefenseConversion_ControlsCostsEnchant_AndRestoresOnRemoval()
            => HookTestEnv.AwaitTask(BurstAsync);
        private async Task BurstAsync()
        {
            using var e = new BattleFlowTestEnv(); Setup(e, 6); var owner = e.Ally.Entity;
            Add(e, S + "C2"); await Cast(e, "SK_Burst_Noelle", 3);
            Assert.That(e.Ally.GetFirstSelectEffect(0).TargetNumber, Is.EqualTo(3));
            Assert.That(e.Ally.GetFirstSelectEffect(1).TargetNumber, Is.EqualTo(5));
            Assert.That(owner.GetFinalATK("", "", -1, "Physical"), Is.EqualTo(190f)); owner.TotalDEF = 120;
            Assert.That(owner.GetFinalATK("", "", -1, "Physical"), Is.EqualTo(208f));
            Assert.That(APCostResolver.ForSkill(owner, new SkillMainData { APCost = 35 }, 1), Is.EqualTo(25));
            Assert.That(APCostResolver.ForSkill(owner, new SkillMainData { APCost = 25 }, 0), Is.EqualTo(20));
            Assert.That(owner.HasStatus(S + "Enchant"), Is.True);
            owner.RemoveStatus(NoelleScriptHooks.SweepingTime);
            Assert.That(owner.GetFinalATK("", "", -1, "Physical"), Is.EqualTo(100f)); Assert.That(owner.HasStatus(S + "Enchant"), Is.False);
            Assert.That(e.Ally.GetBoundSkillID(0), Is.EqualTo("SK_Normal_Noelle"));
            Assert.That(e.Ally.GetFirstSelectEffect(1).TargetNumber, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator C4_ExpirationReplacementAndBreak_TriggerOnce_DeathDoesNot()
            => HookTestEnv.AwaitTask(C4Async);
        private async Task C4Async()
        {
            using var e = new BattleFlowTestEnv(); Setup(e, 4); var owner = e.Ally.Entity;
            Add(e, NoelleScriptHooks.C4); int hits = 0;
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            { if (fact.Source.SourceEffectID == "STE_Noelle_C4Damage") hits++; }, 0);
            await BattleExecution.Run(async () =>
            {
                var old = Add(e, NoelleScriptHooks.Breastplate, 3); await BattleExecution.Flush();
                Add(e, NoelleScriptHooks.Breastplate, 3); await BattleExecution.Flush();
                Assert.That(old.IsActive, Is.False); Assert.That(hits, Is.EqualTo(3));
                owner.RemoveStatusInstance(owner.GetStatus(NoelleScriptHooks.Breastplate), "Expired"); await BattleExecution.Flush();
                Assert.That(hits, Is.EqualTo(6));
                Add(e, NoelleScriptHooks.Breastplate, 3); await BattleExecution.Flush();
                owner.TakeDamage(2500, DamageSourceInfo.CreateUnknown()); await BattleExecution.Flush();
                Assert.That(hits, Is.EqualTo(9));
                Add(e, NoelleScriptHooks.Breastplate, 3); await BattleExecution.Flush();
                owner.TakeDamage(100000, DamageSourceInfo.CreateUnknown()); await BattleExecution.Flush();
                Assert.That(hits, Is.EqualTo(9));
            });
        }

        [UnityTest]
        public IEnumerator C6_ThreeRealKills_ExtendOnlyTwice_AndAllBoundDurationsFollow()
            => HookTestEnv.AwaitTask(C6Async);
        private async Task C6Async()
        {
            using var e = new BattleFlowTestEnv(); Setup(e, 6); var root = Add(e, NoelleScriptHooks.SweepingTime, 3);
            foreach (var enemy in e.Manager.Enemies) enemy.Entity.CurrentHP = 1;
            await Cast(e, "SK_SweepNormal_Noelle", 0);
            Assert.That(root.RemainingPhaseCount, Is.EqualTo(5));
            Assert.That(root.BoundStatuses.All(child => child.RemainingPhaseCount == 5), Is.True);
            Assert.That(root.LifeTriggerCount, Is.EqualTo(2));
        }
    }
}
