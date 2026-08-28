using System;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Imports the layout.json produced by art_assets/UI/export_psd_layout.py.
/// This is an Editor-only visual hierarchy importer; runtime battle logic stays
/// in UIBattleController and can be bound after the visual prefab is approved.
/// </summary>
internal static class PsdLayoutImporter
{
    private const string GeneratedAssetRoot = "Assets/Resources/UI/PSDImported";

    [MenuItem("Tools/UI 管线/导入 PSD Layout JSON...")]
    private static void ImportFromMenu()
    {
        string initialDirectory = GetSuggestedExternalUiDirectory();
        string jsonPath = EditorUtility.OpenFilePanel(
            "选择 PSD 导出的 layout.json",
            initialDirectory,
            "json");
        if (string.IsNullOrEmpty(jsonPath)) return;

        try
        {
            Import(jsonPath);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("PSD UI 导入失败", exception.Message, "确定");
        }
    }

    internal static GameObject Import(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            throw new FileNotFoundException("找不到布局 JSON。", jsonPath);

        string json = File.ReadAllText(jsonPath, Encoding.UTF8);
        PsdLayoutManifest manifest = JsonUtility.FromJson<PsdLayoutManifest>(json);
        ValidateManifest(manifest);

        RectTransform parent = ResolveParent(manifest.document);
        string sourceName = string.IsNullOrWhiteSpace(manifest.source)
            ? Path.GetFileName(Path.GetDirectoryName(jsonPath))
            : Path.GetFileNameWithoutExtension(manifest.source);
        string importFolder = GeneratedAssetRoot + "/" + SanitizeAssetName(sourceName);
        EnsureAssetFolder(importFolder);

        var rootObject = new GameObject(
            GameObjectUtility.GetUniqueNameForSibling(parent, "PSD_" + sourceName),
            typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(rootObject, "Import PSD UI layout");
        RectTransform root = rootObject.GetComponent<RectTransform>();
        root.SetParent(parent, false);
        SetDocumentRect(root, manifest.document.width, manifest.document.height);

        string jsonDirectory = Path.GetDirectoryName(jsonPath) ?? string.Empty;
        BuildChildren(
            manifest.layers,
            root,
            manifest.document,
            jsonDirectory,
            importFolder);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(rootObject.scene);
        Selection.activeGameObject = rootObject;
        EditorGUIUtility.PingObject(rootObject);
        Debug.Log(
            $"PSD UI 导入完成：{manifest.total_layers} 个图层，根节点 {rootObject.name}。" +
            "复杂混合模式、九宫格与业务字段仍需人工确认。",
            rootObject);
        return rootObject;
    }

    private static void ValidateManifest(PsdLayoutManifest manifest)
    {
        if (manifest == null)
            throw new InvalidDataException("JSON 不是有效的 PSD 布局清单。");
        if (manifest.document == null || manifest.document.width <= 0 || manifest.document.height <= 0)
            throw new InvalidDataException("JSON 缺少有效的 document.width/document.height。");
        if (manifest.layers == null)
            throw new InvalidDataException("JSON 缺少 layers 数组。");
        if (manifest.schema_version > 1)
            throw new InvalidDataException($"暂不支持 schema_version={manifest.schema_version}。");
    }

    private static RectTransform ResolveParent(PsdDocumentInfo document)
    {
        if (Selection.activeTransform is RectTransform selectedRect &&
            selectedRect.GetComponentInParent<Canvas>() != null)
            return selectedRect;

        Canvas canvas = Object.FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            var canvasObject = new GameObject(
                "Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(canvasObject, "Create PSD UI Canvas");
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(document.width, document.height);
            scaler.matchWidthOrHeight = 0.5f;
        }
        return (RectTransform)canvas.transform;
    }

    private static void BuildChildren(
        PsdLayerInfo[] layers,
        RectTransform parent,
        PsdDocumentInfo document,
        string jsonDirectory,
        string importFolder)
    {
        if (layers == null) return;

        // psd-tools returns the Photoshop stack top-first. Unity draws later
        // siblings over earlier siblings, so create the list in reverse order.
        for (int index = layers.Length - 1; index >= 0; index--)
        {
            PsdLayerInfo layer = layers[index];
            if (layer == null) continue;

            bool isGroup = layer.is_group || string.Equals(layer.kind, "group", StringComparison.OrdinalIgnoreCase);
            var layerObject = new GameObject(
                string.IsNullOrWhiteSpace(layer.name) ? "Unnamed PSD Layer" : layer.name,
                typeof(RectTransform));
            RectTransform rect = layerObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);

            if (isGroup)
            {
                SetDocumentRect(rect, document.width, document.height);
                BuildChildren(layer.children, rect, document, jsonDirectory, importFolder);
            }
            else
            {
                SetLayerRect(rect, layer.bbox, document);
                if (string.Equals(layer.kind, "type", StringComparison.OrdinalIgnoreCase))
                    ConfigureText(layerObject, layer, rect);
                else
                    ConfigureImage(layerObject, layer, jsonDirectory, importFolder);
            }
            layerObject.SetActive(layer.visible);
        }
    }

