using System.Collections.Generic;

/// <summary>
/// PreAlliesDamage / PreSelfDamage 事件钩子登记与触发。
/// 是否属于主动行为统一查询 ActiveActionQuery；本类只处理钩子范围和执行顺序。
/// </summary>
public static class PreDamageHookSystem
{
    private sealed class PreDamageHookEntry
    {
        public string StatusID2;
        public BattleEntity Caster;
        public object Host;
        public string EventHookName;
        public StatusActionData TriggerLine;
        public readonly List<StatusInstance> Instances = new List<StatusInstance>();
    }

    private static readonly List<PreDamageHookEntry> Entries = new List<PreDamageHookEntry>();

    /// <summary>
    /// 登记带 PreAlliesDamage / PreSelfDamage 的 OnTrigger 行。
    /// Allies 按状态ID、施放者和行动行去重；Self 额外按宿主区分。
    /// </summary>
    public static void RegisterPreDamageHook(StatusInstance instance, object host)
    {
        if (instance == null || instance.MainData == null) return;
        if (instance.Caster == null || instance.Caster.CharacterCtrl == null) return;
        DataManager dm = DataManager.Instance;
        if (dm == null || !dm.StatusActionDict.TryGetValue(instance.StatusID2, out List<StatusActionData> actions))
            return;

        foreach (StatusActionData action in actions)
        {
            if (action.ActionType != "OnTrigger") continue;
            string eventHookName;
            if (ScriptHookParser.TryFind(action.ScriptHook, "PreAlliesDamage", out _))
                eventHookName = "PreAlliesDamage";
            else if (ScriptHookParser.TryFind(action.ScriptHook, "PreSelfDamage", out _))
                eventHookName = "PreSelfDamage";
            else
                continue;

            PreDamageHookEntry entry = FindEntry(instance, host, action, eventHookName);
            if (entry == null)
            {
                entry = new PreDamageHookEntry
                {
                    StatusID2 = instance.StatusID2,
                    Caster = instance.Caster,
                    Host = host,
                    EventHookName = eventHookName,
                    TriggerLine = action
                };
                Entries.Add(entry);
            }
            if (!entry.Instances.Contains(instance)) entry.Instances.Add(instance);
        }
    }

    public static void UnregisterPreDamageHook(StatusInstance instance)
    {
        if (instance == null) return;
        for (int index = Entries.Count - 1; index >= 0; index--)
        {
            PreDamageHookEntry entry = Entries[index];
            entry.Instances.Remove(instance);
            if (entry.Instances.Count == 0) Entries.RemoveAt(index);
        }
    }

    /// <summary>
    /// 主动行为目标预解析完成后触发。Allies 响应任意我方行动者；Self 只响应状态实体宿主或场地占用者自身。
    /// </summary>
    public static void TriggerPreDamageHooks(BattleEntity activeActor)
    {
        if (activeActor == null || Entries.Count == 0) return;
        var snapshot = new List<PreDamageHookEntry>(Entries);
        snapshot.Sort((left, right) => GetApplyOrder(left).CompareTo(GetApplyOrder(right)));

        foreach (PreDamageHookEntry entry in snapshot)
        {
            if (entry.EventHookName == "PreSelfDamage" && !IsHostedByActor(entry.Host, activeActor))
                continue;

            StatusInstance status = GetActiveInstance(entry);
            if (status == null || status.Caster == null || status.Caster.CharacterCtrl == null) continue;
            ScriptHookParser.TryFind(entry.TriggerLine.ScriptHook, entry.EventHookName, out string argument);
            object executionHost = entry.EventHookName == "PreSelfDamage" ? entry.Host : status.Caster;
            status.Caster.CharacterCtrl.TryExecuteEventStatusAction(
                status,
                entry.TriggerLine,
                executionHost,
                new ScriptHookContext
                {
                    Caster = status.Caster,
                    Target = activeActor,
                    Status = status,
                    StatusHost = executionHost,
                    EventHookName = entry.EventHookName,
                    EventHookArgument = argument,
                    ActionTargetPositions = BattleManager.Instance != null
                        ? BattleManager.Instance.PendingActionTargetPositions
                        : null
                });
        }
    }

    public static bool HasPreDamageHook(string statusID2)
    {
        if (string.IsNullOrEmpty(statusID2)) return false;
        foreach (PreDamageHookEntry entry in Entries)
            if (entry.StatusID2 == statusID2 && GetActiveInstance(entry) != null) return true;
        return false;
    }

    public static void ClearAll()
    {
        Entries.Clear();
    }

    private static PreDamageHookEntry FindEntry(
        StatusInstance instance,
        object host,
        StatusActionData action,
        string eventHookName)
    {
        foreach (PreDamageHookEntry entry in Entries)
        {
            if (entry.StatusID2 != instance.StatusID2
                || entry.Caster != instance.Caster
                || entry.TriggerLine != action
                || entry.EventHookName != eventHookName)
                continue;
            if (eventHookName == "PreSelfDamage" && !ReferenceEquals(entry.Host, host)) continue;
            return entry;
        }
        return null;
    }

    private static StatusInstance GetActiveInstance(PreDamageHookEntry entry)
    {
        if (entry == null) return null;
        foreach (StatusInstance instance in entry.Instances)
            if (instance != null && instance.IsActive) return instance;
        return null;
    }

    private static long GetApplyOrder(PreDamageHookEntry entry)
    {
        StatusInstance instance = GetActiveInstance(entry);
        return instance != null ? instance.ApplyOrder : long.MaxValue;
    }

    private static bool IsHostedByActor(object host, BattleEntity actor)
    {
        if (host is BattleEntity entity) return entity == actor;
        if (host is FieldPosition field) return field.Occupant == actor;
        return false;
    }
}
