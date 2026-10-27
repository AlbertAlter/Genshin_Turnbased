using System;
using System.Collections.Generic;

/// <summary>一次只读元素池快照；不暴露可修改的ElementalAura或反应状态。</summary>
public sealed class ElementPoolReading
{
    private readonly float finiteAmount;
    public BattleEntity Target { get; }
    public string Element { get; }
    public ReactionPoolKind Kind { get; }
    public bool IsIntrinsic { get; }
    public bool IsPresent => IsIntrinsic || finiteAmount > 0f;
    /// <summary>只用于有限池。固有元素没有数量，不能偷偷转换为0、9999或浮点Infinity。</summary>
    public float Amount => !IsIntrinsic ? finiteAmount
        : throw new InvalidOperationException("固有元素没有有限元素量；反应必须按固有池规则计算：" + Element);
    /// <summary>本次有限反应所需的额度，不是给固有池赋予数量。</summary>
    public float AvailableFor(float requirement)
    {
        if (float.IsNaN(requirement) || float.IsInfinity(requirement) || requirement < 0f)
            throw new ArgumentOutOfRangeException(nameof(requirement));
        return IsIntrinsic ? requirement : Math.Min(requirement, Math.Max(0f, finiteAmount));
    }
    public BattleEntity SourceEntity { get; }
    public int SourceEntityID { get; }
    public string SourceEffectID { get; }
    public string SourceSkillID { get; }
    public string SourceStatusID { get; }
    public int OriginPhase { get; }
    public long PoolApplyOrder { get; }

    internal ElementPoolReading(BattleEntity target, string element, ReactionPoolKind kind, float amount,
        BattleEntity source, int sourceID, string effectID, string skillID, int phase, long order = 0)
    {
        if (float.IsNaN(amount) || float.IsInfinity(amount))
            throw new ArgumentOutOfRangeException(nameof(amount), "有限元素池不能保存NaN或Infinity。");
        Target = target; Element = element; Kind = kind; finiteAmount = amount;
        SourceEntity = source; SourceEntityID = sourceID; SourceEffectID = effectID;
        SourceSkillID = skillID; OriginPhase = phase; PoolApplyOrder = order;
    }

    private ElementPoolReading(BattleEntity target, string element, StatusInstance status)
    {
        Target = target; Element = element; Kind = ReactionPoolKind.Intrinsic; IsIntrinsic = true;
        SourceEntity = target; SourceEntityID = target.EntityID; SourceEffectID = string.Empty;
        SourceStatusID = status.StatusID2;
        SourceSkillID = string.Empty; OriginPhase = status.AddInPhase; PoolApplyOrder = status.ApplyOrder;
    }

    /// <summary>从真实持有的状态构造只读表示；不施加状态或登记普通附着。</summary>
    public static ElementPoolReading FromIntrinsicStatus(BattleEntity target, string element, StatusInstance status)
    {
        if (target == null || status == null || !ReferenceEquals(status.Host, target) || !status.IsActive
            || !target.GetStatusList().Contains(status))
            throw new ArgumentException("固有元素快照必须来自目标身上有效的具体状态实例。");
        if (element != "Pyro" && element != "Hydro" && element != "Cryo" && element != "Electro"
            && element != "Dendro" && element != "Anemo" && element != "Geo")
            throw new ArgumentException("固有元素必须明确指定元素类型。", nameof(element));
        return new ElementPoolReading(target, element, status);
    }
}

/// <summary>
/// 元素读取唯一入口。角色/敌人都查询真实BattleEntity；位置入口只读取当前占位者，不猜场地状态的元素。
/// 不扣量、不刷新来源、不清理失效状态、不生成声明或随机结果；存活与反应资格仍由调用者判断。
/// </summary>
public static class ElementQuery
{
    public static readonly IReadOnlyList<string> Elements = Array.AsReadOnly(
        new[] { "Pyro", "Hydro", "Cryo", "Electro", "Anemo", "Geo", "Dendro" });

    public static string IntrinsicStatusID(string element)
    {
        foreach (string candidate in Elements)
            if (candidate == element) return "ST_Intrinsic_" + element;
        return null;
    }

    public static bool IsIntrinsicStatus(string statusID)
    {
        foreach (string element in Elements)
            if (statusID == IntrinsicStatusID(element)) return true;
        return false;
    }

