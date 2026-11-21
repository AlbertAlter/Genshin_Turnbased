using System;
using Newtonsoft.Json;
using UnityEngine;

public static class RoguelikeSaveJson
{
    private static readonly JsonSerializerSettings SaveSettings = new JsonSerializerSettings
    {
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        DateParseHandling = DateParseHandling.None,
    };

    // Save DTOs distinguish an absent reward or route node from an empty inline object.
    private static bool IsProgressData(Type type) =>
        type == typeof(ActiveRunSaveData) || type == typeof(ChapterEntrySaveData) ||
        type == typeof(RoguelikeProgressSnapshot) || type == typeof(RoguelikeFlowData) || type == typeof(RoguelikeNodeSelection) ||
        type == typeof(RoguelikeBattleSetup) || type == typeof(WeaponLoadout) ||
        type == typeof(RoguelikeWishProgress) || type == typeof(RoguelikeWishPlanningData) ||
        type == typeof(RoguelikePendingNodeReward);

    public static string Serialize<T>(T value, bool prettyPrint = true) where T : class
    {
        return IsProgressData(typeof(T))
            ? JsonConvert.SerializeObject(value, prettyPrint ? Formatting.Indented : Formatting.None, SaveSettings)
            : JsonUtility.ToJson(value, prettyPrint);
    }

    public static T Deserialize<T>(string json) where T : class
    {
        return IsProgressData(typeof(T))
            ? JsonConvert.DeserializeObject<T>(json, SaveSettings)
            : JsonUtility.FromJson<T>(json);
    }

    public static T DeepClone<T>(T value) where T : class
    {
        if (value == null)
            return default(T);
        return Deserialize<T>(Serialize(value, false));
    }
}
