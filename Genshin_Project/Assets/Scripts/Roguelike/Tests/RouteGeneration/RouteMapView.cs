using System.Collections;
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
        private readonly Dictionary<RouteNodeType, Sprite> _nodeIcons = new Dictionary<RouteNodeType, Sprite>();
        private readonly Dictionary<string, Sprite> _artSprites = new Dictionary<string, Sprite>();
        private readonly List<Sprite> _brushes = new List<Sprite>();
        private RouteMapStyle _style = new RouteMapStyle();

        public static string GetNodeIconPath(RouteNodeType type)
        {
            return new RouteMapStyle().GetIconPath(type);
        }

        public IEnumerator ApplyStyle(RouteMapStyle style)
        {
            var loaded = new Dictionary<string, Sprite>();
            foreach (string path in style.ArtPaths().Distinct())
            {
                yield return StreamingAssetSpriteLoader.Load(path,
                    sprite => loaded[path] = PrepareNodeIcon(sprite),
                    error => Debug.LogWarning(error, this));
            }
            ClearChildren(_edgeLayer);
            ClearChildren(_nodeLayer);
            ReleaseArt();
            _style = style;
            foreach (KeyValuePair<string, Sprite> art in loaded) _artSprites.Add(art.Key, art.Value);
            foreach (RouteNodeType type in new[] { RouteNodeType.Battle, RouteNodeType.Event, RouteNodeType.Elite })
                if (loaded.TryGetValue(style.GetIconPath(type), out Sprite icon)) _nodeIcons.Add(type, icon);
            if (style.RenderMode == RouteLineMode.BrushTexture)
            {
                foreach (string path in style.BrushTextures)
                    if (loaded.TryGetValue(path, out Sprite brush)) _brushes.Add(brush);
                if (_brushes.Count == 0 || _brushes.Count != style.BrushTextures.Length)
                {
                    _brushes.Clear();
                    Debug.LogWarning("Route brush textures are missing; using procedural strokes.", this);
                }
            }
        }

        private static Sprite PrepareNodeIcon(Sprite source)
        {
            Texture2D texture = source.texture;
            Color32[] pixels = texture.GetPixels32();
            int minX = texture.width;
            int minY = texture.height;
            int maxX = -1;
            int maxY = -1;
            for (int y = 0; y < texture.height; y++)
            for (int x = 0; x < texture.width; x++)
            {
                int index = y * texture.width + x;
                byte alpha = pixels[index].a;
                // Treat the source as an alpha mask so Image.color is the exact UI brown.
                pixels[index] = new Color32(255, 255, 255, alpha);
                if (alpha <= 8) continue;
                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x);
                maxY = Mathf.Max(maxY, y);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            // Unequal transparent padding must not make one node type appear smaller.
            Rect bounds = maxX >= minX
                ? new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1)
                : source.rect;
            Sprite icon = Sprite.Create(texture, bounds, new Vector2(0.5f, 0.5f),
                100f, 0u, SpriteMeshType.FullRect);
            icon.name = source.name;
            Destroy(source);
            return icon;
        }

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
                CreateEdge(nodes[edge.FromNodeId], nodes[edge.ToNodeId]);
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

        private float NodeSize(RouteNodeType type) =>
            _style.UseIcons && _nodeIcons.ContainsKey(type) ? _style.IconSize : _nodeSize.x;

        private void CreateEdge(RouteNodeData from, RouteNodeData to)
        {
            Vector2 delta = to.Position - from.Position;
            float insetFrom = NodeSize(from.Type) * 0.5f + _style.NodeGap;
            float insetTo = NodeSize(to.Type) * 0.5f + _style.NodeGap;
            float length = delta.magnitude - insetFrom - insetTo;
            if (length <= 0f) return;
            uint seed = RouteDashLayout.EdgeSeed(_style.PatternSeed, from.Id, to.Id);
            List<RouteDash> dashes = RouteDashLayout.Build(length, _style, seed);
            GameObject lineObject = new GameObject("Edge_" + from.Id + "_" + to.Id,
                typeof(RectTransform), typeof(CanvasRenderer));
            lineObject.layer = gameObject.layer;
            RectTransform line = lineObject.GetComponent<RectTransform>();
            line.SetParent(_edgeLayer, false);
            line.anchorMin = Vector2.zero;
            line.anchorMax = Vector2.zero;
            line.pivot = new Vector2(0f, 0.5f);
            line.anchoredPosition = from.Position + delta.normalized * insetFrom;
            float height = _style.Width * (1f + _style.WidthVariation) + _style.OffsetVariation * 2f +
                           _style.DashLength * (1f + _style.LengthVariation) * Mathf.Sin(_style.AngleVariation * Mathf.Deg2Rad) + 2f;
            line.sizeDelta = new Vector2(length, height);
            line.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            if (_brushes.Count == 0)
            {
                lineObject.AddComponent<RouteHandDrawnLineGraphic>().Configure(_style, dashes, seed, length);
            }
            else
            {
                for (int i = 0; i < _brushes.Count; i++)
                {
                    GameObject brushObject = new GameObject("Brush_" + i, typeof(RectTransform),
                        typeof(CanvasRenderer), typeof(RouteHandDrawnLineGraphic));
                    brushObject.layer = gameObject.layer;
                    RectTransform brushRect = brushObject.GetComponent<RectTransform>();
                    brushRect.SetParent(line, false);
                    brushRect.anchorMin = Vector2.zero;
                    brushRect.anchorMax = Vector2.one;
                    brushRect.offsetMin = Vector2.zero;
                    brushRect.offsetMax = Vector2.zero;
                    brushRect.pivot = line.pivot;
                    brushObject.GetComponent<RouteHandDrawnLineGraphic>()
                        .Configure(_style, dashes, seed, length, _brushes[i], i, _brushes.Count);
                }
            }
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
            rect.sizeDelta = Vector2.one * NodeSize(node.Type);
            Image image = nodeObject.GetComponent<Image>();
            image.sprite = _circleSprite;
            image.type = Image.Type.Simple;
            if (_style.UseIcons && _nodeIcons.TryGetValue(node.Type, out Sprite icon))
            {
                image.sprite = icon;
                image.preserveAspect = true;
                image.color = new Color(_style.NodeColor.r, _style.NodeColor.g, _style.NodeColor.b, _style.NodeOpacity);
            }
            else
            {
                image.color = GetNodeColor(node.Type);
            }
            image.raycastTarget = false;

            GameObject textObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textObject.layer = gameObject.layer;
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.SetParent(rect, false);
            textRect.anchorMin = new Vector2(0.5f, 0.5f);
            textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.pivot = new Vector2(0.5f, 1f);
            textRect.anchoredPosition = new Vector2(0f, -NodeSize(node.Type) * 0.5f - 5f);
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

        private void ReleaseArt()
        {
            foreach (Sprite icon in _artSprites.Values)
            {
                if (icon == null) continue;
                Destroy(icon.texture);
                Destroy(icon);
            }
            _artSprites.Clear();
            _nodeIcons.Clear();
            _brushes.Clear();
        }

        private void OnDestroy()
        {
            ReleaseArt();
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
