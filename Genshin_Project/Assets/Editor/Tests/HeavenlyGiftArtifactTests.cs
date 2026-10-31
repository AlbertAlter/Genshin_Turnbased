using System.Collections.Generic;
using NUnit.Framework;
using OfficeOpenXml;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>562按源表结构构造内存配表：开场仅监听，Skill后才建立五回合窗口。</summary>
    public sealed class HeavenlyGiftArtifactTests
    {
        private static readonly string[] Elements = { "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Geo", "Dendro" };
        private static string Key(int id) => "ST_Artifact_" + id;
        private static string Eff(int id) => "STE_Artifact_" + id;

        [TestCase(false), TestCase(true), Timeout(7000)]
        public void SkillWindow_IsNotGrantedAtOpening_AndSwitchOnlyChangesLiveElement(bool ascended)
        {
            using var env = new BattleFlowTestEnv(); Configure(env, ascended);
            env.CreateMinimalBattle();
            var owner = env.Ally.Entity;
            var hydro = env.CreateAlly(2, false); Identity(env, hydro.Entity, 990021, "Hydro");
            var electro = env.CreateAlly(3, false); Identity(env, electro.Entity, 990022, "Electro");
            Assert.That(env.Ally.InitializeArtifactSetStatuses(new Dictionary<int, int> { [562] = 4 }), Is.True);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            Assert.That(owner.GetFinalRechargeRate(), Is.EqualTo(1.2f).Within(.0001f));
            Assert.That(owner.HasStatus(Key(456203)), Is.False);
            Assert.That(Bonus(owner, "Pyro"), Is.Zero, "初始化监听不能提前赠送战技后的窗口");
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            var parent = owner.GetStatus(Key(456203));
            Assert.That(parent, Is.Not.Null); Assert.That(parent.RemainingPhaseCount, Is.EqualTo(5));
            var children = new List<StatusInstance>(parent.BoundStatuses);
            Assert.That(children, Has.Count.EqualTo(ascended ? 15 : 6),
                "升级档只准备队伍三种元素加两份显示；普通档只有本人元素加显示");
            float amount = ascended ? .4f : .2f;
            Assert.That(Bonus(electro.Entity, "Pyro"), Is.EqualTo(amount).Within(.0001f));
            Assert.That(Bonus(electro.Entity, "Hydro"), Is.Zero);
            Assert.That(env.Manager.TrySwitchActiveAlly(1), Is.True);
            Assert.That(Bonus(electro.Entity, "Pyro"), Is.EqualTo(amount).Within(.0001f));
            Assert.That(Bonus(electro.Entity, "Hydro"), Is.EqualTo(ascended ? .4f : 0f).Within(.0001f));
            if (ascended)
            {
                Assert.That(StatusDescriptionResolver.Resolve(electro.Entity.GetStatus(HeavenlyGiftArtifactHooks.ActiveDisplay)),
                    Is.EqualTo("水元素伤害提高40%（同元素不叠加）"));
                Assert.That(StatusDescriptionResolver.Resolve(electro.Entity.GetStatus(HeavenlyGiftArtifactHooks.OwnerDisplay)),
                    Is.EqualTo("火元素伤害提高40%"), "显示读取装备者，而不是受益者元素");
            }
            Assert.That(env.Manager.TrySwitchActiveAlly(2), Is.True);
            Assert.That(Bonus(owner, "Hydro"), Is.Zero);
            Assert.That(Bonus(owner, "Electro"), Is.EqualTo(ascended ? .4f : 0f).Within(.0001f));
            Assert.That(env.Manager.TrySwitchActiveAlly(0), Is.True);
            Assert.That(Bonus(owner, "Pyro"), Is.EqualTo(amount).Within(.0001f), "相同元素OR去重，不加成80%");
            Assert.That(parent.BoundStatuses, Is.EqualTo(children), "切人只改变读取条件，不重建子实例或刷新计时");
            foreach (var child in children)
            {
                Assert.That(child.RemainingPhaseCount, Is.EqualTo(5));
                Assert.That(child.Caster, Is.SameAs(owner));
                Assert.That(child.EquipmentSource.Owner, Is.SameAs(owner));
            }
            owner.TakeDamage(100000f, DamageSourceInfo.CreateUnknown());
            foreach (var child in children) Assert.That(child.IsActive, Is.False);
            Assert.That(Bonus(hydro.Entity, "Pyro"), Is.Zero, "装备者阵亡撤销全部来源绑定");
            Assert.That(random.FloatCallCount, Is.EqualTo(1)); Assert.That(random.IntCallCount, Is.Zero);
        }

        [TestCase(false), TestCase(true), Timeout(7000)]
        public void RepeatedSkills_ReplaceOnlyOwnWindow_AndDifferentOwnersDoNotStack(bool ascended)
        {
            using var env = new BattleFlowTestEnv(); Configure(env, ascended); env.CreateMinimalBattle();
            var owner = env.Ally.Entity; var second = env.CreateAlly(2, false);
            Identity(env, second.Entity, 990021, "Hydro");
            env.Ally.InitializeArtifactSetStatuses(new Dictionary<int, int> { [562] = 4 });
            second.InitializeArtifactSetStatuses(new Dictionary<int, int> { [562] = 4 });
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            var first = owner.GetStatus(Key(456203)); var oldChildren = new List<StatusInstance>(first.BoundStatuses);
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            var replacement = owner.GetStatus(Key(456203));
            Assert.That(replacement, Is.Not.SameAs(first)); Assert.That(first.IsActive, Is.False);
            foreach (var child in oldChildren) Assert.That(child.IsActive, Is.False);
            Assert.That(env.Manager.TrySwitchActiveAlly(1), Is.True);
            Assert.That(env.Manager.UseSkillBySlot(1), Is.True);
            var other = second.Entity.GetStatus(Key(456203));
            Assert.That(replacement.IsActive, Is.True); Assert.That(other.IsActive, Is.True);
            Assert.That(Bonus(owner, "Hydro"), Is.EqualTo(ascended ? .4f : .2f).Within(.0001f));
            owner.TakeDamage(100000f, DamageSourceInfo.CreateUnknown());
            Assert.That(other.IsActive, Is.True, "清理旧装备者不能清掉另一个来源的具体实例");
            Assert.That(Bonus(second.Entity, "Hydro"), Is.EqualTo(ascended ? .4f : .2f).Within(.0001f));
            Assert.That(Bonus(second.Entity, "Pyro"), Is.Zero);
            Assert.That(random.FloatCallCount, Is.EqualTo(3)); Assert.That(random.IntCallCount, Is.Zero);
        }

        private static float Bonus(BattleEntity entity, string element)
        {
            using (DamageQueryContext.Enter("SE_HeavenlyQuery", "Normal"))
                return entity.GetStatusDMGBonus("SE_HeavenlyQuery", "", -1, element, null);
        }

        private static void Identity(BattleFlowTestEnv env, BattleEntity entity, int id, string element)
        {
            entity.EntityID = id;
            env.DataManager.CharacterAttributesDict[id] = new CharacterAttributesData { CharacterID = id, Element = element };
        }

        private static void Configure(BattleFlowTestEnv env, bool ascended)
        {
            env.ConfigureAllyAction(2, "SK_Heavenly", "SE_Heavenly", "Physical", ".01*TotalATK,0", 0);
            using var parameters = new ExcelPackage(); var sheet = parameters.Workbook.Worksheets.Add("Hexerei");
            sheet.Cells[1, 1].Value = "Key"; sheet.Cells[1, 2].Value = "Value";
            sheet.Cells[2, 1].Value = "Enabled"; sheet.Cells[2, 2].Value = ascended ? 1 : 0;
            sheet.Cells[3, 1].Value = "CharacterIDs"; sheet.Cells[3, 2].Value = BattleFlowTestEnv.AllyID + ";990021";
            sheet.Cells[4, 1].Value = "RequiredCount"; sheet.Cells[4, 2].Value = 2;
            env.DataManager.TeamBonusRules.Hexerei = TeamBonusTableLoader.Read(sheet);
            Main(env, 456201, "RechargeBonus"); Effect(env, 45620101, "Buff", ".2");
            Main(env, 456202); Main(env, 456203);
            env.DataManager.StatusActionDict[Key(456202)] = new List<StatusActionData>
            { new StatusActionData { StatusID = 456202, StatusID2 = Key(456202), ActionType = "OnTrigger",
                OnAction = "Skill", Param1 = Eff(45620201) } };
            var apply = Effect(env, 45620201, "ApplyStatus", Key(456203)); apply.Duration = 5; apply.AddInPhase = 1;
            foreach (int id in new[] { 456204, 456205, 456206 }) Main(env, id, independent: true).Display = 1;
            env.DataManager.StatusMainDict[Key(456204)].Description = "{0}元素伤害提高20%";
            env.DataManager.StatusMainDict[Key(456205)].Description = "{0}元素伤害提高40%";
            env.DataManager.StatusMainDict[Key(456206)].Description = "{0}元素伤害提高40%（同元素不叠加）";
            Bind(env, 45620301, 456204, "HeavenlyGiftMode(Base)");
            Bind(env, 45620302, 456205, "HeavenlyGiftMode(Ascended)");
            Bind(env, 45620303, 456206, "HeavenlyGiftMode(Ascended)");
            for (int i = 0; i < Elements.Length; i++)
                foreach (bool upgraded in new[] { false, true })
                {
                    int id = (upgraded ? 456220 : 456210) + i;
                    var main = Main(env, id, "DMGBonus", true); main.ApplyElementType = Elements[i];
                    main.ScriptHook = "HeavenlyGiftBonus(" + (upgraded ? "Ascended" : "Base") + ")";
                    Effect(env, id*100+1, "Buff", upgraded ? ".4" : ".2");
                    Bind(env, 45620304 + i + (upgraded ? 7 : 0), id,
                        "HeavenlyGiftMode(" + (upgraded ? "Ascended" : "Base") + ");HeavenlyGift"
                        + (upgraded ? "TeamElement" : "OwnElement") + "(" + Elements[i] + ")");
                }
            env.DataManager.ArtifactInitiateStatusDict[562] = new Dictionary<int, List<string>>
            { [2] = new List<string> { Key(456201) }, [4] = new List<string> { Key(456202) } };
        }

        private static StatusMainData Main(BattleFlowTestEnv env, int id, string part = null, bool independent = false)
        {
            var main = new StatusMainData { StatusID = id, StatusID2 = Key(id),
                StatusType = part == null ? null : "Buff", MultiplierPart = part,
                MaxCount = 1, MaxStack = independent ? 0 : 1 };
            env.DataManager.StatusMainDict[main.StatusID2] = main; return main;
        }

        private static StatusEffectData Effect(BattleFlowTestEnv env, int id, string type, string param)
        {
            var effect = new StatusEffectData { StatusEffectID = id, StatusEffectID2 = Eff(id),
                EffectIndex = id % 100, EffectType = type, Param1 = param, TargetType = "Self" };
            env.DataManager.StatusEffectDict[effect.StatusEffectID2] = effect; return effect;
        }

        private static void Bind(BattleFlowTestEnv env, int id, int child, string hook)
        {
            var effect = Effect(env, id, "BindStatus", Key(child)); effect.ScriptHook = hook;
            effect.TargetType = "Allies"; effect.TargetSelect = "-1"; effect.AddInPhase = 1;
        }
    }
}
