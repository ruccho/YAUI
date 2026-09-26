using System;
using System.Collections.Generic;
using NUnit.Framework;
using Yaui.Layout.Yoga;
using Ref = Yaui.Tests.YogaReference;

namespace Yaui.Tests
{
    /// <summary>
    /// The Burst port of Yoga (Yaui.Layout.Yoga) against the managed port it came from (YogaReference): the same
    /// random trees, laid out and then changed and laid out again, must give the same results.
    /// </summary>
    public class YogaEquivalenceTests
    {
        private readonly List<YogaNode> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var node in _created) YogaNodeStore.Destroy(node);

            _created.Clear();
        }

        // A text-like measure: wraps 160 units of content at the available width, 20 units per line.
        private static YogaSize Measure(float width, YogaMeasureMode widthMode)
        {
            var w = widthMode == YogaMeasureMode.Undefined || float.IsNaN(width)
                ? 160f
                : Math.Min(160f, Math.Max(width, 1f));
            var lines = (float)Math.Ceiling(160f / Math.Max(w, 1f));
            return new YogaSize(w, lines * 20f);
        }

        private sealed class Pair
        {
            public YogaNode Node;
            public Ref.YogaNode Reference;
            public readonly List<Pair> Children = new();
        }

        private static Ref.YogaValue R(YogaValue v)
        {
            return new Ref.YogaValue(v.Value, (Ref.YogaUnit)(int)v.Unit);
        }

        private YogaValue RandomLength(Random random, bool allowAuto)
        {
            switch (random.Next(allowAuto ? 4 : 3))
            {
                case 0: return YogaValue.Point(random.Next(0, 200));
                case 1: return YogaValue.Percent(random.Next(0, 100));
                case 2: return YogaValue.Undefined;
                default: return YogaValue.Auto;
            }
        }

        private Pair Build(Random random, int depth)
        {
            var pair = new Pair { Node = YogaNodeStore.Create(), Reference = new Ref.YogaNode() };
            _created.Add(pair.Node);
            var n = pair.Node;
            var r = pair.Reference;

            void Set<T>(T value, Action<T> a, Action<T> b)
            {
                a(value);
                b(value);
            }

            Set((FlexDirection)random.Next(4), v => n.FlexDirection = v, v => r.FlexDirection = v);
            Set((FlexWrap)random.Next(3), v => n.FlexWrap = v, v => r.FlexWrap = v);
            Set(
                new[]
                {
                    FlexJustify.FlexStart, FlexJustify.Center, FlexJustify.FlexEnd, FlexJustify.SpaceBetween,
                    FlexJustify.SpaceAround, FlexJustify.SpaceEvenly
                }[random.Next(6)],
                v => n.JustifyContent = v, v => r.JustifyContent = v);
            Set(new[] { FlexAlign.FlexStart, FlexAlign.Center, FlexAlign.FlexEnd, FlexAlign.Stretch }[random.Next(4)],
                v => n.AlignItems = v, v => r.AlignItems = v);
            Set(new[] { FlexAlign.Auto, FlexAlign.FlexStart, FlexAlign.Center, FlexAlign.Stretch }[random.Next(4)],
                v => n.AlignSelf = v, v => r.AlignSelf = v);
            Set(
                new[] { FlexAlign.FlexStart, FlexAlign.Center, FlexAlign.Stretch, FlexAlign.SpaceBetween }[
                    random.Next(4)],
                v => n.AlignContent = v, v => r.AlignContent = v);
            Set((float)random.Next(3), v => n.FlexGrow = v, v => r.FlexGrow = v);
            Set((float)random.Next(2), v => n.FlexShrink = v, v => r.FlexShrink = v);
            if (random.Next(3) == 0)
                Set(YogaValue.Point(random.Next(10, 100)), v => n.FlexBasis = v, v => r.FlexBasis = R(v));

            Set(RandomLength(random, true), v => n.Width = v, v => r.Width = R(v));
            Set(RandomLength(random, true), v => n.Height = v, v => r.Height = R(v));
            if (random.Next(4) == 0)
            {
                Set(RandomLength(random, false), v => n.MinWidth = v, v => r.MinWidth = R(v));
                Set(RandomLength(random, false), v => n.MaxHeight = v, v => r.MaxHeight = R(v));
            }

            for (var edge = 0; edge < 4; edge++)
            {
                var e = (YogaEdge)edge;
                Set(YogaValue.Point(random.Next(0, 3) * 5), v => n.SetMargin(e, v),
                    v => r.SetMargin((Ref.YogaEdge)(int)e, R(v)));
                Set(YogaValue.Point(random.Next(0, 3) * 4), v => n.SetPadding(e, v),
                    v => r.SetPadding((Ref.YogaEdge)(int)e, R(v)));
                Set((float)random.Next(0, 3), v => n.SetBorder(e, v), v => r.SetBorder((Ref.YogaEdge)(int)e, v));
            }

            Set((float)random.Next(0, 3) * 4, v => n.SetGap(YogaGutter.Column, v),
                v => r.SetGap(Ref.YogaGutter.Column, v));
            Set((float)random.Next(0, 3) * 4, v => n.SetGap(YogaGutter.Row, v), v => r.SetGap(Ref.YogaGutter.Row, v));
            if (depth > 0 && random.Next(8) == 0)
            {
                Set(FlexPositionType.Absolute, v => n.PositionType = v, v => r.PositionType = v);
                Set(YogaValue.Point(random.Next(0, 50)), v => n.SetPosition(YogaEdge.Left, v),
                    v => r.SetPosition(Ref.YogaEdge.Left, R(v)));
                Set(YogaValue.Point(random.Next(0, 50)), v => n.SetPosition(YogaEdge.Top, v),
                    v => r.SetPosition(Ref.YogaEdge.Top, R(v)));
            }

            var childCount = depth >= 4 ? 0 : random.Next(0, 5);
            if (childCount == 0 && random.Next(2) == 0)
            {
                n.SetMeasureFunction((_, w, wm, h, hm) => Measure(w, wm));
                r.MeasureFunction = (_, w, wm, h, hm) =>
                {
                    var size = Measure(w, (YogaMeasureMode)(int)wm);
                    return new Ref.YogaSize(size.Width, size.Height);
                };
                return pair;
            }

            for (var i = 0; i < childCount; i++)
            {
                var child = Build(random, depth + 1);
                pair.Children.Add(child);
                n.InsertChild(child.Node, i);
                r.InsertChild(child.Reference, i);
            }

            return pair;
        }

        private static void AssertSame(Pair pair, string path)
        {
            var n = pair.Node;
            var r = pair.Reference;
            Assert.AreEqual(r.LayoutX, n.LayoutX, 0.01f, path + " x");
            Assert.AreEqual(r.LayoutY, n.LayoutY, 0.01f, path + " y");
            Assert.AreEqual(r.LayoutWidth, n.LayoutWidth, 0.01f, path + " width");
            Assert.AreEqual(r.LayoutHeight, n.LayoutHeight, 0.01f, path + " height");
            Assert.AreEqual(r.LayoutPaddingLeft, n.LayoutPaddingLeft, 0.01f, path + " padding");
            for (var i = 0; i < pair.Children.Count; i++) AssertSame(pair.Children[i], path + "/" + i);
        }

        private static void Collect(Pair pair, List<Pair> all)
        {
            all.Add(pair);
            foreach (var child in pair.Children) Collect(child, all);
        }

        [Test]
        public void RandomTreesLayOutLikeTheManagedPort([Range(0, 199)] int seed)
        {
            var random = new Random(seed);
            var root = Build(random, 0);
            root.Node.Width = YogaValue.Point(400);
            root.Reference.Width = Ref.YogaValue.Point(400);
            var height = random.Next(2) == 0 ? float.NaN : 600f;
            root.Node.CalculateLayout(400f, height);
            root.Reference.CalculateLayout(400f, height);
            AssertSame(root, "root");

            // Changes, then layout again (through the caches).
            var all = new List<Pair>();
            Collect(root, all);
            for (var i = 0; i < 3; i++)
            {
                var target = all[random.Next(all.Count)];
                var width = YogaValue.Point(random.Next(20, 150));
                target.Node.Width = width;
                target.Reference.Width = R(width);
            }

            root.Node.CalculateLayout(400f, height);
            root.Reference.CalculateLayout(400f, height);
            AssertSame(root, "relayout");
        }
    }
}