using UnityEngine;

public static class RoguelikeSaveJson
{
    public static string Serialize<T>(T value, bool prettyPrint = true) where T : class
    {
        return JsonUtility.ToJson(value, prettyPrint);
    }

    public static T Deserialize<T>(string json) where T : class
    {
        return JsonUtility.FromJson<T>(json);
    }

    public static T DeepClone<T>(T value) where T : class
    {
        if (value == null)
            return default(T);
        return Deserialize<T>(Serialize(value, false));
    }
}
