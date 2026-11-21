using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>显示用的只读上下文。Caster是状态来源，不是看到该Buff的受益角色。</summary>
public sealed class StatusDescriptionContext
{
    public StatusInstance Status { get; }
    public object Host { get; }
    public BattleManager Battle { get; }
    public bool English { get; }
    public BattleEntity Caster => Status?.Caster;

    public StatusDescriptionContext(StatusInstance status, object host, BattleManager battle, bool english = false)
    { Status = status; Host = host; Battle = battle; English = english; }
}

/// <summary>
/// 状态Description模板的{0}、{1}等动态参数。读取时求值，不修改共享MainData，
/// 不执行ScriptHook（其中可能有概率/副作用），不负责Buff、绑定或生命周期。
/// </summary>
public static class StatusDescriptionResolver
{
    private static readonly Dictionary<string, Func<StatusDescriptionContext, int, string>> Providers
        = new Dictionary<string, Func<StatusDescriptionContext, int, string>>(StringComparer.Ordinal);

    static StatusDescriptionResolver()
    {
        HeavenlyGiftArtifactHooks.RegisterDescriptions();
        Register(XiaoScriptHooks.Tamer, (context, index) => index == 0
            ? BuffPercent(context, "STE_Xiao_TamerBonus", "ST_Xiao_TamerBonus") : null);
        Register(XiaoScriptHooks.Cycling, (context, index) => index == 0
            ? BuffPercent(context, "STE_Xiao_CyclingBonus", "ST_Xiao_CyclingBonus") : null);
        Register(RazorScriptHooks.Sigils, (context, index) => index == 0
            ? BuffPercent(context, "STE_Razor_SigilsBuff") : null);
        Register(EulaScriptHooks.Heart, (context, index) => index == 0
            ? BuffPercent(context, "STE_Eula_HeartDEF") : null);
        Register("ST_Zhongli_FortifyBonus", (context, index) => index == 0
            ? BuffPercent(context, "STE_Zhongli_FortifyBonus") : null);
        Register(GanyuScriptHooks.C4, (context, index) => index == 0
            ? BuffPercent(context, "STE_Ganyu_C4Bonus", factor: context.Status.RuntimeIntValue) : null);
        Register(YanfeiScriptHooks.Brilliance, (context, index) => index == 0
            ? BuffPercent(context, "STE_Yanfei_BrillianceBonus") : null);
        Register("ST_Artifact_452305", (context, index) => index == 0
            ? BuffPercent(context, "STE_Artifact_45230101") : null);
        Register("ST_Artifact_453503", (context, index) => index == 0
            ? BuffPercent(context, "STE_Artifact_45350501") : null);
        Register("ST_Artifact_454004", (context, index) => index == 0
            ? BuffPercent(context, "STE_Artifact_45400401") : null);
        Register("ST_Artifact_454505", (context, index) => index == 0
            ? BuffPercent(context, "STE_Artifact_45450301") : index == 1
            ? BuffPercent(context, "STE_Artifact_45450401") : null);
        Register("ST_Artifact_454803", (context, index) => index == 0
            ? BuffPercent(context, "STE_Artifact_45480201") : null);
        Register("ST_Artifact_455203", (context, index) => index == 0
            ? BuffPercent(context, "STE_Artifact_45520201") : null);
        Register(SongOfDaysPastArtifact.WaveID, (context, index) => index == 0
            ? BuffNumber(context, "STE_Artifact_45500501", 1) : index == 1
            ? SongOfDaysPastArtifact.GetRemainingHits(context.Status)?.ToString(CultureInfo.InvariantCulture) : null);
        Register(SongOfDaysPastArtifact.BonusID, (context, index) => index == 0
            ? BuffNumber(context, "STE_Artifact_45500501", 1) : null);
        Register(WidsithWeaponCount.ID, WidsithTheme);
    }

    /// <summary>按状态ID2注册只读参数提供器。返回null表示参数未定义，保留原占位符。</summary>
    public static void Register(string statusID, Func<StatusDescriptionContext, int, string> provider)
    {
        if (string.IsNullOrWhiteSpace(statusID)) throw new ArgumentException("状态ID不能为空。", nameof(statusID));
        if (provider == null) throw new ArgumentNullException(nameof(provider));
        Providers.Add(statusID, provider);
    }

    public static string Resolve(StatusInstance status, object host = null, BattleManager battle = null)
        => Format(status?.MainData?.Description, GetArguments(status, host, battle));

    internal static Func<int, string> GetArguments(StatusInstance status, object host, BattleManager battle = null, bool english = false)
    {
        if (status == null || string.IsNullOrEmpty(status.StatusID2)
            || !Providers.TryGetValue(status.StatusID2, out var provider))
            return null;
        var context = new StatusDescriptionContext(status, host, battle ?? BattleManager.Instance, english);
        return index => provider(context, index);
    }

