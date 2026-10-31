using System;
using System.Globalization;

/// <summary>Damage.Param3 的性质标签；不替代 Normal/Heavy/Skill/Burst 伤害类型。</summary>
[Serializable]
public struct DamageProperties
{
    public bool IsLunar;
    public bool IsStellar;
    public bool IsNightSoul;
    public bool IsPneuma;
    public bool IsOusia;
    public bool IsResonance;
    public ReactionType Reaction;
    public bool UsesSpecialFormula => IsLunar || IsStellar;

    public static bool IsTrue(string element)
        => string.Equals(element?.Trim(), "True", StringComparison.OrdinalIgnoreCase);

    public static bool IsDirect(string element)
        => string.Equals(element?.Trim(), "None", StringComparison.OrdinalIgnoreCase);

    public static bool IsFixed(string element) => IsTrue(element) || IsDirect(element);

    /// <summary>Damage.Element 必须显式填写；None 是直接伤害，不能兼作物理默认值。</summary>
    public static string NormalizeElement(string element)
    {
        foreach (string token in new[] { "Physical", "None", "True", "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Dendro", "Geo" })
            if (string.Equals(element?.Trim(), token, StringComparison.OrdinalIgnoreCase)) return token;
        throw new FormatException("Damage.Element 必须填写七元素、Physical（物理）、None（直接伤害）或 True（真实伤害）：" + element);
    }

    public static DamageProperties Parse(string element, string param3)
    {
        element = NormalizeElement(element);
        var result = new DamageProperties();
        if (!string.IsNullOrWhiteSpace(param3))
            foreach (string raw in param3.Split(';'))
            {
                switch (raw.Trim().ToLowerInvariant())
                {
                    case "lunar": case "月曜": result.IsLunar = true; break;
                    case "stellar": case "星烁": result.IsStellar = true; break;
                    case "nightsoul": case "夜魂": result.IsNightSoul = true; break;
                    case "pneuma": case "芒": result.IsPneuma = true; break;
                    case "ousia": case "荒": result.IsOusia = true; break;
                    case "resonance": case "共鸣": result.IsResonance = true; break;
                    default: throw new FormatException("Damage.Param3 只允许 Lunar/月曜、Stellar/星烁、NightSoul/夜魂、Pneuma/芒、Ousia/荒、Resonance/共鸣，多个用分号分隔：" + param3);
                }
            }
        if (result.IsLunar && result.IsStellar)
            throw new FormatException("同一次 Damage 不可同时填写 Lunar 和 Stellar。");
        string token = element?.Trim().ToLowerInvariant();
        if (result.IsResonance && token != "geo")
            throw new FormatException("Resonance/共鸣只允许Geo岩伤；不改变DamageType或伤害公式。");
        if (token == "true" && (result.IsPneuma || result.IsOusia))
            throw new FormatException("True 沿用Param3留空规则，不使用芒/荒性质标签。");
        if (result.IsLunar)
            switch (token)
            {
                case "dendro": result.Reaction = ReactionType.LunarBloom; break;
                case "electro": result.Reaction = ReactionType.LunarElectroCharged; break;
                case "geo": result.Reaction = ReactionType.LunarCrystallize; break;
                default: throw new FormatException("Lunar 只允许 Dendro(草)、Electro(雷)、Geo(岩)。");
            }
        if (result.IsStellar)
            switch (token)
            {
                case "cryo": case "electro": result.Reaction = ReactionType.StellarSuperconduct; break;
                case "anemo": break; // 允许标记；星扩散公式未实装，不冒充星超导。
                default: throw new FormatException("Stellar 只允许 Cryo(冰)、Electro(雷)、Anemo(风)。");
            }
        if (result.IsNightSoul && token != "pyro" && token != "hydro" && token != "electro"
            && token != "cryo" && token != "anemo" && token != "dendro" && token != "geo")
            throw new FormatException("NightSoul 只允许七种元素伤害，不允许物理、无元素或 True。");
        return result;
    }

    public void RequireImplementedFormula()
    {
        if (IsStellar && Reaction == ReactionType.None)
            throw new NotSupportedException("Stellar+Anemo 已支持性质标记，但星扩散伤害公式尚未实装，不能按普通风伤或星超导结算。");
    }

    /// <summary>True/None 的纯数字 Param1 是固定基准，不再解释成攻击力倍率。</summary>
    public static bool TryReadFixedTrueBase(string param1, out float value)
    {
        bool numeric = float.TryParse(param1?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        if (numeric && (float.IsNaN(value) || float.IsInfinity(value) || value < 0f))
            throw new FormatException("True/None 固定基准必须为有限非负数。");
        return numeric;
    }
}
