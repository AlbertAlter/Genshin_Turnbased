using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Roguelike.Tests.RouteGeneration
{
    public sealed class RouteTestController : MonoBehaviour
    {
        [SerializeField] private RouteGenerationSettings generationSettings = new RouteGenerationSettings();
        [SerializeField] private bool useFixedSeed;
        [SerializeField] private int fixedSeed = 12345;
        [SerializeField] private bool logRandomDraws = true;
        [SerializeField, Min(1f)] private float scrollSpeed = 900f;

        private const string StageConfigRelativePath = "Data/Roguelike/StageConfig.xlsx";
        private const string StageRuleRelativePath = "Data/Roguelike/StageRule.xlsx";

        private RouteMapView _view;
        private RouteHorizontalBrowser _browser;
        private Text _statusText;
        private Text _debugToggleLabel;
        private Font _font;

        private void Awake()
        {
            BuildInterface();
            GenerateRoute();
        }

        public void GenerateRoute()
        {
            try
            {
                string workbookPath = Path.Combine(Application.streamingAssetsPath,
                    StageConfigRelativePath.Replace('/', Path.DirectorySeparatorChar));
                string ruleWorkbookPath = Path.Combine(Application.streamingAssetsPath,
                    StageRuleRelativePath.Replace('/', Path.DirectorySeparatorChar));
                var loader = new RouteConfigLoader();
                var configs = loader.Load(workbookPath);
                string ruleDescription;
                try
                {
                    generationSettings.Apply(loader.LoadRouteUiRules(ruleWorkbookPath));
                    ruleDescription = "StageRule";
                }
                catch (Exception exception)
                {
                    ruleDescription = "scene fallback";
                    Debug.LogWarning("StageRule load failed; using RouteTest scene fallback values. " + exception.Message, this);
                }
                IBattleRandomSource randomSource = useFixedSeed
                    ? (IBattleRandomSource)new SystemBattleRandomSource(fixedSeed)
                    : BattleRandom.Source;
                RouteRandomLog randomLog = logRandomDraws ? new RouteRandomLog() : null;
                randomLog?.Begin(randomSource.GetType().Name + (useFixedSeed ? " seed=" + fixedSeed : string.Empty));
                RouteGenerationResult result = new RouteMapGenerator(generationSettings, randomSource, randomLog).Generate(configs);
                if (randomLog != null)
                    Debug.Log(randomLog.Complete(result), this);
                if (!result.Success)
                {
                    ShowError(result.Error);
                    return;
                }

                _view.Render(result.Map, generationSettings);
                Canvas.ForceUpdateCanvases();
                _browser.ResetToStart();
                _browser.ClampToBounds();
                int nodeCount = result.Map.Stages.Sum(stage => stage.Nodes.Count);
                string seedDescription = useFixedSeed ? "fixed seed " + fixedSeed : "BattleRandom";
                _statusText.text = result.Map.Stages.Count + " stages  |  " + nodeCount + " nodes  |  "
                                   + result.Map.Edges.Count + " edges  |  attempt " + result.Map.AttemptNumber
                                   + "  |  " + seedDescription + "  |  " + ruleDescription;
                _statusText.color = new Color(0.88f, 0.92f, 1f, 1f);
            }
            catch (Exception exception)
            {
                ShowError(exception.Message);
            }
        }

        private void ShowError(string message)
        {
            string error = "Route generation failed: " + message;
            _statusText.text = error;
            _statusText.color = new Color(1f, 0.45f, 0.40f, 1f);
            Debug.LogError(error, this);
        }

        private void BuildInterface()
        {
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer < 0)
                uiLayer = 5;
            _font = LoadBuiltinFont();

            EnsureEventSystem(uiLayer);
            GameObject canvasObject = CreateUiObject("Canvas", null, uiLayer,
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            RectTransform background = CreatePanel("Background", canvasObject.transform, uiLayer,
                new Color(0.055f, 0.075f, 0.115f, 1f));
            Stretch(background);
            background.GetComponent<Image>().raycastTarget = true;

            RectTransform topBar = CreatePanel("TopControlBar", canvasObject.transform, uiLayer,
                Color.clear);
            topBar.anchorMin = new Vector2(0f, 1f);
            topBar.anchorMax = new Vector2(1f, 1f);
            topBar.pivot = new Vector2(0.5f, 1f);
            topBar.sizeDelta = new Vector2(0f, 60f);
            topBar.anchoredPosition = Vector2.zero;
            _statusText = CreateText("StatusText", topBar, uiLayer, 22, TextAnchor.MiddleLeft);
            RectTransform statusRect = _statusText.rectTransform;
            Stretch(statusRect);
            statusRect.offsetMin = new Vector2(24f, 0f);
            statusRect.offsetMax = new Vector2(-24f, 0f);

            RectTransform routePlane = CreatePanel("RoutePlane", canvasObject.transform, uiLayer,
                new Color(0.075f, 0.105f, 0.155f, 1f));
            routePlane.anchorMin = Vector2.zero;
            routePlane.anchorMax = Vector2.one;
            routePlane.offsetMin = Vector2.zero;
            routePlane.offsetMax = Vector2.zero;
            routePlane.gameObject.AddComponent<RectMask2D>();

            RectTransform routeContent = CreateRect("RouteContent", routePlane, uiLayer);
            routeContent.anchorMin = Vector2.zero;
            routeContent.anchorMax = new Vector2(0f, 1f);
            routeContent.pivot = Vector2.zero;
            routeContent.anchoredPosition = Vector2.zero;
            routeContent.sizeDelta = new Vector2(1920f, 0f);
            RectTransform edgeLayer = CreateRect("EdgeLayer", routeContent, uiLayer);
            Stretch(edgeLayer);
            RectTransform debugLayer = CreateRect("CoordinateDebugLayer", routeContent, uiLayer);
            Stretch(debugLayer);
            RectTransform nodeLayer = CreateRect("NodeLayer", routeContent, uiLayer);
            Stretch(nodeLayer);

            // The browser lives on the Canvas so drag events from any UI area can bubble to it.
            _browser = canvasObject.AddComponent<RouteHorizontalBrowser>();
            _browser.Initialize(routePlane, routeContent, scrollSpeed);
            _view = routePlane.gameObject.AddComponent<RouteMapView>();
            _view.Initialize(routeContent, debugLayer, edgeLayer, nodeLayer, _font,
                new Vector2(generationSettings.NodeWidth, generationSettings.NodeHeight));

            RectTransform bottomBar = CreatePanel("BottomControlBar", canvasObject.transform, uiLayer,
                Color.clear);
            bottomBar.anchorMin = Vector2.zero;
            bottomBar.anchorMax = new Vector2(1f, 0f);
            bottomBar.pivot = new Vector2(0.5f, 0f);
            bottomBar.sizeDelta = new Vector2(0f, 60f);
            bottomBar.anchoredPosition = Vector2.zero;
            Button generateButton = CreateButton("GenerateButton", bottomBar, uiLayer, "生成路线");
            RectTransform generateRect = generateButton.GetComponent<RectTransform>();
            generateRect.anchorMin = new Vector2(1f, 0.5f);
            generateRect.anchorMax = new Vector2(1f, 0.5f);
            generateRect.pivot = new Vector2(1f, 0.5f);
            generateRect.anchoredPosition = new Vector2(-12f, 0f);
            generateRect.sizeDelta = new Vector2(220f, 48f);
            generateButton.onClick.AddListener(GenerateRoute);

            Button debugButton = CreateButton("CoordinateDebugButton", bottomBar, uiLayer, "隐藏坐标辅助");
            RectTransform debugRect = debugButton.GetComponent<RectTransform>();
            debugRect.anchorMin = new Vector2(1f, 0.5f);
            debugRect.anchorMax = new Vector2(1f, 0.5f);
            debugRect.pivot = new Vector2(1f, 0.5f);
            debugRect.anchoredPosition = new Vector2(-244f, 0f);
            debugRect.sizeDelta = new Vector2(220f, 48f);
            _debugToggleLabel = debugButton.GetComponentInChildren<Text>();
            debugButton.onClick.AddListener(ToggleCoordinateDebug);

            CreateHoldControl("HoldLeftButton", routePlane, uiLayer, "<", true);
            CreateHoldControl("HoldRightButton", routePlane, uiLayer, ">", false);

            // 路线区域铺满屏幕后，控制文字和按钮作为透明悬浮层保持在最上方。
            topBar.SetAsLastSibling();
            bottomBar.SetAsLastSibling();
        }

        private void ToggleCoordinateDebug()
        {
            bool visible = _view.ToggleCoordinateDebug();
            if (_debugToggleLabel != null)
                _debugToggleLabel.text = visible ? "隐藏坐标辅助" : "显示坐标辅助";
        }

        private void CreateHoldControl(string name, RectTransform parent, int layer, string label, bool left)
        {
            Button button = CreateButton(name, parent, layer, label);
            RectTransform rect = button.GetComponent<RectTransform>();
            float side = left ? 0f : 1f;
            rect.anchorMin = new Vector2(side, 0.5f);
            rect.anchorMax = new Vector2(side, 0.5f);
            rect.pivot = new Vector2(side, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(72f, 160f);
            Image image = button.GetComponent<Image>();
            image.color = new Color(0.08f, 0.12f, 0.19f, 0.86f);
            HoldScrollButton hold = button.gameObject.AddComponent<HoldScrollButton>();
            hold.Initialize(_browser, left ? -1 : 1);
        }

        private Button CreateButton(string name, Transform parent, int layer, string labelText)
        {
            GameObject buttonObject = CreateUiObject(name, parent, layer, typeof(Image), typeof(Button));
            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.19f, 0.31f, 0.48f, 1f);
            Button button = buttonObject.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.27f, 0.43f, 0.65f, 1f);
            colors.pressedColor = new Color(0.13f, 0.22f, 0.35f, 1f);
            button.colors = colors;
            Text label = CreateText("Label", buttonObject.transform, layer, 24, TextAnchor.MiddleCenter);
            label.text = labelText;
            Stretch(label.rectTransform);
            return button;
        }

        private Text CreateText(string name, Transform parent, int layer, int size, TextAnchor alignment)
        {
            GameObject textObject = CreateUiObject(name, parent, layer, typeof(Text));
            Text text = textObject.GetComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform CreatePanel(string name, Transform parent, int layer, Color color)
        {
            GameObject panel = CreateUiObject(name, parent, layer, typeof(Image));
            Image image = panel.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return panel.GetComponent<RectTransform>();
        }

        private static RectTransform CreateRect(string name, Transform parent, int layer)
        {
            return CreateUiObject(name, parent, layer).GetComponent<RectTransform>();
        }

        private static GameObject CreateUiObject(string name, Transform parent, int layer, params Type[] components)
        {
            Type[] componentTypes = new Type[components.Length + 1];
            componentTypes[0] = typeof(RectTransform);
            Array.Copy(components, 0, componentTypes, 1, components.Length);
            GameObject result = new GameObject(name, componentTypes);
            result.layer = layer;
            if (parent != null)
                result.transform.SetParent(parent, false);
            return result;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void EnsureEventSystem(int layer)
        {
            if (FindObjectOfType<EventSystem>() != null)
                return;
            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            eventSystem.layer = layer;
        }

        private static Font LoadBuiltinFont()
        {
            try
            {
                Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (font != null)
                    return font;
            }
            catch (ArgumentException)
            {
                // Unity versions differ in the built-in legacy font resource name.
            }
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
    }
}
