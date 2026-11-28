using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 显式暂停持有者共用入口。只冻结战斗时钟与执行边界，不改 Time.timeScale，菜单和设置保持可操作。
/// 关闭者只释放自己的持有；新表现模块的自动时钟使用 DeltaTime / UnscaledTime。
/// </summary>
public static class BattlePause
{
    private static readonly HashSet<object> owners = new();
    private static TaskCompletionSource<bool> resumed;
    private static double pausedAt, pausedSeconds;
    private static int resumedFrame = -1;
    public static bool IsPaused => owners.Count > 0;
    public static bool BlocksInput => IsPaused || resumedFrame == Time.frameCount;
    public static float DeltaTime => IsPaused ? 0 : Time.unscaledDeltaTime;
    public static float ScaledDeltaTime => IsPaused ? 0 : Time.deltaTime;
    public static double UnscaledTimeAsDouble => (IsPaused ? pausedAt : Time.unscaledTimeAsDouble) - pausedSeconds;
    public static float UnscaledTime => (float)UnscaledTimeAsDouble;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        owners.Clear(); pausedAt = pausedSeconds = 0; resumedFrame = -1;
        var pending = resumed; resumed = null; pending?.TrySetCanceled();
    }

    public static void SetPaused(object owner, bool paused)
    {
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        if (paused)
        {
            if (!owners.Add(owner) || owners.Count > 1) return;
            pausedAt = Time.unscaledTimeAsDouble;
            resumed = new TaskCompletionSource<bool>();
        }
        else
        {
            if (!owners.Remove(owner) || owners.Count > 0) return;
            pausedSeconds += Math.Max(0, Time.unscaledTimeAsDouble - pausedAt);
            resumedFrame = Time.frameCount;
            var pending = resumed; resumed = null; pending?.TrySetResult(true);
        }
    }

    /// <summary>业务只在原有异步边界等待，不取消或重做已经提交的效果；取消战斗仍可立即打断等待。</summary>
    public static async Task WaitUntilResumed(CancellationToken cancellation = default)
    {
        while (IsPaused)
        {
            cancellation.ThrowIfCancellationRequested();
            var pending = resumed.Task;
            if (!cancellation.CanBeCanceled) { await pending; continue; }
            var cancelled = new TaskCompletionSource<bool>();
            using (cancellation.Register(() => cancelled.TrySetCanceled()))
            { Task finished = await Task.WhenAny(pending, cancelled.Task); await finished; }
        }
        cancellation.ThrowIfCancellationRequested();
    }

    public static IEnumerator WaitForSeconds(float seconds)
    {
        float elapsed = 0;
        while (IsPaused || elapsed < seconds)
        { elapsed += ScaledDeltaTime; yield return null; }
    }
}
