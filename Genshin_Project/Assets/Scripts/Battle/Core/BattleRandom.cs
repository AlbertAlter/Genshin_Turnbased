using System;

/// <summary>
/// 战斗随机数来源。所有会影响战斗结果的随机逻辑都通过此接口取数，
/// 以便战斗、测试和未来的种子/步数恢复共用同一入口。
/// </summary>
public interface IBattleRandomSource
{
    int NextInt(int minInclusive, int maxExclusive);
    float NextFloat01();
}

/// <summary>默认战斗随机源；可用固定 seed 构造以复现完整随机序列。</summary>
public sealed class SystemBattleRandomSource : IBattleRandomSource
{
    private readonly System.Random _random;

    public long Step { get; private set; }

    public SystemBattleRandomSource()
        : this(new System.Random())
    {
    }

    public SystemBattleRandomSource(int seed)
        : this(new System.Random(seed))
    {
    }

    private SystemBattleRandomSource(System.Random random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "随机整数上限必须大于下限。");

        Step++;
        return _random.Next(minInclusive, maxExclusive);
    }

    public float NextFloat01()
    {
        Step++;
        return (float)_random.NextDouble();
    }
}

/// <summary>
/// 项目的统一随机入口。测试可以注入序列源；战斗重置时恢复默认随机源。
/// 只有玩法规则明确要求隔离随机序列时，才应另建独立随机流。
/// </summary>
public static class BattleRandom
{
    private static IBattleRandomSource _source = new SystemBattleRandomSource();

    public static IBattleRandomSource Source => _source;

    public static void SetSource(IBattleRandomSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public static void ResetSource()
    {
        _source = new SystemBattleRandomSource();
    }

    public static int NextInt(int minInclusive, int maxExclusive)
    {
        return _source.NextInt(minInclusive, maxExclusive);
    }

    public static float NextFloat01()
    {
        return _source.NextFloat01();
    }
}
