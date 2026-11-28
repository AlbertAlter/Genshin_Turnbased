using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Publish real Unity object references; never guess FBX local fileIDs or alter scene layouts.</summary>
[InitializeOnLoad]
public sealed class BattleNativeEffectCatalogBuilder : AssetPostprocessor, IPreprocessBuildWithReport
{
    private const string Path = "Assets/Resources/UI/BattleNativeEffects.asset";
    private static bool queued, publishing;
    public int callbackOrder => 0;
    static BattleNativeEffectCatalogBuilder()
    {
        Queue();
        EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingEditMode) Publish(); };
    }
    private static void Queue()
    {
        if (queued) return;
        queued = true; EditorApplication.delayCall += PublishWhenReady;
    }
    private static void PublishWhenReady()
    {
        queued = false;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { Queue(); return; }
        try { Publish(); } catch (Exception error) { Debug.LogError("原生特效资源发布失败：" + error.Message); }
    }
    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] from)
    {
        if (publishing) return;
        foreach (string file in imported)
            if (file.StartsWith("Assets/ArtAssets/UE/", StringComparison.Ordinal) && file.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) { Queue(); return; }
    }
    public static void Publish()
    {
        if (publishing) return;
        publishing = true;
        BattleNativeEffectCatalog temporary = null;
        try
        {
            temporary = ScriptableObject.CreateInstance<BattleNativeEffectCatalog>();
            temporary.name = System.IO.Path.GetFileNameWithoutExtension(Path);
            temporary.shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Resources/UI/BattleEffectMesh.shader");
            if (temporary.shader == null) throw new InvalidDataException("缺少公共3D特效Shader。");
            foreach (var kind in BattleEffectParameters.Kinds)
            {
                var entry = new BattleNativeEffectCatalog.Entry { kind = kind };
                foreach (var pair in BattleEffectTableWriter.Resolve(kind).Models)
                    entry.parts.Add(new BattleNativeEffectCatalog.Part { part = pair.Key, model = pair.Value });
                temporary.entries.Add(entry);
            }
            var current = AssetDatabase.LoadAssetAtPath<BattleNativeEffectCatalog>(Path);
            if (current == null) { AssetDatabase.CreateAsset(temporary, Path); temporary = null; }
            else { EditorUtility.CopySerialized(temporary, current); EditorUtility.SetDirty(current); }
            AssetDatabase.SaveAssets();
        }
        finally { if (temporary != null) UnityEngine.Object.DestroyImmediate(temporary); publishing = false; }
    }
    public void OnPreprocessBuild(BuildReport report) { Publish(); }
}
