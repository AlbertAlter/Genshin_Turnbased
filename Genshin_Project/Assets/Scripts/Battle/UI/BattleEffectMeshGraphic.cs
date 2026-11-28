using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public enum BattleEffectPart { Flash, Fire, FireCloud, Ring, Spark, Smoke, Ember, Arrow, Impact, BurnFootprint, Flame, LisaCast, LisaStrike, KaeyaIce, KaeyaMist, KaeyaGrain, KaeyaImpact, VentiArc, VentiGust, VentiFeather, VentiElement, DandelionWind, DandelionSeed, DandelionGlow, VentiSigil, VentiUprush, VentiLiftLine }

/// <summary>Borrowed native Unity assets. Formal hosts bind these explicitly; preview resolution is editor-only.</summary>
[Serializable]
public sealed class BattleEffectModelAsset
{
    public GameObject Prefab;
    public AnimationClip Clip;
    public string[] IncludedMaterials;
    public string[] IceMaterials, MistMaterials;
    // Radius in the authored metre space, measured at ReferenceTime in the native clip.
    // This is asset unit metadata, independent of the tunable effect range/size.
    public float ReferenceRadius, ReferenceTime;

    internal bool IncludesMaterial(string materialName)
    {
        if (IncludedMaterials == null) return true;
        return MatchesMaterial(IncludedMaterials, materialName);
    }
    internal bool MatchesMaterial(string[] names, string materialName)
    {
        if (names == null) return false;
        if (string.IsNullOrEmpty(materialName)) return false;
        foreach (string expected in names)
        {
            if (string.IsNullOrEmpty(expected)) continue;
            if (string.Equals(materialName, expected, StringComparison.Ordinal)
                || (Prefab != null && string.Equals(materialName, Prefab.name + "-" + expected, StringComparison.Ordinal)))
                return true;
        }
        return false;
    }
}

public static class BattleEffectModelUnits
{
    public static float ToMetres(float importedRadius, float authoredRadius)
    {
        if (!(importedRadius > 0) || !(authoredRadius > 0) || float.IsInfinity(importedRadius) || float.IsInfinity(authoredRadius))
            throw new InvalidOperationException("特效模型缺少有效的原生尺寸或米制参考尺寸。");
        double ratio = authoredRadius / (double)importedRadius;
        // Accept only recognised unit changes. An unrelated bad transform must fail visibly.
        foreach (float scale in new[] { 1f, .01f, 100f, .001f, 1000f })
            if (Math.Abs(ratio / scale - 1) <= .03) return scale;
        throw new InvalidOperationException($"特效模型尺寸不符：导入半径 {importedRadius:0.###}，米制参考 {authoredRadius:0.###}。请检查导入单位和根变换。");
    }
}

public static class BattleEffectMeshProjection
{
    public static Vector3 Project(Vector3 metres, Vector2 origin, float pixelsPerMetre, float perspective, Matrix4x4 toLayer)
        => toLayer.MultiplyPoint3x4(Project(metres, origin, pixelsPerMetre, perspective));
    public static Vector3 Project(Vector3 metres, Vector2 origin, float pixelsPerMetre, float perspective)
    {
        float projection = 1f / Mathf.Max(.35f, 1 + metres.z * perspective * .035f);
        return new Vector3(origin.x + metres.x * pixelsPerMetre * projection,
            origin.y + metres.y * pixelsPerMetre * projection, 0);
    }
}

public sealed class BattleEffectAssets
{
    public readonly Dictionary<BattleEffectPart, BattleEffectModelAsset> Models = new Dictionary<BattleEffectPart, BattleEffectModelAsset>();
}

