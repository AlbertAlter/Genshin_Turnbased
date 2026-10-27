/// <summary>
/// 562天之美赐：隐藏数值状态与显示状态共用元素读取。查询只读，切人不重施、
/// 不续时、不迁移状态；绑定时仅准备固定队伍拥有的元素。
/// </summary>
public static class HeavenlyGiftArtifactHooks
{
    public const string BaseDisplay = "ST_Artifact_456204";
    public const string OwnerDisplay = "ST_Artifact_456205";
    public const string ActiveDisplay = "ST_Artifact_456206";

    public static void RegisterConditions()
    {
        BattleSpecialRules.RegisterCondition("HeavenlyGiftMode", (argument, context) =>
            MatchesMode(BattleManager.Instance, argument));
        BattleSpecialRules.RegisterCondition("HeavenlyGiftOwnElement", (argument, context) =>
            IsElement(argument) && ElementOf(BattleManager.Instance, context?.Caster) == argument);
        BattleSpecialRules.RegisterCondition("HeavenlyGiftTeamElement", (argument, context) =>
            TeamHasElement(BattleManager.Instance, argument));
        BattleSpecialRules.RegisterCondition("HeavenlyGiftBonus", (argument, context) =>
            Applies(BattleManager.Instance, context?.Status, argument));
    }

    public static void RegisterDescriptions()
    {
        StatusDescriptionResolver.Register(BaseDisplay, OwnerArgument);
        StatusDescriptionResolver.Register(OwnerDisplay, OwnerArgument);
        StatusDescriptionResolver.Register(ActiveDisplay, (context, index) => index == 0
            ? ElementName(ActiveElement(context.Battle)) : null);
    }

    private static string OwnerArgument(StatusDescriptionContext context, int index)
        => index == 0 ? ElementName(ElementOf(context.Battle, context.Caster)) : null;

    public static bool Applies(BattleManager battle, StatusInstance status, string mode)
    {
        if (status == null || !status.IsActive || !MatchesMode(battle, mode)) return false;
        string element = status.MainData?.ApplyElementType;
        if (!IsElement(element)) return false;
        string owner = ElementOf(battle, status.Caster);
        if (!IsElement(owner)) return false;
        // 同一元素只有一个40%候选，OR判定，不把来源相同的两项相加成80%。
        return element == owner || (mode == "Ascended" && element == ActiveElement(battle));
    }

    public static bool MatchesMode(BattleManager battle, string mode)
    {
        var team = battle?.TeamBonuses;
        if (team == null || team.IsDisposed) return false;
        return mode == "Base" ? !team.HasHexerei : mode == "Ascended" && team.HasHexerei;
    }

    public static bool TeamHasElement(BattleManager battle, string element)
    {
        var team = battle?.TeamBonuses;
        return IsElement(element) && team != null && !team.IsDisposed && team.CountElement(element) > 0;
    }

    public static string ElementOf(BattleManager battle, BattleEntity actor)
    {
        var team = battle?.TeamBonuses;
        if (team == null || team.IsDisposed || !team.Contains(actor)) return string.Empty;
        string element = team.ElementOf(actor);
        return IsElement(element) ? element : string.Empty;
    }

    public static string ActiveElement(BattleManager battle)
    {
        var active = battle != null ? battle.GetActiveAlly() : null;
        return active != null && active.IsActive && active.Entity != null && active.Entity.IsAlive
            ? ElementOf(battle, active.Entity) : string.Empty;
    }

    private static bool IsElement(string element)
        => element == "Pyro" || element == "Hydro" || element == "Electro" || element == "Cryo"
            || element == "Anemo" || element == "Geo" || element == "Dendro";

    private static string ElementName(string element)
    {
        switch (element)
        {
            case "Pyro": return "火";
            case "Hydro": return "水";
            case "Electro": return "雷";
            case "Cryo": return "冰";
            case "Anemo": return "风";
            case "Geo": return "岩";
            case "Dendro": return "草";
            default: return "未确定"; // 没有有效前台时不猜元素，也不回退到穿戴者。
        }
    }
}
