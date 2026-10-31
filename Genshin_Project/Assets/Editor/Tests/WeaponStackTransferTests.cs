using System.Collections.Generic;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>独立标记转层与小数回能；只写用例，不自动运行Unity。</summary>
    public sealed class WeaponStackTransferTests
    {
        private static string Key(int id) => DataManager.WeaponStatusKey(id);
        private static string EffectKey(int id) => "STE_Weapon_" + id;
        private static StatusMainData Main(BattleFlowTestEnv env, int id, int max = 1, string part = null)
        {
            var main = new StatusMainData { StatusID = id, StatusID2 = Key(id), StatusType = "Buff",
                MaxStack = max, WhenMax = "0", MultiplierPart = part };
            env.DataManager.StatusMainDict[Key(id)] = main;
            return main;
        }
        private static StatusEffectData Effect(BattleFlowTestEnv env, int id, string type, string param)
        {
            var effect = new StatusEffectData { StatusEffectID = id, StatusEffectID2 = EffectKey(id),
                EffectType = type, Param1 = param, TargetType = "Self" };
            env.DataManager.StatusEffectDict[effect.StatusEffectID2] = effect;
            return effect;
        }
        private static StatusActionData Action(BattleFlowTestEnv env, int id, string effects, string action = "OnTrigger")
        {
            var row = new StatusActionData { StatusID = id, StatusID2 = Key(id), ActionType = action, Param1 = effects };
            if (!env.DataManager.StatusActionDict.TryGetValue(Key(id), out var rows))
                env.DataManager.StatusActionDict[Key(id)] = rows = new List<StatusActionData>();
            rows.Add(row); return row;
        }
        private static WeaponRuntimeContext Weapon(BattleEntity owner, int weapon, float first, float second)
        {
            var result = new WeaponRuntimeContext { WeaponID = weapon, Equipper = owner,
                Refinement = 1, SourceInstanceID = "Transfer:" + weapon + ":" + owner.EntityID };
            result.ResolvedParams[1] = first; result.ResolvedParams[2] = second; return result;
        }
        private static StatusInstance Add(BattleFlowTestEnv env, BattleEntity target, int id, WeaponRuntimeContext weapon)
            => target.AddStatus(Key(id), weapon.Equipper, 0, 2, 0, env.DataManager.StatusMainDict[Key(id)], weapon);
        private static void Start(BattleFlowTestEnv env)
        {
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            BattleRandom.SetSource(new SequenceBattleRandomSource());
        }
        private static void TransferDefinitions(BattleFlowTestEnv env, int weapon)
        {
            int root = (40000 + weapon) * 100, listener = root + (weapon == 3037 ? 1 : 3);
            Main(env, root + 1); Main(env, root + 2, 3); Main(env, listener);
            int reward = root + (weapon == 3037 ? 3 : 4);
            Main(env, reward, weapon == 3037 ? 0 : 3, weapon == 3037 ? null : "EMBonus");
            int effectRoot = listener * 100;
            for (int i = 1; i <= 3; i++)
            {
                int id = effectRoot + i + (weapon == 3037 ? 1 : 0);
                var effect = Effect(env, id, "ApplyStatus", Key(reward));
                effect.Duration = weapon == 3037 ? 1 : 3; effect.AddInPhase = weapon == 3037 ? 3 : 2;
                effect.TriggerPhase = weapon == 3037 ? 3 : 0;
                if (i > 1) effect.ScriptHook = "Hook(" + id + ")";
            }
            int removeID = effectRoot + 5;
            Effect(env, removeID, "RemoveStatus", Key(root + 2));
            if (weapon != 3037)
            {
                var gain = Effect(env, effectRoot + 4, "GainEnergy", "Index(2)");
                gain.Param2 = "Flat"; gain.DamageHook = "Hook(" + gain.StatusEffectID + ")";
                Effect(env, reward * 100 + 1, "Buff", "Index(1)");
                var healMark = Effect(env, (root + 1) * 100 + 1, "ApplyStatus", Key(root + 2));
                healMark.Duration = 8; healMark.AddInPhase = 2;
                var heal = Action(env, root + 1, healMark.StatusEffectID2); heal.OnHealFrom = heal.WhenHealing = "All";
            }
            else
            {
                var seed = Effect(env, effectRoot + 1, "ApplyStatus", Key(root + 2)); seed.Duration = 8; seed.AddInPhase = 2;
                var skill = Action(env, listener, seed.StatusEffectID2); skill.OnAction = "Skill";
                skill.SetTriggerLimits("Turn", "1");
                var gain = Effect(env, reward * 100 + 1, "GainEnergy", "Index(1)"); gain.Param2 = "Flat";
                gain.DamageHook = "Hook(" + gain.StatusEffectID + ")";
                var payout = Action(env, reward, gain.StatusEffectID2, "OnItsTurn");
                payout.ScriptHook = "Hook(4303703)"; payout.SetTriggerLimits("Life", "1");
            }
            var ids = new List<string>();
            for (int i = weapon == 3037 ? 2 : 1; i <= 5; i++) ids.Add(EffectKey(effectRoot + i));
            var transfer = Action(env, listener, string.Join(";", ids));
            transfer.OnAction = weapon == 3037 ? "Burst" : "Skill;Burst";
            transfer.ScriptHook = "Hook(" + listener + ")"; transfer.Cooldown = weapon == 3037 ? 0 : 4;
        }

        [Test, Timeout(7000)]
        public void BlackSword_UsesExistingPerHitCritical_OnlyNormalHeavy_OncePerTurn()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(0, "SK_BlackNormal", "SE_BlackNormal", "Physical", ".01*TotalATK,0", 0);
            env.ConfigureAllyAction(1, "SK_BlackHeavy", "SE_BlackHeavy", "Physical", ".01*TotalATK,0", 0);
            env.ConfigureAllyAction(2, "SK_BlackSkill", "SE_BlackSkill", "Physical", ".01*TotalATK,0", 0);
            Main(env, 4303302); Effect(env, 430330201, "Heal", "Index(2)*TotalATK");
            var action = Action(env, 4303302, EffectKey(430330201)); action.OutgoingHit = "Normal;Heavy";
            action.ScriptHook = "Hook(4303302)"; action.SetTriggerLimits("Turn", "1");
            env.CreateMinimalBattle(); var owner = env.Ally.Entity; owner.CurrentHP = 500f; owner.TotalATK = 100f;
            owner.CritRate = .5f; Add(env, owner, 4303302, Weapon(owner, 3033, .2f, .6f)); Start(env);
            var random = new SequenceBattleRandomSource(new[] { .1f, .9f, .1f, .1f }); BattleRandom.SetSource(random);
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            Assert.That(owner.CurrentHP, Is.EqualTo(500f), "技能暴击不治疗");
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            Assert.That(owner.CurrentHP, Is.EqualTo(500f), "非暴击不占本回合治疗次数");
            Assert.That(env.Manager.UseHeavyAttackBySlot(0), Is.True);
            Assert.That(owner.CurrentHP, Is.EqualTo(560f).Within(.001f));
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            Assert.That(owner.CurrentHP, Is.EqualTo(560f).Within(.001f));
            Assert.That(random.FloatCallCount, Is.EqualTo(4), "治疗条件不得重抽暴击");
            Assert.That(random.IntCallCount, Is.Zero);
        }

        [TestCase(3046, 1), TestCase(3046, 2), TestCase(3046, 3)]
        [TestCase(3072, 1), TestCase(3072, 2), TestCase(3072, 3), Timeout(7000)]
        public void HealingMarks_TransferExactInstances_OneFractionalFlatGain_ThenShareCooldown(int weapon, int count)
        {
            using var env = new BattleFlowTestEnv(); TransferDefinitions(env, weapon);
            env.ConfigureAllyAction(2, "SK_Transfer", "SE_Transfer", "Physical", ".01*TotalATK,0", 0);
            env.ConfigureAllyAction(3, "SK_TransferBurst", "SE_TransferBurst", "Physical", ".01*TotalATK,0", 0);
            env.CreateMinimalBattle(); var owner = env.Ally.Entity; owner.MaxEnergy = 60; owner.CurrentEnergy = 1.25f;
            owner.TotalRechargeRate = 3f; int root = (40000 + weapon) * 100;
            var context = Weapon(owner, weapon, 50f, 2.5f); context.Refinement = 2;
            Add(env, owner, root + 1, context); Add(env, owner, root + 3, context); Start(env);
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True, "零标记的技能不得占奋起冷却");
            for (int i = 0; i < count; i++) owner.Heal(1f, owner); // 满血有效治疗事实，ActualAmount=0；自疗两个条件只匹配一次。
            var consumed = owner.GetStatusInstances(Key(root + 2)); Assert.That(consumed, Has.Count.EqualTo(count));
            int gains = 0;
            using var energy = BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged, (_, fact) =>
            { if (fact.Resource == "Energy" && fact.Subject == owner) { gains++; Assert.That(fact.RequestedDelta, Is.EqualTo(count * 2.5)); } }, 0);
            bool addedDuringResponse = false;
            using var response = BattleEventHub.Subscribe<StatusEventData>(BattleEventKind.StatusAdded, (_, fact) =>
            {
                if (fact.StatusId != Key(root + 4) || addedDuringResponse) return;
                addedDuringResponse = true;
                foreach (var mark in consumed) Assert.That(mark.IsActive, Is.False, "中途响应应在转层整链提交后执行");
                Assert.That(owner.CurrentEnergy, Is.EqualTo(1.25f + count * 2.5f));
                owner.Heal(1f, owner); // 子链响应新获得的标记必须留到下一次，不被旧消耗清除。
            }, 0);
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            Assert.That(owner.GetStatusInstances(Key(root + 4)), Has.Count.EqualTo(count));
            Assert.That(owner.GetFinalEM(), Is.EqualTo(owner.TotalEM + count * 50f));
            Assert.That(owner.GetStatusInstances(Key(root + 2)), Has.Count.EqualTo(1));
            Assert.That(gains, Is.EqualTo(1));
            Assert.That(env.Manager.UseBurstBySlot(0), Is.True);
            Assert.That(owner.GetStatusInstances(Key(root + 2)), Has.Count.EqualTo(1), "战技和爆发共享4回合冷却");
            Assert.That(gains, Is.EqualTo(1));
        }

        [Test, Timeout(7000)]
        public void Amenoma_MultipleBurstsAccumulatePending_OneSumAtPhaseThree_NotSquaredOrImmediate()
        {
            using var env = new BattleFlowTestEnv(); TransferDefinitions(env, 3037);
            env.ConfigureAllyAction(2, "SK_Seed", "SE_Seed", "Physical", ".01*TotalATK,0", 0);
            env.ConfigureAllyAction(3, "SK_SeedBurst", "SE_SeedBurst", "Physical", ".01*TotalATK,0", 0);
            env.DataManager.SkillEffectDict["SE_Seed"].DamageType = "Burst";
            env.DataManager.SkillEffectDict["SE_SeedBurst"].DamageType = "Normal";
            env.CreateMinimalBattle(); var owner = env.Ally.Entity; owner.MaxEnergy = 60; owner.CurrentEnergy = 1.25f;
            var context = Weapon(owner, 3037, 7.5f, 0f); context.Refinement = 2;
            Add(env, owner, 4303701, context); Start(env);
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True); Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            Assert.That(owner.GetStatusInstances(Key(4303702)), Has.Count.EqualTo(1), "看主动Skill，而非实际伤害标签，每回合一枚");
            Add(env, owner, 4303702, context); Add(env, owner, 4303702, context);
            Assert.That(env.Manager.UseBurstBySlot(0), Is.True);
            Assert.That(owner.HasStatus(Key(4303702)), Is.False);
            Assert.That(owner.GetStatusInstances(Key(4303703)), Has.Count.EqualTo(3));
            for (int i = 0; i < 3; i++) Add(env, owner, 4303702, context);
            Assert.That(env.Manager.UseBurstBySlot(0), Is.True);
            var pending = owner.GetStatusInstances(Key(4303703)); Assert.That(pending, Has.Count.EqualTo(6));
            Assert.That(owner.CurrentEnergy, Is.EqualTo(1.25f), "爆发只转存，不立即回能");
            int gains = 0;
            using var energy = BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged, (_, fact) =>
            { if (fact.Resource == "Energy" && fact.Subject == owner) { gains++; Assert.That(fact.RequestedDelta, Is.EqualTo(45d)); } }, 0);
            env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);
            Assert.That(owner.CurrentEnergy, Is.EqualTo(46.25f)); Assert.That(gains, Is.EqualTo(1));
            Assert.That(owner.HasStatus(Key(4303703)), Is.False);
            foreach (var instance in pending) Assert.That(instance.IsActive, Is.False, "统一持续时间机制自然移除");
        }
    }
}
