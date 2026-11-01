using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    // 使用内存配表和正式执行入口；不读取Charts，也不替用户启动Unity。
    public sealed class DilucScriptHooksTests
    {
        private static StatusMainData Main(BattleFlowTestEnv env, int id, string key,
            string part = null, string hook = null)
        {
            var main = new StatusMainData { StatusID = id, StatusID2 = key, StatusName = key,
                StatusType = part == null ? null : "Buff", MultiplierPart = part,
                ScriptHook = hook, MaxStack = 1, MaxCount = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[key] = main;
            return main;
        }
        private static void StatusEffect(BattleFlowTestEnv env, int id, string key, string type,
            string p1, string p2 = null, string hook = null)
            => env.DataManager.StatusEffectDict[key] = new StatusEffectData
            { StatusEffectID = id, StatusEffectID2 = key, EffectType = type, Param1 = p1,
                Param2 = p2, ScriptHook = hook, TargetType = "Self" };
        private static void SkillEffect(BattleFlowTestEnv env, int id, string key, int order,
            string type, string p1, string p2 = null, string p3 = null, int duration = 0, int phase = 0)
            => env.DataManager.SkillEffectDict[key] = new SkillEffectData
            { SkillEffectID = id, SkillEffectID2 = key, EffectIndex = order, EffectType = type,
                Param1 = p1, Param2 = p2, Param3 = p3, Duration = duration, AddInPhase = phase, TargetType = "Self" };
        private static BattleEntity Start(BattleFlowTestEnv env, int constellation = 0)
        {
            env.CreateMinimalBattle();
            var owner = env.Ally.Entity; owner.EntityID = 1015; owner.ConstellationLevel = constellation;
            owner.MaxEnergy = 40; owner.CurrentEnergy = 0; owner.CritRate = 0;
            BattleRandom.SetSource(new SequenceBattleRandomSource());
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            return owner;
        }
        private static void NextTurn(BattleFlowTestEnv env)
        {
            env.Manager.EndAllyTurn();
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
        }
        private static void ConfigureChain(BattleFlowTestEnv env)
        {
            for (int stage = 1; stage <= 3; stage++)
            {
                string prefix = "SE_Skill_Diluc" + stage;
                env.ConfigureAllyAction(2, "SK_Skill_Diluc" + stage, prefix, "Physical", "1*TotalATK,0",
                    apCost: 15, cooldown: stage == 1 ? 2 : 0);
                env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_2_Diluc{stage}"]
                    = env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_2"];
                // 未发生伤害也不能屏蔽回能及后面的控制效果。
                env.DataManager.SkillEffectDict[prefix].ScriptHook = "OnField(999999)";
                SkillEffect(env, 10150300 + stage * 10 + 1, prefix + "Energy", 1,
                    "GainEnergy", stage == 3 ? "3" : "2", "Based");
                env.DataManager.SkillEffectDict[prefix + "Energy"].Element = "Pyro";
                if (stage == 1)
                    SkillEffect(env, 10150312, prefix + "OpenChain", 2,
                        "ApplyStatus", DilucScriptHooks.ChainStatus, duration: 1, phase: 3);
                else
                    SkillEffect(env, 10150300 + stage * 10 + 2, prefix + "ClearC4", 2,
                        "RemoveStatus", "ST_Diluc_C4Ready");
                SkillEffect(env, 10150300 + stage * 10 + 3, prefix + "Next", 3, "ChangeControl",
                    "Skill", "SK_Skill_Diluc" + (stage == 3 ? 1 : stage + 1), "0");
                if (stage == 3)
                    SkillEffect(env, 10150334, prefix + "CloseChain", 4, "RemoveStatus", DilucScriptHooks.ChainStatus);
            }
            env.DataManager.SkillLevelDict.Remove($"{BattleFlowTestEnv.AllyID}_2");
            env.DataManager.CharacterAttributesDict[BattleFlowTestEnv.AllyID].ElementalSkillID = "SK_Skill_Diluc1";
            var chain = Main(env, 4101501, DilucScriptHooks.ChainStatus);
            Main(env, 4101510, "ST_Diluc_C4Ready");
            env.DataManager.StatusActionDict[chain.StatusID2] = new List<StatusActionData>
            { new StatusActionData { StatusID = chain.StatusID, StatusID2 = chain.StatusID2,
                ActionType = "OnEnd", Param1 = "SE_Diluc_SkillReset;SE_Diluc_ClearC4" } };
            SkillEffect(env, 10150901, "SE_Diluc_SkillReset", 0, "ChangeControl", "Skill", "SK_Skill_Diluc1", "0");
            SkillEffect(env, 10150902, "SE_Diluc_ClearC4", 1, "RemoveStatus", "ST_Diluc_C4Ready");
        }

        [Test]
        public void Chain_PlayerSelectionExit_PreservesSecondAndThirdStageBindings()
        {
            using var env = new BattleFlowTestEnv(); ConfigureChain(env); var owner = Start(env);
            var input = env.Manager.gameObject.AddComponent<BattleInputController>();
            for (int stage = 1; stage <= 3; stage++)
            {
                Assert.That(env.Ally.GetBoundSkillID(2), Is.EqualTo("SK_Skill_Diluc" + stage));
                Assert.That(env.Ally.GetBlockReason(2), Is.Null);
                input.UIEnterSelectSkill(2);
                Assert.That(input.IsSelecting, Is.True);
                if (stage == 2)
                {
                    input.UICancelSelection();
                    Assert.That(env.Ally.GetBoundSkillID(2), Is.EqualTo("SK_Skill_Diluc2"));
                    input.UIEnterSelectSkill(2);
                }
                input.UIConfirmSelection();
                Assert.That(input.IsSelecting, Is.False);
                Assert.That(env.Ally.GetBoundSkillID(2), Is.EqualTo("SK_Skill_Diluc" + (stage == 3 ? 1 : stage + 1)));
            }
            Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(55));
            Assert.That(owner.HasStatus(DilucScriptHooks.ChainStatus), Is.False);
            Assert.That(env.Ally.GetCooldownRemaining(2), Is.EqualTo(2));
        }

        [TestCase(1, 3f), TestCase(2, 6f), TestCase(3, 10.5f)]
        public void Chain_CompleteOrExpire_ReturnsFirstStageWithoutResettingRecordedCooldown(int stages, float energy)
        {
            using var env = new BattleFlowTestEnv(); ConfigureChain(env); var owner = Start(env);
            int hits = 0;
            using var observer = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, __) => hits++, 0);
            for (int stage = 1; stage <= stages; stage++)
            {
                Assert.That(env.Ally.GetBoundSkillID(2), Is.EqualTo("SK_Skill_Diluc" + stage));
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            }
            Assert.That(owner.CurrentEnergy, Is.EqualTo(energy));
            Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(100 - stages * 15));
            Assert.That(hits, Is.Zero, "回能不依赖命中，也没有为切回第一段重放技能");
            if (stages == 3)
            {
                Assert.That(owner.HasStatus(DilucScriptHooks.ChainStatus), Is.False);
                Assert.That(env.Ally.GetCooldownRemaining(2), Is.EqualTo(2));
            }
            else owner.AddStatus("ST_Diluc_C4Ready", owner, 0, 2, 0, env.DataManager.StatusMainDict["ST_Diluc_C4Ready"]);
            env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);
            Assert.That(env.Ally.GetBoundSkillID(2), Is.EqualTo("SK_Skill_Diluc1"));
            Assert.That(env.Ally.GetCooldownRemaining(2), Is.EqualTo(2), "阶段3只切回，不清第一段冷却");
            Assert.That(owner.HasStatus(DilucScriptHooks.ChainStatus), Is.False);
            Assert.That(owner.HasStatus("ST_Diluc_C4Ready"), Is.False);
            Assert.That(owner.CurrentEnergy, Is.EqualTo(energy));
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            Assert.That(env.Ally.GetCooldownRemaining(2), Is.EqualTo(1));
            Assert.That(env.Manager.UseSkillBySlot(0), Is.False);
        }

        [Test]
        public void C1_UsesStrictHalfOfFinalMaximumHP_PerTarget()
        {
            using var env = new BattleFlowTestEnv(); var owner = Start(env); var target = env.Enemy.Entity;
            var hp = Main(env, 4999901, "ST_Diluc_TestHP", "HPBonus");
            StatusEffect(env, 499990101, "STE_Diluc_TestHP", "Buff", "1");
            target.AddStatus(hp.StatusID2, target, 0, 2, 0, hp);
            Assert.That(target.GetFinalHP(), Is.EqualTo(2000f));
            target.CurrentHP = 1000;
            var context = new ScriptHookContext { Caster = owner, Target = target };
            Assert.That(ScriptHookEvaluator.Evaluate("Diluc_C1HP()", context), Is.False);
            target.CurrentHP = 1000.1f;
            Assert.That(ScriptHookEvaluator.Evaluate("Diluc_C1HP()", context), Is.True);
            context.Target = env.CreateEnemy(2).Entity; context.Target.CurrentHP = 400;
            Assert.That(ScriptHookEvaluator.Evaluate("Diluc_C1HP()", context), Is.False);
        }

        [UnityTest]
        public IEnumerator C4_RequiresTeammatePositiveActiveSkillPayment_WithActualPurposeAndSkillID()
            => HookTestEnv.AwaitTask(C4Async);
        private async Task C4Async()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(0, "SK_Teammate", "SE_Teammate", "Physical", "1*TotalATK,0", 10);
            var owner = Start(env); var teammate = env.CreateAlly(2); teammate.Entity.EntityID = 1014;
            var chainMain = Main(env, 4101501, DilucScriptHooks.ChainStatus);
            var chain = owner.AddStatus(chainMain.StatusID2, owner, 1, 3, 0, chainMain);
            var context = new ScriptHookContext { Caster = owner, Status = chain, StatusHost = owner };
            foreach (var kind in new[] { APSpendKind.Unspecified, APSpendKind.Switch, APSpendKind.Thaw, APSpendKind.Skill })
            {
                using var action = BattleEventHub.BeginActionExecution();
                context.Declaration = BattleEventHub.Publish(new APSpentEventData(teammate.Entity, 5, 95, kind,
                    kind == APSpendKind.Skill ? "SK_Teammate" : null));
                Assert.That(ScriptHookEvaluator.Evaluate("Diluc_C4OtherAP()", context), Is.EqualTo(kind == APSpendKind.Skill));
            }
            using (BattleEventHub.BeginActionExecution(false))
            {
                context.Declaration = BattleEventHub.Publish(new APSpentEventData(teammate.Entity, 5, 95, APSpendKind.Skill, "SK_Charge"));
                Assert.That(ScriptHookEvaluator.Evaluate("Diluc_C4OtherAP()", context), Is.False);
            }
            using (BattleEventHub.BeginActionExecution())
                foreach (var payer in new[] { owner, env.Enemy.Entity, teammate.Entity })
                {
                    context.Declaration = BattleEventHub.Publish(new APSpentEventData(payer,
                        payer == teammate.Entity ? 0 : 5, 95, APSpendKind.Skill, "SK_Test"));
                    Assert.That(ScriptHookEvaluator.Evaluate("Diluc_C4OtherAP()", context), Is.False);
                }
            APSpentEventData actual = null; long actionID = 0;
            using var observer = BattleEventHub.Subscribe<APSpentEventData>(BattleEventKind.APSpent, (h, payment) =>
            { if (payment.Subject == teammate.Entity) { actual = payment; actionID = h.ActionExecutionId; } }, 0);
            await BattleExecution.Run(() => teammate.ExecuteSkillByIDAsync("SK_Teammate", 0, true));
            Assert.That(actual, Is.Not.Null); Assert.That(actual.SpendKind, Is.EqualTo(APSpendKind.Skill));
            Assert.That(actual.SourceSkillID, Is.EqualTo("SK_Teammate")); Assert.That(actionID, Is.Not.Zero);
            Assert.That(new APSpentEventData(owner, 1, 99).SpendKind, Is.EqualTo(APSpendKind.Unspecified));
            Assert.Throws<ArgumentException>(() => new APSpentEventData(owner, 1, 99, APSpendKind.Skill));
        }

        [UnityTest]
        public IEnumerator C4_RealTeammatePayment_GrantsNextStageBonus_ThenClearsIt()
            => HookTestEnv.AwaitTask(C4BonusAsync);
        private async Task C4BonusAsync()
        {
            using var env = new BattleFlowTestEnv(); ConfigureChain(env);
            env.ConfigureAllyAction(0, "SK_Teammate", "SE_Teammate", "Physical", "1*TotalATK,0", 10);
            env.DataManager.SkillEffectDict["SE_Skill_Diluc2"].ScriptHook = null;
            var ready = env.DataManager.StatusMainDict["ST_Diluc_C4Ready"];
            ready.StatusType = "Buff"; ready.MultiplierPart = "DMGBonus"; ready.ApplyDamageType = "Skill";
            ready.ApplyString = "SK_Skill_Diluc2;SK_Skill_Diluc3";
            StatusEffect(env, 410151001, "STE_Diluc_C4Damage", "Buff", ".4");
            StatusEffect(env, 410150101, "STE_Diluc_C4Ready", "ApplyStatus", ready.StatusID2);
            env.DataManager.StatusActionDict[DilucScriptHooks.ChainStatus].Add(new StatusActionData
            { StatusID = 4101501, StatusID2 = DilucScriptHooks.ChainStatus, ActionType = "OnTrigger",
                Param1 = "STE_Diluc_C4Ready", ScriptHook = "Check(1015_C4);Diluc_C4OtherAP()", APUsed = "All" });
            var owner = Start(env, 4); var teammate = env.CreateAlly(2); teammate.Entity.EntityID = 1014;
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            Assert.That(owner.HasStatus(ready.StatusID2), Is.False);
            await BattleExecution.Run(() => teammate.ExecuteSkillByIDAsync("SK_Teammate", 0, true));
            Assert.That(owner.HasStatus(ready.StatusID2), Is.True);
            float damage = 0;
            using var observer = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
            { if (hit.Source.SourceEffectID == "SE_Skill_Diluc2") damage += hit.ActualHPDamage; }, 0);
            await BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_Skill_Diluc2", 2, true));
            Assert.That(damage, Is.EqualTo(70f).Within(.01f));
            Assert.That(owner.HasStatus(ready.StatusID2), Is.False);
            Assert.That(env.Ally.GetBoundSkillID(2), Is.EqualTo("SK_Skill_Diluc3"));
        }

        [UnityTest]
        public IEnumerator C2_TwoTriggersPerTurn_ThreeIndependentCopies_ReplaceOnlyOldest()
            => HookTestEnv.AwaitTask(C2Async);
        private async Task C2Async()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(0, "SK_Normal_Diluc", "SE_Normal_Diluc", "Physical", "1*TotalATK,0", 25);
            var watch = Main(env, 4101506, "ST_Diluc_C2Watch");
            var buff = Main(env, 4101507, "ST_Diluc_C2Stack", "ATKBonus"); buff.MaxStack = buff.MaxCount = 3;
            StatusEffect(env, 410150701, "STE_Diluc_C2ATK", "Buff", ".1");
            StatusEffect(env, 410150702, "STE_Diluc_C2AP", "APCost", "-1", "Normal;Heavy");
            StatusEffect(env, 410150601, "STE_Diluc_C2Grant", "ApplyStatus", buff.StatusID2);
            var grant = env.DataManager.StatusEffectDict["STE_Diluc_C2Grant"]; grant.Duration = 3; grant.AddInPhase = 2;
            env.DataManager.StatusActionDict[watch.StatusID2] = new List<StatusActionData>
            { new StatusActionData { StatusID = watch.StatusID, StatusID2 = watch.StatusID2, ActionType = "OnTrigger",
                OnDamage = "All", Param1 = "STE_Diluc_C2Grant", MaxTimePerTurn = 2 } };
            var owner = Start(env, 2); owner.AddStatus(watch.StatusID2, owner, 0, 2, 0, watch);
            var source = DamageSourceInfo.Create(env.Enemy.Entity, ReactionSourceKind.EnemySkill, "SK_Enemy", "SE_Enemy");
            Func<Task> hurt = () => BattleExecution.Run(async () => { owner.TakeDamage(10f, source); await BattleExecution.Flush(); });
            await hurt(); await hurt(); await hurt();
            var first = owner.GetStatusInstances(buff.StatusID2);
            Assert.That(first, Has.Count.EqualTo(2)); Assert.That(env.Ally.GetBoundSkillAPCost(0), Is.EqualTo(23));
            Assert.That(owner.GetFinalATK("", "", -1, ""), Is.EqualTo(120f).Within(.01f));
            NextTurn(env); await hurt();
            var third = owner.GetStatusInstances(buff.StatusID2)[2];
            Assert.That(first[1].RemainingPhaseCount, Is.LessThan(third.RemainingPhaseCount));
            await hurt();
            var current = owner.GetStatusInstances(buff.StatusID2);
            Assert.That(current, Has.Count.EqualTo(3)); Assert.That(first[0].IsActive, Is.False);
            Assert.That(current, Does.Contain(first[1])); Assert.That(current, Does.Contain(third));
            Assert.That(owner.GetFinalATK("", "", -1, ""), Is.EqualTo(130f).Within(.01f));
            Assert.That(env.Ally.GetBoundSkillAPCost(0), Is.EqualTo(22));
            foreach (var status in current) Assert.That(status.StackCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Dawn_RightToLeftEachOriginalTargetOnce_FlushesBeforeNextTarget()
            => HookTestEnv.AwaitTask(DawnAsync);
        private async Task DawnAsync()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(3, "SK_Burst_Diluc", DilucScriptHooks.BirdEffect, "Physical", "1*TotalATK,0", 0);
            var bird = env.DataManager.SkillEffectDict[DilucScriptHooks.BirdEffect];
            bird.TargetOverride = "Diluc_DawnTargets()"; bird.ExecuteCountHandler = "Diluc_DawnCount()";
            bird.ScriptHook = "Diluc_DawnHasTarget()";
            var owner = Start(env); var middle = env.CreateEnemy(2).Entity; env.CreateEnemy(3);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            var order = new List<int>(); var executions = new HashSet<long>();
            using var observer = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (header, damage) =>
            {
                if (damage.Source.SourceEffectID != DilucScriptHooks.BirdEffect) return;
                order.Add(damage.Subject.SlotPosition); executions.Add(header.EffectExecutionId);
                // 前一个目标的后续响应让原位置2失效；不能继续命中它或新占位者。
                if (damage.Subject.SlotPosition == 1)
                    BattleExecution.Enqueue(() => { middle.CurrentHP = 0; return Task.CompletedTask; });
            }, 0);
            Assert.That(BattleSpecialRules.TryResolveTargets("Diluc_DawnTargets()", env.Manager, owner, out var preview), Is.True);
            Assert.That(preview, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(BattleSpecialRules.TryResolveTargets("Diluc_DawnTargets()", env.Manager, owner, out var secondPreview), Is.True);
            Assert.That(secondPreview, Is.EqualTo(preview), "预览不推进目标迭代器");
            await BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_Burst_Diluc", 3, true));
            Assert.That(order, Is.EqualTo(new[] { 1, 3 })); Assert.That(executions.Count, Is.EqualTo(1));
            Assert.That(random.FloatCallCount, Is.EqualTo(2)); Assert.That(random.IntCallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Dawn_ThreeSequentialBirdTargets_ThenTwoCompleteExplosionHits()
            => HookTestEnv.AwaitTask(DawnFullAsync);
        private async Task DawnFullAsync()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(3, "SK_Burst_Diluc", DilucScriptHooks.BirdEffect, "Pyro", ".6*TotalATK,2", 0, 3);
            env.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID][0].EnergyUsed = 40;
            var bird = env.DataManager.SkillEffectDict[DilucScriptHooks.BirdEffect];
            bird.TargetOverride = "Diluc_DawnTargets()"; bird.ExecuteCountHandler = "Diluc_DawnCount()";
            bird.ScriptHook = "Diluc_DawnHasTarget()";
            SkillEffect(env, 10150402, "SE_Burst_DilucExplosion", 1, "Damage", null);
            var explosion = env.DataManager.SkillEffectDict["SE_Burst_DilucExplosion"];
            explosion.Element = "Pyro"; explosion.DamageType = "Burst"; explosion.TargetType = "Enemy"; explosion.TargetNumber = -1;
            env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_3_Explosion"] = new Dictionary<int, SkillLevelData>
            { [1] = new SkillLevelData { CharacterID = BattleFlowTestEnv.AllyID, SkillType = 3, SkillLevel = 1,
                ParamID = explosion.SkillEffectID2, Hits1 = "2.04*TotalATK,2", Hits2 = "2.04*TotalATK,2" } };
            var owner = Start(env); env.CreateEnemy(2); env.CreateEnemy(3); owner.CurrentEnergy = 40;
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            var order = new List<string>(); var explosionHits = new HashSet<long>();
            using var observer = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (header, hit) =>
            {
                if (hit.Source.SourceEntity != owner) return;
                bool isBird = hit.Source.SourceEffectID == DilucScriptHooks.BirdEffect;
                order.Add((isBird ? "bird" : "explosion") + hit.Subject.SlotPosition);
                Assert.That(hit.ActualHPDamage, Is.EqualTo(isBird ? 30f : 102f).Within(.01f));
                Assert.That(hit.Element, Is.EqualTo("Pyro")); Assert.That(hit.ElementAmount, Is.EqualTo(2f));
                Assert.That(hit.DamageType, Is.EqualTo("Burst"));
                if (!isBird) explosionHits.Add(header.HitId);
            }, 0);
            await BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_Burst_Diluc", 3, true));
            Assert.That(order, Is.EqualTo(new[] { "bird1", "bird2", "bird3", "explosion1", "explosion2", "explosion3",
                "explosion1", "explosion2", "explosion3" }));
            Assert.That(explosionHits.Count, Is.EqualTo(2)); Assert.That(owner.CurrentEnergy, Is.Zero);
            Assert.That(env.Ally.GetCooldownRemaining(3), Is.EqualTo(3));
            Assert.That(random.FloatCallCount, Is.EqualTo(9)); Assert.That(random.IntCallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Infusion_UsesEnchantWithCurrentLevels_OverridesPhysicalZeroGauge_AndKeepsBothButtons()
            => HookTestEnv.AwaitTask(InfusionAsync);
        private async Task InfusionAsync()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(0, "SK_Normal_Diluc", "SE_Normal_Diluc", "Physical", "1.3399*TotalATK,0", 25);
            env.ConfigureAllyAction(1, "SK_Heavy_Diluc", "SE_Heavy_Diluc", "Physical", ".688*TotalATK,0", 35);
            env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_0"][11] = new SkillLevelData
            { CharacterID = BattleFlowTestEnv.AllyID, SkillType = 0, SkillLevel = 11, ParamID = "SE_Normal_Diluc", Hits1 = "2.8628" };
            env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_1"][11] = new SkillLevelData
            { CharacterID = BattleFlowTestEnv.AllyID, SkillType = 1, SkillLevel = 11, ParamID = "SE_Heavy_Diluc", Hits1 = "1.47" };
            env.DataManager.SkillEffectDict["SE_Heavy_Diluc"].TargetNumber = 3;
            var attr = env.DataManager.CharacterAttributesDict[BattleFlowTestEnv.AllyID];
            attr.NormalAttackID = "SK_Normal_Diluc"; attr.HeavyAttackID = "SK_Heavy_Diluc";
            var infusion = Main(env, 4101502, "ST_Diluc_PyroInfusion");
            infusion.StatusType = "Enchant";
            env.ConfigureAllyAction(3, "SK_Burst_Diluc", "SE_Burst_DilucInfusion", "Pyro", null, 0,
                effectType: "ApplyStatus", param1: infusion.StatusID2, duration: 2, targetType: "Self");
            var application = env.DataManager.SkillEffectDict["SE_Burst_DilucInfusion"];
            application.Param2 = "1"; application.Param3 = "0"; application.AddInPhase = 2;
            var owner = Start(env); env.CreateEnemy(2); env.CreateEnemy(3);
            env.Ally.SkillLevels[0] = env.Ally.SkillLevels[1] = 11;
            await BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_Burst_Diluc", 3, true));
            var status = owner.GetStatus(infusion.StatusID2);
            Assert.That(status.Enchant.Element, Is.EqualTo("Pyro"));
            Assert.That(status.Enchant.ElementAmount, Is.EqualTo(1f));
            Assert.That(status.Enchant.CanBeOverridden, Is.True);
            Assert.That(env.Ally.GetBoundSkillID(0), Is.EqualTo("SK_Normal_Diluc"));
            Assert.That(env.Ally.GetBoundSkillID(1), Is.EqualTo("SK_Heavy_Diluc"));
            Assert.That(env.Ally.GetBoundSkillAPCost(0), Is.EqualTo(25)); Assert.That(env.Ally.GetBoundSkillAPCost(1), Is.EqualTo(35));
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random); int hits = 0;
            using var observer = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
            {
                if (hit.Source.SourceEntity != owner) return;
                hits++; Assert.That(hit.ElementAmount, Is.EqualTo(hits <= 4 ? 1f : 0f));
                Assert.That(hit.Element, Is.EqualTo(hits <= 4 ? "Pyro" : "Physical"));
                Assert.That(hit.Source.SourceEffectID, Is.EqualTo(hit.DamageType == "Normal" ? "SE_Normal_Diluc" : "SE_Heavy_Diluc"));
                Assert.That(hit.ActualHPDamage, Is.EqualTo(hit.DamageType == "Normal" ? 143.14f : 73.5f).Within(.01f));
            }, 0);
            env.Ally.ForcedTargetPositions.Add(1);
            await BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_Normal_Diluc", 0, true));
            env.Ally.ForcedTargetPositions.AddRange(new[] { 1, 2, 3 });
            await BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_Heavy_Diluc", 1, true));
            Assert.That(hits, Is.EqualTo(4)); Assert.That(random.FloatCallCount, Is.EqualTo(4)); Assert.That(random.IntCallCount, Is.Zero);
            owner.RemoveStatus(infusion.StatusID2, -1);
            Assert.That(env.Ally.GetBoundSkillID(0), Is.EqualTo("SK_Normal_Diluc"));
            Assert.That(env.Ally.GetBoundSkillID(1), Is.EqualTo("SK_Heavy_Diluc"));
            Assert.That(EnchantStatusSystem.GetActive(owner), Is.Null);
            env.Ally.ForcedTargetPositions.Add(1);
            await BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_Normal_Diluc", 0, true));
            Assert.That(hits, Is.EqualTo(5)); Assert.That(random.FloatCallCount, Is.EqualTo(5));
        }

        private static StatusMainData ConfigureC6(BattleFlowTestEnv env)
        {
            var main = Main(env, 4101511, DilucScriptHooks.C6Status, "DMGBonus", "Diluc_C6Damage()");
            main.ApplyDamageType = "Normal";
            StatusEffect(env, 410151101, "STE_Diluc_C6Damage", "Buff", ".3");
            StatusEffect(env, 410151102, "STE_Diluc_C6AP", "APCost", "-10", "Normal", "Diluc_C6AP()");
            StatusEffect(env, 410151103, "STE_Diluc_C6Remove", "RemoveStatus", DilucScriptHooks.C6Status);
            env.DataManager.StatusActionDict[main.StatusID2] = new List<StatusActionData>
            { new StatusActionData { StatusID = main.StatusID, StatusID2 = main.StatusID2, ActionType = "OnTrigger",
                ScriptHook = "Diluc_C6Spent()", Param1 = "STE_Diluc_C6Remove" } };
            return main;
        }

        [UnityTest]
        public IEnumerator C6_TwoWholeNormalEffects_KeepAllHitsTargetsRepeats_ThenRemove()
            => HookTestEnv.AwaitTask(C6Async);
        private async Task C6Async()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(0, "SK_Normal_Diluc", "SE_Normal_Diluc", "Physical", "1*TotalATK,0", 25);
            env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_0"][1].Hits2 = "1*TotalATK,0";
            env.DataManager.SkillEffectDict["SE_Normal_Diluc"].TargetNumber = -1;
            env.DataManager.SkillEffectDict["SE_Normal_Diluc"].ExecuteCount = 2;
            // 费用看按钮，伤害看DamageType，不得由ActionType决定六命伤害预算。
            env.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID][0].ActionType = "Skill";
            var main = ConfigureC6(env); var owner = Start(env, 6); env.CreateEnemy(2);
            owner.AddStatus(main.StatusID2, owner, 1, 2, 0, main);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            var values = new List<float>(); int invocation = 0;
            using var observer = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
            {
                if (hit.Source.SourceEffectID != "SE_Normal_Diluc") return;
                values.Add(hit.ActualHPDamage);
                Assert.That(owner.HasStatus(main.StatusID2), Is.EqualTo(invocation < 2),
                    "第二次全部hit和目标结束前不能提前移除");
            }, 0);
            for (; invocation < 3; invocation++)
            {
                Assert.That(env.Ally.GetBoundSkillAPCost(0), Is.EqualTo(invocation < 2 ? 15 : 25));
                await BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_Normal_Diluc", 0, true));
                Assert.That(owner.HasStatus(main.StatusID2), Is.EqualTo(invocation == 0));
            }
            Assert.That(values.Count, Is.EqualTo(24));
            for (int i = 0; i < values.Count; i++) Assert.That(values[i], Is.EqualTo(i < 16 ? 65f : 50f).Within(.01f));
            Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(45));
            Assert.That(random.FloatCallCount, Is.EqualTo(24)); Assert.That(random.IntCallCount, Is.Zero);
        }

        [Test]
        public void C6_UncommittedInvocationRefunds_AndNestedThirdCannotBorrowReservedUse()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(0, "SK_Normal_Diluc", "SE_Normal_Diluc", "Physical", "1*TotalATK,0", 25);
            var main = ConfigureC6(env); var owner = Start(env, 6);
            var status = owner.AddStatus(main.StatusID2, owner, 1, 2, 0, main);
            var context = new ScriptHookContext { Caster = owner, Status = status, HitEffectID = "SE_Normal_Diluc" };
            using (BattleSpecialRules.BeginEffectInvocation(owner, "SE_Normal_Diluc", 1))
            {
                Assert.That(ScriptHookEvaluator.Evaluate("Diluc_C6Damage()", context), Is.True);
                using (BattleSpecialRules.BeginEffectInvocation(owner, "SE_Normal_Diluc", 2))
                {
                    Assert.That(ScriptHookEvaluator.Evaluate("Diluc_C6Damage()", context), Is.True);
                    using (BattleSpecialRules.BeginEffectInvocation(owner, "SE_Normal_Diluc", 3))
                        Assert.That(ScriptHookEvaluator.Evaluate("Diluc_C6Damage()", context), Is.False);
                }
                Assert.That(ScriptHookEvaluator.Evaluate("Diluc_C6Damage()", context), Is.True);
            }
            Assert.That(env.Ally.GetBoundSkillAPCost(0), Is.EqualTo(15), "没有实际命中会归还两次预留");
            var refreshed = owner.AddStatus(main.StatusID2, owner, 1, 2, 0, main);
            Assert.That(refreshed, Is.Not.SameAs(status)); Assert.That(owner.HasStatusInstance(status), Is.False);
            Assert.That(env.Ally.GetBoundSkillAPCost(0), Is.EqualTo(15));
        }
    }
}
