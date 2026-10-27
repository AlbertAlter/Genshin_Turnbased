using System;
using System.Globalization;

/// <summary>每份Enchant的独立参数；不复用Count余额、RuntimeElement或共享配表。</summary>
[Serializable]
public struct EnchantStatusData
{
    public string Element;
    public float ElementAmount;
    public bool CanBeOverridden;

    public EnchantStatusData(string element, float amount, bool canBeOverridden)
    { Element = element; ElementAmount = amount; CanBeOverridden = canBeOverridden; }
}

/// <summary>
/// 只读选取附魔；仅Normal/Heavy物理伤害转换元素和附着量。
/// 可覆盖：火>水>雷>冰，同元素取后施加者；不可覆盖者优先，多份时保留最早者。
/// 未启用实例仍按原生命周期存在，查询不移除、刷新、消耗或声明反应。
/// </summary>
public static class EnchantStatusSystem
{
    public static bool IsEnchant(StatusMainData main)
        => string.Equals(main?.StatusType?.Trim(), "Enchant", StringComparison.OrdinalIgnoreCase);

    public static EnchantStatusData Parse(StatusMainData main, string element, string amount, string flag)
    {
        if (!IsEnchant(main)) return default;
        string canonical = ApplyElementEffect.ParseElement(element);
        if (!float.TryParse(amount?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            || float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            throw new TableValidationException("Enchant的Param2须明确填写有限非负元素附着量：" + main.StatusID2);
        string token = flag?.Trim();
        if (!string.IsNullOrEmpty(token) && token != "0" && token != "1")
            throw new TableValidationException("Enchant的Param3仅允许空/0（允许覆盖）或1（不可覆盖）：" + main.StatusID2);
        bool canBeOverridden = token != "1";
        if (canBeOverridden && Priority(canonical) == 0)
            throw new TableValidationException("风、草、岩Enchant必须填Param3=1（不可覆盖）：" + main.StatusID2);
        return new EnchantStatusData(canonical, value, canBeOverridden);
    }

    private static int Priority(string element) => element switch
    { "Pyro" => 4, "Hydro" => 3, "Electro" => 2, "Cryo" => 1, _ => 0 };

    public static bool CanConvert(string damageType, string element)
        => (damageType == "Normal" || damageType == "Heavy")
            && string.Equals(element?.Trim(), "Physical", StringComparison.OrdinalIgnoreCase);

    /// <summary>只读入口，供伤害与未来UI查询；附着量始终来自返回的具体实例。</summary>
    public static StatusInstance GetActive(BattleEntity actor)
    {
        if (actor == null) return null;
        StatusInstance selected = null;
        foreach (var status in actor.GetEffectiveStatusList())
        {
            if (status == null || !status.IsActive || !IsEnchant(status.MainData)) continue;
            if (string.IsNullOrEmpty(status.Enchant.Element))
                throw new InvalidOperationException("Enchant缺少实例参数：" + status.StatusID2);
            if (selected == null) { selected = status; continue; }
            var current = selected.Enchant;
            var candidate = status.Enchant;
            if (!current.CanBeOverridden)
            {
                if (!candidate.CanBeOverridden && status.ApplyOrder < selected.ApplyOrder) selected = status;
                continue;
            }
            if (!candidate.CanBeOverridden || Priority(candidate.Element) > Priority(current.Element)
                || (Priority(candidate.Element) == Priority(current.Element) && status.ApplyOrder > selected.ApplyOrder))
                selected = status;
        }
        return selected;
    }

    public static EnchantStatusData? Resolve(BattleEntity actor, string damageType, string element)
        => CanConvert(damageType, element) ? GetActive(actor)?.Enchant : null;

    /// <summary>整套表加载后验证引用；Enchant是状态类型，不是效果类型。</summary>
    public static void ValidateDefinitions(DataManager data)
    {
        foreach (var main in data.StatusMainDict.Values)
            if (IsEnchant(main) && !string.IsNullOrEmpty(main.GetMultiplierPart()))
                throw new TableValidationException("Enchant不使用属性乘区：" + main.StatusID2);
        foreach (var effect in data.SkillEffectDict.Values)
            if (effect != null) ValidateApplication(data, effect.EffectType, effect.Param1,
                effect.Element, effect.Param2, effect.Param3, effect.SkillEffectID2);
        foreach (var effect in data.StatusEffectDict.Values)
            if (effect != null) ValidateApplication(data, effect.EffectType, effect.Param1,
                effect.Element, effect.Param2, effect.Param3, effect.StatusEffectID2);
        foreach (var effect in data.EnemySkillEffectDict.Values)
            if (effect != null) ValidateApplication(data, effect.EffectType, effect.Param1,
                effect.Element, effect.Param2, effect.Param3, effect.SkillEffectID2);
    }

    private static void ValidateApplication(DataManager data, string type, string id,
        string element, string amount, string flag, string effectID)
    {
        if (string.Equals(type, "Enchant", StringComparison.OrdinalIgnoreCase))
            throw new TableValidationException("Enchant是状态类型，请通过ApplyStatus/BindStatus施加：" + effectID);
        if ((type != "ApplyStatus" && type != "BindStatus") || string.IsNullOrEmpty(id)) return;
        id = CountStatusSystem.ResolvePublicStatusReference(id);
        if (data.StatusMainDict.TryGetValue(id, out var main) && IsEnchant(main)) Parse(main, element, amount, flag);
    }
}
