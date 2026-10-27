using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>剩余武器的真实内存战斗入口回归，只编写，按项目规则未运行Unity。</summary>
    public sealed class WeaponRemainingRegressionTests
    {
        private static string Key(int id) => DataManager.WeaponStatusKey(id);
        private static string EffectKey(int id) => "STE_Weapon_" + id;
        private static StatusMainData Main(BattleFlowTestEnv env, int id, bool count = false, string part = null, string hook = null)
        {
            var data = new StatusMainData { StatusID = id, StatusID2 = Key(id), StatusName = "RemainingWeaponTest",
                StatusType = count ? "Count" : "Buff", MaxCount = 1, MaxStack = 1, WhenMax = "0", ScriptHook = hook };
            if (part == "DMGBonus") data.MultiplierPart2 = part; else data.MultiplierPart1 = part;
            env.DataManager.StatusMainDict[data.StatusID2] = data; return data;
        }
        private static StatusEffectData Effect(BattleFlowTestEnv env, int id, string type, string parameter,
            string target = "Self", int duration = 0)
        {
            var data = new StatusEffectData { StatusEffectID = id, StatusEffectID2 = EffectKey(id), EffectType = type,
                Param1 = parameter, TargetType = target, Duration = duration, Element = type == "Damage" ? "Physical" : "None" };
            env.DataManager.StatusEffectDict[data.StatusEffectID2] = data; return data;
        }
        private static StatusActionData Action(BattleFlowTestEnv env, int id, string effects = null, string actionType = "OnTrigger")
        {
            if (!env.DataManager.StatusActionDict.TryGetValue(Key(id), out var rows))
                env.DataManager.StatusActionDict[Key(id)] = rows = new List<StatusActionData>();
            var data = new StatusActionData { StatusID2 = Key(id), ActionType = actionType, Param1 = effects };
            rows.Add(data); return data;
        }
        private static WeaponRuntimeContext Weapon(BattleEntity owner, int id, params float[] parameters)
        {
            var context = new WeaponRuntimeContext { WeaponID = id, Equipper = owner, Refinement = 1,
                SourceInstanceID = "Remaining:" + id + ":" + owner.SlotPosition };
            for (int i = 0; i < parameters.Length; i++) context.ResolvedParams[i + 1] = parameters[i];
            return context;
        }
        private static StatusInstance Add(BattleFlowTestEnv env, BattleEntity owner, int id, WeaponRuntimeContext weapon)
            => owner.AddStatus(Key(id), owner, 999, 2, 0, env.DataManager.StatusMainDict[Key(id)], weapon);
        private static SequenceBattleRandomSource Start(BattleFlowTestEnv env)
        {
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random); return random;
        }
        private static void Next(BattleFlowTestEnv env)
        {
            int turn = env.Manager.TurnCount; env.Manager.EndAllyTurn();
            env.Flow.AdvanceUntil(env.Manager, () => env.Manager.TurnCount > turn && env.Manager.CurrentPhase == TurnPhase.AllyAction
                && env.Manager.IsCurrentPhaseReady, "next weapon turn");
        }
        private static void QualificationTables(BattleFlowTestEnv env, int weapon)
        {
            int id = (40000 + weapon) * 100 + 2;
            Main(env, id, true, weapon == 3185 ? "ATKBonus" : "DMGBonus", weapon == 3159 ? "Hook(4315901)" : null);
            if (weapon == 3184) env.DataManager.StatusMainDict[Key(id)].ApplyDamageType = "Normal";
            Effect(env, id * 100 + 1, "Buff", "Count(" + Key(id) + ")");
            Effect(env, id * 100 + 2, "ApplyStatus", Key(id));
            var normal = Action(env, id, EffectKey(id * 100 + 2)); normal.SetTriggerLimits("Hit", "1");
            if (weapon == 3185) normal.OutgoingHit = "Normal;Heavy;Skill;Burst";
            else
            {
                normal.OutgoingDamage = "Normal"; if (weapon == 3159) normal.ScriptHook = "Hook(431590202)";
                Effect(env, id * 100 + 3, "ApplyStatus", Key(id)); Effect(env, id * 100 + 4, "ApplyStatus", Key(id));
                Action(env, id, EffectKey(id * 100 + 3)).OnAction = weapon == 3159 ? "Burst" : "Skill";
                Action(env, id, EffectKey(id * 100 + 4)).ScriptHook = "ResourceChanged(Equipper);Hook(" + (id * 100 + 4) + ")";
            }
            Action(env, id, actionType: "OnItsTurn");
        }

        [Test, Timeout(12000)]
        public void Jade_RealSwitchesUseWholeRounds_PreserveQualificationOffField_AndHPUsesOneAdditiveRegion()
        {
            using (var env = new BattleFlowTestEnv())
            {
                Main(env, 4314501, true, "HPBonus"); Main(env, 4314502, part: "EMBonus");
                Effect(env, 431450101, "Buff", "Index(1)*Count(" + Key(4314501) + ")");
                Effect(env, 431450102, "ApplyStatus", Key(4314501)); Effect(env, 431450103, "BindStatus", Key(4314502));
                Effect(env, 431450201, "Buff", "Index(2)*Count(" + Key(4314501) + ")");
                Action(env, 4314501, EffectKey(431450102)).ScriptHook = "SwitchIn(Equipper)";
                Action(env, 4314501, EffectKey(431450102)).ScriptHook = "SwitchOut(Equipper)";
                Action(env, 4314501, actionType: "OnItsTurn");
                env.CreateMinimalBattle(); var jade = env.CreateAlly(2); var owner = jade.Entity;
                owner.CurrentHP = 500f; var count = Add(env, owner, 4314501, Weapon(owner, 3145, .32f, 40f)); Start(env);
                Assert.That(count.RuntimeValue, Is.Zero); Assert.That(owner.GetFinalHP(), Is.EqualTo(1000f));
                Next(env); Assert.That(count.RuntimeValue, Is.EqualTo(1f));
                Assert.That(owner.GetFinalHP(), Is.EqualTo(1320f).Within(.001f)); Assert.That(owner.GetFinalEM(), Is.EqualTo(40f));
                Assert.That(owner.CurrentHP, Is.EqualTo(500f), "上限增加不伪造治疗");
                Assert.That(env.Manager.TrySwitchActiveAlly(1), Is.True); Next(env);
                Assert.That(count.RuntimeValue, Is.EqualTo(1f), "实际切入后只过1回合不能失效");
                Assert.That(env.Manager.TrySwitchActiveAlly(0), Is.True); Next(env); Next(env);
                Assert.That(count.RuntimeValue, Is.EqualTo(1f), "后台不继续消耗前台期限");
                Assert.That(env.Manager.TrySwitchActiveAlly(1), Is.True); owner.Heal(10000f, owner);
                Assert.That(owner.CurrentHP, Is.EqualTo(1320f).Within(.001f));
                Next(env); Assert.That(count.RuntimeValue, Is.EqualTo(1f)); Next(env);
                Assert.That(count.RuntimeValue, Is.Zero); Assert.That(owner.GetFinalHP(), Is.EqualTo(1000f));
                Assert.That(owner.GetFinalEM(), Is.Zero); Assert.That(owner.CurrentHP, Is.EqualTo(1000f));
                Assert.That(owner.GetStatus(Key(4314501)), Is.SameAs(count));
                Assert.That(count.BoundStatuses, Has.Count.EqualTo(1));
            }
        }

        [TestCase(3159), TestCase(3184), Timeout(11000)]
        public void ThreeQualifications_InitialEnergyAndRealSpendGain_OnlyExpireTheCorrespondingQualification(int weapon)
        {
            using (var env = new BattleFlowTestEnv())
            {
                QualificationTables(env, weapon);
                int countID = (40000 + weapon) * 100 + 2;
                int actionType = weapon == 3159 ? 3 : 2;
                env.ConfigureAllyAction(0, "SK_Normal_Qualifications", "SE_Normal_Qualifications", "Hydro", ".1*TotalATK,0", 1);
                env.ConfigureAllyAction(actionType, "SK_Burst_Qualifications", "SE_Burst_Qualifications", "Physical", ".1*TotalATK,0", 1);
                env.CreateMinimalBattle(); var owner = env.Ally.Entity;
                var count = Add(env, owner, countID, Weapon(owner, weapon, .12f, .08f, .16f, .28f)); Start(env);
                Assert.That(count.RuntimeValue, Is.EqualTo(.08f));
                Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True); Assert.That(count.RuntimeValue, Is.EqualTo(.16f));
                Assert.That(actionType == 3 ? env.Manager.UseBurstBySlot(0) : env.Manager.UseSkillBySlot(0), Is.True);
                Assert.That(count.RuntimeValue, Is.EqualTo(.28f));
                Next(env); Assert.That(count.RuntimeValue, Is.EqualTo(.16f));
                Next(env); Assert.That(count.RuntimeValue, Is.EqualTo(.16f)); Next(env);
                Assert.That(count.RuntimeValue, Is.EqualTo(.08f), "施放资格满3回合才清，能量资格不随它清");
                env.Ally.SetSkillEffects("SK_Normal_Qualifications", new List<SkillEffectData>
                { new SkillEffectData { SkillEffectID2 = "SE_Normal_QualificationsEnergy", EffectType = "GainEnergy",
                    TargetType = "Self", Param1 = "100", Param2 = "Flat" } });
                Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True); Assert.That(count.RuntimeValue, Is.Zero);
                env.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID][1].EnergyUsed = 20;
                Assert.That(actionType == 3 ? env.Manager.UseBurstBySlot(0) : env.Manager.UseSkillBySlot(0), Is.True);
                Assert.That(count.RuntimeValue, Is.EqualTo(.16f));
                Assert.That(count.IsPermanent, Is.True); Assert.That(count.StackCount, Is.EqualTo(1));
            }
        }

        [Test, Timeout(10000)]
        public void PolarStar_ShieldHitsCount_ButRepeatedNormalDoesNotInventOtherSources()
        {
            using (var env = new BattleFlowTestEnv())
            {
                QualificationTables(env, 3185);
                for (int type = 0; type < 4; type++) env.ConfigureAllyAction(type, "SK_Polar" + type, "SE_Polar" + type,
                    "Physical", ".1*TotalATK,0", 1);
                env.CreateMinimalBattle(); var owner = env.Ally.Entity;
                var count = Add(env, owner, 4318502, Weapon(owner, 3185, .12f, .10f, .20f, .30f, .48f)); Start(env);
                env.Enemy.Entity.AddShield(100000f, "", 10f);
                Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True); Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                Assert.That(count.RuntimeValue, Is.EqualTo(.10f));
                Assert.That(env.Manager.UseHeavyAttackBySlot(0), Is.True); Assert.That(count.RuntimeValue, Is.EqualTo(.20f));
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True); Assert.That(env.Manager.UseBurstBySlot(0), Is.True);
                Assert.That(count.RuntimeValue, Is.EqualTo(.48f)); Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(1000f));
                Next(env); Next(env); Next(env); Assert.That(count.RuntimeValue, Is.Zero);
                Assert.That(owner.GetStatus(Key(4318502)), Is.SameAs(count));
            }
        }

        [Test, Timeout(9000)]
        public void Cashflow_ThreeDistinctHPChangesInOneBatchReachThree_AndOnlyThenBindAPReduction()
        {
            using (var env = new BattleFlowTestEnv())
            {
                Main(env, 4321502); Main(env, 4321503, true, "DMGBonus"); Main(env, 4321504, part: "DMGBonus");
                Main(env, 4321505); Main(env, 4321506);
                Effect(env, 432150201, "ApplyStatus", Key(4321503), duration: 1);
                Action(env, 4321502, EffectKey(432150201)).ScriptHook = "Hook(4321502)";
                Effect(env, 432150302, "BindStatus", Key(4321504)); Effect(env, 432150303, "BindStatus", Key(4321505));
                Effect(env, 432150304, "BindStatus", Key(4321506)).ScriptHook = "Hook(432150304)";
                Effect(env, 432150601, "APCost", "-2").Param2 = "Normal";
                Effect(env, 432150602, "APCost", "-3").Param2 = "Heavy";
                env.ConfigureAllyAction(0, "SK_CashNormal", "SE_CashNormal", "Physical", ".1*TotalATK,0", 20);
                env.ConfigureAllyAction(1, "SK_CashHeavy", "SE_CashHeavy", "Physical", ".1*TotalATK,0", 30);
                env.CreateMinimalBattle(); var owner = env.Ally.Entity; Start(env);
                Add(env, owner, 4321502, Weapon(owner, 3215, .16f, .16f, .14f));
                Assert.That(owner.HasStatus(Key(4321506)), Is.False);
                using (var batch = new HitBatch())
                    for (int i = 0; i < 3; i++) batch.Resolve(new HitRequest { Target = owner, DamageBeforeShield = 10f,
                        Element = "True", Source = DamageSourceInfo.Create(env.Enemy.Entity, ReactionSourceKind.EnemySkill, "", ""), TargetIndex = i });
                var count = owner.GetStatus(Key(4321503)); Assert.That(count.RuntimeValue, Is.EqualTo(3f));
                Assert.That(count.BoundStatuses.Count(s => s.StatusID2 == Key(4321506) && s.IsActive), Is.EqualTo(1));
                Assert.That(env.Ally.GetBoundSkillAPCost(0), Is.EqualTo(18)); Assert.That(env.Ally.GetBoundSkillAPCost(1), Is.EqualTo(27));
                owner.TakeDamage(1f, DamageSourceInfo.Create(env.Enemy.Entity, ReactionSourceKind.EnemySkill, "", ""));
                Assert.That(count.BoundStatuses.Count(s => s.StatusID2 == Key(4321506) && s.IsActive), Is.EqualTo(1));
                Next(env); Assert.That(owner.HasStatus(Key(4321503)), Is.False); Assert.That(owner.HasStatus(Key(4321506)), Is.False);
                Assert.That(env.Ally.GetBoundSkillAPCost(0), Is.EqualTo(20));
            }
        }

        [Test, Timeout(9000)]
        public void Pride_OneVacuumPerLogicalHit_AllHitTargetsFinishBeforeOneUseIsSpent()
        {
            using (var env = new BattleFlowTestEnv())
            {
                Main(env, 4317102); Main(env, 4317103, true);
                Effect(env, 431710201, "ApplyStatus", Key(4317103), duration: 5);
                Action(env, 4317102, EffectKey(431710201)).OnAction = "Burst";
                var action = Action(env, 4317103, EffectKey(431710301)); action.OutgoingHit = "Normal;Heavy";
                action.ScriptHook = "Hook(4317103)"; action.SetTriggerLimits("Hit", "1");
                var vacuum = Effect(env, 431710301, "Damage", "Index(2)*TotalATK,0", "Enemy");
                vacuum.TargetSelect = "-1"; vacuum.TargetOverride = "Hook(431710301)";
                env.ConfigureAllyAction(0, "SK_PrideNormal", "SE_PrideNormal", "Physical", ".1*TotalATK,0", 1);
                env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_0"][1].Hits2 = ".1*TotalATK,0";
                env.ConfigureAllyAction(3, "SK_PrideBurst", "SE_PrideBurst", "Physical", ".1*TotalATK,0", 1);
                env.CreateMinimalBattle(); env.CreateEnemy(2); var owner = env.Ally.Entity;
                Add(env, owner, 4317102, Weapon(owner, 3171, .08f, .8f)); var random = Start(env);
                Assert.That(env.Manager.UseBurstBySlot(0), Is.True);
                var count = owner.GetStatus(Key(4317103)); Assert.That(count.RuntimeValue, Is.EqualTo(8f));
                var during = new List<float>(); var slots = new List<int>();
                using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
                {
                    if (hit.Source.SourceEffectID != EffectKey(431710301)) return;
                    during.Add(count.RuntimeValue); slots.Add(hit.Subject.SlotPosition);
                }, 0);
                env.Ally.ForcedTargetPositions.AddRange(new[] { 1, 2 });
                Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                Assert.That(slots, Is.EqualTo(new[] { 1, 2, 1, 2 }));
                Assert.That(during, Is.EqualTo(new[] { 8f, 8f, 7f, 7f })); Assert.That(count.RuntimeValue, Is.EqualTo(6f));
                count.RuntimeValue = 1f;
                env.Ally.ForcedTargetPositions.AddRange(new[] { 1, 2 });
                Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                Assert.That(slots, Has.Count.EqualTo(6)); Assert.That(owner.HasStatus(Key(4317103)), Is.False);
                Assert.That(random.FloatCallCount, Is.EqualTo(15)); Assert.That(random.IntCallCount, Is.Zero);
            }
        }

        [Test, Timeout(10000)]
        public void Verdict_OnePartyShieldBatchGivesOneSigil_AndFirstWholeSkillDamageConsumesBeforeSecondEffect()
        {
            using (var env = new BattleFlowTestEnv())
            {
                Main(env, 4317602); Main(env, 4317603, true, "DMGBonus").ApplyDamageType = "Skill";
                Effect(env, 431760201, "ApplyStatus", Key(4317603), duration: 4);
                Action(env, 4317602, EffectKey(431760201)).ScriptHook = "Hook(4317602)";
                Effect(env, 431760301, "Buff", "Index(2)*Count(" + Key(4317603) + ")");
                Effect(env, 431760302, "RemoveStatus", Key(4317603));
                var remove = Action(env, 4317603, EffectKey(431760302));
                remove.ScriptHook = "PostSelfDamage(4317603)"; remove.OutgoingDamage = "Skill";
                env.ConfigureAllyAction(2, "SK_Verdict", "SE_Verdict", "Physical", ".1*TotalATK,0", 1);
                env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_2"][1].Hits2 = ".1*TotalATK,0";
                env.DataManager.SkillEffectDict["SE_Verdict_Second"] = new SkillEffectData
                { SkillEffectID2 = "SE_Verdict_Second", EffectIndex = 1, EffectType = "Damage", DamageType = "Skill", Element = "Physical",
                    Param1 = ".1*TotalATK,0", TargetType = "Enemy", TargetNumber = 1, TargetConsecutive = 1, TargetConsecutiveSet = true };
                env.CreateMinimalBattle(); env.CreateAlly(2); env.CreateEnemy(2); var owner = env.Ally.Entity;
                Add(env, owner, 4317602, Weapon(owner, 3176, .2f, .18f)); var random = Start(env);
                CrystallizeShieldSystem.ApplyPartyShield(env.Manager, "Pyro", 100f, 2);
                var count = owner.GetStatus(Key(4317603)); Assert.That(count.RuntimeValue, Is.EqualTo(1f), "给两名队员授盾仍只有一份约印");
                CrystallizeShieldSystem.ApplyPartyShield(env.Manager, "Hydro", 100f, 2);
                Assert.That(count.RuntimeValue, Is.EqualTo(2f), "第二次独立授盾不可被同hit或阶段合并");
                var during = new List<float>();
                using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
                { if (hit.Source.SourceSkillID == "SK_Verdict") during.Add(owner.GetStatus(Key(4317603))?.RuntimeValue ?? -1f); }, 0);
                env.Ally.ForcedTargetPositions.AddRange(new[] { 1, 2 }); Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                Assert.That(during, Is.EqualTo(new[] { 2f, 2f, 2f, 2f, -1f, -1f }));
                Assert.That(owner.HasStatus(Key(4317603)), Is.False); Assert.That(owner.HasStatus(Key(4317602)), Is.True);
                Assert.That(random.FloatCallCount, Is.EqualTo(6)); Assert.That(random.IntCallCount, Is.Zero);
            }
        }

        [Test, Timeout(9000)]
        public void Harp_CountsWholeEffects_NotHitsOrItsOwnSplash_AndTheNthEffectCanProcImmediately()
        {
            using (var env = new BattleFlowTestEnv())
            {
                Main(env, 4318202, true);
                Effect(env, 431820201, "ApplyStatus", Key(4318202));
                var splash = Effect(env, 431820202, "Damage", "1.25*TotalATK,0", "Enemy");
                splash.Param2 = "Splash(1;1,1)"; splash.TargetSelect = "1"; splash.TargetConsecutive = 4;
                splash.TargetOverride = "PostSelfDamage(4318202)";
                var action = Action(env, 4318202, EffectKey(431820201) + ";" + EffectKey(431820202));
                action.OutgoingHit = "All"; action.ScriptHook = "PostSelfDamage(4318202);Hook(4318202)";
                env.ConfigureAllyAction(0, "SK_Harp", "SE_Harp", "Physical", ".1*TotalATK,0", 1);
                env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_0"][1].Hits2 = ".1*TotalATK,0";
                env.CreateMinimalBattle(); env.CreateEnemy(2); var owner = env.Ally.Entity;
                var count = Add(env, owner, 4318202, Weapon(owner, 3182, .2f, 1f, 2f)); var random = Start(env);
                int extras = 0; var sources = new HashSet<long>(); var added = new HashSet<long>();
                using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (header, hit) =>
                {
                    if (hit.Source.SourceEffectID == EffectKey(431820202)) { extras++; added.Add(header.EffectExecutionId); }
                    else if (hit.Source.SourceEffectID == "SE_Harp") sources.Add(header.EffectExecutionId);
                }, 0);
                for (int i = 0; i < 3; i++)
                {
                    env.Ally.ForcedTargetPositions.AddRange(new[] { 1, 2 }); Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                    Assert.That(count.RuntimeValue, Is.EqualTo(i == 1 ? 1f : 2f));
                    Assert.That(extras, Is.EqualTo(i == 2 ? 4 : 2));
                }
                Assert.That(added, Has.Count.EqualTo(2)); Assert.That(sources, Has.Count.EqualTo(3));
                Assert.That(added.Overlaps(sources), Is.False, "追加效果不能沿用父声明的执行编号");
                Assert.That(random.FloatCallCount, Is.EqualTo(18), "16个目标暴击判定及2次完整效果概率判定");
                Assert.That(random.IntCallCount, Is.EqualTo(2), "每次成功只抽一次命中中心");
            }
        }

        [Test, Timeout(14000)]
        public void Heartstrings_IndependentSourcesChooseOneHPTier_OnlyBurstCriticalPersistsForOneMoreRound()
        {
            using (var env = new BattleFlowTestEnv())
            {
                Main(env, 4318901, true, "HPBonus"); Main(env, 4318902, part: "CritRate", hook: "Hook(4318902)").ApplyDamageType = "Burst";
                Effect(env, 431890101, "Buff", "Count(" + Key(4318901) + ")");
                Effect(env, 431890102, "BindStatus", Key(4318902)); Effect(env, 431890201, "Buff", "Index(4)");
                for (int id = 431890103; id <= 431890105; id++) Effect(env, id, "ApplyStatus", Key(4318901));
                Action(env, 4318901, EffectKey(431890103)).OnAction = "Skill";
                Action(env, 4318901, EffectKey(431890104)).ScriptHook = "ResourceChanged(Equipper);Hook(431890104)";
                Action(env, 4318901, EffectKey(431890105)).WhenHealing = "All";
                Action(env, 4318901, actionType: "OnItsTurn");
                env.ConfigureAllyAction(2, "SK_HeartSkill", "SE_HeartSkill", "Physical", ".1*TotalATK,0", 1);
                env.CreateMinimalBattle(); var owner = env.Ally.Entity;
                var count = Add(env, owner, 4318901, Weapon(owner, 3189, .12f, .24f, .4f, .28f)); Start(env);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True); Assert.That(count.RuntimeValue, Is.EqualTo(.12f));
                BondOfLifeStatusHandler.AddValue(owner, 1f); Assert.That(count.RuntimeValue, Is.EqualTo(.24f));
                owner.Heal(10f, owner); Assert.That(count.RuntimeValue, Is.EqualTo(.4f));
                Assert.That(owner.GetFinalHP(), Is.EqualTo(1400f).Within(.001f));
                var context = new ScriptHookContext { Caster = owner, Status = owner.GetStatus(Key(4318902)), StatusHost = owner };
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(4318902)", context), Is.True);
                for (int i = 0; i < 5; i++) Next(env);
                Assert.That(count.RuntimeValue, Is.EqualTo(.24f)); Assert.That(owner.GetFinalHP(), Is.EqualTo(1240f).Within(.001f));
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(4318902)", context), Is.True);
                Next(env); Assert.That(count.RuntimeValue, Is.Zero);
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(4318902)", context), Is.False);
                Assert.That(owner.GetStatus(Key(4318901)), Is.SameAs(count));
            }
        }

        [Test, Timeout(10000)]
        public void BlazingSuns_FourElementalHitsExtendOnce_NotOncePerTarget_AndBlessingOnlyAddsAttributes()
        {
            using (var env = new BattleFlowTestEnv())
            {
                Main(env, 4317801); Main(env, 4317802, true, "CritDMG"); Main(env, 4317803, part: "ATKBonus");
                Main(env, 4317804, part: "CritDMG", hook: "Hook(4317804)"); Main(env, 4317805, part: "ATKBonus", hook: "Hook(4317804)");
                Effect(env, 431780101, "ApplyStatus", Key(4317802), duration: 2);
                Action(env, 4317801, EffectKey(431780101)).OnAction = "Skill;Burst";
                Effect(env, 431780201, "Buff", "Index(1)"); Effect(env, 431780301, "Buff", "Index(2)");
                Effect(env, 431780401, "Buff", "0.75*Index(1)"); Effect(env, 431780501, "Buff", "0.75*Index(2)");
                Effect(env, 431780202, "BindStatus", Key(4317803)); Effect(env, 431780203, "BindStatus", Key(4317804));
                Effect(env, 431780204, "BindStatus", Key(4317805));
                env.ConfigureAllyAction(2, "SK_SunsSkill", "SE_SunsSkill", "Physical", ".1*TotalATK,0", 1);
                env.ConfigureAllyAction(0, "SK_SunsNormal", "SE_SunsNormal", "Pyro", ".1*TotalATK,0", 1);
                var levels = env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_0"][1];
                levels.Hits2 = levels.Hits3 = levels.Hits4 = ".1*TotalATK,0";
                env.CreateMinimalBattle(); env.CreateEnemy(2); var owner = env.Ally.Entity;
                Add(env, owner, 4317801, Weapon(owner, 3178, .2f, .28f)); Start(env);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True); var count = owner.GetStatus(Key(4317802));
                env.Ally.ForcedTargetPositions.AddRange(new[] { 1, 2 }); Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                Assert.That(count.RuntimeValue, Is.EqualTo(4f)); Assert.That(count.RemainingPhaseCount, Is.EqualTo(3));
                Assert.That(count.BoundStatuses.Select(s => s.RemainingPhaseCount), Is.All.EqualTo(3));
                Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True); Assert.That(count.RemainingPhaseCount, Is.EqualTo(3));
                var extra = owner.GetStatus(Key(4317805)); var context = new ScriptHookContext { Caster = owner, Status = extra, StatusHost = owner };
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(4317804)", context), Is.False);
                env.ConfigureStatus(NightSoulStatusHandler.BlessingStatusID);
                owner.AddStatus(NightSoulStatusHandler.BlessingStatusID, owner, 0, 2, 0,
                    env.DataManager.StatusMainDict[NightSoulStatusHandler.BlessingStatusID]);
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(4317804)", context), Is.True);
                Assert.That(count.RemainingPhaseCount, Is.EqualTo(3)); Assert.That(count.RuntimeValue, Is.EqualTo(4f));
            }
        }

        [Test, Timeout(12000)]
        public void Absolution_ExtendsConcreteBuffsOnly_ExpiredBuffsDoNotRevive_AndSwitchOutClearsWithoutResettingMainWindow()
        {
            using (var env = new BattleFlowTestEnv())
            {
                Main(env, 4320501); Main(env, 4320502, true); Main(env, 4320503); Main(env, 4320504); Main(env, 4320505);
                Effect(env, 432050101, "ApplyStatus", Key(4320502), duration: 4);
                Effect(env, 432050102, "ApplyStatus", Key(4320503), duration: 1);
                Effect(env, 432050103, "ApplyStatus", Key(4320504), duration: 1);
                Effect(env, 432050302, "BindStatus", Key(4320505));
                var start = Action(env, 4320501, EffectKey(432050101) + ";" + EffectKey(432050102) + ";" + EffectKey(432050103));
                start.OnAction = "Skill"; start.Cooldown = 4;
                env.ConfigureAllyAction(2, "SK_AbsolutionSkill", "SE_AbsolutionSkill", "Physical", ".1*TotalATK,0", 1);
                env.ConfigureAllyAction(0, "SK_AbsolutionNormal", "SE_AbsolutionNormal", "Physical", ".1*TotalATK,0", 1);
                env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_2"][1].Hits2 = ".1*TotalATK,0";
                env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_0"][1].Hits2 = ".1*TotalATK,0";
                env.CreateMinimalBattle(); env.CreateAlly(2); env.CreateEnemy(2); var owner = env.Ally.Entity;
                Add(env, owner, 4320501, Weapon(owner, 3205, .4f, .4f)); Start(env);
                env.Ally.ForcedTargetPositions.AddRange(new[] { 1, 2 }); Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                var count = owner.GetStatus(Key(4320502)); var normal = owner.GetStatus(Key(4320503));
                var skill = owner.GetStatus(Key(4320504));
                Assert.That(normal.RemainingPhaseCount, Is.EqualTo(3), "两个逻辑Skill hit各延长1，不能按四个目标次数延长");
                Assert.That(normal.BoundStatuses.Single().RemainingPhaseCount, Is.EqualTo(3));
                env.Ally.ForcedTargetPositions.AddRange(new[] { 1, 2 }); Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                Assert.That(skill.RemainingPhaseCount, Is.EqualTo(2)); Assert.That(count.RuntimeValue, Is.Zero);
                Assert.That(count.RemainingPhaseCount, Is.EqualTo(4)); Next(env); Next(env);
                Assert.That(owner.HasStatus(Key(4320504)), Is.False);
                Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True); Assert.That(owner.HasStatus(Key(4320504)), Is.False);
                int remaining = count.RemainingPhaseCount;
                Assert.That(env.Manager.TrySwitchActiveAlly(1), Is.True);
                Assert.That(owner.HasStatus(Key(4320503)), Is.False); Assert.That(normal.BoundStatuses.All(s => !s.IsActive), Is.True);
                Assert.That(count.RemainingPhaseCount, Is.EqualTo(remaining));
                Assert.That(env.Manager.TrySwitchActiveAlly(0), Is.True); Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                Assert.That(owner.HasStatus(Key(4320503)), Is.False); Assert.That(owner.HasStatus(Key(4320504)), Is.False);
            }
        }

        [Test, Timeout(12000)]
        public void Nightweaver_RealTeamLunarBloomAndOwnSkillClaimLatestGroup_PersonalBuffsStay_AndOldSourceDoesNotReturn()
        {
            using (var env = new BattleFlowTestEnv())
            {
                Main(env, 4322101, true); Main(env, 4322102, part: "EMBonus"); Main(env, 4322103, part: "EMBonus");
                Main(env, 4322104, part: "DMGBonus", hook: "Hook(432210105)").ApplyReactionType = "绽放";
                Main(env, 4322105, part: "DMGBonus", hook: "Hook(432210105)").ApplyReactionType = "超绽放;烈绽放";
                Main(env, 4322106, hook: "Hook(432210105)").MultiplierPart3 = "LunarDMGBonus";
                env.DataManager.StatusMainDict[Key(4322106)].ApplyReactionType = "月绽放";
                Effect(env, 432210101, "ApplyStatus", Key(4322102), duration: 1);
                Effect(env, 432210102, "ApplyStatus", Key(4322101));
                Effect(env, 432210103, "ApplyStatus", Key(4322103), duration: 3);
                Effect(env, 432210104, "ApplyStatus", Key(4322101));
                for (int id = 432210105; id <= 432210107; id++)
                {
                    var bind = Effect(env, id, "BindStatus", Key(4322104 + id - 432210105), "Allies");
                    bind.TargetSelect = "-1"; bind.ScriptHook = "Hook(432210105)";
                }
                for (int n = 1; n <= 6; n++) if (n != 1)
                    Effect(env, (4322100 + n) * 100 + 1, "Buff", "Index(" + (n == 2 ? 1 : n == 3 ? 2 : n - 1) + ")");
                var own = Action(env, 4322101, EffectKey(432210101) + ";" + EffectKey(432210102));
                own.OutgoingDamage = "Skill"; own.ScriptHook = "Hook(432210101)"; own.SetTriggerLimits("Hit", "1");
                Action(env, 4322101, EffectKey(432210103) + ";" + EffectKey(432210104)).ScriptHook = "Hook(432210103)";
                env.ConfigureAllyAction(2, "SK_Nightweaver", "SE_Nightweaver", "Hydro", ".1*TotalATK,1", 1);
                env.CreateMinimalBattle(); var second = env.CreateAlly(2); var first = env.Ally.Entity;
                env.Enemy.Entity.TotalHP = env.Enemy.Entity.CurrentHP = 10000f;
                var firstCount = Add(env, first, 4322101, Weapon(first, 3221, 60f, 60f, 1.2f, .8f, .4f));
                var secondCount = Add(env, second.Entity, 4322101, Weapon(second.Entity, 3221, 75f, 75f, 1.5f, 1f, .5f));
                env.ConfigureStatus(LunarReactionConversion.BloomActive);
                first.AddStatus(LunarReactionConversion.BloomActive, first, 0, 2, 0, env.DataManager.StatusMainDict[LunarReactionConversion.BloomActive]);
                Start(env); env.Enemy.Entity.ApplyAura("Dendro", 1f, first.EntityID);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                Assert.That(firstCount.RuntimeValue, Is.EqualTo(1f)); Assert.That(secondCount.RuntimeValue, Is.Zero);
                Assert.That(second.Entity.HasStatus(Key(4322103)), Is.True, "队友真实月绽放也发朔月个人Buff");
                Assert.That(env.Manager.TrySwitchActiveAlly(1), Is.True);
                Assert.That(env.Manager.UseSkillBySlot(1), Is.True);
                Assert.That(firstCount.RuntimeValue, Is.Zero); Assert.That(secondCount.RuntimeValue, Is.EqualTo(1f));
                Assert.That(first.GetFinalEM(), Is.EqualTo(120f)); Assert.That(second.Entity.GetFinalEM(), Is.EqualTo(150f));
                Assert.That(first.GetStatus(Key(4322104)).Caster, Is.SameAs(second.Entity));
                Assert.That(first.GetStatusDMGBonus("", "", 2, "Hydro", env.Enemy.Entity), Is.Zero, "反应专属奖励不能漏入普通直伤");
                second.Entity.RemoveStatus(Key(4322102));
                Assert.That(secondCount.RuntimeValue, Is.Zero); Assert.That(firstCount.RuntimeValue, Is.Zero);
                Assert.That(first.HasStatus(Key(4322104)), Is.False, "最新来源失效不能恢复旧来源");
                Assert.That(first.HasStatus(Key(4322102)), Is.True); Assert.That(first.HasStatus(Key(4322103)), Is.True);
            }
        }

        [Test, Timeout(9000)]
        public void Tales_ActualPaymentReducesAllPools_OnlyOneNormalRecoveryPerRootEffect()
        {
            using (var env = new BattleFlowTestEnv())
            {
                Main(env, 4319101, true, "DMGBonus").ApplyDamageType = "Normal";
                Main(env, 4319102, part: "DMGBonus").ApplyDamageType = "Skill";
                Main(env, 4319103, part: "DMGBonus").ApplyDamageType = "Burst";
                Effect(env, 431910101, "Buff", "Count(" + Key(4319101) + ")");
                Effect(env, 431910102, "BindStatus", Key(4319102)); Effect(env, 431910103, "BindStatus", Key(4319103));
                Effect(env, 431910201, "Buff", "Index(1)").DamageHook = "Hook(431910201)";
                Effect(env, 431910301, "Buff", "Index(1)").DamageHook = "Hook(431910301)";
                Effect(env, 431910104, "ApplyStatus", Key(4319101));
                var action = Action(env, 4319101, EffectKey(431910104)); action.ScriptHook = "Hook(431910104)";
                action.OutgoingDamage = "Normal;Skill;Burst"; action.SetTriggerLimits("Effect", "1");
                env.ConfigureAllyAction(0, "SK_Tales", "SE_Tales", "Physical", ".1*TotalATK,0", 10);
                env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_0"][1].Hits2 = ".1*TotalATK,0";
                env.CreateMinimalBattle(); var owner = env.Ally.Entity;
                var count = Add(env, owner, 4319101, Weapon(owner, 3191, .8f, .01f, .03f, .06f)); Start(env);
                Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                Assert.That(count.RuntimeValue, Is.EqualTo(.73f).Within(.00001f));
                using (DamageQueryContext.Enter("SE_TalesSkill_Query", "Skill"))
                    Assert.That(owner.GetStatusDMGBonus("SE_TalesSkill_Query", "", 0, "Physical", env.Enemy.Entity), Is.EqualTo(.7f).Within(.00001f));
                using (DamageQueryContext.Enter("SE_TalesBurst_Query", "Burst"))
                    Assert.That(owner.GetStatusDMGBonus("SE_TalesBurst_Query", "", 0, "Physical", env.Enemy.Entity), Is.EqualTo(.7f).Within(.00001f));
            }
        }
    }
}
