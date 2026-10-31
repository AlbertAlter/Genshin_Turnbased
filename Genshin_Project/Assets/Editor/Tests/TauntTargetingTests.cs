using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class TauntTargetingTests
    {
        private static TargetResolutionRequest Request(int mode, int count = 1)
            => new TargetResolutionRequest
            {
                Side = BattleSide.Ally, Domain = BattleTargetDomain.Unit, MaxPosition = 4,
                TargetNumber = count, TargetConsecutive = mode, IsEligible = _ => true,
                Score = p => 10 - p, RandomIndex = _ => 0, TauntPositions = new List<int> { 4 }
            };

        [TestCase(0)] [TestCase(1)]
        public void OrdinarySingle_RequiresTaunterDespiteHigherOriginalScore(int mode)
        {
            var request = Request(mode); int calls = 0;
            request.RandomIndex = _ => { calls++; return 0; };
            Assert.That(TargetResolver.Resolve(request).Positions, Is.EqualTo(new[] { 4 }));
            Assert.That(calls, Is.Zero, "只有一个合法选择，不额外抽随机");
        }

        [Test]
        public void Mode1AdjacentBatch_ContainsTaunter_NotNecessarilyAtItsCenter()
        {
            var request = Request(1, 3);
            Assert.That(TargetResolver.Resolve(request).Positions, Is.EqualTo(new[] { 2, 3, 4 }));
            request.TauntPositions = new List<int> { 1 }; request.IsEligible = p => p == 1 || p == 3 || p == 4;
            request.TargetNumber = 2;
            Assert.That(TargetResolver.Resolve(request).Positions, Is.EqualTo(new[] { 1, 2 }),
                "必须包含嘲讽者，不能为覆盖两个活人而绕过嘲讽");
        }

        [Test]
        public void OrdinaryNonAdjacentBatch_IncludesTaunterOnce_ThenFillsOtherSlots()
        {
            var result = TargetResolver.Resolve(Request(0, 3));
            Assert.That(result.Positions, Is.EqualTo(new[] { 4, 1, 2 }));
        }

        [TestCase(1, new[] { 3 })]
        [TestCase(2, new[] { 3, 4 })]
        [TestCase(3, new[] { 3, 4, 1 })]
        public void NonAdjacentBatch_SelectsAllTauntersFirst(int count, int[] expected)
        {
            var request = Request(0, count);
            request.TauntPositions = new List<int> { 3, 4 };
            Assert.That(TargetResolver.Resolve(request).Positions, Is.EqualTo(expected));
            request.ValidateForcedTaunt = true;
            request.ForcedPositions = new List<int>(expected);
            Assert.That(TargetResolver.Resolve(request).IsValid, Is.True);
            request.ForcedPositions[0] = 1;
            Assert.That(TargetResolver.Resolve(request).Error, Is.EqualTo(BattleTipKeys.TauntTargetRestricted));
        }

        [Test]
        public void ManualContinuousWindow_CoversTaunter_AndStopsAtBothLimits()
        {
            var previous = BattleRandom.Source;
            try
            {
                var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
                var selector = new TargetSelector();
                selector.Init(BattleSide.Enemy, 3, 1, p => 10 - p, _ => true, tauntProvider: p => p == 2);
                Assert.That(selector.CurrentSelection, Is.EqualTo(new[] { 1, 2, 3 }));
                selector.MoveLeft(); Assert.That(selector.LastMoveBlockedByTaunt, Is.True);
                selector.MoveRight(); Assert.That(selector.CurrentSelection, Is.EqualTo(new[] { 2, 3, 4 }));
                Assert.That(selector.LastMoveBlockedByTaunt, Is.False);
                selector.MoveRight(); Assert.That(selector.LastMoveBlockedByTaunt, Is.True);
                Assert.That(random.IntCallCount, Is.EqualTo(1), "移动不重抽随机");
            }
            finally { BattleRandom.SetSource(previous); }
        }

        [Test]
        public void ManualNonAdjacent_ExhaustsTaunters_ThenAllowsOthers()
        {
            var previous = BattleRandom.Source;
            try
            {
                var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
                var selector = new TargetSelector(); var excluded = new List<int>();
                foreach (int expected in new[] { 3, 5, 1 })
                {
                    selector.Init(BattleSide.Enemy, 1, 0, p => 10 - p, _ => true,
                        excluded, tauntProvider: p => p == 3 || p == 5);
                    Assert.That(selector.CurrentSelection, Is.EqualTo(new[] { expected }));
                    excluded.Add(expected);
                }
                selector.MoveLeft(); Assert.That(selector.LastMoveBlockedByTaunt, Is.False);
                Assert.That(random.IntCallCount, Is.EqualTo(3));
            }
            finally { BattleRandom.SetSource(previous); }
        }

        [Test]
        public void ManualTaunt_RemovalAndAbsenceReleaseConstraint_AllModeResetsIt()
        {
            var previous = BattleRandom.Source;
            try
            {
                BattleRandom.SetSource(new SequenceBattleRandomSource());
                bool taunting = true, present = true;
                var selector = new TargetSelector();
                selector.Init(BattleSide.Enemy, 1, 0, p => 10 - p, p => p != 3 || present,
                    tauntProvider: p => p == 3 && taunting);
                Assert.That(selector.CurrentSelection, Is.EqualTo(new[] { 3 }));
                taunting = false; selector.MoveLeft();
                Assert.That(selector.CurrentSelection, Is.EqualTo(new[] { 2 }));
                taunting = true; Assert.That(selector.IsSelectionValid(), Is.True);
                Assert.That(selector.CurrentSelection, Is.EqualTo(new[] { 3 }));
                present = false; Assert.That(selector.IsSelectionValid(), Is.True);
                Assert.That(selector.CurrentSelection, Is.EqualTo(new[] { 1 }));
                selector.MoveLeft(); Assert.That(selector.LastMoveBlockedByTaunt, Is.False);
                selector.InitAll(BattleSide.Enemy, _ => 1, _ => true);
                selector.MoveRight(); Assert.That(selector.LastMoveBlockedByTaunt, Is.False);
            }
            finally { BattleRandom.SetSource(previous); }
        }

        [TestCase(2)] [TestCase(3)] [TestCase(4)]
        public void RandomSingle_IgnoresTaunt(int mode)
        {
            Assert.That(TargetResolver.Resolve(Request(mode)).Positions, Is.EqualTo(new[] { 1 }));
        }

        [TestCase(2)] [TestCase(3)] [TestCase(4)]
        public void RandomBatch_IgnoresTauntStartingWithItsFirstSelection(int mode)
        {
            var request = Request(mode, 2); int calls = 0;
            request.RandomIndex = _ => { calls++; return 0; };
            Assert.That(TargetResolver.Resolve(request).Positions,
                Is.EqualTo(mode == 3 ? new[] { 1, 1 } : new[] { 1, 2 }));
            Assert.That(calls, Is.EqualTo(mode == 2 ? 1 : 2));
        }

        [Test]
        public void RandomAdjacentBatch_PrioritizesCoverageWithoutRequiringTaunter()
        {
            var request = Request(2, 2);
            request.TauntPositions = new List<int> { 1 };
            request.IsEligible = p => p == 1 || p == 3 || p == 4;
            Assert.That(TargetResolver.Resolve(request).Positions, Is.EqualTo(new[] { 3, 4 }));
        }

        [Test]
        public void All_Fixed_Inherited_AndFieldTargets_AreNotRedirected()
        {
            var request = Request(2, -1);
            Assert.That(TargetResolver.Resolve(request).Positions, Is.EqualTo(new[] { 1, 2, 3, 4 }));
            request.TargetNumber = 1; request.FixedPositions = new List<int> { 1 };
            Assert.That(TargetResolver.Resolve(request).Positions, Is.EqualTo(new[] { 1 }));
            request.FixedPositions = null; request.TargetOverride = "0,0";
            request.PreviousPositions = new List<int> { 1, 2 };
            Assert.That(TargetResolver.Resolve(request).Positions, Is.EqualTo(new[] { 1, 2 }));
            request.TargetOverride = null; request.Domain = BattleTargetDomain.Field;
            Assert.That(TargetResolver.Resolve(request).Positions, Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void InvalidOrRemovedTaunter_DoesNotCreateAnIllegalTarget()
        {
            var request = Request(1); request.IsEligible = p => p != 4;
            Assert.That(TargetResolver.Resolve(request).Positions, Is.EqualTo(new[] { 1 }));
            request.TauntPositions.Clear(); request.IsEligible = _ => true;
            Assert.That(TargetResolver.Resolve(request).Positions, Is.EqualTo(new[] { 1 }));
        }

        [TestCase(false, 2, 1)] [TestCase(false, 2, 5)]
        [TestCase(false, 3, 1)] [TestCase(false, 3, 5)]
        [TestCase(false, 4, 1)] [TestCase(false, 4, 5)]
        [TestCase(true, 2, 1)] [TestCase(true, 2, 5)]
        [TestCase(true, 3, 1)] [TestCase(true, 3, 5)]
        [TestCase(true, 4, 1)] [TestCase(true, 4, 5)]
        [Timeout(7000)]
        public void CharacterSkillAndStatus_RandomModesIgnoreTauntRegardlessOfRepeatCount(
            bool statusEffect, int mode, int executeCount)
        {
            const string effectID = "SE_TauntRandom";
            const string statusID = "ST_TauntRandom";
            using var env = new BattleFlowTestEnv();
            if (statusEffect)
            {
                env.ConfigureStatus(statusID, actionType: "OnTrigger", actionEffectID2: effectID);
                env.DataManager.StatusEffectDict[effectID] = new StatusEffectData
                {
                    StatusEffectID2 = effectID, EffectType = "Damage", Element = "Physical", DamageType = "Skill",
                    Param1 = "0.01*TotalATK,0", TargetType = "Enemy", TargetSelect = "1",
                    TargetConsecutive = mode, ExecuteCount = executeCount
                };
            }
            else
            {
                env.ConfigureAllyAction(2, "SK_TauntRandom", effectID, "Physical", "0.01*TotalATK,0", 10);
                var effect = env.DataManager.SkillEffectDict[effectID];
                effect.TargetConsecutive = mode; effect.ExecuteCount = executeCount;
            }
            env.ConfigureStatus("Taunt"); env.CreateMinimalBattle();
            env.CreateEnemy(2); env.CreateEnemy(3); env.CreateEnemy(4);
            var taunter = env.CreateEnemy(5).Entity;
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            taunter.AddStatus("Taunt", taunter, 0, 6, 0, env.DataManager.StatusMainDict["Taunt"]);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            var positions = new List<int>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,
                (_, hit) => { if (hit.Source.SourceEffectID == effectID) positions.Add(hit.Subject.SlotPosition); }, 0);
            if (statusEffect)
            {
                var instance = env.Ally.Entity.AddStatus(statusID, env.Ally.Entity, 0, 2, 0, env.DataManager.StatusMainDict[statusID]);
                Assert.That(env.Ally.TryExecuteEventStatusAction(instance, env.DataManager.StatusActionDict[statusID][0],
                    env.Ally.Entity, new ScriptHookContext { Caster = env.Ally.Entity }), Is.True);
            }
            else Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            Assert.That(positions, Has.Count.EqualTo(executeCount));
            Assert.That(positions, Is.All.EqualTo(1), "固定随机序列选择1号，不因5号持有Taunt而重定向");
            Assert.That(random.IntCallCount, Is.EqualTo(executeCount), "每次执行正常选目标，不为嘲讽或执行次数额外抽随机");
        }

        [UnityTest]
        public IEnumerator EnemyAdjacentRandom_SingleExecution_IgnoresTaunt()
            => HookTestEnv.AwaitTask(() => AdjacentRandomAsync(1));

        [TestCase(0)] [TestCase(1)]
        public void CharacterAutomaticDamage_UsesEnemyTaunt(int mode)
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(2, "SK_TauntAuto", "SE_TauntAuto", "Physical", "0.01*TotalATK,0", 10);
            env.DataManager.SkillEffectDict["SE_TauntAuto"].TargetConsecutive = mode;
            env.ConfigureStatus("Taunt"); env.CreateMinimalBattle();
            var taunter = env.CreateEnemy(3).Entity;
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            taunter.AddStatus("Taunt", taunter, 0, 6, 0, env.DataManager.StatusMainDict["Taunt"]);
            BattleRandom.SetSource(new SequenceBattleRandomSource());
            var positions = new List<int>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,
                (_, hit) => { if (hit.Source.SourceEffectID == "SE_TauntAuto") positions.Add(hit.Subject.SlotPosition); }, 0);
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            Assert.That(positions, Is.EqualTo(new[] { 3 }));
        }

        [Test]
        public void PlayerInput_MultipleTauntersFirst_LimitEmitsKey_CancelClearsRestriction()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(2, "SK_TauntInput", "SE_TauntInput", "Physical", "0.01*TotalATK,0", 10);
            var effect = env.DataManager.SkillEffectDict["SE_TauntInput"];
            effect.TargetConsecutive = 0; effect.TargetNumber = 3;
            env.ConfigureStatus("Taunt"); env.CreateMinimalBattle();
            var first = env.CreateEnemy(3).Entity; var second = env.CreateEnemy(5).Entity;
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            first.AddStatus("Taunt", first, 0, 6, 0, env.DataManager.StatusMainDict["Taunt"]);
            second.AddStatus("Taunt", second, 0, 6, 0, env.DataManager.StatusMainDict["Taunt"]);
            BattleRandom.SetSource(new SequenceBattleRandomSource());
            var input = env.Manager.gameObject.AddComponent<BattleInputController>();
            var tips = new List<string>(); input.TipRequested += tips.Add;
            input.UIEnterSelectSkill(2);
            Assert.That(input.Selector.CurrentSelection, Is.EqualTo(new[] { 3 }));
            input.UIMoveSelection(-1);
            Assert.That(tips, Is.EqualTo(new[] { BattleTipKeys.TauntTargetRestricted }));
            input.UIConfirmSelection();
            Assert.That(input.Selector.CurrentSelection, Is.EqualTo(new[] { 5 }));
            input.UIConfirmSelection();
            Assert.That(input.Selector.CurrentSelection, Is.EqualTo(new[] { 1 }));
            tips.Clear(); input.UIMoveSelection(-1); Assert.That(tips, Is.Empty);
            input.UICancelSelection();
            input.UIEnterSelectSkill(2);
            Assert.That(input.Selector.CurrentSelection, Is.EqualTo(new[] { 3 }));
            Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(100), "选择/移动/取消不扣费");
        }

        [UnityTest]
        public IEnumerator EnemyAdjacentRandom_FiveExecutions_IgnoreTauntFromFirstGroup()
            => HookTestEnv.AwaitTask(() => AdjacentRandomAsync(5));
        private async Task AdjacentRandomAsync(int executeCount)
        {
            using var env = new BattleFlowTestEnv(); env.ConfigureEnemyDamageSkill(.01f);
            var effect = env.DataManager.EnemySkillEffectDict["SE_Enemy_BattleFlow"];
            effect.TargetConsecutive = 2; effect.TargetNumber = 3; effect.ExecuteCount = executeCount;
            env.DataManager.EnemySkillMainDict[990201].UsePerTurn = 0;
            env.ConfigureStatus("Taunt"); env.CreateMinimalBattle();
            env.CreateAlly(2); env.CreateAlly(3); var taunter = env.CreateAlly(4).Entity;
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            taunter.AddStatus("Taunt", taunter, 1, 6, 0, env.DataManager.StatusMainDict["Taunt"]);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            var positions = new List<int>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,
                (_, hit) => { if (hit.Source.SourceKind == ReactionSourceKind.EnemySkill) positions.Add(hit.Subject.SlotPosition); }, 0);
            await BattleExecution.Run(env.Enemy.TakeEnemyActionAsync);
            var expected = new List<int>();
            for (int i = 0; i < executeCount; i++) expected.AddRange(new[] { 1, 2, 3 });
            Assert.That(positions, Is.EqualTo(expected));
            Assert.That(random.IntCallCount, Is.EqualTo(1 + executeCount), "一次技能选择，每次执行独立选择相邻范围");
            Assert.That(random.FloatCallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator EnemyRealSkill_UsesCurrentTaunt_AndStopsUsingItAfterRemoval()
            => HookTestEnv.AwaitTask(EnemyRealSkillAsync);
        private async Task EnemyRealSkillAsync()
        {
            using var env = new BattleFlowTestEnv(); env.ConfigureEnemyDamageSkill(.01f);
            env.DataManager.EnemySkillEffectDict["SE_Enemy_BattleFlow"].TargetConsecutive = 1;
            env.DataManager.EnemySkillMainDict[990201].UsePerTurn = 0;
            env.ConfigureStatus("Taunt"); env.CreateMinimalBattle();
            var normal = env.CreateAlly(2, hp: 9000).Entity;
            env.Ally.Entity.HitWeight = 0f;
            env.CreateAlly(3).Entity.HitWeight = 0f;
            normal.HitWeight = 1f;
            var taunter = env.CreateAlly(4).Entity;
            taunter.HitWeight = 0f;
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            var mark = taunter.AddStatus("Taunt", taunter, 1, 6, 0, env.DataManager.StatusMainDict["Taunt"]);
            var targets = new List<BattleEntity>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,
                (_, hit) => { if (hit.Source.SourceKind == ReactionSourceKind.EnemySkill) targets.Add(hit.Subject); }, 0);
            await BattleExecution.Run(env.Enemy.TakeEnemyActionAsync);
            Assert.That(targets, Is.EqualTo(new[] { taunter }));
            taunter.RemoveStatusInstance(mark);
            await BattleExecution.Run(env.Enemy.TakeEnemyActionAsync);
            Assert.That(targets, Is.EqualTo(new[] { taunter, normal }));
            Assert.That(random.IntCallCount, Is.EqualTo(2), "两次技能选择各抽一次；两次目标均唯一，不额外抽取");
            Assert.That(random.FloatCallCount, Is.Zero);
        }
    }
}
