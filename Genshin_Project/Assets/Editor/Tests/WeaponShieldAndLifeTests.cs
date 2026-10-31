using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>四把护盾武器及3157/3161/3163/3164正式入口回归定义，不自动启动Unity。</summary>
    public sealed class WeaponShieldAndLifeTests
    {
        private static string Key(int id) => DataManager.WeaponStatusKey(id);
        private static string Eff(int id) => "STE_Weapon_" + id;
        private static StatusMainData Main(BattleFlowTestEnv env, int id, string part = null, bool count = false)
        {
            var main = new StatusMainData { StatusID = id, StatusID2 = Key(id), StatusType = count ? "Count" : "Buff",
                MultiplierPart = part, MaxStack = 1, MaxCount = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[Key(id)] = main; return main;
        }
        private static StatusEffectData Effect(BattleFlowTestEnv env, int id, string type, string formula, bool hook = false)
        {
            var effect = new StatusEffectData { StatusEffectID = id, StatusEffectID2 = Eff(id), EffectType = type,
                Param1 = formula, TargetType = "Self", DamageHook = hook ? "Hook(" + id + ")" : null };
            env.DataManager.StatusEffectDict[Eff(id)] = effect; return effect;
        }
        private static StatusActionData Action(BattleFlowTestEnv env, int id, params int[] effects)
        {
            var action = new StatusActionData { StatusID = id, StatusID2 = Key(id), ActionType = "OnTrigger",
                Param1 = string.Join(";", effects.Select(Eff)) };
            env.DataManager.StatusActionDict[Key(id)] = new List<StatusActionData> { action }; return action;
        }
        private static WeaponRuntimeContext Weapon(BattleEntity owner, int id, params float[] parameters)
        {
            var context = new WeaponRuntimeContext { Equipper = owner, WeaponID = id, Refinement = 1,
                SourceInstanceID = "ShieldLife:" + id + ":" + owner.SlotPosition };
            for (int i = 0; i < parameters.Length; i++) context.ResolvedParams[i + 1] = parameters[i]; return context;
        }
        private static StatusInstance Add(BattleFlowTestEnv env, BattleEntity owner, int id, WeaponRuntimeContext context)
            => owner.AddStatus(Key(id), owner, 0, 2, 0, env.DataManager.StatusMainDict[Key(id)], context);
        private static void Start(BattleFlowTestEnv env)
        { BattleRandom.SetSource(new SequenceBattleRandomSource()); env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction); }
        private static void Next(BattleFlowTestEnv env)
        { env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction); }
        private static float Attack(BattleEntity owner) => owner.GetStatusATKBonus("", "", -1, "");

        [TestCase(3156), TestCase(3172), TestCase(3195), TestCase(3208), Timeout(10000)]
        public void ShieldFamily_UsesItsOwnEffectOrHitLimit_ShieldChangesOnlyAttackMultiplier_RefreshesAtCap(int weapon)
        {
            int id = (40000 + weapon) * 100, expected = weapon == 3208 ? 3 : 1;
            using var env = new BattleFlowTestEnv(); Main(env, id + 1, "ShieldStrength"); Main(env, id + 2);
            Main(env, id + 3, "ATKBonus", true); Effect(env, (id + 1) * 100 + 1, "Buff", "Index(1)");
            Effect(env, (id + 3) * 100 + 1, "Buff", "Index(2)*Count(" + Key(id + 3) + ")", true);
            var action = Action(env, id + 2, (id + 2) * 100 + 1); action.OutgoingHit = "All";
            if (weapon == 3208) action.MaxTimePerHit = 1; else action.MaxTimePerEffect = 1;
            Effect(env, (id + 2) * 100 + 1, "ApplyStatus", Key(id + 3)).Duration = 2;
            env.ConfigureAllyAction(0, "SK_Summit", "SE_Summit", "Physical", ".01*TotalATK,0", 0);
            var level = env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_0"][1]; level.Hits2 = level.Hits3 = ".01*TotalATK,0";
            env.CreateMinimalBattle(); env.CreateEnemy(2); var owner = env.Ally.Entity;
            var context = Weapon(owner, weapon, .2f, .04f); Add(env, owner, id + 1, context); Add(env, owner, id + 2, context);
            Start(env); env.Enemy.Entity.AddShield(1000f, "", 5f);
            env.Ally.ForcedTargetPositions.AddRange(new[] { 1, 2 });
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            var count = owner.GetStatus(Key(id + 3)); Assert.That(count.RuntimeValue, Is.EqualTo(expected), "3hit乘2目标按该武器Hit或Effect额度");
            Assert.That(count.StackCount, Is.EqualTo(1)); Assert.That(Attack(owner), Is.EqualTo(.04f * expected).Within(.00001f));
            owner.AddShield(100f, "", 5f); Assert.That(owner.GetTotalShieldHP(), Is.EqualTo(120f).Within(.001f));
            Assert.That(Attack(owner), Is.EqualTo(.08f * expected).Within(.00001f));
            Assert.That(owner.AbsorbDamageWithShield(200f, "Physical"), Is.EqualTo(80f).Within(.001f));
            Assert.That(Attack(owner), Is.EqualTo(.04f * expected).Within(.00001f)); Assert.That(count.RuntimeValue, Is.EqualTo(expected));
            for (int i = 0; i < 4; i++) Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            Assert.That(count.RuntimeValue, Is.EqualTo(5f)); Next(env); Assert.That(count.RemainingPhaseCount, Is.EqualTo(1));
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True); Assert.That(count.RuntimeValue, Is.EqualTo(5f));
            Assert.That(count.RemainingPhaseCount, Is.EqualTo(2));
            for (int i = 0; i < 3; i++) Assert.That(Attack(owner), Is.EqualTo(.2f).Within(.00001f));
            Assert.That(count.RemainingPhaseCount, Is.EqualTo(2)); Next(env); Next(env);
            Assert.That(owner.HasStatus(Key(id + 3)), Is.False); Assert.That(Attack(owner), Is.Zero);
        }

        [Test, Timeout(10000)]
        public void JadeCutter_ReadsWearerFinalMaxHPDynamically_AsFlatAttackOutsideAttackPercent()
        {
            using var env = new BattleFlowTestEnv(); Main(env, 4315701, "HPBonus"); Main(env, 4315702, "ATKBonusFlat");
            Effect(env, 431570101, "Buff", "Index(1)"); var conversion = Effect(env, 431570201, "Buff", "Index(2)", true);
            Main(env, 990501, "HPBonusFlat"); Effect(env, 99050101, "Buff", "500");
            env.CreateMinimalBattle(); var owner = env.Ally.Entity; env.CreateAlly(2);
            var context = Weapon(owner, 3157, .2f, .012f); Add(env, owner, 4315701, context); var source = Add(env, owner, 4315702, context);
            owner.WeaponATKBonus = .5f; Start(env); owner.CurrentHP = 10f;
            Assert.That(owner.GetFinalHP(), Is.EqualTo(1200f).Within(.001f));
            Assert.That(owner.GetStatusATKBonusFlat("", "", -1, ""), Is.EqualTo(14.4f).Within(.001f));
            owner.AddStatus(Key(990501), owner, 2, 2, 0, env.DataManager.StatusMainDict[Key(990501)]);
            Assert.That(owner.GetStatusATKBonusFlat("", "", -1, ""), Is.EqualTo(20.4f).Within(.001f));
            Assert.That(owner.GetFinalATK("", "", -1, ""), Is.EqualTo((owner.TotalATK + owner.WeaponATK) * 1.5f + 20.4f).Within(.001f));
            Assert.That(owner.CurrentHP, Is.EqualTo(10f)); owner.RemoveStatus(Key(990501));
            Assert.That(owner.GetStatusATKBonusFlat("", "", -1, ""), Is.EqualTo(14.4f).Within(.001f));
            source.WeaponContext = Weapon(env.Manager.Allies[1].Entity, 3157, .2f, .012f);
            Assert.Throws<InvalidOperationException>(() => WeaponHooks.TryResolveBuffScalar(conversion, source, out _));
            Assert.That(WeaponHooks.TryResolveBuffScalar(new StatusEffectData { StatusEffectID = 431570202,
                EffectType = "Buff", DamageHook = "Hook(431570202)" }, source, out _), Is.False);
        }

        private static void KeyOfKhajNisut(BattleFlowTestEnv env)
        {
            Main(env, 4316101, "HPBonus"); Main(env, 4316102); Main(env, 4316103, "EMBonus", true); Main(env, 4316104, "EMBonus");
            Effect(env, 431610101, "Buff", "Index(1)"); Effect(env, 431610301, "Buff", "Index(2)", true);
            Effect(env, 431610401, "Buff", "Index(3)", true);
            var action = Action(env, 4316102, 431610201, 431610202); action.OutgoingHit = "Skill"; action.MaxTimePerHit = 1;
            Effect(env, 431610201, "ApplyStatus", Key(4316103)).Duration = 5;
            var share = Effect(env, 431610202, "ApplyStatus", Key(4316104)); share.Duration = 5; share.TargetType = "Allies";
            share.TargetSelect = "-1"; share.TargetOverride = "Hook(431610204)"; share.ScriptHook = "Hook(431610202)";
            env.ConfigureAllyAction(2, "SK_Key", "SE_Key", "Physical", ".01*TotalATK,0", 0);
            var level = env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_2"][1]; level.Hits2 = level.Hits3 = ".01*TotalATK,0";
        }

        [Test, Timeout(10000)]
        public void Key_ThreeHitsOnTwoTargets_GainsThreeBeforeSharing_UsesOriginalOwnersHPAndRemovesOnDeath()
        {
            using var env = new BattleFlowTestEnv(); KeyOfKhajNisut(env); Main(env, 990502, "HPBonusFlat");
            Effect(env, 99050201, "Buff", "500"); env.CreateMinimalBattle(); env.CreateEnemy(2);
            var owner = env.Ally.Entity; var other = env.CreateAlly(2, hp: 5000f).Entity; var dead = env.CreateAlly(3).Entity; dead.CurrentHP = 0f;
            var context = Weapon(owner, 3161, .2f, .0012f, .002f); Add(env, owner, 4316101, context); Add(env, owner, 4316102, context);
            Start(env); Assert.That(owner.GetStatusEMBonus(), Is.Zero); Assert.That(other.GetStatusEMBonus(), Is.Zero);
            env.Ally.ForcedTargetPositions.AddRange(new[] { 1, 2 }); Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            var count = owner.GetStatus(Key(4316103)); var share = other.GetStatus(Key(4316104));
            Assert.That(count.RuntimeValue, Is.EqualTo(3f)); Assert.That(count.StackCount, Is.EqualTo(1));
            Assert.That(owner.HasStatus(Key(4316104)), Is.False); Assert.That(dead.HasStatus(Key(4316104)), Is.False);
            Assert.That(owner.GetStatusEMBonus(), Is.EqualTo(4.32f).Within(.0001f));
            Assert.That(other.GetStatusEMBonus(), Is.EqualTo(2.4f).Within(.0001f)); Assert.That(share.WeaponContext, Is.SameAs(context));
            owner.AddStatus(Key(990502), owner, 2, 2, 0, env.DataManager.StatusMainDict[Key(990502)]);
            Assert.That(owner.GetStatusEMBonus(), Is.EqualTo(6.12f).Within(.0001f));
            Assert.That(other.GetStatusEMBonus(), Is.EqualTo(3.4f).Within(.0001f)); Next(env);
            Assert.That(count.RemainingPhaseCount, Is.EqualTo(4)); Assert.That(share.RemainingPhaseCount, Is.EqualTo(4));
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True); Assert.That(count.RemainingPhaseCount, Is.EqualTo(5));
            var refreshed = other.GetStatus(Key(4316104)); Assert.That(refreshed, Is.Not.SameAs(share));
            Assert.That(refreshed.RemainingPhaseCount, Is.EqualTo(5)); Assert.That(count.RuntimeValue, Is.EqualTo(3f));
            owner.TakeDamage(10000f, DamageSourceInfo.CreateUnknown());
            Assert.That(other.HasStatus(Key(4316104)), Is.False); Assert.That(other.GetStatusEMBonus(), Is.Zero);
        }

        [Test, Timeout(10000)]
        public void Key_DoesNotShareBeforeThirdHit_AndEachSourceReplacesOneOrdinarySharedWindow()
        {
            using var env = new BattleFlowTestEnv(); KeyOfKhajNisut(env);
            var level = env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_2"][1]; level.Hits2 = level.Hits3 = null;
            env.CreateMinimalBattle(); var owner = env.Ally.Entity; var other = env.CreateAlly(2).Entity; var recipient = env.CreateAlly(3).Entity;
            var first = Weapon(owner, 3161, .2f, .0012f, .002f); var second = Weapon(other, 3161, .4f, .0024f, .004f);
            Add(env, owner, 4316101, first); Add(env, owner, 4316102, first);
            Add(env, other, 4316101, second); Add(env, other, 4316102, second); Start(env);
            for (int i = 0; i < 2; i++) Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            Assert.That(recipient.HasStatus(Key(4316104)), Is.False);
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True); var old = recipient.GetStatus(Key(4316104));
            Assert.That(env.Manager.TrySwitchActiveAlly(1), Is.True);
            for (int i = 0; i < 3; i++) Assert.That(env.Manager.UseSkillBySlot(1), Is.True);
            var current = recipient.GetStatus(Key(4316104)); Assert.That(current.Caster, Is.SameAs(other)); Assert.That(old.IsActive, Is.False);
            Assert.That(recipient.GetStatusEMBonus(), Is.EqualTo(5.6f).Within(.0001f));
            owner.TakeDamage(10000f, DamageSourceInfo.CreateUnknown()); Assert.That(recipient.GetStatus(Key(4316104)), Is.SameAs(current));
            other.TakeDamage(10000f, DamageSourceInfo.CreateUnknown()); Assert.That(recipient.HasStatus(Key(4316104)), Is.False);
        }

        private static void Splendor(BattleFlowTestEnv env)
        {
            Main(env, 4316301); Main(env, 4316302); var damage = Main(env, 4316303, "DMGBonus", true); damage.ApplyDamageType = "Skill";
            Main(env, 4316304, "HPBonus", true);
            Action(env, 4316301, 431630101).ScriptHook = "Hook(4316301)";
            var team = Action(env, 4316302, 431630201); team.ScriptHook = "Hook(4316302)"; team.MaxTimePerEffect = 1;
            Effect(env, 431630101, "ApplyStatus", Key(4316303)).Duration = 2;
            Effect(env, 431630201, "ApplyStatus", Key(4316304)).Duration = 2;
            Effect(env, 431630301, "Buff", "Index(1)*Count(ST_Weapon_4316303)");
            Effect(env, 431630401, "Buff", "Index(2)*Count(ST_Weapon_4316304)");
        }

        [Test, Timeout(10000)]
        public void Splendor_RealHPOnly_GroupHealSharesOneEffect_SeparateEffectsRefreshIndependentCounts()
        {
            using var env = new BattleFlowTestEnv(); Splendor(env); env.CreateMinimalBattle(); var owner = env.CreateAlly(2).Entity;
            var first = env.Ally.Entity; var second = env.CreateAlly(3).Entity;
            var context = Weapon(owner, 3163, .08f, .14f); Add(env, owner, 4316301, context); Add(env, owner, 4316302, context); Start(env);
            owner.CurrentHP = first.CurrentHP = second.CurrentHP = 500f;
            using (var heal = new HealBatch()) { heal.Add(first, 10f, owner); heal.Add(second, 10f, owner); heal.Add(owner, 10f, owner); }
            var hp = owner.GetStatus(Key(4316304)); var damage = owner.GetStatus(Key(4316303));
            Assert.That(hp.RuntimeValue, Is.EqualTo(1f)); Assert.That(damage.RuntimeValue, Is.EqualTo(1f));
            Assert.That(owner.GetFinalHP(), Is.EqualTo(1140f).Within(.001f)); Assert.That(owner.CurrentHP, Is.EqualTo(510f));
            using (BattleResolutionContext.BeginEffect()) { first.Heal(1f, owner); second.Heal(1f, owner); }
            Assert.That(hp.RuntimeValue, Is.EqualTo(2f), "同根效果连锁只再加一点");
            owner.Heal(1f, owner); owner.Heal(1f, owner); Assert.That(damage.RuntimeValue, Is.EqualTo(3f));
            env.Enemy.Entity.Heal(10f, owner); first.CurrentHP = first.GetFinalHP(); first.Heal(10f, owner);
            Assert.That(hp.RuntimeValue, Is.EqualTo(2f));
            owner.AddShield(10f, "", 5f);
            using (var hit = new HitBatch()) hit.Resolve(new HitRequest { Target = owner, Source = DamageSourceInfo.CreateUnknown(),
                DamageBeforeShield = 1f, Element = "Physical" });
            Assert.That(damage.RuntimeValue, Is.EqualTo(3f));
            Next(env); Assert.That(hp.RemainingPhaseCount, Is.EqualTo(1)); Assert.That(damage.RemainingPhaseCount, Is.EqualTo(1));
            second.Heal(1f, owner); Assert.That(hp.RemainingPhaseCount, Is.EqualTo(2)); Assert.That(damage.RemainingPhaseCount, Is.EqualTo(1));
            using (DamageQueryContext.Enter("query", "Skill"))
                Assert.That(owner.GetStatusDMGBonus("query", "", 0, "Physical", env.Enemy.Entity), Is.EqualTo(.24f).Within(.0001f));
            using (DamageQueryContext.Enter("query", "Normal"))
                Assert.That(owner.GetStatusDMGBonus("query", "", 2, "Physical", env.Enemy.Entity), Is.Zero);
            Next(env); Assert.That(owner.HasStatus(Key(4316303)), Is.False); Assert.That(owner.HasStatus(Key(4316304)), Is.True);
            Next(env); Assert.That(owner.HasStatus(Key(4316304)), Is.False);
        }

        [Test, Timeout(10000)]
        public void Splendor_MaxHPWindowEndingClampsOnlyExcessHP_WithoutInventingAHeal()
        {
            using var env = new BattleFlowTestEnv(); Splendor(env); env.CreateMinimalBattle(); var owner = env.Ally.Entity;
            var other = env.CreateAlly(2).Entity; var context = Weapon(owner, 3163, .08f, .14f);
            Add(env, owner, 4316301, context); Add(env, owner, 4316302, context); Start(env); other.CurrentHP = 500f; other.Heal(1f, owner);
            owner.CurrentHP = 1140f; int heals = 0, adjustments = 0;
            using var heal = BattleEventHub.Subscribe<HealEventData>(BattleEventKind.HealResolved, (_, fact) => heals++, 0);
            using var hp = BattleEventHub.Subscribe<HPChangedEventData>(BattleEventKind.HPChanged, (_, fact) =>
            { if (fact.Subject == owner && fact.Reason == "MaxHPAdjustment") adjustments++; }, 0);
            owner.RemoveStatus(Key(4316304)); Assert.That(owner.CurrentHP, Is.EqualTo(1000f)); Assert.That(heals, Is.Zero);
            Assert.That(adjustments, Is.EqualTo(1)); Assert.That(owner.GetStatus(Key(4316303)).RuntimeValue, Is.EqualTo(1f));
        }

        [Test, Timeout(10000)]
        public void Uraku_ActualGeoDamageFromCommittedActiveAlly_OneWindowPerHit_OnlyNormalAndSkillBonusesDouble()
        {
            using var env = new BattleFlowTestEnv(); Main(env, 4316401, "DEFBonus"); Main(env, 4316404);
            foreach (int id in new[] { 4316402, 4316403, 4316405, 4316406 })
                Main(env, id, "DMGBonus").ApplyDamageType = id == 4316402 || id == 4316405 ? "Normal" : "Skill";
            Effect(env, 431640101, "Buff", "Index(1)"); Effect(env, 431640201, "Buff", "Index(2)"); Effect(env, 431640301, "Buff", "Index(3)");
            Effect(env, 431640501, "Buff", "Index(2)"); Effect(env, 431640601, "Buff", "Index(3)");
            var action = Action(env, 4316404, 431640401, 431640402); action.ScriptHook = "Hook(4316404)"; action.MaxTimePerHit = 1;
            Effect(env, 431640401, "ApplyStatus", Key(4316405)).Duration = 4;
            Effect(env, 431640402, "ApplyStatus", Key(4316406)).Duration = 4;
            env.ConfigureAllyAction(2, "SK_Uraku", "SE_Uraku", "Geo", ".1*TotalATK,0", 0);
            env.CreateMinimalBattle(); var owner = env.CreateAlly(2).Entity; env.CreateEnemy(2);
            var context = Weapon(owner, 3164, .2f, .16f, .24f);
            for (int id = 4316401; id <= 4316404; id++) Add(env, owner, id, context);
            Start(env); var counts = new List<string>();
            using var listen = BattleEventHub.Subscribe<StatusEventData>(BattleEventKind.StatusAdded, (_, fact) =>
            { if (fact.StatusId == Key(4316405) || fact.StatusId == Key(4316406)) counts.Add(fact.StatusId); }, 0);
            foreach (var sample in new[] { new { Source = owner, Element = "Geo" },
                new { Source = env.Ally.Entity, Element = "Pyro" }, new { Source = env.Enemy.Entity, Element = "Geo" } })
                using (var hit = new HitBatch()) hit.Resolve(new HitRequest { Target = env.Enemy.Entity,
                    Source = DamageSourceInfo.Create(sample.Source, ReactionSourceKind.StatusEffect, "", "SE_UrakuRejected"),
                    DamageBeforeShield = 1f, Element = sample.Element });
            Assert.That(counts, Is.Empty, "后台来源、非岩伤、敌方来源均不触发");
            env.Enemy.Entity.AddShield(1000f, "", 5f); Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            Assert.That(counts, Is.Empty, "零HP伤害不触发"); env.Ally.ForcedTargetPositions.AddRange(new[] { 1, 2 });
            BattleDeclaration committed = null;
            using var snapshot = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            { if (fact.Source.SourceEffectID == "SE_Uraku" && fact.ActualHPDamage > 0f) committed = BattleEventHub.Current; }, 0);
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            Assert.That(committed, Is.Not.Null); Assert.That(env.Manager.TrySwitchActiveAlly(1), Is.True);
            Assert.That(ScriptHookEvaluator.Evaluate("Hook(4316404)", new ScriptHookContext {
                Status = owner.GetStatus(Key(4316404)), Caster = owner, Declaration = committed }), Is.True,
                "切人后查询旧声明仍读取当时的前台来源，不读当前IsActive");
            Assert.That(counts.Count(x => x == Key(4316405)), Is.EqualTo(1)); Assert.That(counts.Count(x => x == Key(4316406)), Is.EqualTo(1));
            Assert.That(owner.GetStatusDEFBonus(), Is.EqualTo(.2f).Within(.0001f));
            using (DamageQueryContext.Enter("q", "Normal")) Assert.That(owner.GetStatusDMGBonus("q", "", 2, "Geo", env.Enemy.Entity), Is.EqualTo(.32f).Within(.0001f));
            using (DamageQueryContext.Enter("q", "Skill")) Assert.That(owner.GetStatusDMGBonus("q", "", 0, "Geo", env.Enemy.Entity), Is.EqualTo(.48f).Within(.0001f));
            using (DamageQueryContext.Enter("q", "Burst")) Assert.That(owner.GetStatusDMGBonus("q", "", 3, "Geo", env.Enemy.Entity), Is.Zero);
            for (int i = 0; i < 4; i++) Next(env);
            Assert.That(owner.HasStatus(Key(4316405)), Is.False); Assert.That(owner.HasStatus(Key(4316406)), Is.False);
        }
    }
}
