using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>内存规则用例，不读取武器工作簿；本次仅编写，未运行Unity。</summary>
    public sealed class WeaponHookRegressionTests
    {
        private HookTestDataManager data;
        private readonly List<GameObject> objects = new List<GameObject>();
        [SetUp]
        public void SetUp()
        {
            HookTestEnv.ResetStaticState();
            HookTestEnv.ClearBattleManagerSingleton();
            BattleEventHub.ResetSession();
            data = HookTestEnv.CreateDataManager(false);
        }
        [TearDown]
        public void TearDown()
        {
            BattleRandom.ResetSource();
            BattleEventHub.ResetSession();
            foreach (var obj in objects) UnityEngine.Object.DestroyImmediate(obj);
            objects.Clear();
            HookTestEnv.ResetStaticState();
            HookTestEnv.DestroyDataManager(data);
        }
        private BattleEntity Owner()
        {
            var obj = new GameObject("WeaponHookOwner"); objects.Add(obj);
            var entity = obj.AddComponent<BattleEntity>();
            entity.EntityID = 1009; entity.Side = BattleSide.Ally;
            entity.CurrentHP = entity.TotalHP = 100f;
            var controller = obj.AddComponent<CharacterBattleController>();
            controller.Entity = entity;
            entity.CharacterCtrl = controller;
            return entity;
        }
        private static StatusInstance Create(BattleEntity owner, int weapon, int statusID, params float[] parameters)
        {
            var context = new WeaponRuntimeContext { WeaponID = weapon, Refinement = 1, Equipper = owner, SourceInstanceID = "WeaponTest:" + weapon };
            for (int i = 0; i < parameters.Length; i++) context.ResolvedParams[i + 1] = parameters[i];
            var status = new StatusInstance
            {
                StatusID2 = DataManager.WeaponStatusKey(statusID), WeaponContext = context,
                MainData = new StatusMainData { StatusID = statusID, StatusID2 = DataManager.WeaponStatusKey(statusID), StatusType = "Count" },
                IsActive = true, ApplyOrder = 3, AddInPhase = 2
            };
            status.RecordSource(owner);
            CountStatusSystem.OnCreated(status, owner);
            owner.StatusDict.Add(status);
            return status;
        }
        private static CountStatusApplication Again(StatusInstance status) => new CountStatusApplication
        { Caster = status.Caster, MainData = status.MainData, WeaponContext = status.WeaponContext, Duration = 2, AddInPhase = 2 };

        private BattleManager TimedBattle()
        {
            var battle = HookTestEnv.CreateBattleManager(); objects.Add(battle.gameObject);
            SetTime(battle, 1, 2);
            return battle;
        }
        private static void SetTime(BattleManager battle, int turn, int phase)
        {
            typeof(BattleManager).GetProperty("TurnCount").SetValue(battle, turn);
            typeof(BattleManager).GetProperty("CurrentPhase").SetValue(battle, (TurnPhase)phase);
        }
        private BattleEntity Member(BattleManager battle, bool active)
        {
            var owner = Owner(); owner.SlotPosition = battle.AllyCount + 1;
            battle.RegisterAlly(owner.CharacterCtrl); owner.CharacterCtrl.IsActive = active;
            return owner;
        }
        private static BattleDeclaration Payment(BattleEntity actor, int amount)
            => BattleEventHub.Record(new object(), new APSpentEventData(actor, amount, null));
        private static void ReapplyFrom(StatusInstance status, BattleDeclaration declaration, DamageResolvedEvent hit = null)
        {
            using (ScriptHookContext.EnterExecution(new ScriptHookContext
                { Status = status, Declaration = declaration, DamageEvent = hit }))
                CountStatusSystem.OnReapplied(status, status.Caster, Again(status));
        }

        [Test]
        public void APDeclaration_KeepsPaymentLayoutAndTimeAfterTheLayoutChanges()
        {
            var battle = TimedBattle(); var first = Member(battle, true); var second = Member(battle, false);
            var fact = new APSpentEventData(first, 25, 70);
            first.CharacterCtrl.IsActive = false; second.CharacterCtrl.IsActive = true;
            SetTime(battle, 2, 5);
            Assert.That(fact.WasOnField(first), Is.True);
            Assert.That(fact.WasOnField(second), Is.False);
            Assert.That(fact.SubjectWasOnField, Is.True);
            Assert.That(fact.Turn, Is.EqualTo(1)); Assert.That(fact.Phase, Is.EqualTo(2));
            Assert.That(fact.Before, Is.EqualTo(95)); Assert.That(fact.After, Is.EqualTo(70));
        }

        [Test]
        public void AlleyHunter_UsesActualAPAndAFullFrontRoundWithoutCarryingRemaindersAcrossSwitches()
        {
            var battle = TimedBattle(); var front = Member(battle, true); var owner = Member(battle, false);
            var status = Create(owner, 3087, 4308701, .02f, .20f, .04f);
            BattleSpecialRules.OnBattleReady(battle);
            ReapplyFrom(status, Payment(front, 15));
            var last = Payment(front, 10); ReapplyFrom(status, last); ReapplyFrom(status, last);
            Assert.That(status.RuntimeValue, Is.EqualTo(.02f).Within(.00001f));
            ReapplyFrom(status, Payment(front, 250));
            Assert.That(status.RuntimeValue, Is.EqualTo(.20f).Within(.00001f));
            front.CharacterCtrl.IsActive = false; owner.CharacterCtrl.IsActive = true;
            ReapplyFrom(status, BattleEventHub.Record(new object(), new SwitchEventData(front, owner)));
            ReapplyFrom(status, Payment(owner, 24)); // 保护期AP不能留下零头。
            SetTime(battle, 2, 1); ReapplyFrom(status, Payment(owner, 25));
            Assert.That(status.RuntimeValue, Is.EqualTo(.20f).Within(.00001f));
            SetTime(battle, 2, 2); ReapplyFrom(status, Payment(owner, 24));
            Assert.That(status.RuntimeValue, Is.EqualTo(.20f).Within(.00001f));
            ReapplyFrom(status, Payment(owner, 1));
            Assert.That(status.RuntimeValue, Is.EqualTo(.16f).Within(.00001f));
            ReapplyFrom(status, Payment(owner, 24));
            owner.CharacterCtrl.IsActive = false; front.CharacterCtrl.IsActive = true;
            ReapplyFrom(status, BattleEventHub.Record(new object(), new SwitchEventData(owner, front)));
            ReapplyFrom(status, Payment(front, 1));
            Assert.That(status.RuntimeValue, Is.EqualTo(.16f).Within(.00001f));
            CountStatusSystem.OnItsTurn(status, owner);
            Assert.That(status.RuntimeValue, Is.EqualTo(.16f).Within(.00001f));
        }

        [UnityTest]
        public IEnumerator Calamity_OnlyReceivesPaymentsFromItsCurrentWindowAndDoublesOnlyItsOffFieldIncrement()
            => HookTestEnv.AwaitTask(Calamity_OnlyReceivesPaymentsFromItsCurrentWindowAndDoublesOnlyItsOffFieldIncrementAsync);

        private async Task Calamity_OnlyReceivesPaymentsFromItsCurrentWindowAndDoublesOnlyItsOffFieldIncrementAsync()
        {
            var battle = TimedBattle(); var owner = Member(battle, true);
            var openingPayment = Payment(owner, 25); // 建窗之前发生，不追溯。
            var status = Create(owner, 3198, 4319803, .12f, .032f);
            Assert.That(status.RuntimeValue, Is.Zero);
            await BattleExecution.Run(() =>
            {
                BattleEventHub.Dispatch(openingPayment);
                BattleEventHub.Publish(new APSpentEventData(owner, 15, null));
                BattleEventHub.Publish(new APSpentEventData(owner, 10, null));
                return Task.CompletedTask;
            });
            Assert.That(status.RuntimeValue, Is.EqualTo(1f));
            var oldPayment = Payment(owner, 25);
            CountStatusSystem.OnReapplied(status, owner, Again(status));
            Assert.That(status.RemainingPhaseCount, Is.EqualTo(5));
            await BattleExecution.Run(() =>
            {
                BattleEventHub.Dispatch(oldPayment); // 旧订阅的已捕获付款不能进入重置后的池。
                return Task.CompletedTask;
            });
            Assert.That(status.RuntimeValue, Is.Zero);
            await BattleExecution.Run(() =>
            {
                BattleEventHub.Publish(new APSpentEventData(owner, 500, null));
                return Task.CompletedTask;
            });
            Assert.That(status.RuntimeValue, Is.EqualTo(6f));
            var effect = new StatusEffectData
                { StatusEffectID = 431980301, EffectType = "Buff", DamageHook = "Hook(431980301)" };
            Assert.That(WeaponHooks.TryResolveBuffScalar(effect, status, out float front), Is.True);
            owner.CharacterCtrl.IsActive = false;
            Assert.That(WeaponHooks.TryResolveBuffScalar(effect, status, out float back), Is.True);
            Assert.That(front, Is.EqualTo(.192f).Within(.00001f));
            Assert.That(back, Is.EqualTo(.384f).Within(.00001f));
            owner.RemoveStatusInstance(status);
            Assert.That(status.CountRuntimeState, Is.Null);
        }

        private void ApplyEffect(int id, string countID, int duration, string hook = null, int index = 0)
        {
            data.StatusEffectDict["STE_Weapon_" + id] = new StatusEffectData
            { StatusEffectID = id, StatusEffectID2 = "STE_Weapon_" + id, EffectType = "ApplyStatus",
                Param1 = countID, Duration = duration, TargetType = "Self", ScriptHook = hook, EffectIndex = index };
        }

        [UnityTest]
        public IEnumerator Haran_OtherFormalSkillsGainOnceAndOwnSkillSavesBeforeClearing()
            => HookTestEnv.AwaitTask(Haran_OtherFormalSkillsGainOnceAndOwnSkillSavesBeforeClearingAsync);
        private async Task Haran_OtherFormalSkillsGainOnceAndOwnSkillSavesBeforeClearingAsync()
        {
            var battle = TimedBattle(); var owner = Member(battle, false); var other = Member(battle, true);
            var sigils = Create(owner, 3160, 4316002, .12f, .2f);
            data.StatusMainDict[sigils.StatusID2] = sigils.MainData;
            string bonusID = "ST_Weapon_4316003";
            data.StatusMainDict[bonusID] = new StatusMainData
                { StatusID = 4316003, StatusID2 = bonusID, StatusType = "Count", MaxStack = 1 };
            data.StatusActionDict[sigils.StatusID2] = new List<StatusActionData>
            {
                new StatusActionData { StatusID2 = sigils.StatusID2, ActionType = "OnTrigger", ScriptHook = "Hook(4316002)",
                    Param1 = "STE_Weapon_431600201", MaxTimePerAction = 1 },
                new StatusActionData { StatusID2 = sigils.StatusID2, ActionType = "OnTrigger", ScriptHook = "Hook(431600202)",
                    OnAction = "Skill", Param1 = "STE_Weapon_431600202;STE_Weapon_431600203" }
            };
            ApplyEffect(431600201, sigils.StatusID2, 0);
            ApplyEffect(431600202, bonusID, 2);
            ApplyEffect(431600203, sigils.StatusID2, 0);
            await BattleExecution.Run(() =>
            {
                using var action = BattleResolutionContext.EnterAction(new BattleResolutionContext.CounterScope());
                var declaration = BattleEventHub.Publish(new ActionEventData(other, "SK_Test", "Skill", false));
                BattleEventHub.Dispatch(declaration);
                return Task.CompletedTask;
            });
            Assert.That(sigils.RuntimeValue, Is.EqualTo(1f));
            await BattleExecution.Run(() =>
            {
                BattleEventHub.Publish(new ActionEventData(other, "SK_Test", "Skill", false));
                BattleEventHub.Publish(new ActionEventData(other, "SK_Test", "Skill", true));
                BattleEventHub.Publish(new ActionEventData(other, "SK_Test", "Normal", false));
                return Task.CompletedTask;
            });
            Assert.That(sigils.RuntimeValue, Is.EqualTo(2f));
            await BattleExecution.Run(() =>
            {
                StatusOnHitHookSystem.NotifyPlayerAction(owner, new SkillMainData { SkillID2 = "SK_Own", ActionType = "Skill" });
                return Task.CompletedTask;
            });
            var saved = owner.GetStatus(bonusID);
            Assert.That(saved, Is.Not.Null);
            Assert.That(saved.RuntimeValue, Is.EqualTo(2f)); Assert.That(saved.RemainingPhaseCount, Is.EqualTo(2));
            Assert.That(sigils.RuntimeValue, Is.Zero); Assert.That(owner.GetStatus(sigils.StatusID2), Is.SameAs(sigils));
            saved.RemainingPhaseCount = 1;
            await BattleExecution.Run(() =>
            {
                StatusOnHitHookSystem.NotifyPlayerAction(owner, new SkillMainData { SkillID2 = "SK_Empty", ActionType = "Skill" });
                return Task.CompletedTask;
            });
            Assert.That(saved.RemainingPhaseCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Surfing_MergesAllTargetsAfterDamageAndDoesNotRefreshOrReviveOldWindows()
            => HookTestEnv.AwaitTask(Surfing_MergesAllTargetsAfterDamageAndDoesNotRefreshOrReviveOldWindowsAsync);
        private async Task Surfing_MergesAllTargetsAfterDamageAndDoesNotRefreshOrReviveOldWindowsAsync()
        {
            var battle = TimedBattle(); var owner = Member(battle, true); var other = Member(battle, false);
            var target = Owner(); target.Side = BattleSide.Enemy;
            var status = Create(owner, 3217, 4321703, .2f, .12f);
            data.StatusMainDict[status.StatusID2] = status.MainData;
            data.StatusActionDict[status.StatusID2] = new List<StatusActionData>
            { new StatusActionData { StatusID2 = status.StatusID2, ActionType = "OnTrigger", ScriptHook = "Hook(4321703)",
                Param1 = "STE_Weapon_432170302;STE_Weapon_432170303", MaxTimePerHit = 1 } };
            ApplyEffect(432170302, status.StatusID2, 4, "Hook(432170302)");
            ApplyEffect(432170303, status.StatusID2, 4, "Hook(432170303)", 1);
            status.RemainingPhaseCount = 1;
            async Task Hit(bool hp, bool vape, string type = "Normal", bool reset = false, bool teammate = false)
            {
                await BattleExecution.Run(() =>
                {
                    using var effect = BattleResolutionContext.BeginEffect();
                    using var collection = BattleResolutionContext.BeginCollection();
                    long hit = BattleEventHub.NextIdentity();
                    using var snapshot = BattleSpecialRules.BeginHit(hit, owner, new[] { target });
                    float before = status.RuntimeValue;
                    if (vape)
                        StatusOnHitHookSystem.NotifyReactions(new[] { new ReactionOccurrence { Type = ReactionType.Vaporize,
                            SourceEntity = teammate ? other : owner, Target = target, SourceEffectID = "SE_Test" } });
                    for (int i = 0; i < 2; i++)
                    {
                        var damage = new DamageResolvedEvent { Source = DamageSourceInfo.Create(owner,
                            ReactionSourceKind.CharacterSkill, "SK_Test", "SE_Test"), Target = target,
                            DamageType = type, HitLanded = true, ActualHPDamage = hp ? 10f : 0f,
                            RuntimeHitID = hit, TargetIndex = i + 1 };
                        StatusOnHitHookSystem.NotifyDamageBatch(new[] { damage });
                        Assert.That(status.RuntimeValue, Is.EqualTo(before)); // 多目标及反应声明都不能提前改层。
                    }
                    if (reset)
                        using (ScriptHookContext.EnterExecution(new ScriptHookContext { ExecutingStatusEffectID = 432170201 }))
                            CountStatusSystem.OnReapplied(status, owner, Again(status));
                    return Task.CompletedTask;
                });
            }
            await Hit(true, true); Assert.That(status.RuntimeValue, Is.EqualTo(4f));
            Assert.That(status.RemainingPhaseCount, Is.EqualTo(1));
            status.RuntimeValue = 1f;
            await Hit(true, true); Assert.That(status.RuntimeValue, Is.EqualTo(1f)); // 1→0→1。
            await Hit(true, false); Assert.That(status.RuntimeValue, Is.Zero); Assert.That(status.IsActive, Is.True);
            await Hit(false, true, "Skill"); Assert.That(status.RuntimeValue, Is.EqualTo(1f));
            await Hit(false, false); Assert.That(status.RuntimeValue, Is.EqualTo(1f));
            await Hit(false, true, "Skill", teammate: true); Assert.That(status.RuntimeValue, Is.EqualTo(1f));
            await Hit(true, false, reset: true); Assert.That(status.RuntimeValue, Is.EqualTo(4f));
            Assert.That(status.RemainingPhaseCount, Is.EqualTo(4)); // 旧hit不能扣新代窗口。
            owner.RemoveStatusInstance(status); Assert.That(status.CountRuntimeState, Is.Null);
        }

        private StatusInstance TulayWindow(BattleEntity owner)
        {
            var origin = owner.GetStatus("ST_Weapon_4321202");
            if (origin == null)
            {
                origin = new StatusInstance { StatusID2 = "ST_Weapon_4321202", IsActive = true,
                    WeaponContext = new WeaponRuntimeContext { SourceInstanceID = "WeaponTest:3212", WeaponID = 3212, Equipper = owner } };
                origin.RecordSource(owner); owner.StatusDict.Add(origin);
            }
            using (ScriptHookContext.EnterExecution(new ScriptHookContext { Status = origin }))
                return Create(owner, 3212, 4321203, 1f, .048f, .096f, .48f);
        }

        [Test]
        public void Tulay_APAndLogicalHitsShareOneCapButNeverRefreshTheWindowOrResurrectAnOldOne()
        {
            var battle = TimedBattle(); var owner = Member(battle, true);
            var status = TulayWindow(owner); status.RemainingPhaseCount = 1;
            var ledger = new StatusActionRuntimeState { LifeTriggerCount = 2 };
            status.ActionRuntime.Add("existing", ledger);
            ReapplyFrom(status, Payment(owner, 50));
            var hit = new DamageResolvedEvent { Source = DamageSourceInfo.Create(owner, ReactionSourceKind.CharacterSkill, "SK_TEST", "SE_TEST"),
                DamageType = "Normal", HitLanded = true, ShieldAbsorbed = 10, RuntimeHitID = BattleEventHub.NextIdentity() };
            hit.Declaration = BattleEventHub.Record(hit, new DamageEventData(hit), hit.RuntimeHitID);
            ReapplyFrom(status, hit.Declaration, hit); ReapplyFrom(status, hit.Declaration, hit);
            var splash = new DamageResolvedEvent { Source = hit.Source, Target = Owner(), DamageType = "Normal",
                HitLanded = true, RuntimeHitID = hit.RuntimeHitID };
            splash.Declaration = BattleEventHub.Record(splash, new DamageEventData(splash), splash.RuntimeHitID);
            ReapplyFrom(status, splash.Declaration, splash);
            Assert.That(status.RuntimeValue, Is.EqualTo(.192f).Within(.00001f));
            ReapplyFrom(status, Payment(owner, 250));
            Assert.That(status.RuntimeValue, Is.EqualTo(.48f).Within(.00001f));
            Assert.That(status.RemainingPhaseCount, Is.EqualTo(1));
            Assert.That(status.ActionRuntime["existing"], Is.SameAs(ledger));
            owner.RemoveStatusInstance(status);
            var latest = TulayWindow(owner);
            foreach (int id in new[] { 432120302, 432120303, 432120304 })
            {
                var effect = new StatusEffectData { StatusEffectID = id };
                Assert.That(WeaponHooks.CanExecuteWindowEffect(effect, status), Is.False);
                Assert.That(WeaponHooks.CanExecuteWindowEffect(effect, latest), Is.True);
            }
            Assert.That(latest.RuntimeValue, Is.Zero);
            Assert.That(latest.RemainingPhaseCount, Is.EqualTo(4));
            Assert.Throws<InvalidOperationException>(() => Create(owner, 3212, 4321203, 1f, .048f, .096f, .48f));
        }

        [UnityTest]
        public IEnumerator LostPrayer_StartsAtZeroAndUsesTheRealEntryPhaseThenClearsOnDeathOrSwitch()
            => HookTestEnv.AwaitTask(LostPrayer_StartsAtZeroAndUsesTheRealEntryPhaseThenClearsOnDeathOrSwitchAsync);

        private async Task LostPrayer_StartsAtZeroAndUsesTheRealEntryPhaseThenClearsOnDeathOrSwitchAsync()
        {
            var battle = TimedBattle(); var owner = Member(battle, true); var reserve = Member(battle, false);
            var status = Create(owner, 3207, 4320702, .08f);
            BattleSpecialRules.OnBattleReady(battle);
            Assert.That(status.RuntimeValue, Is.Zero); Assert.That(status.TriggerPhase, Is.EqualTo(2));
            CountStatusSystem.OnItsTurn(status, owner); Assert.That(status.RuntimeValue, Is.Zero);
            SetTime(battle, 2, 1); CountStatusSystem.OnItsTurn(status, owner);
            Assert.That(status.RuntimeValue, Is.Zero);
            SetTime(battle, 2, 2); CountStatusSystem.OnItsTurn(status, owner); CountStatusSystem.OnItsTurn(status, owner);
            Assert.That(status.RuntimeValue, Is.EqualTo(1f));
            SetTime(battle, 2, 5); owner.CharacterCtrl.IsActive = false; reserve.CharacterCtrl.IsActive = true;
            await BattleExecution.Run(() =>
            {
                BattleEventHub.Publish(new SwitchEventData(owner, reserve)); return Task.CompletedTask;
            });
            Assert.That(status.RuntimeValue, Is.Zero); Assert.That(owner.HasStatusInstance(status), Is.True);
            owner.CharacterCtrl.IsActive = true; reserve.CharacterCtrl.IsActive = false;
            await BattleExecution.Run(() =>
            {
                BattleEventHub.Publish(new SwitchEventData(reserve, owner)); return Task.CompletedTask;
            });
            Assert.That(status.TriggerPhase, Is.EqualTo(5));
            SetTime(battle, 3, 5); CountStatusSystem.OnItsTurn(status, owner);
            Assert.That(status.RuntimeValue, Is.EqualTo(1f));
            owner.CurrentHP = 0;
            await BattleExecution.Run(() =>
            {
                BattleEventHub.Publish(new DeathEventData(new DamageResolvedEvent { Target = owner })); return Task.CompletedTask;
            });
            Assert.That(status.RuntimeValue, Is.Zero); Assert.That(status.IsActive, Is.True);
            CountStatusSystem.OnItsTurn(status, owner); Assert.That(status.RuntimeValue, Is.Zero);
        }

        [TestCase(1)]
        [TestCase(5)]
        public void WeaponAPPreview_UsesTulayIndexAndOnlyTheMatchingPinesRefinementWithoutDrawingRandom(int refinement)
        {
            var battle = TimedBattle(); var owner = Member(battle, true);
            var context = new WeaponRuntimeContext { WeaponID = 3212, Equipper = owner, Refinement = refinement,
                ResolvedParams = new Dictionary<int,float> { [1] = refinement } };
            var tulay = new StatusInstance { StatusID2 = "ST_Weapon_4321201", IsActive = true, StackCount = 1,
                MainData = new StatusMainData { StatusID = 4321201, MaxCount = 1 }, WeaponContext = context };
            tulay.RecordSource(owner); owner.StatusDict.Add(tulay);
            data.StatusEffectDict.Add("tulay-ap", new StatusEffectData { StatusEffectID = 432120101,
                EffectType = "APCost", Param1 = "-Index(1)", Param2 = "Normal", DamageHook = "Hook(432120101)" });
            var pines = new StatusInstance { StatusID2 = "ST_Weapon_4317306", IsActive = true, StackCount = 1,
                MainData = new StatusMainData { StatusID = 4317306, MaxCount = 1 },
                WeaponContext = new WeaponRuntimeContext { WeaponID = 3173, Equipper = owner, Refinement = refinement } };
            pines.RecordSource(owner); owner.StatusDict.Add(pines);
            for (int rank = 1; rank <= 5; rank++)
                data.StatusEffectDict.Add("pines-ap-" + rank, new StatusEffectData { StatusEffectID = 431730600 + rank,
                    EffectType = "APCost", Param1 = (1 - rank).ToString(), Param2 = "Normal",
                    ScriptHook = "Hook(43173060" + rank + ")" });
            var random = new SequenceBattleRandomSource(new[] { .5f }); BattleRandom.SetSource(random);
            int before = battle.APManager.CurrentAP;
            for (int i = 0; i < 2; i++)
                Assert.That(APCostResolver.ForSkill(owner, new SkillMainData { APCost = 20, ActionType = "Normal" }, 0),
                    Is.EqualTo(21 - 2 * refinement));
            Assert.That(battle.APManager.CurrentAP, Is.EqualTo(before));
            Assert.That(random.FloatCallCount, Is.Zero); Assert.That(random.IntCallCount, Is.Zero);
        }

        [TestCase(3056, 4305602)]
        [TestCase(3080, 4308002)]
        [TestCase(3112, 4311202)]
        [TestCase(3132, 4313202)]
        public void RoyalCounts_StartAtOneCapAtFiveAndKeepTheSameInstance(int weapon, int statusID)
        {
            var owner = Owner(); var status = Create(owner, weapon, statusID, .08f);
            var ledger = new StatusActionRuntimeState { LifeTriggerCount = 7, PendingTriggerCount = 1 };
            status.ActionRuntime.Add("existing", ledger);
            Assert.That(status.RuntimeValue, Is.EqualTo(1f));
            for (int i = 0; i < 8; i++) Assert.That(CountStatusSystem.OnReapplied(status, owner, Again(status)), Is.SameAs(status));
            Assert.That(status.RuntimeValue, Is.EqualTo(5f));
            Assert.That(status.IsPermanent, Is.True);
            Assert.That(status.StackCount, Is.EqualTo(1));
            Assert.That(status.ActionRuntime["existing"], Is.SameAs(ledger));
            Assert.That(status.ApplyOrder, Is.EqualTo(3));
        }

        [Test]
        public void Whiteblind_FullCountStillRefreshesTheWholeWindowAndItsExistingChild()
        {
            var owner = Owner(); var status = Create(owner, 3058, 4305802, .06f);
            var child = new StatusInstance { StatusID2 = "child", IsActive = true, AddInPhase = 2, RemainingPhaseCount = 1 };
            owner.StatusDict.Add(child); status.BoundStatuses.Add(child);
            for (int i = 0; i < 3; i++) CountStatusSystem.OnReapplied(status, owner, Again(status));
            status.RemainingPhaseCount = 1;
            CountStatusSystem.OnReapplied(status, owner, Again(status));
            Assert.That(status.RuntimeValue, Is.EqualTo(4f));
            Assert.That(status.RemainingPhaseCount, Is.EqualTo(2));
            Assert.That(child.RemainingPhaseCount, Is.EqualTo(2));
            Assert.That(status.BoundStatuses.Count, Is.EqualTo(1));
            var foreign = Again(status);
            foreign.WeaponContext = new WeaponRuntimeContext { WeaponID = 3058, Equipper = owner, SourceInstanceID = "other" };
            Assert.Throws<InvalidOperationException>(() => CountStatusSystem.OnReapplied(status, owner, foreign));
            Assert.That(status.RuntimeValue, Is.EqualTo(4f));
        }

        [Test]
        public void Ibis_StoresTheRefinedEMAmount_NotTheNumberOfPoints()
        {
            var owner = Owner(); var status = Create(owner, 3096, 4309602, 50f);
            Assert.That(status.RuntimeValue, Is.EqualTo(50f));
            CountStatusSystem.OnReapplied(status, owner, Again(status));
            CountStatusSystem.OnReapplied(status, owner, Again(status));
            Assert.That(status.RuntimeValue, Is.EqualTo(100f));
            Assert.That(status.StackCount, Is.EqualTo(1));
        }

        [Test]
        public void FadingTwilight_CyclesTheRefinedRatiosWithoutRebuildingTheStatus()
        {
            var owner = Owner(); var status = Create(owner, 3093, 4309301, .12f, .20f, .28f);
            Assert.That(status.RuntimeValue, Is.EqualTo(.12f));
            foreach (float expected in new[] { .20f, .28f, .12f })
            {
                Assert.That(CountStatusSystem.OnReapplied(status, owner, Again(status)), Is.SameAs(status));
                Assert.That(status.RuntimeValue, Is.EqualTo(expected));
                Assert.That(status.IsPermanent, Is.True);
            }
        }

        [Test]
        public void SerpentSpine_ConsumesOnlyTheCurrentCommittedDamageOnce()
        {
            var owner = Owner(); var status = Create(owner, 3061, 4306101, .06f, .03f);
            Assert.That(status.RuntimeValue, Is.Zero);
            Assert.That(status.TriggerPhase, Is.EqualTo(1));
            status.RuntimeValue = 3f;
            var damage = new DamageResolvedEvent { Target = owner, ActualHPDamage = 1f, HPBefore = 100f, HPAfter = 99f };
            damage.Declaration = BattleEventHub.Record(damage, new DamageEventData(damage));
            using (ScriptHookContext.EnterExecution(new ScriptHookContext { DamageEvent = damage, Declaration = damage.Declaration }))
            {
                CountStatusSystem.OnReapplied(status, owner, Again(status));
                CountStatusSystem.OnReapplied(status, owner, Again(status));
            }
            Assert.That(status.RuntimeValue, Is.EqualTo(2f));
            Assert.That(status.IsPermanent, Is.True);
        }

        [Test]
        public void Favonius_UsesSavedCriticalAndRefinementBeforeOneProbabilityRoll()
        {
            var owner = Owner();
            var status = new StatusInstance
            { WeaponContext = new WeaponRuntimeContext { Equipper = owner, WeaponID = 3029, Refinement = 3, ResolvedParams = new Dictionary<int,float> { [1] = .8f } } };
            var damage = new DamageResolvedEvent
            { Source = DamageSourceInfo.Create(owner, ReactionSourceKind.CharacterSkill, "SK_TEST", "SE_TEST"), HitLanded = true, Critical = new CriticalHitResult { IsCritical = true } };
            var context = new ScriptHookContext { Caster = owner, Status = status, DamageEvent = damage };
            var random = new SequenceBattleRandomSource(new[] { .5f });
            BattleRandom.SetSource(random);
            Assert.That(ScriptHookEvaluator.Evaluate("Hook(430290102)", context), Is.False);
            Assert.That(random.FloatCallCount, Is.Zero);
            Assert.That(ScriptHookEvaluator.Evaluate("Hook(430290103)", context), Is.True);
            Assert.That(random.FloatCallCount, Is.EqualTo(1));
            damage.Critical = new CriticalHitResult { IsCritical = false };
            Assert.That(ScriptHookEvaluator.Evaluate("Hook(430290103)", context), Is.False);
            Assert.That(random.FloatCallCount, Is.EqualTo(1));
        }

        [Test]
        public void DamageDeclaration_KeepsTheOriginalCriticalValueSnapshot()
        {
            var damage = new DamageResolvedEvent { Critical = new CriticalHitResult { IsCritical = true, Roll = .2f } };
            var fact = new DamageEventData(damage);
            damage.Critical = new CriticalHitResult { IsCritical = false, Roll = .9f };
            Assert.That(fact.Critical.Value.IsCritical, Is.True);
            Assert.That(fact.Critical.Value.Roll, Is.EqualTo(.2f));
            Assert.That(new DamageEventData(new DamageResolvedEvent()).Critical, Is.Null);
        }

        [TestCase(3099, 4309902, 4309904)]
        [TestCase(3122, 4312202, 4312204)]
        public void MarkReward_SavesActualCountBeforeRemovalAndReplacesRatherThanAdds(int weapon, int marksID, int rewardID)
        {
            var owner = Owner(); var marks = Create(owner, weapon, marksID, .03f, .07f);
            CountStatusSystem.OnReapplied(marks, owner, Again(marks));
            var reward = Create(owner, weapon, rewardID, .03f, .07f);
            Assert.That(reward.RuntimeValue, Is.EqualTo(2f));
            owner.RemoveStatusInstance(marks);
            Assert.That(reward.RuntimeValue, Is.EqualTo(2f));
            Create(owner, weapon, marksID, .03f, .07f);
            reward.RemainingPhaseCount = 1;
            CountStatusSystem.OnReapplied(reward, owner, Again(reward));
            Assert.That(reward.RuntimeValue, Is.EqualTo(1f));
            Assert.That(reward.RemainingPhaseCount, Is.EqualTo(3));
        }

        [Test]
        public void Widsith_DrawsExactlyOnceAndConditionsOnlyReadTheChosenTheme()
        {
            var random = new SequenceBattleRandomSource(intValues: new[] { 1, 3 });
            BattleRandom.SetSource(random);
            var owner = Owner(); var status = Create(owner, 3130, 4313002, .6f, .48f, 240f);
            var context = new ScriptHookContext { Caster = owner, Status = status };
            for (int repeat = 0; repeat < 3; repeat++)
            {
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(431300201)", context), Is.True);
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(431300202)", context), Is.False);
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(431300203)", context), Is.False);
            }
            Assert.That(random.IntCallCount, Is.EqualTo(1));
            Assert.That(random.FloatCallCount, Is.Zero);
            CountStatusSystem.OnReapplied(status, owner, Again(status));
            Assert.That(random.IntCallCount, Is.EqualTo(2));
            Assert.That(status.RuntimeValue, Is.EqualTo(3f));
            Assert.That(status.RemainingPhaseCount, Is.EqualTo(2));
            Assert.That(ScriptHookEvaluator.Evaluate("Hook(431300203)", context), Is.True);
            Assert.That(random.IntCallCount, Is.EqualTo(2));
        }

        [TestCase("Pyro", true)]
        [TestCase("Geo", true)]
        [TestCase("Physical", false)]
        [TestCase("Physical", false)]
        [TestCase("", false)]
        public void ElementalReward_UsesTheActualDamageElement(string element, bool expected)
        {
            var owner = Owner(); var status = Create(owner, 3099, 4309902, .03f);
            status.MainData.MultiplierPart = "DMGBonus";
            status.MainData.ScriptHook = "Hook(4309905)";
            data.StatusEffectDict.Add("elemental-test", new StatusEffectData
                { StatusEffectID = 430990201, EffectType = "Buff", Param1 = "0.21" });
            Assert.That(owner.GetStatusDMGBonus("SE_TEST", "SK_TEST", 0, element, null),
                Is.EqualTo(expected ? .21f : 0f).Within(.00001f));
        }

        [Test]
        public void FlowingPurity_IncludesFinalRemovalPreservesFractionsAndDoesNotRewardTwice()
        {
            var owner = Owner(); var accumulator = Create(owner, 3146, 4314603, .08f, .02f, .12f);
            var partial = BattleEventHub.Record(new object(), new ResourceEventData("BondOfLife", "ST_BondOfLife", owner,
                1500, 500, -1000, ResourceChangeReason.HealingAbsorption, false));
            using (ScriptHookContext.EnterExecution(new ScriptHookContext { Status = accumulator, Declaration = partial }))
            {
                CountStatusSystem.OnReapplied(accumulator, owner, Again(accumulator));
                CountStatusSystem.OnReapplied(accumulator, owner, Again(accumulator));
                Assert.That(accumulator.RuntimeValue, Is.EqualTo(1000f));
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(431460302)", ScriptHookContext.Current), Is.False);
            }
            var full = BattleEventHub.Record(new object(), new ResourceEventData("BondOfLife", "ST_BondOfLife", owner,
                500, 0, -900, ResourceChangeReason.Spend, false));
            using (ScriptHookContext.EnterExecution(new ScriptHookContext { Status = accumulator, Declaration = full }))
            {
                CountStatusSystem.OnReapplied(accumulator, owner, Again(accumulator));
                Assert.That(accumulator.RuntimeValue, Is.EqualTo(1500f));
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(431460302)", ScriptHookContext.Current), Is.True);
                var reward = Create(owner, 3146, 4314604, .08f, .02f, .12f);
                Assert.That(reward.RuntimeValue, Is.EqualTo(.03f).Within(.00001f));
                Assert.That(accumulator.RuntimeValue, Is.Zero);
                Assert.That(accumulator.IsActive, Is.True);
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(431460302)", ScriptHookContext.Current), Is.False);
                CountStatusSystem.OnReapplied(accumulator, owner, Again(accumulator));
                Assert.That(accumulator.RuntimeValue, Is.Zero);
            }
        }

        [TestCase(3162, 4316203, 28, "Normal")]
        [TestCase(3187, 4318703, 12, "Heavy")]
        public void EMHitCount_ReservesEachTargetAndOldCancellationCannotRestoreNewGeneration(int weapon, int id, int maximum, string type)
        {
            var owner = Owner(); owner.TotalEM = 100f;
            var enemy = Owner(); enemy.Side = BattleSide.Enemy;
            var status = Create(owner, weapon, id, .08f, 1.6f);
            status.RuntimeValue = 1f;
            using (var batch = new CountDamageBatch())
            {
                var context = new CountDamageContext { Attacker = owner, Target = enemy, SourceKind = ReactionSourceKind.CharacterSkill, DamageType = type };
                Assert.That(batch.Prepare(context).BaseDamageBonus, Is.EqualTo(160f));
                Assert.That(batch.Prepare(context).BaseDamageBonus, Is.Zero);
                CountStatusSystem.OnReapplied(status, owner, Again(status));
            }
            Assert.That(status.RuntimeValue, Is.EqualTo(maximum));
            Assert.That(status.IsActive, Is.True);
            using (var batch = new CountDamageBatch())
            {
                var context = new CountDamageContext { Attacker = owner, Target = enemy, SourceKind = ReactionSourceKind.CharacterSkill, DamageType = type };
                var plan = batch.Prepare(context);
                plan.Resolve(new DamageResolvedEvent { Target = enemy, HitLanded = true, ActualHPDamage = 0f, ShieldAbsorbed = 10f });
            }
            Assert.That(status.RuntimeValue, Is.EqualTo(maximum - 1));
        }

        [Test]
        public void Fruit_UsesLastReactionPhaseAndNeverDecaysTwiceInTheSamePhase()
        {
            var battle = HookTestEnv.CreateBattleManager(); objects.Add(battle.gameObject);
            typeof(BattleManager).GetProperty("CurrentPhase").SetValue(battle, (TurnPhase)5);
            typeof(BattleManager).GetProperty("TurnCount").SetValue(battle, 2);
            var owner = Owner();
            var occurrence = new ReactionOccurrence { SourceEntity = owner, Type = ReactionType.Vaporize };
            var reaction = BattleEventHub.Record(occurrence, new ReactionEventData(occurrence));
            StatusInstance status;
            using (ScriptHookContext.EnterExecution(new ScriptHookContext { Declaration = reaction }))
            {
                status = Create(owner, 3143, 4314302, 24f);
                CountStatusSystem.OnReapplied(status, owner, Again(status));
                Assert.That(status.RuntimeValue, Is.EqualTo(1f));
            }
            CountStatusSystem.OnItsTurn(status, owner);
            Assert.That(status.RuntimeValue, Is.EqualTo(1f));
            typeof(BattleManager).GetProperty("TurnCount").SetValue(battle, 3);
            CountStatusSystem.OnItsTurn(status, owner);
            Assert.That(owner.HasStatusInstance(status), Is.False);
            Assert.That(status.RuntimeValue, Is.Zero);
            // 被移除实例的重复阶段入口不得访问已清理的私有状态。
            CountStatusSystem.OnItsTurn(status, owner);
            Assert.That(status.RuntimeValue, Is.Zero);
        }

        [TestCase(3158, 4315804)]
        [TestCase(3173, 4317304)]
        [TestCase(3183, 4318304)]
        public void TeamReward_LatestWholeSourceRemovesOldParentAndItsConcreteChildren(int weapon, int id)
        {
            var battle = HookTestEnv.CreateBattleManager(); objects.Add(battle.gameObject);
            var first = Owner(); first.SlotPosition = 1; battle.RegisterAlly(first.CharacterCtrl);
            var second = Owner(); second.SlotPosition = 2; battle.RegisterAlly(second.CharacterCtrl);
            var old = Create(first, weapon, id, .1f, .2f, .3f);
            var child = new StatusInstance { StatusID2 = "old-team-child", IsActive = true };
            second.StatusDict.Add(child); old.BoundStatuses.Add(child);
            var untouched = new StatusInstance { StatusID2 = "another-weapon-reward", IsActive = true };
            first.StatusDict.Add(untouched);
            var latest = Create(second, weapon, id, .05f, .1f, .15f);
            Assert.That(first.HasStatusInstance(old), Is.False);
            Assert.That(second.HasStatusInstance(child), Is.False);
            Assert.That(first.HasStatusInstance(untouched), Is.True);
            Assert.That(latest.RuntimeValue, Is.EqualTo(1f));
            Assert.That(latest.RemainingPhaseCount, Is.EqualTo(3));
            second.RemoveStatusInstance(latest);
            Assert.That(first.HasStatusInstance(old), Is.False);
        }

        [TestCase(3063, 4306301, 4306302)]
        [TestCase(3114, 4311401, 4311402)]
        public void Lithic_ReadsFullRegisteredNationRosterIncludingDeadAndZeroDoesNotFallBack(int weapon, int attackID, int criticalID)
        {
            var battle = HookTestEnv.CreateBattleManager(); objects.Add(battle.gameObject);
            typeof(BattleManager).GetProperty("CurrentPhase").SetValue(battle, (TurnPhase)2);
            BattleEntity owner = null;
            int[] ids = { 1022, 1072, 1030, 1009 };
            for (int i = 0; i < ids.Length; i++)
            {
                var member = Owner(); member.EntityID = ids[i]; member.SlotPosition = i + 1;
                battle.RegisterAlly(member.CharacterCtrl);
                data.CharacterOverviewList.Add(new CharacterOverviewData
                    { CharacterID = ids[i], Nation = i < 2 ? "璃月" : i == 2 ? "至冬" : "蒙德" });
                if (i == 0) owner = member;
                if (i == 1) member.CurrentHP = 0f; // 阵亡仍属编队，不能少算这一名。
            }
            foreach (int id in new[] { attackID, criticalID })
            {
                var context = new WeaponRuntimeContext { WeaponID = weapon, Equipper = owner, Refinement = 1,
                    SourceInstanceID = "LithicTest:" + weapon, ResolvedParams = new Dictionary<int,float> { [1] = .07f, [2] = .03f } };
                var status = new StatusInstance
                {
                    StatusID2 = DataManager.WeaponStatusKey(id), IsActive = true,
                    WeaponContext = context, MainData = new StatusMainData
                        { StatusID = id, MultiplierPart = id == attackID ? "ATKBonus" : "CritRate", MaxCount = 1 }
                };
                status.RecordSource(owner); owner.StatusDict.Add(status);
                int effectID = id * 100 + 1;
                data.StatusEffectDict.Add("STE_Weapon_" + effectID, new StatusEffectData
                    { StatusEffectID = effectID, EffectType = "Buff", Param1 = id == attackID ? "Index(1)" : "Index(2)", DamageHook = "Hook(" + effectID + ")" });
            }
            Assert.That(owner.GetStatusATKBonus("", "", 0, "Pyro"), Is.EqualTo(.14f).Within(.00001f));
            Assert.That(owner.GetStatusCritRate("", "", 0, "Pyro"), Is.EqualTo(.06f).Within(.00001f));
            foreach (var metadata in data.CharacterOverviewList) metadata.Nation = "蒙德";
            Assert.That(owner.GetStatusATKBonus("", "", 0, "Pyro"), Is.Zero);
            Assert.That(owner.GetStatusCritRate("", "", 0, "Pyro"), Is.Zero);
        }
    }
}
