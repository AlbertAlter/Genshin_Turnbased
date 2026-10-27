using System;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>配表驱动的Immune回归用例；不读取Charts或启动Unity。</summary>
    public sealed class DamageImmunityTests
    {
        private BattleFlowTestEnv env;
        private BattleEntity target;
        private const int MainID = 98970;
        private const string StatusID = "ST_Test_ConfiguredImmune";

        [SetUp]
        public void SetUp()
        {
            env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            target = env.Ally.Entity;
            target.TotalHP = target.CurrentHP = 100f;
        }

        [TearDown]
        public void TearDown() { BattleRandom.ResetSource(); env?.Dispose(); }

        [TestCase("Immune")]
        [TestCase("immune")]
        [TestCase(" IMMUNE ")]
        public void TypeName_IsCaseInsensitive(string value)
            => Assert.That(DamageImmunity.IsImmuneType(value), Is.True);

        [TestCase("Pyro;Hydro", "Pyro", "Hydro")]
        [TestCase(" pyro ; HYDRO ", "Pyro", "Hydro")]
        [TestCase("None;Physical", "None", "Physical")]
        public void Elements_AreExplicitSemicolonLists_NotImplicitAll(string raw, string first, string second)
        {
            Assert.That(DamageImmunity.ParseElements(raw), Is.EqualTo(second == null
                ? new[] { first } : new[] { first, second }));
        }

        [TestCase(null)] [TestCase("")] [TestCase("All")] [TestCase("True")]
        [TestCase("Pyro;")] [TestCase("Pyro;;Hydro")] [TestCase("Lunar")] [TestCase("火")]
        public void InvalidOrMissingElements_FailInsteadOfGrantingImmunity(string value)
            => Assert.Throws<TableValidationException>(() => DamageImmunity.ParseElements(value));

        [Test]
        public void StatusAndEffectMustBothBeImmune_AndNoFixedStatusIDIsRequired()
        {
            StatusInstance status = AddImmune("Pyro;Hydro");
            Assert.That(DamageImmunity.Resolve(target, "Pyro", "SE_Test"), Is.True);
            Assert.That(DamageImmunity.Resolve(target, "Hydro", "SE_Test"), Is.True);
            Assert.That(DamageImmunity.Resolve(target, "Electro", "SE_Test"), Is.False);
            Assert.DoesNotThrow(() => DamageImmunity.ValidateDefinitions(env.DataManager));
            status.MainData.StatusType = "Buff";
            Assert.That(DamageImmunity.Resolve(target, "Pyro", "SE_Test"), Is.False);
            Assert.Throws<TableValidationException>(() => DamageImmunity.ValidateDefinitions(env.DataManager));
            status.MainData.StatusType = "Immune";
            env.DataManager.StatusEffectDict.Clear();
            Assert.That(DamageImmunity.Resolve(target, "Pyro", "SE_Test"), Is.False);
            Assert.Throws<TableValidationException>(() => DamageImmunity.ValidateDefinitions(env.DataManager));
        }

        [Test]
        public void PassiveImmune_DoesNotRequireTargets_AndCannotBeASkillEffect()
        {
            Assert.DoesNotThrow(() => TableValidator.ValidateEffectRow("Status_Effect", 3,
                "Immune", "Pyro;Hydro", 0, "", "", 0, 0, "", false,
                checkTargetNumber: false, isStatusEffectTable: true));
            Assert.Throws<TableValidationException>(() => TableValidator.ValidateEffectRow("SkillsEffect", 3,
                "Immune", "Pyro", 0, "", "Self", 0, 0, "", false));
            DefineImmune("Pyro");
            env.DataManager.StatusEffectDict["STE_Test_Immune"].Param1 = "1";
            Assert.Throws<TableValidationException>(() => DamageImmunity.ValidateDefinitions(env.DataManager));
        }

        [Test]
        public void ImmuneHit_PreservesHitAndHistory_ButConsumesNeitherShieldNorHP()
        {
            StatusInstance immunity = AddImmune("Pyro;Hydro");
            StatusInstance onHit = HookTestEnv.AttachStatus(target, "ST_Immune_OnHit", target, 3);
            StatusInstance onDamage = HookTestEnv.AttachStatus(target, "ST_Immune_OnDamage", target, 3);
            HookTestEnv.InjectStatusAction(env.DataManager, onHit.StatusID2, "OnTrigger", "", "", hitSource: "All");
            HookTestEnv.InjectStatusAction(env.DataManager, onDamage.StatusID2, "OnTrigger", "", "", damageSource: "All");
            target.AddShield(50f, "None", 3f);
            Shield shield = target.Shields[0];
            HitResolution result;
            using (var batch = new HitBatch()) result = batch.Resolve(Request("Pyro"));
            Assert.That(result.DamageEvent.HitLanded, Is.True);
            Assert.That(result.DamageEvent.IsImmune, Is.True);
            Assert.That(result.DamageEvent.ImmuneDamage, Is.EqualTo(25f));
            Assert.That(result.DamageEvent.ShieldAbsorbed, Is.Zero);
            Assert.That(result.DamageEvent.ActualHPDamage, Is.Zero);
            Assert.That(shield.Value, Is.EqualTo(50f));
            Assert.That(target.CurrentHP, Is.EqualTo(100f));
            Assert.That(onHit.LifeTriggerCount, Is.EqualTo(1));
            Assert.That(onDamage.LifeTriggerCount, Is.Zero);
            var fact = result.DamageEvent.Declaration.Get<DamageEventData>();
            var snapshot = result.DamageEvent.Snapshot();
            target.RemoveStatusInstance(immunity);
            Assert.That(fact.IsImmune, Is.True);
            Assert.That(fact.ImmuneDamage, Is.EqualTo(25f));
            Assert.That(snapshot.IsImmune, Is.True);
            Assert.That(DamageImmunity.Resolve(target, "Pyro", "SE_Test"), Is.False);
        }

        [Test]
        public void MultipleInstances_StayImmuneUntilLastApplicableInstanceIsRemoved()
        {
            StatusInstance first = AddImmune("None;Pyro");
            StatusInstance second = target.AddStatus(StatusID, target, 0, 2, 0, first.MainData);
            target.RemoveStatusInstance(first);
            Assert.That(DamageImmunity.Resolve(target, "Physical", "SE_Test"), Is.False);
            Assert.That(DamageImmunity.Resolve(target, "None", "SE_Test"), Is.True);
            target.RemoveStatusInstance(second);
            Assert.That(DamageImmunity.Resolve(target, "None", "SE_Test"), Is.False);
        }

        [TestCase("None", "None", true)]
        [TestCase("None", "Physical", false)]
        [TestCase("Physical", "None", false)]
        [TestCase("Physical", "Physical", true)]
        [TestCase("None;Physical", "True", false)]
        public void DirectAndPhysicalImmunityAreSeparate_TrueAlwaysBypasses(string immune, string element, bool blocked)
        {
            AddImmune(immune); target.AddShield(50, "None", 3);
            using var batch = new HitBatch();
            var hit = batch.Resolve(Request(element)).DamageEvent;
            Assert.That(hit.IsImmune, Is.EqualTo(blocked));
            Assert.That(target.Shields[0].Value, Is.EqualTo(blocked || element == "True" ? 50 : 25));
            Assert.That(target.CurrentHP, Is.EqualTo(element == "True" ? 75 : 100));
        }

        [Test]
        public void DamageElementControlsReactionImmunity_TrueBypasses_AndPlainZeroIsNotImmune()
        {
            AddImmune("Pyro");
            var reaction = Request("Pyro");
            reaction.Source = DamageSourceInfo.Create(env.Enemy.Entity, ReactionSourceKind.DerivedReaction,
                "SK_Trigger", "SE_Trigger", ReactionType.Overloaded);
            using (var batch = new HitBatch())
            {
                DamageResolvedEvent hit = batch.Resolve(reaction).DamageEvent;
                Assert.That(hit.IsImmune, Is.True);
                Assert.That(hit.HitLanded, Is.False, "派生伤害不伪造攻击命中");
            }
            using (var batch = new HitBatch())
            {
                Assert.That(batch.Resolve(Request("True")).DamageEvent.IsImmune, Is.False);
                Assert.That(target.CurrentHP, Is.EqualTo(75f));
                var zero = Request("Electro"); zero.DamageBeforeShield = 0f;
                Assert.That(batch.Resolve(zero).DamageEvent.IsImmune, Is.False);
            }
        }

        [Test]
        public void ImmuneDamage_DoesNotBlockElementApplicationOrReaction()
        {
            AddImmune("Pyro");
            target.ApplyAura("Cryo", 1f, env.Enemy.Entity.EntityID, "SE_Aura", "SK_Aura");
            var reaction = ReactionResolver.Resolve(new ReactionContext { SourceEntity = env.Enemy.Entity,
                Target = target, AttackElement = "Pyro", AttackAmount = 1f, PreReactionDamage = 25f,
                ApplicationPhase = 2 });
            Assert.That(reaction.TriggeredReactions.Exists(r => r.Type == ReactionType.Melt), Is.True);
            var request = Request("Pyro"); request.DamageBeforeShield = reaction.FinalDamage;
            using (var batch = new HitBatch()) Assert.That(batch.Resolve(request).DamageEvent.IsImmune, Is.True);
            Assert.That(target.CurrentHP, Is.EqualTo(100f));
            Assert.That(ElementQuery.ReadNormal(target, "Cryo"), Is.Null);
            target.ApplyAura("Hydro", 1f, env.Enemy.Entity.EntityID, "SE_Aura", "SK_Aura");
            Assert.That(ElementQuery.HasAura(target, "Hydro"), Is.True);
        }

        [Test]
        public void TeamImmune_ProjectsToItsSideOnly_WithoutCopyingTheStatus()
        {
            StatusMainData main = DefineImmune("Electro");
            env.Manager.StartBattle();
            var status = env.Manager.AllyTeam.AddStatus(StatusID, target, 0, 2, 0, main);
            var reserve = env.CreateAlly(2, false).Entity;
            Assert.That(DamageImmunity.Resolve(target, "Electro", "SE_Test"), Is.True);
            Assert.That(DamageImmunity.Resolve(reserve, "Electro", "SE_Test"), Is.True);
            Assert.That(DamageImmunity.Resolve(env.Enemy.Entity, "Electro", "SE_Test"), Is.False);
            Assert.That(reserve.GetStatusList(), Has.No.Member(status));
            env.Manager.AllyTeam.RemoveStatusInstance(status);
            Assert.That(DamageImmunity.Resolve(target, "Electro", "SE_Test"), Is.False);
        }

        [Test]
        public void ImmunityCondition_IsEvaluatedOnce_NotAgainForDeclarationOrPresentation()
        {
            AddImmune("Pyro");
            env.DataManager.StatusEffectDict["STE_Test_Immune"].ScriptHook = "Chance(0.5)";
            var random = new SequenceBattleRandomSource(new[] { .1f }); BattleRandom.SetSource(random);
            using (var batch = new HitBatch())
            {
                var hit = batch.Resolve(Request("Pyro")).DamageEvent;
                Assert.That(hit.Declaration.Get<DamageEventData>().IsImmune, Is.True);
                Assert.That(hit.Snapshot().IsImmune, Is.True);
            }
            Assert.That(random.FloatCallCount, Is.EqualTo(1));
        }


        private StatusMainData DefineImmune(string elements)
        {
            var main = new StatusMainData { StatusID = MainID, StatusID2 = StatusID,
                StatusType = "Immune", MaxStack = 3, MaxCount = 1 };
            env.DataManager.StatusMainDict[StatusID] = main;
            env.DataManager.StatusEffectDict["STE_Test_Immune"] = new StatusEffectData
            { StatusEffectID = MainID * 100 + 1, StatusEffectID2 = "STE_Test_Immune", EffectType = "Immune", Element = elements };
            return main;
        }

        private StatusInstance AddImmune(string elements)
            => target.AddStatus(StatusID, target, 0, 2, 0, DefineImmune(elements));

        private HitRequest Request(string element) => new HitRequest { Target = target, Element = element,
            DamageBeforeShield = 25f, DamageType = "Normal",
            Source = DamageSourceInfo.Create(env.Enemy.Entity, ReactionSourceKind.EnemySkill, "SK_Test", "SE_Test") };
    }
}
