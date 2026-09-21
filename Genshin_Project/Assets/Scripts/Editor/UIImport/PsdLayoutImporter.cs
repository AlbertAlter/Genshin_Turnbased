using System;
using System.Collections.Generic;
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

    private sealed class TemplateRecord
    {
        public GameObject GameObject;
        public PsdLayerInfo Layer;
    }

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
        PsdLayoutOverrideTable overrides = PsdLayoutOverrideTable.LoadAdjacentTo(jsonPath);

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
        SetStretchRect(root);

        string jsonDirectory = Path.GetDirectoryName(jsonPath) ?? string.Empty;
        var templates = new Dictionary<string, TemplateRecord>(StringComparer.OrdinalIgnoreCase);
        BuildChildren(
            manifest.layers,
            root,
            manifest.document,
            jsonDirectory,
            importFolder,
            overrides,
            string.Empty,
            templates);

        overrides.WarnUnmatchedRows();

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
        if (manifest.schema_version > 2)
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
        string importFolder,
        PsdLayoutOverrideTable overrides,
        string fallbackParentPath,
        Dictionary<string, TemplateRecord> templates)
    {
        if (layers == null) return;

        // psd-tools returns the Photoshop stack top-first. Unity draws later
        // siblings over earlier siblings, so create the list in reverse order.
        for (int index = layers.Length - 1; index >= 0; index--)
        {
            PsdLayerInfo layer = layers[index];
            if (layer == null) continue;

            string fallbackPath = string.IsNullOrEmpty(fallbackParentPath)
                ? $"{index:D4}:{layer.name}"
                : $"{fallbackParentPath}/{index:D4}:{layer.name}";
            string sourcePath = string.IsNullOrWhiteSpace(layer.path) ? fallbackPath : layer.path;
            PsdLayoutOverride layoutOverride = overrides.Find(sourcePath);
            string assetMode = FirstNonEmpty(layoutOverride?.AssetMode, layer.asset_mode, "Auto");
            if (string.Equals(assetMode, "Ignore", StringComparison.OrdinalIgnoreCase)) continue;
            string templateRole = FirstNonEmpty(layoutOverride?.TemplateRole, layer.template_role, string.Empty);
            bool explicitInstance = string.Equals(
                layoutOverride?.TemplateRole,
                "instance",
                StringComparison.OrdinalIgnoreCase);
            if (explicitInstance) continue;

            bool isGroup = layer.is_group || string.Equals(layer.kind, "group", StringComparison.OrdinalIgnoreCase);
            var layerObject = new GameObject(
                FirstNonEmpty(layoutOverride?.Name, layer.name, "Unnamed PSD Layer"),
                typeof(RectTransform));
            RectTransform rect = layerObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);

            if (isGroup)
            {
                SetStretchRect(rect);
                AddBindingMetadata(layerObject, layer, layoutOverride, sourcePath);
                BuildChildren(
                    layer.children,
                    rect,
                    document,
                    jsonDirectory,
                    importFolder,
                    overrides,
                    sourcePath,
                    templates);
            }
            else
            {
                SetLayerRect(
                    rect,
                    layer.layout_bbox != null && layer.layout_bbox.Length >= 4
                        ? layer.layout_bbox
                        : layer.bbox,
                    document,
                    layoutOverride);
                if (string.Equals(assetMode, "Text", StringComparison.OrdinalIgnoreCase) ||
                    (string.Equals(assetMode, "Auto", StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(layer.kind, "type", StringComparison.OrdinalIgnoreCase)))
                    ConfigureText(layerObject, layer, rect);
                else
                    ConfigureImage(
                        layerObject,
                        layer,
                        jsonDirectory,
                        importFolder,
                        assetMode,
                        layoutOverride);
                AddBindingMetadata(layerObject, layer, layoutOverride, sourcePath);
            }
            layerObject.SetActive(layer.visible);

            string templateId = FirstNonEmpty(layoutOverride?.TemplateID, layer.template_id, string.Empty);
            if (string.Equals(templateRole, "template", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(templateId))
            {
                templates[templateId] = new TemplateRecord { GameObject = layerObject, Layer = layer };
            }
        }

        // Instances intentionally contain no independent child definitions. They
        // clone the selected template and only apply the group-relative offset.
        for (int index = layers.Length - 1; index >= 0; index--)
        {
            PsdLayerInfo layer = layers[index];
            if (layer == null) continue;
            string fallbackPath = string.IsNullOrEmpty(fallbackParentPath)
                ? $"{index:D4}:{layer.name}"
                : $"{fallbackParentPath}/{index:D4}:{layer.name}";
            string sourcePath = string.IsNullOrWhiteSpace(layer.path) ? fallbackPath : layer.path;
            PsdLayoutOverride layoutOverride = overrides.Find(sourcePath);
            string assetMode = FirstNonEmpty(layoutOverride?.AssetMode, layer.asset_mode, "Auto");
            if (string.Equals(assetMode, "Ignore", StringComparison.OrdinalIgnoreCase)) continue;
            string templateRole = FirstNonEmpty(layoutOverride?.TemplateRole, layer.template_role, string.Empty);
            if (!string.Equals(
                    layoutOverride?.TemplateRole,
                    "instance",
                    StringComparison.OrdinalIgnoreCase))
                continue;

            string templateId = FirstNonEmpty(layoutOverride?.TemplateID, layer.template_id, string.Empty);
            if (!templates.TryGetValue(templateId, out TemplateRecord template))
            {
                Debug.LogWarning($"PSD UI 实例找不到模板 {templateId}：{sourcePath}");
                continue;
            }

            GameObject instance = Object.Instantiate(template.GameObject, parent, false);
            Undo.RegisterCreatedObjectUndo(instance, "Create PSD UI template instance");
            instance.name = FirstNonEmpty(layoutOverride?.Name, layer.name, template.GameObject.name);
            RectTransform instanceRect = instance.GetComponent<RectTransform>();
            Vector2 relativeOffset = GetRelativeGroupOffset(template.Layer, layer);
            instanceRect.anchoredPosition += new Vector2(
                layoutOverride?.OffsetX ?? relativeOffset.x,
                layoutOverride?.OffsetY ?? relativeOffset.y);
            PsdUiBinding binding = instance.GetComponent<PsdUiBinding>();
            if (binding == null) binding = instance.AddComponent<PsdUiBinding>();
            binding.bindingKey = FirstNonEmpty(layoutOverride?.BindingKey, layer.binding_key, binding.bindingKey);
            binding.templateId = templateId;
            binding.templateRole = "instance";
            binding.sourcePath = sourcePath;
            instance.SetActive(layer.visible);
        }
    }

    private static void ConfigureText(GameObject layerObject, PsdLayerInfo layer, RectTransform rect)
    {
        TextMeshProUGUI label = layerObject.AddComponent<TextMeshProUGUI>();
        label.font = ProjectUiFont.LoadOrCreate();
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
        string importFolder,
        string assetMode,
        PsdLayoutOverride layoutOverride)
    {
        Image image = layerObject.AddComponent<Image>();
        image.raycastTarget = false;
        bool allowCrop = layoutOverride?.AllowCrop ?? false;
        image.preserveAspect = !allowCrop;
        image.color = new Color(1f, 1f, 1f, Mathf.Clamp01(layer.opacity / 255f));

        if (string.Equals(assetMode, "Native", StringComparison.OrdinalIgnoreCase)) return;

        string existingAssetPath = FirstNonEmpty(
            layoutOverride?.ExistingAssetPath,
            layer.existing_asset,
            string.Empty);
        if (string.Equals(assetMode, "Existing", StringComparison.OrdinalIgnoreCase))
        {
            image.sprite = LoadExistingSprite(existingAssetPath, layerObject);
            ConfigureImagePresentation(image, allowCrop, layoutOverride?.SliceBorder ?? layer.slice_border);
            return;
        }

        if (string.IsNullOrWhiteSpace(layer.asset)) return;
        string sourceImage = Path.GetFullPath(Path.Combine(jsonDirectory, layer.asset));
        if (!File.Exists(sourceImage))
        {
            Debug.LogWarning($"PSD 图层图片不存在：{sourceImage}", layerObject);
            return;
        }

        string sliceBorder = FirstNonEmpty(layoutOverride?.SliceBorder, layer.slice_border, string.Empty);
        string assetPath = CopyAndImportSprite(sourceImage, importFolder, sliceBorder);
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        ConfigureImagePresentation(image, allowCrop, sliceBorder);
    }

    private static string CopyAndImportSprite(string sourcePath, string importFolder, string sliceBorder)
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
            Vector4 border = ParseBorder(sliceBorder);
            bool changed = importer.textureType != TextureImporterType.Sprite ||
                importer.spriteImportMode != SpriteImportMode.Single ||
                importer.mipmapEnabled || importer.spriteBorder != border;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.spriteBorder = border;
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

    private static void SetStretchRect(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private static void SetLayerRect(
        RectTransform rect,
        int[] bbox,
        PsdDocumentInfo document,
        PsdLayoutOverride layoutOverride)
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

        float width = layoutOverride?.Width ?? Mathf.Max(0, right - left);
        float height = layoutOverride?.Height ?? Mathf.Max(0, bottom - top);
        Vector2 anchor = ParseAnchor(layoutOverride?.Anchor, left, top, right, bottom, document);
        float centerX = (left + right) * 0.5f;
        float centerY = (top + bottom) * 0.5f;
        string sizeMode = layoutOverride?.SizeMode ?? "Fixed";
        bool stretchX = string.Equals(sizeMode, "StretchX", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sizeMode, "StretchBoth", StringComparison.OrdinalIgnoreCase);
        bool stretchY = string.Equals(sizeMode, "StretchY", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sizeMode, "StretchBoth", StringComparison.OrdinalIgnoreCase);
        Vector2 anchorMin = new Vector2(stretchX ? 0f : anchor.x, stretchY ? 0f : anchor.y);
        Vector2 anchorMax = new Vector2(stretchX ? 1f : anchor.x, stretchY ? 1f : anchor.y);
        float defaultOffsetX = stretchX
            ? centerX - document.width * 0.5f
            : centerX - anchor.x * document.width;
        float defaultOffsetY = stretchY
            ? document.height * 0.5f - centerY
            : anchor.y * document.height - centerY;

        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(
            layoutOverride?.OffsetX ?? defaultOffsetX,
            layoutOverride?.OffsetY ?? defaultOffsetY);
        rect.sizeDelta = new Vector2(
            stretchX ? width - document.width : width,
            stretchY ? height - document.height : height);
        rect.localScale = Vector3.one;
    }

    private static Vector2 ParseAnchor(
        string value,
        int left,
        int top,
        int right,
        int bottom,
        PsdDocumentInfo document)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            float centerX = (left + right) * 0.5f;
            float centerY = (top + bottom) * 0.5f;
            float x = centerX < document.width / 3f ? 0f : centerX > document.width * 2f / 3f ? 1f : 0.5f;
            float y = centerY < document.height / 3f ? 1f : centerY > document.height * 2f / 3f ? 0f : 0.5f;
            return new Vector2(x, y);
        }

        string normalized = value.Replace("_", string.Empty).Replace("-", string.Empty).Trim().ToLowerInvariant();
        float anchorX = normalized.EndsWith("left") ? 0f : normalized.EndsWith("right") ? 1f : 0.5f;
        float anchorY = normalized.StartsWith("top") ? 1f : normalized.StartsWith("bottom") ? 0f : 0.5f;
        return new Vector2(anchorX, anchorY);
    }

    private static void AddBindingMetadata(
        GameObject layerObject,
        PsdLayerInfo layer,
        PsdLayoutOverride layoutOverride,
        string sourcePath)
    {
        string bindingKey = FirstNonEmpty(layoutOverride?.BindingKey, layer.binding_key, string.Empty);
        string templateId = FirstNonEmpty(layoutOverride?.TemplateID, layer.template_id, string.Empty);
        string templateRole = FirstNonEmpty(layoutOverride?.TemplateRole, layer.template_role, string.Empty);
        if (string.IsNullOrEmpty(bindingKey) && string.IsNullOrEmpty(templateId)) return;

        PsdUiBinding binding = layerObject.AddComponent<PsdUiBinding>();
        binding.bindingKey = bindingKey;
        binding.templateId = templateId;
        binding.templateRole = templateRole;
        binding.sourcePath = sourcePath;
    }

    private static Sprite LoadExistingSprite(string path, GameObject context)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Debug.LogWarning("PSD UI Existing 模式没有填写 ExistingAssetPath。", context);
            return null;
        }

        string assetPath = path.Replace('\\', '/');
        if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) && Path.IsPathRooted(path))
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string fullPath = Path.GetFullPath(path);
            if (fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                assetPath = fullPath.Substring(projectRoot.Length).TrimStart('\\', '/').Replace('\\', '/');
        }

        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        if (sprite == null)
            Debug.LogWarning($"PSD UI 找不到现有 Sprite：{assetPath}", context);
        return sprite;
    }

    private static void ConfigureImagePresentation(Image image, bool allowCrop, string sliceBorder)
    {
        Vector4 border = ParseBorder(sliceBorder);
        if (border != Vector4.zero)
        {
            image.type = Image.Type.Sliced;
            image.preserveAspect = false;
            return;
        }

        if (!allowCrop || image.sprite == null) return;
        AspectRatioFitter fitter = image.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = image.sprite.rect.width / Mathf.Max(1f, image.sprite.rect.height);
    }

    private static Vector4 ParseBorder(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Vector4.zero;
        string[] parts = value.Split(',');
        if (parts.Length != 4) return Vector4.zero;
        if (!float.TryParse(parts[0], out float left) ||
            !float.TryParse(parts[1], out float bottom) ||
            !float.TryParse(parts[2], out float right) ||
            !float.TryParse(parts[3], out float top))
            return Vector4.zero;
        return new Vector4(left, bottom, right, top);
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        return string.Empty;
    }

    private static Vector2 GetRelativeGroupOffset(PsdLayerInfo template, PsdLayerInfo instance)
    {
        Rect templateRect = GetEffectivePsdRect(template);
        Rect instanceRect = GetEffectivePsdRect(instance);
        return new Vector2(
            instanceRect.center.x - templateRect.center.x,
            templateRect.center.y - instanceRect.center.y);
    }

    private static Rect GetEffectivePsdRect(PsdLayerInfo layer)
    {
        int[] bbox = layer?.layout_bbox != null && layer.layout_bbox.Length >= 4
            ? layer.layout_bbox
            : layer?.bbox;
        if (bbox != null && bbox.Length >= 4 && bbox[2] > bbox[0] && bbox[3] > bbox[1])
        {
            return Rect.MinMaxRect(bbox[0], bbox[1], bbox[2], bbox[3]);
        }

        bool found = false;
        Rect result = new Rect();
        if (layer?.children != null)
        {
            foreach (PsdLayerInfo child in layer.children)
            {
                Rect childRect = GetEffectivePsdRect(child);
                if (childRect.width <= 0f || childRect.height <= 0f) continue;
                if (!found)
                {
                    result = childRect;
                    found = true;
                }
                else
                {
                    result = Rect.MinMaxRect(
                        Mathf.Min(result.xMin, childRect.xMin),
                        Mathf.Min(result.yMin, childRect.yMin),
                        Mathf.Max(result.xMax, childRect.xMax),
                        Mathf.Max(result.yMax, childRect.yMax));
                }
            }
        }
        return result;
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
