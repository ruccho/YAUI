using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Yaui.Layout.Yoga;
using Yaui.Rendering;

namespace Yaui.Core
{
    internal enum SegmentKind
    {
        /// <summary>Primitives drawn where the stencil equals <see cref="DrawSegment.StencilDepth"/>.</summary>
        Draw,

        /// <summary>A mask's shape incrementing the stencil from depth - 1 to <see cref="DrawSegment.StencilDepth"/>.</summary>
        MaskPush,

        /// <summary>A mask's shape decrementing the stencil from <see cref="DrawSegment.StencilDepth"/> back.</summary>
        MaskPop,

        /// <summary>The meshes of <see cref="DrawSegment.CustomDraw"/>; no primitives.</summary>
        Custom
    }

    /// <summary>A run of the draw order drawn in one draw call.</summary>
    internal struct DrawSegment
    {
        public int Start;
        public int Count;

        /// <summary>A custom material, or null for the uber shader. Always null for mask shapes.</summary>
        public Material Material;

        public SegmentKind Kind;

        /// <summary>The custom draw of a <see cref="SegmentKind.Custom"/> segment.</summary>
        public YauiCustomDraw CustomDraw;

        /// <summary>Number of masks around the primitives; 0 without masks (no stencil test).</summary>
        public int StencilDepth;

        /// <summary>The textures of the draw: <see cref="PanelState.SegmentTextures"/>[TextureStart..+TextureCount].</summary>
        public int TextureStart;

        public int TextureCount;
    }

    /// <summary>
    /// Runtime state of a <see cref="YauiPanel"/>: the depth-first order of its elements, which is both the layout
    /// tree and the draw order, and the draw order buffer.
    /// </summary>
    internal sealed class PanelState : IDisposable
    {
        public readonly YauiPanel Panel;

        /// <summary>Elements in depth-first order. Rebuilt from the Transform hierarchy when the structure changes.</summary>
        public readonly List<YauiElement> Elements = new();

        /// <summary>Elements whose layout style changed since the last submission.</summary>
        public readonly List<YauiElement> LayoutDirtyElements = new();

        private NativeList<int> nodes;
        private NativeList<int> parents;

        // End (exclusive) of the subtree of each depth-first index.
        private NativeList<int> subtreeEnd;

        // Depth-first indices whose subtrees need their transforms propagated again, unless all do.
        private NativeList<int> dirtyNodes;
        private NativeList<int2> transformRanges;
        private bool allTransformsDirty = true;
        private bool hitTestDirty;
        private NativeList<uint> order;
        private GraphicsBuffer orderBuffer;

        // Scratch of the transform pass, per depth-first index.
        private NativeList<float2x3> world;
        private NativeList<float> opacity;
        private NativeList<int> clipSlot;
        private NativeList<float4> clipRect;

        private readonly List<YauiElement> childBuffer = new();

        // The elements of the last hit test grid, by depth-first index.
        private readonly HitTestGrid hitTest = new();
        private readonly List<YauiElement> hitElements = new();
        private readonly List<YogaNode> yogaChildBuffer = new();

        public bool StructureDirty = true;

        /// <summary>A node is rotated or under a rounded clip: draw with per-pixel clipping.</summary>
        public bool NeedsPixelClip;

        private NativeArray<int> scanResult;

        /// <summary>The primitives of an element changed (e.g. a text needs more glyph slots).</summary>
        public bool OrderDirty;

        public bool TransformDirty = true;

        /// <summary>Set at submission when the layout job includes this panel; cleared when the results are applied.</summary>
        public bool LayoutScheduled;

        /// <summary>Texture ids of the segments, at most TextureRegistry.SlotCount per segment.</summary>
        public readonly List<int> SegmentTextures = new();

        /// <summary>Runs of the draw order by material; the draw calls of this panel.</summary>
        public readonly List<DrawSegment> Segments = new();

