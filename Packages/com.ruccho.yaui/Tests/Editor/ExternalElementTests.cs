using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Yaui.Core;
using Yaui.Rendering;
using Yaui.Tests.External;

namespace Yaui.Tests
{
    /// <summary>
    /// An element built outside the package on the public API (<see cref="MonospaceLabel"/>): its measurement, its
    /// content primitives in the draw order, and its textures.
    /// </summary>
    public class ExternalElementTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null)
                    Object.DestroyImmediate(o);

            _created.Clear();
        }

        private YauiElement CreateRoot()
        {
            var go = new GameObject("TestPanel");
            _created.Add(go);
            var panel = go.AddComponent<YauiPanel>();
            panel.RenderMode = PanelRenderMode.World;
            panel.ReferenceResolution = new Vector2(1000f, 1000f);
            var root = panel.GetComponent<YauiElement>();
            var layout = root.Layout;
            layout.alignItems = FlexAlign.FlexStart;
            root.Layout = layout;
            return root;
        }

        private MonospaceLabel CreateLabel(YauiElement parent, string text)
        {
            var atlas = new Texture2D(256, 256, TextureFormat.Alpha8, false) { hideFlags = HideFlags.DontSave };
            _created.Add(atlas);
            var go = new GameObject("Label");
            go.transform.SetParent(parent.transform, false);
            var label = go.AddComponent<MonospaceLabel>();
            label.Atlas = atlas;
            label.Text = text;
            return label;
        }

        private static PrimitiveData Content(YauiElement element, int index)
        {
            return YauiSystem.Primitives.Read(element.ContentRange.Start + index);
        }

        [Test]
        public void ContentIsMeasuredAndWraps()
        {
            var label = CreateLabel(CreateRoot(), "Hello");
            YauiPanel.ForceUpdate();
            Assert.AreEqual(new Vector2(50f, 20f), label.LayoutRect.size);

            var layout = label.Layout;
            layout.width = 30f;
            label.Layout = layout;
            YauiPanel.ForceUpdate();
            Assert.AreEqual(new Vector2(30f, 40f), label.LayoutRect.size, "Three columns: two lines.");

            label.Text = "Hello world";
            YauiPanel.ForceUpdate();
            Assert.AreEqual(80f, label.LayoutRect.height, "Measured again after the text changed.");
        }

        [Test]
        public void ContentPrimitivesAreDrawnAfterTheBox()
        {
            var root = CreateRoot();
            var label = CreateLabel(root, "Hi");
            var box = label.Box;
            box.backgroundColor = Color.white;
            label.Box = box;
            YauiPanel.ForceUpdate();

            Assert.AreEqual(2, label.PrimitiveCount);
            var glyph = Content(label, 1);
            Assert.AreEqual((uint)label.NodeSlot, glyph.Node);
            Assert.AreEqual(PrimitiveFlags.Text, glyph.Flags & (PrimitiveFlags)0xffffu);
            Assert.AreEqual(10f, glyph.Rect.x);
            Assert.AreEqual(label.Atlas, YauiSystem.Textures.Get(PrimitiveTexture.IdOf(glyph.Flags)));
            Assert.AreEqual(MonospaceLabel.Spread, YauiSystem.Textures.Parameters(label.AtlasTexture.Id).y);

            // The box, then the whole block of the content.
            var state = root.PanelState;
            Assert.AreEqual(1 + label.ContentRange.Capacity, state.DrawCount);

            // More primitives than the block holds: a new block in the draw order.
            label.Text = "Hello world";
            YauiPanel.ForceUpdate();
            Assert.AreEqual(11, label.PrimitiveCount);
            Assert.AreEqual(1 + label.ContentRange.Capacity, state.DrawCount);
        }

        [Test]
        public void ShadowsAndRectanglesMixWithGlyphs()
        {
            var label = CreateLabel(CreateRoot(), "abc");
            label.Shadow = true;
            label.Underline = true;
            YauiPanel.ForceUpdate();

            Assert.AreEqual(7, label.PrimitiveCount);
            Assert.IsTrue((Content(label, 0).Flags & PrimitiveFlags.Shadow) != 0, "Shadows come first.");
            Assert.AreEqual(1f, Content(label, 0).Rect.x, "Offset by the shadow.");
            Assert.IsTrue((Content(label, 3).Flags & PrimitiveFlags.Shadow) == 0);
            var underline = Content(label, 6);
            Assert.AreEqual(0, PrimitiveTexture.IdOf(underline.Flags), "A plain rectangle.");
            Assert.AreEqual(30f, underline.Rect.z);

            // Fewer primitives in the same block empty the rest.
            label.Shadow = false;
            YauiPanel.ForceUpdate();
            Assert.AreEqual(4, label.PrimitiveCount);
            Assert.AreEqual(0f, Content(label, 5).Rect.z);
        }

        [Test]
        public void DisablingReleasesTheContentAndTheTexture()
        {
            var label = CreateLabel(CreateRoot(), "Hi");
            YauiPanel.ForceUpdate();
            var id = label.AtlasTexture.Id;
            Assert.IsNotNull(YauiSystem.Textures.Get(id));

            label.enabled = false;
            Assert.AreEqual(0, label.ContentRange.Capacity);
            Assert.IsNull(YauiSystem.Textures.Get(id), "The only reference was released.");

            label.enabled = true;
            YauiPanel.ForceUpdate();
            Assert.AreEqual(2, label.PrimitiveCount);
            Assert.IsTrue(label.AtlasTexture.IsValid);
        }

        [Test]
        public void ChangingTheTextureRebuildsTheDrawOrder()
        {
            var root = CreateRoot();
            var label = CreateLabel(root, "Hi");
            YauiPanel.ForceUpdate();
            var state = root.PanelState;
            Assert.IsFalse(state.OrderDirty);

            var other = new Texture2D(256, 256, TextureFormat.Alpha8, false) { hideFlags = HideFlags.DontSave };
            _created.Add(other);

            // Registered already, so that it does not take over the id the label releases.
            var held = YauiTexture.Acquire(other);
            label.Atlas = other;
            Assert.IsTrue(state.OrderDirty, "The draw splits by textures.");
            YauiPanel.ForceUpdate();
            Assert.AreEqual(held.Id, state.SegmentTextures[0]);
            held.Release();
        }

        [Test]
        public void MeasureExceptionsGiveAnEmptySize()
        {
            var root = CreateRoot();
            var go = new GameObject("Throwing");
            go.transform.SetParent(root.transform, false);
            var element = go.AddComponent<ThrowingMeasureElement>();

            // Logged at every measurement, which Yoga may repeat.
            LogAssert.ignoreFailingMessages = true;
            YauiPanel.ForceUpdate();
            Assert.AreEqual(Vector2.zero, element.LayoutRect.size);
        }
    }
}
