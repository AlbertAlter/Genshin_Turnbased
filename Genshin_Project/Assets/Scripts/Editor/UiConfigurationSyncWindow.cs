using System;
using UnityEditor;
using UnityEngine;

/// <summary>UI art import and animation-only publication. Never rebuilds or modifies scene layout.</summary>
public static class UiConfigurationSyncWindow
{
    [MenuItem("Tools/UI/导入美术资源")]
    public static void ImportArt()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        try
        {
            UiChartSync.Sync();
            ReferencedArtAssetsSync.SyncReferencedImages();
            Debug.Log("UI art synchronized. Scene configuration was not changed.");
        }
        catch(Exception error) { Debug.LogException(error); }
    }

    // Initial scene construction only. Never called by the art import menu.
    public static void ConfigureInitialVectorRendering(GameObject root)
    {
        foreach (var binding in root.GetComponentsInChildren<PsdUiBinding>(true))
        {
            if (binding.bindingKey != "EnemyAvatar") continue;
            var image=binding.GetComponentInChildren<UnityEngine.UI.Image>(true);
            if (image != null && image.GetComponent<AvatarAlphaEffects>() == null)
                image.gameObject.AddComponent<AvatarAlphaEffects>();
        }
        // Read only the published export manifest; never infer eligibility from alpha alone.
        string path = System.IO.Path.Combine(Application.streamingAssetsPath,
            "Charts/UI/BattleField1_vector_assets.csv");
        if (!System.IO.File.Exists(path)) return;
        var decisions = new System.Collections.Generic.Dictionary<string,string>(StringComparer.Ordinal);
        var extents = new System.Collections.Generic.Dictionary<string,Vector4>(StringComparer.Ordinal);
        foreach (string line in System.IO.File.ReadAllLines(path))
        {
            string[] cells = line.Split(','); // Exporter emits unquoted ASCII paths/modes.
            if (cells.Length >= 2) decisions[cells[0].Trim('\uFEFF')] = cells[1];
            if (cells.Length >= 7 &&
                float.TryParse(cells[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(cells[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                float.TryParse(cells[5], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float w) &&
                float.TryParse(cells[6], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float h))
                extents[cells[0].Trim('\uFEFF')] = new Vector4(x,y,w,h);
        }
        foreach (var reference in root.GetComponentsInChildren<StreamingSpriteReference>(true))
        {
            bool circle = decisions.TryGetValue(reference.relativePath ?? "", out string mode) && mode == "VectorCircle";
            bool changed = reference.vectorCircle != circle;
            if (reference.vectorCircle != circle)
            {
                Undo.RecordObject(reference,"Apply exported vector rendering decision");
                reference.vectorCircle = circle;
                EditorUtility.SetDirty(reference);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(reference.gameObject.scene);
            }
            if (circle || changed) reference.RefreshEditorPreview();
        }
        foreach (var view in root.GetComponentsInChildren<PsdHealthLayers>(true))
        {
            if (view.layers.Length != 7 || view.layers[2] == null) continue;
            bool vector = decisions.TryGetValue(view.layers[2].relativePath ?? "", out string mode) && mode == "VectorCapsule";
            extents.TryGetValue(view.layers[2].relativePath ?? "", out Vector4 sourceExtent);
            // Apply to base and all clipped layers: they must share the same silhouette.
            for (int i=2; i<view.layers.Length; i++)
            {
                var layer=view.layers[i];
                if (layer == null || (layer.vectorBase == vector && layer.sourceBaseExtent == sourceExtent)) continue;
                Undo.RecordObject(layer,"Apply exported HP vector silhouette");
                layer.vectorBase=vector;
                layer.sourceBaseExtent=sourceExtent;
                EditorUtility.SetDirty(layer);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(layer.gameObject.scene);
            }
            view.ReloadLayers();
        }
    }

    [MenuItem("Tools/UI/更新 UI 配置")]
    public static void UpdateConfiguration()
    {
        try
        {
            UiChartSync.SyncAnimation();
            // In Edit Mode the scene is not touched at all; the next run reads the published table.
            if(EditorApplication.isPlaying)
            {
                foreach(var preview in UnityEngine.Object.FindObjectsOfType<ShieldPreviewPanel>(true))preview.RequestReload();
                foreach(var view in UnityEngine.Object.FindObjectsOfType<BattlefieldApView>(true))view.ReloadAnimationParameters();
                foreach(var view in UnityEngine.Object.FindObjectsOfType<BattlefieldHealthBar>(true))view.ReloadAnimationParameters();
                foreach(var view in UnityEngine.Object.FindObjectsOfType<BattleUiEnemyShieldBar>(true))view.ReloadAnimationParameters();
                foreach(var view in UnityEngine.Object.FindObjectsOfType<BattlefieldDamagePopupController>(true))view.ReloadConfiguration();
                foreach(var view in UnityEngine.Object.FindObjectsOfType<BattlefieldPresentationPlayer>(true))view.ReloadConfiguration();
                foreach(var view in UnityEngine.Object.FindObjectsOfType<BattlefieldSelectionPresentation>(true))view.ReloadAnimationParameters();
                foreach(var view in UnityEngine.Object.FindObjectsOfType<BattlefieldActionStateView>(true))view.ReloadAnimationParameters();
                foreach(var route in UnityEngine.Object.FindObjectsOfType<Roguelike.Tests.RouteGeneration.RouteTestController>(true))route.ReloadRouteArt();
            }
            Debug.Log("UI 动画、伤害/状态跳字与路线美术参数已同步；运行中的界面已刷新。未重建或保存场景。");
        }
        catch(Exception error) { Debug.LogException(error); }
    }
}
