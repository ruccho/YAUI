using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using Yaui.Rendering;

namespace Yaui.Tests
{
    /// <summary>
    /// Layout, text, hit testing and structure through the whole pipeline, run synchronously with
    /// <see cref="YauiPanel.ForceUpdate"/>. Panels are world space so that the canvas size is fixed.
    /// </summary>
    public class YauiTests
    {
        private readonly List<GameObject> created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in created)
                if (go != null)
                    Object.DestroyImmediate(go);

            created.Clear();
        }

        private YauiPanel CreatePanel(float width = 1000f, float height = 1000f)
        {
            var go = new GameObject("TestPanel");
            created.Add(go);
            var panel = go.AddComponent<YauiPanel>();
            panel.RenderMode = PanelRenderMode.World;
            panel.ReferenceResolution = new Vector2(width, height);
            return panel;
        }

        private static T Create<T>(Component parent, string name = null) where T : YauiElement
        {
            var go = new GameObject(name ?? typeof(T).Name);
            go.transform.SetParent(parent.transform, false);
            return go.AddComponent<T>();
        }

        private static YauiElement Box(Component parent, float width, float height, Color color = default)
        {
            var element = Create<YauiElement>(parent);
            var layout = LayoutStyle.Default;
            layout.Width = width;
            layout.Height = height;
            element.Layout = layout;
            var box = BoxStyle.Default;
            box.BackgroundColor = color == default ? Color.white : color;
            element.Box = box;
            return element;
        }

        private static YauiElement HitTest(YauiPanel panel, float x, float y)
        {
            return panel.CurrentState.HitTestCanvas(new float2(x, y));
        }

        [Test]
        public void RowLaysOutWithPaddingGapAndGrow()
        {
            var panel = CreatePanel();
            var root = panel.GetComponent<YauiElement>();
            var layout = LayoutStyle.Default;
            layout.Direction = FlexDirection.Row;
            layout.Padding = new Edges(10f);
            layout.Gap = new Vector2(20f, 0f);
            layout.AlignItems = FlexAlign.FlexStart;
            root.Layout = layout;

            var a = Box(root, 100f, 50f);
            var b = Box(root, 100f, 50f);
            var grow = b.Layout;
            grow.Grow = 1f;
            b.Layout = grow;
            YauiPanel.ForceUpdate();

            Assert.AreEqual(new Rect(10f, 10f, 100f, 50f), a.LayoutRect);
            Assert.AreEqual(new Rect(130f, 10f, 860f, 50f), b.LayoutRect);
        }

        [Test]
        public void PercentSizeFollowsParent()
        {
            var panel = CreatePanel(800f, 600f);
            var child = Box(panel.GetComponent<YauiElement>(), 0f, 0f);
            var layout = child.Layout;
            layout.Width = Length.Percent(50f);
            layout.Height = Length.Percent(25f);
            child.Layout = layout;
            YauiPanel.ForceUpdate();

            Assert.AreEqual(new Vector2(400f, 150f), child.LayoutRect.size);
        }

        [Test]
        public void TextIsMeasuredAndWrapsWithinItsWidth()
        {
            var panel = CreatePanel();
            var root = panel.GetComponent<YauiElement>();
            var layout = root.Layout;
            layout.AlignItems = FlexAlign.FlexStart;
            root.Layout = layout;

            var text = Create<YauiText>(root);
            text.FontSize = 32f;
            text.Text = "Hello";
            YauiPanel.ForceUpdate();
            var single = text.LayoutRect.size;
            Assert.Greater(single.x, 0f);
            Assert.Greater(single.y, 0f);

            var textLayout = text.Layout;
            textLayout.Width = 120f;
            text.Layout = textLayout;
            text.Text = "The quick brown fox jumps over the lazy dog";
            YauiPanel.ForceUpdate();
            Assert.AreEqual(120f, text.LayoutRect.width);
            Assert.Greater(text.LayoutRect.height, single.y * 2f, "The text wraps into several lines.");
        }

        [Test]
        public void FallbackTextIsMeasured()
        {
            Text.AtgText.ForceFallback = true;
            try
            {
                var panel = CreatePanel();
                var root = panel.GetComponent<YauiElement>();
                var layout = root.Layout;
                layout.AlignItems = FlexAlign.FlexStart;
                root.Layout = layout;
                var text = Create<YauiText>(root);
                text.FontSize = 32f;
                text.Text = "Hello";
                YauiPanel.ForceUpdate();
                Assert.Greater(text.LayoutRect.width, 0f);
                Assert.Greater(text.LayoutRect.height, 0f);
            }
            finally
            {
                Text.AtgText.ForceFallback = false;
            }
        }

        [Test]
        public void ChangesInsideFixedSizeBoxesDoNotMoveThem()
        {
            var panel = CreatePanel();
            var root = panel.GetComponent<YauiElement>();
            var box = Box(root, 300f, 100f);
            var boxLayout = box.Layout;
            boxLayout.AlignItems = FlexAlign.FlexStart;
            box.Layout = boxLayout;
            var after = Box(root, 50f, 50f);
            var text = Create<YauiText>(box);
            text.Text = "A";
            YauiPanel.ForceUpdate();
            var boxRect = box.LayoutRect;
            var afterRect = after.LayoutRect;
            var textWidth = text.LayoutRect.width;

            text.Text = "A much longer text";
            YauiPanel.ForceUpdate();

            Assert.AreEqual(boxRect, box.LayoutRect);
            Assert.AreEqual(afterRect, after.LayoutRect);
            Assert.Greater(text.LayoutRect.width, textWidth, "The text inside the boundary is laid out again.");
        }

        [Test]
        public void HitTestFindsTheTopmostVisibleElement()
        {
            var panel = CreatePanel();
            var root = panel.GetComponent<YauiElement>();
            var layout = root.Layout;
            layout.AlignItems = FlexAlign.FlexStart;
            root.Layout = layout;

            var back = Box(root, 200f, 200f);
            var front = Box(back, 100f, 100f, Color.red);
            var invisible = Create<YauiElement>(root);
            var invisibleLayout = LayoutStyle.Default;
            invisibleLayout.Position = PositionType.Absolute;
            invisibleLayout.Inset = new Edges(0f, 0f, Length.Auto, Length.Auto);
            invisibleLayout.Width = 500f;
            invisibleLayout.Height = 500f;
            invisible.Layout = invisibleLayout;
            YauiPanel.ForceUpdate();

            Assert.AreSame(front, HitTest(panel, 50f, 50f), "Children are on top; invisible boxes are not hit.");
            Assert.AreSame(back, HitTest(panel, 150f, 150f));
            Assert.IsNull(HitTest(panel, 300f, 300f));

            front.RaycastTarget = false;
            YauiPanel.ForceUpdate();
            Assert.AreSame(back, HitTest(panel, 50f, 50f));
        }

        [Test]
        public void HitTestRespectsClipsAndRotation()
        {
            var panel = CreatePanel();
            var root = panel.GetComponent<YauiElement>();
            var layout = root.Layout;
            layout.AlignItems = FlexAlign.FlexStart;
            root.Layout = layout;

            var clip = Box(root, 100f, 100f, new Color(0f, 0f, 0f, 0f));
            clip.ClipChildren = true;
            var overflow = Box(clip, 300f, 50f, Color.red);
            var rotated = Box(root, 100f, 100f, Color.blue);
            rotated.Rotation = 45f;
            YauiPanel.ForceUpdate();

            Assert.AreSame(overflow, HitTest(panel, 50f, 25f));
            Assert.IsNull(HitTest(panel, 200f, 25f), "Clipped parts are not hit.");

            // The rotated box spans y 100..200; its unrotated corner is outside when rotated by 45 degrees.
            Assert.AreSame(rotated, HitTest(panel, 50f, 150f));
            Assert.IsNull(HitTest(panel, 2f, 102f));
        }

        [Test]
        public void SiblingOrderIsTheDrawAndHitOrder()
        {
            var panel = CreatePanel();
            var root = panel.GetComponent<YauiElement>();
            var a = Box(root, 100f, 100f);
            var b = Box(root, 100f, 100f);
            foreach (var element in new[] { a, b })
            {
                var layout = element.Layout;
                layout.Position = PositionType.Absolute;
                layout.Inset = new Edges(0f, 0f, Length.Auto, Length.Auto);
                element.Layout = layout;
            }

            YauiPanel.ForceUpdate();
            Assert.AreSame(b, HitTest(panel, 50f, 50f));

            b.transform.SetAsFirstSibling();
            YauiPanel.ForceUpdate();
            Assert.AreSame(a, HitTest(panel, 50f, 50f));

            a.gameObject.SetActive(false);
            YauiPanel.ForceUpdate();
            Assert.AreSame(b, HitTest(panel, 50f, 50f));
        }

        [Test]
        public void RenderTransformDoesNotChangeTheLayout()
        {
            var panel = CreatePanel();
            var root = panel.GetComponent<YauiElement>();
            var a = Box(root, 100f, 100f);
            var b = Box(root, 100f, 100f);
            YauiPanel.ForceUpdate();
            var rect = b.LayoutRect;

            a.Translate = new Vector2(500f, 0f);
            a.Scale = new Vector2(2f, 2f);
            YauiPanel.ForceUpdate();

            Assert.AreEqual(rect, b.LayoutRect);
            Assert.AreSame(a, HitTest(panel, 550f, 50f), "Hits follow the render transform.");
        }

        [Test]
        public void MasksSplitTheDrawOrderAndClipHits()
        {
            var panel = CreatePanel();
            var root = panel.GetComponent<YauiElement>();
            var layout = root.Layout;
            layout.AlignItems = FlexAlign.FlexStart;
            root.Layout = layout;

            var outer = Box(root, 200f, 200f);
            outer.gameObject.AddComponent<YauiMask>();
            var inner = Box(outer, 100f, 100f, Color.blue);
            inner.gameObject.AddComponent<YauiMask>().ShowMaskGraphic = false;
            var content = Box(inner, 400f, 50f, Color.red);
            var after = Box(root, 50f, 50f, Color.green);
            YauiPanel.ForceUpdate();

            var kinds = new List<Core.SegmentKind>();
            var depths = new List<int>();
            foreach (var segment in panel.CurrentState.Segments)
            {
                kinds.Add(segment.Kind);
                depths.Add(segment.StencilDepth);
            }

            // Root and outer box, push outer, push inner (its box is hidden), content, pop inner, pop outer, after.
            CollectionAssert.AreEqual(new[]
            {
                Core.SegmentKind.Draw, Core.SegmentKind.MaskPush, Core.SegmentKind.MaskPush, Core.SegmentKind.Draw,
                Core.SegmentKind.MaskPop, Core.SegmentKind.MaskPop, Core.SegmentKind.Draw
            }, kinds);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 2, 2, 1, 0 }, depths);

            Assert.AreSame(content, HitTest(panel, 50f, 25f));
            Assert.AreSame(outer, HitTest(panel, 150f, 25f), "The content is clipped by the inner mask's bounds.");
            Assert.AreSame(after, HitTest(panel, 25f, 225f));
        }

        private sealed class TestDraw : YauiCustomDraw
        {
            public int Collected;

            protected override void OnCollectDraws(YauiDrawList draws)
            {
                Collected++;
            }
        }

        private static List<Core.SegmentKind> SegmentKinds(YauiPanel panel)
        {
            var kinds = new List<Core.SegmentKind>();
            foreach (var segment in panel.CurrentState.Segments) kinds.Add(segment.Kind);

            return kinds;
        }

        [Test]
        public void CustomDrawsSplitTheDrawOrder()
        {
            var panel = CreatePanel();
            var root = panel.GetComponent<YauiElement>();
            var a = Box(root, 100f, 100f);
            var draw = a.gameObject.AddComponent<TestDraw>();
            Box(a, 50f, 50f, Color.red);
            Box(root, 100f, 100f, Color.blue);
            YauiPanel.ForceUpdate();

            // Root and a, a's meshes, a's child and b.
            var segments = panel.CurrentState.Segments;
            CollectionAssert.AreEqual(
                new[] { Core.SegmentKind.Draw, Core.SegmentKind.Custom, Core.SegmentKind.Draw }, SegmentKinds(panel));
            Assert.AreSame(draw, segments[1].CustomDraw);
            Assert.AreEqual(2, segments[2].Count);
            Assert.Greater(draw.Collected, 0, "Custom draws record their draws at the collection.");

            // Root, a and a's child, a's meshes, b.
            draw.Position = YauiCustomDrawPosition.AfterChildren;
            YauiPanel.ForceUpdate();
            CollectionAssert.AreEqual(
                new[] { Core.SegmentKind.Draw, Core.SegmentKind.Custom, Core.SegmentKind.Draw }, SegmentKinds(panel));
            Assert.AreEqual(1, segments[2].Count);

            draw.enabled = false;
            YauiPanel.ForceUpdate();
            CollectionAssert.AreEqual(new[] { Core.SegmentKind.Draw }, SegmentKinds(panel));
        }

        [Test]
        public void CustomDrawsFollowTheMaskOfTheirElement()
        {
            var panel = CreatePanel();
            var root = panel.GetComponent<YauiElement>();
            var outer = Box(root, 200f, 200f);
            var mask = outer.gameObject.AddComponent<YauiMask>();
            Box(outer, 50f, 50f, Color.red);
            var draw = outer.gameObject.AddComponent<TestDraw>();
            draw.Position = YauiCustomDrawPosition.AfterChildren;
            YauiPanel.ForceUpdate();

            // After the children: inside the mask.
            var depths = new List<int>();
            foreach (var segment in panel.CurrentState.Segments) depths.Add(segment.StencilDepth);

            CollectionAssert.AreEqual(new[]
            {
                Core.SegmentKind.Draw, Core.SegmentKind.MaskPush, Core.SegmentKind.Draw, Core.SegmentKind.Custom,
                Core.SegmentKind.MaskPop
            }, SegmentKinds(panel));
            CollectionAssert.AreEqual(new[] { 0, 1, 1, 1, 1 }, depths);

            // After the element itself: hidden with the mask graphic.
            draw.Position = YauiCustomDrawPosition.AfterSelf;
            mask.ShowMaskGraphic = false;
            YauiPanel.ForceUpdate();
            CollectionAssert.DoesNotContain(SegmentKinds(panel), Core.SegmentKind.Custom);
        }

        private static Sprite MakeSprite(int size, bool readable)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.Apply(false, !readable);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        [Test]
        public void SmallSpritesShareAnAtlasAndDrawsSplitAtNineTextures()
        {
            var panel = CreatePanel();
            var root = panel.GetComponent<YauiElement>();
            var sprites = new List<Sprite>();
            for (var i = 0; i < 20; i++) sprites.Add(MakeSprite(32, false));

            for (var i = 0; i < 12; i++) sprites.Add(MakeSprite(128, false));

            foreach (var sprite in sprites)
            {
                var image = Create<YauiImage>(root);
                var layout = LayoutStyle.Default;
                layout.Width = 10f;
                layout.Height = 10f;
                image.Layout = layout;
                image.Sprite = sprite;
            }

            YauiPanel.ForceUpdate();
            var segments = panel.CurrentState.Segments;

            // One atlas page and 12 textures: 8 in the first draw, 5 in the second.
            Assert.AreEqual(2, segments.Count);
            Assert.AreEqual(8, segments[0].TextureCount);
            Assert.AreEqual(5, segments[1].TextureCount);
            foreach (var sprite in sprites) Object.DestroyImmediate(sprite.texture);
        }

        [Test]
        public void GpuStoreReusesFreedSlots()
        {
            using var store = new GpuStore<int>(4, 1);
            var a = store.Allocate();
            var b = store.Allocate();
            Assert.AreEqual(1, a);
            Assert.AreEqual(2, b);
            store.Free(a);
            Assert.AreEqual(a, store.Allocate());

            var capacity = GpuStore<int>.RangeCapacity(9);
            Assert.AreEqual(16, capacity);
            var range = store.AllocateRange(capacity);
            store.FreeRange(range, capacity);
            Assert.AreEqual(range, store.AllocateRange(capacity));
        }

        [Test]
        public void PreparingTheSameTextAgainKeepsTheUvs()
        {
            if (!Text.AtgText.IsSupported) Assert.Ignore("The ATG internals do not match this Unity version.");

#pragma warning disable 618
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#pragma warning restore 618
            using var atg = new Text.AtgText();
            var request = new Text.TextRequest
            {
                Font = Text.AtgText.GetFontAsset(font),
                FontSize = 32f,
                Color = Color.white,
                WordWrap = true
            };

            // Preparing the same text again (OnValidate after an undo) makes the native generator return its cached
            // result, whose UVs are already in the atlas.
            var first = new List<Text.GlyphQuad>();
            var again = new List<Text.GlyphQuad>();
            atg.Prepare(System.MemoryExtensions.AsSpan("Hello, YAUI"), request);
            atg.Generate(200f);
            Text.AtgText.ResolveMissingGlyphs(new List<Text.AtgText> { atg });
            atg.Convert(first);
            for (var i = 0; i < 3; i++)
            {
                atg.Prepare(System.MemoryExtensions.AsSpan("Hello, YAUI"), request);
                atg.Generate(200f);
            }

            atg.Convert(again);

            Assert.AreEqual(first.Count, again.Count);
            for (var i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i].Uv, again[i].Uv, $"The UVs of glyph {i}.");
                Assert.Greater(first[i].Uv.z - first[i].Uv.x, 0f);
            }
        }

        private static List<PrimitiveData> DrawnGlyphs(YauiText text)
        {
            var result = new List<PrimitiveData>();
            var (start, capacity) = text.GlyphRange;
            for (var i = 0; i < capacity; i++)
            {
                var p = Core.YauiSystem.Primitives[start + i];
                if (p.Rect.z > 0f) result.Add(p);
            }

            return result;
        }

        [Test]
        public void EllipsisCutsTheTextToItsBox()
        {
            var panel = CreatePanel();
            var root = panel.GetComponent<YauiElement>();
            var layout = root.Layout;
            layout.AlignItems = FlexAlign.FlexStart;
            root.Layout = layout;
            var text = Create<YauiText>(root);
            text.WordWrap = false;
            text.FontSize = 32f;
            text.Text = "The quick brown fox jumps over the lazy dog";
            var textLayout = text.Layout;
            textLayout.Width = 200f;
            text.Layout = textLayout;
            YauiPanel.ForceUpdate();
            var overflowing = DrawnGlyphs(text);
            Assert.Greater(overflowing[^1].Rect.x, 200f, "Without an ellipsis the text overflows.");

            text.Overflow = TextOverflow.Ellipsis;
            YauiPanel.ForceUpdate();
            var elided = DrawnGlyphs(text);
            Assert.Less(elided.Count, overflowing.Count);
            foreach (var glyph in elided)
                Assert.LessOrEqual(glyph.Rect.x + glyph.Rect.z, 200f + Text.AtgText.VertexPadding + 0.5f);

            // The layout still sees the size of the whole text.
            var height = text.LayoutRect.height;
            text.Text = "The quick brown fox jumps over the lazy cat";
            YauiPanel.ForceUpdate();
            Assert.AreEqual(height, text.LayoutRect.height);
            Assert.AreEqual(elided.Count, DrawnGlyphs(text).Count);
        }

        [Test]
        public void TextShadowAndOutlineAddPrimitives()
        {
            var panel = CreatePanel();
            var text = Create<YauiText>(panel.GetComponent<YauiElement>());
            text.FontSize = 32f;
            text.Text = "Shadow";
            YauiPanel.ForceUpdate();
            var plain = DrawnGlyphs(text);

            text.OutlineWidth = 2f;
            text.ShadowOffset = new Vector2(2f, 3f);
            YauiPanel.ForceUpdate();
            var styled = DrawnGlyphs(text);

            Assert.AreEqual(plain.Count * 2, styled.Count, "A shadow for each glyph.");
            for (var i = 0; i < plain.Count; i++)
            {
                var shadow = styled[i];
                var glyph = styled[plain.Count + i];
                Assert.IsTrue((shadow.Flags & PrimitiveFlags.Shadow) != 0);
                Assert.IsTrue((glyph.Flags & PrimitiveFlags.Border) != 0);
                // The outlined glyph grows around the plain one, and the shadow follows it with the offset.
                Assert.Less(glyph.Rect.x, plain[i].Rect.x);
                Assert.Greater(glyph.Rect.z, plain[i].Rect.z);
                Assert.AreEqual(glyph.Rect.x + 2f, shadow.Rect.x, 1e-3f);
                Assert.AreEqual(glyph.Rect.y + 3f, shadow.Rect.y, 1e-3f);
            }
        }

        private static Texture2D TestTexture()
        {
            var texture = new Texture2D(8, 8);
            texture.hideFlags = HideFlags.DontSave;
            return texture;
        }

        [Test]
        public void LinearFillCutsTheImageAndItsUvs()
        {
            var panel = CreatePanel();
            var texture = TestTexture();
            try
            {
                var image = Create<YauiImage>(panel.GetComponent<YauiElement>());
                var layout = image.Layout;
                layout.Width = 100f;
                layout.Height = 50f;
                image.Layout = layout;
                image.Sprite = Sprite.Create(texture, new Rect(0f, 0f, 8f, 8f), Vector2.zero);
                image.Type = ImageType.Filled;
                image.FillMethod = FillMethod.Horizontal;
                image.FillOrigin = (int)FillOriginHorizontal.Right;
                image.FillAmount = 0.25f;
                YauiPanel.ForceUpdate();

                var p = Core.YauiSystem.Primitives[image.PrimitiveRange.Start];
                Assert.AreEqual(75f, p.Rect.x, 1e-3f);
                Assert.AreEqual(25f, p.Rect.z, 1e-3f);
                Assert.AreEqual(50f, p.Rect.w, 1e-3f);

                image.FillMethod = FillMethod.Radial360;
                YauiPanel.ForceUpdate();
                p = Core.YauiSystem.Primitives[image.PrimitiveRange.Start];
                Assert.IsTrue((p.Flags & PrimitiveFlags.RadialFill) != 0);
                Assert.AreEqual(100f, p.Rect.z, 1e-3f, "A radial fill covers the whole rect.");
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void RawImageDrawsItsTexture()
        {
            var panel = CreatePanel();
            var texture = TestTexture();
            try
            {
                var image = Create<YauiRawImage>(panel.GetComponent<YauiElement>());
                var layout = image.Layout;
                layout.Width = 40f;
                layout.Height = 30f;
                image.Layout = layout;
                image.Texture = texture;
                YauiPanel.ForceUpdate();

                var p = Core.YauiSystem.Primitives[image.PrimitiveSlot];
                Assert.AreEqual(new float4(0f, 0f, 40f, 30f), p.Rect);
                Assert.AreSame(texture, Core.YauiSystem.Textures.Get(PrimitiveTexture.IdOf(p.Flags)));
                Assert.AreSame(image, HitTest(panel, 20f, 15f), "Hit without a visible box.");

                image.Texture = null;
                YauiPanel.ForceUpdate();
                Assert.AreEqual(0, image.PrimitiveSlot);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}