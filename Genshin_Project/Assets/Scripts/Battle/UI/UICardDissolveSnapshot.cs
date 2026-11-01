using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// GPU-only composition of card Image/RawImage layers, bottom to top. A retained session can refresh live video.
/// Preserves geometry, sprite UVs, vertex tint/alpha and normal source-over blending.
/// Does not bake PNGs, read back pixels, move source transforms, or include the external shadow.
/// UICardFrameView supplies its explicit opening mask; other custom materials/masks still fail explicitly.
/// </summary>
public static class UICardDissolveSnapshot
{
    public sealed class CaptureSession : IDisposable
    {
        public RenderTexture Texture { get; private set; }
        private CommandBuffer commands;
        private List<UnityEngine.Object> resources;
        internal CaptureSession(RenderTexture texture, CommandBuffer commands, List<UnityEngine.Object> resources)
        { Texture = texture; this.commands = commands; this.resources = resources; }
        public void Render() { if (commands != null) Graphics.ExecuteCommandBuffer(commands); }
        internal RenderTexture Detach() { var texture = Texture; Texture = null; return texture; }
        public void Dispose()
        {
            commands?.Release(); commands = null;
            if (resources != null) foreach (var item in resources) UICardDissolveSnapshot.Dispose(item);
            resources = null;
            if (Texture != null) { Texture.Release(); UICardDissolveSnapshot.Dispose(Texture); Texture = null; }
        }
    }
    public static RenderTexture Capture(RectTransform basis, IReadOnlyList<Graphic> layers, int heightPixels)
    {
        using (var session = BeginCapture(basis, layers, heightPixels)) return session.Detach();
    }
    public static CaptureSession BeginCapture(RectTransform basis, IReadOnlyList<Graphic> layers, int heightPixels,
        Texture clipTexture = null, IReadOnlyList<Graphic> clippedLayers = null, bool includeRendererBrightness = true)
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
            var seen = new HashSet<Graphic>();
            var clipped = clippedLayers == null ? new HashSet<Graphic>() : new HashSet<Graphic>(clippedLayers);
            foreach (var image in layers)
            {
                if (image == null || !seen.Add(image)) throw new ArgumentException("Card layers must be non-null and unique.");
                if (!image.transform.IsChildOf(basis) && image.transform != basis) throw new ArgumentException("Card layers must belong to the basis.");
                if (!image.isActiveAndEnabled) continue;
                var spriteImage = image as Image; var rawImage = image as RawImage;
                if (spriteImage == null && rawImage == null) throw new NotSupportedException("Unsupported card graphic: " + image.name);
                if ((spriteImage != null && spriteImage.sprite == null) || (rawImage != null && rawImage.texture == null))
                    throw new ArgumentException("Card layer texture is not loaded: " + image.name);
                var original = image.material;
                if (original != null && original.shader.name != "UI/Default")
                    throw new NotSupportedException("Card snapshot needs an explicit adapter for custom material: " + image.name);
                for (Transform t = image.transform; t != basis; t = t.parent)
                {
                    var mask = t.GetComponent<Mask>();
                    bool knownMask = clipped.Contains(image) && clipTexture != null &&
                        mask != null && mask.graphic.mainTexture == clipTexture;
                    if ((mask?.isActiveAndEnabled == true && !knownMask) || t.GetComponent<RectMask2D>()?.isActiveAndEnabled == true)
                        throw new NotSupportedException("Card snapshot needs an explicit adapter for this UI mask: " + image.name);
                }
                if (spriteImage != null && spriteImage.sprite.associatedAlphaSplitTexture != null)
                    throw new NotSupportedException("Split-alpha card textures need a capture adapter.");
                // Build in Graphic-local space, then convert to card-local space exactly once.
                // CanvasRenderer's render cache must not be treated as a local source mesh.
                var mesh = BuildLayerMesh(basis, image);
                temporary.Add(mesh);
                var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                temporary.Add(material);
                material.SetTexture("_MainTex", image.mainTexture);
                material.SetTexture("_ClipTex", clipped.Contains(image) && clipTexture != null ? clipTexture : Texture2D.whiteTexture);
                material.SetVector("_CardRect", new Vector4(rect.xMin, rect.yMin, rect.width, rect.height));
                material.SetMatrix("_CompositeLocal", Matrix4x4.identity);
                Color tint = image.canvasRenderer.GetColor();
                // An effect drawn inside the same dimmed card group receives its brightness at display time.
                if (!includeRendererBrightness) tint = new Color(1, 1, 1, tint.a);
                if (original != null && original.HasProperty("_Color")) tint *= original.GetColor("_Color");
                material.SetColor("_Tint", tint);
                material.SetMatrix("_CompositeMVP", projection);
                commands.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0);
                drawn++;
            }
            if (drawn == 0) throw new InvalidOperationException("No visible card layers to capture.");
            Graphics.ExecuteCommandBuffer(commands);
            success = true;
            return new CaptureSession(target, commands, temporary);
        }
        finally
        {
            if (!success)
            {
                commands.Release();
                foreach (var item in temporary) Dispose(item);
                target.Release(); Dispose(target);
            }
        }
    }

    // Matches uGUI's Simple Image / RawImage drawing rectangles and UVs without depending
    // on a Canvas rebuild. The owned mesh survives disabling the source Graphic during exit.
    internal static Mesh BuildLayerMesh(RectTransform basis, Graphic graphic)
    {
        Rect drawing = graphic.GetPixelAdjustedRect();
        Vector4 uv;
        var image = graphic as Image;
        if (image != null)
        {
            if (image.type != Image.Type.Simple || image.useSpriteMesh)
                throw new NotSupportedException("Card snapshot requires a Simple quad Image: " + image.name);
            Sprite sprite = image.overrideSprite != null ? image.overrideSprite : image.sprite;
            Vector2 size = sprite.rect.size;
            if (image.preserveAspect && size.sqrMagnitude > 0)
            {
                float spriteRatio = size.x / size.y;
                if (spriteRatio > drawing.width / drawing.height)
                {
                    float oldHeight = drawing.height;
                    drawing.height = drawing.width / spriteRatio;
                    drawing.y += (oldHeight - drawing.height) * image.rectTransform.pivot.y;
                }
                else
                {
                    float oldWidth = drawing.width;
                    drawing.width = drawing.height * spriteRatio;
                    drawing.x += (oldWidth - drawing.width) * image.rectTransform.pivot.x;
                }
            }
            Vector4 padding = UnityEngine.Sprites.DataUtility.GetPadding(sprite);
            float spriteWidth = Mathf.RoundToInt(size.x), spriteHeight = Mathf.RoundToInt(size.y);
            drawing = Rect.MinMaxRect(drawing.x + drawing.width * padding.x / spriteWidth,
                drawing.y + drawing.height * padding.y / spriteHeight,
                drawing.xMax - drawing.width * padding.z / spriteWidth,
                drawing.yMax - drawing.height * padding.w / spriteHeight);
            uv = UnityEngine.Sprites.DataUtility.GetOuterUV(sprite);
        }
        else if (graphic is RawImage raw)
        {
            Rect source = raw.uvRect;
            Texture texture = raw.mainTexture;
            float sx = texture.width * texture.texelSize.x, sy = texture.height * texture.texelSize.y;
            uv = new Vector4(source.xMin * sx, source.yMin * sy, source.xMax * sx, source.yMax * sy);
        }
        else throw new NotSupportedException("Unsupported card graphic: " + graphic.name);

        Matrix4x4 toCard = basis.worldToLocalMatrix * graphic.transform.localToWorldMatrix;
        var mesh = new Mesh { name = "Card-local snapshot mesh", hideFlags = HideFlags.HideAndDontSave };
        using (var vertices = new VertexHelper())
        {
            vertices.AddVert(toCard.MultiplyPoint3x4(new Vector3(drawing.xMin, drawing.yMin)), graphic.color, new Vector2(uv.x, uv.y));
            vertices.AddVert(toCard.MultiplyPoint3x4(new Vector3(drawing.xMin, drawing.yMax)), graphic.color, new Vector2(uv.x, uv.w));
            vertices.AddVert(toCard.MultiplyPoint3x4(new Vector3(drawing.xMax, drawing.yMax)), graphic.color, new Vector2(uv.z, uv.w));
            vertices.AddVert(toCard.MultiplyPoint3x4(new Vector3(drawing.xMax, drawing.yMin)), graphic.color, new Vector2(uv.z, uv.y));
            vertices.AddTriangle(0, 1, 2);
            vertices.AddTriangle(2, 3, 0);
            vertices.FillMesh(mesh);
        }
        return mesh;
    }

    internal static void Dispose(UnityEngine.Object item)
    {
        if (item == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(item); else UnityEngine.Object.DestroyImmediate(item);
    }
}
