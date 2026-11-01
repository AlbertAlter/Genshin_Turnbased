using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Wwise's editor setup used to remove the native component from an open camera.
// A Play snapshot can still contain that removal after the on-disk setting was fixed.
[InitializeOnLoad]
internal static class BattleSceneAudioListenerRepair
{
    static BattleSceneAudioListenerRepair()
    {
        EditorApplication.delayCall += RepairAfterReload;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void RepairAfterReload()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode) RestoreMissingListener();
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode) RestoreMissingListener();
        else if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += RepairAfterReload;
    }

    private static void RestoreMissingListener()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || !scene.path.StartsWith("Assets/Scene/", StringComparison.Ordinal) ||
            AkWwiseEditorSettings.Instance.CreateWwiseListener) return;
        var roots = scene.GetRootGameObjects();
        if (!roots.Any(root => root.GetComponentInChildren<CharacterBattleVoice>(true) != null)) return;
        // Count enabled listeners across loaded scenes so an additive setup keeps its chosen listener.
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var loaded = SceneManager.GetSceneAt(i);
            if (loaded.isLoaded && loaded.GetRootGameObjects().Any(root =>
                root.GetComponentsInChildren<AudioListener>().Any(listener => listener.isActiveAndEnabled))) return;
        }
        var cameras = roots.SelectMany(root => root.GetComponentsInChildren<Camera>())
            .Where(camera => camera.isActiveAndEnabled && camera.CompareTag("MainCamera")).ToArray();
        if (cameras.Length != 1 || cameras[0].GetComponent<AudioListener>() != null) return;
        Undo.AddComponent<AudioListener>(cameras[0].gameObject);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[角色语音] 已恢复当前战斗场景主相机缺失的 Unity AudioListener。", cameras[0]);
    }
}
