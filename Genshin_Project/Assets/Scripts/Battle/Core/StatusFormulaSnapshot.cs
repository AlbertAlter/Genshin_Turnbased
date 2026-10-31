using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;

/// <summary>
/// Status_Attributes仅是启用标记。记忆保存来源数值和公式参数，不注册Buff副本、行动或资源。
/// 目标条件延后求值；Count表达式只在Capture读取，命中预备贡献继续由CountDamageBatch负责。
/// </summary>
public sealed class StatusFormulaSnapshot
{
    private sealed class Term
    {
        internal float Value;
        internal string DeferredHook;
        internal StatusEffectData HitParameter;
    }
    private sealed class Group
    {
        internal StatusInstance ConditionView, Original;
        internal string ID, Part, Reaction, DeferredHook;
        internal int Cap, Count;
        internal bool BakedHP;
        internal readonly List<Term> Terms = new List<Term>();
    }
    private sealed class Plan
    {
        internal string Effect, Element, DamageType;
        internal readonly List<Group> Groups = new List<Group>();
        internal readonly Dictionary<string, float> Fixed = new Dictionary<string, float>();
        internal readonly Dictionary<string, float> FixedUnbakedHP = new Dictionary<string, float>();
        internal readonly Dictionary<string, float> Attributes = new Dictionary<string, float>();
    }
    private sealed class Frame : IDisposable
    {
        internal readonly Frame Previous = Current.Value;
        internal readonly StatusFormulaSnapshot Memory;
        internal readonly string Effect;
        internal readonly BattleEntity Target;
        private bool disposed;
        internal Frame(StatusFormulaSnapshot memory, string effect, BattleEntity target = null)
        { Memory = memory; Effect = effect; Target = target; Current.Value = this; }
        public void Dispose() { if (disposed) return; disposed = true; Current.Value = Previous; }
    }
    private static readonly AsyncLocal<Frame> Current = new AsyncLocal<Frame>();
    private static readonly string[] Elements = { "", "Physical", "None", "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Dendro", "Geo", "true" };
    private static readonly HashSet<string> Parts = new HashSet<string>(StringComparer.Ordinal)
    {
        "ATKBonus", "ATKBonusFlat", "ExtraBaseATK", "HPBonus", "HPBonusFlat", "ExtraBaseHP",
        "DEFBonus", "DEFBonusFlat", "ExtraBaseDEF", "EMBonus", "RechargeBonus", "CritRate", "CritDMG",
        "BaseDMGBonusFlat", "DMGBonus", "BaseDMGBonus", "LunarDMGBonus", "StellarDMGBonus", "Elevation",
        "DEFReduction", "DEFIgnored", "HealBonus", "ShieldBonus", "ShieldBonusFlat"
    };
    private readonly Dictionary<string, float> raw = new Dictionary<string, float>(StringComparer.Ordinal);
    private readonly Dictionary<string, Plan> plans = new Dictionary<string, Plan>(StringComparer.Ordinal);
    private readonly Dictionary<int, int> levels = new Dictionary<int, int>();
    private readonly Dictionary<StatusInstance, StatusInstance> conditionViews = new Dictionary<StatusInstance, StatusInstance>();
    private readonly BattleEntity source;
    private readonly StatusInstance identitySource;
    private readonly float atkResonance, cryoCrit, lunarTeam;
    private readonly Dictionary<string, float> teamDamage = new Dictionary<string, float>();
    public string StatusID { get; }
    public long InstanceID { get; }
    public int Turn { get; }
    public int Phase { get; }
    public int SourceLevel { get; }
    public string Identity => StatusID + "#" + InstanceID + "@" + Turn + "/" + Phase;