    private static void ConfigureText(GameObject layerObject, PsdLayerInfo layer, RectTransform rect)
    {
        TextMeshProUGUI label = layerObject.AddComponent<TextMeshProUGUI>();
        label.text = layer.text ?? layer.name ?? string.Empty;
        label.raycastTarget = false;
        label.enableWordWrapping = true;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.fontSize = Mathf.Clamp(rect.rect.height * 0.55f, 10f, 96f);
        label.color = new Color(1f, 1f, 1f, Mathf.Clamp01(layer.opacity / 255f));
    }

    private static void ConfigureImage(
        GameObject layerObject,
        PsdLayerInfo layer,
        string jsonDirectory,
        string importFolder)
    {
        Image image = layerObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = false;
        image.color = new Color(1f, 1f, 1f, Mathf.Clamp01(layer.opacity / 255f));

        if (string.IsNullOrWhiteSpace(layer.asset)) return;
        string sourceImage = Path.GetFullPath(Path.Combine(jsonDirectory, layer.asset));
        if (!File.Exists(sourceImage))
        {
            Debug.LogWarning($"PSD 图层图片不存在：{sourceImage}", layerObject);
            return;
        }

        string assetPath = CopyAndImportSprite(sourceImage, importFolder);
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
    }

    private static string CopyAndImportSprite(string sourcePath, string importFolder)
    {
        string filename = SanitizeAssetName(Path.GetFileNameWithoutExtension(sourcePath)) + ".png";
        string assetPath = importFolder + "/" + filename;
        string destinationPath = Path.GetFullPath(assetPath);

        bool shouldCopy = !File.Exists(destinationPath) ||
            new FileInfo(destinationPath).Length != new FileInfo(sourcePath).Length ||
            File.GetLastWriteTimeUtc(destinationPath) < File.GetLastWriteTimeUtc(sourcePath);
        if (shouldCopy)
            File.Copy(sourcePath, destinationPath, true);

        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
        {
            bool changed = importer.textureType != TextureImporterType.Sprite ||
                importer.spriteImportMode != SpriteImportMode.Single ||
                importer.mipmapEnabled;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            if (changed) importer.SaveAndReimport();
        }
        return assetPath;
    }

    private static void SetDocumentRect(RectTransform rect, int width, int height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(width, height);
        rect.localScale = Vector3.one;
    }

    private static void SetLayerRect(RectTransform rect, int[] bbox, PsdDocumentInfo document)
    {
        int left = 0;
        int top = 0;
        int right = document.width;
        int bottom = document.height;
        if (bbox != null && bbox.Length >= 4)
        {
            left = bbox[0];
            top = bbox[1];
            right = bbox[2];
            bottom = bbox[3];
        }

        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(Mathf.Max(0, right - left), Mathf.Max(0, bottom - top));
        rect.localScale = Vector3.one;
    }

    private static void EnsureAssetFolder(string assetFolder)
    {
        string fullPath = Path.GetFullPath(assetFolder);
        Directory.CreateDirectory(fullPath);
        AssetDatabase.Refresh();
    }

    private static string SanitizeAssetName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "PSDLayout";
        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return value.Trim().Replace(' ', '_');
    }

    private static string GetSuggestedExternalUiDirectory()
    {
        string path = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "..", "art_assets", "UI"));
        return Directory.Exists(path) ? path : Application.dataPath;
    }
}
