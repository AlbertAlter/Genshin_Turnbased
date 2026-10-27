using System.Collections.Generic;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>510/511新版规则的内存用例，须人工通过Unity运行；不读取策划源表。</summary>
    public sealed class AdventurerAndLuckyDogArtifactTests
    {
        [Test]
        public void Adventurer_InitializesOnlyAfterFullRoster_AndDoesNotEmitHealingOrDuplicateBonuses()
        {
            using (var env = new BattleFlowTestEnv())
            {
                Configure(env);
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                Assert.That(env.Ally.InitializeArtifactSetStatuses(new Dictionary<int, int> { [510] = 4 }), Is.True);
                Assert.That(owner.TotalHP, Is.EqualTo(1000f));
                Assert.That(owner.HasStatus(AdventurerArtifactHooks.SharedID), Is.False, "不以尚未组齐的半队初始化绑定");
                var reserve = env.CreateAlly(2, false).Entity;
                var wounded = env.CreateAlly(3, false).Entity;
                var fourth = env.CreateAlly(4, false).Entity;
                wounded.CurrentHP = 550f;
                int heals = 0, hpChanges = 0;
                using (BattleEventHub.Subscribe<HealEventData>(BattleEventKind.HealResolved, (header, fact) => heals++, 0))
                using (BattleEventHub.Subscribe<HPChangedEventData>(BattleEventKind.HPChanged, (header, fact) => hpChanges++, 0))
                {
                    env.Manager.StartBattle();
                    Assert.That(owner.TotalHP, Is.EqualTo(2200f), "本人两件套1000，加全队200");
                    Assert.That(owner.CurrentHP, Is.EqualTo(2200f));
                    foreach (var member in new[] { reserve, wounded, fourth })
                    {
                        Assert.That(member.TotalHP, Is.EqualTo(1200f));
                        var child = member.GetStatus(AdventurerArtifactHooks.SharedID);
                        Assert.That(child, Is.Not.Null);
                        Assert.That(child.IsPermanent, Is.True);
                        Assert.That(child.Caster, Is.SameAs(owner));
                        Assert.That(child.EquipmentSource.Owner, Is.SameAs(owner));
                        Assert.That(child.MainData.Display, Is.Zero);
                    }
                    Assert.That(reserve.CurrentHP, Is.EqualTo(1200f));
                    Assert.That(wounded.CurrentHP, Is.EqualTo(550f), "非满血保持绝对值，不将上限加成做成治疗");
                    Assert.That(heals, Is.Zero);
                    Assert.That(hpChanges, Is.Zero, "开战赋值不冒充战斗内获取/生命变化");
                    Assert.That(env.Ally.InitializeArtifactSetStatuses(new Dictionary<int, int> { [510] = 4 }), Is.True);
                    BattleSpecialRules.OnBattleReady(env.Manager);
                    Assert.That(owner.TotalHP, Is.EqualTo(2200f));
                    Assert.That(reserve.GetStatusInstances(AdventurerArtifactHooks.SharedID).Count, Is.EqualTo(1));
                    Assert.That(heals, Is.Zero);
                    Assert.That(hpChanges, Is.Zero);
                }
            }
        }

        [Test]
        public void Adventurer_TwoPiecesOnlyIncreaseWearerMaximum()
        {
            using (var env = new BattleFlowTestEnv())
            {
                Configure(env);
                env.CreateMinimalBattle();
                var reserve = env.CreateAlly(2, false).Entity;
                env.Ally.InitializeArtifactSetStatuses(new Dictionary<int, int> { [510] = 2 });
                env.Manager.StartBattle();
                Assert.That(env.Ally.Entity.TotalHP, Is.EqualTo(2000f));
                Assert.That(reserve.TotalHP, Is.EqualTo(1000f));
                Assert.That(reserve.HasStatus(AdventurerArtifactHooks.SharedID), Is.False);
            }
        }

        [Test]
        public void Adventurer_TwoWearersKeepIndependentBindings_ButShareMaxCountAndOnlyLoseOwnSource()
        {
            using (var env = new BattleFlowTestEnv())
            {
                Configure(env);
                env.CreateMinimalBattle();
                var first = env.Ally.Entity;
                var secondController = env.CreateAlly(2, false);
                var second = secondController.Entity;
                var recipient = env.CreateAlly(3, false).Entity;
                env.Ally.InitializeArtifactSetStatuses(new Dictionary<int, int> { [510] = 4 });
                secondController.InitializeArtifactSetStatuses(new Dictionary<int, int> { [510] = 4 });
                env.Manager.StartBattle();
                Assert.That(recipient.GetStatusInstances(AdventurerArtifactHooks.SharedID).Count, Is.EqualTo(2));
                Assert.That(recipient.TotalHP, Is.EqualTo(1200f), "同名最多计一份200，不是400");
                int clamps = 0, heals = 0;
                using (BattleEventHub.Subscribe<HPChangedEventData>(BattleEventKind.HPChanged, (header, fact) =>
                { if (fact.Subject == recipient && fact.Reason == "MaxHPAdjustment") clamps++; }, 0))
                using (BattleEventHub.Subscribe<HealEventData>(BattleEventKind.HealResolved, (header, fact) => heals++, 0))
                {
                    first.TakeDamage(first.TotalHP + 1f, EnemySource(env));
                    Assert.That(recipient.GetStatusInstances(AdventurerArtifactHooks.SharedID).Count, Is.EqualTo(1));
                    Assert.That(recipient.TotalHP, Is.EqualTo(1200f), "第二来源还在，第一来源阵亡不撤走全部加成");
                    Assert.That(first.CurrentHP, Is.Zero, "撤属性不复活装备者");
                    second.TakeDamage(second.TotalHP + 1f, EnemySource(env));
                    Assert.That(recipient.HasStatus(AdventurerArtifactHooks.SharedID), Is.False);
                    Assert.That(recipient.TotalHP, Is.EqualTo(1000f));
                    Assert.That(recipient.CurrentHP, Is.EqualTo(1000f), "旧当前血量不得超过新上限");
                    Assert.That(clamps, Is.EqualTo(1), "真实钳制只声明一次，不伪造伤害或治疗");
                    Assert.That(heals, Is.Zero);
                    first.CurrentHP = first.TotalHP;
                    Assert.That(recipient.TotalHP, Is.EqualTo(1000f), "复活不恢复旧装备身份的绑定");
                }
            }
        }

        [Test]
        public void Adventurer_ResetRetractsAppliedStats_AndSameEntityCanInitializeANewBattle()
        {
            using (var env = new BattleFlowTestEnv())
            {
                Configure(env);
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                env.Ally.InitializeArtifactSetStatuses(new Dictionary<int, int> { [510] = 4 });
                env.Manager.StartBattle();
                Assert.That(owner.TotalHP, Is.EqualTo(2200f));
                env.Manager.ResetBattle();
                Assert.That(owner.TotalHP, Is.EqualTo(1000f));
                Assert.That(owner.CurrentHP, Is.EqualTo(1000f));
                env.Manager.RegisterAlly(env.Ally);
                env.Manager.RegisterEnemy(env.Enemy);
                env.Ally.InitializeArtifactSetStatuses(new Dictionary<int, int> { [510] = 4 });
                env.Manager.StartBattle();
                Assert.That(owner.TotalHP, Is.EqualTo(2200f), "上一场净差账本不能使新场重复增加或少加");
            }
        }

        [Test]
        public void LuckyDog_DamageTakenIsAdditiveInReceiverZone_AndAppliedBeforeShieldAndHP()
        {
            using (var env = new BattleFlowTestEnv())
            {
                Configure(env);
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                env.Ally.InitializeArtifactSetStatuses(new Dictionary<int, int> { [511] = 4 });
                Assert.That(owner.GetFinalDEF(), Is.EqualTo(200f));
                Assert.That(owner.PrepareDMGTaken("effect", "skill", "Normal", "Physical").GetMultiplier(ReactionType.None),
                    Is.EqualTo(.92f).Within(.00001f));
                var vulnerability = Main(env, 459980, "DMGTaken");
                Effect(env, 45998001, "Buff", "0.2");
                owner.AddStatus(vulnerability.StatusID2, owner, 0, 1, 0, vulnerability);
                float before = owner.CurrentHP;
                using (var batch = new HitBatch())
                {
                    var result = batch.Resolve(new HitRequest { Target = owner, DamageBeforeShield = 100f,
                        Element = "Pyro", DamageType = "Normal", Source = EnemySource(env) });
                    Assert.That(result.DMGTakenMultiplier, Is.EqualTo(1.12f).Within(.00001f),
                        "1 - 0.08 + 0.20，不是0.92*1.20");
                    Assert.That(result.DamageBeforeShield, Is.EqualTo(112f).Within(.0001f));
                    Assert.That(owner.CurrentHP, Is.EqualTo(before - 112f).Within(.0001f));
                }
            }
        }

        private static DamageSourceInfo EnemySource(BattleFlowTestEnv env)
            => DamageSourceInfo.Create(env.Enemy.Entity, ReactionSourceKind.EnemySkill, "test", "test");

        private static void Configure(BattleFlowTestEnv env)
        {
            Main(env, 451001, "HPBonusFlat");
            Main(env, 451002, "");
            var shared = Main(env, 451003, "HPBonusFlat");
            shared.MaxStack = 0;
            Main(env, 451101, "DEFBonusFlat");
            Main(env, 451102, "DMGTaken");
            Effect(env, 45100101, "Buff", "1000");
            var binding = Effect(env, 45100201, "BindStatus", AdventurerArtifactHooks.SharedID);
            binding.TargetType = "Allies";
            binding.TargetSelect = "-1";
            binding.ScriptHook = "Hook(45100201)";
            Effect(env, 45100301, "Buff", "200");
            Effect(env, 45110101, "Buff", "100");
            Effect(env, 45110201, "Buff", "-0.08");
            env.DataManager.ArtifactInitiateStatusDict[510] = new Dictionary<int, List<string>>
            { [2] = new List<string> { AdventurerArtifactHooks.PersonalID }, [4] = new List<string> { AdventurerArtifactHooks.ParentID } };
            env.DataManager.ArtifactInitiateStatusDict[511] = new Dictionary<int, List<string>>
            { [2] = new List<string> { "ST_Artifact_451101" }, [4] = new List<string> { "ST_Artifact_451102" } };
        }

        private static StatusMainData Main(BattleFlowTestEnv env, int id, string part)
        {
            var main = new StatusMainData { StatusID = id, StatusID2 = "ST_Artifact_" + id,
                StatusType = string.IsNullOrEmpty(part) ? "" : "Buff", Display = 0,
                MultiplierPart1 = part, MaxStack = 1, MaxCount = 1, WhenMax = "1" };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            return main;
        }

        private static StatusEffectData Effect(BattleFlowTestEnv env, int id, string type, string value)
        {
            var effect = new StatusEffectData { StatusEffectID = id, StatusEffectID2 = "STE_Artifact_" + id,
                EffectIndex = 1, EffectType = type, Param1 = value, TargetType = "Self" };
            env.DataManager.StatusEffectDict[effect.StatusEffectID2] = effect;
            return effect;
        }
    }
}
