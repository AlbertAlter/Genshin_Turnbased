using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>场景表现组件接口。完成指命中/必要收尾完成；跳字飘散不阻塞。回调必须在播放线程调用。</summary>
public interface IBattlePresentationView
{
    void Present(long playbackID, BattlePresentationCue cue, Action<Exception> completed);
    void Cancel(long playbackID);
}

/// <summary>Opt in to cosmetic launch cues. These are not additional battle results.</summary>
public interface IBattlePresentationAnticipationView : IBattlePresentationView { }

/// <summary>纯时间调度器；只展示快照，不调用战斗效果。FIFO批次、稳定同刻顺序、代次隔离迟到回调。</summary>
public sealed class BattlePresentationPlayback
{
    private sealed class Job
    {
        public long ID;
        public IReadOnlyList<BattlePresentationCue> Cues;
        public readonly TaskCompletionSource<bool> Done = new TaskCompletionSource<bool>();
        public CancellationTokenRegistration Registration;
        public volatile bool CancelRequested;
        public double Elapsed;
        public double TimeShift;
        public int Block = -1;
        public int Next, Waiting, Tails;
        public bool Closed;
    }
    private readonly Queue<Job> jobs = new Queue<Job>();
    private readonly Action<long, BattlePresentationCue, Action<Exception>> present;
    private readonly Action<long> cancel;
    private readonly int threadID = Thread.CurrentThread.ManagedThreadId;
    private Job active;
    private bool pumping;
    private long nextID;
    public bool IsBusy => active != null || jobs.Count != 0;

    public BattlePresentationPlayback(Action<long, BattlePresentationCue, Action<Exception>> present, Action<long> cancel)
    { this.present = present ?? throw new ArgumentNullException(nameof(present)); this.cancel = cancel; }

    public Task Enqueue(IReadOnlyList<BattlePresentationCue> cues, CancellationToken token = default)
    {
        CheckThread();
        if (cues == null) throw new ArgumentNullException(nameof(cues));
        var job = new Job { ID = ++nextID, Cues = cues };
        jobs.Enqueue(job);
        job.Registration = token.Register(() =>
        {
            job.CancelRequested = true;
            if (Thread.CurrentThread.ManagedThreadId == threadID) Advance(0);
        });
        if (job.Closed) job.Registration.Dispose();
        Advance(0);
        return job.Done.Task;
    }

    public void Advance(double deltaSeconds)
    {
        CheckThread();
        if (deltaSeconds < 0 || double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds))
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (pumping) return;
        pumping = true;
        try
        {
            // 本帧耗时只计入进入Advance时的那一批；新批次从0起步，避免低帧率时跳过整个后续效果。
            if (active != null) active.Elapsed += deltaSeconds;
            while (true)
            {
                if (active == null)
                {
                    if (jobs.Count == 0) break;
                    active = jobs.Dequeue();
                }
                Job job = active;
                if (job.CancelRequested) { Close(job, null, true); continue; }
                while (!job.Closed && !job.CancelRequested && job.Next < job.Cues.Count)
                {
                    BattlePresentationCue cue = job.Cues[job.Next];
                    if (cue.Block != job.Block)
                    {
                        if (job.Waiting > 0) break;
                        // 子效果/下一效果必须等前一组必要收尾；等待期间不吞掉后组自己的hit间隔。
                        if (job.Block >= 0) job.TimeShift = Math.Max(job.TimeShift, job.Elapsed - cue.Time);
                        job.Block = cue.Block;
                    }
                    if (cue.Time + job.TimeShift > job.Elapsed) break;
                    job.Next++;
                    // The banner runs over hits/voice. Only the next action waits for it to finish.
                    bool tail = cue.Result.Data.Kind == BattleEventKind.ActionPrompt;
                    if (tail) job.Tails++; else job.Waiting++;
                    bool signalled = false;
                    void Completed(Exception error)
                    {
                        CheckThread();
                        if (signalled || job.Closed) return;
                        signalled = true;
                        if (tail) job.Tails--; else job.Waiting--;
                        if (error != null) Close(job, error, false);
                        else if (!pumping) Advance(0);
                    }
                    try { present(job.ID, cue, Completed); }
                    catch (Exception error) { Close(job, error, false); }
                }
                if (job.Closed) continue;
                if (job.CancelRequested) { Close(job, null, true); continue; }
                if (job.Next == job.Cues.Count && job.Waiting == 0 && job.Tails == 0) { Close(job, null, false); continue; }
                break;
            }
        }
        finally { pumping = false; }
    }

    // 跳过表现不取消真实业务；清理等待中的组件并释放完成信号，绝不再次提交伤害。
    public void Skip()
    {
        CheckThread();
        Job current = active;
        Job[] pending = jobs.ToArray(); jobs.Clear();
        if (current != null) Close(current, null, false, true);
        foreach (Job job in pending) Close(job, null, false);
        Advance(0);
    }

    public void Cancel()
    {
        CheckThread();
        Job current = active;
        Job[] pending = jobs.ToArray(); jobs.Clear();
        if (current != null) Close(current, null, true);
        foreach (Job job in pending) Close(job, null, true);
    }

    private void Close(Job job, Exception error, bool cancelled, bool clearViews = false)
    {
        if (job.Closed) return;
        job.Closed = true;
        if (ReferenceEquals(active, job)) active = null;
        job.Registration.Dispose();
        if (cancelled || error != null || clearViews)
        {
            try { cancel?.Invoke(job.ID); }
            catch (Exception cleanup) { if (error == null && !cancelled) error = cleanup; }
        }
        if (cancelled) job.Done.TrySetCanceled();
        else if (error != null) job.Done.TrySetException(error);
        else job.Done.TrySetResult(true);
    }
    private void CheckThread()
    {
        if (Thread.CurrentThread.ManagedThreadId != threadID)
            throw new InvalidOperationException("表现调度与完成回调必须在同一主线程执行。");
    }
}
