using System;

/// <summary>公共机制状态身份；月笼实际挂格子，其余挂队伍。正式读表缺行必须报错。</summary>
public static class TeamPublicStatuses
{
    public const string VerdantDew = "VerdantDew";
    public const string Thundercloud = "Thundercloud";
    public const string PolarDomain = "PolarDomain";
    public const string MoonCage = "MoonCage";

    public static StatusMainData CreateDefinition(string id)
    {
        int number;
        string name;
        switch (id)
        {
            case VerdantDew: number = 40025; name = "草露"; break;
            case Thundercloud: number = 40026; name = "雷暴云"; break;
            case PolarDomain: number = 40027; name = "极星辉域"; break;
            case MoonCage: number = 40028; name = "月笼"; break;
            default: throw new ArgumentException("不是已定义的公共机制状态：" + id);
        }
        return new StatusMainData { StatusID = number, StatusID2 = id, StatusName = name,
            Display = id == MoonCage ? 1 : 0, MaxStack = id == VerdantDew ? 3 : 1, MaxCount = id == VerdantDew ? 3 : 1,
            WhenMax = id == VerdantDew || id == MoonCage ? "1" : "0" };
    }

    internal static StatusMainData Resolve(string id)
    {
        var expected = CreateDefinition(id);
        var data = DataManager.Instance;
        if (data == null) return expected; // 独立纯规则对象，不是正式加载失败的兜底。
        if (!data.StatusMainDict.TryGetValue(id, out var main))
            throw new InvalidOperationException("公共状态表缺少机制状态：" + id);
        if (main.StatusID != expected.StatusID || main.MaxStack != expected.MaxStack
            || main.MaxCount != expected.MaxCount || main.WhenMax != expected.WhenMax
            || CountStatusSystem.IsCount(main) || !string.IsNullOrEmpty(main.GetMultiplierPart()))
            throw new InvalidOperationException("公共机制状态的ID、实例上限或类型不符合专用规则：" + id);
        return main;
    }
}