internal sealed class BattleEffectMeshSource
{
    public readonly Vector3[] Vertices, Normals;
    public readonly Vector2[] UV;
    public readonly Color[] Colors;
    public readonly int[] Triangles;
    public readonly Vector3[][] Morphs;
    public BattleEffectMeshSource(Mesh mesh, Color fallback)
    {
        if (!mesh.isReadable) throw new InvalidOperationException("特效模型未开启 Read/Write：" + mesh.name);
        Vertices = mesh.vertices; Normals = mesh.normals; Triangles = mesh.triangles;
        UV = mesh.uv;
        Colors = mesh.colors;
        if (Colors.Length != Vertices.Length) { Colors = new Color[Vertices.Length]; for (int i = 0; i < Colors.Length; i++) Colors[i] = fallback; }
        if (Normals.Length != Vertices.Length) { Normals = new Vector3[Vertices.Length]; for (int i = 0; i < Normals.Length; i++) Normals[i] = Vector3.back; }
        Morphs = new Vector3[mesh.blendShapeCount][];
        for (int i = 0; i < Morphs.Length; i++)
        {
            Morphs[i] = new Vector3[Vertices.Length];
            mesh.GetBlendShapeFrameVertices(i, mesh.GetBlendShapeFrameCount(i) - 1, Morphs[i], null, null);
        }
    }
    internal BattleEffectMeshSource(Vector3[] vertices, Vector3[] normals, Color[] colors, Vector2[] uv, int[] triangles, Vector3[][] morphs)
    { Vertices=vertices; Normals=normals; Colors=colors; UV=uv; Triangles=triangles; Morphs=morphs; }
}

internal sealed class BattleEffectModelPose : IDisposable
{
    private sealed class Part
    {
        public Transform Transform;
        public SkinnedMeshRenderer Skin;
        public BattleEffectMeshSource Source;
        public float[] Weights;
        public float? MaterialNoise;
    }
    private readonly GameObject owner, instance;
    private readonly List<Part> parts = new List<Part>();
    private readonly AnimationClip clip;
    public readonly Vector3 UpAxis, ArrowAxis;
    public readonly float ArrowTip, UnitsToMetres;
    public BattleEffectModelPose(BattleEffectModelAsset asset, Transform parent, Dictionary<Mesh, BattleEffectMeshSource> cache)
    {
        if (asset?.Prefab == null) throw new InvalidOperationException("缺少已绑定的3D特效模型。");
        owner = new GameObject("EffectPose", typeof(Transform)); owner.transform.SetParent(parent, false);
        owner.hideFlags = HideFlags.DontSave;
        try
        {
        instance = UnityEngine.Object.Instantiate(asset.Prefab, owner.transform, false); clip = asset.Clip;
        foreach (var animator in instance.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
        foreach (var animation in instance.GetComponentsInChildren<Animation>(true)) { animation.Stop(); animation.enabled = false; }
        foreach (var camera in instance.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
        var availableMaterials = new HashSet<string>(StringComparer.Ordinal);
        bool matchedRenderer = false;
        foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = false;
            bool included = asset.IncludedMaterials == null;
            Color fallback = Color.white;
            float? materialNoise = null;
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null) continue;
                availableMaterials.Add(material.name);
                if (asset.IncludesMaterial(material.name)) included = true;
                if (asset.MatchesMaterial(asset.IceMaterials, material.name)) materialNoise = -1;
                if (asset.MatchesMaterial(asset.MistMaterials, material.name)) materialNoise = .65f;
                if (material.HasProperty("_Color")) fallback = material.GetColor("_Color");
                else if (material.HasProperty("_BaseColor")) fallback = material.GetColor("_BaseColor");
            }
            if (!included) continue;
            matchedRenderer = true;
            var skin = renderer as SkinnedMeshRenderer;
            Mesh mesh = skin != null ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) continue;
            if (!cache.TryGetValue(mesh, out var source)) { source = new BattleEffectMeshSource(mesh, fallback); cache.Add(mesh, source); }
            var part = new Part { Transform = renderer.transform, Skin = skin, Source = source,
                Weights = skin != null ? new float[source.Morphs.Length] : null, MaterialNoise = materialNoise };
            ReadWeights(part); parts.Add(part);
        }
        if (parts.Count == 0)
        {
            if (asset.IncludedMaterials != null && !matchedRenderer)
                throw new InvalidOperationException("模型材质筛选未匹配任何特效网格：" + asset.Prefab.name
                    + "；期望材质：" + string.Join(", ", asset.IncludedMaterials)
                    + "；实际材质：" + string.Join(", ", availableMaterials));
            throw new InvalidOperationException("模型没有可读的特效网格：" + asset.Prefab.name);
        }
        Sample(asset.ReferenceTime);
        Vector3 min = Vector3.one * float.PositiveInfinity, max = Vector3.one * float.NegativeInfinity;
        float radius = 0;
        foreach (var part in parts)
        {
            // Measure exactly the transform chain used for drawing, including the FBX root.
            Matrix4x4 matrix = owner.transform.worldToLocalMatrix * part.Transform.localToWorldMatrix;
            for (int i = 0; i < part.Source.Vertices.Length; i++)
            {
                Vector3 vertex = part.Source.Vertices[i];
                if (part.Weights != null) for (int shape = 0; shape < part.Weights.Length; shape++) vertex += part.Source.Morphs[shape][i] * part.Weights[shape];
                Vector3 p = matrix.MultiplyPoint3x4(vertex);
                min = Vector3.Min(min, p); max = Vector3.Max(max, p); radius = Mathf.Max(radius, p.magnitude);
            }
        }
        UnitsToMetres = BattleEffectModelUnits.ToMetres(radius, asset.ReferenceRadius);
        min *= UnitsToMetres; max *= UnitsToMetres;
        Vector3 extent = max - min; int axis = extent.y >= extent.x && extent.y >= extent.z ? 1 : extent.z >= extent.x ? 2 : 0;
        Vector3 basis = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
        UpAxis = basis * (min[axis] + max[axis] >= 0 ? 1 : -1);
        ArrowAxis = basis * (Mathf.Abs(min[axis]) < Mathf.Abs(max[axis]) ? -1 : 1);
        ArrowTip = Mathf.Min(Mathf.Abs(min[axis]), Mathf.Abs(max[axis]));
        Sample(0);
        }
        catch { Dispose(); throw; }
    }
    private static void ReadWeights(Part part)
    { if (part.Weights != null) for (int i = 0; i < part.Weights.Length; i++) part.Weights[i] = part.Skin.GetBlendShapeWeight(i) * .01f; }
    public void Sample(float normalized)
    {
        if (clip == null) return;
        clip.SampleAnimation(instance, Mathf.Clamp01(normalized) * clip.length);
        foreach (var part in parts) ReadWeights(part);
    }
    public void Draw(BattleEffectMeshBatch batch, Matrix4x4 pose, Vector2 origin, float pixelsPerUnit, Color tint, float opacity, float noise, float perspective, Color? colorOverride = null, float colorShare = 0, int depthSide = 0)
    {
        if (!(opacity > .0001f)) return;
        foreach (var part in parts)
        {
            Matrix4x4 matrix = pose * Matrix4x4.Scale(Vector3.one * UnitsToMetres) * owner.transform.worldToLocalMatrix * part.Transform.localToWorldMatrix;
            if (matrix.GetColumn(0).sqrMagnitude + matrix.GetColumn(1).sqrMagnitude + matrix.GetColumn(2).sqrMagnitude < .00000001f) continue;
            batch.Append(part.Source, part.Weights, matrix, origin, pixelsPerUnit, tint, opacity, part.MaterialNoise ?? noise, perspective, colorOverride, colorShare, depthSide);
        }
    }
    public void Dispose() { if (owner != null) UnityEngine.Object.Destroy(owner); }
}

