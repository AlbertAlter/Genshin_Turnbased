using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using OfficeOpenXml;
using UnityEngine;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class OffFieldTests
    {
        internal static StatusInstance Leave(BattleFlowTestEnv env, BattleEntity unit, int duration = 0, int phase = 6)
        {
            if (!env.DataManager.StatusMainDict.ContainsKey(PublicControlStates.OffField))
                env.ConfigureStatus(PublicControlStates.OffField);
            return unit.AddStatus(PublicControlStates.OffField, unit, duration, phase, 0,
                env.DataManager.StatusMainDict[PublicControlStates.OffField]);
        }

        private static TargetResolutionResult Resolve(BattleFlowTestEnv env, BattleEntity source,
            string target = "Enemy", int mode = 1, int count = 1, object origin = null,
            bool bound = false, List<int> forced = null, bool taunt = false)
            => BattleTargetResolver.Resolve(env.Manager, source, origin ?? source, target, count, mode,
                null, null, forced, null, selfUsesOrigin: bound, respectTaunt: taunt,
                weightProvider: source.Side == BattleSide.Enemy
                    ? (Func<int, float>)(p => env.Manager.GetEntityByPosition(BattleSide.Ally, p)?.HitWeight ?? 0f) : null);

        [Test]
        public void RuntimePublicTable_DefinesOneSharedMarkerWithoutChangeControl()
        {
            using var book = new ExcelPackage(new FileInfo(Path.Combine(Application.streamingAssetsPath, "Data", "StatusData_Overall.xlsx")));
            var sheet = book.Workbook.Worksheets["StatusData_Overall_Main"];
            int found = 0;
            for (int row = 2; row <= sheet.Dimension.End.Row; row++)
                if (sheet.Cells[row, 2].Text == "OffField")
                {
                    found++;
                    Assert.That(sheet.Cells[row, 1].GetValue<int>(), Is.EqualTo(40040));
                    Assert.That(sheet.Cells[row, 14].GetValue<int>(), Is.EqualTo(1));
                }
            Assert.That(found, Is.EqualTo(1));
            var effects = book.Workbook.Worksheets["StatusEffect_Overall"];
            for (int row = 2; row <= effects.Dimension.End.Row; row++)
                Assert.That(effects.Cells[row, 1].GetValue<int>() / 100, Is.Not.EqualTo(40040));
        }

        [TestCase(false, 0)] [TestCase(false, 1)] [TestCase(false, 2)] [TestCase(false, 3)] [TestCase(false, 4)]
        [TestCase(true, 0)] [TestCase(true, 1)] [TestCase(true, 2)] [TestCase(true, 3)] [TestCase(true, 4)]
        public void BothSides_AllAbsent_NoTargetsAndNoRandomDraw(bool enemySource, int mode)
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var target = enemySource ? env.Ally.Entity : env.Enemy.Entity;
            var source = enemySource ? env.Enemy.Entity : env.Ally.Entity;
            Leave(env, target);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            var result = Resolve(env, source, mode: mode);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Positions, Is.Empty);
            Assert.That(target.IsAlive, Is.True);
            Assert.That(target.Position.Occupant, Is.SameAs(target));
            Assert.That(target.Position.IsOccupied, Is.True, "保留格子，不让召唤顶替");
            Assert.That(random.IntCallCount, Is.Zero); Assert.That(random.FloatCallCount, Is.Zero);
        }

        [Test]
        public void ConsecutiveWindow_PreservesGapButExcludesAbsentScoreAndEntity()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var absent = env.CreateEnemy(2, 90000).Entity; Leave(env, absent);
            var third = env.CreateEnemy(3).Entity;
            var random = new SequenceBattleRandomSource(intValues: new[] { 0 }); BattleRandom.SetSource(random);
            var result = Resolve(env, env.Ally.Entity, count: 3);
            Assert.That(result.Positions, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(BattleTargetResolver.GetEntities(env.Manager, result), Is.EqualTo(new[] { env.Enemy.Entity, third }));
            Assert.That(env.Manager.GetEnemyHpAt(2), Is.Zero);
            Assert.That(random.IntCallCount, Is.Zero); Assert.That(random.FloatCallCount, Is.Zero);
            var forced = Resolve(env, env.Ally.Entity, forced: new List<int> { 2 });
            Assert.That(forced.IsValid, Is.False);
            Assert.That(BattleTargetResolver.GetEntities(env.Manager, forced), Is.Empty);
            Assert.That(env.Manager.SummonEnemy(BattleFlowTestEnv.EnemyID, 1, 2), Is.Null);
        }

        [Test]
        public void AbsentTaunter_DoesNotAttractTargets_AndReturnRestoresEligibility()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var other = env.CreateAlly(2).Entity;
            env.ConfigureStatus("Taunt");
            other.AddStatus("Taunt", other, 0, 2, 0, env.DataManager.StatusMainDict["Taunt"]);
            var off = Leave(env, other);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            Assert.That(Resolve(env, env.Enemy.Entity, taunt: true).Positions, Is.EqualTo(new[] { 1 }));
            other.RemoveStatusInstance(off);
            Assert.That(Resolve(env, env.Enemy.Entity, taunt: true).Positions, Is.EqualTo(new[] { 2 }));
            Assert.That(random.IntCallCount, Is.Zero); Assert.That(random.FloatCallCount, Is.Zero);
        }

        [TestCase(false)] [TestCase(true)]
        public void ExistingSelfStatus_KeepsItsHost_WhileNewSelectionAndReviveCannotPickAbsent(bool enemy)
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var host = enemy ? env.Enemy.Entity : env.Ally.Entity;
            Leave(env, host);
            Assert.That(Resolve(env, host, "Self").IsValid, Is.False);
            var bound = Resolve(env, host, "Self", origin: host, bound: true);
            Assert.That(BattleTargetResolver.GetEntities(env.Manager, bound), Is.EqualTo(new[] { host }));
            Assert.That(Resolve(env, host, "AllyField").IsValid, Is.True);
            var dead = BattleTargetResolver.Resolve(env.Manager, host, host, "Allies", -1, 0,
                null, null, null, null, deadOnly: true);
            Assert.That(dead.IsValid, Is.False);
        }

        [Test]
        public void OffFieldOwner_StillProvidesExistingOverallShield_ButNotMemberCount()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var owner = env.Ally.Entity; var other = env.CreateAlly(2).Entity;
            var main = new StatusMainData { StatusID2 = "ST_TestOffFieldShield", StatusType = "OverallShield", MaxStack = 1 };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            var shieldStatus = owner.AddStatus(main.StatusID2, owner, 0, 2, 0, main);
            owner.AddShield(100, "None", 0, sourceStatus: shieldStatus);
            var off = Leave(env, owner);
            Assert.That(OverallShieldSystem.Members(env.Manager, BattleSide.Ally), Is.EqualTo(new[] { other }));
            Assert.That(OverallShieldSystem.GetProtectingShields(other), Does.Contain(owner.Shields[0]));
            Assert.That(OverallShieldSystem.HasAny(env.Manager), Is.True);
            owner.RemoveStatusInstance(off);
            Assert.That(OverallShieldSystem.Members(env.Manager, BattleSide.Ally).Count, Is.EqualTo(2));
            Assert.That(owner.HasStatusInstance(shieldStatus), Is.True);
        }

        [TestCase(false)] [TestCase(true)]
        public void TimersAndSelfActions_ContinueWhileAbsent_ExpiryReturnsSameUnit(bool enemy)
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            var host = enemy ? env.Enemy.Entity : env.Ally.Entity;
            env.ConfigureStatus("ST_OffFieldTickMarker");
            env.ConfigureStatus("ST_OffFieldTicker", actionType: "OnItsTurn", actionEffectID2: "STE_OffFieldTick");
            env.DataManager.StatusEffectDict["STE_OffFieldTick"] = new StatusEffectData {
                StatusEffectID = 49997001, StatusEffectID2 = "STE_OffFieldTick", EffectType = "ApplyStatus",
                TargetType = "Self", Param1 = "ST_OffFieldTickMarker", Duration = 0, AddInPhase = 6
            };
            var off = Leave(env, host, 2);
            var ticking = host.AddStatus("ST_OffFieldTicker", host, 3, 6, 0, env.DataManager.StatusMainDict["ST_OffFieldTicker"]);
            env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPostTurn);
            Assert.That(host.IsOffField, Is.True); Assert.That(off.RemainingPhaseCount, Is.EqualTo(1));
            Assert.That(host.HasStatus("ST_OffFieldTickMarker"), Is.True);
            Assert.That(ticking.RemainingPhaseCount, Is.EqualTo(2));
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPostTurn);
            Assert.That(host.IsOnField, Is.True);
            Assert.That(env.Manager.GetEntityByPosition(host.Side, host.SlotPosition), Is.SameAs(host));
            Assert.That(host.HasStatusInstance(ticking), Is.True);
        }

        [Test]
        public void AllAbsent_IsNotBattleEnd_AndSingleReturningAllyActivatesWithoutAP()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var returning = env.CreateAlly(2);
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            Leave(env, env.Ally.Entity); Leave(env, env.Enemy.Entity);
            var off = Leave(env, returning.Entity);
            Assert.That(env.Manager.GetActiveAlly(), Is.Null);
            env.Manager.CheckBattleEnd(); Assert.That(env.Manager.IsBattleOver, Is.False);
            int ap = env.Manager.APManager.CurrentAP;
            returning.Entity.RemoveStatusInstance(off);
            Assert.That(env.Manager.GetActiveAlly(), Is.SameAs(returning));
            Assert.That(env.Ally.IsActive, Is.False);
            Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(ap));
            Assert.That(env.Manager.IsBattleOver, Is.False);
        }

        [Test]
        public void NonActionPhaseReturn_WaitsForPhaseTwoAndDoesNotReplaceExistingActive()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); var returning = env.CreateAlly(2);
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            Leave(env, env.Ally.Entity); Leave(env, env.Enemy.Entity);
            var off = Leave(env, returning.Entity);
            env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPostTurn);
            returning.Entity.RemoveStatusInstance(off);
            Assert.That(env.Manager.GetActiveAlly(), Is.Null);
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            Assert.That(env.Manager.GetActiveAlly(), Is.SameAs(returning));
            env.Ally.Entity.RemoveStatus(PublicControlStates.OffField, -1);
            Assert.That(env.Manager.GetActiveAlly(), Is.SameAs(returning));
        }

        [UnityTest]
        public IEnumerator LeavingBetweenHits_SkipsLaterHitsButBoundStatusCanStillDamageHost()
            => HookTestEnv.AwaitTask(HitsAsync);
        private async Task HitsAsync()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var target = env.Enemy.Entity; int hits = 0;
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            { if (fact.Subject == target && ++hits == 1) Leave(env, target); }, 0);
            float hp = target.CurrentHP;
            await BattleExecution.Run(() => TrueDamageExecutor.ExecuteAsync(new[] { target },
                new[] { new HitData(1, 0), new HitData(1, 0) }, _ => 10, DamageSourceInfo.CreateUnknown(), "test", "Skill"));
            Assert.That(hits, Is.EqualTo(1)); Assert.That(target.CurrentHP, Is.EqualTo(hp - 10));
            await BattleExecution.Run(() => TrueDamageExecutor.ExecuteAsync(new[] { target },
                new[] { new HitData(1, 0) }, _ => 10, DamageSourceInfo.CreateUnknown(), "bound", "Skill", boundStatusHost: target));
            Assert.That(hits, Is.EqualTo(2)); Assert.That(target.CurrentHP, Is.EqualTo(hp - 20));
        }

        [Test]
        public void PhaseTwoExpiry_AutoActivatesOnlyReturningAlly_AndSkillButtonsRecover()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var returning = env.CreateAlly(2);
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            Leave(env, env.Ally.Entity); Leave(env, env.Enemy.Entity);
            Leave(env, returning.Entity, 1, 2);
            var root = new GameObject("OffFieldUiTest");
            try
            {
                var ui = root.AddComponent<UIBattleController>();
                var buttonObject = new GameObject("Skill", typeof(RectTransform));
                buttonObject.transform.SetParent(root.transform);
                ui.btnSkill = buttonObject.AddComponent<UnityEngine.UI.Button>();
                var endObject = new GameObject("EndTurn", typeof(RectTransform));
                endObject.transform.SetParent(root.transform);
                ui.btnEndTurn = endObject.AddComponent<UnityEngine.UI.Button>();
                var refresh = typeof(UIBattleController).GetMethod("RefreshButtons", BindingFlags.Instance | BindingFlags.NonPublic);
                refresh.Invoke(ui, new object[] { env.Manager });
                Assert.That(ui.btnSkill.gameObject.activeSelf, Is.False);
                Assert.That(ui.btnEndTurn.gameObject.activeSelf, Is.True);
                env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPostTurn);
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                Assert.That(env.Manager.GetActiveAlly(), Is.SameAs(returning));
                refresh.Invoke(ui, new object[] { env.Manager });
                Assert.That(ui.btnSkill.gameObject.activeSelf, Is.True);
                Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(100));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [TestCase(false)] [TestCase(true)]
        public void ExistingStatus_InvokedSkillCanExecuteWhileSourceIsAbsent(bool enemy)
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureStatus("ST_OffFieldCalledMarker");
            string skill;
            if (enemy)
            {
                env.ConfigureEnemyDamageSkill();
                skill = "SK_Enemy_BattleFlow";
                var effect = env.DataManager.EnemySkillEffectDict["SE_Enemy_BattleFlow"];
                effect.EffectType = "ApplyStatus"; effect.Param1 = "ST_OffFieldCalledMarker"; effect.TargetType = "Self";
            }
            else
            {
                skill = "SK_OffFieldCalled";
                env.ConfigureAllyAction(2, skill, "SE_OffFieldCalled", "Physical", null, apCost: 0,
                    effectType: "ApplyStatus", param1: "ST_OffFieldCalledMarker");
                env.DataManager.SkillEffectDict["SE_OffFieldCalled"].TargetType = "Self";
            }
            env.ConfigureStatus("ST_OffFieldSkillTicker", actionType: "OnItsTurn", actionEffectID2: skill);
            env.CreateMinimalBattle(); env.Manager.StartBattle();
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            var host = enemy ? env.Enemy.Entity : env.Ally.Entity;
            Leave(env, host);
            host.AddStatus("ST_OffFieldSkillTicker", host, 0, 6, 0, env.DataManager.StatusMainDict["ST_OffFieldSkillTicker"]);
            env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPostTurn);
            Assert.That(host.IsOffField, Is.True);
            Assert.That(host.HasStatus("ST_OffFieldCalledMarker"), Is.True);
        }

        [Test]
        public void StartOfAllyTurnWithoutActive_ReportsOnce_AndSwitchNavigationSkipsAbsent()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var other = env.CreateAlly(2); var available = env.CreateAlly(4);
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            Leave(env, other.Entity);
            Assert.That(env.Manager.FindNextLivingAllySlot(0, 1), Is.EqualTo(3));
            Assert.That(env.Manager.CanSwitchActiveAllyTo(1), Is.False);
            Leave(env, env.Ally.Entity); Leave(env, available.Entity); Leave(env, env.Enemy.Entity);
            var tips = new List<string>(); env.Manager.TipRequested += tips.Add;
            env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPostTurn);
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            Assert.That(tips, Is.EqualTo(new[] { BattleTipKeys.NoActiveCharacter }));
            Assert.That(env.Manager.GetActiveAlly(), Is.Null);
            Assert.That(env.Manager.IsBattleOver, Is.False);
        }

        [Test]
        public void OffFieldMarker_RejectsFieldAndTeamHosts()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); env.ConfigureStatus("OffField");
            var main = env.DataManager.StatusMainDict["OffField"];
            Assert.Throws<InvalidOperationException>(() => env.Ally.Entity.Position.AddStatus("OffField", env.Ally.Entity, 1, 2, 0, main));
            Assert.Throws<InvalidOperationException>(() => env.Manager.GetTeam(BattleSide.Ally).AddStatus("OffField", env.Ally.Entity, 1, 2, 0, main));
        }
    }
}
