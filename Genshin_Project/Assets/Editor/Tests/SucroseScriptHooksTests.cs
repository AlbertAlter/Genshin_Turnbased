using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>内存表与真实效果入口；未读取Charts，测试由用户在Unity中运行。</summary>
    public sealed class SucroseScriptHooksTests
    {
        private const string S = "ST_Sucrose_", E = "STE_Sucrose_";
        private static StatusMainData Main(BattleFlowTestEnv e, int id, string key, string part = "")
        {
            var m = new StatusMainData { StatusID = 4102600 + id, StatusID2 = S + key, StatusType = part == "" ? "" : "Buff",
                MultiplierPart1 = part, MaxStack = 1, MaxCount = 1, WhenMax = "0" };
            e.DataManager.StatusMainDict[m.StatusID2] = m;
            if (!e.DataManager.CharacterStatusIDs.TryGetValue(1026, out var ids)) e.DataManager.CharacterStatusIDs[1026] = ids = new HashSet<string>();
            ids.Add(m.StatusID2); return m;
        }
        private static StatusEffectData Effect(BattleFlowTestEnv e, int id, string key, string type, string param = null, string target = "Self")
        {
            var f = new StatusEffectData { StatusEffectID = id, StatusEffectID2 = E + key, EffectType = type, Param1 = param, TargetType = target };
            e.DataManager.StatusEffectDict[f.StatusEffectID2] = f; return f;
        }
        private static void Action(BattleFlowTestEnv e, string key, string type, string effects, string hook = null, string outgoing = null)
            => e.DataManager.StatusActionDict[S + key] = new List<StatusActionData> { new StatusActionData {
                StatusID = e.DataManager.StatusMainDict[S + key].StatusID, StatusID2 = S + key, ActionType = type,
                Param1 = effects, ScriptHook = hook, OutgoingHit = outgoing } };
        private static StatusInstance Add(BattleFlowTestEnv e, string key, int duration = 0, int trigger = 0)
            => e.Ally.Entity.AddStatus(S + key, e.Ally.Entity, duration, 2, trigger, e.DataManager.StatusMainDict[S + key]);
        private static void Setup(BattleFlowTestEnv e, int constellation = 0)
        {
            e.ConfigureAllyAction(0, "SK_Normal_Sucrose", "SE_Normal_Sucrose", "Anemo", ".4792*TotalATK,1", 0);
            e.ConfigureAllyAction(2, "SK_Skill_Sucrose", "SE_Skill_Sucrose", "Anemo", "2.112*TotalATK,1", 0, 4);
            Main(e, 1, "Wind"); Main(e, 3, "Catalyst"); Main(e, 4, "CatalystBonus", "EMBonus");
            Main(e, 5, "Mollis"); Main(e, 6, "EMShare", "EMBonus"); Main(e, 7, "Alchemania");
            Main(e, 8, "C6Bonus", "DMGBonus").ScriptHook = "Sucrose_C6Element()";
            var wind = Effect(e, 410260101, "WindDamage", "Damage", "1*TotalATK,1", "Enemy");
            wind.Element = "Anemo"; wind.DamageType = "Burst"; wind.TargetSelect = "-1"; wind.DamageHook = SucroseScriptHooks.RecordHook;
            var extra = Effect(e, 410260102, "AbsorbedDamage", "Damage", ".44*TotalATK,1", "Enemy");
            extra.Element = "Physical"; extra.DamageType = "Burst"; extra.TargetSelect = "-1"; extra.DamageHook = SucroseScriptHooks.AbsorbedHook;
            var bind = Effect(e, 410260103, "C6Bind", "BindStatus", S + "C6Bonus", "Allies");
            bind.TargetSelect = "-1"; bind.ScriptHook = "Check(1026_C6)";
            Action(e, "Wind", "OnItsTurn", E + "WindDamage;" + E + "AbsorbedDamage");
            var catalyst = Effect(e, 410260301, "CatalystApply", "ApplyStatus", S + "CatalystBonus", "Allies");
            catalyst.Duration = 2; catalyst.TargetOverride = "Sucrose_SwirlAllies()";
            Action(e, "Catalyst", "OnTrigger", E + "CatalystApply"); e.DataManager.StatusActionDict[S + "Catalyst"][0].ReactionTriggered = "Swirl";
            Effect(e, 410260401, "CatalystBonus", "Buff", "50");
            var share = Effect(e, 410260501, "EMShareApply", "ApplyStatus", S + "EMShare", "AlliesOnly");
            share.Duration = 2; share.TargetSelect = "-1";
            Action(e, "Mollis", "OnTrigger", E + "EMShareApply", "Sucrose_EMHit()", "Skill;Burst");
            Effect(e, 410260601, "EMShare", "Buff", ".2*TotalEM").DamageHook = "Sucrose_EMShare()";
            Effect(e, 410260701, "Cooldown", "ReduceCooldown", "Skill").Param2 = "1";
            Action(e, "Alchemania", "OnTrigger", E + "Cooldown", "Chance(0.1)", "Normal;Heavy");
            Effect(e, 410260801, "C6Bonus", "Buff", ".2");
            e.CreateMinimalBattle(); e.CreateAlly(2); e.CreateEnemy(2, 10000); e.CreateEnemy(3, 10000);
            e.Ally.Entity.EntityID = 1026; e.Ally.Entity.ConstellationLevel = constellation;
            e.Enemy.Entity.CurrentHP = e.Enemy.Entity.TotalHP = 10000;
            e.Manager.StartBattle(); e.Flow.AdvanceToPhase(e.Manager, TurnPhase.AllyAction);
            e.Ally.Entity.TotalATK = 100; e.Ally.Entity.CritRate = e.Ally.Entity.CritDMG = 0;
            BattleRandom.SetSource(new SequenceBattleRandomSource());
        }
        private static Task Tick(BattleFlowTestEnv e, StatusInstance wind) => BattleExecution.Run(() =>
            e.Ally.ExecuteStatusActionsTask(wind, e.DataManager.StatusActionDict[S + "Wind"], e.Ally.Entity, "OnItsTurn"));

        [UnityTest] public IEnumerator Wind_OnCasterUsesLiveAttack_AndNeverDealsPlaceholderPhysicalDamage() => HookTestEnv.AwaitTask(LiveWind);
        private async Task LiveWind()
        {
            using var e = new BattleFlowTestEnv(); Setup(e, 2); var wind = Add(e, "Wind", 2, 3);
            Assert.That(wind.Host, Is.SameAs(e.Ally.Entity)); Assert.That(wind.RemainingPhaseCount, Is.EqualTo(3)); Assert.That(wind.FormulaSnapshot, Is.Null);
            var hits = new List<float>(); int extra = 0;
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, f) =>
            { if (f.Source.SourceEffectID == E + "WindDamage") hits.Add(f.DamageBeforeShield); if (f.Source.SourceEffectID == E + "AbsorbedDamage") extra++; }, 0);
            await Tick(e, wind); e.Ally.Entity.TotalATK = 200; await Tick(e, wind);
            Assert.That(hits, Has.Count.EqualTo(6)); foreach (var hit in hits.Take(3)) Assert.That(hit, Is.EqualTo(50).Within(.001));
            foreach (var hit in hits.Skip(3)) Assert.That(hit, Is.EqualTo(100).Within(.001)); Assert.That(extra, Is.Zero);
        }

        [UnityTest] public IEnumerator Absorption_ProjectPriorityLocksPerInstance_AndBoundC6EndsWithParent() => HookTestEnv.AwaitTask(Absorption);
        private async Task Absorption()
        {
            using var e = new BattleFlowTestEnv(); Setup(e, 6); var owner = e.Ally.Entity;
            var targets = e.Manager.Enemies.Select(x => x.Entity).OrderBy(x => x.SlotPosition).ToList();
            targets[0].ApplyAura("Hydro", 1, owner.EntityID); targets[1].ApplyAura("Cryo", 1, owner.EntityID); targets[2].ApplyAura("Electro", 1, owner.EntityID);
            var wind = Add(e, "Wind", 2, 3); var extras = new List<DamageEventData>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, f) => { if (f.Source.SourceEffectID == E + "AbsorbedDamage") extras.Add(f); }, 0);
            await Tick(e, wind);
            Assert.That(SucroseScriptHooks.GetAbsorbedElement(wind), Is.EqualTo("Electro")); Assert.That(extras, Has.Count.EqualTo(3));
            Assert.That(extras.Select(x => x.Element), Is.All.EqualTo("Electro"));
            Assert.That(e.DataManager.StatusEffectDict[E + "AbsorbedDamage"].Element, Is.EqualTo("Physical"));
            Assert.That(owner.GetStatusDMGBonus("query", "", -1, "Electro", targets[0]), Is.EqualTo(.2f));
            Assert.That(owner.GetStatusDMGBonus("query", "", -1, "Pyro", targets[0]), Is.Zero);
            targets[0].ApplyAura("Pyro", 5, owner.EntityID); await Tick(e, wind);
            Assert.That(SucroseScriptHooks.GetAbsorbedElement(wind), Is.EqualTo("Electro")); owner.RemoveStatusInstance(wind);
            Assert.That(owner.GetStatusDMGBonus("query", "", -1, "Electro", targets[0]), Is.Zero);
            Assert.That(SucroseScriptHooks.GetAbsorbedElement(Add(e, "Wind", 2, 3)), Is.Null);
        }

        [UnityTest] public IEnumerator SharedMasteryReadsLiveCaster_ExcludesCaster_AndDoesNotStack() => HookTestEnv.AwaitTask(Mastery);
        private async Task Mastery()
        {
            using var e = new BattleFlowTestEnv(); Setup(e); var owner = e.Ally.Entity; var other = e.Manager.Allies.First(x => x.Entity != owner).Entity;
            owner.TotalEM = 100; other.TotalEM = 10; Add(e, "Mollis"); e.Ally.ForcedTargetPositions.Add(1);
            await BattleExecution.Run(() => e.Ally.ExecuteSkillByIDAsync("SK_Skill_Sucrose", 2, true));
            Assert.That(owner.HasStatus(S + "EMShare"), Is.False); Assert.That(other.GetFinalEM(), Is.EqualTo(30));
            owner.TotalEM = 200; Assert.That(other.GetFinalEM(), Is.EqualTo(50)); await Tick(e, Add(e, "Wind", 2, 3));
            Assert.That(other.GetStatusInstances(S + "EMShare").Count, Is.EqualTo(1)); Assert.That(other.GetFinalEM(), Is.EqualTo(50));
        }

        [UnityTest] public IEnumerator C4_RollsIndependentlyForEachTarget_AndUsesNormalCooldownEntry() => HookTestEnv.AwaitTask(Cooldown);
        private async Task Cooldown()
        {
            using var e = new BattleFlowTestEnv(); Setup(e, 4); e.Ally.ForcedTargetPositions.Add(1);
            await BattleExecution.Run(() => e.Ally.ExecuteSkillByIDAsync("SK_Skill_Sucrose", 2, true));
            Assert.That(e.Ally.GetCooldownRemaining(2), Is.EqualTo(4)); Add(e, "Alchemania");
            var random = new SequenceBattleRandomSource(new[] { .05f, .95f, .05f }); BattleRandom.SetSource(random);
            await BattleExecution.Run(() =>
            {
                using var batch = new HitBatch(); int index = 0;
                foreach (var enemy in e.Manager.Enemies) batch.Resolve(new HitRequest { Target = enemy.Entity, TargetIndex = index++, Element = "Anemo", DamageType = "Heavy", DamageBeforeShield = 1,
                    Source = DamageSourceInfo.Create(e.Ally.Entity, ReactionSourceKind.CharacterSkill, "SK_Heavy_Sucrose", "SE_Heavy_Sucrose") });
                return Task.CompletedTask;
            });
            Assert.That(random.FloatCallCount, Is.EqualTo(3)); Assert.That(random.IntCallCount, Is.Zero); Assert.That(e.Ally.GetCooldownRemaining(2), Is.EqualTo(2));
        }

        [UnityTest] public IEnumerator SwirlMastery_MatchesAbsorbedElement_AndNeverTheAnemoCaster() => HookTestEnv.AwaitTask(Catalyst);
        private async Task Catalyst()
        {
            using var e = new BattleFlowTestEnv(); Setup(e); var owner = e.Ally.Entity; var other = e.Manager.Allies.First(x => x.Entity != owner).Entity; other.EntityID = 990101;
            e.DataManager.CharacterAttributesDict[1026] = new CharacterAttributesData { CharacterID = 1026, Element = "Anemo" };
            e.DataManager.CharacterAttributesDict[990101] = new CharacterAttributesData { CharacterID = 990101, Element = "Pyro" }; Add(e, "Catalyst");
            await BattleExecution.Run(() =>
            {
                StatusOnHitHookSystem.NotifyReactions(new[] { new ReactionOccurrence { Type = ReactionType.Swirl, SourceEntity = owner, Target = e.Enemy.Entity,
                    SourceEffectID = "SE_Skill_Sucrose", InvolvedElements = new List<string> { "Anemo", "Pyro" } } }); return Task.CompletedTask;
            });
            Assert.That(other.HasStatus(S + "CatalystBonus"), Is.True); Assert.That(owner.HasStatus(S + "CatalystBonus"), Is.False);
        }

        [UnityTest] public IEnumerator SkillAirborne_UsesCommittedTargets_AndDoesNotGuessMissingEnemyClass() => HookTestEnv.AwaitTask(Airborne);
        private async Task Airborne()
        {
            using var e = new BattleFlowTestEnv(); Setup(e);
            e.DataManager.StatusMainDict["ST_Fall_Enemy"] = new StatusMainData { StatusID = 40002, StatusID2 = "ST_Fall_Enemy", MaxStack = 1, WhenMax = "0" };
            var enemies = e.Manager.Enemies.Select(x => x.Entity).OrderBy(x => x.SlotPosition).ToList();
            for (int i = 0; i < enemies.Count; i++)
            {
                enemies[i].EntityID = 990201 + i;
                e.DataManager.EnemyMainDict[enemies[i].EntityID] = new EnemyMainData { EnemyID = enemies[i].EntityID,
                    EnemyType = i == 0 ? "Minion" : i == 1 ? "Elite" : "" };
            }
            var damage = e.DataManager.SkillEffectDict["SE_Skill_Sucrose"]; damage.TargetNumber = 3;
            e.DataManager.SkillEffectDict["SE_Skill_SucroseFall"] = new SkillEffectData {
                SkillEffectID = damage.SkillEffectID + 1, SkillEffectID2 = "SE_Skill_SucroseFall", EffectIndex = 1,
                EffectType = "ApplyStatus", Duration = 1, Param1 = "ST_Fall_Enemy", TargetType = "Enemy", TargetOverride = "Sucrose_SkillMinions()" };
            e.Ally.ForcedTargetPositions.AddRange(new[] { 1, 2, 3 });
            await BattleExecution.Run(() => e.Ally.ExecuteSkillByIDAsync("SK_Skill_Sucrose", 2, true));
            Assert.That(enemies[0].HasStatus("ST_Fall_Enemy"), Is.True);
            Assert.That(enemies[1].HasStatus("ST_Fall_Enemy"), Is.False); Assert.That(enemies[2].HasStatus("ST_Fall_Enemy"), Is.False);
        }
    }
}
