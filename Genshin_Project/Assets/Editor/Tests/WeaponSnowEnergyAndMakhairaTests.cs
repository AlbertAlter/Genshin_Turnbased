using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>3062/3064/3068真实入口定义；不自动运行Unity测试。</summary>
    public sealed class WeaponSnowEnergyAndMakhairaTests
    {
        private static string Key(int id) => DataManager.WeaponStatusKey(id);
        private static string Eff(int id) => "STE_Weapon_" + id;
        private static StatusMainData Main(BattleFlowTestEnv env, int id, string part = null, bool independent = false)
        {
            var main = new StatusMainData { StatusID = id, StatusID2 = Key(id), StatusType = "Buff",
                MultiplierPart1 = part, MaxStack = independent ? 0 : 1, MaxCount = independent ? 0 : 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[Key(id)] = main; return main;
        }
        private static StatusEffectData Effect(BattleFlowTestEnv env, int id, string type, string parameter)
        {
            var effect = new StatusEffectData { StatusEffectID = id, StatusEffectID2 = Eff(id), EffectType = type,
                Param1 = parameter, TargetType = "Self" };
            env.DataManager.StatusEffectDict[Eff(id)] = effect; return effect;
        }
        private static StatusActionData Action(BattleFlowTestEnv env, int id, string type, string effects)
        {
            var action = new StatusActionData { StatusID = id, StatusID2 = Key(id), ActionType = type, Param1 = effects };
            env.DataManager.StatusActionDict[Key(id)] = new List<StatusActionData> { action }; return action;
        }
        private static WeaponRuntimeContext Weapon(BattleEntity owner, int id, params float[] parameters)
        {
            var context = new WeaponRuntimeContext { Equipper = owner, WeaponID = id, Refinement = 2,
                SourceInstanceID = "SnowEnergyMakhaira:" + id + ":" + owner.EntityID };
            for (int i = 0; i < parameters.Length; i++) context.ResolvedParams[i + 1] = parameters[i];
            return context;
        }
        private static StatusInstance Add(BattleFlowTestEnv env, BattleEntity owner, int id, WeaponRuntimeContext weapon)
            => owner.AddStatus(Key(id), owner, 0, 2, 0, env.DataManager.StatusMainDict[Key(id)], weapon);
        private static void Start(BattleFlowTestEnv env)
        { BattleRandom.SetSource(new SequenceBattleRandomSource()); env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction); }
        private static void Next(BattleFlowTestEnv env)
        { env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction); }

        private static void Snow(BattleFlowTestEnv env, int weapon = 3062)
        {
            int id = (40000 + weapon) * 100 + 1, effectID = id * 100 + 1;
            Main(env, id);
            var action = Action(env, id, "OnTrigger", Eff(effectID));
            action.ScriptHook = "PostSelfDamage(" + id + ");Hook(" + id + ")"; action.OutgoingHit = "Normal;Heavy";
            action.MaxTimePerEffect = 1; action.Cooldown = 3;
            var damage = Effect(env, effectID, "Damage", "Index(2)*TotalATK,0");
            damage.Element = "Cryo"; damage.Param2 = weapon == 3113 ? null : "Splash(1;1,1)"; damage.TargetType = "Enemy";
            damage.TargetSelect = "1"; damage.TargetConsecutive = 4;
            damage.TargetOverride = "PostSelfDamage(" + id + ")"; damage.DamageHook = "Hook(" + effectID + ")";
            env.ConfigureAllyAction(0, "SK_Snow", "SE_Snow", "Physical", ".01*TotalATK,0", 0);
            env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_0"][1].Hits2 = ".01*TotalATK,0";
        }

        [TestCase(3062), TestCase(3138), Timeout(7000)]
        public void Crystal_OneChancePerEffect_IndependentCryoTierResistanceAndCrit_ZeroAuraAndCooldown(int weapon)
        {
            using var env = new BattleFlowTestEnv(); Snow(env, weapon); env.CreateMinimalBattle(); int id = (40000 + weapon) * 100 + 1;
            var left = env.Enemy.Entity; var center = env.CreateEnemy(2).Entity; var right = env.CreateEnemy(3).Entity;
            left.CryoRes = .2f; right.CryoRes = .5f; var owner = env.Ally.Entity;
            owner.CritRate = .5f; owner.CritDMG = 1f; Add(env, owner, id, Weapon(owner, weapon, .7f, .95f, 2.4f));
            Start(env); env.Ally.ForcedTargetPositions.Add(2);
            left.ApplyAura("Hydro", 1f, owner.EntityID); center.ApplyAura("Cryo", 1f, owner.EntityID); right.ApplyAura("Cryo", 1f, owner.EntityID);
            var hits = new List<DamageEventData>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, damage) =>
            { if (damage.Source.SourceEffectID == Eff(id * 100 + 1)) hits.Add(damage); }, 0);
            var random = new SequenceBattleRandomSource(new[] { .9f, .9f, .8f, .9f, .9f, .6f, .1f, .9f, .9f, .9f, .9f });
            BattleRandom.SetSource(random);
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True); Assert.That(hits, Is.Empty, "失败不追加也不占冷却");
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            Assert.That(hits.Select(x => x.Subject), Is.EqualTo(new[] { center, left, right }));
            Assert.That(hits.Select(x => x.DamageBeforeShield), Is.EqualTo(new[] { 240f, 38f, 60f }).Within(.001f));
            Assert.That(hits.Select(x => x.ElementAmount), Is.All.EqualTo(0f));
            Assert.That(hits.Select(x => x.Source.ReactionType), Is.All.EqualTo(ReactionType.None));
            Assert.That(left.GetAura("Hydro").AuraAmount, Is.EqualTo(1f)); Assert.That(left.GetAura("Cryo"), Is.Null);
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True); Assert.That(hits, Has.Count.EqualTo(3));
            Assert.That(random.FloatCallCount, Is.EqualTo(11)); Assert.That(random.IntCallCount, Is.Zero, "单一命中中心不额外抽随机整数");
        }

        [TestCase(3062), TestCase(3113), TestCase(3138), Timeout(7000)]
        public void Crystal_FailedChanceDoesNotRerollForExecuteCountRepeats(int weapon)
        {
            using var env = new BattleFlowTestEnv(); Snow(env, weapon); env.DataManager.SkillEffectDict["SE_Snow"].ExecuteCount = 2;
            int id = (40000 + weapon) * 100 + 1;
            env.CreateMinimalBattle(); Add(env, env.Ally.Entity, id, Weapon(env.Ally.Entity, weapon, .7f, .95f, 2.4f)); Start(env);
            var random = new SequenceBattleRandomSource(new[] { .9f, .9f, .8f, .9f, .9f }); BattleRandom.SetSource(random);
            var hits = new List<DamageEventData>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, damage) =>
            { if (damage.Source.SourceEffectID == Eff(id * 100 + 1)) hits.Add(damage); }, 0);
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            Assert.That(hits, Is.Empty); Assert.That(random.FloatCallCount, Is.EqualTo(5), "四次直伤暴击加一次概率，失败不逐重复重抽");
        }

        [TestCase(false, 47.5f), TestCase(true, 120f), Timeout(7000)]
        public void Dragonspine_OneRandomActualVictim_UsesItsPreHitCryoTier_NoSplash(bool cryo, float expected)
        {
            using var env = new BattleFlowTestEnv(); Snow(env, 3113); env.CreateMinimalBattle();
            var other = env.Enemy.Entity; var selected = env.CreateEnemy(2).Entity; var owner = env.Ally.Entity;
            owner.CritRate = .5f; owner.CritDMG = 1f; selected.CryoRes = .5f;
            Add(env, owner, 4311301, Weapon(owner, 3113, .7f, .95f, 2.4f)); Start(env);
            other.ApplyAura("Cryo", 1f, owner.EntityID); selected.ApplyAura(cryo ? "Cryo" : "Hydro", 1f, owner.EntityID);
            var hits = new List<DamageEventData>();
            using var observer = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            { if (fact.Source.SourceEffectID == Eff(431130101)) hits.Add(fact); }, 0);
            var random = new SequenceBattleRandomSource(new[] { .9f, .9f, .9f, .9f, .6f, .1f }, new[] { 1 });
            BattleRandom.SetSource(random); env.Ally.ForcedTargetPositions.AddRange(new[] { 1, 2 });
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            Assert.That(hits, Has.Count.EqualTo(1)); Assert.That(hits[0].Subject, Is.SameAs(selected));
            Assert.That(hits[0].DamageBeforeShield, Is.EqualTo(expected).Within(.001f));
            Assert.That(hits[0].Element, Is.EqualTo("Cryo")); Assert.That(hits[0].ElementAmount, Is.Zero);
            Assert.That(hits[0].DamageType, Is.Null.Or.Empty); Assert.That(other.CurrentHP, Is.EqualTo(999f).Within(.001f));
            Assert.That(selected.GetAura(cryo ? "Cryo" : "Hydro").AuraAmount, Is.EqualTo(1f));
            Assert.That(random.FloatCallCount, Is.EqualTo(6)); Assert.That(random.IntCallCount, Is.EqualTo(1));
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True); Assert.That(hits, Has.Count.EqualTo(1), "成功后受3回合冷却限制");
        }

        [Test, Timeout(9000)]
        public void Kitain_LosesEnergyOnce_ParentOwnsBothFractionalPhaseDrivers_DeathCleansThem()
        {
            using var env = new BattleFlowTestEnv(); Main(env, 4311501, "DMGBonus").ApplyDamageType = "Skill";
            Main(env, 4311502); Main(env, 4311503); Main(env, 4311504);
            var trigger = Action(env, 4311501, "OnTrigger", Eff(431150102) + ";" + Eff(431150103));
            trigger.OutgoingHit = "Skill"; trigger.MaxTimePerEffect = 1; trigger.Cooldown = 3;
            Effect(env, 431150101, "Buff", "Index(1)"); Effect(env, 431150102, "GainEnergy", "-3").Param2 = "Flat";
            Effect(env, 431150103, "ApplyStatus", Key(4311502)).Duration = 2;
            Effect(env, 431150201, "BindStatus", Key(4311503)).TriggerPhase = 1;
            Effect(env, 431150203, "BindStatus", Key(4311504)).TriggerPhase = 3;
            foreach (int id in new[] { 4311503, 4311504 })
            {
                int effectID = id * 100 + 1; Action(env, id, "OnItsTurn", Eff(effectID));
                var gain = Effect(env, effectID, "GainEnergy", "Index(2)"); gain.Param2 = "Flat"; gain.DamageHook = "Hook(" + effectID + ")";
            }
            env.ConfigureAllyAction(2, "SK_Kitain", "SE_Kitain", "Physical", ".01*TotalATK,0", 0);
            env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_2"][1].Hits2 = ".01*TotalATK,0";
            env.CreateMinimalBattle(); env.CreateAlly(2); var owner = env.Ally.Entity;
            Add(env, owner, 4311501, Weapon(owner, 3115, .075f, 2.5f)); Start(env);
            owner.CurrentEnergy = 2.25f; owner.TotalRechargeRate = 3f; env.Enemy.Entity.AddShield(100f, "", 3f);
            var changes = new List<ResourceEventData>();
            using var observer = BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged, (_, fact) =>
            { if (fact.Subject == owner && fact.Resource == "Energy") changes.Add(fact); }, 0);
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True); Assert.That(owner.CurrentEnergy, Is.Zero);
            var parent = owner.GetStatus(Key(4311502)); var children = parent.BoundStatuses.ToList();
            Assert.That(children, Has.Count.EqualTo(2)); Assert.That(parent.AddInPhase, Is.EqualTo(2));
            Assert.That(children.All(x => x.AddInPhase == parent.AddInPhase && x.RemainingPhaseCount == 2), Is.True);
            Assert.That(changes.Select(x => x.RequestedDelta), Is.EqualTo(new[] { -3d }));
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True); Assert.That(owner.GetStatus(Key(4311502)), Is.SameAs(parent));
            Assert.That(env.Manager.TrySwitchActiveAlly(1), Is.True); Next(env);
            Assert.That(owner.CurrentEnergy, Is.EqualTo(5f)); Next(env); Assert.That(owner.CurrentEnergy, Is.EqualTo(10f));
            Assert.That(parent.IsActive, Is.False); Assert.That(children.All(x => !x.IsActive), Is.True);
            Next(env); Assert.That(owner.CurrentEnergy, Is.EqualTo(10f));
            Assert.That(changes.Select(x => x.RequestedDelta), Is.EqualTo(new[] { -3d, 2.5d, 2.5d, 2.5d, 2.5d }));
            Assert.That(env.Manager.TrySwitchActiveAlly(0), Is.True); Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            var activeChildren = owner.GetStatus(Key(4311502)).BoundStatuses.ToList();
            owner.TakeDamage(2000f, DamageSourceInfo.CreateUnknown());
            Assert.That(activeChildren.All(x => !x.IsActive), Is.True); Assert.That(owner.HasStatus(Key(4311502)), Is.False);
        }

        [Test, Timeout(9000)]
        public void Katsuragikiri_LosesEnergyBeforeWindow_ThenPaysFractionalFlatTwiceEvenOffField()
        {
            using var env = new BattleFlowTestEnv(); Main(env, 4306402); Main(env, 4306403);
            var trigger = Action(env, 4306402, "OnTrigger", Eff(430640201) + ";" + Eff(430640202));
            trigger.OutgoingHit = "Skill"; trigger.MaxTimePerEffect = 1; trigger.Cooldown = 3;
            Action(env, 4306403, "OnItsTurn", Eff(430640301));
            Effect(env, 430640201, "GainEnergy", "-3").Param2 = "Flat";
            var window = Effect(env, 430640202, "ApplyStatus", Key(4306403)); window.Duration = 2; window.AddInPhase = window.TriggerPhase = 3;
            var gain = Effect(env, 430640301, "GainEnergy", "Index(2)"); gain.Param2 = "Flat"; gain.DamageHook = "Hook(430640301)";
            env.ConfigureAllyAction(2, "SK_Katsuragikiri", "SE_Katsuragikiri", "Physical", ".01*TotalATK,0", 0);
            env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_2"][1].Hits2 = ".01*TotalATK,0";
            env.CreateMinimalBattle(); env.CreateAlly(2); var owner = env.Ally.Entity;
            Add(env, owner, 4306402, Weapon(owner, 3064, .075f, 5.75f)); Start(env);
            owner.CurrentEnergy = 2.25f; owner.TotalRechargeRate = 3f; env.Enemy.Entity.AddShield(100f, "", 3f);
            var changes = new List<ResourceEventData>();
            using var listener = BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged, (_, change) =>
            { if (change.Subject == owner && change.Resource == "Energy") changes.Add(change); }, 0);
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True); Assert.That(owner.CurrentEnergy, Is.Zero);
            Assert.That(changes.Select(x => x.RequestedDelta), Is.EqualTo(new[] { -3d }));
            Assert.That(env.Manager.TrySwitchActiveAlly(1), Is.True);
            Next(env); Assert.That(owner.CurrentEnergy, Is.EqualTo(5.75f));
            Next(env); Assert.That(owner.CurrentEnergy, Is.EqualTo(11.5f)); Assert.That(owner.HasStatus(Key(4306403)), Is.False);
            Next(env); Assert.That(owner.CurrentEnergy, Is.EqualTo(11.5f));
            Assert.That(changes.Select(x => x.RequestedDelta), Is.EqualTo(new[] { -3d, 5.75d, 5.75d }));
        }

        [Test, Timeout(9000)]
        public void MountainPin_RespondsToOtherMembersSkillActionWithoutDamage_NotSkillDamageOrOwnAction()
        {
            using var env = new BattleFlowTestEnv(); Main(env, 4312501, "DMGBonus").ApplyDamageType = "Skill";
            Main(env, 4312502); Main(env, 4312503, "DMGBonus").ApplyDamageType = "Skill";
            Effect(env, 431250101, "Buff", "Index(1)"); Effect(env, 431250301, "Buff", "Index(2)");
            Action(env, 4312502, "OnTrigger", Eff(431250201)).ScriptHook = "Hook(4312502)";
            Effect(env, 431250201, "ApplyStatus", Key(4312503)).Duration = 2;
            env.ConfigureStatus("ST_PinMarker");
            env.ConfigureAllyAction(2, "SK_Pin", "SE_Pin", "Physical", null, 0, effectType: "ApplyStatus",
                param1: "ST_PinMarker", duration: 1, targetType: "Self");
            env.ConfigureAllyAction(0, "SK_PinNormal", "SE_PinNormal", "Physical", ".01*TotalATK,0", 0);
            env.DataManager.SkillEffectDict["SE_PinNormal"].DamageType = "Skill";
            env.CreateMinimalBattle(); var owner = env.CreateAlly(2).Entity;
            var context = Weapon(owner, 3125, .12f, .12f); Add(env, owner, 4312501, context); Add(env, owner, 4312502, context); Start(env);
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True); Assert.That(owner.HasStatus(Key(4312503)), Is.False);
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True); var first = owner.GetStatus(Key(4312503));
            Assert.That(first, Is.Not.Null); Assert.That(first.Caster, Is.SameAs(owner)); Assert.That(first.AddInPhase, Is.EqualTo(2));
            using (DamageQueryContext.Enter("SE_PinQuery", "Skill"))
                Assert.That(owner.GetStatusDMGBonus("SE_PinQuery", "", 0, "Physical", env.Enemy.Entity), Is.EqualTo(.24f).Within(.0001f));
            using (DamageQueryContext.Enter("SE_PinQuery", "Normal"))
                Assert.That(owner.GetStatusDMGBonus("SE_PinQuery", "", 2, "Physical", env.Enemy.Entity), Is.Zero);
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True); Assert.That(first.IsActive, Is.False);
            var current = owner.GetStatus(Key(4312503)); Assert.That(owner.GetStatusInstances(Key(4312503)), Has.Count.EqualTo(1));
            Assert.That(env.Manager.TrySwitchActiveAlly(1), Is.True); Assert.That(env.Manager.UseSkillBySlot(1), Is.True);
            Assert.That(owner.GetStatus(Key(4312503)), Is.SameAs(current), "自己的Skill不能刷新队友施放窗口");
            owner.TakeDamage(2000f, DamageSourceInfo.CreateUnknown()); Assert.That(owner.HasStatus(Key(4312502)), Is.False);
            Assert.That(owner.HasStatus(Key(4312503)), Is.False);
        }

        private static void Makhaira(BattleFlowTestEnv env, int weapon = 3068)
        {
            int root = (40000 + weapon) * 100;
            Main(env, root + 1); Main(env, root + 2); Main(env, root + 3, "ATKBonusFlat", true); Main(env, root + 4, "ATKBonusFlat", true);
            Action(env, root + 1, "OnApply", Eff((root + 1) * 100 + 1));
            var init = Effect(env, (root + 1) * 100 + 1, "ApplyStatus", Key(root + 2)); init.AddInPhase = init.TriggerPhase = 1;
            Action(env, root + 2, "OnItsTurn", Eff((root + 2) * 100 + 1)).Cooldown = 3;
            var parent = Effect(env, (root + 2) * 100 + 1, "ApplyStatus", Key(root + 3)); parent.Duration = 3; parent.AddInPhase = 3;
            parent.DamageHook = "Hook(" + parent.StatusEffectID + ")";
            var scalar = Effect(env, (root + 3) * 100 + 1, "Buff", "Index(1)"); scalar.DamageHook = "Hook(" + scalar.StatusEffectID + ")";
            var bind = Effect(env, (root + 3) * 100 + 2, "BindStatus", Key(root + 4)); bind.AddInPhase = 3; bind.TargetType = "AlliesOnly";
            bind.TargetSelect = "-1"; bind.DamageHook = "Hook(" + bind.StatusEffectID + ")";
            var child = Effect(env, (root + 4) * 100 + 1, "Buff", "Index(1)"); child.DamageHook = "Hook(" + child.StatusEffectID + ")";
        }

        [TestCase(3068), TestCase(3144), Timeout(9000)]
        public void OpeningAttackShare_ReadsAllInitialBuffsAfterAttributeCommit(int weapon)
        {
            using var env = new BattleFlowTestEnv(); Makhaira(env, weapon);
            env.DataManager.CharacterAttributesDict[BattleFlowTestEnv.AllyID].Element = "Dendro";
            using var parameters = new OfficeOpenXml.ExcelPackage();
            var sheet = parameters.Workbook.Worksheets.Add("Resonance");
            sheet.Cells[1, 1].Value = "Key"; sheet.Cells[1, 2].Value = "Value";
            sheet.Cells[2, 1].Value = "Enabled"; sheet.Cells[2, 2].Value = 1;
            sheet.Cells[3, 1].Value = "PairCount"; sheet.Cells[3, 2].Value = 2;
            sheet.Cells[4, 1].Value = "ProtectTypes"; sheet.Cells[4, 2].Value = 4;
            sheet.Cells[5, 1].Value = "DendroEM"; sheet.Cells[5, 2].Value = 50f;
            env.DataManager.TeamBonusRules.Resonance = TeamBonusTableLoader.Read(sheet);
            env.CreateMinimalBattle(); var owner = env.Ally.Entity; var recipient = env.CreateAlly(2, false).Entity;
            owner.TotalEM = 100f;
            int root = (40000 + weapon) * 100;
            Add(env, owner, root + 1, Weapon(owner, weapon, .24f));
            // 武器先挂载，圣遗物属性随后挂载；周期不能在前者的OnApply里提前采样。
            var mastery = new StatusMainData { StatusID = 990682, StatusID2 = "ST_OpeningArtifactEM",
                StatusType = "Buff", MultiplierPart1 = "EMBonus", MaxStack = 1, MaxCount = 1 };
            env.DataManager.StatusMainDict[mastery.StatusID2] = mastery;
            Effect(env, 99068201, "Buff", "80");
            owner.AddStatus(mastery.StatusID2, owner, 0, 2, 0, mastery);
            var health = Main(env, 990683, "HPBonus"); Effect(env, 99068301, "Buff", ".2");
            owner.AddStatus(health.StatusID2, owner, 0, 2, 0, health);
            Assert.That(owner.HasStatus(Key(root + 3)), Is.False);
            int snapshots = 0;
            using var created = BattleEventHub.Subscribe<StatusEventData>(BattleEventKind.StatusAdded, (_, fact) =>
            {
                if (fact.StatusId != Key(root + 3)) return;
                snapshots++;
                Assert.That(BattleAttributeInitialization.IsComplete(owner), Is.True);
                Assert.That(BattleAttributeInitialization.IsComplete(recipient), Is.True);
                Assert.That(owner.CurrentHP, Is.EqualTo(1200f).Within(.001f));
            }, 0);
            Start(env);
            Assert.That(snapshots, Is.EqualTo(1));
            Assert.That(owner.GetFinalEM(), Is.EqualTo(230f).Within(.001f));
            Assert.That(owner.GetStatusATKBonusFlat("", "", -1, ""), Is.EqualTo(55.2f).Within(.001f));
            Assert.That(recipient.GetStatusATKBonusFlat("", "", -1, ""), Is.EqualTo(16.56f).Within(.001f));
        }

        [TestCase(3068), TestCase(3144), Timeout(9000)]
        public void AttackShare_SnapshotsPerSource_BindsOldValueToNewMembers_DeduplicatesAndCleansOnlyItsChildren(int weapon)
        {
            using var env = new BattleFlowTestEnv(); Makhaira(env, weapon); env.CreateMinimalBattle(); var owner = env.Ally.Entity;
            int root = (40000 + weapon) * 100;
            var second = env.CreateAlly(2).Entity; second.EntityID = 990012; var recipient = env.CreateAlly(3).Entity; recipient.EntityID = 990013;
            owner.TotalEM = 100f; second.TotalEM = 200f; recipient.TotalEM = 900f;
            var firstWeapon = Weapon(owner, weapon, .24f); firstWeapon.Refinement = 1;
            Add(env, owner, root + 1, firstWeapon); Add(env, second, root + 1, Weapon(second, weapon, .30f)); Start(env);
            Assert.That(owner.GetStatusATKBonusFlat("", "", -1, ""), Is.EqualTo(42f).Within(.001f));
            Assert.That(second.GetStatusATKBonusFlat("", "", -1, ""), Is.EqualTo(67.2f).Within(.001f));
            Assert.That(recipient.GetStatusATKBonusFlat("", "", -1, ""), Is.EqualTo(25.2f).Within(.001f));
            var parent = owner.GetStatus(Key(root + 3)); owner.TotalEM = 1000f;
            StatusBindingSystem.ApplyBindings(parent, owner); StatusBindingSystem.ApplyBindings(parent, owner);
            Assert.That(recipient.GetStatusInstances(Key(root + 4)), Has.Count.EqualTo(2));
            var late = env.CreateAlly(4).Entity; late.EntityID = 990014; StatusBindingSystem.ApplyBindings(parent, owner);
            Assert.That(late.GetStatusATKBonusFlat("", "", -1, ""), Is.EqualTo(7.2f).Within(.001f), "补建沿用父快照，不重读1000精通");
            Next(env); Next(env); Assert.That(owner.GetStatusATKBonusFlat("", "", -1, ""), Is.EqualTo(42f).Within(.001f));
            Next(env); Assert.That(parent.IsActive, Is.False);
            Assert.That(recipient.GetStatusATKBonusFlat("", "", -1, ""), Is.EqualTo(90f).Within(.001f), "第4回合快照A=240、B=60");
            owner.TakeDamage(2000f, DamageSourceInfo.CreateUnknown());
            Assert.That(recipient.GetStatusATKBonusFlat("", "", -1, ""), Is.EqualTo(18f).Within(.001f));
            Assert.That(recipient.GetStatusInstances(Key(root + 4)), Has.Count.EqualTo(1));
        }
    }
}
