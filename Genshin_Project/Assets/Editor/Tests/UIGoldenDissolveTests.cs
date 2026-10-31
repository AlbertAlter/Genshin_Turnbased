#if ROGUELIKE_SAVE_TESTS
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class UIGoldenDissolveTests
    {
        [TestCase(-810f, -130f)]
        [TestCase(-130f, -130f)]
        [TestCase(210f, 80f)]
        [TestCase(720f, 80f)]
        public void Dissolve_LocalCoordinatesSurviveCanvasBatchTransform(float x, float y)
        {
            var canvas = new GameObject("DissolveCanvas", typeof(RectTransform), typeof(Canvas));
            var card = new GameObject("DissolveCard", typeof(RectTransform), typeof(RawImage));
            card.transform.SetParent(canvas.transform, false);
            try
            {
                var effect = card.AddComponent<UIGoldenDissolve>();
                Assert.That(canvas.GetComponent<Canvas>().additionalShaderChannels & AdditionalCanvasShaderChannels.TexCoord1,
                    Is.EqualTo(AdditionalCanvasShaderChannels.TexCoord1));
                using (var vertices = new VertexHelper())
                {
                    var bounds = new Rect(-80, -160, 160, 320);
                    var corners = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
                    foreach (var corner in corners)
                        vertices.AddVert(bounds.min + Vector2.Scale(bounds.size, corner), Color.white, corner * .5f);
                    effect.ModifyMesh(vertices);
                    var batchTransform = Matrix4x4.TRS(new Vector3(x, y, 0), Quaternion.Euler(0, 0, 7), Vector3.one * .7f);
                    var vertex = UIVertex.simpleVert;
                    for (int i = 0; i < corners.Length; i++)
                    {
                        vertices.PopulateUIVertex(ref vertex, i);
                        vertex.position = batchTransform.MultiplyPoint3x4(vertex.position);
                        // This is the final shader's coordinate input, independent of the transformed POSITION.
                        Vector2 uv = new Vector2((vertex.uv1.x - bounds.xMin) / bounds.width,
                            (vertex.uv1.y - bounds.yMin) / bounds.height);
                        Assert.That(Vector2.Distance(uv, corners[i]), Is.LessThan(.00001f));
                        Assert.That(Vector2.Distance((Vector2)vertex.uv0, corners[i] * .5f), Is.LessThan(.00001f));
                    }
                }
            }
            finally { Object.DestroyImmediate(canvas); }
        }

        // GPU regression: a card at either side of a scaled Canvas must fill its own capture,
        // and hiding the original UI must not blank a retained video capture.
        [TestCase(false)]
        [TestCase(true)]
        public void Capture_StaysOnCard_AndRefreshesHiddenSource(bool useImage)
        {
            var canvas = new GameObject("SnapshotCanvas", typeof(RectTransform), typeof(Canvas));
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var videoFrame = new RenderTexture(8, 8, 0);
            var readback = new Texture2D(32, 64, TextureFormat.RGBA32, false);
            Sprite sprite = null;
            var previousTarget = RenderTexture.active;
            try
            {
                canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var basis = new GameObject("Card", typeof(RectTransform)).GetComponent<RectTransform>();
                basis.SetParent(canvas.transform, false);
                basis.sizeDelta = new Vector2(160, 320);
                basis.localScale = Vector3.one * .7f;
                var layer = new GameObject("CardSource", typeof(RectTransform)).GetComponent<RectTransform>();
                layer.SetParent(basis, false);
                layer.anchorMin = Vector2.zero; layer.anchorMax = Vector2.one; layer.sizeDelta = Vector2.zero;
                Graphic graphic;
                if (useImage)
                {
                    sprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), Vector2.one * .5f);
                    var image = layer.gameObject.AddComponent<Image>(); image.sprite = sprite; graphic = image;
                }
                else
                {
                    videoFrame.Create();
                    var raw = layer.gameObject.AddComponent<RawImage>(); raw.texture = videoFrame;
                    raw.uvRect = new Rect(.1f, 0, .8f, 1); graphic = raw;
                }

                void Paint(Color color)
                {
                    var pixels = new Color[64];
                    for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
                    texture.SetPixels(pixels); texture.Apply();
                    if (!useImage) Graphics.Blit(texture, videoFrame);
                }
                void AssertCapture(RenderTexture result, Color expected)
                {
                    RenderTexture.active = result;
                    readback.ReadPixels(new Rect(0, 0, 32, 64), 0, 0); readback.Apply();
                    foreach (var point in new[] { new Vector2Int(2, 2), new Vector2Int(16, 32), new Vector2Int(29, 61) })
                    {
                        Color value = readback.GetPixel(point.x, point.y);
                        Assert.That(value.a, Is.GreaterThan(.98f), "Capture is empty or clipped at " + basis.anchoredPosition);
                        Assert.That(value.r, Is.EqualTo(expected.r).Within(.02f));
                        Assert.That(value.g, Is.EqualTo(expected.g).Within(.02f));
                    }
                    RenderTexture.active = previousTarget;
                }

                foreach (var position in new[] { new Vector2(-810, -130), new Vector2(-130, 80), new Vector2(210, -130), new Vector2(720, 80) })
                {
                    basis.anchoredPosition = position; graphic.enabled = true; Paint(Color.red);
                    using (var capture = UICardDissolveSnapshot.BeginCapture(basis, new[] { graphic }, 64))
                    {
                        AssertCapture(capture.Texture, Color.red);
                        graphic.enabled = false; Canvas.ForceUpdateCanvases();
                        Paint(Color.green); capture.Render();
                        AssertCapture(capture.Texture, Color.green);
                    }
                }
            }
            finally
            {
                RenderTexture.active = previousTarget;
                Object.DestroyImmediate(canvas);
                if (sprite != null) Object.DestroyImmediate(sprite);
                videoFrame.Release(); Object.DestroyImmediate(videoFrame);
                Object.DestroyImmediate(texture); Object.DestroyImmediate(readback);
            }
        }

        private static Dictionary<string, string> Values() => new Dictionary<string, string>
        {
            ["DurationSeconds"] = ".6", ["UseUnscaledTime"] = "1", ["NoiseScale"] = "12",
            ["EdgeWidth"] = ".055", ["EdgeSoftness"] = ".012", ["EdgeColor"] = "#FFAD2EFF",
            ["EdgeHighlight"] = "#FFF5B3FF", ["DirectionX"] = "0", ["DirectionY"] = "1", ["DirectionWeight"] = ".2",
            ["FragmentCount"] = "100", ["FragmentLifetimeSeconds"] = ".36",
            ["FragmentSizeMinHeightRatio"] = ".006", ["FragmentSizeMaxHeightRatio"] = ".018",
            ["FragmentDriftHeightRatio"] = ".08", ["FragmentSpreadHeightRatio"] = ".05",
            ["FragmentRoughness"] = ".35", ["FragmentFadeInRatio"] = ".12", ["ShadowFadePower"] = "1.6",
            ["CaptureHeightPixels"] = "885", ["PreviewCardHeight"] = "500", ["PreviewLoopPauseSeconds"] = ".8",
            ["PreviewBackgroundColor"] = "#2D2822FF",
        };

        [TestCase(false)]
        [TestCase(true)]
        public void Preview_MigratesLegacySerializedPaths_AndPreservesCustomPaths(bool backslashes)
        {
            var node = new UnityEngine.GameObject("GoldenDissolvePreviewPathTest");
            try
            {
                var preview = node.AddComponent<GoldenDissolvePreview>();
                string PathValue(string path) => backslashes ? path.Replace('/', '\\') : path;
                preview.cardPath = PathValue("art_assets/Character_Whole/1009_03.png");
                preview.framePath = PathValue("art_assets/UI/BattleField1/UI_components/AvatarFrame/FrameOutline.png");
                preview.innerShadowPath = PathValue("art_assets/UI/BattleField1/UI_components/AvatarFrame/FrameInnerShadow.png");
                preview.outerShadowPath = PathValue("art_assets/UI/BattleField1/UI_components/AvatarFrame/FrameShadow.png");
                var migrate = typeof(GoldenDissolvePreview).GetMethod("MigrateLegacyPaths",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.That(migrate, Is.Not.Null);
                migrate.Invoke(preview, null);
                Assert.That(preview.cardPath, Is.EqualTo("art_assets/Enemy/2001_01.png"));
                Assert.That(preview.framePath, Is.EqualTo("art_assets/Character_Bare/Frame/01.png"));
                Assert.That(preview.innerShadowPath, Is.EqualTo("art_assets/UI/BattleField1/UI_components/card/CardInnerShadow.png"));
                Assert.That(preview.outerShadowPath, Is.EqualTo("art_assets/UI/BattleField1/UI_components/card/CardShadow.png"));

                preview.cardPath = "art_assets/custom/art.png";
                preview.framePath = "art_assets/custom/frame.png";
                preview.innerShadowPath = "art_assets/custom/inner.png";
                preview.outerShadowPath = "art_assets/custom/outer.png";
                migrate.Invoke(preview, null);
                Assert.That(preview.cardPath, Is.EqualTo("art_assets/custom/art.png"));
                Assert.That(preview.framePath, Is.EqualTo("art_assets/custom/frame.png"));
                Assert.That(preview.innerShadowPath, Is.EqualTo("art_assets/custom/inner.png"));
                Assert.That(preview.outerShadowPath, Is.EqualTo("art_assets/custom/outer.png"));
            }
            finally { UnityEngine.Object.DestroyImmediate(node); }
        }

        [Test]
        public void Values_LoadDurationColorsAndNormalizedDirection()
        {
            var values = Values(); values["DirectionX"] = "1";
            var style = UIGoldenDissolveStyle.Parse(values);
            Assert.That(style.DurationSeconds, Is.EqualTo(.6f));
            Assert.That(style.UseUnscaledTime, Is.True);
            Assert.That(style.NoiseScale, Is.EqualTo(12));
            Assert.That(style.Direction.magnitude, Is.EqualTo(1).Within(.0001f));
            Assert.That(style.EdgeColor.r, Is.EqualTo(1));
        }

        [Test]
        public void ZeroDurationEdgeWidthAndDirection_AreValid()
        {
            var values = Values(); values["DurationSeconds"] = "0"; values["EdgeWidth"] = "0"; values["DirectionY"] = "0";
            var style = UIGoldenDissolveStyle.Parse(values);
            Assert.That(style.DurationSeconds, Is.Zero);
            Assert.That(style.EdgeWidth, Is.Zero);
            Assert.That(style.Direction.sqrMagnitude, Is.Zero);
        }

        [TestCase("DurationSeconds", "-1")]
        [TestCase("NoiseScale", "0")]
        [TestCase("EdgeWidth", "1.1")]
        [TestCase("EdgeSoftness", "0")]
        [TestCase("DirectionX", "2")]
        [TestCase("DirectionWeight", "-1")]
        [TestCase("UseUnscaledTime", "0.5")]
        [TestCase("DurationSeconds", "NaN")]
        [TestCase("NoiseScale", "Infinity")]
        [TestCase("EdgeColor", "bad color")]
        [TestCase("FragmentCount", "1.5")]
        [TestCase("FragmentCount", "4097")]
        [TestCase("FragmentLifetimeSeconds", "0")]
        [TestCase("FragmentSizeMinHeightRatio", ".1")]
        [TestCase("FragmentRoughness", "2")]
        [TestCase("FragmentFadeInRatio", "1")]
        [TestCase("CaptureHeightPixels", "8192")]
        public void InvalidValue_IsRejected(string key, string value)
        {
            var values = Values(); values[key] = value;
            Assert.Throws<InvalidDataException>(() => UIGoldenDissolveStyle.Parse(values));
        }

        [Test]
        public void UnknownOrMissingParameters_AreRejected()
        {
            var values = Values(); values.Remove("NoiseScale");
            Assert.Throws<InvalidDataException>(() => UIGoldenDissolveStyle.Parse(values));
            values = Values(); values["Unexpected"] = "1";
            Assert.Throws<InvalidDataException>(() => UIGoldenDissolveStyle.Parse(values));
        }

        [Test]
        public void Fragments_AreDeterministicAndWithinCard_WithoutGameplayRandom()
        {
            var style = UIGoldenDissolveStyle.Parse(Values());
            var old = BattleRandom.Source; var source = new NoDrawSource();
            BattleRandom.SetSource(source);
            try
            {
                var size = new UnityEngine.Vector2(300, 520);
                var first = GoldenDissolveMath.BuildFragments(style, size, 123);
                var again = GoldenDissolveMath.BuildFragments(style, size, 123);
                var other = GoldenDissolveMath.BuildFragments(style, size, 456);
                Assert.That(first.Length, Is.EqualTo(100));
                for (int i = 0; i < first.Length; i++)
                {
                    Assert.That(first[i].UV, Is.EqualTo(again[i].UV));
                    Assert.That(first[i].BirthSeconds, Is.EqualTo(again[i].BirthSeconds));
                    Assert.That(first[i].UV.x, Is.InRange(0, 1));
                    Assert.That(first[i].UV.y, Is.InRange(0, 1));
                    Assert.That(first[i].BirthSeconds, Is.InRange(0, style.DurationSeconds));
                }
                Assert.That(first[0].UV, Is.Not.EqualTo(other[0].UV));
                Assert.That(source.Draws, Is.Zero);
            }
            finally { BattleRandom.SetSource(old); }
        }

        [Test]
        public void Fragments_OnlyLiveAfterBirthAndDisappearByTailEnd()
        {
            var style = UIGoldenDissolveStyle.Parse(Values());
            var fragment = GoldenDissolveMath.BuildFragments(style, new UnityEngine.Vector2(300, 520), 1)[0];
            Assert.That(GoldenDissolveMath.Evaluate(fragment, style, fragment.BirthSeconds - .01f, out _, out _, out _), Is.False);
            Assert.That(GoldenDissolveMath.Evaluate(fragment, style, fragment.BirthSeconds + style.FragmentLifetimeSeconds * .5f,
                out _, out float size, out float alpha), Is.True);
            Assert.That(size, Is.LessThan(fragment.Diameter)); Assert.That(alpha, Is.InRange(0, 1));
            Assert.That(GoldenDissolveMath.Evaluate(fragment, style, style.TotalDuration + .001f, out _, out _, out _), Is.False);
            Assert.That(GoldenDissolveMath.ShadowAlpha(0, style.ShadowFadePower), Is.EqualTo(1));
            Assert.That(GoldenDissolveMath.ShadowAlpha(1, style.ShadowFadePower), Is.Zero);
        }

        [Test]
        public void DisabledFragments_DoNotAddAnInvisibleTail()
        {
            var values = Values(); values["FragmentCount"] = "0"; values["FragmentLifetimeSeconds"] = "0";
            var style = UIGoldenDissolveStyle.Parse(values);
            Assert.That(style.TotalDuration, Is.EqualTo(style.DurationSeconds));
            Assert.That(GoldenDissolveMath.BuildFragments(style, new UnityEngine.Vector2(300, 520), 1), Is.Empty);
        }

        private sealed class NoDrawSource : IBattleRandomSource
        {
            public int Draws;
            public int NextInt(int min, int max) { Draws++; throw new System.InvalidOperationException("No gameplay RNG in visuals."); }
            public float NextFloat01() { Draws++; throw new System.InvalidOperationException("No gameplay RNG in visuals."); }
        }
    }
}
#endif
