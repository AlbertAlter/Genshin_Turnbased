using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>同一来源效果在本阶段自然到期的状态，共用一次调度和表现序列。</summary>
internal static class StatusExpiryBatch
{
    internal static Task Run(List<(StatusInstance inst, object host, List<StatusActionData> actions)> members,
        Action<(StatusInstance inst, object host)> removeExpired, BattleStatusPromptOccurrence prompts)
        => StatusActionDispatch.ExecuteBatchAsync(members, "OnEnd", prompts,
            BattleStatusPhaseScheduler.IsDue, removeExpired);
}
