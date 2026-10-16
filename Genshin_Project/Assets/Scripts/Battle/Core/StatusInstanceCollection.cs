using System;
using System.Collections.Generic;

/// <summary>
/// 同一配置 ID 可以保存多份真实实例。旧的按 ID 查询返回最早一份；生命周期使用实例引用。
/// 索引器赋值仅供脚本管理的单例状态及旧数据入口，普通施加必须使用 Add。
/// </summary>
public sealed class StatusInstanceCollection
{
    private readonly Dictionary<string, List<StatusInstance>> _byID =
        new Dictionary<string, List<StatusInstance>>(StringComparer.Ordinal);

    public IEnumerable<StatusInstance> Values
    {
        get
        {
            foreach (var group in _byID.Values)
                foreach (StatusInstance status in group) yield return status;
        }
    }

    public StatusInstance this[string id]
    {
        get
        {
            if (!TryGetValue(id, out StatusInstance status)) throw new KeyNotFoundException(id);
            return status;
        }
        set
        {
            if (value == null) { _byID.Remove(id); return; }
            if (string.IsNullOrEmpty(value.StatusID2)) value.StatusID2 = id;
            if (value.StatusID2 != id) throw new ArgumentException("状态 ID 与容器键不一致。", nameof(value));
            _byID[id] = new List<StatusInstance> { value };
        }
    }

    public bool TryGetValue(string id, out StatusInstance status)
    {
        status = null;
        if (id == null || !_byID.TryGetValue(id, out var group)) return false;
        foreach (StatusInstance candidate in group)
            if (status == null || candidate.ApplyOrder < status.ApplyOrder) status = candidate;
        return status != null;
    }

    public bool ContainsKey(string id) => id != null && _byID.ContainsKey(id);

    public bool Contains(StatusInstance status) => status != null
        && _byID.TryGetValue(status.StatusID2, out var group) && group.Contains(status);

    public List<StatusInstance> GetAll(string id)
    {
        var result = id != null && _byID.TryGetValue(id, out var group)
            ? new List<StatusInstance>(group) : new List<StatusInstance>();
        result.Sort((left, right) => left.ApplyOrder.CompareTo(right.ApplyOrder));
        return result;
    }

    public void Add(StatusInstance status)
    {
        if (status == null || string.IsNullOrEmpty(status.StatusID2))
            throw new ArgumentException("状态实例必须有 ID。", nameof(status));
        if (!_byID.TryGetValue(status.StatusID2, out var group))
            _byID.Add(status.StatusID2, group = new List<StatusInstance>());
        if (!group.Contains(status)) group.Add(status);
    }

    public bool Remove(StatusInstance status)
    {
        if (status == null || !_byID.TryGetValue(status.StatusID2, out var group)
            || !group.Remove(status)) return false;
        if (group.Count == 0) _byID.Remove(status.StatusID2);
        return true;
    }

    // 原始容器操作不执行生命周期；正式玩法移除走 BattleEntity.RemoveStatus。
    public bool Remove(string id) => id != null && _byID.Remove(id);
    public void Clear() => _byID.Clear();
}

/// <summary>一次属性加总内，同 ID 的所有实例共同受 MaxCount 限制，不改变实例或 Count 数值。</summary>
public static class StatusAggregation
{
    public static int Take(StatusInstance status, Dictionary<string, int> used)
    {
        if (status == null || !status.IsActive || status.MainData == null) return 0;
        int count = Math.Max(1, status.StackCount);
        int cap = status.MainData.MaxCount;
        if (cap <= 0) return count;
        string id = status.StatusID2 ?? status.MainData.StatusID.ToString();
        used.TryGetValue(id, out int previous);
        count = Math.Min(count, Math.Max(0, cap - previous));
        used[id] = previous + count;
        return count;
    }
}
