using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>同一来源效果在本阶段自然到期的状态，共用一次调度和表现序列。</summary>
internal static class StatusExpiryBatch
{
    internal static Task Run(List<(StatusInstance inst, object host, List<StatusActionData> actions)> members,
        Action<(StatusInstance inst, object host)> removeExpired, BattleStatusPromptOccurrence prompts)
        => BattleExecution.Run(() => Execute(members, removeExpired, prompts));

    private static async Task Execute(List<(StatusInstance inst, object host, List<StatusActionData> actions)> members,
        Action<(StatusInstance inst, object host)> removeExpired, BattleStatusPromptOccurrence prompts)
    {
        // 调度器已按 ApplyOrder 排序。每个实例运行自己的行动，再处理下一个。
        foreach (var item in members)
        {
            BattleExecution.ThrowIfCancelled();
            var member = (item.inst, item.host);
            if (!BattleStatusPhaseScheduler.IsDue(member)) continue;
            await StatusActionDispatch.ExecuteTask(item.inst, item.actions, item.host, "OnEnd", prompts);
            removeExpired(member);
            await BattleExecution.Flush();
        }
    }
}
