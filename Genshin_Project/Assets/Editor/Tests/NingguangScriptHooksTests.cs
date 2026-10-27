using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    // 内存表走正式技能、状态、场地绑定和敌人行动入口，不读取Charts或Docs。
    public sealed class NingguangScriptHooksTests
    {
        private static StatusMainData Main(BattleFlowTestEnv env, int suffix, string id, string type = null)
        {
            var main = new StatusMainData { StatusID = 4101700 + suffix, StatusID2 = id, StatusName = id,
                StatusType = type, MaxCount = 1, MaxStack = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[id] = main; return main;
        }
        private static BattleEntity Start(BattleFlowTestEnv env)
        {
            env.CreateMinimalBattle(); var owner = env.Ally.Entity; owner.EntityID = 1017; owner.CritRate = 0;
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction); return owner;
        }
        private static StatusInstance Star(BattleFlowTestEnv env, BattleEntity owner, string mode = null)
        {
            if (!env.DataManager.StatusMainDict.TryGetValue(NingguangStarJadeCount.ID, out var main))
                main = Main(env, 1, NingguangStarJadeCount.ID, "Count");
            return owner.AddStatus(main.StatusID2, owner, 0, 2, 0, main, null, mode);
        }
        private static StatusEffectData Effect(BattleFlowTestEnv env, int status, int index, string key, string type,
            string param, string target = "Self")
        {
            var effect = new StatusEffectData { StatusEffectID = (4101700 + status) * 100 + index,
                StatusEffectID2 = key, EffectIndex = index, EffectType = type, Param1 = param, TargetType = target };
            env.DataManager.StatusEffectDict[key] = effect; return effect;
        }
        private static StatusInstance Screen(BattleFlowTestEnv env, BattleEntity owner, params int[] slots)
        {
            var main = Main(env, 5, NingguangScreenCount.ID, "Count");
            var fieldMain = Main(env, 6, NingguangScriptHooks.ScreenFieldID, "Debuff"); fieldMain.MaxStack = 0;
            fieldMain.MultiplierPart2 = "DMGBonus"; fieldMain.ScriptHook = "Ningguang_ScreenFieldActive()";
            Effect(env, 6, 1, "STE_Ningguang_ScreenDamagePenalty", "Debuff", "-0.1");
            var screen = owner.AddStatus(main.StatusID2, owner, 5, 2, 0, main);
            foreach (int slot in slots)
            {
                var field = env.Manager.Field.GetSlot(BattleSide.Enemy, slot);
                screen.BoundStatuses.Add(field.AddStatus(fieldMain.StatusID2, owner, 0, 2, 0, fieldMain));
            }
            return screen;
        }

        [Test]
        public void Normal_TwoHitsAndThreeTargets_GrantOneJade_AndC6DoesNotGetClipped()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(0, "SK_Normal_Ningguang", "SE_Normal_Ningguang", "Physical", ".01*TotalATK,0");
            env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_0"][1].Hits2 = ".01*TotalATK,0";
            env.DataManager.SkillEffectDict["SE_Normal_Ningguang"].TargetNumber = -1;
            env.DataManager.SkillEffectDict["SE_Normal_NingguangJade"] = new SkillEffectData
            { SkillEffectID = 10170102, SkillEffectID2 = "SE_Normal_NingguangJade", EffectIndex = 1,
                EffectType = "ApplyStatus", Param1 = NingguangStarJadeCount.ID, TargetType = "Self", ScriptHook = "Ningguang_NormalHit()" };
            Main(env, 1, NingguangStarJadeCount.ID, "Count");
            var owner = Start(env); env.CreateEnemy(2); env.CreateEnemy(3);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            for (int i = 1; i <= 4; i++)
            {
                Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                Assert.That(owner.GetStatus(NingguangStarJadeCount.ID).RuntimeIntValue, Is.EqualTo(Math.Min(3, i)));
            }
            var original = owner.GetStatus(NingguangStarJadeCount.ID);
            Assert.That(Star(env, owner, "C6"), Is.SameAs(original)); Assert.That(original.RuntimeIntValue, Is.EqualTo(7));
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True); Assert.That(original.RuntimeIntValue, Is.EqualTo(7));
            Assert.That(random.FloatCallCount, Is.EqualTo(30)); Assert.That(random.IntCallCount, Is.Zero);
        }

        [Test]
        public void Normal_AllMissesDoNotGrantJade_AndNonNormalCannotBorrowHit()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(0, "SK_Normal_Ningguang", "SE_Normal_Ningguang", "Physical", ".01*TotalATK,0");
            env.DataManager.SkillEffectDict["SE_Normal_Ningguang"].ScriptHook = "OnField(999999)";
            env.DataManager.SkillEffectDict["SE_Normal_NingguangJade"] = new SkillEffectData
            { SkillEffectID = 10170102, SkillEffectID2 = "SE_Normal_NingguangJade", EffectIndex = 1,
                EffectType = "ApplyStatus", Param1 = NingguangStarJadeCount.ID, TargetType = "Self", ScriptHook = "Ningguang_NormalHit()" };
            Main(env, 1, NingguangStarJadeCount.ID, "Count"); var owner = Start(env);
            BattleRandom.SetSource(new SequenceBattleRandomSource());
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True); Assert.That(owner.HasStatus(NingguangStarJadeCount.ID), Is.False);
            Assert.That(ScriptHookEvaluator.Evaluate("Ningguang_NormalHit()", new ScriptHookContext { Caster = owner }), Is.False);
        }

        [UnityTest]
        public IEnumerator Screen_OneBudgetPerWholeEnemyAction_AddsNegativeDamageBonus_NotDamageTaken()
            => HookTestEnv.AwaitTask(ScreenAsync);
        private async Task ScreenAsync()
        {
            using var env = new BattleFlowTestEnv(); env.ConfigureEnemyDamageSkill(.01f);
            env.DataManager.EnemySkillMainDict[990201].UsePerTurn = 0;
            env.DataManager.EnemySkillParamDict["SE_Enemy_BattleFlow"].Hits2 = ".01,0";
            env.DataManager.EnemySkillEffectDict["SE_Enemy_BattleFlow"].TargetNumber = -1;
            var owner = Start(env); var member = env.CreateAlly(2).Entity;
            var screen = Screen(env, owner, 1, 2, 3);
            var outside = env.CreateEnemy(5).Entity;
            var bonus = Main(env, 20, "ST_Test_EnemyBonus", "Buff"); bonus.MultiplierPart2 = "DMGBonus";
            Effect(env, 20, 1, "STE_Test_EnemyBonus", "Buff", "0.5");
            env.Enemy.Entity.AddStatus(bonus.StatusID2, env.Enemy.Entity, 0, 2, 0, bonus);
            Assert.That(env.Enemy.Entity.GetStatusDMGBonus("x", "x", -1, "Physical", member), Is.EqualTo(.4f).Within(.00001f));
            Assert.That(outside.GetStatusDMGBonus("x", "x", -1, "Physical", member), Is.Zero);
            Assert.That(member.PrepareDMGTaken("x", "x", "Normal", "Physical").GetMultiplier(ReactionType.None), Is.EqualTo(1), "不污染受击方减伤区");
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random); int hits = 0;
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
            {
                if (hit.Source.SourceKind != ReactionSourceKind.EnemySkill) return;
                Assert.That(screen.IsActive, Is.True); Assert.That(screen.RuntimeIntValue, Is.EqualTo(6 - hits / 4)); hits++;
                float defense = hit.Subject.GetFinalDEF();
                float expected = env.Enemy.Entity.TotalATK * .01f * 1.4f
                    * (1f - defense / (defense + 5f * env.Enemy.Entity.Level + 500f));
                Assert.That(hit.DamageBeforeShield, Is.EqualTo(expected).Within(.00001f), "1.5减0.1得到1.4，不能使用1.5乘0.9");
            }, 0);
            for (int i = 0; i < 6; i++) await BattleExecution.Run(env.Enemy.TakeEnemyActionAsync);
            Assert.That(hits, Is.EqualTo(24)); Assert.That(screen.RuntimeIntValue, Is.Zero); Assert.That(screen.IsActive, Is.False);
            Assert.That(env.Enemy.Entity.GetStatusDMGBonus("x", "x", -1, "Physical", member), Is.EqualTo(.5f).Within(.00001f));
            foreach (var field in env.Manager.Field.EnemySlots) Assert.That(field.StatusList.Exists(s => s.StatusID2 == NingguangScriptHooks.ScreenFieldID), Is.False);
            Assert.That(random.IntCallCount, Is.EqualTo(6)); Assert.That(random.FloatCallCount, Is.Zero);
        }

        [Test]
        public void Screen_FieldPenaltyFollowsCurrentOccupant_AndUnboundMarkerDoesNotApply()
        {
            using var env = new BattleFlowTestEnv(); var owner = Start(env);
            var screen = Screen(env, owner, 1, 2, 3); var moving = env.CreateEnemy(5).Entity;
            Assert.That(env.Enemy.Entity.GetStatusDMGBonus("x", "x", -1, "Pyro", owner), Is.EqualTo(-.1f).Within(.00001f));
            env.Manager.Field.GetSlot(BattleSide.Enemy, 1).Occupant = null;
            env.Enemy.Entity.Position = env.Manager.Field.GetSlot(BattleSide.Enemy, 5);
            env.Enemy.Entity.SlotPosition = 5; env.Enemy.Entity.Position.Occupant = env.Enemy.Entity;
            moving.Position = env.Manager.Field.GetSlot(BattleSide.Enemy, 2);
            moving.SlotPosition = 2; moving.Position.Occupant = moving;
            Assert.That(env.Enemy.Entity.GetStatusDMGBonus("x", "x", -1, "Pyro", owner), Is.Zero);
            Assert.That(moving.GetStatusDMGBonus("x", "x", -1, "Pyro", owner), Is.EqualTo(-.1f).Within(.00001f));
            var field = env.Manager.Field.GetSlot(BattleSide.Enemy, 5);
            var main = env.DataManager.StatusMainDict[NingguangScriptHooks.ScreenFieldID];
            var orphan = field.AddStatus(main.StatusID2, owner, 0, 2, 0, main);
            Assert.That(env.Enemy.Entity.GetStatusDMGBonus("x", "x", -1, "Pyro", owner), Is.Zero, "未绑定的临时标记不借用另一份屏");
            field.RemoveStatusInstance(orphan); owner.RemoveStatusInstance(screen);
            Assert.That(moving.GetStatusDMGBonus("x", "x", -1, "Pyro", owner), Is.Zero);
        }

        [UnityTest]
        public IEnumerator Skill_BindsWholeSelectedRange_RefreshesScreen_ButProducesEnergyOnlyOncePerRound()
            => HookTestEnv.AwaitTask(SkillAsync);
        private async Task SkillAsync()
        {
            using var env = new BattleFlowTestEnv();
            env.DataManager.CharacterAttributesDict[BattleFlowTestEnv.AllyID].Element = "Geo";
            env.ConfigureAllyAction(2, "SK_Skill_Ningguang", "SE_Skill_NingguangClear", "Physical", null, apCost: 0,
                effectType: "RemoveStatus", param1: NingguangScreenCount.ID, targetType: "Self");
            void Row(int index, string suffix, string type, string param, string target, Action<SkillEffectData> configure = null)
            {
                var e = new SkillEffectData { SkillEffectID2 = "SE_Skill_Ningguang" + suffix, EffectIndex = index,
                    EffectType = type, Param1 = param, TargetType = target };
                configure?.Invoke(e); env.DataManager.SkillEffectDict[e.SkillEffectID2] = e;
            }
            Row(1, "", "Damage", ".01*TotalATK,0", "Enemy", e =>
            { e.Element = "Geo"; e.DamageType = "Skill"; e.TargetNumber = 3; e.TargetConsecutive = 1; e.TargetConsecutiveSet = true; });
            Row(2, "Fields", "ApplyStatus", NingguangScriptHooks.ScreenFieldID, "EnemyField", e => e.TargetOverride = "0,0");
            Row(3, "Screen", "ApplyStatus", NingguangScreenCount.ID, "Self", e => { e.Duration = 5; e.AddInPhase = 2; });
            Row(4, "Energy", "GainEnergy", "10", "Allies", e =>
            { e.Element = "Geo"; e.Param2 = "Based"; e.TargetNumber = -1; e.ScriptHook = "Ningguang_SkillEnergy()"; });
            Row(5, "Lock", "ApplyStatus", "ST_Ningguang_EnergyLock", "Self", e =>
            { e.Duration = 1; e.AddInPhase = 1; e.ScriptHook = "Ningguang_SkillEnergy()"; });
            var screenMain = Main(env, 5, NingguangScreenCount.ID, "Count");
            var fieldMain = Main(env, 6, NingguangScriptHooks.ScreenFieldID, "Debuff"); fieldMain.MaxStack = 0;
            fieldMain.MultiplierPart2 = "DMGBonus"; fieldMain.ScriptHook = "Ningguang_ScreenFieldActive()";
            Effect(env, 6, 1, "STE_Ningguang_ScreenDamagePenalty", "Debuff", "-0.1");
            Main(env, 9, "ST_Ningguang_EnergyLock");
            var res = Main(env, 8, "ST_Ningguang_C4Resistance", "Buff"); res.MultiplierPart2 = "ResBonus";
            res.ApplyElementType = "Pyro;Hydro;Electro;Cryo;Anemo;Dendro;Geo"; res.ScriptHook = "Ningguang_ScreenActive()";
            Effect(env, 8, 1, "STE_Ningguang_C4Resistance", "Buff", "0.1");
            var bind = Effect(env, 5, 2, "STE_Ningguang_C4Bind", "BindStatus", res.StatusID2, "AllyTeam");
            bind.ScriptHook = "Check(1017_C4)";
            var owner = Start(env); owner.ConstellationLevel = 4; owner.MaxEnergy = 100; owner.CurrentEnergy = 0;
            var member = env.CreateAlly(2).Entity; member.MaxEnergy = 100; member.CurrentEnergy = 0; env.CreateEnemy(4);
            BattleRandom.SetSource(new SequenceBattleRandomSource());
            async Task Cast(params int[] slots)
            {
                env.Ally.ForcedTargetPositions = new List<int>(slots);
                await BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_Skill_Ningguang", 2, true));
            }
            await Cast(1, 2, 3);
            var first = owner.GetStatus(screenMain.StatusID2);
            Assert.That(first.BoundStatuses, Has.Count.EqualTo(4), "完整三格含空格，加一份四命团队绑定");
            Assert.That(owner.CurrentEnergy, Is.EqualTo(15f)); Assert.That(member.CurrentEnergy, Is.EqualTo(12f));
            foreach (string element in new[] { "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Dendro", "Geo" })
                Assert.That(member.GetStatusResBonus(element), Is.EqualTo(.1f).Within(.00001f));
            Assert.That(member.GetStatusResBonus("Physical"), Is.Zero);
            await Cast(3, 4, 5);
            var second = owner.GetStatus(screenMain.StatusID2);
            Assert.That(first.IsActive, Is.False); Assert.That(first.BoundStatuses, Is.Empty);
            Assert.That(second.RuntimeIntValue, Is.EqualTo(6)); Assert.That(second.RemainingPhaseCount, Is.EqualTo(5));
            Assert.That(owner.CurrentEnergy, Is.EqualTo(15f)); Assert.That(member.CurrentEnergy, Is.EqualTo(12f));
            Assert.That(env.Manager.Field.GetSlot(BattleSide.Enemy, 1).StatusList, Is.Empty);
            Assert.That(env.Manager.AllyTeam.GetStatusInstances(res.StatusID2), Has.Count.EqualTo(1));
            env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            await Cast(3, 4, 5);
            Assert.That(owner.CurrentEnergy, Is.EqualTo(30f)); Assert.That(member.CurrentEnergy, Is.EqualTo(24f));
            owner.RemoveStatus(NingguangScreenCount.ID);
            Assert.That(env.Manager.AllyTeam.HasStatus(res.StatusID2), Is.False);
            Assert.That(member.GetStatusResBonus("Geo"), Is.Zero);
        }

        [Test]
        public void Screen_ReapplicationAndExpiryRemoveSpecificBindings_WithoutSpendingLastCharge()
        {
            using var env = new BattleFlowTestEnv(); var owner = Start(env); var old = Screen(env, owner, 1, 2, 3);
            int broken = 0;
            using var listener = BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged, (_, r) =>
            { if (r.PoolId == NingguangScreenCount.ID && r.Reason == ResourceChangeReason.Spend && r.After == 0) broken++; }, 0);
            var next = owner.AddStatus(old.StatusID2, owner, 5, 2, 0, old.MainData);
            Assert.That(next, Is.Not.SameAs(old)); Assert.That(old.IsActive, Is.False); Assert.That(next.RuntimeIntValue, Is.EqualTo(6));
            Assert.That(old.BoundStatuses, Is.Empty);
            owner.RemoveStatusInstance(next, "Expired"); Assert.That(broken, Is.Zero);
        }

        [Test]
        public void Charge_ReadsCountAtFiring_ConsumesBeforeDamage_EmptyPositionKeepsJades()
        {
            using var env = new BattleFlowTestEnv(); var owner = Start(env); var stars = Star(env, owner, "C6");
            var markerMain = Main(env, 3, NingguangScriptHooks.ChargeTargetID); markerMain.MaxStack = 0;
            var chargeMain = Main(env, 2, NingguangScriptHooks.ChargeID);
            var charge = owner.AddStatus(chargeMain.StatusID2, owner, 1, 2, 0, chargeMain);
            var field = env.Manager.Field.GetSlot(BattleSide.Enemy, 1);
            charge.BoundStatuses.Add(field.AddStatus(markerMain.StatusID2, owner, 0, 2, 0, markerMain));
            charge.RemainingPhaseCount = 0;
            using (BattleSpecialRules.BeginStatusAction(charge))
            {
                Assert.That(stars.RuntimeIntValue, Is.Zero);
                Assert.That(ExecuteCountEvaluator.Evaluate(1, "Ningguang_FiredJades()", owner), Is.EqualTo(7));
                Star(env, owner); Assert.That(stars.RuntimeIntValue, Is.EqualTo(1));
                Assert.That(ExecuteCountEvaluator.Evaluate(1, "Ningguang_FiredJades()", owner), Is.EqualTo(7));
            }
            Assert.That(ExecuteCountEvaluator.Evaluate(1, "Ningguang_FiredJades()", owner), Is.Zero);
            field.Occupant = null;
            env.Enemy.Entity.CurrentHP = 0;
            using (BattleSpecialRules.BeginStatusAction(charge))
                Assert.That(ExecuteCountEvaluator.Evaluate(1, "Ningguang_FiredJades()", owner), Is.Zero);
            Assert.That(stars.RuntimeIntValue, Is.EqualTo(1));
        }

        [Test]
        public void Burst_SixOrTwelveFreshHits_PreservesScreen_AndDoesNotRequeryAfterFirstMissile()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(3, "SK_Burst_Ningguang", "SE_Burst_Ningguang", "Physical", ".01*TotalATK,0", apCost: 0);
            var effect = env.DataManager.SkillEffectDict["SE_Burst_Ningguang"];
            effect.TargetConsecutive = 3; effect.ExecuteCountHandler = "Ningguang_BurstCount()";
            var owner = Start(env); env.CreateEnemy(2); var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            int hits = 0; StatusInstance screen = null;
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
            {
                if (hit.Source.SourceEffectID != "SE_Burst_Ningguang") return;
                hits++;
                if (hits == 7) owner.RemoveStatusInstance(screen, "TestRemovalAfterFirstMissile");
            }, 0);
            Assert.That(env.Manager.UseBurstBySlot(0), Is.True); Assert.That(hits, Is.EqualTo(6));
            screen = Screen(env, owner, 1, 2, 3);
            Assert.That(env.Manager.UseBurstBySlot(0), Is.True); Assert.That(hits, Is.EqualTo(18));
            Assert.That(random.FloatCallCount, Is.EqualTo(18));
            Assert.That(random.IntCallCount, Is.EqualTo(18), "每一发重新随机一次，不预选整批目标");
            screen = Screen(env, owner, 1, 2, 3);
            Assert.That(env.Manager.UseBurstBySlot(0), Is.True); Assert.That(hits, Is.EqualTo(30));
            Assert.That(screen.IsActive, Is.True); Assert.That(screen.RuntimeIntValue, Is.EqualTo(6), "爆发不碎屏、不消费抵御次数");
        }

        [UnityTest]
        public IEnumerator C2_OnlyEnemyExhaustionResetsCooldown_AndNewScreenCannotResetTwoRoundLimit()
            => HookTestEnv.AwaitTask(C2Async);
        private async Task C2Async()
        {
            using var env = new BattleFlowTestEnv(); env.ConfigureEnemyDamageSkill(.01f);
            env.DataManager.EnemySkillMainDict[990201].UsePerTurn = 0;
            env.ConfigureAllyAction(2, "SK_Skill_Ningguang", "SE_Skill_Ningguang", "Physical", ".01*TotalATK,0", apCost: 0, cooldown: 3);
            var owner = Start(env); owner.ConstellationLevel = 2;
            var main = Main(env, 12, NingguangScriptHooks.C2ID);
            Effect(env, 12, 1, "STE_Ningguang_ResetSkill", "ResetCooldown", "SK_Skill_Ningguang");
            env.DataManager.StatusActionDict[main.StatusID2] = new List<StatusActionData>
            { new StatusActionData { StatusID = main.StatusID, StatusID2 = main.StatusID2, ActionType = "OnTrigger",
                Param1 = "STE_Ningguang_ResetSkill", ScriptHook = "Ningguang_ScreenBroken()", Cooldown = 2 } };
            var watcher = owner.AddStatus(main.StatusID2, owner, 0, 2, 0, main);
            BattleRandom.SetSource(new SequenceBattleRandomSource());
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True); Assert.That(env.Ally.GetCooldownRemaining(2), Is.EqualTo(3));
            var screen = Screen(env, owner, 1, 2, 3);
            for (int i = 0; i < 6; i++) await BattleExecution.Run(env.Enemy.TakeEnemyActionAsync);
            Assert.That(env.Ally.GetCooldownRemaining(2), Is.Zero); Assert.That(watcher.LifeTriggerCount, Is.EqualTo(1));
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            screen = Screen(env, owner, 1, 2, 3);
            for (int i = 0; i < 6; i++) await BattleExecution.Run(env.Enemy.TakeEnemyActionAsync);
            Assert.That(env.Ally.GetCooldownRemaining(2), Is.EqualTo(3)); Assert.That(watcher.LifeTriggerCount, Is.EqualTo(1));
            screen = Screen(env, owner, 1, 2, 3); owner.RemoveStatusInstance(screen, "Expired");
            await BattleExecution.Run(BattleExecution.Flush);
            Assert.That(env.Ally.GetCooldownRemaining(2), Is.EqualTo(3)); Assert.That(watcher.LifeTriggerCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ChargedSkill_StartupHasNoAction_ThenRealStatusFiresCurrentSevenJadesAndCleansBindings()
            => HookTestEnv.AwaitTask(ChargedSkillAsync);
        private async Task ChargedSkillAsync()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(1, "SK_Heavy_Ningguang", "SE_Heavy_NingguangTarget", "Physical", null,
                apCost: 30, effectType: "ApplyStatus", param1: NingguangScriptHooks.ChargeTargetID, targetType: "EnemyField");
            env.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID][0].ActionType = "";
            env.DataManager.SkillEffectDict["SE_Heavy_NingguangCharge"] = new SkillEffectData
            { SkillEffectID2 = "SE_Heavy_NingguangCharge", EffectIndex = 1, EffectType = "ApplyStatus",
                Param1 = NingguangScriptHooks.ChargeID, Duration = 1, AddInPhase = 2, TargetType = "Self" };
            var marker = Main(env, 3, NingguangScriptHooks.ChargeTargetID); marker.MaxStack = 0;
            var chargeMain = Main(env, 2, NingguangScriptHooks.ChargeID);
            var mainDamage = Effect(env, 2, 2, "STE_Ningguang_HeavyDamage", "Damage", ".1*TotalATK,0", "Enemy");
            mainDamage.Element = "Physical"; mainDamage.DamageType = "Heavy"; mainDamage.TargetOverride = "Ningguang_ChargeTarget()";
            var jadeDamage = Effect(env, 2, 3, "STE_Ningguang_JadeDamage", "Damage", ".01*TotalATK,0", "Enemy");
            jadeDamage.Element = "Physical"; jadeDamage.DamageType = "Heavy"; jadeDamage.TargetOverride = "Ningguang_ChargeTarget()";
            jadeDamage.ExecuteCountHandler = "Ningguang_FiredJades()";
            env.DataManager.StatusActionDict[chargeMain.StatusID2] = new List<StatusActionData>
            { new StatusActionData { StatusID = chargeMain.StatusID, StatusID2 = chargeMain.StatusID2, ActionType = "OnEnd",
                Param1 = "STE_Ningguang_HeavyDamage;STE_Ningguang_JadeDamage" } };
            var freeMain = Main(env, 10, "ST_Ningguang_T1");
            var free = Effect(env, 10, 1, "STE_Ningguang_FreeCharge", "APCost", "Free");
            free.Param2 = "Heavy"; free.ScriptHook = "Ningguang_StarJadeAP()";
            var owner = Start(env); var stars = Star(env, owner);
            owner.AddStatus(freeMain.StatusID2, owner, 0, 2, 0, freeMain);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            int hits = 0, actions = 0;
            using var hitListener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
            {
                if (hit.Source.SourceEntity != owner) return;
                Assert.That(hit.DamageType, Is.EqualTo("Heavy")); Assert.That(stars.RuntimeIntValue, Is.Zero); hits++;
            }, 0);
            using var actionsListener = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted, (_, action) =>
            { if (action.Subject == owner) actions++; }, 0);
            env.Ally.ForcedTargetPositions = new List<int> { 1 };
            await BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_Heavy_Ningguang", 1, true));
            Assert.That(actions, Is.Zero); Assert.That(hits, Is.Zero); Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(100));
            Assert.That(stars.RuntimeIntValue, Is.EqualTo(1)); Star(env, owner, "C6");
            env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            Assert.That(hits, Is.EqualTo(8)); Assert.That(actions, Is.Zero); Assert.That(stars.RuntimeIntValue, Is.Zero);
            Assert.That(owner.HasStatus(chargeMain.StatusID2), Is.False);
            Assert.That(env.Manager.Field.GetSlot(BattleSide.Enemy, 1).StatusList, Is.Empty);
            Assert.That(random.FloatCallCount, Is.EqualTo(8)); Assert.That(random.IntCallCount, Is.Zero);
        }
    }
}
