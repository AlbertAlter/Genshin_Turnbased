using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Temporary GPU-only composition of ordinary card Image layers, bottom to top.
/// Preserves geometry, sprite UVs, vertex tint/alpha and normal source-over blending.
/// Does not bake PNGs, read back pixels, move source transforms, or include the external shadow.
/// Unsupported custom materials/internal masks fail explicitly rather than silently changing PSD rendering.
/// </summary>
public static class UICardDissolveSnapshot
{
    public static RenderTexture Capture(RectTransform basis, IReadOnlyList<Image> layers, int heightPixels)
    {
        if (basis == null || layers == null || layers.Count == 0) throw new ArgumentException("Card snapshot needs a basis and Image layers.");
        Rect rect = basis.rect;
        if (rect.width <= 0 || rect.height <= 0) throw new ArgumentException("Card snapshot bounds are empty.");
        var shader = Resources.Load<Shader>("UI/GoldenDissolveComposite");
        if (shader == null) throw new InvalidOperationException("Missing GoldenDissolveComposite shader.");
        int height = Mathf.Clamp(heightPixels, 32, Mathf.Min(4096, SystemInfo.maxTextureSize));
        int width = Mathf.Clamp(Mathf.RoundToInt(height * rect.width / rect.height), 1, SystemInfo.maxTextureSize);
        var target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
        {
            name = "GoldenDissolve_CardSnapshot", hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            useMipMap = false, autoGenerateMips = false,
        };
        var commands = new CommandBuffer { name = "Capture card, frame and inner shadow" };
        var temporary = new List<UnityEngine.Object>();
        bool success = false;
        try
        {
            if (!target.Create()) throw new InvalidOperationException("Unable to allocate card snapshot.");
            Canvas.ForceUpdateCanvases();
            commands.SetRenderTarget(target);
            commands.SetViewport(new Rect(0, 0, width, height));
            commands.ClearRenderTarget(false, true, Color.clear);
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(rect.xMin, rect.xMax, rect.yMin, rect.yMax, -1000, 1000), true);
            int drawn = 0;
            var seen = new HashSet<Image>();
            foreach (var image in layers)
            {
                if (image == null || !seen.Add(image)) throw new ArgumentException("Card layers must be non-null and unique.");
                if (!image.transform.IsChildOf(basis) && image.transform != basis) throw new ArgumentException("Card layers must belong to the basis.");
                if (!image.isActiveAndEnabled) continue;
                if (image.sprite == null) throw new ArgumentException("Card layer sprite is not loaded: " + image.name);
                var original = image.material;
                if (original != null && original.shader.name != "UI/Default")
                    throw new NotSupportedException("Card snapshot needs an explicit adapter for custom material: " + image.name);
                for (Transform t = image.transform; t != basis; t = t.parent)
                    if (t.GetComponent<Mask>()?.isActiveAndEnabled == true || t.GetComponent<RectMask2D>()?.isActiveAndEnabled == true)
                        throw new NotSupportedException("Card snapshot does not flatten internal UI masks: " + image.name);
                if (image.sprite.associatedAlphaSplitTexture != null) throw new NotSupportedException("Split-alpha card textures need a capture adapter.");
                var renderedMesh = image.canvasRenderer.GetMesh();
                if (renderedMesh == null) throw new InvalidOperationException("Card UI mesh is not ready: " + image.name);
                // The renderer owns its mesh; snapshot only disposes this private clone.
                var mesh = UnityEngine.Object.Instantiate(renderedMesh);
                mesh.name = "Card snapshot mesh"; mesh.hideFlags = HideFlags.HideAndDontSave;
                temporary.Add(mesh);
                if (mesh.vertexCount == 0) throw new InvalidOperationException("Card UI mesh is not ready: " + image.name);
                var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                temporary.Add(material);
                material.SetTexture("_MainTex", image.mainTexture);
                Color tint = image.canvasRenderer.GetColor();
                if (original != null && original.HasProperty("_Color")) tint *= original.GetColor("_Color");
                material.SetColor("_Tint", tint);
                material.SetMatrix("_CompositeMVP", projection * basis.worldToLocalMatrix * image.transform.localToWorldMatrix);
                commands.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0);
                drawn++;
            }
            if (drawn == 0) throw new InvalidOperationException("No visible card layers to capture.");
            Graphics.ExecuteCommandBuffer(commands);
            success = true;
            return target;
        }
        finally
        {
            commands.Release();
            foreach (var item in temporary) Dispose(item);
            if (!success) { target.Release(); Dispose(target); }
        }
    }

    internal static void Dispose(UnityEngine.Object item)
    {
        if (item == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(item); else UnityEngine.Object.DestroyImmediate(item);
    }
}
