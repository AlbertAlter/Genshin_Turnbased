using System;

/// <summary>540 潜光本身持有Count与4回合窗口；掉血重施只增点，不刷新窗口。</summary>
public sealed class VermillionCountHandler : CountStatusHandler
{
    public const string StatusID = "ST_Artifact_454003";
    public const float InitialValue = 0f;
    public const float MinimumValue = 0f;
    public const float MaximumValue = 4f;
    public const int Duration = 4;

    public override void OnCreated(StatusInstance status, object host)
    {
        RequireOwner(status, host);
        status.RuntimeValue = InitialValue;
        status.StackCount = 1;
        status.IsPermanent = false;
        status.RemainingPhaseCount = Duration;
        status.AddInPhase = 1;
        status.TriggerPhase = 0;
    }

    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host,
        CountStatusApplication application)
    {
        RequireOwner(status, host);
        status.RuntimeValue = Math.Min(MaximumValue, status.RuntimeValue + 1f);
        // Duration=0只是本次重施请求；不可把已有潜光改成永久或延长它及其绑定Buff。
        return status;
    }

    public override void OnRemoved(StatusInstance status, object host)
    { status.RuntimeValue = MinimumValue; }

    private static void RequireOwner(StatusInstance status, object host)
    {
        if (!(host is BattleEntity owner) || owner != status.Caster)
            throw new InvalidOperationException("潜光Count必须施加给穿戴者本人。");
    }
}
