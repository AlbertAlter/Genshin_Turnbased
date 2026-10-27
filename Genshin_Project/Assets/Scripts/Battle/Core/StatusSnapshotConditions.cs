using System;
using System.Collections.Generic;

/// <summary>快照参数中的条件边界。未登记的新Buff钩子明确报错，不能猜成来源条件并永久锁错。</summary>
public static class StatusSnapshotConditions
{
    private static readonly HashSet<string> LiveTargetStatuses = new HashSet<string>(StringComparer.Ordinal);
    /// <summary>敌方标记投影/逐次消费增益不属于施法者面板；快照时跳过，实际命中仍读取当时的具体状态。</summary>
    public static void RegisterLiveTargetStatus(string statusID) => LiveTargetStatuses.Add(statusID);
    public static bool IsLiveTargetStatus(StatusInstance status)
        => status != null && LiveTargetStatuses.Contains(status.StatusID2);
    internal static bool HasLiveTargetPart(IEnumerable<string> parts)
    {
        var dm = DataManager.Instance;
        if (dm == null) return false;
        var requested = new HashSet<string>(parts, StringComparer.Ordinal);
        foreach (string id in LiveTargetStatuses)
            if (dm.StatusMainDict.TryGetValue(id, out var main) && requested.Contains(main.GetMultiplierPart())) return true;
        return false;
    }
    private static readonly HashSet<string> SourceNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "Check", "OnField", "OffField", "TeamBonus", "HeavenlyGiftBonus", "PaleFlameFull",
        "Bennett_Encouraged", "Bennett_LowHP", "Razor_LowEnergy", "Ningguang_ScreenActive", "Ningguang_ScreenFieldActive"
    };
    private static readonly HashSet<string> DeferredNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "Chance", "HitLanded", "Kaeya_C1", "Diluc_C1HP", "Razor_LowTargetHP",
        "Diluc_C6Damage", "Klee_SparkDamage", "1012_C2", "1012_C6Element"
    };
    private static readonly HashSet<string> SourceNumbers = Numbers(
        "4300301 4300701 4303402 4303601 4305001 4305103 4305202 4305203 4305702 " +
        "4307302 4307703 4309003 4309905 4310102 4311001 4311003 4312001 4312205 4313402 4314602 4314604 4315901 " +
        "4316001 4316603 4316703 4317505 4317804 4318701 4318902 4319003 4319205 4319303 " +
        "4319603 4319801 4320002 4320003 4320202 4320601 4320702 4321004 4321103 4321104 4321302 " +
        "4321804 432210105 4322304 4322404 4322405 431300201 431300202 431300203 450502 451802 452002 453202 " +
        "45460201 45460301 45460401 45460501 45460601 45460701 454903 455104 455501 455703 455704");
    private static readonly HashSet<string> DeferredNumbers = Numbers(
        "4300201 4300801 4301201 4301202 4301301 4301701 4302101 4302501 4303801 4304204 4305501 4309702 4310201 4318102 " +
        "4310601 450202 451402 451403 451502 451602 451902 452202 452302 " +
        "454102 455005 456004 456302 456304");
    private static HashSet<string> Numbers(string text) => new HashSet<string>(text.Split(' '), StringComparer.Ordinal);
    // 专用AP预备管线在真实出伤前才确定累计付款；它不是普通Count表达式的快照参数。
    internal static bool NeedsHitParameter(StatusEffectData effect)
        => effect?.EffectType == "Buff" && effect.StatusEffectID == 431810201 && effect.DamageHook == "Hook(431810201)";

    /// <summary>扩展Buff条件时显式登记依赖，避免快照按函数名、目标是否为空或结果真假猜测。</summary>
    public static void Register(string name, bool dependsOnHitOrTarget)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException(nameof(name));
        bool numbered = name.StartsWith("Hook:", StringComparison.Ordinal);
        string key = numbered ? name.Substring(5) : name;
        var selected = numbered ? (dependsOnHitOrTarget ? DeferredNumbers : SourceNumbers)
            : (dependsOnHitOrTarget ? DeferredNames : SourceNames);
        var opposite = numbered ? (dependsOnHitOrTarget ? SourceNumbers : DeferredNumbers)
            : (dependsOnHitOrTarget ? SourceNames : DeferredNames);
        if (opposite.Contains(key)) throw new InvalidOperationException("快照条件依赖重复且相互矛盾：" + name);
        selected.Add(key);
    }
    internal static bool Freeze(string hooks, ScriptHookContext context, out string deferred)
    {
        deferred = null;
        if (!ScriptHookParser.TryParseAll(hooks, out var calls)) throw new FormatException("快照Buff条件格式错误：" + hooks);
        var pending = new List<string>();
        foreach (var call in calls)
        {
            bool late;
            if (call.FunctionName == "Hook")
            {
                if (DeferredNumbers.Contains(call.Argument)) late = true;
                else if (SourceNumbers.Contains(call.Argument)) late = false;
                else throw Missing(call.Text);
            }
            else if (DeferredNames.Contains(call.FunctionName) || ScriptHookEvaluator.IsEventDriven(call.Text)) late = true;
            else if (SourceNames.Contains(call.FunctionName)) late = false;
            else throw Missing(call.Text);
            if (late) pending.Add(call.Text);
            else if (!ScriptHookEvaluator.Evaluate(call.Text, context)) return false;
        }
        deferred = string.Join(";", pending);
        return true;
    }
    private static Exception Missing(string hook)
        => new NotSupportedException("快照尚未定义该Buff条件的来源/目标边界：" + hook + "。请先为StatusSnapshotConditions登记依赖。");
    internal static bool EvaluateDeferred(string hooks, ScriptHookContext context, StatusInstance original)
    {
        if (!ScriptHookParser.TryParseAll(hooks, out var calls)) throw new FormatException(hooks);
        foreach (var call in calls)
        {
            // 消费资格按原完整调用帧管理；不复制出一份能冒领原资源的状态身份。
            var evaluation = context.Snapshot();
            if (call.FunctionName == "Klee_SparkDamage" || call.FunctionName == "Diluc_C6Damage" || call.FunctionName == "1012_C6Element"
                || (call.FunctionName == "Hook" && (call.Argument == "4310201" || call.Argument == "4301201"
                    || call.Argument == "4301202" || call.Argument == "4318102"))) evaluation.Status = original;
            if (!ScriptHookEvaluator.Evaluate(call.Text, evaluation)) return false;
        }
        return true;
    }
}
