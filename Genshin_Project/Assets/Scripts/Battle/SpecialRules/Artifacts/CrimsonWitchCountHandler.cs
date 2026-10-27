using System;

/// <summary>523 魔女：战技后初值1，最多3；重施增加1并刷新整个3回合窗口。</summary>
public sealed class CrimsonWitchCountHandler : CountStatusHandler
{
    public const string StatusID = "ST_Artifact_452305";
    public const float InitialValue = 1f;
    public const float MinimumValue = 0f;
    public const float MaximumValue = 3f;
    public const int Duration = 3;

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
        Refresh(status); // 满3也刷新，不重建为1，不另开计时器。
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
            throw new InvalidOperationException("魔女Count必须施加给穿戴者本人。");
    }
}
