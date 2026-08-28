using System;
using System.Collections.Generic;

/// <summary>扩散第二阶段的延迟写回事务；所有目标算完后才统一改变战场。</summary>
public sealed class ReactionTransaction
{
    private readonly List<Action> _writes = new List<Action>();
    public void Enqueue(Action write) { if (write != null) _writes.Add(write); }
    public void Commit() { foreach (Action write in _writes) write(); _writes.Clear(); }
}