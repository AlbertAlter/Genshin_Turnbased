using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class MonaScriptHooksTests
    {
        private const string S = "ST_Mona_", F = "STE_Mona_";
        private static StatusMainData Main(BattleFlowTestEnv e, int n, string key, string part = "", int stack = 1, int cap = 1)
        {
            var main = new StatusMainData { StatusID = 4102800 + n, StatusID2 = S + key, StatusType = part == "" ? "" : "Buff",
                MultiplierPart = part, MaxStack = stack, MaxCount = cap, WhenMax = "0" };
            e.DataManager.StatusMainDict[main.StatusID2] = main;
            if (!e.DataManager.CharacterStatusIDs.TryGetValue(1028, out var ids)) e.DataManager.CharacterStatusIDs[1028] = ids = new HashSet<string>();
            ids.Add(main.StatusID2); return main;
        }
        private static StatusEffectData Fx(BattleFlowTestEnv e, string key, int n, string name, string type, string param = null, string target = "Self")
        {
            var f = new StatusEffectData { StatusEffectID = e.DataManager.StatusMainDict[S + key].StatusID * 100 + n,
                StatusEffectID2 = F + name, EffectIndex = n, EffectType = type, Param1 = param, TargetType = target };
            e.DataManager.StatusEffectDict[f.StatusEffectID2] = f; return f;
        }
        private static StatusActionData Action(BattleFlowTestEnv e, string key, string type, string effects, string hook = null)
        {
            var a = new StatusActionData { StatusID = e.DataManager.StatusMainDict[S + key].StatusID,
                StatusID2 = S + key, ActionType = type, Param1 = effects, ScriptHook = hook };
            if (!e.DataManager.StatusActionDict.TryGetValue(a.StatusID2, out var rows)) e.DataManager.StatusActionDict[a.StatusID2] = rows = new List<StatusActionData>();
            rows.Add(a); return a;
        }
        private static StatusInstance Add(BattleFlowTestEnv e, string key, BattleEntity host = null, int duration = 0)
            => (host ?? e.Ally.Entity).AddStatus(S + key, e.Ally.Entity, duration, 2, 0, e.DataManager.StatusMainDict[S + key]);
        private static void Setup(BattleFlowTestEnv e)
        {
            e.ConfigureAllyAction(0, "SK_Normal_Mona", "SE_Normal_Mona", "Hydro", "1*TotalATK,0", 0);
            e.ConfigureAllyAction(1, "SK_Heavy_Mona", "SE_Heavy_Mona", "Hydro", "1*TotalATK,0", 0);
            e.DataManager.SkillEffectDict["SE_Heavy_Mona"].DamageHook = "Mona_C6()";
            Main(e, 1, "Phantom", stack: 0); Main(e, 2, "LesserPhantom", stack: 0);
            Main(e, 3, "Weakness", "DMGBonus", 0).StatusType = "Debuff";
            Main(e, 4, "Bubble"); Main(e, 5, "Omen");
            Main(e, 6, "OmenBonus", "DMGBonus", 0).ScriptHook = "Mona_OmenDamage()";
            Main(e, 7, "C1Amplifying", "BaseDMGBonus", 0).ScriptHook = "Mona_Omen()";
            e.DataManager.StatusMainDict[S + "C1Amplifying"].ApplyReactionType = "Vaporize";
            Main(e, 10, "C4Rate", "CritRate", 0).ScriptHook = "Mona_Omen()";
            Main(e, 12, "Escape", "DMGTaken").ScriptHook = "Mona_Dodge()";
            Main(e, 14, "Hexerei"); Main(e, 15, "Glow", stack: 3, cap: 3);
            Main(e, 16, "GlowBonus", "BaseDMGBonus", 0, 3).ScriptHook = "Mona_GlowBonus()";
            e.DataManager.StatusMainDict[S + "GlowBonus"].ApplyReactionType = "Vaporize";
            Main(e, 17, "C2"); Main(e, 18, "Guarantee"); Main(e, 20, "Calamity").StatusType = "Count";
            foreach (var pair in new[] { new[] { "OmenBonus", "OmenBonus" }, new[] { "C1Amplifying", "C1Amplifying" }, new[] { "C4Rate", "C4Rate" } })
                Fx(e, "Omen", 2 + Array.IndexOf(new[] { "OmenBonus", "C1Amplifying", "C4Rate" }, pair[0]), "Bind" + pair[0], "BindStatus", S + pair[1], "AllyTeam");
            Fx(e, "OmenBonus", 1, "OmenBonus", "Buff", ".42");
            Fx(e, "C1Amplifying", 1, "C1Front", "Buff", ".15").ScriptHook = "Mona_Foreground()";
            Fx(e, "C1Amplifying", 2, "C1Back", "Buff", ".24").ScriptHook = "Mona_Background()";
            Fx(e, "C4Rate", 1, "C4Rate", "Buff", ".15");
            Fx(e, "Weakness", 1, "Weakness", "Buff", "-.1");
            foreach (string key in new[] { "Phantom", "LesserPhantom" })
                Fx(e, key, 4, key + "Bind", "BindStatus", S + "Weakness", "EnemyField").TargetOverride = "Mona_PhantomArea()";
            Fx(e, "Escape", 1, "Escape", "Buff", "-.5");
            var phantom = Fx(e, "Escape", 2, "EscapePhantom", "ApplyStatus", S + "LesserPhantom", "EnemyField");
            phantom.Duration = 1; phantom.AddInPhase = phantom.TriggerPhase = 6; phantom.TargetOverride = "Mona_AttackerField()";
            var dodge = Action(e, "Escape", "OnTrigger", F + "EscapePhantom", "Mona_DodgeHit()"); dodge.OnHit = "All"; dodge.MaxTimePerTurn = 1;
            Fx(e, "Glow", 1, "GlowBind", "BindStatus", S + "GlowBonus", "AllyTeam");
            Fx(e, "GlowBonus", 1, "GlowBonus", "Buff", ".05");
            Fx(e, "Hexerei", 2, "GlowConsume", "RemoveStatus", S + "Glow");
            Action(e, "Hexerei", "OnTrigger", F + "GlowConsume", "Mona_Vape()").MaxTimePerHit = 1;
            Fx(e, "C2", 2, "GuaranteeRemove", "RemoveStatus", S + "Guarantee");
            var auto = Fx(e, "C2", 3, "AutoHeavy", "Damage", "1*TotalATK,0", "Enemy");
            auto.Element = "Hydro"; auto.DamageType = "Heavy"; auto.DamageHook = "Mona_C6()"; auto.TargetOverride = "Mona_AutoArea()";
            var c2 = Action(e, "C2", "OnTrigger", F + "GuaranteeRemove;" + F + "AutoHeavy", "Mona_C2()");
            c2.OutgoingHit = "Normal"; c2.MaxTimePerHit = 1; c2.Cooldown = 1;
            var add = typeof(TeamBonusParameters).GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var p in new[] { new[] { "Enabled", "1" }, new[] { "RequiredCount", "2" }, new[] { "CharacterIDs", "1028;1024;990001" } })
                add.Invoke(e.DataManager.TeamBonusRules.Hexerei, new object[] { p[0], p[1] });
            e.CreateMinimalBattle(); e.CreateAlly(2); var fischl = e.CreateAlly(3);
            fischl.Entity.EntityID = 1024; fischl.Entity.ConstellationLevel = 6;
            e.CreateEnemy(2, 10000); e.CreateEnemy(3, 10000); e.Ally.Entity.EntityID = 1028; e.Ally.Entity.ConstellationLevel = 6;
            e.Enemy.Entity.TotalHP = e.Enemy.Entity.CurrentHP = 10000;
            e.Manager.StartBattle(); e.Flow.AdvanceToPhase(e.Manager, TurnPhase.AllyAction);
            foreach (var ally in e.Manager.Allies) { ally.Entity.TotalATK = 100; ally.Entity.CritRate = ally.Entity.CritDMG = 0; }
            BattleRandom.SetSource(new SequenceBattleRandomSource());
        }
        private static Task Tick(BattleFlowTestEnv e, CharacterBattleController actor, StatusInstance state)
            => BattleExecution.Run(() => actor.ExecuteStatusActionsTask(state, e.DataManager.StatusActionDict[state.StatusID2], state.Host, "OnItsTurn"));
        private static StatusInstance Emitter(BattleFlowTestEnv e, CharacterBattleController actor, bool snapshot = false, string element = "Physical")
        {
            const string key = "ST_MonaTest_Emitter", effect = "STE_MonaTest_Damage";
            var main = new StatusMainData { StatusID = 4802820, StatusID2 = key, MaxStack = 1, MaxCount = 1, WhenMax = "0" };
            e.DataManager.StatusMainDict[key] = main;
            e.DataManager.StatusEffectDict[effect] = new StatusEffectData { StatusEffectID = 480282001, StatusEffectID2 = effect,
                EffectType = "Damage", Element = element, DamageType = "Skill", Param1 = element == "Physical" ? "1*TotalATK,0" : "1*TotalATK,1", TargetType = "Enemy", TargetSelect = "-1" };
            e.DataManager.StatusActionDict[key] = new List<StatusActionData> { new StatusActionData { StatusID2 = key, ActionType = "OnItsTurn", Param1 = effect } };
            if (snapshot) e.DataManager.StatusAttributesDict[key] = new StatusAttributesData { StatusID = main.StatusID, StatusID2 = key };
            return actor.Entity.AddStatus(key, actor.Entity, 3, 2, 3, main);
        }

        [UnityTest] public IEnumerator Snapshot_FixesSelfValues_ButAcceptsNewOmenAndLosesExpiredOmen() => HookTestEnv.AwaitTask(Snapshot);
        private async Task Snapshot()
        {
            using var e = new BattleFlowTestEnv(); Setup(e); var actor = e.Manager.Allies[1]; actor.Entity.DMGBonus = .1f;
            var state = Emitter(e, actor, true); actor.Entity.DMGBonus = .2f; actor.Entity.TotalATK = 500;
            var omen = Add(e, "Omen", e.Enemy.Entity, 1); var damage = new List<DamageEventData>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, f) =>
            { if (f.Source.SourceEffectID == "STE_MonaTest_Damage") damage.Add(f); }, 0);
            await Tick(e, actor, state);
            Assert.That(damage[0].DamageBeforeShield, Is.EqualTo(76f).Within(.001));
            Assert.That(damage[1].DamageBeforeShield, Is.EqualTo(55f).Within(.001));
            Assert.That(damage[0].Critical.Value.CriticalRate, Is.EqualTo(.15f).Within(.001));
            e.Enemy.Entity.RemoveStatusInstance(omen); damage.Clear(); await Tick(e, actor, state);
            Assert.That(damage.Select(x => x.DamageBeforeShield), Is.All.EqualTo(55f).Within(.001));
        }

        [UnityTest] public IEnumerator Phantoms_CoexistAndBindOverlapOnce_AndC6ChangesEachTargetWithinOneHit() => HookTestEnv.AwaitTask(Targets);
        private async Task Targets()
        {
            using var e = new BattleFlowTestEnv(); Setup(e); var field = e.Manager.Field.GetSlot(BattleSide.Enemy, 2);
            var a = field.AddStatus(S + "Phantom", e.Ally.Entity, 2, 6, 6, e.DataManager.StatusMainDict[S + "Phantom"]);
            var b = field.AddStatus(S + "LesserPhantom", e.Ally.Entity, 1, 6, 6, e.DataManager.StatusMainDict[S + "LesserPhantom"]);
            var c = field.AddStatus(S + "Phantom", e.Ally.Entity, 2, 6, 6, e.DataManager.StatusMainDict[S + "Phantom"]);
            Assert.That(a.IsActive && b.IsActive && c.IsActive, Is.True);
            Assert.That(e.Enemy.Entity.GetStatusDMGBonus("query", "", -1, "Physical", e.Ally.Entity), Is.EqualTo(-.1f).Within(.001));
            Add(e, "Omen", e.Enemy.Entity, 1);
            var heavy = e.Ally.GetSkillEffects("SK_Heavy_Mona"); heavy[0].TargetNumber = -1; e.Ally.SetSkillEffects("SK_Heavy_Mona", heavy);
            var hits = new List<DamageEventData>(); var ids = new HashSet<long>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (h, f) =>
            { if (f.Source.SourceEffectID == "SE_Heavy_Mona") { hits.Add(f); ids.Add(h.HitId); } }, 0);
            await BattleExecution.Run(() => e.Ally.ExecuteSkillByIDAsync("SK_Heavy_Mona", 1, true));
            Assert.That(ids, Has.Count.EqualTo(1)); Assert.That(hits, Has.Count.EqualTo(3));
            Assert.That(hits[0].DamageBeforeShield, Is.EqualTo(142f).Within(.001));
            Assert.That(hits[1].DamageBeforeShield, Is.EqualTo(50f).Within(.001));
        }

        [UnityTest] public IEnumerator Escape_ReducesTriggeringHitAndFollowingHitsOnlyOnMona_OncePerTurn() => HookTestEnv.AwaitTask(Escape);
        private async Task Escape()
        {
            using var e = new BattleFlowTestEnv(); Setup(e); Add(e, "Escape"); var other = e.Manager.Allies[1].Entity;
            var results = new List<float>();
            async Task Hit(BattleEntity target)
            {
                using (var batch = new HitBatch())
                using (BattleSpecialRules.BeginHit(batch.RuntimeHitID, e.Enemy.Entity, new[] { target }))
                {
                    var taken = target.PrepareDMGTaken("enemy-hit", "enemy", "Normal", "Physical");
                    results.Add(batch.Resolve(new HitRequest { Target = target, DamageBeforeShield = 100, DMGTaken = taken, Element = "Physical",
                        DamageType = "Normal", Source = DamageSourceInfo.Create(e.Enemy.Entity, ReactionSourceKind.EnemySkill, "enemy", "enemy-hit") }).DamageBeforeShield);
                }
                await BattleExecution.Flush();
            }
            await BattleExecution.Run(async () =>
            {
                using var scope = BattleResolutionContext.EnterAction(new BattleResolutionContext.CounterScope());
                await Hit(e.Ally.Entity); await Hit(other); await Hit(e.Ally.Entity);
            });
            await BattleExecution.Run(async () =>
            { using var scope = BattleResolutionContext.EnterAction(new BattleResolutionContext.CounterScope()); await Hit(e.Ally.Entity); });
            Assert.That(results, Is.EqualTo(new[] { 50f, 100f, 50f, 100f }));
            Assert.That(e.Manager.Field.GetSlot(BattleSide.Enemy, 1).StatusList.Count(x => x.StatusID2 == S + "LesserPhantom"), Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator Glow_FixesEachNewLayersStrength_AndOneVapeHitConsumesOldLayersForAllVapingTargets() => HookTestEnv.AwaitTask(Glow);
        private async Task Glow()
        {
            using var e = new BattleFlowTestEnv(); Setup(e); Add(e, "Hexerei"); e.DataManager.HexereiStatusIDs.Add(S + "GlowBonus");
            var first = Add(e, "Glow", duration: 2); first.RemainingPhaseCount = 1;
            var windowMain = new StatusMainData { StatusID = 4102408, StatusID2 = HexereiStatusEnhancement.Window, MaxStack = 1, WhenMax = "0" };
            e.DataManager.StatusMainDict[windowMain.StatusID2] = windowMain;
            var window = e.Manager.AllyTeam.AddStatus(windowMain.StatusID2, e.Manager.Allies[2].Entity, 2, 2, 0, windowMain);
            var second = Add(e, "Glow", duration: 2); var third = Add(e, "Glow", duration: 2);
            Assert.That(first.RemainingPhaseCount, Is.EqualTo(2));
            Assert.That(first.BoundStatuses.Single().HexereiMultiplier, Is.EqualTo(1));
            Assert.That(second.BoundStatuses.Single().HexereiMultiplier, Is.EqualTo(2));
            e.Manager.AllyTeam.RemoveStatusInstance(window);
            Assert.That(third.BoundStatuses.Single().HexereiMultiplier, Is.EqualTo(2));
            var actor = e.Manager.Allies[1]; var state = Emitter(e, actor, element: "Pyro");
            e.Enemy.Entity.ApplyAura("Hydro", 10, e.Ally.Entity.EntityID);
            e.Manager.Enemies[1].Entity.ApplyAura("Hydro", 10, e.Ally.Entity.EntityID);
            var values = new List<float>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, f) =>
            { if (f.Source.SourceEffectID == "STE_MonaTest_Damage") values.Add(f.DamageBeforeShield); }, 0);
            await Tick(e, actor, state);
            Assert.That(values[0], Is.EqualTo(93.75f).Within(.001)); Assert.That(values[1], Is.EqualTo(93.75f).Within(.001));
            Assert.That(values[2], Is.EqualTo(50f).Within(.001)); Assert.That(e.Ally.Entity.HasStatus(S + "Glow"), Is.False);
        }

        [UnityTest] public IEnumerator Glow_NestedVapeCannotReuseLayersAwaitingRemoval() => HookTestEnv.AwaitTask(NestedGlow);
        private async Task NestedGlow()
        {
            using var e = new BattleFlowTestEnv(); Setup(e); Add(e, "Hexerei"); Add(e, "Glow", duration: 2);
            var actor = e.Manager.Allies[1]; var nested = e.Manager.Allies[2];
            var first = Emitter(e, actor, element: "Pyro"); var next = Emitter(e, nested, element: "Pyro");
            foreach (var enemy in e.Manager.Enemies) enemy.Entity.ApplyAura("Hydro", 10, e.Ally.Entity.EntityID);
            bool started = false; var firstValues = new List<float>(); var nextValues = new List<float>();
            // 早于魔导监听者执行，在辉光正常RemoveStatus之前产生另一名队友的嵌套蒸发。
            using var response = BattleEventHub.Subscribe<ReactionEventData>(BattleEventKind.ReactionOccurred, (_, fact) =>
            {
                if (started || fact.Source != actor.Entity || fact.Reaction != ReactionType.Vaporize) return;
                started = true;
                Assert.That(e.Ally.Entity.HasStatus(S + "Glow"), Is.True);
                BattleExecution.Enqueue(() => nested.ExecuteStatusActionsTask(next, e.DataManager.StatusActionDict[next.StatusID2], next.Host, "OnItsTurn"));
            }, 0);
            using var damage = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            {
                if (fact.Source.SourceEffectID != "STE_MonaTest_Damage") return;
                (fact.Source.SourceEntity == actor.Entity ? firstValues : nextValues).Add(fact.DamageBeforeShield);
            }, 0);
            await Tick(e, actor, first);
            Assert.That(started, Is.True);
            Assert.That(firstValues, Has.Count.EqualTo(3)); Assert.That(nextValues, Has.Count.EqualTo(3));
            Assert.That(firstValues, Is.All.EqualTo(78.75f).Within(.001));
            Assert.That(nextValues, Is.All.EqualTo(75f).Within(.001));
            Assert.That(e.Ally.Entity.HasStatus(S + "Glow"), Is.False);
        }

        [UnityTest] public IEnumerator C2_GuaranteedAttackDoesNotRollProbability_AndAutomaticHeavyKeepsHitIdentityPerTarget() => HookTestEnv.AwaitTask(C2);
        private async Task C2()
        {
            using var e = new BattleFlowTestEnv(); Setup(e); Add(e, "C2"); Add(e, "Guarantee", duration: 1);
            var rng = new SequenceBattleRandomSource(); BattleRandom.SetSource(rng);
            int auto = 0, actions = 0;
            using var damage = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, f) => { if (f.Source.SourceEffectID == F + "AutoHeavy") auto++; }, 0);
            using var starts = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted, (_, a) => actions++, 0);
            e.Ally.ForcedTargetPositions.Add(2);
            await BattleExecution.Run(() => e.Ally.ExecuteSkillByIDAsync("SK_Normal_Mona", 0, true));
            Assert.That(auto, Is.EqualTo(3)); Assert.That(actions, Is.EqualTo(1)); Assert.That(e.Ally.Entity.HasStatus(S + "Guarantee"), Is.False);
            Assert.That(rng.FloatCallCount, Is.EqualTo(4), "只有普攻及自动重击三目标的暴击抽取，没有20%概率抽取");
        }
    }
}