        /// <summary>Shader properties of each draw call (segment).</summary>
        public readonly List<MaterialPropertyBlock> SegmentProperties = new();

        public float2 CanvasSize;
        public float ScaleFactor = 1f;
        private float2 laidOutSize = -1f;
        private YogaNode layoutRoot;

        // Layout boundaries dirtied by their content since the last layout. Written at submission only.
        private readonly List<YogaNode> dirtyBoundaries = new();
        private Action<YogaNode> onBoundaryDirtied;

        // Texts whose content changed; whether their size did is resolved in the layout job, after generation.
        private readonly List<YauiText> pendingMeasures = new();

        public PanelState(YauiPanel panel)
        {
            Panel = panel;
            nodes = new NativeList<int>(64, Allocator.Persistent);
            parents = new NativeList<int>(64, Allocator.Persistent);
            subtreeEnd = new NativeList<int>(64, Allocator.Persistent);
            dirtyNodes = new NativeList<int>(64, Allocator.Persistent);
            transformRanges = new NativeList<int2>(16, Allocator.Persistent);
            order = new NativeList<uint>(64, Allocator.Persistent);
            world = new NativeList<float2x3>(64, Allocator.Persistent);
            opacity = new NativeList<float>(64, Allocator.Persistent);
            clipSlot = new NativeList<int>(64, Allocator.Persistent);
            clipRect = new NativeList<float4>(64, Allocator.Persistent);
        }

        public YauiElement Root => Panel != null ? Panel.Element : null;

        public GraphicsBuffer OrderBuffer => orderBuffer;

        public int DrawCount => order.Length;

        /// <summary>Whether anything is drawn: primitives, or the meshes of custom draws.</summary>
        public bool HasDraws => Segments.Count > 0;

        public void MarkStructureDirty()
        {
            StructureDirty = true;
            YauiSystem.RequestUpdate();
        }

        public void MarkTransformDirty()
        {
            TransformDirty = true;
            allTransformsDirty = true;
            YauiSystem.RequestUpdate();
        }

        /// <summary>The transform, opacity or clip of an element changed: its subtree needs to be propagated.</summary>
        public void MarkTransformDirty(YauiElement element)
        {
            TransformDirty = true;
            if (!StructureDirty && element.DfsIndex >= 0 && element.DfsIndex < nodes.Length &&
                Elements[element.DfsIndex] == element)
                dirtyNodes.Add(element.DfsIndex);
            else
                allTransformsDirty = true;

            YauiSystem.RequestUpdate();
        }

        /// <summary>Whether an element receives hits changed.</summary>
        public void MarkHitTestDirty()
        {
            hitTestDirty = true;
        }

        public void MarkLayoutDirty(YauiElement element)
        {
            if (!element.LayoutDirty)
            {
                element.LayoutDirty = true;
                LayoutDirtyElements.Add(element);
            }

            YauiSystem.RequestUpdate();
        }

        /// <summary>
        /// Main thread, at submission: rebuilds the depth-first order and the Yoga tree from the Transform hierarchy.
        /// </summary>
        public void RebuildStructure()
        {
            // Queries until the next collection see the last rendered state.
            if (hitTestDirty) BuildHitTest();

            StructureDirty = false;
            TransformDirty = true;
            allTransformsDirty = true;
            foreach (var element in Elements) element.DfsIndex = -1;

            Elements.Clear();
            nodes.Clear();
            parents.Clear();
            subtreeEnd.Clear();

            var root = Root;
            if (root != null && root.IsRegisteredTo(this)) Visit(root, -1);

            RebuildOrder();
        }

        /// <summary>Whether the draw order contains masks (the overlay then needs a stencil buffer).</summary>
        public bool HasMasks { get; private set; }

        // Draws at the end of a subtree, innermost last: a mask's removal, or a custom draw after the children.
        private readonly List<(int Index, bool Mask)> closeStack = new();
        private Material segmentMaterial;
        private int segmentStart;
        private int stencilDepth;

