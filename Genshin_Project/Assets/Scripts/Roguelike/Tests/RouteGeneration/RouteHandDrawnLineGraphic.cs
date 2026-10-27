using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Roguelike.Tests.RouteGeneration
{
    /// <summary>One batched UI mesh per edge (or per brush variant), not one GameObject per dash.</summary>
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class RouteHandDrawnLineGraphic : MaskableGraphic
    {
        private RouteMapStyle _style;
        private List<RouteDash> _dashes;
        private uint _seed;
        private float _length;
        private Sprite _brush;
        private int _brushIndex, _brushCount;
        private readonly List<Vector2> _outline = new List<Vector2>(40);
        public override Texture mainTexture => _brush != null ? _brush.texture : Texture2D.whiteTexture;

        public void Configure(RouteMapStyle style, List<RouteDash> dashes, uint seed, float length,
            Sprite brush = null, int brushIndex = 0, int brushCount = 1)
        {
            _style = style; _dashes = dashes; _seed = seed; _length = length;
            _brush = brush; _brushIndex = brushIndex; _brushCount = brushCount;
            color = new Color(style.LineColor.r, style.LineColor.g, style.LineColor.b, style.LineOpacity);
            raycastTarget = false;
            SetAllDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_style == null || _dashes == null) return;
            foreach (RouteDash dash in _dashes)
            {
                if (_brush != null)
                {
                    int variant = Mathf.Min(_brushCount - 1,
                        Mathf.FloorToInt((RouteDashLayout.Noise(_seed, dash.Index, 10) + 1f) * 0.5f * _brushCount));
                    if (variant == _brushIndex) AddBrush(vh, dash);
                }
                else AddStroke(vh, dash);
            }
        }

        private Vector2 Place(Vector2 point, RouteDash dash)
        {
            float angle = dash.Angle * Mathf.Deg2Rad;
            float x = point.x - dash.Length * 0.5f;
            Vector2 placed = new Vector2(dash.Start + dash.Length * 0.5f + x * Mathf.Cos(angle) - point.y * Mathf.Sin(angle),
                dash.Offset + x * Mathf.Sin(angle) + point.y * Mathf.Cos(angle));
            placed.x = Mathf.Clamp(placed.x, 0f, _length);
            return placed;
        }

        private void AddBrush(VertexHelper vh, RouteDash dash)
        {
            int first = vh.currentVertCount;
            Rect uv = _brush.textureRect;
            Vector2 min = new Vector2(uv.xMin / _brush.texture.width, uv.yMin / _brush.texture.height);
            Vector2 max = new Vector2(uv.xMax / _brush.texture.width, uv.yMax / _brush.texture.height);
            float half = dash.Width * 0.5f;
            vh.AddVert(Place(new Vector2(0, -half), dash), color, min);
            vh.AddVert(Place(new Vector2(0, half), dash), color, new Vector2(min.x, max.y));
            vh.AddVert(Place(new Vector2(dash.Length, half), dash), color, max);
            vh.AddVert(Place(new Vector2(dash.Length, -half), dash), color, new Vector2(max.x, min.y));
            vh.AddTriangle(first, first + 1, first + 2);
            vh.AddTriangle(first, first + 2, first + 3);
        }

        private void AddStroke(VertexHelper vh, RouteDash dash)
        {
            _outline.Clear();
            int samples = _style.SamplesPerDash;
            float halfWidth = dash.Width * 0.5f;
            float endWidth = halfWidth * (1f - _style.EndTaper);
            float cap = Mathf.Min(endWidth, dash.Length * 0.5f) * _style.EndRoundness;
            for (int i = 0; i < samples; i++)
            {
                float t = i / (samples - 1f);
                _outline.Add(new Vector2(Mathf.Lerp(cap, dash.Length - cap, t), HalfWidth(dash, t, i, 20)));
            }
            const int capSteps = 6;
            for (int i = 1; i <= capSteps; i++)
            {
                float angle = Mathf.Lerp(90, -90, i / (float)capSteps) * Mathf.Deg2Rad;
                _outline.Add(new Vector2(dash.Length - cap + Mathf.Cos(angle) * cap, Mathf.Sin(angle) * endWidth));
            }
            for (int i = samples - 2; i >= 0; i--)
            {
                float t = i / (samples - 1f);
                _outline.Add(new Vector2(Mathf.Lerp(cap, dash.Length - cap, t), -HalfWidth(dash, t, i, 40)));
            }
            for (int i = 1; i < capSteps; i++)
            {
                float angle = Mathf.Lerp(-90, -270, i / (float)capSteps) * Mathf.Deg2Rad;
                _outline.Add(new Vector2(cap + Mathf.Cos(angle) * cap, Mathf.Sin(angle) * endWidth));
            }

            int center = vh.currentVertCount;
            vh.AddVert(Place(new Vector2(dash.Length * 0.5f, 0), dash), color, Vector2.zero);
            Color clear = color; clear.a = 0;
            for (int i = 0; i < _outline.Count; i++)
            {
                Vector2 point = _outline[i];
                Vector2 before = point - _outline[(i + _outline.Count - 1) % _outline.Count];
                Vector2 after = _outline[(i + 1) % _outline.Count] - point;
                Vector2 normal = (new Vector2(-before.y, before.x).normalized +
                                  new Vector2(-after.y, after.x).normalized).normalized;
                vh.AddVert(Place(point, dash), color, Vector2.zero);
                // A narrow alpha fringe keeps 3px strokes smooth while zooming/scaling the Canvas.
                vh.AddVert(Place(point + normal * 0.5f, dash), clear, Vector2.zero);
            }
            for (int i = 0; i < _outline.Count; i++)
            {
                int next = (i + 1) % _outline.Count;
                int inner = center + 1 + i * 2;
                int nextInner = center + 1 + next * 2;
                vh.AddTriangle(center, inner, nextInner);
                vh.AddTriangle(inner, inner + 1, nextInner);
                vh.AddTriangle(nextInner, inner + 1, nextInner + 1);
            }
        }

        private float HalfWidth(RouteDash dash, float t, int sample, int channel)
        {
            float taper = 1f - _style.EndTaper * Mathf.Abs(t * 2f - 1f);
            float rough = RouteDashLayout.Noise(_seed, dash.Index, sample + channel) *
                          _style.EdgeRoughness * Mathf.Sin(t * Mathf.PI);
            return Mathf.Max(0.05f, dash.Width * 0.5f * taper + rough);
        }
    }
}
