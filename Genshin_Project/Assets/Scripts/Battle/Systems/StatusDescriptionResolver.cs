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
    public BattleEntity Caster => Status?.Caster;

    public StatusDescriptionContext(StatusInstance status, object host, BattleManager battle)
    { Status = status; Host = host; Battle = battle; }
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
    }

    /// <summary>按状态ID2注册只读参数提供器。返回null表示参数未定义，保留原占位符。</summary>
    public static void Register(string statusID, Func<StatusDescriptionContext, int, string> provider)
    {
        if (string.IsNullOrWhiteSpace(statusID)) throw new ArgumentException("状态ID不能为空。", nameof(statusID));
        if (provider == null) throw new ArgumentNullException(nameof(provider));
        Providers.Add(statusID, provider);
    }

    public static string Resolve(StatusInstance status, object host = null, BattleManager battle = null)
    {
        string template = status?.MainData?.Description ?? string.Empty;
        if (status == null || string.IsNullOrEmpty(status.StatusID2)
            || !Providers.TryGetValue(status.StatusID2, out var provider))
            return Format(template, null);
        var context = new StatusDescriptionContext(status, host, battle ?? BattleManager.Instance);
        return Format(template, index => provider(context, index));
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
