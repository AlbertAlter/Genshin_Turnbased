using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Consumes the UI table published by UiChartSync, just like the scene generator.</summary>
public static class BattlefieldHealthBarBuilder
{
    public static void Configure(Canvas canvas)
    {
        var style = BattlefieldHealthStyle.Load();
        foreach (var binding in canvas.GetComponentsInChildren<PsdUiBinding>(true))
        {
            if (binding.bindingKey != "ValueBar") continue;
            if ((binding.sourcePath ?? "").StartsWith("0309:Boss/") && binding.name == "矩形 7")
            {
                var modelObject = binding.transform.Find("HealthFill");
                if (modelObject == null)
                {
                    modelObject=new GameObject("HealthFill",typeof(RectTransform),typeof(CanvasRenderer),typeof(RoundedHealthFill)).transform;
                    modelObject.SetParent(binding.transform,false);
                    var r=(RectTransform)modelObject;
                    r.anchorMin=Vector2.zero; r.anchorMax=Vector2.one; r.offsetMin=r.offsetMax=Vector2.zero;
                }
                var boss=binding.GetComponent<BattlefieldHealthBar>() ?? binding.gameObject.AddComponent<BattlefieldHealthBar>();
                boss.side=BattleSide.Enemy; boss.slot=1; boss.fill=modelObject.GetComponent<RoundedHealthFill>();
                BindEnemyPips(boss);
                var stack=binding.GetComponent<PsdHealthLayers>() ?? binding.gameObject.AddComponent<PsdHealthLayers>();
                stack.bar=boss; stack.bossStyle=true;
                var legacy=binding.GetComponent<UnityEngine.UI.Image>();
                if (legacy != null) legacy.enabled=false;
                continue;
            }
            // Ordinary HUD health bars; card ElementalShield remains untouched.
            Transform group = binding.transform;
            while (group.parent != null && group.parent.name != "角色栏" && group.parent.name != "敌人栏") group = group.parent;
            if (group.parent == null) continue;
            bool ally = group.parent.name == "角色栏";
            string prefix = ally ? "角色" : "敌人";
            // Match StageViewLoader's stable ordering; PSD copies can be named 敌人2 拷贝 2.
            var groups = Enumerable.Range(0, group.parent.childCount).Select(group.parent.GetChild)
                .Where(item => item.name.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(item => FirstNumber(item.name)).ToList();
            int slot = groups.IndexOf(group) + 1;
            if (slot <= 0) continue;
            var background = binding.GetComponent<RectTransform>();
            if (background == null || background.rect.width <= style.Left+style.Right || background.rect.height <= style.Top+style.Bottom) continue;
            var child = binding.transform.Find("HealthFill");
            bool created = child == null;
            if (created)
                child = new GameObject("HealthFill", typeof(RectTransform), typeof(CanvasRenderer), typeof(RoundedHealthFill)).transform;
            if (created) child.SetParent(binding.transform, false);
            var rect = (RectTransform)child;
            if (created) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; }
            var fill = child.GetComponent<RoundedHealthFill>();
            fill.raycastTarget = false;
            if (created) fill.Fraction = 1;
            var view = binding.GetComponent<BattlefieldHealthBar>() ?? binding.gameObject.AddComponent<BattlefieldHealthBar>();
            view.side = ally ? BattleSide.Ally : BattleSide.Enemy;
            view.slot = slot; view.fill = fill;
            if (!ally) BindEnemyPips(view);
            if (created) view.ApplyStyle(style);
            var frame = binding.GetComponent<PsdHealthBarFrame>();
            if (created && frame != null && frame.psdCompositeFill)
            {
                fill.psdTexturePath = frame.psdFillPath;
                if (ally) fill.psdPadding = new Vector4(3, 7, 5, 2);
                if (frame.psdFillPath.Contains("EnemyHealth_Layers/"))
                {
                    fill.psdBodyHeight = 11;
                    fill.psdCaps = new Vector2(8.5f, 10.5f);
                }
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
            if (ally)
            {
                var label = group.GetComponentsInChildren<PsdUiBinding>(true).FirstOrDefault(item =>
                    item.bindingKey == "CurrentHPText" || (item.sourcePath ?? "").EndsWith("/0074:786", StringComparison.Ordinal));
                if (label != null)
                {
                    label.bindingKey = "CurrentHPText";
                    view.currentHPText = label.GetComponent<TMP_Text>();
                }
                else
                {
                    Transform legacy = group.Find("786");
                    if (legacy != null)
                    {
                        label = legacy.GetComponent<PsdUiBinding>() ?? legacy.gameObject.AddComponent<PsdUiBinding>();
                        label.bindingKey = "CurrentHPText";
                        view.currentHPText = legacy.GetComponent<TMP_Text>();
                    }
                }
            }
        }
    }

    private static void BindEnemyPips(BattlefieldHealthBar bar)
    {
        var group = bar.transform.parent;
        var row = bar.enemyPips;
        row.outline = group.Find("血槽1_Circle0") as RectTransform;
        row.core = group.Find("血槽1_Circle1") as RectTransform;
        row.empty = group.Find("血槽2（空") as RectTransform;
        if (row.outline == null || row.core == null || row.empty == null)
            throw new InvalidOperationException($"Missing enemy health pip artwork under {group.name}.");
        row.outline.gameObject.SetActive(false);
        row.core.gameObject.SetActive(false);
        row.empty.gameObject.SetActive(false);
    }

    public static void ConfigurePsdLayers(Canvas canvas)
    {
        foreach (var bar in canvas.GetComponentsInChildren<BattlefieldHealthBar>(true))
        {
            var layers = bar.GetComponent<PsdHealthLayers>() ?? bar.gameObject.AddComponent<PsdHealthLayers>();
            layers.bar = bar;
            layers.EnsureLayers();
            layers.ReloadLayers();
        }
    }

    private static int FirstNumber(string value)
    {
        string digits = new string(value.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out int result) ? result : int.MaxValue;
    }

    public static void UpdateScene()
    {
        const string path = "Assets/Scene/BattleField1.unity";
        Scene scene = SceneManager.GetSceneByPath(path);
        bool alreadyOpen = scene.IsValid() && scene.isLoaded;
        if (alreadyOpen && scene.isDirty) throw new InvalidOperationException("Please save BattleField1 before updating health bars.");
        if (!alreadyOpen) scene = EditorSceneManager.OpenScene(path,
            string.IsNullOrEmpty(SceneManager.GetActiveScene().path) ? OpenSceneMode.Single : OpenSceneMode.Additive);
        foreach (var root in scene.GetRootGameObjects())
            foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
                if (canvas.isRootCanvas) Configure(canvas);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save BattleField1");
        if (!alreadyOpen && SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true);
        Debug.Log("HEALTH_BAR_SCENE_UPDATED (read published Assets/StreamingAssets/Charts/UI)");
    }
}
