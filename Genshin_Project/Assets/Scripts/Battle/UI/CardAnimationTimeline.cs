using System;

/// <summary>Original variable frame durations. Sampling limits do not alter animation speed.</summary>
public sealed class CardAnimationTimeline
{
    private readonly double[] ends;
    public int Count => ends.Length;
    public double Duration => ends[ends.Length - 1];
    public CardAnimationTimeline(float[] seconds)
    {
        if (seconds == null || seconds.Length == 0) throw new ArgumentException("Animation needs frames.");
        ends = new double[seconds.Length];
        double end = 0;
        for (int i = 0; i < seconds.Length; i++)
        {
            if (seconds[i] <= 0 || float.IsNaN(seconds[i]) || float.IsInfinity(seconds[i]))
                throw new ArgumentException("Animation frame duration must be positive and finite.");
            end += seconds[i]; ends[i] = end;
        }
    }
    public double Start(int index) => index <= 0 ? 0 : ends[Math.Min(index, Count) - 1];
    public int FrameAt(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return 0;
        double time = seconds % Duration;
        if (time < 0) time += Duration;
        int low = 0, high = ends.Length - 1;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (time < ends[mid]) high = mid; else low = mid + 1;
        }
        return low;
    }
}
