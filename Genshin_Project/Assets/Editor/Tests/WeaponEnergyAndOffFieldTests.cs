using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>3100/3105/3108正式资源/伤害入口定义，未自动运行Unity。</summary>
    public sealed class WeaponEnergyAndOffFieldTests
    {
        private static string Key(int id) => DataManager.WeaponStatusKey(id);
        private static string Eff(int id) => "STE_Weapon_" + id;
        private static void Main(BattleFlowTestEnv env, int id, string part = null, bool count = false)
            => env.DataManager.StatusMainDict[Key(id)] = new StatusMainData { StatusID = id, StatusID2 = Key(id),
                StatusType = count ? "Count" : "Buff", MultiplierPart = part, MaxStack = 1, MaxCount = 1, WhenMax = "0" };
        private static StatusEffectData Effect(BattleFlowTestEnv env, int id, string type, string parameter)
        {
            var data = new StatusEffectData { StatusEffectID = id, StatusEffectID2 = Eff(id), EffectType = type,
                Param1 = parameter, TargetType = "Self" }; env.DataManager.StatusEffectDict[Eff(id)] = data; return data;
        }
        private static StatusActionData Action(BattleFlowTestEnv env, int id, string effect, string hook)
        {
            var data = new StatusActionData { StatusID = id, StatusID2 = Key(id), ActionType = "OnTrigger", Param1 = effect, ScriptHook = hook };
            env.DataManager.StatusActionDict[Key(id)] = new List<StatusActionData> { data }; return data;
        }
        private static StatusInstance Add(BattleFlowTestEnv env, BattleEntity owner, int id, int weapon, float parameter)
        {
            var context = new WeaponRuntimeContext { WeaponID = weapon, Equipper = owner, Refinement = 1, SourceInstanceID = "EnergyOffField:" + weapon };
            context.ResolvedParams[1] = parameter; return owner.AddStatus(Key(id), owner, 0, 2, 0, env.DataManager.StatusMainDict[Key(id)], context);
        }
        private static void Start(BattleFlowTestEnv env)
        { BattleRandom.SetSource(new SequenceBattleRandomSource()); env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction); }
        private static void Next(BattleFlowTestEnv env)
        { env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction); }

        [Test, Timeout(9000)]
        public void Cloudforged_ActualEnergyLossAddsOneIncrement_CapsAtTwoAndRefreshesAtCap()
        {
            using var env = new BattleFlowTestEnv(); Main(env, 4310001); Main(env, 4310002, "EMBonus", true);
            Action(env, 4310001, Eff(431000101), "Hook(4310001)");
            var apply = Effect(env, 431000101, "ApplyStatus", Key(4310002)); apply.Duration = 5; apply.AddInPhase = 2;
            Effect(env, 431000201, "Buff", "Count(" + Key(4310002) + ")"); env.CreateMinimalBattle(); var owner = env.Ally.Entity;
            var other = env.CreateAlly(2).Entity; Add(env, owner, 4310001, 3100, 40f); Start(env);
            owner.CurrentEnergy = 0f; CharacterBattleController.ApplyEnergyGain(owner, -5f, "Flat", "");
            Assert.That(owner.HasStatus(Key(4310002)), Is.False);
            owner.CurrentEnergy = 20f; CharacterBattleController.ApplyEnergyGain(owner, -7.5f, "Flat", "");
            var count = owner.GetStatus(Key(4310002)); Assert.That(count.RuntimeValue, Is.EqualTo(40f));
            Assert.That(count.StackCount, Is.EqualTo(1)); CharacterBattleController.ApplyEnergyGain(owner, -1f, "Flat", "");
            Assert.That(count.RuntimeValue, Is.EqualTo(80f)); Next(env); Assert.That(count.RemainingPhaseCount, Is.EqualTo(4));
            other.CurrentEnergy = 10f; CharacterBattleController.ApplyEnergyGain(other, -5f, "Flat", "");
            Assert.That(count.RemainingPhaseCount, Is.EqualTo(4)); CharacterBattleController.ApplyEnergyGain(owner, -1f, "Flat", "");
            Assert.That(owner.GetStatus(Key(4310002)), Is.SameAs(count)); Assert.That(count.RuntimeValue, Is.EqualTo(80f));
            Assert.That(count.RemainingPhaseCount, Is.EqualTo(5)); CharacterBattleController.ApplyEnergyGain(owner, 2f, "Flat", "");
            Assert.That(count.RuntimeValue, Is.EqualTo(80f));
            for (int i = 0; i < 5; i++) Next(env); Assert.That(owner.HasStatus(Key(4310002)), Is.False);
        }

        [Test, Timeout(9000)]
        public void Rainbow_SavesSourceRosterAndFieldFactsAtShieldHit_NotAtDelayedQuery()
        {
            using var env = new BattleFlowTestEnv(); Main(env, 4310501); Main(env, 4310502, "ATKBonus");
            Action(env, 4310501, Eff(431050101), "Hook(4310501)").OutgoingHit = "All";
            var apply = Effect(env, 431050101, "ApplyStatus", Key(4310502)); apply.Duration = 2; apply.AddInPhase = 2;
            Effect(env, 431050201, "Buff", "Index(1)"); const string damageID = "STE_RainbowAttack";
            env.ConfigureStatus("ST_RainbowAttack", actionType: "OnTrigger", actionEffectID2: damageID);
            env.DataManager.StatusEffectDict[damageID] = new StatusEffectData { StatusEffectID2 = damageID, EffectType = "Damage",
                Param1 = ".01*TotalATK,0", Element = "Physical", DamageType = "Skill", TargetType = "Enemy", TargetSelect = "-1" };
            env.CreateMinimalBattle(); var owner = env.CreateAlly(2).Entity; owner.EntityID = 990012;
            var listen = Add(env, owner, 4310501, 3105, .28f); Start(env); env.Enemy.Entity.AddShield(1000f, "", 5f);
            var attack = owner.AddStatus("ST_RainbowAttack", owner, 0, 2, 0, env.DataManager.StatusMainDict["ST_RainbowAttack"]);
            var events = new List<DamageResolvedEvent>();
            using var observer = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            {
                if (fact.Source.SourceEffectID == damageID) events.Add(new DamageResolvedEvent { Source = fact.Source,
                    Target = fact.Subject, HitLanded = fact.HitLanded, SourceWasPartyMember = fact.SourceWasPartyMember, SourceWasOnField = fact.SourceWasOnField });
            }, 0);
            void Attack() => owner.CharacterCtrl.TryExecuteEventStatusAction(attack, env.DataManager.StatusActionDict["ST_RainbowAttack"][0], owner,
                new ScriptHookContext { Caster = owner });
            Attack(); Assert.That(owner.HasStatus(Key(4310502)), Is.True); Assert.That(events.Single().SourceWasPartyMember, Is.True);
            Assert.That(events[0].SourceWasOnField, Is.False); Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(1000f));
            Assert.That(env.Manager.TrySwitchActiveAlly(1), Is.True);
            var context = new ScriptHookContext { Status = listen, Caster = owner, DamageEvent = events[0] };
            Assert.That(ScriptHookEvaluator.Evaluate("Hook(4310501)", context), Is.True);
            Assert.That(owner.GetStatusATKBonus("", "", -1, ""), Is.EqualTo(.28f).Within(.0001f));
            Attack(); Assert.That(events, Has.Count.EqualTo(2)); Assert.That(events[1].SourceWasOnField, Is.True);
            Assert.That(env.Manager.TrySwitchActiveAlly(0), Is.True); context.DamageEvent = events[1];
            Assert.That(ScriptHookEvaluator.Evaluate("Hook(4310501)", context), Is.False);
            context.DamageEvent = events[1].Snapshot(); context.DamageEvent.SourceWasPartyMember = false;
            Assert.That(ScriptHookEvaluator.Evaluate("Hook(4310501)", context), Is.False);
        }

        [Test, Timeout(9000)]
        public void CrescentPike_ActualEnergyGainCreatesWindow_ThreeHitsOnTwoTargetsAppendSixIndependentPhysicalHits()
        {
            using var env = new BattleFlowTestEnv(); Main(env, 4310801); Main(env, 4310802);
            Action(env, 4310801, Eff(431080101), "ResourceChanged(Equipper);Hook(4310801)");
            Action(env, 4310802, Eff(431080201), null).OutgoingDamage = "Normal;Heavy";
            var apply = Effect(env, 431080101, "ApplyStatus", Key(4310802)); apply.Duration = 1; apply.AddInPhase = 2;
            var extra = Effect(env, 431080201, "Damage", "Index(1)*TotalATK,0"); extra.Element = "Physical";
            extra.TargetType = "Enemy"; extra.TargetOverride = "Hook(431080201)";
            env.ConfigureAllyAction(0, "SK_Crescent", "SE_Crescent", "Pyro", ".1*TotalATK,0", 0);
            var levels = env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_0"][1]; levels.Hits2 = levels.Hits3 = ".1*TotalATK,0";
            env.CreateMinimalBattle(); var second = env.CreateEnemy(2).Entity; var owner = env.Ally.Entity;
            Add(env, owner, 4310801, 3108, .2f); Start(env); owner.CurrentEnergy = owner.MaxEnergy;
            CharacterBattleController.ApplyEnergyGain(owner, 1f, "Flat", ""); Assert.That(owner.HasStatus(Key(4310802)), Is.False);
            owner.CurrentEnergy = 0f; CharacterBattleController.ApplyEnergyGain(owner, .5f, "Flat", "");
            var window = owner.GetStatus(Key(4310802)); Assert.That(window, Is.Not.Null);
            var sources = new List<DamageEventData>(); var targets = new List<DamageEventData>(); var executions = new List<long>();
            using var observe = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (header, fact) =>
            {
                if (fact.Source.SourceEffectID == "SE_Crescent") sources.Add(fact);
                if (fact.Source.SourceEffectID == Eff(431080201)) { targets.Add(fact); executions.Add(header.EffectExecutionId); }
            }, 0);
            env.Ally.ForcedTargetPositions.AddRange(new[] { 1, 2 }); var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            Assert.That(sources, Has.Count.EqualTo(6)); Assert.That(targets, Has.Count.EqualTo(6));
            Assert.That(targets.Count(x => x.Subject == second), Is.EqualTo(3)); Assert.That(executions.Distinct().Count(), Is.EqualTo(6));
            Assert.That(targets.Select(x => x.DamageBeforeShield), Is.All.EqualTo(10f));
            Assert.That(targets.Select(x => x.Element), Is.All.EqualTo("Physical")); Assert.That(targets.Select(x => x.ElementAmount), Is.All.EqualTo(0f));
            Assert.That(targets.All(x => string.IsNullOrEmpty(x.DamageType)), Is.True);
            Assert.That(random.FloatCallCount, Is.EqualTo(12)); Assert.That(random.IntCallCount, Is.Zero);
            env.Enemy.Entity.AddShield(1000f, "", 2f); second.AddShield(1000f, "", 2f);
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True); Assert.That(targets, Has.Count.EqualTo(6));
            Assert.That(owner.GetStatus(Key(4310802)), Is.SameAs(window)); Next(env); Assert.That(owner.HasStatus(Key(4310802)), Is.False);
        }
    }
}
