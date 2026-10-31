using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>使用内存表经过正式状态/伤害/治疗入口，不依赖导表；由用户在Unity运行。</summary>
    public sealed class QiqiScriptHooksTests
    {
        private const string S = "ST_Qiqi_", E = "STE_Qiqi_";
        private static StatusMainData Main(BattleFlowTestEnv e, int id, string key, string part = "")
        {
            var m = new StatusMainData { StatusID = 4102700 + id, StatusID2 = S + key,
                StatusType = part == "" ? "" : "Buff", MultiplierPart = part, MaxStack = 1, MaxCount = 1, WhenMax = "0" };
            e.DataManager.StatusMainDict[m.StatusID2] = m;
            if (!e.DataManager.CharacterStatusIDs.TryGetValue(1027, out var ids))
                e.DataManager.CharacterStatusIDs[1027] = ids = new HashSet<string>();
            ids.Add(m.StatusID2); return m;
        }
        private static StatusEffectData Effect(BattleFlowTestEnv e, int id, string key, string type, string param, string target = "Self")
        {
            var f = new StatusEffectData { StatusEffectID = 410270000 + id, StatusEffectID2 = E + key,
                EffectType = type, Param1 = param, TargetType = target };
            e.DataManager.StatusEffectDict[f.StatusEffectID2] = f; return f;
        }
        private static StatusActionData Action(BattleFlowTestEnv e, string key, string type, string effects, string hook = null)
        {
            var a = new StatusActionData { StatusID = e.DataManager.StatusMainDict[S + key].StatusID,
                StatusID2 = S + key, ActionType = type, Param1 = effects, ScriptHook = hook };
            if (!e.DataManager.StatusActionDict.TryGetValue(a.StatusID2, out var rows))
                e.DataManager.StatusActionDict[a.StatusID2] = rows = new List<StatusActionData>();
            rows.Add(a); return a;
        }
        private static StatusInstance Add(BattleFlowTestEnv e, string key, BattleEntity host = null)
            => (host ?? e.Ally.Entity).AddStatus(S + key, e.Ally.Entity, 0, 2, 0, e.DataManager.StatusMainDict[S + key]);
        private static void Setup(BattleFlowTestEnv e, int constellation = 0)
        {
            e.ConfigureAllyAction(0, "SK_Normal_Qiqi", "SE_Normal_Qiqi", "Physical", "1*TotalATK,0", 0);
            e.ConfigureAllyAction(3, "SK_Burst_Qiqi", "SE_Burst_QiqiSeal", "Cryo", null, 0,
                effectType: "ApplyStatus", param1: S + "Talisman", duration: 4);
            Main(e, 1, "Herald"); Main(e, 2, "Talisman"); Main(e, 3, "LifeProlongation");
            Main(e, 4, "IncomingHealing", "BeHealedBonus"); Main(e, 5, "Arcanum"); Main(e, 6, "Ascetic");
            var frozen = Main(e, 7, "FrozenBone", "DMGBonus"); frozen.ApplyDamageType = "Normal;Heavy"; frozen.ScriptHook = "Qiqi_C2Cryo()";
            Main(e, 8, "Resurrection");
            var heal = Effect(e, 101, "HitHeal", "Heal", "1*TotalATK", "Allies"); heal.TargetSelect = "-1";
            var herald = Action(e, "Herald", "OnTrigger", E + "HitHeal", "Qiqi_HeraldHit()");
            herald.OutgoingHit = "Normal;Heavy"; herald.MaxTimePerHit = 1;
            Effect(e, 201, "TalismanHeal", "Heal", "1*TotalATK", "Allies").TargetOverride = "Qiqi_TalismanAttacker()";
            var rune = Action(e, "Talisman", "OnTrigger", E + "TalismanHeal", "Qiqi_TalismanHit()"); rune.OnHit = "All"; rune.MaxTimePerAction = 1;
            var life = Effect(e, 301, "LifeApply", "ApplyStatus", S + "IncomingHealing", "Allies");
            life.Duration = 2; life.TargetOverride = "Qiqi_Reactor()";
            Action(e, "LifeProlongation", "OnTrigger", E + "LifeApply", "Qiqi_Reaction()");
            Effect(e, 401, "IncomingHealing", "Buff", ".2");
            var arcanum = Effect(e, 501, "ArcanumApply", "ApplyStatus", S + "Talisman", "Enemy");
            arcanum.Duration = 2; arcanum.TargetSelect = "1"; arcanum.TargetConsecutive = 4; arcanum.TargetOverride = "Qiqi_NormalTargets()";
            var roll = Action(e, "Arcanum", "OnTrigger", E + "ArcanumApply", "Qiqi_ArcanumHit();Chance(0.5)");
            roll.OutgoingHit = "Normal;Heavy"; roll.Cooldown = 7;
            Effect(e, 601, "Energy", "GainEnergy", "2").Param2 = "Flat";
            var energy = Action(e, "Ascetic", "OnTrigger", E + "Energy", "Qiqi_MarkedColdHit()"); energy.OutgoingHit = E + "ColdDamage";
            Effect(e, 701, "FrozenBone", "Buff", ".15");
            Effect(e, 801, "Revive", "Revive", "0.5*Target.TotalHP", "Allies").TargetOverride = "Qiqi_FallenAllies()";
            Action(e, "Resurrection", "OnTrigger", E + "Revive", "Qiqi_Revive()").Cooldown = 4;
            e.CreateMinimalBattle(); e.CreateAlly(2); e.CreateEnemy(2, 10000); e.CreateEnemy(3, 10000);
            e.Ally.Entity.EntityID = 1027; e.Ally.Entity.ConstellationLevel = constellation;
            e.Enemy.Entity.CurrentHP = e.Enemy.Entity.TotalHP = 10000;
            e.Manager.StartBattle(); e.Flow.AdvanceToPhase(e.Manager, TurnPhase.AllyAction);
            e.Ally.Entity.TotalATK = 100; e.Ally.Entity.CritRate = e.Ally.Entity.CritDMG = 0;
            BattleRandom.SetSource(new SequenceBattleRandomSource());
        }
        private static async Task Hit(BattleFlowTestEnv e, BattleEntity source, string effect, string type, params BattleEntity[] targets)
        {
            using (var batch = new HitBatch())
            using (BattleSpecialRules.BeginHit(batch.RuntimeHitID, source, targets))
                for (int i = 0; i < targets.Length; i++)
                    batch.Resolve(new HitRequest { Target = targets[i], TargetIndex = i, Element = "Physical", DamageType = type,
                        DamageBeforeShield = 1, Source = DamageSourceInfo.Create(source, ReactionSourceKind.StatusEffect, null, effect) });
            await BattleExecution.Flush();
        }

        [UnityTest] public IEnumerator Herald_HealsOncePerHit_AndC1PaysForEachPreviouslyMarkedTarget() => HookTestEnv.AwaitTask(Herald);
        private async Task Herald()
        {
            using var e = new BattleFlowTestEnv(); Setup(e, 1); Add(e, "Herald"); Add(e, "Ascetic");
            var enemies = e.Manager.Enemies.Select(x => x.Entity).ToArray();
            Add(e, "Talisman", enemies[0]); Add(e, "Talisman", enemies[1]);
            int heals = 0;
            using var listener = BattleEventHub.Subscribe<HealEventData>(BattleEventKind.HealResolved,
                (_, f) => { if (f.EffectId == E + "HitHeal") heals++; }, 0);
            await BattleExecution.Run(() => Hit(e, e.Ally.Entity, "SE_Normal_Qiqi", "Normal", enemies));
            Assert.That(heals, Is.EqualTo(2), "全体两名队友各收到一次治疗，不按三个目标重复治疗");
            e.Ally.Entity.CurrentEnergy = 0;
            await BattleExecution.Run(() => Hit(e, e.Ally.Entity, E + "ColdDamage", "Skill", enemies));
            Assert.That(e.Ally.Entity.CurrentEnergy, Is.EqualTo(4)); Assert.That(heals, Is.EqualTo(2));
        }

        [UnityTest] public IEnumerator IndependentStatus_AllRowsAndOtherCharacterExtensionsShareEachRunesQuota() => HookTestEnv.AwaitTask(StatusQuota);
        private async Task StatusQuota()
        {
            using var e = new BattleFlowTestEnv(); Setup(e);
            var other = e.Manager.Allies.First(x => x != e.Ally); var targets = e.Manager.Enemies.Take(2).Select(x => x.Entity).ToArray();
            foreach (var enemy in targets) Add(e, "Talisman", enemy);
            e.ConfigureStatus("ST_IndependentEmitter");
            var emitter = other.Entity.AddStatus("ST_IndependentEmitter", other.Entity, 0, 2, 0, e.DataManager.StatusMainDict["ST_IndependentEmitter"]);
            var rows = new List<StatusActionData>();
            for (int i = 0; i < 2; i++)
            {
                var damage = Effect(e, 910 + i, "Emitter" + i, "Damage", "1*TotalATK,0", "Enemy");
                damage.Element = "Physical"; damage.TargetSelect = "-1";
                rows.Add(new StatusActionData { StatusID2 = emitter.StatusID2, ActionType = "OnItsTurn", Param1 = damage.StatusEffectID2 });
            }
            var heals = new List<HealEventData>(); var scopes = new HashSet<long>(); bool extension = false; int starts = 0;
            using var h = BattleEventHub.Subscribe<HealEventData>(BattleEventKind.HealResolved, (_, f) => { if (f.EffectId == E + "TalismanHeal") heals.Add(f); }, 0);
            using var a = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted, (_, f) => starts++, 0);
            using var d = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (header, f) =>
            {
                scopes.Add(header.ActionScopeId); Assert.That(header.ActionExecutionId, Is.Zero);
                if (extension || f.Source.SourceEffectID != E + "Emitter0") return;
                extension = true;
                BattleExecution.Enqueue(() => Hit(e, e.Ally.Entity, "derived", "", targets));
            }, 0);
            await BattleExecution.Run(() => other.ExecuteStatusActionsTask(emitter, rows, other.Entity, "OnItsTurn"));
            Assert.That(heals, Has.Count.EqualTo(2)); Assert.That(heals.Select(x => x.Subject), Is.All.SameAs(other.Entity));
            Assert.That(scopes, Has.Count.EqualTo(1)); Assert.That(scopes.Single(), Is.GreaterThan(0)); Assert.That(starts, Is.Zero);
            await BattleExecution.Run(() => other.ExecuteStatusActionsTask(emitter, rows, other.Entity, "OnItsTurn"));
            Assert.That(heals, Has.Count.EqualTo(4)); Assert.That(scopes, Has.Count.EqualTo(2));
        }

        [UnityTest] public IEnumerator Arcanum_RollsOnceForMultiTargetHit_AndCooldownDoesNotRollAgain() => HookTestEnv.AwaitTask(Arcanum);
        private async Task Arcanum()
        {
            using var e = new BattleFlowTestEnv(); Setup(e); Add(e, "Arcanum");
            var enemies = e.Manager.Enemies.Select(x => x.Entity).ToArray();
            var random = new SequenceBattleRandomSource(new[] { .2f }); BattleRandom.SetSource(random);
            await BattleExecution.Run(() => Hit(e, e.Ally.Entity, "SE_Normal_Qiqi", "Normal", enemies));
            Assert.That(random.FloatCallCount, Is.EqualTo(1)); Assert.That(enemies.Count(x => x.HasStatus(S + "Talisman")), Is.EqualTo(1));
            await BattleExecution.Run(() => Hit(e, e.Ally.Entity, "SE_Heavy_Qiqi", "Heavy", enemies));
            Assert.That(random.FloatCallCount, Is.EqualTo(1), "冷却内不再抽概率");
        }

        [UnityTest] public IEnumerator Burst_MarksBeforeDamage_AndC6RevivesOnlyAfterAllBurstEffects() => HookTestEnv.AwaitTask(Burst);
        private async Task Burst()
        {
            using var e = new BattleFlowTestEnv(); Setup(e, 6); Add(e, "Resurrection");
            var owner = e.Ally.Entity; var fallen = e.Manager.Allies.First(x => x != e.Ally).Entity; fallen.CurrentHP = 0;
            var effects = e.Ally.GetSkillEffects("SK_Burst_Qiqi"); effects[0].TargetNumber = -1;
            effects.Add(new SkillEffectData { SkillEffectID = 10270402, SkillEffectID2 = "SE_Burst_QiqiDamage", EffectIndex = 1,
                EffectType = "Damage", Element = "Cryo", DamageType = "Burst", Param1 = "1*TotalATK,0", TargetType = "Enemy", TargetNumber = -1 });
            e.Ally.SetSkillEffects("SK_Burst_Qiqi", effects);
            int heals = 0, damage = 0; bool sawComplete = false;
            using var h = BattleEventHub.Subscribe<HealEventData>(BattleEventKind.HealResolved, (_, f) =>
            { if (f.EffectId == E + "TalismanHeal") { heals++; Assert.That(f.Subject, Is.SameAs(owner)); Assert.That(fallen.IsDead, Is.True); } }, 0);
            using var d = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, f) =>
            { if (f.Source.SourceEffectID == "SE_Burst_QiqiDamage") { damage++; Assert.That(f.Subject.HasStatus(S + "Talisman"), Is.True); Assert.That(fallen.IsDead, Is.True); } }, 0);
            using var a = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted, (_, f) =>
            { if (f.Subject == owner) { sawComplete = true; Assert.That(damage, Is.EqualTo(3)); } }, 0);
            await BattleExecution.Run(() => e.Ally.ExecuteSkillByIDAsync("SK_Burst_Qiqi", 3, true));
            Assert.That(sawComplete, Is.True); Assert.That(heals, Is.EqualTo(3)); Assert.That(fallen.IsAlive, Is.True);
            Assert.That(fallen.CurrentHP, Is.EqualTo(fallen.GetFinalHP() * .5f).Within(.001));
        }

        [UnityTest] public IEnumerator LifeProlongation_UsesActualAllyReactionSource() => HookTestEnv.AwaitTask(Life);
        private async Task Life()
        {
            using var e = new BattleFlowTestEnv(); Setup(e); Add(e, "LifeProlongation");
            var other = e.Manager.Allies.First(x => x != e.Ally).Entity;
            await BattleExecution.Run(() =>
            {
                StatusOnHitHookSystem.NotifyReactions(new[] { new ReactionOccurrence { Type = ReactionType.Swirl,
                    SourceEntity = other, Target = e.Enemy.Entity, InvolvedElements = new List<string> { "Anemo", "Pyro" } } });
                return Task.CompletedTask;
            });
            Assert.That(other.HasStatus(S + "IncomingHealing"), Is.True); Assert.That(e.Ally.Entity.HasStatus(S + "IncomingHealing"), Is.False);
        }
    }
}