        /// <summary>
        /// Main thread: rebuilds the draw order buffer from the elements, split into draw calls where the material
        /// changes and around masks: a mask's shape is drawn into the stencil before its descendants and removed
        /// after them.
        /// </summary>
        public void RebuildOrder()
        {
            OrderDirty = false;
            order.Clear();
            Segments.Clear();
            SegmentTextures.Clear();
            textureStart = 0;
            closeStack.Clear();
            HasMasks = false;
            segmentMaterial = null;
            segmentStart = 0;
            stencilDepth = 0;
            for (var i = 0; i < Elements.Count; i++)
            {
                // Subtrees that ended.
                while (closeStack.Count > 0 && i >= subtreeEnd[closeStack[^1].Index]) Close();

                var element = Elements[i];
                if (!element.IsRegisteredTo(this)) continue;

                var mask = element.Mask;
                var custom = element.CustomDraw;
                if (mask == null || mask.ShowMaskGraphic)
                {
                    var material = element.Material;
                    if (material != segmentMaterial)
                    {
                        FlushDraw();
                        segmentMaterial = material;
                    }

                    var before = order.Length;
                    element.AppendDrawOrder(order);
                    SplitByTextures(before);
                    if (custom != null && custom.Position == YauiCustomDrawPosition.AfterSelf) AddCustom(custom);
                }

                if (mask != null && stencilDepth < 255)
                {
                    FlushDraw();
                    closeStack.Add((i, true));
                    stencilDepth++;
                    AddShape(element, SegmentKind.MaskPush);
                    HasMasks = true;
                }

                // Above the mask's removal: drawn inside the mask.
                if (custom != null && custom.Position == YauiCustomDrawPosition.AfterChildren)
                    closeStack.Add((i, false));
            }

            while (closeStack.Count > 0) Close();

            FlushDraw();
            while (SegmentProperties.Count < Segments.Count) SegmentProperties.Add(new MaterialPropertyBlock());

            UploadOrder();
        }

        private void Close()
        {
            var (index, isMask) = closeStack[^1];
            closeStack.RemoveAt(closeStack.Count - 1);
            var element = Elements[index];
            if (!isMask)
            {
                AddCustom(element.CustomDraw);
                return;
            }

            FlushDraw();
            AddShape(element, SegmentKind.MaskPop);
            stencilDepth--;
        }

        private void AddCustom(YauiCustomDraw draw)
        {
            FlushDraw();
            Segments.Add(new DrawSegment
            {
                Start = order.Length, Kind = SegmentKind.Custom, CustomDraw = draw, StencilDepth = stencilDepth,
                TextureStart = textureStart
            });
        }

        private void AddShape(YauiElement element, SegmentKind kind)
        {
            var start = order.Length;
            element.AppendMaskShape(order);
            for (var i = start; i < order.Length; i++) AddTexture(TextureOf(i));

            Segments.Add(new DrawSegment
            {
                Start = start, Count = order.Length - start, Kind = kind, StencilDepth = stencilDepth,
                TextureStart = textureStart, TextureCount = SegmentTextures.Count - textureStart
            });
            segmentStart = order.Length;
            textureStart = SegmentTextures.Count;
        }

        private void FlushDraw()
        {
            FlushDraw(order.Length);
        }

        /// <summary>Ends the current draw segment before the draw position <paramref name="end"/>.</summary>
        private void FlushDraw(int end)
        {
            if (end > segmentStart)
                Segments.Add(new DrawSegment
                {
                    Start = segmentStart, Count = end - segmentStart, Material = segmentMaterial,
                    Kind = SegmentKind.Draw, StencilDepth = stencilDepth,
                    TextureStart = textureStart, TextureCount = SegmentTextures.Count - textureStart
                });

            segmentStart = end;
            textureStart = SegmentTextures.Count;
        }

        private int textureStart;

        private int TextureOf(int drawPosition)
        {
            return PrimitiveTexture.IdOf(YauiSystem.Primitives.Read((int)order[drawPosition]).Flags);
        }