    public static ElementPoolReading ReadIntrinsic(BattleEntity target, string element)
        => ReadPool(target, element, ReactionPoolKind.Intrinsic);

    /// <summary>实际元素影响；固有优先于普通附着，不把冻/燃虚拟池当作技能条件。</summary>
    public static ElementPoolReading ReadAura(BattleEntity target, string element)
        => ReadIntrinsic(target, element) ?? ReadNormal(target, element);

    public static bool HasAura(BattleEntity target, string element) => ReadAura(target, element)?.IsPresent == true;

    public static ElementPoolReading ReadReactionAura(ReactionContext context, string element)
    {
        if (context == null) return null;
        var intrinsic = context.AllowsReactionPool(element, ReactionPoolKind.Intrinsic)
            ? ReadIntrinsic(context.Target, element) : null;
        return intrinsic ?? (context.AllowsReactionPool(element, ReactionPoolKind.NormalAura)
            ? ReadNormal(context.Target, element) : null) ?? ReadShield(context, element);
    }

    /// <summary>仅反应读取盾元素，不把盾写入普通附着或固有状态。</summary>
    public static ElementPoolReading ReadShield(ReactionContext context, string element)
    {
        if (context?.Target == null || !context.AllowsReactionPool(element, ReactionPoolKind.EnemyShield)) return null;
        Shield shield = context.Target.GetEnemyElementalShield();
        if (shield == null) return null;
        string shieldElement = context.EnemyShieldHit?.ShieldRow == "Freeze" ? "Cryo" : shield.Element;
        if (!string.Equals(shieldElement, element, StringComparison.OrdinalIgnoreCase)) return null;
        return new ElementPoolReading(context.Target, element, ReactionPoolKind.EnemyShield, shield.Value,
            shield.SourceStatus?.Caster, shield.SourceStatus?.Caster != null ? shield.SourceStatus.Caster.EntityID : 0,
            string.Empty, string.Empty, shield.SourceStatus?.AddInPhase ?? 0);
    }

    public static ElementPoolReading ReadShield(BattleEntity target, string element)
        => ReadShield(new ReactionContext { Target = target,
            EnemyShieldHit = EnemyElementShieldSystem.PrepareHit(target, string.Empty, 0f) }, element);

    public static ElementPoolReading ReadNormal(BattleEntity target, string element)
        => ReadPool(target, element, ReactionPoolKind.NormalAura);

    public static ElementPoolReading ReadPool(BattleEntity target, string element, ReactionPoolKind kind)
    {
        if (kind == ReactionPoolKind.Any)
            throw new ArgumentException("Any包含不同来源的多个池，不能冒充一份池；请查询具体种类或Capture。", nameof(kind));
        if (kind != ReactionPoolKind.NormalAura && kind != ReactionPoolKind.Frozen && kind != ReactionPoolKind.Burning
            && kind != ReactionPoolKind.Intrinsic) throw new ArgumentOutOfRangeException(nameof(kind));
        if (target == null || string.IsNullOrEmpty(element)) return null;
        if (kind == ReactionPoolKind.NormalAura)
        {
            ElementalAura aura = target.GetAura(element);
            return aura == null ? null : FromNormal(target, aura);
        }
        if (kind == ReactionPoolKind.Intrinsic)
        {
            string id = IntrinsicStatusID(element);
            if (id == null) return null;
            StatusInstance first = null;
            foreach (var status in target.GetStatusList())
                if (status != null && status.IsActive && ReferenceEquals(status.Host, target)
                    && status.StatusID2 == id && (first == null || status.ApplyOrder < first.ApplyOrder)) first = status;
            return first == null ? null : ElementPoolReading.FromIntrinsicStatus(target, element, first);
        }
        if (kind == ReactionPoolKind.Frozen && element != "Cryo" || kind == ReactionPoolKind.Burning && element != "Pyro")
            return null;
        ReactionType reaction = kind == ReactionPoolKind.Frozen ? ReactionType.Frozen : ReactionType.Burning;
        if (!ReactionStateSystem.TryGetEntityState(target, reaction, out var state)) return null;
        var source = state.SourceSnapshot;
        return new ElementPoolReading(target, element, kind, Math.Max(0f, state.ReactionElementAmount),
            source?.SourceEntity, source?.SourceEntityID ?? 0, source?.SourceEffectID, source?.SourceSkillID,
            state.OriginPhase, state.ApplyOrder);
    }

