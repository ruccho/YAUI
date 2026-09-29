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
        private readonly List<GameObject> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
                if (go != null)
                    Object.DestroyImmediate(go);

            _created.Clear();
        }

        private YauiPanel CreatePanel(float width = 1000f, float height = 1000f)
        {
            var go = new GameObject("TestPanel");
            _created.Add(go);
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
            layout.width = width;
            layout.height = height;
            element.Layout = layout;
            var box = BoxStyle.Default;
            box.backgroundColor = color == default ? Color.white : color;
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
            layout.direction = FlexDirection.Row;
            layout.padding = new Edges(10f);
            layout.gap = new Vector2(20f, 0f);
            layout.alignItems = FlexAlign.FlexStart;
            root.Layout = layout;

            var a = Box(root, 100f, 50f);
            var b = Box(root, 100f, 50f);
            var grow = b.Layout;
            grow.grow = 1f;
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
            layout.width = Length.Percent(50f);
            layout.height = Length.Percent(25f);
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
            layout.alignItems = FlexAlign.FlexStart;
            root.Layout = layout;

            var text = Create<YauiText>(root);
            text.FontSize = 32f;
            text.Text = "Hello";
            YauiPanel.ForceUpdate();
            var single = text.LayoutRect.size;
            Assert.Greater(single.x, 0f);
            Assert.Greater(single.y, 0f);

            var textLayout = text.Layout;
            textLayout.width = 120f;
            text.Layout = textLayout;
            text.Text = "The quick brown fox jumps over the lazy dog";
            YauiPanel.ForceUpdate();
            Assert.AreEqual(120f, text.LayoutRect.width);
            Assert.Greater(text.LayoutRect.height, single.y * 2f, "The text wraps into several lines.");
        }

        [Test]
        public void FallbackTextIsMeasured()
        {
            if (!Text.FallbackText.IsAvailable) Assert.Ignore("The public TextGenerator needs Unity 6000.7.");

            Text.AtgText.ForceFallback = true;
            try
            {
                var panel = CreatePanel();
                var root = panel.GetComponent<YauiElement>();
                var layout = root.Layout;
                layout.alignItems = FlexAlign.FlexStart;
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
            boxLayout.alignItems = FlexAlign.FlexStart;
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
            layout.alignItems = FlexAlign.FlexStart;
            root.Layout = layout;

            var back = Box(root, 200f, 200f);
            var front = Box(back, 100f, 100f, Color.red);
            var invisible = Create<YauiElement>(root);
            var invisibleLayout = LayoutStyle.Default;
            invisibleLayout.position = PositionType.Absolute;
            invisibleLayout.inset = new Edges(0f, 0f, Length.Auto, Length.Auto);
            invisibleLayout.width = 500f;
            invisibleLayout.height = 500f;
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
            layout.alignItems = FlexAlign.FlexStart;
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
                layout.position = PositionType.Absolute;
                layout.inset = new Edges(0f, 0f, Length.Auto, Length.Auto);
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
            layout.alignItems = FlexAlign.FlexStart;
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
            public int collected;

            protected override void OnCollectDraws(YauiDrawList draws)
            {
                collected++;
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
            Assert.Greater(draw.collected, 0, "Custom draws record their draws at the collection.");

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
        public void DrawsUseTheShaderFeaturesOfTheirPrimitives()
        {
            var panel = CreatePanel();
            var root = panel.GetComponent<YauiElement>();
            var box = Box(root, 100f, 50f);
            YauiPanel.ForceUpdate();
            Assert.AreEqual(ShaderFeatures.None, panel.CurrentState.Segments[0].Features);

            // A style change writes the primitive without rebuilding the draw order.
            var style = box.Box;
            style.shadowColor = Color.black;
            style.shadowBlur = 4f;
            style.borderWidth = 2f;
            box.Box = style;
            YauiPanel.ForceUpdate();
            Assert.AreEqual(ShaderFeatures.Shadow | ShaderFeatures.Border, panel.CurrentState.Segments[0].Features);

            var text = Create<YauiText>(root);
            text.Text = "A";
            style.shadowColor = Color.clear;
            style.borderWidth = 0f;
            box.Box = style;
            YauiPanel.ForceUpdate();
            Assert.AreEqual(ShaderFeatures.Text, panel.CurrentState.Segments[0].Features);

            // Rewrites the glyphs in place.
            text.OutlineWidth = 1f;
            text.OutlineColor = Color.black;
            YauiPanel.ForceUpdate();
            Assert.AreEqual(ShaderFeatures.Text | ShaderFeatures.Border, panel.CurrentState.Segments[0].Features);
        }

        private static List<Material> SegmentMaterials(YauiPanel panel)
        {
            var materials = new List<Material>();
            foreach (var segment in panel.CurrentState.Segments) materials.Add(segment.Material);

            return materials;
        }

        private static YauiElement PlacedBox(Component parent, float x, float y, Material material)
        {
            var box = Box(parent, 100f, 100f);
            var layout = box.Layout;
            layout.position = PositionType.Absolute;
            layout.inset = new Edges(x, y, Length.Auto, Length.Auto);
            box.Layout = layout;
            box.Material = material;
            return box;
        }

        [Test]
        public void DrawsOfTheSameMaterialMergeWhereNothingOverlaps()
        {
            var panel = CreatePanel();
            var root = panel.GetComponent<YauiElement>();
            var custom = new Material(Resources.Load<Shader>("Yaui/Uber"));
            for (var i = 0; i < 4; i++) PlacedBox(root, i * 200f, 0f, i % 2 == 0 ? null : custom);

            YauiPanel.ForceUpdate();
            CollectionAssert.AreEqual(new[] { null, custom }, SegmentMaterials(panel));
            Object.DestroyImmediate(custom);
        }

        private static List<ShaderFeatures> SegmentFeatures(YauiPanel panel)
        {
            var features = new List<ShaderFeatures>();
            foreach (var segment in panel.CurrentState.Segments) features.Add(segment.Features);

            return features;
        }

        /// <summary>A 5x5 grid of boxes, the one at <paramref name="shadowed"/> with a shadow.</summary>
        private List<YauiElement> BoxGrid(YauiPanel panel, float size, int shadowed)
        {
            var root = panel.GetComponent<YauiElement>();
            var boxes = new List<YauiElement>();
            for (var i = 0; i < 25; i++)
            {
                var box = PlacedBox(root, i % 5 * (size + 10f), i / 5 * (size + 10f), null);
                var layout = box.Layout;
                layout.width = size;
                layout.height = size;
                box.Layout = layout;
                if (i == shadowed)
                {
                    var style = box.Box;
                    style.shadowColor = Color.black;
                    style.shadowBlur = 2f;
                    box.Box = style;
                }

                boxes.Add(box);
            }

            return boxes;
        }

        [Test]
        public void DrawsSplitByShadowsWhenThePixelsPayForIt()
        {
            var panel = CreatePanel();
            BoxGrid(panel, 190f, 12);
            YauiPanel.ForceUpdate();
            CollectionAssert.AreEquivalent(new[] { ShaderFeatures.None, ShaderFeatures.Shadow },
                SegmentFeatures(panel));

            try
            {
                YauiBatching.SplitByShaderFeatures = false;
                YauiPanel.ForceUpdate();
                CollectionAssert.AreEqual(new[] { ShaderFeatures.Shadow }, SegmentFeatures(panel));

                // Only the GPU gains from a split.
                YauiBatching.SplitByShaderFeatures = true;
                YauiBatching.GpuWeight = 0f;
                YauiPanel.ForceUpdate();
                CollectionAssert.AreEqual(new[] { ShaderFeatures.Shadow }, SegmentFeatures(panel));
            }
            finally
            {
                YauiBatching.SplitByShaderFeatures = true;
                YauiBatching.GpuWeight = 1f;
            }
        }

        [Test]
        public void SmallDrawsDoNotSplit()
        {
            var panel = CreatePanel();
            BoxGrid(panel, 10f, 12);
            YauiPanel.ForceUpdate();
            CollectionAssert.AreEqual(new[] { ShaderFeatures.Shadow }, SegmentFeatures(panel));
        }

        [Test]
        public void SplitsKeepOverlappingBoxesInOrder()
        {
            var panel = CreatePanel();
            BoxGrid(panel, 190f, 0);

            // Over the shadowed box: drawn after it, so the plain boxes before take a draw of their own.
            PlacedBox(panel.GetComponent<YauiElement>(), 50f, 50f, null);
            YauiPanel.ForceUpdate();
            var state = panel.CurrentState;
            var order = state.DrawOrder.ToArray();
            var position = new Dictionary<uint, int>();
            for (var i = 0; i < order.Length; i++) position[order[i]] = i;

            var painter = state.PainterOrder.ToArray();
            Assert.Less(position[painter[0]], position[painter[^1]]);
            Assert.Contains(ShaderFeatures.Shadow, SegmentFeatures(panel));
        }

        /// <summary>The canvas bounds of a primitive as the reordering sees them (axis-aligned nodes, no shadows).</summary>
        private static Rect CanvasBounds(PrimitiveData p)
        {
            if (p.Rect.z <= 0f || p.Rect.w <= 0f) return Rect.zero;

            var node = Core.YauiSystem.Nodes.Gpu.AsArray()[(int)p.Node];
            var min = new Vector2(p.Rect.x * node.Matrix.x, p.Rect.y * node.Matrix.w) + (Vector2)node.Translation;
            return new Rect(min, new Vector2(p.Rect.z * node.Matrix.x, p.Rect.w * node.Matrix.w));
        }

        [Test]
        public void ReorderedGridKeepsOverlappingPairsInOrder()
        {
            // The grid of the benchmark on a Pixel 5, whose labels overflow into the next cells.
            var panel = CreatePanel(978f, 2120f);
            var root = panel.GetComponent<YauiElement>();
            var rootLayout = LayoutStyle.Default;
            rootLayout.direction = FlexDirection.Row;
            rootLayout.wrap = FlexWrap.Wrap;
            rootLayout.alignContent = FlexAlign.FlexStart;
            root.Layout = rootLayout;
            var boxes = new Material(Resources.Load<Shader>("Yaui/Uber"));
            var labels = new Material(Resources.Load<Shader>("Yaui/Uber"));
            for (var i = 0; i < 300; i++)
            {
                var cell = Box(root, 28f, 38f);
                var cellLayout = cell.Layout;
                cellLayout.margin = new Edges(2f);
                cellLayout.direction = FlexDirection.Row;
                cellLayout.alignItems = FlexAlign.Center;
                cellLayout.shrink = 0f;
                cell.Layout = cellLayout;
                cell.Material = boxes;
                var icon = Box(cell, 9.6f, 29.4f);
                var iconLayout = icon.Layout;
                iconLayout.margin = new Edges(1.6f, 0f, 1.6f, 0f);
                icon.Layout = iconLayout;
                icon.Material = boxes;
                var label = Create<YauiText>(cell);
                var labelLayout = LayoutStyle.Default;
                labelLayout.grow = 1f;
                label.Layout = labelLayout;
                label.FontSize = 11.52f;
                label.WordWrap = false;
                label.Text = (i * 7).ToString();
                label.Material = labels;
            }

            YauiPanel.ForceUpdate();
            var state = panel.CurrentState;
            var painter = state.PainterOrder.ToArray();
            var drawn = state.DrawOrder.ToArray();
            var primitives = Core.YauiSystem.Primitives.AsArray();
            var position = new Dictionary<uint, int>();
            for (var i = 0; i < drawn.Length; i++) position[drawn[i]] = i;

            var bounds = new Rect[painter.Length];
            for (var i = 0; i < painter.Length; i++) bounds[i] = CanvasBounds(primitives[(int)painter[i]]);

            // Every overlapping pair is drawn in the painter's order.
            for (var i = 0; i < painter.Length; i++)
            for (var j = i + 1; j < painter.Length; j++)
                if (bounds[i].Overlaps(bounds[j]) && bounds[i].width > 0f && bounds[j].width > 0f)
                    Assert.Less(position[painter[i]], position[painter[j]], $"{i} and {j} were swapped");

            // The draws of the same rule computed by brute force.
            var key = new Dictionary<uint, int>();
            foreach (var segment in state.Segments)
                for (var i = segment.Start; i < segment.Start + segment.Count; i++)
                    key[drawn[i]] = segment.Material == boxes ? 0 : 1;

            var layers = new int[painter.Length];
            var sorted = new List<(int Layer, int Key, int Index)>();
            var last = new int[2];
            for (var i = 0; i < painter.Length; i++)
            {
                var k = key[painter[i]];
                var layer = bounds[i].width > 0f ? 0 : last[k];
                if (bounds[i].width > 0f)
                    for (var j = 0; j < i; j++)
                        if (bounds[j].width > 0f && bounds[i].Overlaps(bounds[j]))
                            layer = Mathf.Max(layer, layers[j] + (key[painter[j]] != k ? 1 : 0));

                layers[i] = layer;
                last[k] = layer;
                sorted.Add((layer, k, i));
            }

            sorted.Sort();
            var best = 0;
            for (var i = 0; i < sorted.Count; i++)
                if (i == 0 || sorted[i].Key != sorted[i - 1].Key)
                    best++;

            Assert.AreEqual(best, state.Segments.Count);
            Object.DestroyImmediate(boxes);
            Object.DestroyImmediate(labels);
        }

        [Test]
        public void OverlappingDrawsKeepTheirOrder()
        {
            var panel = CreatePanel();
            var root = panel.GetComponent<YauiElement>();
            var custom = new Material(Resources.Load<Shader>("Yaui/Uber"));
            var boxes = new List<YauiElement>
            {
                PlacedBox(root, 0f, 0f, null),
                PlacedBox(root, 50f, 50f, custom),
                PlacedBox(root, 100f, 0f, null),
                PlacedBox(root, 500f, 500f, custom)
            };

            // The third box covers the second: it must stay after it, in a draw of its own.
            YauiPanel.ForceUpdate();
            CollectionAssert.AreEqual(new[] { null, custom, null }, SegmentMaterials(panel));

            // Apart, they merge.
            var layout = boxes[2].Layout;
            layout.inset = new Edges(300f, 0f, Length.Auto, Length.Auto);
            boxes[2].Layout = layout;
            YauiPanel.ForceUpdate();
            CollectionAssert.AreEqual(new[] { null, custom }, SegmentMaterials(panel));
            Object.DestroyImmediate(custom);
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
                layout.width = 10f;
                layout.height = 10f;
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

        private static Text.TextRequest AtgRequest()
        {
#pragma warning disable 618
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#pragma warning restore 618
            return new Text.TextRequest
            {
                Font = Text.AtgText.GetFontAsset(font),
                FontSize = 32f,
                Color = Color.white
            };
        }

        private static List<Text.GlyphQuad> GenerateGlyphs(Text.AtgText atg, string value, Text.TextRequest request)
        {
            var glyphs = new List<Text.GlyphQuad>();
            atg.Prepare(System.MemoryExtensions.AsSpan(value), request);
            atg.Generate(-1f);
            Text.AtgText.ResolveMissingGlyphs(new List<Text.AtgText> { atg });
            atg.Convert(glyphs);
            return glyphs;
        }

        [Test]
        public void GlyphsAreYDownFromTheTopOfTheText()
        {
            if (!Text.AtgText.IsSupported) Assert.Ignore("The ATG internals do not match this Unity version.");

            using var atg = new Text.AtgText();
            var glyphs = GenerateGlyphs(atg, "A\nB", AtgRequest());
            Assert.AreEqual(2, glyphs.Count);
            foreach (var glyph in glyphs)
            {
                Assert.Greater(glyph.Max.y, glyph.Min.y);
                Assert.GreaterOrEqual(glyph.Min.y, -Text.AtgText.VertexPadding - 0.5f);
                Assert.LessOrEqual(glyph.Max.y, atg.Size.y + Text.AtgText.VertexPadding + 0.5f);
            }

            Assert.Greater(glyphs[1].Min.y, glyphs[0].Max.y - Text.AtgText.VertexPadding * 2f,
                "The second line is below the first.");
        }

        [Test]
        public void RichTextTagsAreParsed()
        {
            if (!Text.AtgText.IsSupported) Assert.Ignore("The ATG internals do not match this Unity version.");

            using var atg = new Text.AtgText();
            var request = AtgRequest();
            request.RichText = true;
            var glyphs = GenerateGlyphs(atg, "<color=#FF0000>A</color>B", request);
            Assert.AreEqual(2, glyphs.Count, "The tags are not drawn.");
            Assert.AreEqual(new Color32(255, 0, 0, 255), glyphs[0].Color);
            Assert.AreEqual(new Color32(255, 255, 255, 255), glyphs[1].Color);

            request.RichText = false;
            Assert.Greater(GenerateGlyphs(atg, "<b>A</b>", request).Count, 2, "Without rich text the tags are drawn.");
        }

        [Test]
        public void CharactersMissingFromTheFontFallBackToOsFonts()
        {
            if (!Text.AtgText.IsSupported) Assert.Ignore("The ATG internals do not match this Unity version.");

            // The built-in font has no Japanese.
            using var atg = new Text.AtgText();
            var glyphs = GenerateGlyphs(atg, "日本語", AtgRequest());
            Assert.AreEqual(3, glyphs.Count);
            foreach (var glyph in glyphs)
            {
                Assert.IsNotNull(glyph.Atlas);
                Assert.Greater(glyph.Uv.z - glyph.Uv.x, 0f);
            }
        }

        [Test]
        public void TextDataReferencesTheIcuData()
        {
            // Players load the ICU data through this reference to a built-in resource of the editor.
            var data = Resources.Load<Text.YauiTextData>(Text.YauiTextData.ResourcePath);
            Assert.IsNotNull(data);
            Assert.IsNotNull(data.IcuData);
            Assert.AreEqual(Text.YauiTextData.IcuDataName, data.IcuData.name);
        }

        [Test]
        public void WordBoundsSelectTheWordAtAnIndex()
        {
            if (!Text.AtgText.IsSupported || !Text.AtgSelection.Initialize())
                Assert.Ignore("The ATG internals do not match this Unity version.");

            using var atg = new Text.AtgText();
            GenerateGlyphs(atg, "hello world", AtgRequest());
            Text.AtgSelection.WordBounds(atg.GenerationInfo, 8, out var start, out var end);
            Assert.AreEqual(6, start);
            Assert.AreEqual(11, end);
        }

        private static List<PrimitiveData> DrawnGlyphs(YauiText text)
        {
            var result = new List<PrimitiveData>();
            var (start, capacity) = text.ContentRange;
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
            layout.alignItems = FlexAlign.FlexStart;
            root.Layout = layout;
            var text = Create<YauiText>(root);
            text.WordWrap = false;
            text.FontSize = 32f;
            text.Text = "The quick brown fox jumps over the lazy dog";
            var textLayout = text.Layout;
            textLayout.width = 200f;
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
                layout.width = 100f;
                layout.height = 50f;
                image.Layout = layout;
                image.Sprite = Sprite.Create(texture, new Rect(0f, 0f, 8f, 8f), Vector2.zero);
                image.Type = ImageType.Filled;
                image.FillMethod = FillMethod.Horizontal;
                image.FillOrigin = (int)FillOriginHorizontal.Right;
                image.FillAmount = 0.25f;
                YauiPanel.ForceUpdate();

                var p = Core.YauiSystem.Primitives[image.ContentRange.Start];
                Assert.AreEqual(75f, p.Rect.x, 1e-3f);
                Assert.AreEqual(25f, p.Rect.z, 1e-3f);
                Assert.AreEqual(50f, p.Rect.w, 1e-3f);

                image.FillMethod = FillMethod.Radial360;
                YauiPanel.ForceUpdate();
                p = Core.YauiSystem.Primitives[image.ContentRange.Start];
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
                layout.width = 40f;
                layout.height = 30f;
                image.Layout = layout;
                image.Texture = texture;
                YauiPanel.ForceUpdate();

                var p = Core.YauiSystem.Primitives[image.ContentRange.Start];
                Assert.AreEqual(new float4(0f, 0f, 40f, 30f), p.Rect);
                Assert.AreSame(texture, Core.YauiSystem.Textures.Get(PrimitiveTexture.IdOf(p.Flags)));
                Assert.AreSame(image, HitTest(panel, 20f, 15f), "Hit without a visible box.");

                image.Texture = null;
                YauiPanel.ForceUpdate();
                Assert.AreEqual(0, image.ContentRange.Capacity);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}