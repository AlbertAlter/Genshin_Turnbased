using System;
using System.Collections.Generic;

/// <summary>Immutable visual markers relative to one effect execution. Never determines actual hit count.</summary>
public sealed class BattleHitTemplate
{
    public string ID { get; }
    public double RepeatInterval { get; }
    public double ProjectileSeconds { get; }
    private readonly double[] times;
    public BattleHitTemplate(string id, IEnumerable<double> markers, double repeatInterval, double projectileSeconds = 0)
    {
        ID = id; times = new List<double>(markers).ToArray();
        bool Valid(double n) => !double.IsNaN(n) && !double.IsInfinity(n) && n >= 0;
        if (string.IsNullOrWhiteSpace(id) || times.Length == 0 || !Valid(repeatInterval) || !Valid(projectileSeconds))
            throw new ArgumentException("命中模板需要编号、时点和有限非负的间隔。");
        for (int i = 0; i < times.Length; i++)
            if (!Valid(times[i]) || (i > 0 && times[i] < times[i - 1])) throw new ArgumentException("命中时点须为非负递增序列。");
        if (projectileSeconds > times[0]) throw new ArgumentException("首个命中时点不能早于飞行段结束。");
        RepeatInterval = repeatInterval; ProjectileSeconds = projectileSeconds;
    }
    public double HitTime(int index)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        return index < times.Length ? times[index] : times[times.Length - 1] + (index - times.Length + 1) * RepeatInterval;
    }
    public BattleHitTemplate WithUniformInterval(double interval) => new BattleHitTemplate(ID, new[] { times[0] }, interval, ProjectileSeconds);
}