        private bool AddTexture(int id)
        {
            if (id == 0) return true;

            for (var i = textureStart; i < SegmentTextures.Count; i++)
                if (SegmentTextures[i] == id)
                    return true;

            if (SegmentTextures.Count - textureStart >= TextureRegistry.SlotCount) return false;

            SegmentTextures.Add(id);
            return true;
        }

        /// <summary>Splits the draw where the primitives appended from <paramref name="from"/> need a ninth texture.</summary>
        private void SplitByTextures(int from)
        {
            for (var i = from; i < order.Length; i++)
            {
                var id = TextureOf(i);
                if (!AddTexture(id))
                {
                    FlushDraw(i);
                    AddTexture(id);
                }
            }
        }

        private void Visit(YauiElement element, int parent)
        {
            var index = Elements.Count;
            element.DfsIndex = index;
            Elements.Add(element);
            nodes.Add(element.NodeSlot);
            parents.Add(parent);
            subtreeEnd.Add(index + 1);

            // Children are collected before recursing, since the buffer is shared. Only elements whose children
            // changed read the Transform hierarchy; the others reuse their cached children.
            var start = childBuffer.Count;
            if (element.ChildrenDirty) element.RefreshChildren();

            foreach (var child in element.CachedChildren)
                if (child != null && child.IsRegisteredTo(this))
                    childBuffer.Add(child);

            var end = childBuffer.Count;
            if (!element.AcceptsChildren)
            {
                childBuffer.RemoveRange(start, end - start);
                return;
            }

            SyncYogaChildren(element.Yoga, start, end);
            for (var i = start; i < end; i++) Visit(childBuffer[i], index);

            subtreeEnd[index] = Elements.Count;
            childBuffer.RemoveRange(start, end - start);
        }

        /// <summary>Makes the Yoga children match, touching the tree only if they differ (to keep its caches).</summary>
        private void SyncYogaChildren(YogaNode yoga, int start, int end)
        {
            var count = end - start;
            var same = yoga.ChildCount == count;
            for (var i = 0; same && i < count; i++) same = yoga.GetChild(i) == childBuffer[start + i].Yoga;

            if (same) return;

            yoga.ClearChildren();
            for (var i = 0; i < count; i++)
            {
                var child = childBuffer[start + i].Yoga;
                if (!child.Owner.IsNull) child.Owner.RemoveChild(child);
                yoga.InsertChild(child, i);
            }
        }

        /// <summary>Main thread, at submission: applies changed styles to Yoga. Returns true if a layout is needed.</summary>
        public bool PrepareLayout()
        {
            foreach (var element in LayoutDirtyElements)
            {
                element.LayoutDirty = false;
                if (element.IsRegisteredTo(this)) element.PrepareLayout();
            }

            LayoutDirtyElements.Clear();

            var root = Root;
            if (root == null || !root.IsRegisteredTo(this)) return false;

            var yoga = root.Yoga;
            if (layoutRoot != yoga)
            {
                if (!layoutRoot.IsNull) layoutRoot.SetBoundaryDirtied(null);

                layoutRoot = yoga;
                layoutRoot.SetBoundaryDirtied(onBoundaryDirtied ??= dirtyBoundaries.Add);
            }

            // Yoga setters mark the node dirty even if the value is the same.
            var resized = math.any(laidOutSize != CanvasSize);
            if (resized)
            {
                yoga.Width = YogaValue.Point(CanvasSize.x);
                yoga.Height = YogaValue.Point(CanvasSize.y);
                laidOutSize = CanvasSize;
            }

            return yoga.IsDirty || dirtyBoundaries.Count > 0 || pendingMeasures.Count > 0;
        }

        /// <summary>Main thread, at submission: a text changed; its measurement is resolved in the layout job.</summary>
        public void AddPendingMeasure(YauiText text)
        {
            pendingMeasures.Add(text);
        }


