using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class StatusFormulaSnapshotTests
    {
        private const int RootNumber = 4800001;
        private const string Root = "ST_FormulaSnapshot_Test", Damage = "STE_FormulaSnapshot_Damage";
        private static BattleFlowTestEnv Setup(int enemies = 1)
        {
            var e = new BattleFlowTestEnv();
            e.CreateMinimalBattle();
            for (int p = 2; p <= enemies; p++) e.CreateEnemy(p);
            e.Manager.StartBattle(); e.Flow.AdvanceToPhase(e.Manager, TurnPhase.AllyAction);
            var owner = e.Ally.Entity;
            owner.TotalATK = 100; owner.WeaponATK = owner.WeaponATKBonus = owner.CharacterATKBonus = 0;
            owner.CritRate = owner.WeaponCritRate = 0; owner.CritDMG = .5f; owner.WeaponCritDMG = 0;
            owner.TotalDEF = 100; owner.DMGBonus = owner.PyroDmgBonus = owner.WeaponPyroDmgBonus = 0;
            BattleRandom.SetSource(new SequenceBattleRandomSource());
            return e;
        }
        private static StatusInstance Buff(BattleFlowTestEnv e, int n, string part, string value,
            string hook = null, string scope = null, int cap = 1)
        {
            string id = "ST_Snapshot_Buff_" + n;
            var main = new StatusMainData { StatusID = n, StatusID2 = id, StatusType = "Buff",
                MultiplierPart2 = part, MaxCount = cap, ApplyDamageType = scope };
            e.DataManager.StatusMainDict[id] = main;
            e.DataManager.StatusEffectDict[id + "_Value"] = new StatusEffectData
            { StatusEffectID = n * 100 + 1, StatusEffectID2 = id + "_Value", EffectType = "Buff",
                Param1 = value, ScriptHook = hook, TargetType = "Self" };
            return e.Ally.Entity.AddStatus(id, e.Ally.Entity, 0, 2, 0, main);
        }
        private static StatusInstance CreateRoot(BattleFlowTestEnv e, bool snapshot = true, string kind = "Damage", string formula = "1*TotalATK,0")
        {
            var main = new StatusMainData { StatusID = RootNumber, StatusID2 = Root, MaxStack = 1, MaxCount = 1, WhenMax = "0" };
            e.DataManager.StatusMainDict[Root] = main;
            e.DataManager.StatusEffectDict[Damage] = new StatusEffectData
            { StatusEffectID = RootNumber * 100 + 1, StatusEffectID2 = Damage, EffectType = kind,
                Element = kind == "Damage" ? "Pyro" : null, DamageType = kind == "Damage" ? "Skill" : null,
                Param1 = formula, TargetType = kind == "Damage" ? "Enemy" : "Self", TargetSelect = kind == "Damage" ? "-1" : null,
                Duration = 3 };
            e.DataManager.StatusActionDict[Root] = new List<StatusActionData>
            { new StatusActionData { StatusID = RootNumber, StatusID2 = Root, ActionType = "OnItsTurn", Param1 = Damage } };
            if (snapshot) e.DataManager.StatusAttributesDict[Root] = new StatusAttributesData { StatusID = RootNumber, StatusID2 = Root };
            else e.DataManager.StatusAttributesDict.Remove(Root);
            return e.Ally.Entity.AddStatus(Root, e.Ally.Entity, 3, 2, 0, main);
        }
        private static Task Fire(BattleFlowTestEnv e, StatusInstance status)
            => BattleExecution.Run(() => e.Ally.ExecuteStatusActionsTask(status, e.DataManager.StatusActionDict[Root], status.Host, "OnItsTurn"));
        private static IDisposable DamageListener(List<float> values)
            => BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            { if (fact.Source.SourceEffectID == Damage) values.Add(fact.DamageBeforeShield); }, 0);

        [UnityTest]
        public IEnumerator Formula_FreezesAttackCountBonusAndCritical_WithoutConsumingCount()
            => HookTestEnv.AwaitTask(FreezeAsync);
        private async Task FreezeAsync()
        {
            using var e = Setup(); var owner = e.Ally.Entity;
            BondOfLifeStatusHandler.AddValue(owner, 2);
            var atk = Buff(e, 4800011, "ATKBonus", ".5"); var flat = Buff(e, 4800012, "ATKBonusFlat", "20");
            var count = Buff(e, 4800013, "DMGBonus", "0.06*Count(ST_BondOfLife)");
            var root = CreateRoot(e);
            Assert.That(root.FormulaSnapshot.Attribute("TotalATK", Damage, "Pyro"), Is.EqualTo(170));
            Assert.That(BondOfLifeStatusHandler.GetValue(owner), Is.EqualTo(2));
            owner.RemoveStatusInstance(atk); owner.RemoveStatusInstance(flat); owner.RemoveStatusInstance(count);
            BondOfLifeStatusHandler.AddValue(owner, 8);
            owner.TotalATK = 500; owner.DMGBonus = .7f; owner.CritRate = 1;
            root.Snapshot = new Dictionary<string, float> { ["ExistingWeaponCache"] = 77 };
            var damages = new List<float>(); using var listener = DamageListener(damages);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            await Fire(e, root);
            Assert.That(damages.Single(), Is.EqualTo(95.2f).Within(.001f), "170×1.12×0.5，不能重加攻击Buff或读取新Count/暴击率");
            Assert.That(BondOfLifeStatusHandler.GetValue(owner), Is.EqualTo(10));
            Assert.That(root.Snapshot["ExistingWeaponCache"], Is.EqualTo(77));
            Assert.That(random.FloatCallCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator UnmarkedStatus_ContinuesUsingLiveValues()
            => HookTestEnv.AwaitTask(LiveAsync);
        private async Task LiveAsync()
        {
            using var e = Setup(); var root = CreateRoot(e, snapshot: false);
            e.Ally.Entity.TotalATK = 300; e.Ally.Entity.DMGBonus = .1f;
            var damages = new List<float>(); using var listener = DamageListener(damages);
            await Fire(e, root);
            Assert.That(root.FormulaSnapshot, Is.Null);
            Assert.That(damages.Single(), Is.EqualTo(165).Within(.001f));
        }

        [UnityTest]
        public IEnumerator TargetCondition_UsesEachHitsPreContactAura_AfterOriginalBuffExpired()
            => HookTestEnv.AwaitTask(TargetAsync);
        private async Task TargetAsync()
        {
            using var e = Setup(2);
            var buff = Buff(e, 4800014, "DMGBonus", ".35", "Hook(451602)");
            var root = CreateRoot(e); e.Ally.Entity.RemoveStatusInstance(buff, "Expired");
            e.Manager.Enemies[1].Entity.ApplyAura("Pyro", 1, e.Ally.Entity.EntityID);
            var damages = new List<float>(); using var listener = DamageListener(damages);
            await Fire(e, root);
            Assert.That(damages, Has.Count.EqualTo(2));
            Assert.That(damages[0], Is.EqualTo(50).Within(.001f));
            Assert.That(damages[1], Is.EqualTo(67.5).Within(.001f));
        }

        [UnityTest]
        public IEnumerator ProbabilityAndCritical_AreDrawnOnlyForActualTargets_NotDuringCaptureOrLogging()
            => HookTestEnv.AwaitTask(ProbabilityAsync);
        private async Task ProbabilityAsync()
        {
            using var e = Setup(2);
            Buff(e, 4800015, "DMGBonus", ".5", "Chance(0.5)");
            var random = new SequenceBattleRandomSource(floatValues: new[] { .25f, .75f, .75f, .25f });
            BattleRandom.SetSource(random); var root = CreateRoot(e);
            Assert.That(random.FloatCallCount, Is.Zero);
            var damages = new List<float>(); using var listener = DamageListener(damages);
            await Fire(e, root);
            Assert.That(damages[0], Is.EqualTo(75).Within(.001f));
            Assert.That(damages[1], Is.EqualTo(50).Within(.001f));
            Assert.That(random.FloatCallCount, Is.EqualTo(4));
        }

        [UnityTest]
        public IEnumerator Amos_KeepsActualPreparedAP_AfterTheSnapshotWasCreated()
            => HookTestEnv.AwaitTask(AmosAsync);
        private async Task AmosAsync()
        {
            using var e = new BattleFlowTestEnv();
            e.ConfigureAllyAction(1, "SK_Snapshot_Charge", "SE_Snapshot_Charge", "", "", 30,
                effectType: "ApplyStatus", param1: Root, duration: 3, addInPhase: 2, targetType: "Self");
            const string extra = "ST_Snapshot_ExtraCharge";
            e.ConfigureAllyAction(2, "SK_Snapshot_Extra", "SE_Snapshot_Extra", "", "", 10,
                effectType: "ApplyStatus", param1: extra, targetType: "Self");
            foreach (var skill in e.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID]) skill.ActionType = "";
            var main = new StatusMainData { StatusID = RootNumber, StatusID2 = Root, MaxStack = 1, WhenMax = "0" };
            e.DataManager.StatusMainDict[Root] = main;
            e.DataManager.StatusMainDict[extra] = new StatusMainData { StatusID = 4800060, StatusID2 = extra };
            e.DataManager.StatusAttributesDict[Root] = new StatusAttributesData { StatusID = RootNumber, StatusID2 = Root };
            e.DataManager.StatusEffectDict[Damage] = new StatusEffectData { StatusEffectID = RootNumber * 100 + 1,
                StatusEffectID2 = Damage, EffectType = "Damage", Element = "Physical", DamageType = "Heavy",
                Param1 = "1*TotalATK,0", TargetType = "Enemy", TargetSelect = "-1" };
            e.DataManager.StatusActionDict[Root] = new List<StatusActionData> { new StatusActionData
                { StatusID = RootNumber, StatusID2 = Root, ActionType = "OnItsTurn", Param1 = Damage } };
            foreach (int n in new[] { 4318101, 4318102, 4318103 })
            {
                string id = DataManager.WeaponStatusKey(n);
                e.DataManager.StatusMainDict[id] = new StatusMainData { StatusID = n, StatusID2 = id,
                    StatusType = n == 4318103 ? "Count" : "Buff", MultiplierPart1 = n == 4318103 ? null : "DMGBonus",
                    ApplyDamageType = "Normal;Heavy", ScriptHook = n == 4318102 ? "Hook(4318102)" : null, MaxCount = 1, MaxStack = 1 };
                if (n == 4318103) continue;
                string effect = "STE_Weapon_" + (n * 100 + 1);
                e.DataManager.StatusEffectDict[effect] = new StatusEffectData { StatusEffectID = n * 100 + 1,
                    StatusEffectID2 = effect, EffectType = "Buff", TargetType = "Self", Param1 = n == 4318101 ? "Index(1)" : "Index(2)",
                    DamageHook = n == 4318102 ? "Hook(431810201)" : null };
            }
            e.CreateMinimalBattle(); e.Manager.StartBattle(); e.Flow.AdvanceToPhase(e.Manager, TurnPhase.AllyAction);
            var owner = e.Ally.Entity; owner.TotalATK = 100; owner.CritRate = 0;
            var context = new WeaponRuntimeContext { Equipper = owner, WeaponID = 3181, Refinement = 1, SourceInstanceID = "SnapshotAP" };
            context.ResolvedParams[1] = .12f; context.ResolvedParams[2] = .04f;
            foreach (int n in new[] { 4318101, 4318102, 4318103 })
                owner.AddStatus(DataManager.WeaponStatusKey(n), owner, 0, 2, 0, e.DataManager.StatusMainDict[DataManager.WeaponStatusKey(n)], context);
            await BattleExecution.Run(() => e.Ally.ExecuteSkillByIDAsync("SK_Snapshot_Charge", 1, true));
            var root = owner.GetStatus(Root); owner.TotalATK = 500;
            await BattleExecution.Run(() => e.Ally.ExecuteSkillByIDAsync("SK_Snapshot_Extra", 2, true));
            Assert.That(owner.GetStatus("ST_Weapon_4318103").RuntimeValue, Is.EqualTo(40));
            BattleRandom.SetSource(new SequenceBattleRandomSource());
            var damages = new List<float>(); using var listener = DamageListener(damages);
            await Fire(e, root);
            Assert.That(damages.Single(), Is.EqualTo(72).Within(.001f), "冻结100攻击，增伤为12%常驻+本次40AP的32%，不能冻结成0或30AP");
            Assert.That(owner.GetStatus("ST_Weapon_4318103").RuntimeValue, Is.Zero);
        }

        [UnityTest]
        public IEnumerator CapturePrecedesOwnBindings_AndTargetDefenseRemainsLive()
            => HookTestEnv.AwaitTask(BindingAsync);
        private async Task BindingAsync()
        {
            using var e = Setup();
            // 先建立配置；重施同一根时旧绑定被清掉，新绑定不得先进入新根的快照。
            var first = CreateRoot(e);
            var child = new StatusMainData { StatusID = 4800020, StatusID2 = "ST_Snapshot_Child", StatusType = "Buff",
                MultiplierPart2 = "DMGBonus", MaxCount = 1, MaxStack = 1 };
            e.DataManager.StatusMainDict[child.StatusID2] = child;
            e.DataManager.StatusEffectDict["STE_Snapshot_Child"] = new StatusEffectData
            { StatusEffectID = 480002001, StatusEffectID2 = "STE_Snapshot_Child", EffectType = "Buff", Param1 = ".15", TargetType = "Self" };
            e.DataManager.StatusEffectDict["STE_Snapshot_Bind"] = new StatusEffectData
            { StatusEffectID = RootNumber * 100 + 2, StatusEffectID2 = "STE_Snapshot_Bind", EffectType = "BindStatus", Param1 = child.StatusID2, TargetType = "Self" };
            var root = CreateRoot(e);
            Assert.That(first.IsActive, Is.False); Assert.That(root.BoundStatuses, Has.Count.EqualTo(1));
            var replaced = root; root = CreateRoot(e);
            Assert.That(replaced.BoundStatuses.All(childStatus => !childStatus.IsActive), Is.True);
            Assert.That(root.BoundStatuses, Has.Count.EqualTo(1));
            Assert.That(root.FormulaSnapshot.Bonus("DMGBonus", Damage, "Pyro"), Is.Zero);
            e.Manager.Enemies[0].Entity.PyroRes = .5f;
            var damages = new List<float>(); using var listener = DamageListener(damages);
            await Fire(e, root);
            Assert.That(damages.Single(), Is.EqualTo(25).Within(.001f));
            var bound = root.BoundStatuses.Single();
            Assert.That(bound.FormulaSnapshot, Is.Null);
            Assert.That(StatusFormulaSnapshot.Find(bound, Damage), Is.SameAs(root.FormulaSnapshot));
            Assert.That(StatusFormulaSnapshot.Find(bound, "STE_Snapshot_Child"), Is.Null);
            e.Ally.Entity.TotalATK = 500;
            var childActions = new List<StatusActionData> { new StatusActionData
                { StatusID = child.StatusID, StatusID2 = child.StatusID2, ActionType = "OnItsTurn", Param1 = Damage } };
            await BattleExecution.Run(() => e.Ally.ExecuteStatusActionsTask(bound, childActions, bound.Host, "OnItsTurn"));
            Assert.That(damages[1], Is.EqualTo(25).Within(.001f), "另一阶段由具体绑定子状态调用父效果时，不能丢掉父快照");
        }

        [UnityTest]
        public IEnumerator Healing_FreezesSourceButKeepsTargetHPAndIncomingHealingLive()
            => HookTestEnv.AwaitTask(HealingAsync);
        private async Task HealingAsync()
        {
            using var e = Setup(); var owner = e.Ally.Entity; owner.HealBonus = .2f;
            var root = CreateRoot(e, kind: "Heal", formula: "0.1*TotalATK+0.1*Target.TotalHP");
            owner.TotalATK = 300; owner.TotalHP = 2000; owner.CurrentHP = 400;
            owner.HealBonus = 1; owner.BeHealedBonus = .5f;
            await Fire(e, root);
            Assert.That(owner.CurrentHP, Is.EqualTo(757).Within(.001f));
        }

        [UnityTest]
        public IEnumerator Amplification_FreezesMasteryAndCountCoefficient_ButReactsWithNewAura()
            => HookTestEnv.AwaitTask(AmplificationAsync);
        private async Task AmplificationAsync()
        {
            using var e = Setup(); var owner = e.Ally.Entity; owner.TotalEM = 100;
            BondOfLifeStatusHandler.AddValue(owner, 2);
            var bonus = Buff(e, 4800031, "BaseDMGBonus", ".25*Count(ST_BondOfLife)");
            bonus.MainData.ApplyReactionType = "Vaporize";
            var root = CreateRoot(e, formula: "1*TotalATK,1");
            owner.TotalEM = 500; owner.RemoveStatusInstance(bonus); BondOfLifeStatusHandler.ClearValue(owner);
            var target = e.Enemy.Entity; target.ApplyAura("Hydro", 4, target.EntityID);
            var damages = new List<float>(); using var listener = DamageListener(damages);
            await Fire(e, root);
            float expected = 50f * 1.5f * (1f + 2.78f * 100f / 1500f + .5f);
            Assert.That(damages.Single(), Is.EqualTo(expected).Within(.001f));
            Assert.That(target.GetAura("Hydro").AuraAmount, Is.EqualTo(3.5f).Within(.001f));
            using (StatusFormulaSnapshot.Enter(root, Damage))
            {
                var saved = ReactionSourceSnapshot.Capture(new ReactionContext
                { SourceEntity = owner, SourceEffectID = Damage, AttackElement = "Pyro", Target = target }, ReactionType.Vaporize, "Pyro");
                Assert.That(saved.TotalEM, Is.EqualTo(100)); Assert.That(saved.BaseDMGBonus, Is.EqualTo(.5f));
            }
        }

        [UnityTest]
        public IEnumerator Shield_UsesFrozenDefense_WithoutBakingRecipientsShieldStrength()
            => HookTestEnv.AwaitTask(ShieldAsync);
        private async Task ShieldAsync()
        {
            using var e = Setup(); var owner = e.Ally.Entity;
            var root = CreateRoot(e, kind: "Shield", formula: "0.5*TotalDEF+10");
            owner.TotalDEF = 300; owner.ShieldStrength = .5f;
            await Fire(e, root);
            Assert.That(owner.Shields.Single().Value, Is.EqualTo(60).Within(.001f));
        }

        [UnityTest]
        public IEnumerator MemoryScope_SurvivesAwait_AndRestoresAfterNestedException()
            => HookTestEnv.AwaitTask(ScopeAsync);
        private async Task ScopeAsync()
        {
            using var e = Setup(); var root = CreateRoot(e); e.Ally.Entity.TotalATK = 300;
            using (StatusFormulaSnapshot.Enter(root, Damage))
            {
                await Task.Yield();
                Assert.That(e.Ally.GetFinalATK(Damage, 2, "Pyro"), Is.EqualTo(100));
                Assert.That(e.Ally.GetFinalATK("", 2, "Pyro"), Is.EqualTo(300), "无具体效果的面板查询不能误读当前快照");
                try
                {
                    using (StatusFormulaSnapshot.Suspend())
                    {
                        Assert.That(e.Ally.GetFinalATK(Damage, 2, "Pyro"), Is.EqualTo(300));
                        throw new InvalidOperationException("nested");
                    }
                }
                catch (InvalidOperationException) { }
                Assert.That(e.Ally.GetFinalATK(Damage, 2, "Pyro"), Is.EqualTo(100));
                var replacement = CreateRoot(e);
                Assert.That(replacement.FormulaSnapshot.Attribute("TotalATK", Damage, "Pyro"), Is.EqualTo(300),
                    "状态创建必须读取真实当前面板，不能从外层公式继承100攻击");
                Assert.That(e.Ally.GetFinalATK(Damage, 2, "Pyro"), Is.EqualTo(100));
            }
            Assert.That(StatusFormulaSnapshot.For(e.Ally.Entity), Is.Null);
        }

        [UnityTest]
        public IEnumerator DirectSkillEffectCall_IsolatesOuterSnapshot_AndThenRestoresIt()
            => HookTestEnv.AwaitTask(SkillEffectAsync);
        private async Task SkillEffectAsync()
        {
            using var e = Setup(); var root = CreateRoot(e);
            e.Ally.Entity.TotalATK = 300; e.Ally.Entity.DMGBonus = .5f;
            const string called = "SE_Snapshot_LiveNested";
            e.DataManager.SkillEffectDict[called] = new SkillEffectData { SkillEffectID2 = called,
                EffectType = "Damage", Element = "Pyro", DamageType = "Skill", Param1 = "1*TotalATK,0",
                TargetType = "Enemy", TargetNumber = 1, TargetConsecutive = 1, TargetConsecutiveSet = true };
            var actions = new List<StatusActionData> { new StatusActionData { StatusID = RootNumber,
                StatusID2 = Root, ActionType = "OnItsTurn", Param1 = called } };
            float damage = 0;
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            { if (fact.Source.SourceEffectID == called) damage = fact.DamageBeforeShield; }, 0);
            using (StatusFormulaSnapshot.Enter(root, Damage))
            {
                await BattleExecution.Run(() => e.Ally.ExecuteStatusActionsTask(root, actions, root.Host, "OnItsTurn"));
                Assert.That(damage, Is.EqualTo(225).Within(.001f));
                Assert.That(e.Ally.GetFinalATK(Damage, 2, "Pyro"), Is.EqualTo(100));
            }
        }

        [Test]
        public void DamageScopeAndMaxCount_ArePreserved_InsteadOfAddingAllBuffs()
        {
            using var e = Setup();
            Buff(e, 4800016, "DMGBonus", "2", scope: "Normal");
            Buff(e, 4800017, "DMGBonus", ".2"); Buff(e, 4800017, "DMGBonus", ".2");
            var root = CreateRoot(e);
            Assert.That(root.FormulaSnapshot.Bonus("DMGBonus", Damage, "Pyro"), Is.EqualTo(.2f));
            Assert.That(e.Ally.Entity.GetStatusList().Count(s => s.StatusID2 == "ST_Snapshot_Buff_4800017"), Is.EqualTo(2));
        }

        [Test]
        public void UnknownConditionDependency_FailsBeforeRegisteringTheNewSnapshotState()
        {
            using var e = Setup(); Buff(e, 4800032, "DMGBonus", ".5", "MissingSnapshotHook()");
            Assert.Throws<NotSupportedException>(() => CreateRoot(e));
            Assert.That(e.Ally.Entity.HasStatus(Root), Is.False);
        }
    }
}
