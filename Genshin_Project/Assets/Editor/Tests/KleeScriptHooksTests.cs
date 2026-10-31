using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    // 内存配表复用正式技能、hit、状态、场地计时与响应入口，不读取策划源目录。
    public sealed class KleeScriptHooksTests
    {
        private static BattleEntity Start(BattleFlowTestEnv env)
        {
            env.CreateMinimalBattle(); env.Ally.Entity.EntityID = 1016;
            env.Ally.Entity.CritRate = 0; env.Ally.Entity.MaxEnergy = 100; env.Ally.Entity.CurrentEnergy = 0;
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            return env.Ally.Entity;
        }
        private static StatusMainData Main(BattleFlowTestEnv env, int id, string key, string part = null)
        {
            var main = new StatusMainData { StatusID = id, StatusID2 = key, StatusName = key,
                MaxStack = 1, MaxCount = 1, WhenMax = "0", MultiplierPart = part };
            env.DataManager.StatusMainDict[key] = main; return main;
        }
        private static StatusEffectData Effect(BattleFlowTestEnv env, int id, string key, string type, string param)
        {
            var effect = new StatusEffectData { StatusEffectID = id, StatusEffectID2 = key,
                EffectType = type, Param1 = param, TargetType = "Self" };
            env.DataManager.StatusEffectDict[key] = effect; return effect;
        }
        private static void Watch(BattleFlowTestEnv env, BattleEntity owner, int id, string key,
            string param, string hook, string hit = null, string damage = null)
        {
            var main = Main(env, id, key);
            env.DataManager.StatusActionDict[key] = new List<StatusActionData>
            { new StatusActionData { StatusID = id, StatusID2 = key, ActionType = "OnTrigger", Param1 = param,
                ScriptHook = hook, OutgoingHit = hit, OutgoingDamage = damage, MaxTimePerHit = 1 } };
            owner.AddStatus(key, owner, 0, 2, 0, main);
        }
        private static StatusMainData Sparks(BattleFlowTestEnv env)
        {
            var main = Main(env, 4101606, KleeSparkCount.ID, "DMGBonus"); main.StatusType = "Count"; main.ApplyDamageType = "Heavy";
            Effect(env, 410160601, "STE_Klee_TestSparkDamage", "Buff", "0.5").ScriptHook = "Klee_SparkDamage()";
            return main;
        }

        [Test]
        public void NormalMultiTarget_RollsSparkOncePerHit_NotOncePerEnemy()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(0, "SK_Normal_Klee", "SE_Normal_Klee", "Physical", "0.1*TotalATK,0");
            env.DataManager.SkillEffectDict["SE_Normal_Klee"].TargetNumber = -1;
            var owner = Start(env); env.CreateEnemy(2); env.CreateEnemy(3); var sparks = Sparks(env);
            Effect(env, 410160501, "STE_Klee_TestGain", "ApplyStatus", KleeSparkCount.ID);
            Watch(env, owner, 4101605, "ST_Klee_T1", "STE_Klee_TestGain", "Klee_SparkHit();Chance(0.5)", damage: "All");
            var random = new SequenceBattleRandomSource(new[] { .9f, .9f, .9f, .1f }); BattleRandom.SetSource(random);
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            Assert.That(owner.GetStatus(sparks.StatusID2).RuntimeIntValue, Is.EqualTo(1));
            Assert.That(random.FloatCallCount, Is.EqualTo(4), "三次暴击判定与一次火花判定");
            Assert.That(random.IntCallCount, Is.Zero);
        }

        [Test]
        public void C1_SelectsOneActualHitEnemy_AndDoesNotRecurse_ThenRefreshesAttackBuff()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(0, "SK_Normal_Klee", "SE_Normal_Klee", "Physical", "0.1*TotalATK,0");
            env.DataManager.SkillEffectDict["SE_Normal_Klee"].TargetNumber = -1;
            var owner = Start(env); env.CreateEnemy(2); env.CreateEnemy(3);
            var buff = Main(env, 4101609, "ST_Klee_C1ATK", "ATKBonus");
            Effect(env, 410160901, "STE_Klee_TestAttack", "Buff", "0.6");
            var extra = Effect(env, 410160801, KleeScriptHooks.C1Damage, "Damage", "0.2*TotalATK,0");
            extra.Element = "Physical"; extra.DamageType = "Burst"; extra.TargetType = "Enemy";
            extra.TargetSelect = "1"; extra.TargetConsecutive = 4; extra.TargetOverride = "Klee_HitTargets()";
            var gain = Effect(env, 410160802, "STE_Klee_TestC1Apply", "ApplyStatus", buff.StatusID2);
            gain.Duration = 3; gain.AddInPhase = 2; gain.ScriptHook = "Klee_C1Committed()";
            Watch(env, owner, 4101608, "ST_Klee_C1", KleeScriptHooks.C1Damage + ";STE_Klee_TestC1Apply", "Klee_C1Hit();Chance(0.5)", hit: "All");
            int count = 0, slot = 0;
            using var hit = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            { if (fact.Source.SourceEffectID == KleeScriptHooks.C1Damage) { count++; slot = fact.Subject.SlotPosition; } }, 0);
            var random = new SequenceBattleRandomSource(new[] { .9f, .9f, .9f, .1f, .9f }, new[] { 1 }); BattleRandom.SetSource(random);
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            Assert.That(count, Is.EqualTo(1)); Assert.That(slot, Is.EqualTo(2));
            Assert.That(random.FloatCallCount, Is.EqualTo(5)); Assert.That(random.IntCallCount, Is.EqualTo(1));
            Assert.That(owner.GetStatusInstances(buff.StatusID2), Has.Count.EqualTo(1));
            Assert.That(owner.GetStatus(buff.StatusID2).RemainingPhaseCount, Is.EqualTo(3));
        }

        [Test]
        public void HeavyCritical_ThreeTargetsGrantOnlyTwoEnergyToEachPartyMember()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(1, "SK_Heavy_Klee", "SE_Heavy_Klee", "Physical", "0.1*TotalATK,0");
            env.DataManager.SkillEffectDict["SE_Heavy_Klee"].TargetNumber = -1;
            var owner = Start(env); owner.CritRate = 1; env.CreateEnemy(2); env.CreateEnemy(3);
            var ally = env.CreateAlly(2).Entity; ally.MaxEnergy = 100; ally.CurrentEnergy = 0;
            var energy = Effect(env, 410160701, "STE_Klee_TestEnergy", "GainEnergy", "2");
            energy.Param2 = "Flat"; energy.TargetType = "Allies"; energy.TargetSelect = "-1";
            Watch(env, owner, 4101607, "ST_Klee_T2", energy.StatusEffectID2, "Klee_HeavyCritical()", hit: "Heavy");
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            Assert.That(env.Manager.UseHeavyAttackBySlot(0), Is.True);
            Assert.That(owner.CurrentEnergy, Is.EqualTo(2f)); Assert.That(ally.CurrentEnergy, Is.EqualTo(2f));
            Assert.That(random.FloatCallCount, Is.EqualTo(3)); Assert.That(random.IntCallCount, Is.Zero);
        }

        [Test]
        public void Spark_WholeHeavyGetsBonusAfterConsumption_AndFailedPaymentDoesNotConsume()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(1, "SK_Heavy_Klee", "SE_Heavy_Klee", "Physical", "0.1*TotalATK,0", apCost: 35);
            env.DataManager.SkillEffectDict["SE_Heavy_Klee"].TargetNumber = -1;
            env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_1"][1].Hits2 = "0.1*TotalATK,0";
            var owner = Start(env); env.CreateEnemy(2); var spark = Sparks(env);
            var ap = Main(env, 4101605, "ST_Klee_T1");
            var cost = Effect(env, 410160502, "STE_Klee_TestAP", "APCost", "-15"); cost.Param2 = "Heavy"; cost.ScriptHook = "Klee_SparkAP()";
            owner.AddStatus(ap.StatusID2, owner, 0, 2, 0, ap);
            owner.AddStatus(spark.StatusID2, owner, 0, 2, 0, spark);
            var damage = new List<float>();
            using var observer = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            { if (fact.Source.SourceEffectID == "SE_Heavy_Klee") damage.Add(fact.DamageBeforeShield); }, 0);
            var random = new SequenceBattleRandomSource(new[] { .9f, .9f, .9f, .9f, .9f, .9f, .9f, .9f }); BattleRandom.SetSource(random);
            Assert.That(env.Manager.UseHeavyAttackBySlot(0), Is.True);
            Assert.That(owner.GetStatus(spark.StatusID2).RuntimeIntValue, Is.Zero);
            Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(80));
            Assert.That(env.Manager.UseHeavyAttackBySlot(0), Is.True);
            Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(45)); Assert.That(damage, Has.Count.EqualTo(8));
            for (int i = 0; i < 4; i++) Assert.That(damage[i], Is.EqualTo(damage[i + 4] * 1.5f).Within(.001f));
            Assert.That(random.FloatCallCount, Is.EqualTo(8));
            owner.AddStatus(spark.StatusID2, owner, 0, 2, 0, spark);
            env.Manager.APManager.ConsumeAP(45);
            Assert.That(env.Manager.UseHeavyAttackBySlot(0), Is.False);
            Assert.That(owner.GetStatus(spark.StatusID2).RuntimeIntValue, Is.EqualTo(1));
            for (int i = 0; i < 5; i++) owner.AddStatus(spark.StatusID2, owner, 0, 2, 0, spark);
            Assert.That(owner.GetStatus(spark.StatusID2).RuntimeIntValue, Is.EqualTo(3));
        }

        [Test]
        public void Skill_OnlyFirstLandedBounceProducesEnergy_EvenWhenTheFirstBounceMisses()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(2, "SK_Skill_Klee", "SE_Skill_Klee1", "Pyro", "0.1*TotalATK,0", apCost: 30);
            env.DataManager.SkillEffectDict["SE_Skill_Klee1"].ScriptHook = "OnField(999999)";
            for (int i = 1; i <= 3; i++)
            {
                if (i > 1) env.DataManager.SkillEffectDict["SE_Skill_Klee" + i] = new SkillEffectData
                { SkillEffectID = 10160300 + i * 2, SkillEffectID2 = "SE_Skill_Klee" + i, EffectIndex = i * 2,
                    EffectType = "Damage", Element = "Pyro", DamageType = "Skill", Param1 = "0.1*TotalATK,0", TargetType = "Enemy", TargetNumber = -1, TargetConsecutive = 1 };
                string key = "SE_Skill_KleeEnergy" + i;
                env.DataManager.SkillEffectDict[key] = new SkillEffectData
                { SkillEffectID = 10160301 + i * 2, SkillEffectID2 = key, EffectIndex = i * 2 + 1,
                    EffectType = "GainEnergy", Element = "Pyro", Param1 = "8", Param2 = "Based", TargetType = "Self", ScriptHook = "Klee_SkillEnergy(" + i + ")" };
            }
            var owner = Start(env); env.CreateEnemy(2); int gains = 0;
            using var observer = BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged, (_, fact) =>
            { if (fact.Resource == "Energy" && fact.Subject == owner && fact.Added > 0) gains++; }, 0);
            BattleRandom.SetSource(new SequenceBattleRandomSource(new[] { .9f, .9f, .9f, .9f }));
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            Assert.That(gains, Is.EqualTo(1)); Assert.That(owner.CurrentEnergy, Is.EqualTo(12f));
        }

        private static BattleInputController StartThrowSelection(BattleFlowTestEnv env)
        {
            env.ConfigureAllyAction(2, "SK_Skill_Klee", "SE_Skill_Klee1", "Physical", "0.1*TotalATK,0", apCost: 30, cooldown: 5);
            var skill = env.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID][0];
            skill.MaxCharge = skill.InitialCharge = 2;
            var first = env.DataManager.SkillEffectDict["SE_Skill_Klee1"];
            first.EffectIndex = 2; first.TargetNumber = 3; first.TargetConsecutive = 1;
            for (int i = 1; i <= 3; i++)
            {
                if (i > 1) env.DataManager.SkillEffectDict["SE_Skill_Klee" + i] = new SkillEffectData
                {
                    SkillEffectID = i == 2 ? 10160305 : 10160308, SkillEffectID2 = "SE_Skill_Klee" + i,
                    EffectIndex = i == 2 ? 5 : 8, EffectType = "Damage", Element = "Physical", DamageType = "Skill",
                    Param1 = "0.1*TotalATK,0", TargetType = "Enemy", TargetNumber = 3,
                    TargetConsecutive = 2, TargetConsecutiveSet = true
                };
                string key = "SE_Skill_KleeEnergy" + i;
                env.DataManager.SkillEffectDict[key] = new SkillEffectData
                {
                    SkillEffectID = 10160310 + i, SkillEffectID2 = key, EffectIndex = i == 3 ? 10 : i * 3,
                    EffectType = "GainEnergy", Param1 = "8", Param2 = "Flat", TargetType = "Self",
                    ScriptHook = "Klee_SkillEnergy(" + i + ")"
                };
            }
            var mine = Main(env, 4101602, "ST_Klee_Mine"); mine.MaxStack = 0;
            env.DataManager.SkillEffectDict["SE_Skill_KleeMines"] = new SkillEffectData
            {
                SkillEffectID = 10160309, SkillEffectID2 = "SE_Skill_KleeMines", EffectIndex = 9,
                EffectType = "ApplyStatus", Param1 = mine.StatusID2, Duration = 4, AddInPhase = 2,
                TargetType = "EnemyField", TargetOverride = "0,0"
            };
            Start(env);
            for (int p = 2; p <= 5; p++) env.CreateEnemy(p);
            return env.Manager.gameObject.AddComponent<BattleInputController>();
        }

        [Test]
        public void Skill_ManualFirstThrow_CanCancelBeforePaymentAndBothLaterBouncesStayAutomatic()
        {
            using var env = new BattleFlowTestEnv();
            var input = StartThrowSelection(env);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            Assert.That(env.Ally.GetFirstSelectEffect(2).SkillEffectID2, Is.EqualTo("SE_Skill_Klee1"));
            Assert.That(env.Ally.EffectNeedsSelection(env.DataManager.SkillEffectDict["SE_Skill_Klee2"]), Is.False);
            Assert.That(env.Ally.EffectNeedsSelection(env.DataManager.SkillEffectDict["SE_Skill_Klee3"]), Is.False);
            Assert.That(random.IntCallCount, Is.Zero, "查询选择规则不得抽取弹跳落点");
            input.UIEnterSelectSkill(2);
            Assert.That(input.IsSelecting, Is.True);
            Assert.That(input.Selector.CurrentSelection.Count, Is.EqualTo(3), "首段是手选相邻三个位置，不是全体随机预览");
            input.UICancelSelection();
            Assert.That(input.IsSelecting, Is.False);
            Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(100));
            Assert.That(env.Ally.Entity.CurrentEnergy, Is.Zero);
            Assert.That(env.Ally.GetCooldownRemaining(2), Is.Zero);
            Assert.That(env.Ally.GetBoundSkillCurrentCharge(2), Is.EqualTo(2));
            foreach (var enemy in env.Manager.Enemies) Assert.That(enemy.Entity.CurrentHP, Is.EqualTo(1000));
            Assert.That(random.IntCallCount, Is.EqualTo(1), "只有选择器的默认并列落点，尚未执行随机弹跳");
            Assert.That(random.FloatCallCount, Is.Zero);
            Assert.That(env.Ally.EffectNeedsSelection(new SkillEffectData
            {
                SkillEffectID2 = "SE_Unrelated_Random", EffectType = "Damage", Element = "Pyro", TargetType = "Enemy",
                TargetNumber = 1, TargetConsecutive = 3
            }), Is.True, "不取消其他技能原本的随机预确认");
            Assert.That(env.Ally.EffectNeedsSelection(new SkillEffectData
            {
                SkillEffectID2 = "SE_Skill_Klee2", EffectType = "Damage", Element = "Pyro", TargetType = "Enemy",
                TargetNumber = 3, TargetConsecutive = 1
            }), Is.True, "以后明确改成手选时，不得继续按自动弹跳跳过");
        }

        [Test]
        public void Skill_OneManualConfirmation_CommitsAllThreeHitsAndMinesFollowTheLastRandomBounce()
        {
            using var env = new BattleFlowTestEnv();
            var input = StartThrowSelection(env);
            var landed = new Dictionary<string, List<int>>
            {
                ["SE_Skill_Klee1"] = new List<int>(), ["SE_Skill_Klee2"] = new List<int>(),
                ["SE_Skill_Klee3"] = new List<int>()
            };
            int gains = 0;
            using var hit = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            { if (landed.TryGetValue(fact.Source.SourceEffectID, out var positions)) positions.Add(fact.Subject.SlotPosition); }, 0);
            using var energy = BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged, (_, fact) =>
            { if (fact.Subject == env.Ally.Entity && fact.Resource == "Energy" && fact.Added > 0) gains++; }, 0);
            // 一次选择器默认落点、两次既有行动前预解析、两次实际随机弹跳；首段实际命中不抽随机。
            var random = new SequenceBattleRandomSource(new float[9], new[] { 0, 0, 0, 1, 2 });
            BattleRandom.SetSource(random);
            input.UIEnterSelectSkill(2);
            input.Selector.CurrentSelection = new List<int> { 1, 2, 3 };
            input.UIConfirmSelection();
            Assert.That(input.IsSelecting, Is.False, "一次确认后不再停在第二、第三跳");
            Assert.That(landed["SE_Skill_Klee1"], Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(landed["SE_Skill_Klee2"], Is.EqualTo(new[] { 2, 3, 4 }));
            Assert.That(landed["SE_Skill_Klee3"], Is.EqualTo(new[] { 3, 4, 5 }));
            Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(70));
            Assert.That(env.Ally.GetBoundSkillCurrentCharge(2), Is.EqualTo(1));
            Assert.That(gains, Is.EqualTo(1)); Assert.That(env.Ally.Entity.CurrentEnergy, Is.EqualTo(8));
            for (int p = 1; p <= 5; p++)
                Assert.That(env.Manager.Field.GetSlot(BattleSide.Enemy, p).StatusList.Exists(s => s.StatusID2 == "ST_Klee_Mine"), Is.EqualTo(p >= 3));
            Assert.That(random.IntCallCount, Is.EqualTo(5)); Assert.That(random.FloatCallCount, Is.EqualTo(9));
        }

        [Test]
        public void SameFieldMines_StayIndependent_AndEachExplodesOnce()
        {
            using var env = new BattleFlowTestEnv(); var owner = Start(env);
            var mine = Main(env, 4101602, "ST_Klee_Mine"); mine.MaxStack = 0; mine.MaxCount = 0;
            var blast = Effect(env, 410160201, "STE_Klee_MineDamage", "Damage", "0.1*TotalATK,0");
            blast.Element = "Physical"; blast.DamageType = "Skill"; blast.TargetType = "Enemy"; blast.TargetSelect = "1,1";
            env.DataManager.StatusActionDict[mine.StatusID2] = new List<StatusActionData>
            { new StatusActionData { StatusID = mine.StatusID, StatusID2 = mine.StatusID2, ActionType = "OnEnd", Param1 = blast.StatusEffectID2 } };
            var field = env.Enemy.Entity.Position;
            var first = field.AddStatus(mine.StatusID2, owner, 4, 2, 0, mine);
            var second = field.AddStatus(mine.StatusID2, owner, 4, 2, 0, mine);
            Assert.That(second, Is.Not.SameAs(first)); Assert.That(field.StatusList, Has.Count.EqualTo(2));
            int blasts = 0;
            using var observer = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            { if (fact.Source.SourceEffectID == blast.StatusEffectID2) blasts++; }, 0);
            BattleRandom.SetSource(new SequenceBattleRandomSource(new[] { .9f, .9f }));
            for (int i = 0; i < 4; i++)
            {
                env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            }
            Assert.That(blasts, Is.EqualTo(2)); Assert.That(field.StatusList, Is.Empty);
            Assert.That(first.IsActive, Is.False); Assert.That(second.IsActive, Is.False);
        }

        [UnityTest]
        public IEnumerator HitFacts_SurviveScopeExit_AndNestedHitRestoresOriginalTargets()
            => HookTestEnv.AwaitTask(HitFactsAsync);
        private async Task HitFactsAsync()
        {
            using var env = new BattleFlowTestEnv(); var owner = Start(env); var target = env.Enemy.Entity;
            DamageResolvedEvent fact;
            long id = BattleEventHub.NextIdentity();
            using (BattleSpecialRules.BeginHit(id, owner, new[] { target }))
            using (var batch = new HitBatch(id))
            {
                batch.Resolve(new HitRequest { Target = target, Source = DamageSourceInfo.Create(owner,
                    ReactionSourceKind.CharacterSkill, "SK_Normal_Klee", "SE_Normal_Klee"), DamageBeforeShield = 1,
                    Element = "Physical", DamageType = "Normal", HitIndex = 1 });
                batch.Flush(); fact = batch.Results[0].DamageEvent;
            }
            var context = new ScriptHookContext { Caster = owner, DamageEvent = fact, Declaration = fact.Declaration };
            using (ScriptHookContext.EnterExecution(context))
            {
                Assert.That(ScriptHookEvaluator.Evaluate("Klee_C1Hit()", context), Is.True);
                using (BattleSpecialRules.BeginHit(BattleEventHub.NextIdentity(), owner, Array.Empty<BattleEntity>()))
                {
                    BattleSpecialRules.TryResolveTargets("Klee_HitTargets()", env.Manager, owner, out var targets);
                    Assert.That(targets, Is.EquivalentTo(new[] { target.SlotPosition }));
                }
            }
            await Task.Yield();
            Assert.That(ScriptHookEvaluator.Evaluate("Klee_SparkHit()", context), Is.True);
        }

        [TestCase(false), TestCase(true)]
        public void Burst_PhasesThreeAndSixEachFireFourTimes_EndExplosionOccursOnce_AndBindingsClear(bool switchOut)
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(3, "SK_Burst_Klee", "SE_Burst_KleeStatus", "", "", apCost: 0,
                effectType: "ApplyStatus", param1: "ST_Klee_Burst", duration: 3, addInPhase: 2, triggerPhase: 3, targetType: "Self");
            var owner = Start(env); var ally = env.CreateAlly(2); ally.Entity.CurrentEnergy = 0;
            var parent = Main(env, 4101603, "ST_Klee_Burst"); var child = Main(env, 4101604, "ST_Klee_BurstEnemyPhase");
            var bonus = Main(env, 4101611, "ST_Klee_C4ExitBonus", "DMGBonus"); bonus.ApplyString = "STE_Klee_C4Damage";
            Effect(env, 410161101, "STE_Klee_TestExitBonus", "Buff", "1");
            var bind = Effect(env, 410160301, "STE_Klee_TestBind", "BindStatus", child.StatusID2); bind.TriggerPhase = 6;
            var damage = Effect(env, 410160302, "STE_Klee_BurstDamage", "Damage", "0.1*TotalATK,0");
            damage.Element = "Physical"; damage.DamageType = "Burst"; damage.TargetType = "Enemy";
            damage.TargetSelect = "3"; damage.TargetConsecutive = 2; damage.ExecuteCount = 4;
            var energy = Effect(env, 410160303, "STE_Klee_TestC6Energy", "GainEnergy", "4");
            energy.Param2 = "Flat"; energy.TargetType = "AlliesOnly"; energy.TargetSelect = "-1";
            var blast = Effect(env, 410160304, "STE_Klee_C4Damage", "Damage", "0.1*TotalATK,0");
            blast.Element = "Physical"; blast.DamageType = "Burst"; blast.TargetType = "Enemy"; blast.TargetSelect = "-1";
            Effect(env, 410160305, "STE_Klee_TestExitApply", "ApplyStatus", bonus.StatusID2);
            Effect(env, 410160306, "STE_Klee_TestExitClear", "RemoveStatus", bonus.StatusID2);
            Effect(env, 410160307, "STE_Klee_TestQClear", "RemoveStatus", parent.StatusID2);
            env.DataManager.StatusActionDict[parent.StatusID2] = new List<StatusActionData>
            {
                new StatusActionData { StatusID = parent.StatusID, StatusID2 = parent.StatusID2, ActionType = "OnItsTurn", Param1 = damage.StatusEffectID2 + ";" + energy.StatusEffectID2 },
                new StatusActionData { StatusID = parent.StatusID, StatusID2 = parent.StatusID2, ActionType = "OnEnd", Param1 = blast.StatusEffectID2 },
                new StatusActionData { StatusID = parent.StatusID, StatusID2 = parent.StatusID2, ActionType = "OnTrigger", ScriptHook = "SwitchOut(1016)",
                    Param1 = "STE_Klee_TestExitApply;STE_Klee_C4Damage;STE_Klee_TestExitClear;STE_Klee_TestQClear" }
            };
            env.DataManager.StatusActionDict[child.StatusID2] = new List<StatusActionData>
            { new StatusActionData { StatusID = child.StatusID, StatusID2 = child.StatusID2, ActionType = "OnItsTurn", Param1 = damage.StatusEffectID2 } };
            int shots = 0, blasts = 0; float first = 0, ending = 0;
            using var observer = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            {
                if (fact.Source.SourceEffectID == damage.StatusEffectID2) { shots++; first = fact.DamageBeforeShield; }
                if (fact.Source.SourceEffectID == blast.StatusEffectID2) { blasts++; ending = fact.DamageBeforeShield; }
            }, 0);
            BattleRandom.SetSource(new SequenceBattleRandomSource(new float[30]));
            Assert.That(env.Manager.UseBurstBySlot(0), Is.True);
            Assert.That(owner.GetStatus(child.StatusID2), Is.Not.Null);
            int rounds = switchOut ? 1 : 3;
            for (int i = 0; i < rounds; i++)
            {
                env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);
                Assert.That(shots, Is.EqualTo(i * 8 + 4)); Assert.That(ally.Entity.CurrentEnergy, Is.EqualTo((i + 1) * 4f));
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                Assert.That(shots, Is.EqualTo((i + 1) * 8));
            }
            if (switchOut) Assert.That(env.Manager.TrySwitchActiveAlly(1), Is.True);
            Assert.That(blasts, Is.EqualTo(1)); Assert.That(ending, Is.EqualTo(first * (switchOut ? 2 : 1)).Within(.001f));
            Assert.That(owner.HasStatus(parent.StatusID2), Is.False); Assert.That(owner.HasStatus(child.StatusID2), Is.False);
            Assert.That(owner.HasStatus(bonus.StatusID2), Is.False);
        }
    }
}