/// <summary>One pooled UGUI draw chunk. The source remains native 3D geometry, including animated blend shapes.</summary>
public sealed class BattleEffectMeshGraphic : MaskableGraphic
{
    internal Texture EffectTexture;
    public override Texture mainTexture => EffectTexture != null ? EffectTexture : Texture2D.whiteTexture;
    internal readonly List<UIVertex> Vertices = new List<UIVertex>();
    internal readonly List<int> Indices = new List<int>();
    internal Matrix4x4 FromEffectSpace;
    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear(); foreach (var vertex in Vertices) helper.AddVert(vertex);
        for (int i = 0; i + 2 < Indices.Count; i += 3) helper.AddTriangle(Indices[i], Indices[i + 1], Indices[i + 2]);
    }
    internal void Changed() { SetVerticesDirty(); }
}

internal sealed class BattleEffectMeshBatch : IDisposable
{
    private RectTransform mount;
    private readonly RectTransform coordinateSpace;
    private Matrix4x4 effectToWorld;
    private readonly Material material;
    private readonly Texture texture;
    private readonly List<BattleEffectMeshGraphic> chunks = new List<BattleEffectMeshGraphic>();
    private int current;
    private UIVertex[] projected = Array.Empty<UIVertex>();
    private readonly UIVertex[] triangle = new UIVertex[3], clipped = new UIVertex[4];
    public BattleEffectMeshBatch(RectTransform mount, Material material, RectTransform coordinateSpace, Texture texture = null)
    { this.mount = mount; this.material = material; this.coordinateSpace = coordinateSpace; this.texture = texture; }
    public void SetMount(RectTransform target)
    {
        if (target == null) throw new InvalidOperationException("特效的宿主图层已失效。");
        if (mount == target) return;
        mount = target;
        foreach (var chunk in chunks) if (chunk != null)
        {
            chunk.rectTransform.SetParent(mount, false);
            chunk.rectTransform.offsetMin = chunk.rectTransform.offsetMax = Vector2.zero;
        }
    }
    private BattleEffectMeshGraphic Chunk()
    {
        while (chunks.Count <= current) chunks.Add(null);
        if (chunks[current] == null)
        {
            if (mount == null) throw new InvalidOperationException("特效的宿主图层已失效。");
            var go = new GameObject("EffectMesh", typeof(RectTransform), typeof(CanvasRenderer), typeof(BattleEffectMeshGraphic));
            var view = go.GetComponent<BattleEffectMeshGraphic>(); view.rectTransform.SetParent(mount, false);
            view.rectTransform.anchorMin = Vector2.zero; view.rectTransform.anchorMax = Vector2.one;
            view.rectTransform.offsetMin = view.rectTransform.offsetMax = Vector2.zero;
            view.raycastTarget = false; view.material = material; view.EffectTexture = texture;
            view.FromEffectSpace = view.rectTransform.worldToLocalMatrix * effectToWorld;
            chunks[current] = view;
        }
        return chunks[current];
    }
    public void Begin()
    {
        current = 0; effectToWorld = coordinateSpace != null ? coordinateSpace.localToWorldMatrix : Matrix4x4.identity;
        foreach (var chunk in chunks) if (chunk != null)
        {
            chunk.Vertices.Clear(); chunk.Indices.Clear();
            chunk.FromEffectSpace = chunk.rectTransform.worldToLocalMatrix * effectToWorld;
        }
    }
    public void Append(BattleEffectMeshSource source, float[] weights, Matrix4x4 matrix, Vector2 origin, float ppu, Color tint, float opacity, float noise, float perspective, Color? colorOverride = null, float colorShare = 0, int depthSide = 0, Vector4? authoredParameters = null)
    {
        if (depthSide != 0)
        {
            AppendDepth(source, weights, matrix, origin, ppu, tint, opacity, noise, perspective, colorOverride, colorShare, depthSide, authoredParameters);
            return;
        }
        var chunk = Chunk();
        if (chunk.Vertices.Count + source.Vertices.Length > 60000) { current++; chunk = Chunk(); }
        if (source.Vertices.Length > 60000) throw new InvalidOperationException("单个特效网格超过UI分块上限。");
        int offset = chunk.Vertices.Count;
        Matrix4x4 normalMatrix = matrix.inverse.transpose;
        for (int i = 0; i < source.Vertices.Length; i++)
        {
            Vector3 local = source.Vertices[i];
            if (weights != null) for (int shape = 0; shape < source.Morphs.Length; shape++) local += source.Morphs[shape][i] * weights[shape];
            Vector3 point = matrix.MultiplyPoint3x4(local);
            Color color = BattleEffectVertexColor.Apply(source.Colors[i], colorOverride, colorShare) * tint; color.a *= opacity;
            var vertex = UIVertex.simpleVert;
            vertex.position = BattleEffectMeshProjection.Project(point, origin, ppu, perspective, chunk.FromEffectSpace);
            vertex.color = color; vertex.normal = normalMatrix.MultiplyVector(source.Normals[i]).normalized;
            if (source.UV.Length == source.Vertices.Length) vertex.uv0 = source.UV[i];
            vertex.uv1 = new Vector4(point.x, point.y, point.z, 0); vertex.uv2 = authoredParameters ?? new Vector4(noise, -1, 0, 0);
            chunk.Vertices.Add(vertex);
        }
        foreach (int index in source.Triangles) chunk.Indices.Add(offset + index);
    }
    private void AppendDepth(BattleEffectMeshSource source, float[] weights, Matrix4x4 matrix, Vector2 origin, float ppu, Color tint, float opacity, float noise, float perspective, Color? colorOverride, float colorShare, int side, Vector4? authoredParameters)
    {
        if (projected.Length < source.Vertices.Length) projected = new UIVertex[source.Vertices.Length];
        var normalMatrix = matrix.inverse.transpose;
        for (int i = 0; i < source.Vertices.Length; i++)
        {
            Vector3 local = source.Vertices[i];
            if (weights != null) for (int shape = 0; shape < source.Morphs.Length; shape++) local += source.Morphs[shape][i] * weights[shape];
            var point = matrix.MultiplyPoint3x4(local);
            var v = UIVertex.simpleVert;
            var color = BattleEffectVertexColor.Apply(source.Colors[i], colorOverride, colorShare) * tint; color.a *= opacity;
            v.color = color; v.normal = normalMatrix.MultiplyVector(source.Normals[i]).normalized;
            if (source.UV.Length == source.Vertices.Length) v.uv0 = source.UV[i];
            v.uv1 = new Vector4(point.x, point.y, point.z, 0); v.uv2 = authoredParameters ?? new Vector4(noise, -1, 0, 0);
            projected[i] = v;
        }
        for (int start = 0; start + 2 < source.Triangles.Length; start += 3)
        {
            for (int i = 0; i < 3; i++) triangle[i] = projected[source.Triangles[start + i]];
            int count = BattleEffectDepthClip.Clip(triangle, clipped, side);
            if (count < 3) continue;
            var chunk = Chunk();
            if (chunk.Vertices.Count + count > 60000) { current++; chunk = Chunk(); }
            int offset = chunk.Vertices.Count;
            for (int i = 0; i < count; i++)
            {
                var v = clipped[i]; v.position = BattleEffectMeshProjection.Project(v.uv1, origin, ppu, perspective, chunk.FromEffectSpace);
                chunk.Vertices.Add(v);
            }
            for (int i = 1; i + 1 < count; i++)
            { chunk.Indices.Add(offset); chunk.Indices.Add(offset + i); chunk.Indices.Add(offset + i + 1); }
        }
    }
    public void End()
    {
        foreach (var chunk in chunks)
        {
            if (chunk == null) continue;
            bool visible = chunk.Vertices.Count > 0;
            chunk.gameObject.SetActive(visible); if (visible) chunk.Changed();
        }
    }
    public void Dispose() { foreach (var chunk in chunks) if (chunk != null) UnityEngine.Object.Destroy(chunk.gameObject); chunks.Clear(); }
}