        /// <summary>Main thread: makes the next preparation set the size of the root again.</summary>
        public void ResetRootSize()
        {
            laidOutSize = -1f;
        }

        /// <summary>Any thread: runs the layout. Touches only Yoga nodes, never Unity objects.</summary>
        /// <summary>
        /// Layout thread (managed): resolves the changed texts (dirtying Yoga only if their size changed) and adds
        /// the layout boundaries whose content changed, for the Burst layout jobs.
        /// </summary>
        public void ResolveMeasures(NativeList<IntPtr> boundaries)
        {
            foreach (var text in pendingMeasures) text.ResolveMeasure();

            foreach (var boundary in dirtyBoundaries) boundaries.Add(boundary.Pointer);
        }

        /// <summary>Main thread, at submission: the tree for the layout jobs.</summary>
        public RootLayout RootLayout => new()
        {
            Root = layoutRoot.Pointer, Width = CanvasSize.x, Height = CanvasSize.y
        };

        /// <summary>Main thread, at collection: copies the new layout into the nodes and the boxes.</summary>
        public void ApplyLayout()
        {
            LayoutScheduled = false;
            dirtyBoundaries.Clear();
            pendingMeasures.Clear();
            var store = YauiSystem.Nodes;
            foreach (var element in Elements)
            {
                var yoga = element.Yoga;
                if (!yoga.HasNewLayout || !element.IsRegisteredTo(this)) continue;

                yoga.HasNewLayout = false;
                ref var node = ref store[element.NodeSlot];
                node.LayoutPosition = new float2(yoga.LayoutX, yoga.LayoutY);
                var size = new float2(yoga.LayoutWidth, yoga.LayoutHeight);
                if (math.any(size != node.LayoutSize))
                {
                    node.LayoutSize = size;
                    element.WriteBoxRect();
                }

                MarkTransformDirty(element);
                element.OnLayoutApplied();
            }
        }

        /// <summary>Main thread, at collection: propagates transforms if anything changed.</summary>
        public void UpdateTransforms()
        {
            if (!TransformDirty || nodes.Length == 0) return;

            TransformDirty = false;
            var count = nodes.Length;

            // Whole subtrees of the changed elements, merged; nested ones are covered by their ancestors.
            transformRanges.Clear();
            if (allTransformsDirty || world.Length != count)
            {
                transformRanges.Add(new int2(0, count));
            }
            else
            {
                dirtyNodes.Sort();
                var coveredEnd = -1;
                foreach (var index in dirtyNodes)
                    if (index >= coveredEnd)
                    {
                        coveredEnd = subtreeEnd[index];
                        transformRanges.Add(new int2(index, coveredEnd));
                    }
            }

            allTransformsDirty = false;
            dirtyNodes.Clear();
            world.ResizeUninitialized(count);
            opacity.ResizeUninitialized(count);
            clipSlot.ResizeUninitialized(count);
            clipRect.ResizeUninitialized(count);

            var nodeStore = YauiSystem.Nodes;
            var clipStore = YauiSystem.Clips;
            new TransformPass
            {
                Nodes = nodes.AsArray(),
                Parents = parents.AsArray(),
                Ranges = transformRanges.AsArray(),
                Cpu = nodeStore.Cpu,
                Gpu = nodeStore.Gpu.AsArray(),
                GpuDirty = nodeStore.Gpu.DirtyChunks,
                Clips = clipStore.AsArray(),
                ClipsDirty = clipStore.DirtyChunks,
                World = world.AsArray(),
                Opacity = opacity.AsArray(),
                ClipSlot = clipSlot.AsArray(),
                ClipRect = clipRect.AsArray()
            }.Run();

            // Built when queried: without a pointer, moving elements costs nothing for hit testing.
            hitTestDirty = true;

            if (!scanResult.IsCreated) scanResult = new NativeArray<int>(1, Allocator.Persistent);

            new PixelClipScan
            {
                Nodes = nodes.AsArray(),
                Gpu = nodeStore.Gpu.AsArray(),
                Clips = clipStore.AsArray(),
                Result = scanResult
            }.Run();
            NeedsPixelClip = scanResult[0] != 0;
        }

