using System;

/// <summary>535 苍白：真实战技命中后初值1，最多2；重施刷新整个2回合窗口。</summary>
public sealed class PaleFlameCountHandler : CountStatusHandler
{
    public const string StatusID = "ST_Artifact_453503";
    public const float InitialValue = 1f;
    public const float MinimumValue = 0f;
    public const float MaximumValue = 2f;
    public const int Duration = 2;

    public static void RegisterHooks()
    {
        BattleSpecialRules.RegisterCondition("PaleFlameFull", (argument, context) =>
        {
            if (!string.IsNullOrWhiteSpace(argument)) return false;
            StatusInstance count = context?.Caster?.GetStatus(StatusID);
            return count != null && count.IsActive && count.RuntimeValue >= MaximumValue;
        });
    }

    public override void OnCreated(StatusInstance status, object host)
    {
        RequireOwner(status, host);
        status.RuntimeValue = InitialValue;
        status.StackCount = 1;
        Refresh(status);
    }

    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host,
        CountStatusApplication application)
    {
        RequireOwner(status, host);
        status.RuntimeValue = Math.Min(MaximumValue, status.RuntimeValue + 1f);
        Refresh(status);
        return status;
    }

    private static void Refresh(StatusInstance status)
    {
        status.IsPermanent = false;
        status.RemainingPhaseCount = Duration;
        status.AddInPhase = 1;
        status.TriggerPhase = 0;
    }

    public override void OnRemoved(StatusInstance status, object host)
    { status.RuntimeValue = MinimumValue; }

    private static void RequireOwner(StatusInstance status, object host)
    {
        if (!(host is BattleEntity owner) || owner != status.Caster)
            throw new InvalidOperationException("苍白Count必须施加给穿戴者本人。");
    }
}