/// <summary>Clip in native depth before projection, preserving soft Alpha at the holder plane.</summary>
public static class BattleEffectDepthClip
{
    public static int Clip(UIVertex[] triangle, UIVertex[] output, int side)
    {
        int count = 0;
        var previous = triangle[2]; bool previousInside = Inside(previous.uv1.z, side);
        for (int i = 0; i < 3; i++)
        {
            var current = triangle[i]; bool inside = Inside(current.uv1.z, side);
            if (previousInside != inside)
            {
                float t = previous.uv1.z / (previous.uv1.z - current.uv1.z);
                var v = UIVertex.simpleVert;
                v.uv0 = Vector4.LerpUnclamped(previous.uv0, current.uv0, t);
                v.uv1 = Vector4.LerpUnclamped(previous.uv1, current.uv1, t); v.uv1.z = 0;
                v.uv2 = Vector4.LerpUnclamped(previous.uv2, current.uv2, t);
                v.color = Color32.Lerp(previous.color, current.color, t);
                v.normal = Vector3.LerpUnclamped(previous.normal, current.normal, t).normalized;
                output[count++] = v;
            }
            if (inside) output[count++] = current;
            previous = current; previousInside = inside;
        }
        return count;
    }
    // Coplanar geometry belongs to the front exactly once.
    private static bool Inside(float z, int side) => side > 0 ? z > 0 : z <= 0;
}

internal static class BattleEffectVertexColor
{
    public static Color Apply(Color authored, Color? replacement, float share)
    {
        if (!replacement.HasValue || share <= 0) return authored;
        // Neutralise the baked teal hue before recolouring; preserve brightness and the original soft Alpha.
        float brightness = Mathf.Max(authored.r, Mathf.Max(authored.g, authored.b));
        Color target = replacement.Value;
        return Color.Lerp(authored, new Color(target.r * brightness, target.g * brightness, target.b * brightness, authored.a), share);
    }
}