    private StatusFormulaSnapshot(StatusInstance status)
    {
        source = status.Caster; identitySource = status; StatusID = status.StatusID2; InstanceID = status.ApplyOrder;
        Turn = BattleManager.Instance?.TurnCount ?? 0; Phase = (int)(BattleManager.Instance?.CurrentPhase ?? 0);
        SourceLevel = source.Level;
        // 只记录来源公式输入；不复制当前HP、能量、AP、Count载体或目标承伤属性。
        raw["TotalHP"] = source.TotalHP; raw["TotalATK"] = source.TotalATK; raw["TotalDEF"] = source.TotalDEF;
        raw["WeaponATK"] = source.WeaponATK; raw["CharacterATKBonus"] = source.CharacterATKBonus;
        raw["WeaponATKBonus"] = source.WeaponATKBonus; raw["WeaponDEFBonus"] = source.WeaponDEFBonus;
        raw["TotalEM"] = source.TotalEM; raw["TotalRechargeRate"] = source.TotalRechargeRate;
        raw["CritRate"] = source.CritRate; raw["CritDMG"] = source.CritDMG;
        raw["WeaponCritRate"] = source.WeaponCritRate; raw["WeaponCritDMG"] = source.WeaponCritDMG;
        raw["BaseDMGBonusFlat"] = source.BaseDMGBonusFlat; raw["DMGBonus"] = source.DMGBonus;
        raw["BaseDMGBonus"] = source.BaseDMGBonus; raw["LunarDMGBonus"] = source.LunarDMGBonus;
        raw["StellarDMGBonus"] = source.StellarDMGBonus; raw["Elevation"] = source.Elevation;
        raw["DEFReduction"] = source.DEFReduction; raw["DEFIgnored"] = source.DEFIgnored;
        raw["HealBonus"] = source.HealBonus;
        raw["PhysicalDmgBonus"] = source.PhysicalDmgBonus; raw["WeaponPhysicalDmgBonus"] = source.WeaponPhysicalDmgBonus;
        raw["PyroDmgBonus"] = source.PyroDmgBonus; raw["WeaponPyroDmgBonus"] = source.WeaponPyroDmgBonus;
        raw["HydroDmgBonus"] = source.HydroDmgBonus; raw["WeaponHydroDmgBonus"] = source.WeaponHydroDmgBonus;
        raw["ElectroDmgBonus"] = source.ElectroDmgBonus; raw["WeaponElectroDmgBonus"] = source.WeaponElectroDmgBonus;
        raw["CryoDmgBonus"] = source.CryoDmgBonus; raw["WeaponCryoDmgBonus"] = source.WeaponCryoDmgBonus;
        raw["AnemoDmgBonus"] = source.AnemoDmgBonus; raw["WeaponAnemoDmgBonus"] = source.WeaponAnemoDmgBonus;
        raw["DendroDmgBonus"] = source.DendroDmgBonus; raw["WeaponDendroDmgBonus"] = source.WeaponDendroDmgBonus;
        raw["GeoDmgBonus"] = source.GeoDmgBonus; raw["WeaponGeoDmgBonus"] = source.WeaponGeoDmgBonus;
        raw["HPBasis"] = source.CharacterCtrl != null ? source.CharacterCtrl.GetHPBeforePercentBonuses() : source.TotalHP;
        raw["DEFBasis"] = source.CharacterCtrl != null ? source.CharacterCtrl.GetDEFBeforePercentBonuses() : source.TotalDEF;
        raw["BakedHPBonus"] = source.BakedHPBonus; raw["CharacterDEFBonus"] = source.CharacterDEFBonus;
        var team = BattleManager.Instance?.TeamBonuses;
        atkResonance = team?.Resonance.ATKBonus(source) ?? 0f;
        // 固定共鸣资格与系数，目标是否冰附着/冻结仍逐hit判断。
        cryoCrit = team?.Contains(source) == true && team.Resonance.Has("Cryo")
            ? DataManager.Instance.TeamBonusRules.Resonance.Number("CryoCrit") : 0f;
        lunarTeam = source.GetTeamLunarDamageBonus();
        foreach (string element in Elements) teamDamage[element] = source.GetTeamDamageBonus(element);
        if (source.CharacterCtrl != null)
            for (int type = 0; type < 4; type++) levels[type] = source.CharacterCtrl.GetEffectiveSkillLevel(type);
    }

