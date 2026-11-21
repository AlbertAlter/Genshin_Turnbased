using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class ProjectUiCanvasEditor
{
    public static ProjectUiCanvas InstantiateInActiveScene()
    {
        string path = $"Assets/Resources/{ProjectUiCanvas.PrefabPath}.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
            throw new InvalidOperationException($"Missing shared UI canvas prefab: {path}");

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, SceneManager.GetActiveScene());
        return instance.GetComponent<ProjectUiCanvas>();
    }

    public static ProjectUiCanvas FindInActiveScene()
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            if (root.TryGetComponent<ProjectUiCanvas>(out var canvas))
                return canvas;
        return null;
    }

    public static Canvas FindAuthoredCanvas(ProjectUiCanvas sharedCanvas)
    {
        foreach (Transform child in sharedCanvas.ContentFrame)
            if (child.TryGetComponent<Canvas>(out var canvas))
                return canvas;
        return null;
    }
}
