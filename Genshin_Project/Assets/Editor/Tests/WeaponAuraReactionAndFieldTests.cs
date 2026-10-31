using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>3060/3085/3070/3074/3086真实声明与状态入口；不自动运行Unity。</summary>
    public sealed class WeaponAuraReactionAndFieldTests
    {
        private static string Key(int id) => DataManager.WeaponStatusKey(id);
        private static string Eff(int id) => "STE_Weapon_" + id;
        private static StatusMainData Main(BattleFlowTestEnv env, int id, string part = null, int stack = 1, string hook = null)
        {
            var data = new StatusMainData { StatusID = id, StatusID2 = Key(id), StatusType = "Buff",
                MultiplierPart = part, MaxStack = stack, MaxCount = stack, WhenMax = "0", ScriptHook = hook };
            env.DataManager.StatusMainDict[data.StatusID2] = data; return data;
        }
        private static StatusEffectData Effect(BattleFlowTestEnv env, int id, string type, string parameter)
        {
            var data = new StatusEffectData { StatusEffectID = id, StatusEffectID2 = Eff(id), EffectType = type,
                Param1 = parameter, TargetType = "Self" };
            env.DataManager.StatusEffectDict[data.StatusEffectID2] = data; return data;
        }
        private static StatusActionData Action(BattleFlowTestEnv env, int id, string effects, string hook)
        {
            var data = new StatusActionData { StatusID = id, StatusID2 = Key(id), ActionType = "OnTrigger", Param1 = effects, ScriptHook = hook };
            if (!env.DataManager.StatusActionDict.TryGetValue(Key(id), out var actions))
                env.DataManager.StatusActionDict[Key(id)] = actions = new List<StatusActionData>();
            actions.Add(data); return data;
        }
        private static WeaponRuntimeContext Weapon(BattleEntity owner, int weapon, params float[] values)
        {
            var data = new WeaponRuntimeContext { Equipper = owner, WeaponID = weapon, Refinement = 1,
                SourceInstanceID = "AuraReactionField:" + weapon + ":" + owner.EntityID };
            for (int i = 0; i < values.Length; i++) data.ResolvedParams[i + 1] = values[i];
            return data;
        }
        private static StatusInstance Add(BattleFlowTestEnv env, BattleEntity owner, int id, WeaponRuntimeContext weapon)
            => owner.AddStatus(Key(id), owner, 0, 2, 0, env.DataManager.StatusMainDict[Key(id)], weapon);
        private static void Start(BattleFlowTestEnv env)
        { BattleRandom.SetSource(new SequenceBattleRandomSource()); env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction); }
        private static void Next(BattleFlowTestEnv env)
        { env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction); }
        private static void Aim(CharacterBattleController controller, params int[] positions)
        { controller.ForcedTargetPositions.Clear(); controller.ForcedTargetPositions.AddRange(positions); }

        [TestCase(3060), TestCase(3085), Timeout(9000)]
        public void Blackcliff_KillsPerTarget_NotBySharedCharacterId_AndReplacesOnlyOldestIndependentBuff(int weapon)
        {
            using var env = new BattleFlowTestEnv(); int root = (40000 + weapon) * 100;
            Main(env, root + 1); Main(env, root + 2, "ATKBonus", 3);
            Action(env, root + 1, Eff((root + 1) * 100 + 1), "Hook(" + (root + 1) + ")").OutgoingDamage = "All";
            var apply = Effect(env, (root + 1) * 100 + 1, "ApplyStatus", Key(root + 2)); apply.Duration = 8; apply.AddInPhase = 2;
            Effect(env, (root + 2) * 100 + 1, "Buff", "Index(1)");
            env.ConfigureAllyAction(2, "SK_Blackcliff", "SE_Blackcliff", "Physical", "1*TotalATK,0", 0);
            env.CreateMinimalBattle(); env.Enemy.Entity.CurrentHP = 30f;
            env.CreateEnemy(2, 30f); env.CreateEnemy(3, 30f); env.CreateEnemy(4, 30f); env.CreateEnemy(5, 10000f);
            var owner = env.Ally.Entity; var sameIDAlly = env.CreateAlly(2); sameIDAlly.Entity.EntityID = owner.EntityID;
            var weaponContext = Weapon(owner, weapon, .12f); Add(env, owner, root + 1, weaponContext); Start(env);
            Aim(env.Ally, 1, 2); Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            var first = owner.GetStatusInstances(Key(root + 2)); Assert.That(first, Has.Count.EqualTo(2));
            Assert.That(owner.GetStatusATKBonus("", "", -1, ""), Is.EqualTo(.24f).Within(.0001f));
            Assert.That(env.Manager.TrySwitchActiveAlly(1), Is.True); Aim(sameIDAlly, 3);
            Assert.That(env.Manager.UseSkillBySlot(1), Is.True); Assert.That(owner.GetStatusInstances(Key(root + 2)), Has.Count.EqualTo(2));
            Assert.That(env.Manager.TrySwitchActiveAlly(0), Is.True); Next(env);
            Aim(env.Ally, 4); Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            var third = owner.GetStatusInstances(Key(root + 2)).Single(x => !first.Contains(x));
            Next(env); env.CreateEnemy(1, 30f); Aim(env.Ally, 1); Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            var current = owner.GetStatusInstances(Key(root + 2)); Assert.That(current, Has.Count.EqualTo(3));
            Assert.That(first[0].IsActive, Is.False); Assert.That(current, Does.Contain(first[1])); Assert.That(current, Does.Contain(third));
            Assert.That(first[1].RemainingPhaseCount, Is.LessThan(third.RemainingPhaseCount));
            Assert.That(owner.GetStatusATKBonus("", "", -1, ""), Is.EqualTo(.36f).Within(.0001f));
            owner.TakeDamage(2000f, DamageSourceInfo.CreateUnknown()); Assert.That(owner.GetStatusInstances(Key(root + 2)), Is.Empty);
        }

        private static void Talking(BattleFlowTestEnv env)
        {
            Main(env, 4307001); Main(env, 4307002, "ATKBonus"); Main(env, 4307003);
            Action(env, 4307001, Eff(430700101), "Hook(430700101)").Cooldown = 3;
            Action(env, 4307001, Eff(430700102), "Hook(430700102)").Cooldown = 3;
            for (int i = 1; i <= 2; i++)
            { var apply = Effect(env, 430700100 + i, "ApplyStatus", Key(4307001 + i)); apply.Duration = 4; apply.AddInPhase = 2; }
            Effect(env, 430700201, "Buff", "Index(1)");
            string[] elements = { "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Dendro", "Geo" };
            for (int i = 0; i < elements.Length; i++)
            {
                int id = 4307004 + i; Main(env, id, "DMGBonus").ApplyElementType = elements[i];
                Effect(env, 430700301 + i, "BindStatus", Key(id)); Effect(env, id * 100 + 1, "Buff", "Index(2)");
            }
        }

        [TestCase("Pyro", "Hydro", .5f), TestCase("Hydro", "Pyro", 2f), Timeout(9000)]
        public void TalkingStick_FullyConsumedPositiveContactStillTriggers_AndTwoGroupsStayIndependent(string incoming, string oldAura, float oldAmount)
        {
            using var env = new BattleFlowTestEnv(); Talking(env); env.CreateMinimalBattle();
            var owner = env.CreateAlly(2).Entity; Add(env, owner, 4307001, Weapon(owner, 3070, .16f, .12f)); Start(env);
            var listener = owner.GetStatus(Key(4307001));
            owner.ApplyAura(oldAura, oldAmount, owner.EntityID); // 无伤害直接附着，先开启另一组。
            ReactionResolver.Resolve(new ReactionContext { SourceEntity = env.Enemy.Entity, Target = owner,
                SourceKind = ReactionSourceKind.EnemySkill, SourceEffectID = "SE_TalkingContact", AttackElement = incoming,
                AttackAmount = 1f, PreReactionDamage = 0f, EffectExecutionID = ReactionResolver.BeginEffectExecution() });
            Assert.That(owner.GetAura(incoming), Is.Null, "该次正元素量被反应全部消耗，不留附着");
            Assert.That(owner.HasStatus(Key(4307002)), Is.True); Assert.That(owner.HasStatus(Key(4307003)), Is.True);
            Assert.That(owner.CharacterCtrl.IsActive, Is.False); Assert.That(listener.LifeTriggerCount, Is.EqualTo(2));
            Assert.That(owner.GetStatusATKBonus("", "", -1, ""), Is.EqualTo(.16f).Within(.0001f));
            foreach (string element in new[] { "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Dendro", "Geo" })
                Assert.That(owner.GetStatusDMGBonus("", "", -1, element, env.Enemy.Entity), Is.EqualTo(.12f).Within(.0001f));
            Assert.That(owner.GetStatusDMGBonus("", "", -1, "Physical", env.Enemy.Entity), Is.Zero);
            Assert.That(owner.GetStatusDMGBonus("", "", -1, "Physical", env.Enemy.Entity), Is.Zero);
            var oldParent = owner.GetStatus(Key(4307003)); var oldChildren = new List<StatusInstance>(oldParent.BoundStatuses);
            owner.ApplyAura("Cryo", 1f, owner.EntityID); owner.ApplyAura("Electro", 1f, owner.EntityID);
            Assert.That(listener.LifeTriggerCount, Is.EqualTo(2), "四元素共用一个冷却；已有池和查询不重复计次");
            for (int i = 0; i < 3; i++) Next(env);
            owner.ApplyAura("Dendro", 1f, owner.EntityID);
            Assert.That(listener.LifeTriggerCount, Is.EqualTo(3)); Assert.That(oldParent.IsActive, Is.False);
            Assert.That(oldChildren.TrueForAll(x => !x.IsActive), Is.True);
            Assert.That(owner.GetStatus(Key(4307003)).BoundStatuses, Has.Count.EqualTo(7));
            owner.TakeDamage(2000f, DamageSourceInfo.CreateUnknown());
            Assert.That(owner.HasStatus(Key(4307001)), Is.False); Assert.That(owner.HasStatus(Key(4307003)), Is.False);
        }

        [Test, Timeout(7000)]
        public void TalkingStick_ContactAndItsResidualAuraExecuteOnce_ZeroAndOtherTargetsDoNotConsumeCooldown()
        {
            using var env = new BattleFlowTestEnv(); Talking(env); env.CreateMinimalBattle();
            var owner = env.Ally.Entity; Add(env, owner, 4307001, Weapon(owner, 3070, .16f, .12f)); Start(env);
            var status = owner.GetStatus(Key(4307001)); env.Enemy.Entity.ApplyAura("Pyro", 1f, owner.EntityID);
            ReactionResolver.Resolve(new ReactionContext { SourceEntity = env.Enemy.Entity, Target = owner,
                AttackElement = "Pyro", AttackAmount = 0f, SourceKind = ReactionSourceKind.EnemySkill });
            Assert.That(status.LifeTriggerCount, Is.Zero);
            ReactionResolver.Resolve(new ReactionContext { SourceEntity = env.Enemy.Entity, Target = owner,
                AttackElement = "Pyro", AttackAmount = 1f, SourceKind = ReactionSourceKind.EnemySkill,
                SourceEffectID = "SE_TalkingResidual", EffectExecutionID = ReactionResolver.BeginEffectExecution() });
            Assert.That(status.LifeTriggerCount, Is.EqualTo(1), "ElementContact和残留AuraChanged不可执行两次");
            Assert.That(owner.GetStatusInstances(Key(4307002)), Has.Count.EqualTo(1));
            for (int i = 0; i < 5; i++) owner.GetStatusATKBonus("", "", -1, "");
            Assert.That(status.LifeTriggerCount, Is.EqualTo(1));
        }

        [Test, Timeout(9000)]
        public void EarthShaker_UsesActualFriendlyReactionElements_NotDamageElementOrEnemyReaction()
        {
            using var env = new BattleFlowTestEnv(); Main(env, 4307401); Main(env, 4307402, "DMGBonus").ApplyDamageType = "Skill";
            Action(env, 4307401, Eff(430740101), "Hook(4307401)");
            var apply = Effect(env, 430740101, "ApplyStatus", Key(4307402)); apply.Duration = 2; apply.AddInPhase = 2;
            Effect(env, 430740201, "Buff", "Index(1)");
            env.ConfigureAllyAction(0, "SK_EarthNormal", "SE_EarthNormal", "Hydro", ".01*TotalATK,0", 0);
            env.ConfigureAllyAction(2, "SK_EarthSkill", "SE_EarthSkill", "Cryo", ".01*TotalATK,1", 0);
            env.CreateMinimalBattle(); var owner = env.CreateAlly(2).Entity;
            Add(env, owner, 4307401, Weapon(owner, 3074, .16f)); Start(env);
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True); Assert.That(owner.HasStatus(Key(4307402)), Is.False);
            env.Enemy.Entity.ApplyAura("Pyro", 1f, env.Ally.Entity.EntityID);
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True); var first = owner.GetStatus(Key(4307402)); Assert.That(first, Is.Not.Null);
            Assert.That(first.Caster, Is.SameAs(owner)); Assert.That(owner.CharacterCtrl.IsActive, Is.False);
            Assert.That(owner.GetStatusDMGBonus("SE_EarthSkill", "SK_EarthSkill", 2, "Cryo", env.Enemy.Entity), Is.EqualTo(.16f).Within(.0001f));
            Assert.That(owner.GetStatusDMGBonus("SE_EarthNormal", "SK_EarthNormal", 0, "Hydro", env.Enemy.Entity), Is.Zero);
            env.Ally.Entity.ApplyAura("Hydro", 1f, owner.EntityID);
            var enemyReaction = ReactionResolver.Resolve(new ReactionContext { SourceEntity = env.Enemy.Entity,
                Target = env.Ally.Entity, SourceKind = ReactionSourceKind.EnemySkill, AttackElement = "Pyro", AttackAmount = 1f,
                SourceEffectID = "SE_EnemyEarthReaction", EffectExecutionID = ReactionResolver.BeginEffectExecution() });
            StatusOnHitHookSystem.NotifyReactions(enemyReaction.TriggeredReactions);
            Assert.That(owner.GetStatus(Key(4307402)), Is.SameAs(first), "敌方反应不刷新本队武器");
            env.Enemy.Entity.ApplyAura("Pyro", 1f, env.Ally.Entity.EntityID); Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            Assert.That(first.IsActive, Is.False); Assert.That(owner.GetStatusInstances(Key(4307402)), Has.Count.EqualTo(1));
        }

        private static void Hunt(BattleFlowTestEnv env)
        {
            Main(env, 4308601); Main(env, 4308602, "DMGBonus").StatusType = "Debuff";
            var listen = Action(env, 4308601, Eff(430860101) + ";" + Eff(430860102), "PostSelfDamage(4308601);Chance(0.5)");
            listen.OutgoingHit = "Normal;Heavy"; listen.MaxTimePerEffect = 1; listen.Cooldown = 3;
            Action(env, 4308602, Eff(430860202), "Hook(4308602)");
            var center = Effect(env, 430860101, "ApplyStatus", Key(4308602)); center.Duration = 1; center.AddInPhase = 2;
            center.TargetType = "EnemyField"; center.TargetSelect = "1"; center.TargetConsecutive = 4; center.TargetOverride = "PostSelfDamage(4308601)";
            var adjacent = Effect(env, 430860102, "ApplyStatus", Key(4308602)); adjacent.Duration = 1; adjacent.AddInPhase = 2;
            adjacent.TargetType = "EnemyField"; adjacent.TargetOverride = "1,0,1";
            Effect(env, 430860201, "Debuff", "-0.1");
            var damage = Effect(env, 430860202, "Damage", "Index(1)*TotalATK,0"); damage.Element = "Physical";
            damage.TargetType = "Enemy"; damage.TargetSelect = "0,0";
            env.ConfigureAllyAction(0, "SK_Hunt", "SE_Hunt", "Physical", ".01*TotalATK,0", 0);
            env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_0"][1].Hits2 = ".01*TotalATK,0";
            env.ConfigureEnemyDamageSkill(); env.DataManager.EnemySkillMainDict[990201].UsePerTurn = 0;
        }
        private static void EnemyAction(EnemyBattleController enemy)
            => BattleExecution.Run(enemy.TakeEnemyActionAsync).GetAwaiter().GetResult();

        [Test, Timeout(9000)]
        public void Viridescent_OneRandomCenterAndEmptyAdjacentField_ActsBeforeEachActualEnemyAction_WithOriginalSource()
        {
            using var env = new BattleFlowTestEnv(); Hunt(env); env.CreateMinimalBattle(); env.CreateEnemy(2);
            var owner = env.Ally.Entity; env.CreateAlly(2, hp: 900f); var context = Weapon(owner, 3086, 3f);
            Add(env, owner, 4308601, context); Start(env); Aim(env.Ally, 2);
            var random = new SequenceBattleRandomSource(new[] { .9f, .9f, .2f, .9f, .9f }); BattleRandom.SetSource(random);
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            for (int i = 1; i <= 3; i++) Assert.That(env.Manager.Field.GetSlot(BattleSide.Enemy, i).StatusList.Single().StatusID2, Is.EqualTo(Key(4308602)));
            Assert.That(env.Manager.Field.GetSlot(BattleSide.Enemy, 3).Occupant, Is.Null);
            var trace = new List<string>(); var fieldHits = new List<DamageEventData>();
            using var starts = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted, (_, x) =>
            { if (x.Subject == env.Enemy.Entity) trace.Add("start"); }, 0);
            using var hits = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, x) =>
            {
                if (x.Source.SourceEffectID == Eff(430860202)) { fieldHits.Add(x); trace.Add("field"); }
                if (x.Source.SourceEffectID == "SE_Enemy_BattleFlow") trace.Add("enemy");
            }, 0);
            env.Enemy.BeginEnemyTurn(); EnemyAction(env.Enemy); EnemyAction(env.Enemy);
            Assert.That(trace, Is.EqualTo(new[] { "start", "field", "enemy", "start", "field", "enemy" }));
            Assert.That(fieldHits.Select(x => x.DamageBeforeShield), Is.EqualTo(new[] { 150f, 150f }));
            Assert.That(fieldHits.Select(x => x.Source.SourceEntity), Is.All.SameAs(owner));
            Assert.That(fieldHits.Select(x => x.Source.SourceStatus.WeaponContext.SourceInstanceID), Is.All.EqualTo(context.SourceInstanceID));
            Assert.That(random.FloatCallCount, Is.EqualTo(5), "两次攻击暴击、一次概率、两次场地伤害暴击");
            Assert.That(random.IntCallCount, Is.EqualTo(2), "只有两次敌方AI选择；单目标中心不抽第二次随机");
            env.Enemy.Entity.TakeDamage(2000f, DamageSourceInfo.CreateUnknown()); var replacement = env.CreateEnemy(1);
            EnemyAction(replacement); Assert.That(fieldHits.Last().Subject, Is.SameAs(replacement.Entity));
            Assert.That(fieldHits.Last().Source.SourceEntity, Is.SameAs(owner));
            owner.TakeDamage(2000f, DamageSourceInfo.CreateUnknown());
            for (int i = 1; i <= 3; i++) Assert.That(env.Manager.Field.GetSlot(BattleSide.Enemy, i).StatusList, Is.Empty);
        }

        [Test, Timeout(9000)]
        public void Viridescent_PreActionKillStopsOriginalSkillAndDoesNotWriteCooldownOrCompletion()
        {
            using var env = new BattleFlowTestEnv(); Hunt(env); env.DataManager.EnemySkillMainDict[990201].Cooldown = 2;
            env.CreateMinimalBattle(); env.CreateEnemy(2); Start(env); env.Enemy.Entity.CurrentHP = 100f;
            var field = env.Manager.Field.GetSlot(BattleSide.Enemy, 1);
            field.AddStatus(Key(4308602), env.Ally.Entity, 1, 2, 0, env.DataManager.StatusMainDict[Key(4308602)], Weapon(env.Ally.Entity, 3086, 3f));
            var damages = new List<DamageEventData>(); int completions = 0;
            using var hits = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, x) => damages.Add(x), 0);
            using var done = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted, (_, x) =>
            { if (x.Subject == env.Enemy.Entity) completions++; }, 0);
            BattleRandom.SetSource(new SequenceBattleRandomSource()); EnemyAction(env.Enemy);
            Assert.That(env.Enemy.Entity.IsDead, Is.True); Assert.That(completions, Is.Zero);
            Assert.That(damages.Select(x => x.Source.SourceEffectID), Is.EqualTo(new[] { Eff(430860202) }));
            var cooldowns = (Dictionary<int, int>)typeof(EnemyBattleController).GetField("_skillCooldownLeft", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(env.Enemy);
            Assert.That(cooldowns, Is.Empty); Assert.That(env.Ally.Entity.CurrentHP, Is.EqualTo(1000f));
        }

        [Test, Timeout(7000)]
        public void Viridescent_FailedProbabilityIsNotRetriedForExecuteCountRepeats()
        {
            using var env = new BattleFlowTestEnv(); Hunt(env); env.DataManager.SkillEffectDict["SE_Hunt"].ExecuteCount = 2;
            env.CreateMinimalBattle(); Add(env, env.Ally.Entity, 4308601, Weapon(env.Ally.Entity, 3086, 3f)); Start(env);
            var random = new SequenceBattleRandomSource(new[] { .9f, .9f, .8f, .9f, .9f }); BattleRandom.SetSource(random);
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            Assert.That(random.FloatCallCount, Is.EqualTo(5));
            Assert.That(env.Manager.Field.EnemySlots.SelectMany(x => x.StatusList), Is.Empty);
        }
    }
}
