using System;

/// <summary>538 华馆问答。只有本处理器规定0～4、获层、阶段衰减；Buff 只读 RuntimeValue。</summary>
public sealed class HuskCountHandler : CountStatusHandler
{
    public const string StatusID = "ST_Artifact_453805";
    public const float InitialValue = 0f;
    public const float MinimumValue = 0f;
    public const float MaximumValue = 4f;

    private sealed class Timing
    {
        public int LastGainTurn;
        public int LastDecayTurn;
        public int LastTickTurn = -1;
    }

    public static void RegisterHooks()
    {
        BattleSpecialRules.RegisterCondition("HuskOnField", (argument, context) =>
            string.IsNullOrWhiteSpace(argument) && context?.Caster?.CharacterCtrl != null && context.Caster.IsAlive
            && context.Caster.CharacterCtrl.IsActive);
    }

    public override void OnCreated(StatusInstance status, object host)
    {
        RequireOwner(status, host);
        int turn = BattleManager.Instance != null ? BattleManager.Instance.TurnCount : 0;
        status.RuntimeValue = InitialValue;
        status.StackCount = 1;
        status.CountRuntimeState = new Timing { LastGainTurn = turn, LastDecayTurn = turn };
        status.AddInPhase = 1;
        status.TriggerPhase = 1;
    }

    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host,
        CountStatusApplication application)
    {
        RequireOwner(status, host);
        Gain(status);
        return status;
    }

    private static void Gain(StatusInstance status)
    {
        var timing = (Timing)status.CountRuntimeState;
        int turn = BattleManager.Instance != null ? BattleManager.Instance.TurnCount : 0;
        status.RuntimeValue = Math.Min(MaximumValue, status.RuntimeValue + 1f);
        // 满值继续满足获层条件也重置未获层计时，不重建实例或改变 ApplyOrder。
        timing.LastGainTurn = turn;
        timing.LastDecayTurn = turn;
    }

    public override void OnItsTurn(StatusInstance status, object host)
    {
        BattleEntity owner = RequireOwner(status, host);
        if (!owner.IsAlive || !ReferenceEquals(owner.GetStatus(StatusID), status)) return;
        var timing = (Timing)status.CountRuntimeState;
        int turn = BattleManager.Instance != null ? BattleManager.Instance.TurnCount : 0;
        if (timing.LastTickTurn == turn) return;
        timing.LastTickTurn = turn;
        if (!owner.CharacterCtrl.IsActive) Gain(status);
        else if (turn - Math.Max(timing.LastGainTurn, timing.LastDecayTurn) >= 2)
        {
            status.RuntimeValue = Math.Max(MinimumValue, status.RuntimeValue - 1f);
            timing.LastDecayTurn = turn;
        }
        // 为0仍保留本初始化状态，之后可以重新获层；不建立通用归零移除规则。
    }

    public override void OnRemoved(StatusInstance status, object host)
    {
        status.RuntimeValue = InitialValue;
        status.CountRuntimeState = null;
    }

    private static BattleEntity RequireOwner(StatusInstance status, object host)
    {
        var owner = host as BattleEntity;
        if (owner == null || owner.CharacterCtrl == null || status.Caster != owner)
            throw new InvalidOperationException("华馆问答 Count 必须属于穿戴者本人。");
        return owner;
    }
}
