/// <summary>角色具备的始基力标记；由角色配表初始化，不推测国别、附魔或当前伤害元素。</summary>
public static class ArkheStatuses
{
    public const string Pneuma = "ST_Arkhe_Pneuma"; // 公共状态40036，芒。
    public const string Ousia = "ST_Arkhe_Ousia";   // 公共状态40037，荒。

    public static bool IsMarker(string id) => id == Pneuma || id == Ousia;

    public static bool HasPneuma(BattleEntity entity) => entity?.HasStatus(Pneuma) == true;
    public static bool HasOusia(BattleEntity entity) => entity?.HasStatus(Ousia) == true;
    public static bool HasAny(BattleEntity entity) => HasPneuma(entity) || HasOusia(entity);
}
