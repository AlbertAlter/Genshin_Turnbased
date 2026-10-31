using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>3136/3141/3154/3155正式入口回归定义；不自动启动Unity。</summary>
    public sealed class WeaponHealingAndReactionTargetTests
    {
        private static string Key(int id) => DataManager.WeaponStatusKey(id);
        private static string Eff(int id) => "STE_Weapon_" + id;
        private static StatusMainData Main(BattleFlowTestEnv env, int id, string part = null)
        {
            var main = new StatusMainData { StatusID = id, StatusID2 = Key(id), StatusType = "Buff",
                MultiplierPart = part, MaxStack = 1, MaxCount = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[Key(id)] = main; return main;
        }
        private static StatusEffectData Effect(BattleFlowTestEnv env, int id, string type, string formula)
        {
            var effect = new StatusEffectData { StatusEffectID = id, StatusEffectID2 = Eff(id), EffectType = type,
                Param1 = formula, TargetType = "Self" }; env.DataManager.StatusEffectDict[Eff(id)] = effect; return effect;
        }
        private static StatusActionData Action(BattleFlowTestEnv env, int id, string kind, params int[] effects)
        {
            var action = new StatusActionData { StatusID = id, StatusID2 = Key(id), ActionType = kind,
                Param1 = string.Join(";", effects.Select(Eff)) };
            env.DataManager.StatusActionDict[Key(id)] = new List<StatusActionData> { action }; return action;
        }
        private static WeaponRuntimeContext Weapon(BattleEntity owner, int weapon, params float[] parameters)
        {
            var context = new WeaponRuntimeContext { WeaponID = weapon, Equipper = owner, Refinement = 1,
                SourceInstanceID = "HealReaction:" + weapon + ":" + owner.SlotPosition };
            for (int i = 0; i < parameters.Length; i++) context.ResolvedParams[i + 1] = parameters[i]; return context;
        }
        private static StatusInstance Add(BattleFlowTestEnv env, BattleEntity owner, int id, WeaponRuntimeContext weapon)
            => owner.AddStatus(Key(id), owner, 0, 2, 0, env.DataManager.StatusMainDict[Key(id)], weapon);
        private static void Start(BattleFlowTestEnv env)
        { BattleRandom.SetSource(new SequenceBattleRandomSource()); env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction); }
        private static void Next(BattleFlowTestEnv env)
        { env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction); }
        private static void BurstMarker(BattleFlowTestEnv env, string suffix)
        {
            env.ConfigureStatus("ST_BurstMarker_" + suffix);
            env.ConfigureAllyAction(3, "SK_Burst_" + suffix, "SE_Burst_" + suffix, "Physical", null, 0,
                effectType: "ApplyStatus", param1: "ST_BurstMarker_" + suffix, targetType: "Self", duration: 1);
        }

        private static void Amber(BattleFlowTestEnv env)
        {
            for (int id = 4313601; id <= 4313604; id++) Main(env, id);
            Action(env, 4313601, "OnTrigger", 431360101).OnAction = "Burst";
            Effect(env, 431360101, "ApplyStatus", Key(4313602)).Duration = 2;
            Effect(env, 431360201, "BindStatus", Key(4313603)).TriggerPhase = 1;
            Effect(env, 431360203, "BindStatus", Key(4313604)).TriggerPhase = 3;
            foreach (int id in new[] { 4313603, 4313604 })
            {
                int energyID = id * 100 + 1, healingID = id * 100 + 2;
                Action(env, id, "OnItsTurn", energyID, healingID);
                var energy = Effect(env, energyID, "GainEnergy", "Index(1)"); energy.Param2 = "Flat"; energy.DamageHook = "Hook(" + energyID + ")";
                var heal = Effect(env, healingID, "Heal", "Index(2)*Target.TotalHP");
                heal.TargetType = "Allies"; heal.TargetSelect = "-1"; heal.DamageHook = "Hook(" + healingID + ")";
            }
            BurstMarker(env, "Amber");
        }

        [Test, Timeout(10000)]
        public void PrototypeAmber_PerTargetFinalHP_AdditiveBonusesBondAndOverheal_OneBatchAfterFlatEnergy()
        {
            using var env = new BattleFlowTestEnv(); Amber(env); env.CreateMinimalBattle(); var owner = env.Ally.Entity;
            var second = env.CreateAlly(2, hp: 2000f).Entity; var third = env.CreateAlly(3, hp: 500f).Entity;
            var dead = env.CreateAlly(4, hp: 900f).Entity; dead.CurrentHP = 0f;
            env.ConfigureStatus("ST_AmberHP"); var hp = env.DataManager.StatusMainDict["ST_AmberHP"];
            hp.StatusID = 990400; hp.MultiplierPart = "HPBonusFlat";
            env.DataManager.StatusEffectDict["STE_AmberHP"] = new StatusEffectData { StatusEffectID = hp.StatusID * 100 + 1,
                StatusEffectID2 = "STE_AmberHP", EffectType = "Buff", Param1 = "500" };
            second.AddStatus("ST_AmberHP", second, 0, 2, 0, hp);
            var amber = Weapon(owner, 3136, 4.5f, .045f); amber.Refinement = 2;
            Add(env, owner, 4313601, amber); Start(env);
            owner.CurrentHP = 500f; second.CurrentHP = 1000f; third.CurrentHP = 490f;
            owner.HealBonus = .1f; owner.BeHealedBonus = .2f; second.BeHealedBonus = .3f; owner.TotalRechargeRate = 3f;
            owner.CurrentEnergy = 0f; BondOfLifeStatusHandler.AddValue(second, 100f);
            var heals = new List<HealEventData>(); var energies = new List<double>();
            using var energy = BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged, (_, fact) =>
            { if (fact.Subject == owner && fact.Resource == "Energy") energies.Add(fact.Added); }, 0);
            using var healing = BattleEventHub.Subscribe<HealEventData>(BattleEventKind.HealResolved, (_, fact) =>
            {
                if (fact.EffectId != Eff(431360302) && fact.EffectId != Eff(431360402)) return;
                if (heals.Count == 0)
                {
                    Assert.That(owner.CurrentEnergy, Is.EqualTo(4.5f));
                    Assert.That(owner.CurrentHP, Is.EqualTo(558.5f).Within(.001f));
                    Assert.That(second.CurrentHP, Is.EqualTo(1057.5f).Within(.001f));
                    Assert.That(third.CurrentHP, Is.EqualTo(500f));
                    Assert.That(BondOfLifeStatusHandler.GetValue(second), Is.Zero, "首个治疗事件看到整段目标均已提交");
                }
                heals.Add(fact);
            }, 0);
            Assert.That(env.Manager.UseBurstBySlot(0), Is.True); var parent = owner.GetStatus(Key(4313602));
            Assert.That(parent.BoundStatuses, Has.Count.EqualTo(2)); Assert.That(heals, Is.Empty);
            Assert.That(env.Manager.TrySwitchActiveAlly(1), Is.True); Next(env);
            Assert.That(owner.CurrentEnergy, Is.EqualTo(9f));
            var first = heals.Take(3).ToList(); Assert.That(first.Select(x => x.Source), Is.All.SameAs(owner));
            Assert.That(first.Single(x => x.Subject == owner).Requested, Is.EqualTo(58.5f).Within(.001f));
            var received = first.Single(x => x.Subject == second);
            Assert.That(received.Requested, Is.EqualTo(157.5f).Within(.001f)); Assert.That(received.BondAbsorbed, Is.EqualTo(100f));
            Assert.That(received.Actual, Is.EqualTo(57.5f).Within(.001f));
            Assert.That(first.Single(x => x.Subject == third).Overheal, Is.EqualTo(14.75f).Within(.001f));
            Next(env); Assert.That(owner.CurrentEnergy, Is.EqualTo(18f)); Assert.That(parent.IsActive, Is.False);
            Assert.That(heals, Has.Count.EqualTo(12)); Assert.That(energies, Is.EqualTo(new[] { 4.5d, 4.5d, 4.5d, 4.5d }));
            Assert.That(heals.All(x => x.Subject != dead), Is.True); Assert.That(dead.CurrentHP, Is.Zero);
            Next(env); Assert.That(heals, Has.Count.EqualTo(12));
        }

        [Test, Timeout(7000)]
        public void TargetHPFormula_AlsoWorksForRealSkillHeal_WithoutUsingHealerHPForEveryone()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(2, "SK_TargetHeal", "SE_TargetHeal", "Physical", null, 0,
                effectType: "Heal", param1: ".1*Target.TotalHP+5", targetType: "Allies");
            env.DataManager.SkillEffectDict["SE_TargetHeal"].TargetNumber = -1;
            env.CreateMinimalBattle(); var second = env.CreateAlly(2, hp: 2000f).Entity;
            env.Ally.Entity.CurrentHP = second.CurrentHP = 100f; Start(env);
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            Assert.That(env.Ally.Entity.CurrentHP, Is.EqualTo(205f).Within(.001f));
            Assert.That(second.CurrentHP, Is.EqualTo(305f).Within(.001f));
        }

        private static void NativeElement(CharacterBattleController character, string element)
        {
            int id = character.Entity.SlotPosition == 1 ? BattleFlowTestEnv.AllyID : 990100 + character.Entity.SlotPosition;
            character.Entity.EntityID = id;
            var metadata = DataManager.Instance.GetCharacterOverview(id);
            if (metadata == null)
            {
                metadata = new CharacterOverviewData { CharacterID = id };
                DataManager.Instance.CharacterOverviewList.Add(metadata);
            }
            metadata.Element = element; // 开场固有元素从正式身份配置建队伍快照。
        }
        private static void Hakushin(BattleFlowTestEnv env)
        {
            Main(env, 4314101); var trigger = Action(env, 4314101, "OnTrigger", Enumerable.Range(431410101, 7).ToArray());
            trigger.ReactionTriggered = "Electro";
            string[] elements = { "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Dendro", "Geo" };
            for (int i = 0; i < elements.Length; i++)
            {
                int status = 4314102 + i, applyID = 431410101 + i;
                Main(env, status, "DMGBonus").ApplyElementType = elements[i];
                Effect(env, status * 100 + 1, "Buff", "Index(1)");
                var apply = Effect(env, applyID, "ApplyStatus", Key(status)); apply.Duration = 2;
                apply.TargetType = "Allies"; apply.TargetSelect = "-1"; apply.TargetOverride = "Hook(" + applyID + ")";
            }
        }

        [TestCase("Pyro", ReactionType.Overloaded), TestCase("Hydro", ReactionType.ElectroCharged)]
        [TestCase("Anemo", ReactionType.Swirl), TestCase("Geo", ReactionType.Crystallize), TestCase("Dendro", ReactionType.Quicken)]
        [Timeout(10000)]
        public void Hakushin_RealReactionOnlyGrantsParticipatingNativeElements_NotAllElementsOrOtherCaster(string attackElement, ReactionType expected)
        {
            using var env = new BattleFlowTestEnv(); Hakushin(env);
            env.ConfigureAllyAction(2, "SK_Hakushin", "SE_Hakushin", attackElement, ".01*TotalATK,1", 0);
            env.CreateMinimalBattle(); var owner = env.Ally.Entity;
            var electro = env.CreateAlly(2); var hydro = env.CreateAlly(3); var pyro = env.CreateAlly(4);
            NativeElement(env.Ally, "Anemo"); NativeElement(electro, "Electro"); NativeElement(hydro, "Hydro"); NativeElement(pyro, "Pyro");
            var listener = Add(env, owner, 4314101, Weapon(owner, 3141, .1f)); Start(env);
            env.Enemy.Entity.TotalHP = env.Enemy.Entity.CurrentHP = 10000f;
            env.Enemy.Entity.ApplyAura("Electro", 1f, owner.EntityID); var reactions = new List<ReactionEventData>();
            using var observer = BattleEventHub.Subscribe<ReactionEventData>(BattleEventKind.ReactionOccurred, (_, fact) => reactions.Add(fact), 0);
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True); Assert.That(reactions.Any(x => x.Reaction == expected && x.Source == owner), Is.True);
            Assert.That(electro.Entity.HasStatus(Key(4314104)), Is.True);
            Assert.That(owner.HasStatus(Key(4314106)), Is.EqualTo(attackElement == "Anemo"));
            Assert.That(hydro.Entity.HasStatus(Key(4314103)), Is.EqualTo(attackElement == "Hydro"));
            Assert.That(pyro.Entity.HasStatus(Key(4314102)), Is.EqualTo(attackElement == "Pyro"));
            Assert.That(electro.Entity.GetStatusDMGBonus("", "", -1, "Electro", env.Enemy.Entity), Is.EqualTo(.1f).Within(.001f));
            Assert.That(electro.Entity.GetStatusDMGBonus("", "", -1, "Pyro", env.Enemy.Entity), Is.Zero);
            int count = listener.LifeTriggerCount; var old = electro.Entity.GetStatus(Key(4314104));
            Assert.That(env.Manager.TrySwitchActiveAlly(1), Is.True); env.Enemy.Entity.ApplyAura("Electro", 1f, owner.EntityID);
            Assert.That(env.Manager.UseSkillBySlot(1), Is.True); Assert.That(listener.LifeTriggerCount, Is.EqualTo(count));
            Assert.That(electro.Entity.GetStatus(Key(4314104)), Is.SameAs(old));
            var context = new ScriptHookContext { Status = listener, Caster = owner, Declaration = null };
            using (ScriptHookContext.EnterExecution(context))
            {
                Assert.That(BattleSpecialRules.TryResolveTargets("Hook(431410101)", env.Manager, owner, out var empty), Is.True);
                Assert.That(empty, Is.Empty, "没有本次声明就返回空，不从最后反应或敌方残留推断");
            }
            owner.TakeDamage(2000f, DamageSourceInfo.CreateUnknown());
            Assert.That(electro.Entity.HasStatus(Key(4314104)), Is.False, "装备者死亡移除投影到队友的本来源增益");
        }

        [Test, Timeout(10000)]
        public void SkywardBlade_ThreeHitsTwoShieldedTargetsAppendSixIndependentHits_OneAPReductionPerAction()
        {
            using var env = new BattleFlowTestEnv(); Main(env, 4315401, "CritRate"); Main(env, 4315402); Main(env, 4315403);
            Effect(env, 431540101, "Buff", "Index(1)"); Action(env, 4315402, "OnTrigger", 431540201).OnAction = "Burst";
            Effect(env, 431540201, "ApplyStatus", Key(4315403)).Duration = 3;
            Action(env, 4315403, "OnTrigger", 431540301).OutgoingHit = "Normal;Heavy";
            var extra = Effect(env, 431540301, "Damage", "Index(2)*TotalATK,0"); extra.Element = "Physical";
            extra.TargetType = "Enemy"; extra.TargetOverride = "Hook(431540301)";
            Effect(env, 431540302, "APCost", "-1").Param2 = "Normal;Heavy";
            env.ConfigureAllyAction(0, "SK_Skyward", "SE_Skyward", "Pyro", ".01*TotalATK,0", 10);
            var levels = env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_0"][1]; levels.Hits2 = levels.Hits3 = ".01*TotalATK,0";
            BurstMarker(env, "Skyward"); env.CreateMinimalBattle(); env.CreateEnemy(2); var owner = env.Ally.Entity;
            var weapon = Weapon(owner, 3154, .04f, .2f); Add(env, owner, 4315401, weapon); Add(env, owner, 4315402, weapon); Start(env);
            Assert.That(env.Ally.GetBoundSkillAPCost(0), Is.EqualTo(10)); Assert.That(env.Manager.UseBurstBySlot(0), Is.True);
            Assert.That(env.Ally.GetBoundSkillAPCost(0), Is.EqualTo(9));
            foreach (var enemy in env.Manager.Enemies) enemy.Entity.AddShield(1000f, "", 5f);
            var extras = new List<DamageEventData>(); var ids = new List<long>();
            using var observer = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (header, fact) =>
            { if (fact.Source.SourceEffectID == Eff(431540301)) { extras.Add(fact); ids.Add(header.EffectExecutionId); } }, 0);
            var random = new SequenceBattleRandomSource(Enumerable.Repeat(.9f, 12).ToArray()); BattleRandom.SetSource(random);
            env.Ally.ForcedTargetPositions.AddRange(new[] { 1, 2 }); Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            Assert.That(extras, Has.Count.EqualTo(6)); Assert.That(extras.Select(x => x.DamageBeforeShield), Is.All.EqualTo(10f).Within(.001f));
            Assert.That(extras.Select(x => x.ActualHPDamage), Is.All.EqualTo(0f)); Assert.That(ids.Distinct().Count(), Is.EqualTo(6));
            Assert.That(extras.All(x => string.IsNullOrEmpty(x.DamageType) && x.Element == "Physical" && x.ElementAmount == 0f), Is.True);
            Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(91)); Assert.That(random.FloatCallCount, Is.EqualTo(12)); Assert.That(random.IntCallCount, Is.Zero);
            Next(env); Next(env); Next(env); Assert.That(owner.HasStatus(Key(4315403)), Is.False);
            Assert.That(env.Ally.GetBoundSkillAPCost(0), Is.EqualTo(10));
        }

        private static void Aquila(BattleFlowTestEnv env)
        {
            Main(env, 4315501, "ATKBonus"); Main(env, 4315502); Effect(env, 431550101, "Buff", "Index(1)");
            var action = Action(env, 4315502, "OnTrigger", 431550201, 431550202); action.OnDamage = "All"; action.Cooldown = 4;
            Effect(env, 431550201, "Heal", "Index(2)*TotalATK");
            var counter = Effect(env, 431550202, "Damage", "Index(3)*TotalATK,0"); counter.Element = "Physical";
            counter.TargetType = "Enemy"; counter.TargetOverride = "Hook(431550202)";
        }

        [Test, Timeout(10000)]
        public void Aquila_RealEnemyDamageHealsBeforeCounter_OneSharedFourTurnCooldown()
        {
            using var env = new BattleFlowTestEnv(); Aquila(env); env.ConfigureEnemyDamageSkill(.1f); env.CreateMinimalBattle();
            var owner = env.Ally.Entity; var weapon = Weapon(owner, 3155, .2f, 1f, 2f);
            Add(env, owner, 4315501, weapon); Add(env, owner, 4315502, weapon); Start(env); owner.CurrentHP = 500f;
            var trace = new List<string>(); var heals = new List<HealEventData>(); var incoming = new List<DamageEventData>(); var counters = new List<DamageEventData>();
            using var damage = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            {
                if (fact.Subject == owner && fact.Source.SourceEffectID == "SE_Enemy_BattleFlow") { incoming.Add(fact); trace.Add("incoming"); }
                if (fact.Source.SourceEffectID == Eff(431550202)) { counters.Add(fact); trace.Add("counter"); }
            }, 0);
            using var heal = BattleEventHub.Subscribe<HealEventData>(BattleEventKind.HealResolved, (_, fact) =>
            { if (fact.EffectId == Eff(431550201)) { heals.Add(fact); trace.Add("heal"); } }, 0);
            Next(env); Assert.That(trace, Is.EqualTo(new[] { "incoming", "heal", "counter" }));
            Assert.That(heals.Single().Requested, Is.EqualTo(120f).Within(.001f));
            Assert.That(counters.Single().Subject, Is.SameAs(env.Enemy.Entity));
            Assert.That(counters.Single().DamageBeforeShield, Is.EqualTo(120f).Within(.001f));
            Assert.That(owner.CurrentHP, Is.EqualTo(500f - incoming.Single().ActualHPDamage + 120f).Within(.001f));
            Next(env); Next(env); Next(env); Assert.That(heals, Has.Count.EqualTo(1)); Assert.That(counters, Has.Count.EqualTo(1));
            Next(env); Assert.That(heals, Has.Count.EqualTo(2)); Assert.That(counters, Has.Count.EqualTo(2));
        }

        [TestCase(false), TestCase(true), Timeout(7000)]
        public void Aquila_UnknownOrNowDeadAttackerStillAllowsHeal_AndNeverRetargetsAnotherEnemy(bool killAttackerDuringHeal)
        {
            using var env = new BattleFlowTestEnv(); Aquila(env); env.CreateMinimalBattle(); env.CreateEnemy(2);
            var owner = env.Ally.Entity; var weapon = Weapon(owner, 3155, .2f, 1f, 2f);
            Add(env, owner, 4315501, weapon); Add(env, owner, 4315502, weapon); Start(env); owner.CurrentHP = 500f;
            int heals = 0, counters = 0;
            using var heal = BattleEventHub.Subscribe<HealEventData>(BattleEventKind.HealResolved, (_, fact) =>
            {
                if (fact.EffectId != Eff(431550201)) return; heals++;
                if (killAttackerDuringHeal) env.Enemy.Entity.TakeDamage(2000f, DamageSourceInfo.CreateUnknown());
            }, 0);
            using var damage = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            { if (fact.Source.SourceEffectID == Eff(431550202)) counters++; }, 0);
            var source = killAttackerDuringHeal ? DamageSourceInfo.Create(env.Enemy.Entity, ReactionSourceKind.EnemySkill, "SK_Source", "SE_Source")
                : DamageSourceInfo.CreateUnknown();
            owner.TakeDamage(10f, source); Assert.That(heals, Is.EqualTo(1)); Assert.That(counters, Is.Zero);
            Assert.That(owner.CurrentHP, Is.EqualTo(610f).Within(.001f));
            Assert.That(env.Manager.Enemies.Single(x => x.Entity.SlotPosition == 2).Entity.CurrentHP, Is.EqualTo(1000f));
        }
    }
}
