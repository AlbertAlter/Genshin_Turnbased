using UnityEngine;
using UnityEngine.UI;

/// <summary>All irregular gold fragments share one UI mesh/material, not one GameObject per particle.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UIGoldenDissolveFragments : MaskableGraphic
{
    private Texture source;
    private UIGoldenDissolveStyle style;
    private GoldenDissolveMath.Fragment[] fragments;
    private Vector2 cardSize;
    private float seconds;
    private Material ownedMaterial;
    public override Texture mainTexture => source != null ? source : Texture2D.whiteTexture;

    public void Bind(Texture snapshot, Vector2 size, UIGoldenDissolveStyle settings, int seed)
    {
        var shader = Resources.Load<Shader>("UI/GoldenDissolveFragments");
        if (shader == null) throw new System.InvalidOperationException("Missing GoldenDissolveFragments shader.");
        source = snapshot; cardSize = size; style = settings;
        fragments = GoldenDissolveMath.BuildFragments(settings, size, seed);
        if (ownedMaterial == null) ownedMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        ownedMaterial.SetColor("_Gold", settings.EdgeColor);
        ownedMaterial.SetColor("_Highlight", settings.EdgeHighlight);
        ownedMaterial.SetFloat("_Roughness", settings.FragmentRoughness);
        material = ownedMaterial; raycastTarget = false;
        seconds = 0;
        SetAllDirty();
    }

    public void SetElapsed(float value)
    {
        seconds = Mathf.Max(0, value);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (fragments == null || style == null) return;
        foreach (var f in fragments)
        {
            if (!GoldenDissolveMath.Evaluate(f, style, seconds, out Vector2 drift, out float size, out float alpha)) continue;
            Vector2 center = new Vector2((f.UV.x - .5f) * cardSize.x, (f.UV.y - .5f) * cardSize.y) + drift;
            float cos = Mathf.Cos(f.Rotation), sin = Mathf.Sin(f.Rotation);
            int start = mesh.currentVertCount;
            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector2(i < 2 ? -1 : 1, i == 0 || i == 3 ? -1 : 1);
                var rotated = new Vector2(corner.x * cos - corner.y * sin, corner.x * sin + corner.y * cos);
                var vertex = UIVertex.simpleVert;
                vertex.position = center + rotated * size * .5f;
                vertex.uv0 = new Vector4(corner.x, corner.y, f.UV.x, f.UV.y);
                Color tint = color; tint.a *= alpha;
                vertex.color = tint;
                mesh.AddVert(vertex);
            }
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }
    }

    protected override void OnDestroy()
    {
        if (ownedMaterial != null)
        {
            if (Application.isPlaying) Destroy(ownedMaterial); else DestroyImmediate(ownedMaterial);
        }
        base.OnDestroy();
    }
}
