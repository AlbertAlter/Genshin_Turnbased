using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Roguelike.Tests.RouteGeneration
{
    public sealed class RouteMapView : MonoBehaviour
    {
        private RectTransform _content;
        private RectTransform _debugLayer;
        private RectTransform _edgeLayer;
        private RectTransform _nodeLayer;
        private Font _font;
        private Vector2 _nodeSize;
        private Texture2D _circleTexture;
        private Sprite _circleSprite;

        public void Initialize(RectTransform content, RectTransform debugLayer, RectTransform edgeLayer, RectTransform nodeLayer,
            Font font, Vector2 nodeSize)
        {
            _content = content;
            _debugLayer = debugLayer;
            _edgeLayer = edgeLayer;
            _nodeLayer = nodeLayer;
            _font = font;
            _nodeSize = new Vector2(30f, 30f);
            CreateCircleSprite();
        }

        public void Render(RouteMapData map, RouteGenerationSettings settings)
        {
            ClearChildren(_debugLayer);
            ClearChildren(_edgeLayer);
            ClearChildren(_nodeLayer);
            _content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, map.ContentWidth);
            CreateCoordinateDebug(map, settings);

            Dictionary<int, RouteNodeData> nodes = map.Stages.SelectMany(stage => stage.Nodes)
                .ToDictionary(node => node.Id);
            foreach (RouteEdgeData edge in map.Edges)
                CreateEdge(nodes[edge.FromNodeId].Position, nodes[edge.ToNodeId].Position);
            foreach (RouteStageData stage in map.Stages)
                foreach (RouteNodeData node in stage.Nodes)
                    CreateNode(stage, node);
        }

        public bool ToggleCoordinateDebug()
        {
            bool visible = !_debugLayer.gameObject.activeSelf;
            _debugLayer.gameObject.SetActive(visible);
            return visible;
        }

        private void CreateCoordinateDebug(RouteMapData map, RouteGenerationSettings settings)
        {
            for (int stageIndex = 0; stageIndex < map.Stages.Count; stageIndex++)
            {
                float x = settings.StageStartX + stageIndex * settings.StageWidth;
                bool randomizesPosition = stageIndex > 0 && stageIndex < map.Stages.Count - 1;
                for (int slot = 0; slot < 6; slot++)
                {
                    Vector2 standard = new Vector2(
                        x,
                        settings.MidlineHeight + (2.5f - slot) * settings.CoordinateDistance);
                    if (randomizesPosition)
                        CreateRandomRangeCircle(standard, settings.RandomizeRadius);
                    CreateStandardCoordinateDot(standard);
                }

                // 首尾层实际使用中线作为固定标准坐标；六格网格本身没有正中点，需额外标出。
                if (!randomizesPosition)
                    CreateStandardCoordinateDot(new Vector2(x, settings.MidlineHeight));
            }
        }

        private void CreateStandardCoordinateDot(Vector2 position)
        {
            GameObject dotObject = new GameObject(
                "StandardCoordinate",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            dotObject.layer = gameObject.layer;
            RectTransform rect = dotObject.GetComponent<RectTransform>();
            rect.SetParent(_debugLayer, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(6f, 6f);
            Image image = dotObject.GetComponent<Image>();
            image.sprite = _circleSprite;
            image.color = new Color(1f, 0.08f, 0.08f, 1f);
            image.raycastTarget = false;
        }

        private void CreateRandomRangeCircle(Vector2 position, float radius)
        {
            GameObject circleObject = new GameObject(
                "RandomizeRange",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(RouteDebugCircleGraphic));
            circleObject.layer = gameObject.layer;
            RectTransform rect = circleObject.GetComponent<RectTransform>();
            rect.SetParent(_debugLayer, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = Vector2.one * radius * 2f;
            RouteDebugCircleGraphic circle = circleObject.GetComponent<RouteDebugCircleGraphic>();
            circle.Thickness = 3f;
            circle.color = new Color(1f, 0.08f, 0.08f, 0.58f);
            circle.raycastTarget = false;
        }

        private void CreateEdge(Vector2 from, Vector2 to)
        {
            GameObject lineObject = new GameObject("Edge", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            lineObject.layer = gameObject.layer;
            RectTransform line = lineObject.GetComponent<RectTransform>();
            line.SetParent(_edgeLayer, false);
            line.anchorMin = Vector2.zero;
            line.anchorMax = Vector2.zero;
            line.pivot = new Vector2(0f, 0.5f);
            Vector2 delta = to - from;
            line.anchoredPosition = from;
            line.sizeDelta = new Vector2(delta.magnitude, 5f);
            line.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            Image image = lineObject.GetComponent<Image>();
            image.color = new Color(0.78f, 0.83f, 0.92f, 0.72f);
            image.raycastTarget = false;
        }

        private void CreateNode(RouteStageData stage, RouteNodeData node)
        {
            GameObject nodeObject = new GameObject("Node_" + node.Id, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            nodeObject.layer = gameObject.layer;
            RectTransform rect = nodeObject.GetComponent<RectTransform>();
            rect.SetParent(_nodeLayer, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = node.Position;
            rect.sizeDelta = _nodeSize;
            Image image = nodeObject.GetComponent<Image>();
            image.sprite = _circleSprite;
            image.type = Image.Type.Simple;
            image.color = GetNodeColor(node.Type);
            image.raycastTarget = false;

            GameObject textObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textObject.layer = gameObject.layer;
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.SetParent(rect, false);
            textRect.anchorMin = new Vector2(0.5f, 0.5f);
            textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.pivot = new Vector2(0.5f, 1f);
            textRect.anchoredPosition = new Vector2(0f, -20f);
            textRect.sizeDelta = new Vector2(120f, 38f);
            Text label = textObject.GetComponent<Text>();
            label.font = _font;
            label.fontSize = 12;
            label.alignment = TextAnchor.UpperCenter;
            label.color = Color.white;
            label.raycastTarget = false;
            label.text = stage.StageId + "  S" + node.SlotIndex + "\n" + node.Type;
        }

        private void CreateCircleSprite()
        {
            if (_circleSprite != null)
                return;

            const int textureSize = 64;
            float center = (textureSize - 1) * 0.5f;
            float radiusSquared = center * center;
            _circleTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
            {
                name = "RouteNodeCircleTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Color[] pixels = new Color[textureSize * textureSize];
            for (int y = 0; y < textureSize; y++)
            for (int x = 0; x < textureSize; x++)
            {
                float dx = x - center;
                float dy = y - center;
                pixels[y * textureSize + x] = dx * dx + dy * dy <= radiusSquared
                    ? Color.white
                    : Color.clear;
            }
            _circleTexture.SetPixels(pixels);
            _circleTexture.Apply(false, true);
            _circleSprite = Sprite.Create(
                _circleTexture,
                new Rect(0f, 0f, textureSize, textureSize),
                new Vector2(0.5f, 0.5f),
                textureSize);
            _circleSprite.name = "RouteNodeCircleSprite";
        }

        private void OnDestroy()
        {
            if (_circleSprite != null)
                Destroy(_circleSprite);
            if (_circleTexture != null)
                Destroy(_circleTexture);
        }

        private static Color GetNodeColor(RouteNodeType type)
        {
            switch (type)
            {
                case RouteNodeType.Event: return new Color(0.20f, 0.62f, 0.76f, 1f);
                case RouteNodeType.Elite: return new Color(0.67f, 0.28f, 0.78f, 1f);
                case RouteNodeType.Boss: return new Color(0.78f, 0.20f, 0.18f, 1f);
                default: return new Color(0.78f, 0.48f, 0.16f, 1f);
            }
        }

        private static void ClearChildren(Transform parent)
        {
            for (int index = parent.childCount - 1; index >= 0; index--)
            {
                GameObject child = parent.GetChild(index).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
        }
    }

    public sealed class RouteDebugCircleGraphic : MaskableGraphic
    {
        private const int SegmentCount = 64;
        public float Thickness = 3f;

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            Rect rect = rectTransform.rect;
            float outerRadius = Mathf.Min(rect.width, rect.height) * 0.5f;
            float innerRadius = Mathf.Max(0f, outerRadius - Mathf.Max(0f, Thickness));
            Vector2 center = rect.center;

            for (int index = 0; index <= SegmentCount; index++)
            {
                float angle = index * Mathf.PI * 2f / SegmentCount;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                vertexHelper.AddVert(center + direction * outerRadius, color, Vector2.zero);
                vertexHelper.AddVert(center + direction * innerRadius, color, Vector2.zero);
            }

            for (int index = 0; index < SegmentCount; index++)
            {
                int currentOuter = index * 2;
                int currentInner = currentOuter + 1;
                int nextOuter = currentOuter + 2;
                int nextInner = currentOuter + 3;
                vertexHelper.AddTriangle(currentOuter, nextOuter, currentInner);
                vertexHelper.AddTriangle(nextOuter, nextInner, currentInner);
            }
        }
    }
}