    internal static void Capture(StatusInstance status)
    {
        var dm = DataManager.Instance;
        if (status?.Caster == null || dm == null) return;
        var binding = ScriptHookContext.Current;
        if (binding?.Status != null && binding.Status != status && binding.Caster == status.Caster
            && dm.StatusEffectDict.Values.Any(e => e != null && e.StatusEffectID == binding.ExecutingStatusEffectID
                && e.EffectType == "BindStatus" && e.Param1 == status.StatusID2
                && e.StatusEffectID / 100 == binding.Status.MainData?.StatusID)) status.FormulaSnapshotParent = binding.Status;
        if (!dm.StatusAttributesDict.ContainsKey(status.StatusID2)) return;
        // 新状态必须读取施放者当前值，不能意外继承正在执行的外层状态记忆。
        using var suspended = Enter(null, null);
        var memory = new StatusFormulaSnapshot(status);
        var ownStatuses = status.Caster.GetEffectiveStatusList();
        var effects = dm.StatusEffectDict.Values.Where(e => e != null && status.MainData != null
            && e.StatusEffectID / 100 == status.MainData.StatusID
            && (e.EffectType == "Damage" || e.EffectType == "Heal" || e.EffectType == "Shield")).ToList();
        foreach (var effect in effects)
            foreach (string element in Elements)
                memory.plans.Add(Key(effect.StatusEffectID2, element), memory.BuildPlan(effect, element, ownStatuses));
        foreach (string element in Elements)
            memory.plans.Add(Key("", element), memory.BuildPlan(new StatusEffectData
                { StatusEffectID2 = "", DamageType = "Reaction" }, element, ownStatuses));
        foreach (var plan in memory.plans.Values) memory.FoldAttributes(plan);
        status.FormulaSnapshot = memory;
    }
    public static IDisposable Enter(StatusInstance status, string effectID)
        => new Frame(Find(status, effectID), effectID);
    /// <summary>具体绑定子状态调用父状态定义的效果时复用父记忆；子状态自己的新效果不自动获得快照。</summary>
    public static StatusFormulaSnapshot Find(StatusInstance status, string effectID)
    {
        var seen = new HashSet<StatusInstance>();
        while (status != null && seen.Add(status))
        {
            if (status.FormulaSnapshot?.HasPlan(effectID, "") == true) return status.FormulaSnapshot;
            status = status.FormulaSnapshotParent;
        }
        return null;
    }
    public static IDisposable Suspend() => new Frame(null, null);
    internal static IDisposable EnterTarget(BattleEntity target)
        => new Frame(Current.Value?.Memory, Current.Value?.Effect, target);
    public static StatusFormulaSnapshot For(BattleEntity actor, string effectID = null)
    {
        var frame = Current.Value;
        return actor != null && frame?.Memory != null && frame.Memory.source == actor
            && (effectID == null || frame.Effect == effectID)
            ? frame.Memory : null;
    }
    internal bool HasPlan(string effect, string element) => plans.ContainsKey(Key(effect, element));
    private static string Key(string effect, string element) => effect + "|" + Normalize(element);
    private static string Normalize(string element) => string.IsNullOrEmpty(element) ? ""
        : DamageProperties.IsTrue(element) ? "true" : DamageProperties.NormalizeElement(element);
    public float Raw(string field) => raw.TryGetValue(field, out var value) ? value
        : throw new NotSupportedException("快照未记录的来源公式字段：" + field);
    public int SkillLevel(int type, int fallback) => levels.TryGetValue(type, out var level) ? level : fallback;