        private void BuildHitTest()
        {
            hitTestDirty = false;
            if (world.Length != nodes.Length) return;

            hitTest.Build(nodes.AsArray(), parents.AsArray(), YauiSystem.Nodes.Cpu, world.AsArray(),
                clipRect.AsArray(), CanvasSize);
            hitElements.Clear();
            hitElements.AddRange(Elements);
        }

        /// <summary>
        /// Main thread: the topmost hittable element at a screen position (pixels, origin at the bottom-left), as
        /// last rendered, or null.
        /// </summary>
        public YauiElement HitTest(Vector2 screenPosition)
        {
            var screenHeight = CanvasSize.y * ScaleFactor;
            return HitTestCanvas(new float2(screenPosition.x, screenHeight - screenPosition.y) / ScaleFactor);
        }

        /// <summary>
        /// Main thread: all hittable elements at a point in canvas space, topmost first, with their depth-first
        /// indices (higher is on top).
        /// </summary>
        public void HitTestCanvasAll(float2 point, List<(YauiElement Element, int Depth)> output)
        {
            if (hitTestDirty) BuildHitTest();

            foreach (var index in hitTest.QueryAll(point))
                if (index < hitElements.Count && hitElements[index] != null && hitElements[index].IsRegisteredTo(this))
                    output.Add((hitElements[index], index));
        }

        /// <summary>
        /// Main thread: a point in canvas space in the local space of an element's box, with the transforms last
        /// rendered. False if the element has not been rendered in this panel or its transform is degenerate.
        /// </summary>
        public bool TryCanvasToLocal(YauiElement element, float2 canvas, out float2 local)
        {
            local = default;
            var index = element.DfsIndex;
            if (!element.IsRegisteredTo(this) || index < 0 || index >= world.Length) return false;

            var m = world[index];
            var linear = new float2x2(m.c0, m.c1);
            if (math.abs(math.determinant(linear)) < 1e-12f) return false;

            local = math.mul(math.inverse(linear), canvas - m.c2);
            return true;
        }

        /// <summary>Main thread: the transform of an element's box to canvas space, as last rendered.</summary>
        public bool TryGetWorld(YauiElement element, out float2x3 matrix)
        {
            matrix = default;
            var index = element.DfsIndex;
            if (!element.IsRegisteredTo(this) || index < 0 || index >= world.Length) return false;

            matrix = world[index];
            return true;
        }

        /// <summary>Main thread: the topmost hittable element at a point in canvas space, as last rendered, or null.</summary>
        public YauiElement HitTestCanvas(float2 point)
        {
            if (hitTestDirty) BuildHitTest();

            var index = hitTest.Query(point);
            return index >= 0 && index < hitElements.Count && hitElements[index] != null &&
                   hitElements[index].IsRegisteredTo(this)
                ? hitElements[index]
                : null;
        }

        private void UploadOrder()
        {
            var count = Math.Max(order.Length, 1);
            if (orderBuffer == null || orderBuffer.count < count)
            {
                orderBuffer?.Dispose();
                orderBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Math.Max(count, order.Capacity),
                    sizeof(uint));
            }

            if (order.Length > 0) orderBuffer.SetData(order.AsArray());
        }

        public void Dispose()
        {
            hitTest.Dispose();
            if (scanResult.IsCreated) scanResult.Dispose();

            orderBuffer?.Dispose();
            orderBuffer = null;
            if (nodes.IsCreated)
            {
                nodes.Dispose();
                parents.Dispose();
                subtreeEnd.Dispose();
                dirtyNodes.Dispose();
                transformRanges.Dispose();
                order.Dispose();
                world.Dispose();
                opacity.Dispose();
                clipSlot.Dispose();
                clipRect.Dispose();
            }
        }
    }
}