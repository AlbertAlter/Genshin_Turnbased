using System;

/// <summary>全队共享草露，整数0～3；不是任何角色的Count，不因天赋提供者阵亡而清空余额。</summary>
public sealed class VerdantDewPool
{
    public const string ResourceID = "VerdantDew";
    public const int Capacity = 3;
    private readonly TeamBonusSystem team;
    // 唯一余额是队伍宿主中的真实实例数，不再维护另一个int副本。
    public int Value => team.Host.GetStatusInstances(ResourceID).FindAll(s => s.IsActive).Count;
    internal VerdantDewPool(TeamBonusSystem team) { this.team = team; }

    public int Gain(int amount, BattleEntity actor)
    {
        Validate(amount, actor);
        if (amount == 0) return 0;
        using var resolution = BattleEventHub.BeginResolution();
        using var collection = BattleResolutionContext.BeginCollection();
        int before = Value;
        int added = (int)Math.Min(Capacity - before, (long)amount);
        StatusMainData token = added > 0 ? TeamPublicStatuses.Resolve(TeamPublicStatuses.VerdantDew) : null;
        for (int i = 0; i < added; i++) team.Host.AddManagedStatus(ResourceID, actor, 0, team.Phase, token);
        Declare(before, amount, ResourceChangeReason.Gain, actor);
        return Value - before;
    }

    /// <summary>整笔支付；余额不足不改变数值、不发布成功消耗。0请求成功但不声明。</summary>
    public bool TrySpend(int amount, BattleEntity actor)
    {
        Validate(amount, actor);
        if (amount > Value) return false;
        if (amount == 0) return true;
        using var resolution = BattleEventHub.BeginResolution();
        using var collection = BattleResolutionContext.BeginCollection();
        int before = Value;
        var tokens = team.Host.GetStatusInstances(ResourceID).FindAll(s => s.IsActive);
        for (int i = 0; i < amount; i++) team.Host.SpendResourceStatus(tokens[i]);
        Declare(before, -amount, ResourceChangeReason.Spend, actor);
        return true;
    }

    private void Validate(int amount, BattleEntity actor)
    {
        if (team.IsDisposed || team.Host.IsDisposed) throw new InvalidOperationException("不能操作已结束会话的草露池。");
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (!team.Contains(actor)) throw new InvalidOperationException("草露操作来源必须属于本场编队。");
    }

    private void Declare(int before, int requested, ResourceChangeReason reason, BattleEntity actor)
        => BattleEventHub.Publish(new ResourceEventData(ResourceID, ResourceID, null, before,
            Value, requested, reason, true, team, actor));

    // 构筑初值为0；重置不冒充战斗内资源消耗。
    internal void Reset() { team.Host.ClearManagedStatuses(ResourceID); }
}
