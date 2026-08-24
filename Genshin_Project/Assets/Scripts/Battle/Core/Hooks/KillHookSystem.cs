using System.Collections.Generic;

/// <summary>
/// Kill 钩子系统（2026-08-19，非凯亚专属）：
/// 登记带 OnTrigger + ScriptHook=Kill(...) 行的状态实例（按实际状态实例登记，不能只用 StatusID2 作唯一键），
/// 在死亡声明（DamageResolvedEvent.CausedDeath）时按状态 ApplyOrder 从小到大触发。
/// 生命周期接入：状态施加（实体/场地）/ 移除 / 到期 / 覆盖重建 / 父状态连带移除 / 战斗重置。
/// </summary>
public static class KillHookSystem
{
    private class KillHookEntry
    {
        public StatusInstance Status;
        public object Host;
        public StatusActionData TriggerLine;
    }

    private static readonly List<KillHookEntry> _entries = new List<KillHookEntry>();

    /// <summary>
    /// 登记：状态施加成功时调用（实体/位置通用）。
    /// 只登记 ActionType=OnTrigger 且 ScriptHook=Kill(...) 的行；同一状态可登记多条 Kill 行。
    /// </summary>
    public static void Register(StatusInstance status, object host)
    {
        if (status == null) return;
        var dm = DataManager.Instance;
        if (dm == null || !dm.StatusActionDict.TryGetValue(status.StatusID2, out var actions)) return;

        bool registered = false;
        foreach (var act in actions)
        {
            if (act.ActionType != "OnTrigger") continue;
            if (!ScriptHookParser.TryParse(act.ScriptHook, out string fn, out _) || fn != "Kill") continue;
            _entries.Add(new KillHookEntry { Status = status, Host = host, TriggerLine = act });
            registered = true;
        }
        if (registered)
            LogManager.Log(LogCategory.ScriptHook, $"KillHookSystem 登记 {status.StatusID2}（施放者 {status.Caster?.EntityID}）");
    }

    /// <summary>注销：状态以任意形式消失（移除/到期/覆盖重建/父状态连带移除）时调用，按实例引用移除。</summary>
    public static void Unregister(StatusInstance status)
    {
        if (status == null || _entries.Count == 0) return;
        _entries.RemoveAll(e => ReferenceEquals(e.Status, status));
    }

    /// <summary>
    /// 死亡声明入口：只在 CausedDeath 时检查，按状态 ApplyOrder 从小到大触发。
    /// 遍历登记表快照：触发过程中可能移除/注销状态。
    /// 同一已经死亡的目标再次受击（CausedDeath=false）不会再次进入本方法。
    /// </summary>
    public static void NotifyDeath(DamageResolvedEvent damageEvent)
    {
        if (damageEvent == null || !damageEvent.CausedDeath) return;
        if (_entries.Count == 0) return;

        var snapshot = new List<KillHookEntry>(_entries);
        snapshot.Sort((a, b) => a.Status.ApplyOrder.CompareTo(b.Status.ApplyOrder));
        foreach (var entry in snapshot)
        {
            if (entry.Status == null || entry.TriggerLine == null) continue;
            if (entry.Status.Caster == null || entry.Status.Caster.CharacterCtrl == null) continue;

            var context = new ScriptHookContext
            {
                Caster = entry.Status.Caster,
                Target = damageEvent.Target,
                Status = entry.Status,
                StatusHost = entry.Host,
                HitEffectID = damageEvent.Source != null ? damageEvent.Source.SourceEffectID : null,
                DamageEvent = damageEvent,
                ActionTargetPositions = BattleManager.Instance != null
                    ? BattleManager.Instance.PendingActionTargetPositions
                    : null
            };
            // Kill 参数匹配 + 次数限制 + 执行统一走施放者控制器入口
            entry.Status.Caster.CharacterCtrl.TryExecuteEventStatusAction(
                entry.Status, entry.TriggerLine, entry.Host, context);
        }
    }

    /// <summary>
    /// Kill 参数匹配（供 ScriptHookEvaluator 求值使用）：
    ///   Kill(1010)    数字参数   → 匹配来源实体数字ID
    ///   Kill(SK_xxx)  技能参数   → 匹配来源技能ID2
    ///   Kill(SE_xxx) / Kill(STE_xxx) 效果参数 → 匹配来源效果ID2
    /// 空参数或无法识别的格式返回假并记录明确警告。
    /// </summary>
    public static bool MatchesKillArgument(string argument, DamageSourceInfo source)
    {
        if (string.IsNullOrEmpty(argument) || source == null)
        {
            LogManager.LogWarning(LogCategory.ScriptHook, $"Kill 参数为空或来源缺失: Kill({argument})，视为不触发");
            return false;
        }
        string arg = argument.Trim();
        if (int.TryParse(arg, out int entityID))
            return source.SourceEntityID == entityID;
        if (arg.StartsWith("SK_"))
            return source.SourceSkillID == arg;
        if (arg.StartsWith("SE_") || arg.StartsWith("STE_"))
            return source.SourceEffectID == arg;

        LogManager.LogWarning(LogCategory.ScriptHook, $"无法识别的 Kill 参数格式: Kill({arg})，视为不触发");
        return false;
    }

    /// <summary>战斗重置（BattleManager.ResetBattle）时清空全部登记，防止上一场残留。</summary>
    public static void ClearAll()
    {
        if (_entries.Count == 0) return;
        _entries.Clear();
        LogManager.Log(LogCategory.ScriptHook, "KillHookSystem 清空全部登记");
    }

    /// <summary>当前登记条数（测试断言用）。</summary>
    public static int EntryCount => _entries.Count;
}