    public static ElementPoolReading ReadAtPosition(FieldPosition position, string element,
        ReactionPoolKind kind = ReactionPoolKind.NormalAura)
        => ReadPool(position?.Occupant, element, kind);

    public static float GetAmount(BattleEntity target, string element, ReactionPoolKind kind = ReactionPoolKind.NormalAura)
        => ReadPool(target, element, kind)?.Amount ?? 0f;

    public static bool HasNormalAura(BattleEntity target, string element)
        => ReadNormal(target, element)?.IsPresent == true;

    /// <summary>显式反应用途的合计；不用于普通元素影响条件，不合并或改写各池的来源。</summary>
    public static float GetReactionAmount(BattleEntity target, string element, bool ignoreFrozen = false)
    {
        float amount = GetAmount(target, element);
        if (element == "Cryo" && !ignoreFrozen) amount += GetAmount(target, element, ReactionPoolKind.Frozen);
        if (element == "Pyro") amount += GetAmount(target, element, ReactionPoolKind.Burning);
        return amount;
    }

    public static float GetAvailableReactionAmount(BattleEntity target, string element, float requirement, bool ignoreFrozen = false)
    {
        if (float.IsNaN(requirement) || float.IsInfinity(requirement) || requirement < 0f)
            throw new ArgumentOutOfRangeException(nameof(requirement));
        var intrinsic = ReadIntrinsic(target, element);
        return intrinsic != null ? intrinsic.AvailableFor(requirement)
            : Math.Min(requirement, GetReactionAmount(target, element, ignoreFrozen));
    }

    /// <summary>遵守分份锁定及IgnoreFrozenAura，不把被排除池的数量或来源借给本次反应。</summary>
    public static float GetReactionAmount(ReactionContext context, string element)
    {
        if (context == null) return 0f;
        if (context.AllowsReactionPool(element, ReactionPoolKind.Intrinsic)
            && ReadIntrinsic(context.Target, element) != null)
            return ReactionPairRules.RequiredAura(context.AttackElement, element, context.AttackAmount);
        float amount = context.AllowsReactionPool(element, ReactionPoolKind.NormalAura) ? GetAmount(context.Target, element) : 0f;
        if (element == "Cryo" && !context.IgnoreFrozenAura && context.AllowsReactionPool(element, ReactionPoolKind.Frozen))
            amount += GetAmount(context.Target, element, ReactionPoolKind.Frozen);
        if (element == "Pyro" && context.AllowsReactionPool(element, ReactionPoolKind.Burning))
            amount += GetAmount(context.Target, element, ReactionPoolKind.Burning);
        amount += ReadShield(context, element)?.Amount ?? 0f;
        return amount;
    }

    /// <summary>全部正量池的只读快照；稳定按种类及元素排序。普通池和虚拟池即使同元素也各自保留。</summary>
    public static IReadOnlyList<ElementPoolReading> Capture(BattleEntity target)
    {
        var pools = new List<ElementPoolReading>();
        if (target == null) return pools.AsReadOnly();
        foreach (var aura in target.ElementalAuras)
            if (aura.AuraAmount > 0f) pools.Add(FromNormal(target, aura));
        var frozen = ReadPool(target, "Cryo", ReactionPoolKind.Frozen);
        var burning = ReadPool(target, "Pyro", ReactionPoolKind.Burning);
        if (frozen?.IsPresent == true) pools.Add(frozen);
        if (burning?.IsPresent == true) pools.Add(burning);
        foreach (string element in Elements)
        {
            var intrinsic = ReadIntrinsic(target, element);
            if (intrinsic != null) pools.Add(intrinsic);
        }
        pools.Sort((a, b) => { int kind = a.Kind.CompareTo(b.Kind);
            return kind != 0 ? kind : string.CompareOrdinal(a.Element, b.Element); });
        return pools.AsReadOnly();
    }

    private static ElementPoolReading FromNormal(BattleEntity target, ElementalAura aura)
        => new ElementPoolReading(target, aura.Element, ReactionPoolKind.NormalAura, aura.AuraAmount,
            aura.SourceEntity, aura.SourceEntityID, aura.SourceEffectID, aura.SourceSkillID, aura.OriginPhase);
}
