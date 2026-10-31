using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Generates Assets/Scene/BattleField1.unity from StreamingAssets/Charts/UI/BattleField1.csv.</summary>
public static class BattlefieldUiSceneGenerator
{
    private const float DesignWidth = 1920f;
    private const float DesignHeight = 1080f;
    private const string ScenePath = "Assets/Scene/BattleField1.unity";

    // Batch entry point uses the same publication and resource collection as the editor tools.
    public static void SyncAndGenerate()
    {
        UiChartSync.Sync();
        ReferencedArtAssetsSync.SyncReferencedImages();
        Generate();
    }

    public static void Generate()
    {
        string tablePath = Path.Combine(Application.streamingAssetsPath, "Charts", "UI", "BattleField1.csv");
        List<BattlefieldUiLayoutRow> rows;
        try { rows = BattlefieldUiLayoutTable.Load(tablePath); }
        catch (Exception exception)
        {
            Debug.LogError("无法读取运行时 UI 配表：" + tablePath + Environment.NewLine + exception);
            return;
        }

        Validate(rows);
        TMP_FontAsset projectFont = ProjectUiFont.LoadOrCreate();
        Scene previous = SceneManager.GetActiveScene();
        // A batch-mode editor starts with a dirty, untitled scene and Unity refuses
        // to open another scene additively beside it. There is nothing to preserve
        // in that case, so replace the bootstrap scene; keep the additive workflow
        // when the user launched generation from a saved editor scene.
        bool preservePrevious = previous.IsValid() && previous.isLoaded &&
                                !string.IsNullOrWhiteSpace(previous.path);
        Scene scene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            preservePrevious ? NewSceneMode.Additive : NewSceneMode.Single);
        SceneManager.SetActiveScene(scene);
        try
        {
            CreateCamera();
            Canvas canvas = CreateCanvas();
            CreateEventSystem();
            GameObject runtime = CreateBattleRuntime();
            var byPath = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            var rowByPath = new Dictionary<string, BattlefieldUiLayoutRow>(StringComparer.OrdinalIgnoreCase);
            var templateById = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

            foreach (BattlefieldUiLayoutRow row in rows.Where(item =>
                         !Equals(item.AssetMode, "Ignore") && !Equals(item.AssetMode, "Instance")))
            {
                Transform parent = FindParent(row.Path, byPath, canvas.transform);
                BattlefieldUiLayoutRow parentRow = FindParentRow(row.Path, rowByPath);
                GameObject created = CreateNode(row, parent, parentRow, projectFont);
                byPath[row.Path] = created;
                rowByPath[row.Path] = row;
                if (Equals(row.TemplateRole, "template") && !string.IsNullOrWhiteSpace(row.TemplateID))
                    templateById[row.TemplateID] = created;
            }

            foreach (BattlefieldUiLayoutRow row in rows.Where(item => Equals(item.AssetMode, "Instance")))
            {
                if (!templateById.TryGetValue(row.TemplateID, out GameObject template))
                {
                    Debug.LogWarning("模板实例找不到模板，已跳过：" + row.Path + " -> " + row.TemplateID);
                    continue;
                }
                Transform parent = FindParent(row.Path, byPath, canvas.transform);
                GameObject instance = UnityEngine.Object.Instantiate(template, parent, false);
                instance.name = row.Name;
                string templateIdentity = template.GetComponent<UiSyncIdentity>().id;
                foreach (var identity in instance.GetComponentsInChildren<UiSyncIdentity>(true))
                    identity.id = InstanceIdentity(StableId(row), templateIdentity, identity.id);
                RectTransform rect = instance.GetComponent<RectTransform>();
                rect.anchoredPosition += new Vector2(row.OffsetX, row.OffsetY);
                PsdUiBinding binding = instance.GetComponent<PsdUiBinding>() ?? instance.AddComponent<PsdUiBinding>();
                binding.sourcePath = row.Path;
                binding.templateId = row.TemplateID;
                binding.templateRole = "instance";
                AdjustElementAuras(instance, row.LayoutVariant);
                ApplyInstanceText(instance, row);
                byPath[row.Path] = instance;
            }

            // Include original template cards as well as clones in the explicit layout registry.
            foreach (var row in rows.Where(row => Equals(row.LayoutVariant, "Top") || Equals(row.LayoutVariant, "Bottom")))
                if (byPath.TryGetValue(row.Path, out var card)) AdjustElementAuras(card, row.LayoutVariant);

            // Keycap artwork comes from the component PSD, while the table keeps
            // each variant aligned to its glyph position in the battlefield PSD.
            foreach (PsdUiBinding keycap in canvas.GetComponentsInChildren<PsdUiBinding>(true))
            {
                if (keycap.bindingKey != "KeycapQ" && keycap.bindingKey != "KeycapW" &&
                    keycap.bindingKey != "KeycapE") continue;
                int ordinal = FirstNumber(keycap.transform.parent.name);
                string desired = ordinal == 2 ? "KeycapW" : ordinal == 3 ? "KeycapE" : "KeycapQ";
                keycap.gameObject.SetActive(keycap.bindingKey == desired);
            }
            EnforceTargetSelectionLayering(canvas);
            EnsureTargetSelectionGlows(canvas);
            BattlefieldHealthBarBuilder.Configure(canvas);
            BattlefieldHealthBarBuilder.ConfigurePsdLayers(canvas);
            UiConfigurationSyncWindow.ConfigureInitialVectorRendering(canvas.gameObject);
            ConfigureInitialUiModules(runtime, canvas);

            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Scene"));
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("Unity failed to save scene: " + ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"BattleField1 Scene 已按运行时表生成：{ScenePath}（{rows.Count} 行）");
        }
        finally
        {
            if (preservePrevious && previous.IsValid() && previous.isLoaded)
                SceneManager.SetActiveScene(previous);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static Canvas CreateCanvas()
    {
        var go = new GameObject("BattleField1Canvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Keep thin HUD lines and TMP glyph edges aligned with the output pixel grid.
        // This matters especially in the editor Game view, where a fractional canvas
        // scale otherwise makes the one-pixel highlights look uniformly soft.
        canvas.pixelPerfect = true;
        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(DesignWidth, DesignHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        scaler.referencePixelsPerUnit = 100f;
        return canvas;
    }

    private static void CreateCamera()
    {
        var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.orthographic = true;
    }

    private static void CreateEventSystem()
    {
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    private static GameObject CreateBattleRuntime()
    {
        const string stageFolder = "Assets/Resources/Stages";
        string[] stageGuids = AssetDatabase.FindAssets("t:TextAsset", new[] { stageFolder })
            .Where(guid => Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(guid)).Replace('\\', '/') == stageFolder)
            .ToArray(); // Version1 test variants do not replace the original template's stage.
        if (stageGuids.Length != 1)
            throw new InvalidDataException($"BattleField1 需要关卡编辑器目录中恰好一个 Stage，当前为 {stageGuids.Length} 个：{stageFolder}");

        string stagePath = AssetDatabase.GUIDToAssetPath(stageGuids[0]);
        TextAsset stage = AssetDatabase.LoadAssetAtPath<TextAsset>(stagePath);
        if (stage == null) throw new InvalidDataException("无法读取关卡编辑器 Stage：" + stagePath);

        var runtime = new GameObject("BattleRuntime");
        BattleTester tester = runtime.AddComponent<BattleTester>();
        BattlefieldStageViewLoader viewLoader = runtime.AddComponent<BattlefieldStageViewLoader>();
        runtime.AddComponent<BattlefieldActionStateView>();
        runtime.AddComponent<BattlefieldApView>();
        runtime.AddComponent<BattlefieldSelectionPresentation>();
        runtime.AddComponent<BattlefieldSwitchController>();
        tester.StageConfigJson = stage;
        viewLoader.stageConfigJson = stage;
        return runtime;
    }

    // Initial scene construction only. Daily art/config sync must never rebuild these references or layout.
    private static void ConfigureInitialUiModules(GameObject runtime, Canvas canvas)
    {
        var layout = runtime.AddComponent<BattleUiLayout>(); layout.canvas = canvas;
        layout.cards = canvas.GetComponentsInChildren<BattlefieldElementAuraBar>(true)
            .OrderBy(bar => bar.side).ThenBy(bar => bar.slot).Select(bar => new BattleUiCardBinding
            {
                side = bar.side, slot = bar.slot, root = bar.transform as RectTransform, auraBar = bar,
                art = bar.GetComponentsInChildren<PsdUiBinding>(true)
                    .First(binding => binding.bindingKey == (bar.side == BattleSide.Ally ? "CharacterCardArt" : "EnemyCardArt")).transform as RectTransform
            }).ToArray();
        var source = runtime.AddComponent<BattleUiCombatAuraSource>();
        var auras = runtime.AddComponent<BattlefieldElementAuraController>(); auras.layout = layout; auras.dataSource = source;
        BattleUiHudAssembler.Configure(runtime, canvas);
        BattleUiSceneModules.Configure(runtime, layout);
    }

    private static GameObject CreateNode(
        BattlefieldUiLayoutRow row,
        Transform parent,
        BattlefieldUiLayoutRow parentRow,
        TMP_FontAsset projectFont)
    {
        if (Equals(row.AssetMode, "Group"))
        {
            var group = new GameObject(row.Name, typeof(RectTransform));
            group.transform.SetParent(parent, false);
            ApplyRect(group.GetComponent<RectTransform>(), row, parentRow);
            AttachBinding(group, row);
            return group;
        }

        if (Equals(row.AssetMode, "Text"))
        {
            var textObject = new GameObject(row.Name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            ApplyRect(textObject.GetComponent<RectTransform>(), row, parentRow);
            TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
            label.font = projectFont;
            if (Equals(row.FontStyle, "Bold")) label.fontStyle = FontStyles.Bold;
            label.text = string.IsNullOrEmpty(row.Text) ? row.Name : row.Text;
            label.color = new Color32(0xFE, 0xFE, 0xFE, 0xFF);
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.enableAutoSizing = true;
            label.fontSizeMin = 8f;
            label.fontSizeMax = Mathf.Max(12f, row.Height);
            if (row.LayoutVariant == "ComponentText")
            {
                // PSD keycap glyphs use 20.83333px SDK_SC_Web; the solid backdrop
                // is a separate Image. Keep SDF glyphs independent of sprite sampling.
                label.enableAutoSizing = false;
                label.fontSize = 20.83333f;
                label.alignment = TextAlignmentOptions.MidlineGeoAligned;
                label.overflowMode = TextOverflowModes.Overflow;
            }
            if (row.FontSize > 0f)
            {
                label.enableAutoSizing = false;
                label.fontSize = row.FontSize;
                label.alignment = TextAlignmentOptions.MidlineGeoAligned;
                label.overflowMode = TextOverflowModes.Overflow;
            }
            AttachBinding(textObject, row);
            if (Enum.TryParse(row.TextAlignment, out TextAlignmentOptions alignment)) label.alignment = alignment;
            if (row.BindingKey == "APValue" || row.BindingKey == "APSpendHint" || row.BindingKey == "APGainHint")
            {
                var apStyle = BattlefieldApView.LoadStyle();
                label.font = ProjectUiFont.LoadOrCreateOutlined();
                label.color = row.BindingKey == "APValue" ? apStyle.Text :
                    row.BindingKey == "APSpendHint" ? apStyle.Loss : apStyle.Gain;
                textObject.AddComponent<PsdApTextOutline>();
                if (row.BindingKey != "APValue") textObject.SetActive(false);
            }
            return textObject;
        }

        GameObject host;
        GameObject imageObject;
        if (row.AllowCrop)
        {
            bool avatar = row.BindingKey == "CharacterAvatar" || row.BindingKey == "EnemyAvatar";
            host = avatar
                ? new GameObject(row.Name, typeof(RectTransform), typeof(CanvasRenderer),
                    typeof(DiamondMaskGraphic), typeof(Mask))
                : new GameObject(row.Name, typeof(RectTransform), typeof(RectMask2D));
            host.transform.SetParent(parent, false);
            ApplyRect(host.GetComponent<RectTransform>(), row, parentRow);
            if (avatar)
            {
                DiamondMaskGraphic maskGraphic = host.GetComponent<DiamondMaskGraphic>();
                maskGraphic.raycastTarget = false;
                host.GetComponent<Mask>().showMaskGraphic = false;
                StretchToParent(host.GetComponent<RectTransform>());
            }
            imageObject = new GameObject("Image", typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter));
            imageObject.transform.SetParent(host.transform, false);
            if (avatar)
            {
                UnityEngine.Object.DestroyImmediate(imageObject.GetComponent<AspectRatioFitter>());
                ApplyRect(imageObject.GetComponent<RectTransform>(), row, parentRow);
            }
            else
            {
                StretchToParent(imageObject.GetComponent<RectTransform>());
            }
        }
        else
        {
            bool healthFrame = row.BindingKey == "ValueBar" &&
                (row.Path.Contains("角色栏/") || row.Path.Contains("敌人栏/")) && Equals(row.AssetMode, "Generated");
            host = new GameObject(row.Name, typeof(RectTransform), healthFrame ? typeof(PsdHealthBarFrame) : typeof(Image));
            if (healthFrame)
            {
                var frame = host.GetComponent<PsdHealthBarFrame>();
                frame.psdCompositeFill = !string.IsNullOrWhiteSpace(row.StreamingAssetPath) && row.StreamingAssetPath != "-";
                frame.psdFillPath = frame.psdCompositeFill ? row.StreamingAssetPath : "";
            }
            if (healthFrame && row.Path.Contains("敌人栏/"))
            {
                var frame = host.GetComponent<PsdHealthBarFrame>();
                frame.outsideStroke = false;
                frame.outerStrokeColor = new Color32(64,0,0,255);
                frame.innerStrokeColor = new Color32(36,0,0,255);
                frame.psdCompositeFill = !string.IsNullOrWhiteSpace(row.StreamingAssetPath) && row.StreamingAssetPath != "-";
                frame.psdFillPath = frame.psdCompositeFill ? row.StreamingAssetPath : "";
            }
            host.transform.SetParent(parent, false);
            ApplyRect(host.GetComponent<RectTransform>(), row, parentRow);
            imageObject = host;
        }

        Image image = imageObject.GetComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = Equals(row.SizeMode, "Uniform") && !row.AllowCrop;
        image.color = GeneratedColor(row);
        if (row.BindingKey == "ScreenDimmer" || row.BindingKey == "CardOverGlow" ||
            row.BindingKey == "DisabledOverlay" || row.BindingKey == "BloodBond" ||
            Equals(row.AssetMode, "DynamicStreaming"))
            image.enabled = false;
        if (Equals(row.AssetMode, "Streaming") || Equals(row.AssetMode, "DynamicStreaming"))
        {
            StreamingSpriteReference reference = imageObject.AddComponent<StreamingSpriteReference>();
            reference.relativePath = row.StreamingAssetPath;
            reference.allowCrop = row.AllowCrop;
            reference.loadOnEnable = Equals(row.AssetMode, "Streaming");
            reference.revealOnLoad = row.BindingKey != "DisabledOverlay" &&
                                     row.BindingKey != "BloodBond" &&
                                     row.BindingKey != "CardOverGlow";
        }
        AttachBinding(host, row);
        return host;
    }

    private static void AttachBinding(GameObject target, BattlefieldUiLayoutRow row)
    {
        PsdUiBinding binding = target.AddComponent<PsdUiBinding>();
        binding.bindingKey = row.BindingKey;
        binding.templateId = row.TemplateID;
        binding.templateRole = row.TemplateRole;
        binding.sourcePath = row.Path;
        var identity = target.AddComponent<UiSyncIdentity>();
        identity.id = StableId(row);
    }

    private static string StableId(BattlefieldUiLayoutRow row) => !string.IsNullOrWhiteSpace(row.StableID)
        ? row.StableID : string.Join("/", row.Path.Split('/').Select(part =>
            part.Length > 0 && char.IsDigit(part[0]) ? part.Split(':')[0] : part));

    private static string InstanceIdentity(string instance, string template, string child) => child == template
        ? instance : child.StartsWith(template + "/", StringComparison.Ordinal)
            ? instance + child.Substring(template.Length) : instance + "/@" + child;

    // Builds only an inactive UI proposal. No scene save, battle runtime, camera or asset publication.
    internal static Canvas BuildConfigurationPreview()
    {
        var rows = BattlefieldUiLayoutTable.Load(Path.Combine(Application.streamingAssetsPath, "Charts", "UI", "BattleField1.csv"));
        Validate(rows);
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ProjectUiFont.FontAssetPath);
        if (font == null) throw new InvalidDataException("项目 TMP 字体资产缺失。");
        Canvas canvas = CreateCanvas();
        canvas.gameObject.SetActive(false);
        canvas.gameObject.hideFlags = HideFlags.HideAndDontSave;
        Scene previewScene = EditorSceneManager.NewPreviewScene();
        SceneManager.MoveGameObjectToScene(canvas.gameObject, previewScene);
        canvas.gameObject.AddComponent<UiSyncIdentity>().id = "BattleField1Canvas";
        try
        {
            var nodes = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            var parents = new Dictionary<string, BattlefieldUiLayoutRow>(StringComparer.OrdinalIgnoreCase);
            var templates = new Dictionary<string, GameObject>();
            foreach (var row in rows.Where(r => !Equals(r.AssetMode, "Ignore") && !Equals(r.AssetMode, "Instance")))
            {
                var node = CreateNode(row, FindParent(row.Path, nodes, canvas.transform), FindParentRow(row.Path, parents), font);
                nodes.Add(row.Path, node); parents.Add(row.Path, row);
                if (Equals(row.TemplateRole, "template")) templates[row.TemplateID] = node;
            }
            foreach (var row in rows.Where(r => Equals(r.AssetMode, "Instance")))
            {
                var template = templates[row.TemplateID];
                var node = UnityEngine.Object.Instantiate(template, FindParent(row.Path, nodes, canvas.transform), false);
                node.name = row.Name;
                node.GetComponent<RectTransform>().anchoredPosition += new Vector2(row.OffsetX, row.OffsetY);
                string prefix = template.GetComponent<UiSyncIdentity>().id;
                foreach (var identity in node.GetComponentsInChildren<UiSyncIdentity>(true))
                    identity.id = InstanceIdentity(StableId(row), prefix, identity.id);
                var binding = node.GetComponent<PsdUiBinding>();
                binding.sourcePath = row.Path; binding.templateRole = "instance";
                AdjustElementAuras(node, row.LayoutVariant); ApplyInstanceText(node, row);
                nodes[row.Path] = node;
            }
            foreach (var keycap in canvas.GetComponentsInChildren<PsdUiBinding>(true))
            {
                if (keycap.bindingKey != "KeycapQ" && keycap.bindingKey != "KeycapW" && keycap.bindingKey != "KeycapE") continue;
                int ordinal = FirstNumber(keycap.transform.parent.name);
                keycap.gameObject.SetActive(keycap.bindingKey == (ordinal == 2 ? "KeycapW" : ordinal == 3 ? "KeycapE" : "KeycapQ"));
            }
            BattlefieldHealthBarBuilder.Configure(canvas);
            BattlefieldHealthBarBuilder.ConfigurePsdLayers(canvas);
            UiConfigurationSyncWindow.ConfigureInitialVectorRendering(canvas.gameObject);
            EnsureTargetSelectionGlows(canvas);
            return canvas;
        }
        catch { EditorSceneManager.ClosePreviewScene(previewScene); throw; }
    }

    private static void ApplyRect(
        RectTransform rect,
        BattlefieldUiLayoutRow row,
        BattlefieldUiLayoutRow parentRow)
    {
        Vector2 anchor = parentRow == null ? Anchor(row.Anchor) : new Vector2(0.5f, 0.5f);
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = parentRow == null
            ? new Vector2(row.OffsetX, row.OffsetY)
            : DesignPosition(row) - DesignPosition(parentRow);
        rect.sizeDelta = new Vector2(row.Width, row.Height);

        float parentWidth = parentRow?.Width ?? DesignWidth;
        float parentHeight = parentRow?.Height ?? DesignHeight;

        if (Equals(row.SizeMode, "StretchX") || Equals(row.SizeMode, "StretchXY"))
        {
            rect.anchorMin = new Vector2(0f, rect.anchorMin.y);
            rect.anchorMax = new Vector2(1f, rect.anchorMax.y);
            rect.sizeDelta = new Vector2(row.Width - parentWidth, rect.sizeDelta.y);
        }
        if (Equals(row.SizeMode, "StretchY") || Equals(row.SizeMode, "StretchXY"))
        {
            rect.anchorMin = new Vector2(rect.anchorMin.x, 0f);
            rect.anchorMax = new Vector2(rect.anchorMax.x, 1f);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, row.Height - parentHeight);
        }
    }

    private static Vector2 DesignPosition(BattlefieldUiLayoutRow row)
    {
        Vector2 anchor = Anchor(row.Anchor);
        return new Vector2(
            DesignCoordinate(anchor.x, row.OffsetX, DesignWidth),
            DesignCoordinate(anchor.y, row.OffsetY, DesignHeight));
    }

    private static Vector2 Anchor(string value)
    {
        string anchor = value ?? string.Empty;
        float x = anchor.EndsWith("Left", StringComparison.OrdinalIgnoreCase) ? 0f :
            anchor.EndsWith("Right", StringComparison.OrdinalIgnoreCase) ? 1f : 0.5f;
        float y = anchor.StartsWith("Top", StringComparison.OrdinalIgnoreCase) ? 1f :
            anchor.StartsWith("Bottom", StringComparison.OrdinalIgnoreCase) ? 0f : 0.5f;
        return new Vector2(x, y);
    }

    private static Transform FindParent(string path, Dictionary<string, GameObject> byPath, Transform fallback)
    {
        int slash = path.LastIndexOf('/');
        if (slash <= 0) return fallback;
        return byPath.TryGetValue(path.Substring(0, slash), out GameObject parent) ? parent.transform : fallback;
    }

    private static BattlefieldUiLayoutRow FindParentRow(
        string path,
        Dictionary<string, BattlefieldUiLayoutRow> byPath)
    {
        int slash = path.LastIndexOf('/');
        if (slash <= 0) return null;
        byPath.TryGetValue(path.Substring(0, slash), out BattlefieldUiLayoutRow parent);
        return parent;
    }

    private static void StretchToParent(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    private static Color GeneratedColor(BattlefieldUiLayoutRow row)
    {
        // PSD Blackfold is only an opacity reference (128/255), not an exported image.
        if (row.BindingKey == "ScreenDimmer") return new Color(0f, 0f, 0f, 128f / 255f);
        if (row.BindingKey == "ValueBar") return new Color32(0x99, 0x8F, 0x81, 0xFF);
        if (row.BindingKey == "CancelIcon" || row.BindingKey == "SwitchIcon")
            return new Color32(0xFE, 0xFE, 0xFE, 0xFF);
        return Equals(row.AssetMode, "Generated") ? new Color(1f, 1f, 1f, 0f) : Color.white;
    }

    /// <summary>
    /// Keep the target-selection dimmer next to the background. At runtime,
    /// BattlefieldSelectionPresentation moves the entity containers below it and
    /// lifts the caster/current targets above it as complete card + HUD pairs.
    /// The dimmer is visual-only and must never consume pointer events.
    /// </summary>
    internal static void EnsureTargetSelectionGlows(Canvas canvas)
    {
        // 公共PNG的原始画布：卡牌517x885、辉光573x941。透明度已在资源中，不再额外乘色。
        foreach (var art in canvas.GetComponentsInChildren<PsdUiBinding>(true)
                     .Where(item => item.bindingKey == "CharacterCardArt" || item.bindingKey == "EnemyCardArt"))
        {
            Transform card = art.transform.parent;
            if (card == null || card.GetComponentsInChildren<PsdUiBinding>(true)
                .Any(item => item.bindingKey == "CardOverGlow")) continue;
            RectTransform basis = card.Find("CardShadow") as RectTransform;
            bool fromShadow = basis != null;
            if (basis == null) basis = art.GetComponent<RectTransform>();
            if (basis == null) continue;
            var node = new GameObject("CardOverGlow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            node.SetActive(false);
            var rect = node.GetComponent<RectTransform>();
            rect.SetParent(card, false);
            rect.anchorMin = basis.anchorMin; rect.anchorMax = basis.anchorMax; rect.pivot = basis.pivot;
            rect.anchoredPosition = basis.anchoredPosition;
            rect.localRotation = basis.localRotation; rect.localScale = basis.localScale;
            rect.sizeDelta = fromShadow
                ? Vector2.Scale(basis.sizeDelta, new Vector2(573f / 555f, 941f / 923f))
                : basis.sizeDelta + new Vector2(basis.rect.width * (573f / 517f - 1f), basis.rect.height * (941f / 885f - 1f));
            rect.SetSiblingIndex(fromShadow ? basis.GetSiblingIndex() + 1 : art.transform.GetSiblingIndex());
            Image image = node.GetComponent<Image>();
            image.enabled = false; image.raycastTarget = false; image.color = Color.white;
            var binding = node.AddComponent<PsdUiBinding>();
            binding.bindingKey = "CardOverGlow"; binding.sourcePath = art.sourcePath + "/component:CardOverGlow";
            var reference = node.AddComponent<StreamingSpriteReference>();
            reference.relativePath = "art_assets/UI/BattleField1/UI_components/card/CardOverGlow.png";
            reference.loadOnEnable = true; reference.revealOnLoad = false; reference.allowCrop = true;
            node.SetActive(true);
        }
    }

    private static void EnforceTargetSelectionLayering(Canvas canvas)
    {
        PsdUiBinding[] bindings = canvas.GetComponentsInChildren<PsdUiBinding>(true);
        PsdUiBinding background = bindings.FirstOrDefault(item => item.bindingKey == "BattleBackground");
        PsdUiBinding dimmer = bindings.FirstOrDefault(item => item.bindingKey == "ScreenDimmer");
        if (dimmer == null) return;

        Image dimmerImage = dimmer.GetComponent<Image>() ?? dimmer.GetComponentInChildren<Image>(true);
        if (dimmerImage != null)
        {
            dimmerImage.raycastTarget = false;
            dimmerImage.enabled = false;
        }

        if (background == null || background.transform.parent != dimmer.transform.parent) return;
        background.transform.SetSiblingIndex(0);
        dimmer.transform.SetSiblingIndex(1);
    }

    private static void AdjustElementAuras(GameObject instance, string variant)
    {
        if (!Equals(variant, "Top") && !Equals(variant, "Bottom")) return;
        PsdUiBinding art = instance.GetComponentsInChildren<PsdUiBinding>(true)
            .FirstOrDefault(item => item.bindingKey == "CharacterCardArt" || item.bindingKey == "EnemyCardArt");
        if (art == null) return;
        RectTransform artRect = art.GetComponent<RectTransform>();
        RectTransform[] auras = instance.GetComponentsInChildren<PsdUiBinding>(true)
            .Where(item => item.bindingKey == "ElementAura")
            .Select(item => item.GetComponent<RectTransform>()).Where(item => item != null)
            .OrderBy(item => item.anchoredPosition.y).ToArray();
        if (auras.Length == 0) return;
        float artX = DesignCoordinate(artRect.anchorMin.x, artRect.anchoredPosition.x, DesignWidth);
        float artY = DesignCoordinate(artRect.anchorMin.y, artRect.anchoredPosition.y, DesignHeight);
        bool enemy = art.bindingKey == "EnemyCardArt";
        float direction = Equals(variant, "Top") ? 1f : -1f;
        float edge = artY + direction * artRect.rect.height * 0.5f;
        for (int index = 0; index < auras.Length; index++)
        {
            float inward = auras[index].rect.height * 0.5f + index * (auras[index].rect.height + 4f);
            Vector2 position = auras[index].anchoredPosition;
            float desiredY = edge - direction * inward;
            float desiredX = artX + (enemy ? -1f : 1f) *
                (artRect.rect.width * 0.5f + 9f + auras[index].rect.width * 0.5f);
            position.x = desiredX - (auras[index].anchorMin.x - 0.5f) * DesignWidth;
            position.y = desiredY - (auras[index].anchorMin.y - 0.5f) * DesignHeight;
            auras[index].anchoredPosition = position;
        }
        var auraBar = instance.GetComponent<BattlefieldElementAuraBar>() ?? instance.AddComponent<BattlefieldElementAuraBar>();
        auraBar.side = enemy ? BattleSide.Enemy : BattleSide.Ally;
        auraBar.slot = FirstNumber(instance.name);
        auraBar.topAligned = Equals(variant, "Top");
        auraBar.gap = 4;
        auraBar.icons = (auraBar.topAligned ? auras.OrderByDescending(rect => rect.anchoredPosition.y)
            : auras.OrderBy(rect => rect.anchoredPosition.y)).Select(rect => rect.GetComponent<Image>()).ToArray();
    }

    /// <summary>
    /// Repeated PSD groups serialize only one set of children. Restore the text
    /// that differs per clone while retaining the exact Photoshop child offsets.
    /// </summary>
    private static void ApplyInstanceText(GameObject instance, BattlefieldUiLayoutRow row)
    {
        int ordinal = FirstNumber(row.Name);
        if (ordinal == int.MaxValue) return;

        TMP_Text burstKey = instance.GetComponentsInChildren<TMP_Text>(true)
            .FirstOrDefault(item => item.gameObject.name == "Burst_Key");
        if (burstKey != null)
        {
            burstKey.text = ordinal.ToString();
            // Photoshop's glyph boxes differ by numeral; keep their visual
            // centres fixed while giving each glyph its measured width.
            float[] widths = { 7f, 13f, 12f, 15f };
            float[] heights = { 18f, 17f, 17f, 18f };
            if (ordinal >= 1 && ordinal <= widths.Length)
            {
                RectTransform rect = burstKey.rectTransform;
                rect.sizeDelta = new Vector2(widths[ordinal - 1], heights[ordinal - 1]);
                // Badge digits share the same visual centre; glyph bounds must
                // not reintroduce Photoshop's per-digit hand-placement offsets.
            }
        }

        TMP_Text skillKey = instance.GetComponentsInChildren<TMP_Text>(true)
            .FirstOrDefault(item => item.gameObject.name == "Q");
        if (skillKey == null) return;
        string[] shortcuts = { "Q", "W", "E" };
        if (ordinal < 1 || ordinal > shortcuts.Length) return;
        skillKey.text = shortcuts[ordinal - 1];
        float[] shortcutWidths = { 16f, 23f, 13f };
        float[] shortcutHeights = { 21f, 17f, 17f };
        float[] shortcutXOffsets = { 0f, 0.5f, -1.5f };
        float[] shortcutYOffsets = { 0f, 0f, 1f };
        RectTransform shortcutRect = skillKey.rectTransform;
        shortcutRect.sizeDelta = new Vector2(shortcutWidths[ordinal - 1], shortcutHeights[ordinal - 1]);
        shortcutRect.anchoredPosition += new Vector2(
            shortcutXOffsets[ordinal - 1], shortcutYOffsets[ordinal - 1]);
    }

    private static int FirstNumber(string value)
    {
        int result = 0;
        bool found = false;
        foreach (char character in value ?? string.Empty)
        {
            if (character >= '0' && character <= '9')
            {
                found = true;
                result = result * 10 + character - '0';
            }
            else if (found) break;
        }
        return found ? result : int.MaxValue;
    }

    private static float DesignCoordinate(float anchor, float offset, float designSize) =>
        (anchor - 0.5f) * designSize + offset;

    private static void Validate(IEnumerable<BattlefieldUiLayoutRow> rows)
    {
        var sizeModes = new HashSet<string>(new[] { "Fixed", "Uniform", "StretchX", "StretchY", "StretchXY" },
            StringComparer.OrdinalIgnoreCase);
        var assetModes = new HashSet<string>(new[] { "Ignore", "Group", "Instance", "Text", "Generated", "Streaming", "DynamicStreaming" },
            StringComparer.OrdinalIgnoreCase);
        foreach (BattlefieldUiLayoutRow row in rows)
        {
            if (!sizeModes.Contains(row.SizeMode)) throw new InvalidDataException(row.Path + " has invalid SizeMode: " + row.SizeMode);
            if (!assetModes.Contains(row.AssetMode)) throw new InvalidDataException(row.Path + " has invalid AssetMode: " + row.AssetMode);
            if ((Equals(row.AssetMode, "Streaming") || Equals(row.AssetMode, "DynamicStreaming")) &&
                (string.IsNullOrWhiteSpace(row.StreamingAssetPath) || row.StreamingAssetPath == "-"))
                throw new InvalidDataException(row.Path + " has no StreamingAssetPath.");
        }
    }

    private static bool Equals(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