    internal static Func<int, string> GetIconArguments(StatusInstance status, object host, bool english)
        => GetArguments(status, host, english: english)
            ?? (status?.WeaponContext == null ? (Func<int, string>)null : index => WeaponIconArgument(status, index));

    private static string WeaponIconArgument(StatusInstance status, int index)
    {
        // 按效果编号末两位对应占位符。只允许直接Buff表达式和明确登记的只读参数。
        if (index < 0 || index >= 99 || status.MainData == null || DataManager.Instance == null) return null;
        string id = DataManager.WeaponEffectKey(status.MainData.StatusID * 100 + index + 1);
        if (!DataManager.Instance.StatusEffectDict.TryGetValue(id, out var effect) || effect.EffectType != "Buff"
            || !string.IsNullOrWhiteSpace(effect.ScriptHook)
            || !TryReadWeaponBuff(effect, status, out float value)) return null;
        string part = status.MainData.GetMultiplierPart();
        float scale = part == "EMBonus" || part.EndsWith("Flat", StringComparison.Ordinal) ? 1 : 100;
        return (value * scale * status.HexereiMultiplier).ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static bool TryReadWeaponBuff(StatusEffectData effect, StatusInstance status, out float value)
    {
        if (string.IsNullOrWhiteSpace(effect.DamageHook))
        {
            value = 0f;
            return effect.Param1 != null && !effect.Param1.Contains("TotalRechargeRate")
                && BuffExpressionEvaluator.TryResolveScalar(effect.Param1, status, out value);
        }
        if (status.Snapshot != null && WeaponSnapshotHooks.TryResolve(effect, status, out value)) return true;
        switch (effect.StatusEffectID)
        {
            case 431560301: case 431720301: case 431950301: case 432080301:
                return WeaponShieldAndLifeHooks.TryResolve(effect, status, out value);
            case 432220301: case 432220401:
                return WeaponResourceAndAttributeHooks.TryResolve(effect, status, out value);
            case 431980301:
                return WeaponHooks.TryResolveBuffScalar(effect, status, out value);
            default:
                value = 0f;
                return false;
        }
    }

    private static string WidsithTheme(StatusDescriptionContext context, int index)
    {
        if (index != 0) return null;
        switch ((int)context.Status.RuntimeValue)
        {
            case 1: return context.English ? "Recitative" : "宣叙调";
            case 2: return context.English ? "Aria" : "咏叹调";
            case 3: return context.English ? "Interlude" : "间奏曲";
            default: return null;
        }
    }

    private static string BuffPercent(StatusDescriptionContext context, string effectId, string childId = null, int factor = 1)
        => BuffNumber(context, effectId, 100 * factor, childId);

    private static string BuffNumber(StatusDescriptionContext context, string effectId, float scale, string childId = null)
    {
        var data = DataManager.Instance;
        if (data == null || !data.StatusEffectDict.TryGetValue(effectId, out var effect)) return null;
        var source = context.Status;
        if (childId != null)
        {
            source = source.BoundStatuses.Find(child => child?.IsActive == true && child.StatusID2 == childId);
            if (source == null) return "0";
        }
        if (!BuffExpressionEvaluator.TryResolveScalar(effect.Param1, source, out float value)
            && source.Caster?.CharacterCtrl?.TryResolveStatusLevelScalar(effect, out value) != true) return null;
        return (value * source.HexereiMultiplier * scale).ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 仅支持非负整数占位符，不执行表达式/代码。{{和}}表示字面花括号。
    /// 未定义、越界或格式不合法的占位符原样保留；每个参数在一次读取中最多取一次。
    /// </summary>
    public static string Format(string template, Func<int, string> argument)
    {
        if (string.IsNullOrEmpty(template)) return template ?? string.Empty;
        var result = new StringBuilder(template.Length);
        var values = new Dictionary<int, string>();
        for (int i = 0; i < template.Length; i++)
        {
            char current = template[i];
            if ((current == '{' || current == '}') && i + 1 < template.Length && template[i + 1] == current)
            { result.Append(current); i++; continue; }
            if (current == '{')
            {
                int end = i + 1;
                while (end < template.Length && template[end] >= '0' && template[end] <= '9') end++;
                if (end > i + 1 && end < template.Length && template[end] == '}'
                    && int.TryParse(template.Substring(i + 1, end - i - 1), NumberStyles.None,
                        CultureInfo.InvariantCulture, out int index))
                {
                    if (!values.TryGetValue(index, out string value))
                    { value = argument?.Invoke(index); values[index] = value; }
                    if (value == null) result.Append(template, i, end - i + 1);
                    else result.Append(value);
                    i = end;
                    continue;
                }
            }
            result.Append(current);
        }
        return result.ToString();
    }
}
