using System;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>534/547生命、532/534护盾强效及537公式；仅编写，须人工在Unity运行。</summary>
    public sealed class ArtifactHPShieldAndEmblemTests
    {
        [Test]
        public void InitialHP_UsesBaseForPercent_ThenAddsFlat_AndIsIdempotentWithoutHealing()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                // 原面板已有50%生命加成，不得再被圣遗物百分比相乘。
                owner.TotalHP = owner.CurrentHP = 1500f;
                BattleAttributeInitialization.BeginBuild(owner);
                Add(owner, owner, Buff(env, 453401, "HPBonus", "0.2"));
                Add(owner, owner, Buff(env, 454701, "HPBonus", "0.2"));
                Add(owner, owner, Buff(env, 451001, "HPBonusFlat", "1000"));
                int heals = 0, changes = 0;
                using (BattleEventHub.Subscribe<HealEventData>(BattleEventKind.HealResolved, (h, e) => heals++, 0))
                using (BattleEventHub.Subscribe<HPChangedEventData>(BattleEventKind.HPChanged, (h, e) => changes++, 0))
                {
                    env.Manager.StartBattle();
                    Assert.That(owner.TotalHP, Is.EqualTo(2900f).Within(.001f));
                    Assert.That(owner.CurrentHP, Is.EqualTo(2900f).Within(.001f));
                    BattleSpecialRules.OnBattleReady(env.Manager);
                    Assert.That(owner.TotalHP, Is.EqualTo(2900f).Within(.001f));
                    Assert.That(heals, Is.Zero);
                    Assert.That(changes, Is.Zero);
                }
            }
        }

        [Test]
        public void InitialHP_KeepsWoundedAbsoluteHP_AndRemovalOnlyDeclaresRealClamp()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                owner.CurrentHP = 550f;
                var status = Add(owner, owner, Buff(env, 453401, "HPBonus", "0.2"));
                env.Manager.StartBattle();
                Assert.That(owner.TotalHP, Is.EqualTo(1200f).Within(.001f));
                Assert.That(owner.CurrentHP, Is.EqualTo(550f));
                owner.CurrentHP = 1190f;
                int clamps = 0, heals = 0;
                using (BattleEventHub.Subscribe<HPChangedEventData>(BattleEventKind.HPChanged, (h, e) =>
                { if (e.Subject == owner && e.Reason == "MaxHPAdjustment") clamps++; }, 0))
                using (BattleEventHub.Subscribe<HealEventData>(BattleEventKind.HealResolved, (h, e) => heals++, 0))
                {
                    owner.RemoveStatusInstance(status, "Expired");
                    Assert.That(owner.TotalHP, Is.EqualTo(1000f).Within(.001f));
                    Assert.That(owner.CurrentHP, Is.EqualTo(1000f).Within(.001f));
                    Assert.That(clamps, Is.EqualTo(1));
                    Assert.That(heals, Is.Zero);
                }
            }
        }

        [Test]
        public void InitialHP_ResetRetractsOnlyItsBonus_AndDeathDoesNotResurrectWearer()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                var main = Buff(env, 454701, "HPBonus", "0.2");
                Add(owner, owner, main);
                env.Manager.StartBattle();
                env.Manager.ResetBattle();
                Assert.That(owner.TotalHP, Is.EqualTo(1000f).Within(.001f));
                env.Manager.RegisterAlly(env.Ally);
                env.Manager.RegisterEnemy(env.Enemy);
                Add(owner, owner, main);
                env.Manager.StartBattle();
                Assert.That(owner.TotalHP, Is.EqualTo(1200f).Within(.001f));
                owner.TakeDamage(10000f, DamageSourceInfo.Create(env.Enemy.Entity,
                    ReactionSourceKind.EnemySkill, "test", "test"));
                Assert.That(owner.CurrentHP, Is.Zero);
                Assert.That(owner.TotalHP, Is.EqualTo(1000f).Within(.001f));
            }
        }

        [Test]
        public void ShieldStrength_AddsOnReceiver_AndAllShieldsLoseValueConcurrently()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                owner.ShieldStrength = .1f;
                Add(owner, owner, Buff(env, 453201, "ShieldStrength", "0.35"));
                Add(owner, owner, Buff(env, 453404, "ShieldStrength", "0.3"));
                owner.AddShield(100f, "None", 3f);
                owner.AddShield(80f, "None", 3f, 2f);
                Assert.That(owner.GetShieldStrengthMultiplier(), Is.EqualTo(1.75f).Within(.00001f));
                Assert.That(owner.GetTotalShieldHP(), Is.EqualTo(280f).Within(.001f));
                Assert.That(owner.GetTotalShieldHP("Pyro"), Is.EqualTo(280f).Within(.001f));
                Assert.That(owner.Shields[0].Value, Is.EqualTo(100f));
                Assert.That(owner.Shields[1].Value, Is.EqualTo(80f));
                Assert.That(owner.AbsorbDamageWithShield(70f, "Pyro"), Is.Zero);
                Assert.That(owner.Shields[0].Value, Is.EqualTo(60f).Within(.001f));
                Assert.That(owner.Shields[1].Value, Is.EqualTo(60f).Within(.001f));
                Assert.That(owner.GetTotalShieldHP(), Is.EqualTo(210f).Within(.001f));
            }
        }

        [Test]
        public void ShieldStrength_MultipleSourcesRespectMaxCount_AndDeathOnlyRemovesItsSource()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var first = env.Ally.Entity;
                var second = env.CreateAlly(2, false).Entity;
                var recipient = env.CreateAlly(3, false).Entity;
                Add(recipient, recipient, Buff(env, 453201, "ShieldStrength", "0.35"));
                var shared = Buff(env, 453404, "ShieldStrength", "0.3");
                shared.MaxStack = 0;
                Add(recipient, first, shared);
                Add(recipient, second, shared);
                recipient.AddShield(100f, "None", 3f);
                Assert.That(recipient.GetStatusInstances(shared.StatusID2).Count, Is.EqualTo(2));
                Assert.That(recipient.GetShieldStrengthMultiplier(), Is.EqualTo(1.65f).Within(.00001f));
                first.TakeDamage(10000f, DamageSourceInfo.Create(env.Enemy.Entity,
                    ReactionSourceKind.EnemySkill, "test", "test"));
                Assert.That(recipient.GetShieldStrengthMultiplier(), Is.EqualTo(1.65f).Within(.00001f));
                second.TakeDamage(10000f, DamageSourceInfo.Create(env.Enemy.Entity,
                    ReactionSourceKind.EnemySkill, "test", "test"));
                Assert.That(recipient.GetShieldStrengthMultiplier(), Is.EqualTo(1.35f).Within(.00001f));
                Assert.That(recipient.Shields[0].Value, Is.EqualTo(100f), "强效撤销不重写原始盾值");
            }
        }

        [Test]
        public void Emblem_ReadsCastersLiveFinalRecharge_AndCapsOnlyBurstBonus()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var source = env.Ally.Entity;
                var recipient = env.CreateAlly(2, false).Entity;
                source.TotalRechargeRate = 1.8f;
                recipient.TotalRechargeRate = 1f;
                Add(source, source, Buff(env, 453701, "RechargeBonus", "0.2"));
                var emblem = Buff(env, 453702, "DMGBonus", "min(0.75,0.25*TotalRechargeRate)");
                emblem.ApplyDamageType = "Burst";
                Add(recipient, source, emblem);
                env.DataManager.SkillEffectDict["SE_Emblem_Burst"] = new SkillEffectData
                { SkillEffectID2 = "SE_Emblem_Burst", EffectType = "Damage", Element = "Pyro", DamageType = "Burst" };
                env.DataManager.SkillEffectDict["SE_Emblem_Skill"] = new SkillEffectData
                { SkillEffectID2 = "SE_Emblem_Skill", EffectType = "Damage", Element = "Pyro", DamageType = "Skill" };
                Assert.That(recipient.GetStatusDMGBonus("SE_Emblem_Burst", "", 0, "Pyro", env.Enemy.Entity),
                    Is.EqualTo(.5f).Within(.00001f));
                Assert.That(recipient.GetStatusDMGBonus("SE_Emblem_Skill", "", 3, "Pyro", env.Enemy.Entity), Is.Zero);
                source.TotalRechargeRate = 3.8f;
                Assert.That(recipient.GetStatusDMGBonus("SE_Emblem_Burst", "", 0, "Pyro", env.Enemy.Entity),
                    Is.EqualTo(.75f).Within(.00001f));
                Assert.That(source.TotalRechargeRate, Is.EqualTo(3.8f));
                Assert.That(recipient.TotalRechargeRate, Is.EqualTo(1f));
            }
        }

        [Test]
        public void ScalarMin_ComposesWithExistingGrammar_AndRejectsUnknownOrRecursiveAttributes()
        {
            Assert.That(BuffScalarParser.Evaluate("min(7.5e-1,0.25*(Index(1)+Count(Self,ST_Count)))",
                n => 1f, id => 1f), Is.EqualTo(.5f));
            Assert.That(BuffScalarParser.Evaluate("min(.75,.25*TotalRechargeRate)",
                readAttribute: name => 2f), Is.EqualTo(.5f));
            foreach (var expression in new[] { "min(1)", "min(1,2,3)", "max(1,2)", "TotalRechargeRate" })
                Assert.Throws<FormatException>(() => BuffScalarParser.Evaluate(expression), expression);
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var invalid = Add(env.Ally.Entity, env.Ally.Entity,
                    Buff(env, 459981, "RechargeBonus", "TotalRechargeRate"));
                Assert.Throws<InvalidOperationException>(() => env.Ally.Entity.GetFinalRechargeRate());
                Assert.That(BuffExpressionEvaluator.TryResolveScalar("OtherAttribute", invalid, out _), Is.False);
            }
        }

        [TestCase(1250f, 1250f, 0)]
        [TestCase(1400f, 1300f, 1)]
        public void InitialHP_SourceDeathRetractsOnlyBakedBonus_AndClampsToRemainingFinalMaximum(
            float currentHP, float expectedHP, int expectedClamps)
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var source = env.Ally.Entity; var recipient = env.CreateAlly(2, false).Entity;
            Add(recipient, source, Buff(env, 451003, "HPBonusFlat", "200"));
            Add(recipient, recipient, Buff(env, 459981, "HPBonus", ".3"));
            env.Manager.StartBattle();
            Assert.That(recipient.TotalHP, Is.EqualTo(1200f));
            Assert.That(recipient.GetFinalHP(), Is.EqualTo(1500f));
            recipient.CurrentHP = currentHP;
            int clamps = 0, heals = 0;
            using var changes = BattleEventHub.Subscribe<HPChangedEventData>(BattleEventKind.HPChanged, (_, change) =>
            { if (change.Subject == recipient && change.Reason == "MaxHPAdjustment") clamps++; }, 0);
            using var healing = BattleEventHub.Subscribe<HealEventData>(BattleEventKind.HealResolved, (_, change) => heals++, 0);
            source.TakeDamage(10000f, DamageSourceInfo.Create(env.Enemy.Entity, ReactionSourceKind.EnemySkill, "test", "test"));
            Assert.That(recipient.TotalHP, Is.EqualTo(1000f));
            Assert.That(recipient.GetFinalHP(), Is.EqualTo(1300f));
            Assert.That(recipient.CurrentHP, Is.EqualTo(expectedHP));
            Assert.That(clamps, Is.EqualTo(expectedClamps)); Assert.That(heals, Is.Zero);
        }

        private static StatusMainData Buff(BattleFlowTestEnv env, int id, string part, string value)
        {
            var main = new StatusMainData { StatusID = id, StatusID2 = "ST_Artifact_" + id,
                StatusType = "Buff", MultiplierPart = part, MaxStack = 1, MaxCount = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            var effect = new StatusEffectData { StatusEffectID = id * 100 + 1,
                StatusEffectID2 = "STE_Artifact_" + (id * 100 + 1), EffectIndex = 1,
                EffectType = "Buff", Param1 = value, TargetType = "Self" };
            env.DataManager.StatusEffectDict[effect.StatusEffectID2] = effect;
            return main;
        }

        private static StatusInstance Add(BattleEntity host, BattleEntity source, StatusMainData main)
            => host.AddStatus(main.StatusID2, source, 0, 1, 0, main);
    }
}