    private Plan BuildPlan(StatusEffectData output, string element, List<StatusInstance> statuses)
    {
        var plan = new Plan { Effect = output.StatusEffectID2, Element = element, DamageType = output.DamageType };
        var dm = DataManager.Instance;
        foreach (var status in statuses)
        {
            if (StatusSnapshotConditions.IsLiveTargetStatus(status)) continue;
            var main = status?.MainData; string part = main?.GetMultiplierPart();
            if (main == null || !Parts.Contains(part ?? "")
                || !BattleEntity.StatusAppliesTo(main, output.StatusEffectID2, "", output.DamageType, element)) continue;
            var context = Context(status, output.StatusEffectID2, output.DamageType, element, null, null);
            if (!StatusSnapshotConditions.Freeze(main.ScriptHook, context, out string deferred)) continue;
            var group = new Group { ID = status.StatusID2 ?? main.StatusID.ToString(), Part = part, Reaction = main.ApplyReactionType,
                Cap = main.MaxCount, Count = Math.Max(1, status.StackCount), BakedHP = status.HPBonusIncludedInPanel,
                Original = status, DeferredHook = deferred };
            foreach (var effect in dm.StatusEffectDict.Values)
            {
                if (effect == null || effect.StatusEffectID / 100 != main.StatusID
                    || (effect.EffectType != "Buff" && effect.EffectType != "Debuff")
                    || BattleSpecialRules.IsPreparedBaseDamageEffect(status, effect)) continue;
                if (!StatusSnapshotConditions.Freeze(effect.ScriptHook, context, out string effectDeferred)) continue;
                if (StatusSnapshotConditions.NeedsHitParameter(effect))
                {
                    group.Terms.Add(new Term { HitParameter = effect, DeferredHook = effectDeferred });
                    continue;
                }
                if (!HexereiStatusEnhancement.TryResolveScalar(effect, status, out float value)) continue;
                if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidOperationException("快照参数必须为有限值：" + effect.StatusEffectID2);
                group.Terms.Add(new Term { Value = value, DeferredHook = effectDeferred });
            }
            if (group.Terms.Count > 0)
            {
                if (!string.IsNullOrEmpty(group.DeferredHook) || group.Terms.Any(t => !string.IsNullOrEmpty(t.DeferredHook)))
                {
                    if (!conditionViews.TryGetValue(status, out var view)) conditionViews[status] = view = ConditionView(status);
                    group.ConditionView = view;
                }
                plan.Groups.Add(group);
            }
        }
        // 无条件参数在创建时合并。与目标条件竞争同一MaxCount的同ID组保留到命中时，避免提前抢占名额。
        var variable = new HashSet<string>(plan.Groups.Where(g => !string.IsNullOrEmpty(g.Reaction)
            || !string.IsNullOrEmpty(g.DeferredHook) || g.Terms.Any(t => t.HitParameter != null || !string.IsNullOrEmpty(t.DeferredHook)))
            .Select(g => g.Part + "|" + g.ID));
        var quota = new Dictionary<string, int>(); var unbakedQuota = new Dictionary<string, int>();
        foreach (var group in plan.Groups.ToArray())
        {
            string key = group.Part + "|" + group.ID;
            if (variable.Contains(key)) continue;
            quota.TryGetValue(key, out int previous);
            int count = group.Cap <= 0 ? group.Count : Math.Min(group.Count, Math.Max(0, group.Cap - previous));
            quota[key] = previous + count;
            float amount = group.Terms.Sum(t => t.Value) * count;
            plan.Fixed.TryGetValue(group.Part, out float total); plan.Fixed[group.Part] = total + amount;
            if (!group.BakedHP)
            {
                unbakedQuota.TryGetValue(key, out int hpPrevious);
                int hpCount = group.Cap <= 0 ? group.Count : Math.Min(group.Count, Math.Max(0, group.Cap - hpPrevious));
                unbakedQuota[key] = hpPrevious + hpCount;
                plan.FixedUnbakedHP.TryGetValue(group.Part, out float hp);
                plan.FixedUnbakedHP[group.Part] = hp + group.Terms.Sum(t => t.Value) * hpCount;
            }
            plan.Groups.Remove(group);
        }
        return plan;
    }
    private void FoldAttributes(Plan plan)
    {
        Cache("TotalATK", "ATKBonus", "ATKBonusFlat", "ExtraBaseATK");
        Cache("TotalDEF", "DEFBonus", "DEFBonusFlat", "ExtraBaseDEF");
        Cache("TotalHP", "HPBonus", "HPBonusFlat", "ExtraBaseHP");
        Cache("TotalEM", "EMBonus"); Cache("TotalRechargeRate", "RechargeBonus"); Cache("CritDMG", "CritDMG");
        if (cryoCrit == 0) Cache("CritRate", "CritRate");
        void Cache(string name, params string[] parts)
        {
            if (!plan.Groups.Any(g => parts.Contains(g.Part)) && !StatusSnapshotConditions.HasLiveTargetPart(parts))
                plan.Attributes[name] = Attribute(name, plan.Effect, plan.Element);
        }
    }
    private StatusInstance ConditionView(StatusInstance original)
    {
        // 仅为旧目标条件提供身份上下文；不加入任何容器，不复制监听、行动、绑定或Count处理器。
        var view = new StatusInstance { StatusID2 = original.StatusID2, MainData = original.MainData,
            IsActive = true, Caster = original.Caster, Host = StatusHostAccess.FindHost(original, source), WeaponContext = original.WeaponContext,
            EquipmentSource = original.EquipmentSource, ApplyOrder = original.ApplyOrder, StackCount = 1,
            RuntimeElement = original.RuntimeElement, RuntimeValue = original.RuntimeValue,
            HasSourceEntity = original.HasSourceEntity, SourceEntityID = original.SourceEntityID, SourceSide = original.SourceSide };
        return view;
    }
    private ScriptHookContext Context(StatusInstance status, string effect, string type, string element,
        BattleEntity target, ReactionType? reaction)
        => new ScriptHookContext { Caster = status.Caster, Recipient = source, Status = status,
            StatusHost = status.Host ?? StatusHostAccess.FindHost(status, source), Target = target, HitEffectID = effect, DamageType = type,
            DamageElement = element, DamageSkillID = "", DamageReaction = reaction };
    public float Bonus(string part, string effect, string element, BattleEntity target = null,
        ReactionType? reaction = null, bool unbakedHP = false, bool exclusive = false, bool includeLiveTarget = true)
    {
        if (!plans.TryGetValue(Key(effect, element), out var plan))
            throw new InvalidOperationException("快照没有该状态效果的公式参数：" + Identity + "/" + effect + "/" + element);
        var fixedValues = unbakedHP ? plan.FixedUnbakedHP : plan.Fixed;
        float total = !exclusive && fixedValues.TryGetValue(part, out float prepared) ? prepared : 0;
        // 当前敌方标记与逐次消费资格不能烘入施法者记忆；新施加/已消失状态都以本次命中为准。
        if (includeLiveTarget) total += source.GetLiveTargetStatusBonus(part, effect, element, target,
            plan.DamageType, reaction);
        var used = new Dictionary<string, int>();
        foreach (var group in plan.Groups)
        {
            if (group.Part != part || (unbakedHP && group.BakedHP) || (exclusive && string.IsNullOrEmpty(group.Reaction))) continue;
            if (!string.IsNullOrEmpty(group.Reaction) && (!reaction.HasValue
                || !ReactionFilterMatcher.Matches(group.Reaction, reaction.Value, null, element))) continue;
            used.TryGetValue(group.ID, out int previous);
            int count = group.Cap <= 0 ? group.Count : Math.Min(group.Count, Math.Max(0, group.Cap - previous));
            if (count == 0) continue;
            var context = Context(group.ConditionView ?? group.Original, effect, plan.DamageType, element, target, reaction);
            if (!StatusSnapshotConditions.EvaluateDeferred(group.DeferredHook, context, group.Original)) continue;
            bool collected = false; float value = 0;
            foreach (var term in group.Terms)
                if (StatusSnapshotConditions.EvaluateDeferred(term.DeferredHook, context, group.Original))
                {
                    float scalar = term.Value;
                    if (term.HitParameter != null && !WeaponHooks.TryResolveBuffScalar(term.HitParameter, group.Original, out scalar))
                        throw new InvalidOperationException("快照的命中参数缺少已实现的预备读取入口：" + term.HitParameter.StatusEffectID2);
                    if (term.HitParameter != null) scalar *= group.Original.HexereiMultiplier;
                    if (float.IsNaN(scalar) || float.IsInfinity(scalar)) throw new InvalidOperationException("快照命中参数不是有限数值。");
                    value += scalar; collected = true;
                }
            if (!collected) continue;
            used[group.ID] = previous + count; total += value * count;
        }
        return total;
    }
    public float Attribute(string name, string effect, string element, BattleEntity target = null)
    {
        name = name == "ATK" ? "TotalATK" : name == "DEF" ? "TotalDEF" : name == "MaxHP" ? "TotalHP" : name;
        if (plans.TryGetValue(Key(effect, element), out var plan) && plan.Attributes.TryGetValue(name, out var prepared)) return prepared;
        target = target ?? Current.Value?.Target;
        float B(string part) => Bonus(part, effect, element, target);
        switch (name)
        {
            case "ATK": case "TotalATK":
                return (Raw("TotalATK") + Raw("WeaponATK") + B("ExtraBaseATK"))
                    * (1 + Raw("CharacterATKBonus") + Raw("WeaponATKBonus") + atkResonance + B("ATKBonus")) + B("ATKBonusFlat");
            case "DEF": case "TotalDEF":
                float dp = B("DEFBonus"), basis = Raw("DEFBasis") > 0 ? Raw("DEFBasis") : Raw("TotalDEF");
                return Mathf.Max(0, Raw("TotalDEF") + basis * dp + B("ExtraBaseDEF")
                    * (1 + Raw("CharacterDEFBonus") + Raw("WeaponDEFBonus") + dp) + B("DEFBonusFlat"));
            case "MaxHP": case "TotalHP":
                float hp = Bonus("HPBonus", effect, element, target, unbakedHP: true);
                return Mathf.Max(0, Raw("TotalHP") + Raw("HPBasis") * hp
                    + Bonus("HPBonusFlat", effect, element, target, unbakedHP: true)
                    + B("ExtraBaseHP") * (1 + Raw("BakedHPBonus") + hp));
            case "TotalEM": return Mathf.Max(0, Raw("TotalEM") + B("EMBonus"));
            case "TotalRechargeRate": return Mathf.Max(0, (Raw("TotalRechargeRate") > 0 ? Raw("TotalRechargeRate") : 1) + B("RechargeBonus"));
            case "CritRate": return Raw("CritRate") + Raw("WeaponCritRate") + B("CritRate") + CryoCrit(target);
            case "CritDMG": return Raw("CritDMG") + Raw("WeaponCritDMG") + B("CritDMG");
            default: throw new NotSupportedException("快照尚未支持的来源属性：" + name);
        }
    }
    public float TeamDamage(string element) => teamDamage.TryGetValue(Normalize(element), out var value) ? value : 0f;
    public float LunarTeam => lunarTeam;
    public float CryoCrit(BattleEntity target) => target != null
        && WeaponHooks.HasCryoInfluence(identitySource, target) ? cryoCrit : 0f;
    public float ElementBonus(string element)
    {
        if (string.IsNullOrEmpty(element) || DamageProperties.IsFixed(element)) return 0f;
        string prefix = Normalize(element);
        return Raw(prefix + "DmgBonus") + Raw("Weapon" + prefix + "DmgBonus");
    }
    internal ReactionBuffTotals ReactionBonuses(ReactionType reaction, string element, string effect = "",
        BattleEntity target = null, bool specialSkill = false, bool lunarCloud = false)
    {
        float B(string part) => Bonus(part, effect, element, target, reaction);
        bool lunar = ReactionFilterMatcher.IsLunar(reaction), stellar = ReactionFilterMatcher.IsStellar(reaction);
        return new ReactionBuffTotals
        {
            DMGBonus = specialSkill ? 0 : Raw("DMGBonus") + TeamDamage(element) + B("DMGBonus"),
            BaseDMGBonusFlat = Raw("BaseDMGBonusFlat") + B("BaseDMGBonusFlat"),
            BaseDMGBonus = reaction == ReactionType.None ? 0 : Raw("BaseDMGBonus") + B("BaseDMGBonus"),
            LunarDMGBonus = lunar ? Raw("LunarDMGBonus") + lunarTeam + B("LunarDMGBonus") : 0,
            StellarDMGBonus = stellar ? Raw("StellarDMGBonus") + B("StellarDMGBonus") : 0,
            Elevation = specialSkill || lunarCloud ? Raw("Elevation") + B("Elevation") : 0,
            CritRate = lunarCloud ? Raw("CritRate") + Raw("WeaponCritRate") + B("CritRate") : 0,
            CritDMG = lunarCloud ? Raw("CritDMG") + Raw("WeaponCritDMG") + B("CritDMG") : 0,
            EMBonus = lunarCloud ? B("EMBonus") : 0
        };
    }
    public void Trace(string effect, BattleEntity target, float basis, float multiplier,
        float baseDamage, float bonus, float defense, float resistance)
        => LogManager.Log(LogCategory.StatusDamage, $"[公式快照] {Identity} 效果={effect} 目标={target?.EntityID} "
            + $"位置={target?.Side}:{target?.SlotPosition} 基准={basis:R} 倍率={multiplier:R} 基础项={baseDamage:R} "
            + $"增伤乘数={bonus:R} 防御乘数={defense:R} 抗性乘数={resistance:R}");
}
