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

        /// <summary>The features of the uber shader the primitives use (<see cref="PanelState.UpdateFeatures"/>).</summary>
        public ShaderFeatures Features;
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

        private NativeList<int> _nodes;
        private NativeList<int> _parents;

        // End (exclusive) of the subtree of each depth-first index.
        private NativeList<int> _subtreeEnd;

        // Depth-first indices whose subtrees need their transforms propagated again, unless all do.
        private NativeList<int> _dirtyNodes;
        private NativeList<int2> _transformRanges;
        private bool _allTransformsDirty = true;
        private bool _hitTestDirty;
        private NativeList<uint> _order;

        // The painter's order and its segments, kept when reordering can merge draws (Reorder).
        private NativeList<uint> _naiveOrder;
        private readonly List<DrawSegment> _naiveSegments = new();
        private readonly List<int> _naiveTextures = new();
        private readonly List<Material> _reorderMaterials = new();
        private NativeList<int3> _reorderSegments;
        private NativeList<int4> _reorderDraws;
        private NativeList<int> _reorderTextures;
        private bool _reorderable;

        /// <summary>The bounds of a primitive may have changed since the draw order was last reordered.</summary>
        public bool ReorderDirty;
        private GraphicsBuffer _orderBuffer;

        // Scratch of the transform pass, per depth-first index.
        private NativeList<float2x3> _world;
        private NativeList<float> _opacity;
        private NativeList<int> _clipSlot;
        private NativeList<float4> _clipRect;

        private readonly List<YauiElement> _childBuffer = new();

        // The elements of the last hit test grid, by depth-first index.
        private readonly HitTestGrid _hitTest = new();
        private readonly List<YauiElement> _hitElements = new();
        private readonly List<YogaNode> _yogaChildBuffer = new();

        public bool StructureDirty = true;

        /// <summary>A node is rotated or under a rounded clip: draw with per-pixel clipping.</summary>
        public bool NeedsPixelClip;

        private NativeArray<int> _scanResult;

        // Scratch of the feature scan, per segment.
        private NativeList<int2> _featureRanges;
        private NativeList<ShaderFeatures> _features;

        /// <summary>
        /// The segments, or the shader features of a primitive (<see cref="ShaderFeaturesExtensions.Of"/>), changed
        /// since the features of the segments were last scanned.
        /// </summary>
        public bool FeaturesDirty = true;

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
        private float2 _laidOutSize = -1f;
        private YogaNode _layoutRoot;

        // Layout boundaries dirtied by their content since the last layout. Written at submission only.
        private readonly List<YogaNode> _dirtyBoundaries = new();
        private Action<YogaNode> _onBoundaryDirtied;

        // Texts whose content changed; whether their size did is resolved in the layout job, after generation.
        private readonly List<YauiText> _pendingMeasures = new();

        private static readonly Unity.Profiling.ProfilerMarker ReorderJobMarker = new("Yaui.Collect.Reorder.Job");

        public PanelState(YauiPanel panel)
        {
            Panel = panel;
            _nodes = new NativeList<int>(64, Allocator.Persistent);
            _parents = new NativeList<int>(64, Allocator.Persistent);
            _subtreeEnd = new NativeList<int>(64, Allocator.Persistent);
            _dirtyNodes = new NativeList<int>(64, Allocator.Persistent);
            _transformRanges = new NativeList<int2>(16, Allocator.Persistent);
            _order = new NativeList<uint>(64, Allocator.Persistent);
            _naiveOrder = new NativeList<uint>(64, Allocator.Persistent);
            _reorderSegments = new NativeList<int3>(16, Allocator.Persistent);
            _reorderDraws = new NativeList<int4>(16, Allocator.Persistent);
            _reorderTextures = new NativeList<int>(16, Allocator.Persistent);
            _world = new NativeList<float2x3>(64, Allocator.Persistent);
            _opacity = new NativeList<float>(64, Allocator.Persistent);
            _clipSlot = new NativeList<int>(64, Allocator.Persistent);
            _clipRect = new NativeList<float4>(64, Allocator.Persistent);
        }

        public YauiElement Root => Panel != null ? Panel.Element : null;

        public GraphicsBuffer OrderBuffer => _orderBuffer;

        public int DrawCount => _order.Length;

        /// <summary>Primitive slots in the order they are drawn, and in the painter's order (tests).</summary>
        internal NativeArray<uint> DrawOrder => _order.AsArray();

        internal NativeArray<uint> PainterOrder => _reorderable ? _naiveOrder.AsArray() : _order.AsArray();

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
            _allTransformsDirty = true;
            YauiSystem.RequestUpdate();
        }

        /// <summary>The transform, opacity or clip of an element changed: its subtree needs to be propagated.</summary>
        public void MarkTransformDirty(YauiElement element)
        {
            TransformDirty = true;
            if (!StructureDirty && element.DfsIndex >= 0 && element.DfsIndex < _nodes.Length &&
                Elements[element.DfsIndex] == element)
                _dirtyNodes.Add(element.DfsIndex);
            else
                _allTransformsDirty = true;

            YauiSystem.RequestUpdate();
        }

        /// <summary>Whether an element receives hits changed.</summary>
        public void MarkHitTestDirty()
        {
            _hitTestDirty = true;
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
            if (_hitTestDirty) BuildHitTest();

            StructureDirty = false;
            TransformDirty = true;
            _allTransformsDirty = true;
            foreach (var element in Elements) element.DfsIndex = -1;

            Elements.Clear();
            _nodes.Clear();
            _parents.Clear();
            _subtreeEnd.Clear();

            var root = Root;
            if (root != null && root.IsRegisteredTo(this)) Visit(root, -1);

            RebuildOrder();
        }

        /// <summary>Whether the draw order contains masks (the overlay then needs a stencil buffer).</summary>
        public bool HasMasks { get; private set; }

        // Draws at the end of a subtree, innermost last: a mask's removal, or a custom draw after the children.
        private readonly List<(int Index, bool Mask)> _closeStack = new();
        private Material _segmentMaterial;
        private int _segmentStart;
        private int _stencilDepth;

        /// <summary>
        /// Main thread: rebuilds the draw order buffer from the elements, split into draw calls where the material
        /// changes and around masks: a mask's shape is drawn into the stencil before its descendants and removed
        /// after them.
        /// </summary>
        public void RebuildOrder()
        {
            OrderDirty = false;
            FeaturesDirty = true;
            _order.Clear();
            Segments.Clear();
            SegmentTextures.Clear();
            _textureStart = 0;
            _closeStack.Clear();
            HasMasks = false;
            _segmentMaterial = null;
            _segmentStart = 0;
            _stencilDepth = 0;
            for (var i = 0; i < Elements.Count; i++)
            {
                // Subtrees that ended.
                while (_closeStack.Count > 0 && i >= _subtreeEnd[_closeStack[^1].Index]) Close();

                var element = Elements[i];
                if (!element.IsRegisteredTo(this)) continue;

                var mask = element.Mask;
                var custom = element.CustomDraw;
                if (mask == null || mask.ShowMaskGraphic)
                {
                    var material = element.Material;
                    if (material != _segmentMaterial)
                    {
                        FlushDraw();
                        _segmentMaterial = material;
                    }

                    var before = _order.Length;
                    element.AppendDrawOrder(_order);
                    SplitByTextures(before);
                    if (custom != null && custom.Position == YauiCustomDrawPosition.AfterSelf) AddCustom(custom);
                }

                if (mask != null && _stencilDepth < 255)
                {
                    FlushDraw();
                    _closeStack.Add((i, true));
                    _stencilDepth++;
                    AddShape(element, SegmentKind.MaskPush);
                    HasMasks = true;
                }

                // Above the mask's removal: drawn inside the mask.
                if (custom != null && custom.Position == YauiCustomDrawPosition.AfterChildren)
                    _closeStack.Add((i, false));
            }

            while (_closeStack.Count > 0) Close();

            FlushDraw();
            while (SegmentProperties.Count < Segments.Count) SegmentProperties.Add(new MaterialPropertyBlock());

            PlanRegions();
            if (_reorderable)
            {
                _naiveOrder.CopyFrom(_order);
                _naiveSegments.Clear();
                _naiveSegments.AddRange(Segments);
                _naiveTextures.Clear();
                _naiveTextures.AddRange(SegmentTextures);
                ReorderDirty = true;
            }

            UploadOrder();
        }

        /// <summary>
        /// The end (exclusive) of the region of segments from <paramref name="start"/>: the plain draws that follow at
        /// the same stencil depth, whose primitives may be reordered among themselves. Other segments are regions of
        /// their own.
        /// </summary>
        private static int RegionEnd(List<DrawSegment> segments, int start)
        {
            var first = segments[start];
            var end = start + 1;
            if (first.Kind != SegmentKind.Draw) return end;

            while (end < segments.Count && segments[end].Kind == SegmentKind.Draw &&
                   segments[end].StencilDepth == first.StencilDepth)
                end++;

            return end;
        }

        /// <summary>Whether a region has more draws than materials: reordering may merge some of them.</summary>
        private bool CanMerge(List<DrawSegment> segments, int start, int end)
        {
            if (end - start < 3) return false;

            _reorderMaterials.Clear();
            for (var i = start; i < end; i++)
                if (!_reorderMaterials.Contains(segments[i].Material))
                    _reorderMaterials.Add(segments[i].Material);

            return end - start > _reorderMaterials.Count;
        }

        /// <summary>
        /// Main thread, at collection, after the transforms: reorders the painter's order so that draws of the same
        /// material merge where no primitives overlap (<see cref="DrawReorder"/>), if the order, the transforms or the
        /// primitives changed. Only panels where some draws could merge are reordered.
        /// </summary>
        public void Reorder(bool transformsChanged)
        {
            if (!_reorderable || !(ReorderDirty || transformsChanged)) return;

            ReorderDirty = false;
            FeaturesDirty = true;
            _order.CopyFrom(_naiveOrder);
            Segments.Clear();
            SegmentTextures.Clear();
            foreach (var region in _regions)
                if (!region.Mergeable || !ReorderRegion(region))
                    for (var i = region.Start; i < region.End; i++)
                        CopySegment(_naiveSegments[i]);

            while (SegmentProperties.Count < Segments.Count) SegmentProperties.Add(new MaterialPropertyBlock());

            UploadOrder();
        }

        /// <summary>A run of the naive segments reordered as a whole (<see cref="RegionEnd"/>).</summary>
        private struct Region
        {
            public int Start;
            public int End;

            /// <summary>Some of its draws could merge.</summary>
            public bool Mergeable;

            /// <summary>Mergeable: its segments in _reorderSegments and its keys in _regionMaterials.</summary>
            public int SegmentsStart;

            public int MaterialsStart;
            public int MaterialCount;
        }

        private readonly List<Region> _regions = new();

        // The materials of the keys of the mergeable regions.
        private readonly List<Material> _regionMaterials = new();

        /// <summary>Splits the segments into regions and prepares the mergeable ones for <see cref="Reorder"/>.</summary>
        private void PlanRegions()
        {
            _regions.Clear();
            _regionMaterials.Clear();
            _reorderSegments.Clear();
            _reorderable = false;
            for (var s = 0; s < Segments.Count;)
            {
                var e = RegionEnd(Segments, s);
                var region = new Region { Start = s, End = e, Mergeable = CanMerge(Segments, s, e) };
                if (region.Mergeable)
                {
                    _reorderable = true;
                    region.SegmentsStart = _reorderSegments.Length;
                    region.MaterialsStart = _regionMaterials.Count;
                    region.MaterialCount = _reorderMaterials.Count;
                    _regionMaterials.AddRange(_reorderMaterials);

                    // Keys by material, numbered by first appearance.
                    var from = Segments[s].Start;
                    for (var i = s; i < e; i++)
                    {
                        var segment = Segments[i];
                        _reorderSegments.Add(new int3(segment.Start - from, segment.Count,
                            _reorderMaterials.IndexOf(segment.Material)));
                    }
                }

                _regions.Add(region);
                s = e;
            }
        }

        private void CopySegment(DrawSegment segment)
        {
            var textures = segment.TextureStart;
            segment.TextureStart = SegmentTextures.Count;
            for (var i = 0; i < segment.TextureCount; i++) SegmentTextures.Add(_naiveTextures[textures + i]);

            Segments.Add(segment);
        }

        /// <summary>
        /// Reorders a mergeable region of the naive segments and appends its draws. Returns false, leaving nothing
        /// appended and the order as it was, if that would not take fewer draws.
        /// </summary>
        private bool ReorderRegion(Region region)
        {
            var first = _naiveSegments[region.Start];
            var last = _naiveSegments[region.End - 1];
            var from = first.Start;
            var count = last.Start + last.Count - from;
            var segmentCount = region.End - region.Start;

            _reorderDraws.Clear();
            _reorderTextures.Clear();
            using var _ = ReorderJobMarker.Auto();
            new DrawReorder
            {
                Order = _naiveOrder.AsArray().GetSubArray(from, count),
                Segments = _reorderSegments.AsArray().GetSubArray(region.SegmentsStart, segmentCount),
                KeyCount = region.MaterialCount,
                CanvasSize = CanvasSize,
                Primitives = YauiSystem.Primitives.AsArray(),
                Exts = YauiSystem.Exts.AsArray(),
                Nodes = YauiSystem.Nodes.Gpu.AsArray(),
                Clips = YauiSystem.Clips.AsArray(),
                Reordered = _order.AsArray().GetSubArray(from, count),
                Draws = _reorderDraws,
                Textures = _reorderTextures
            }.Run();

            if (_reorderDraws.Length >= segmentCount)
            {
                NativeArray<uint>.Copy(_naiveOrder.AsArray(), from, _order.AsArray(), from, count);
                return false;
            }

            var texture = 0;
            foreach (var draw in _reorderDraws)
            {
                Segments.Add(new DrawSegment
                {
                    Start = from + draw.x, Count = draw.y, Material = _regionMaterials[region.MaterialsStart + draw.z],
                    Kind = SegmentKind.Draw, StencilDepth = first.StencilDepth,
                    TextureStart = SegmentTextures.Count, TextureCount = draw.w
                });
                for (var i = 0; i < draw.w; i++) SegmentTextures.Add(_reorderTextures[texture++]);
            }

            return true;
        }

        private void Close()
        {
            var (index, isMask) = _closeStack[^1];
            _closeStack.RemoveAt(_closeStack.Count - 1);
            var element = Elements[index];
            if (!isMask)
            {
                AddCustom(element.CustomDraw);
                return;
            }

            FlushDraw();
            AddShape(element, SegmentKind.MaskPop);
            _stencilDepth--;
        }

        private void AddCustom(YauiCustomDraw draw)
        {
            FlushDraw();
            Segments.Add(new DrawSegment
            {
                Start = _order.Length, Kind = SegmentKind.Custom, CustomDraw = draw, StencilDepth = _stencilDepth,
                TextureStart = _textureStart
            });
        }

        private void AddShape(YauiElement element, SegmentKind kind)
        {
            var start = _order.Length;
            element.AppendMaskShape(_order);
            for (var i = start; i < _order.Length; i++) AddTexture(TextureOf(i));

            Segments.Add(new DrawSegment
            {
                Start = start, Count = _order.Length - start, Kind = kind, StencilDepth = _stencilDepth,
                TextureStart = _textureStart, TextureCount = SegmentTextures.Count - _textureStart
            });
            _segmentStart = _order.Length;
            _textureStart = SegmentTextures.Count;
        }

        private void FlushDraw()
        {
            FlushDraw(_order.Length);
        }

        /// <summary>Ends the current draw segment before the draw position <paramref name="end"/>.</summary>
        private void FlushDraw(int end)
        {
            if (end > _segmentStart)
                Segments.Add(new DrawSegment
                {
                    Start = _segmentStart, Count = end - _segmentStart, Material = _segmentMaterial,
                    Kind = SegmentKind.Draw, StencilDepth = _stencilDepth,
                    TextureStart = _textureStart, TextureCount = SegmentTextures.Count - _textureStart
                });

            _segmentStart = end;
            _textureStart = SegmentTextures.Count;
        }

        private int _textureStart;

        private int TextureOf(int drawPosition)
        {
            return PrimitiveTexture.IdOf(YauiSystem.Primitives.Read((int)_order[drawPosition]).Flags);
        }

        private bool AddTexture(int id)
        {
            if (id == 0) return true;

            for (var i = _textureStart; i < SegmentTextures.Count; i++)
                if (SegmentTextures[i] == id)
                    return true;

            if (SegmentTextures.Count - _textureStart >= TextureRegistry.SlotCount) return false;

            SegmentTextures.Add(id);
            return true;
        }

        /// <summary>Splits the draw where the primitives appended from <paramref name="from"/> need a ninth texture.</summary>
        private void SplitByTextures(int from)
        {
            for (var i = from; i < _order.Length; i++)
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
            _nodes.Add(element.NodeSlot);
            _parents.Add(parent);
            _subtreeEnd.Add(index + 1);

            // Children are collected before recursing, since the buffer is shared. Only elements whose children
            // changed read the Transform hierarchy; the others reuse their cached children.
            var start = _childBuffer.Count;
            if (element.ChildrenDirty) element.RefreshChildren();

            foreach (var child in element.CachedChildren)
                if (child != null && child.IsRegisteredTo(this))
                    _childBuffer.Add(child);

            var end = _childBuffer.Count;
            if (!element.LaysOutChildren)
            {
                _childBuffer.RemoveRange(start, end - start);
                return;
            }

            SyncYogaChildren(element.Yoga, start, end);
            for (var i = start; i < end; i++) Visit(_childBuffer[i], index);

            _subtreeEnd[index] = Elements.Count;
            _childBuffer.RemoveRange(start, end - start);
        }

        /// <summary>Makes the Yoga children match, touching the tree only if they differ (to keep its caches).</summary>
        private void SyncYogaChildren(YogaNode yoga, int start, int end)
        {
            var count = end - start;
            var same = yoga.ChildCount == count;
            for (var i = 0; same && i < count; i++) same = yoga.GetChild(i) == _childBuffer[start + i].Yoga;

            if (same) return;

            yoga.ClearChildren();
            for (var i = 0; i < count; i++)
            {
                var child = _childBuffer[start + i].Yoga;
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
            if (_layoutRoot != yoga)
            {
                if (!_layoutRoot.IsNull) _layoutRoot.SetBoundaryDirtied(null);

                _layoutRoot = yoga;
                _layoutRoot.SetBoundaryDirtied(_onBoundaryDirtied ??= _dirtyBoundaries.Add);
            }

            // Yoga setters mark the node dirty even if the value is the same.
            var resized = math.any(_laidOutSize != CanvasSize);
            if (resized)
            {
                yoga.Width = YogaValue.Point(CanvasSize.x);
                yoga.Height = YogaValue.Point(CanvasSize.y);
                _laidOutSize = CanvasSize;
            }

            return yoga.IsDirty || _dirtyBoundaries.Count > 0 || _pendingMeasures.Count > 0;
        }

        /// <summary>Main thread, at submission: a text changed; its measurement is resolved in the layout job.</summary>
        public void AddPendingMeasure(YauiText text)
        {
            _pendingMeasures.Add(text);
        }


        /// <summary>Main thread: makes the next preparation set the size of the root again.</summary>
        public void ResetRootSize()
        {
            _laidOutSize = -1f;
        }

        /// <summary>Any thread: runs the layout. Touches only Yoga nodes, never Unity objects.</summary>
        /// <summary>
        /// Layout thread (managed): resolves the changed texts (dirtying Yoga only if their size changed) and adds
        /// the layout boundaries whose content changed, for the Burst layout jobs.
        /// </summary>
        public void ResolveMeasures(NativeList<IntPtr> boundaries)
        {
            foreach (var text in _pendingMeasures) text.ResolveMeasure();

            foreach (var boundary in _dirtyBoundaries) boundaries.Add(boundary.Pointer);
        }

        /// <summary>Main thread, at submission: the tree for the layout jobs.</summary>
        public RootLayout RootLayout => new()
        {
            Root = _layoutRoot.Pointer, Width = CanvasSize.x, Height = CanvasSize.y
        };

        /// <summary>Main thread, at collection: copies the new layout into the nodes and the boxes.</summary>
        public void ApplyLayout()
        {
            LayoutScheduled = false;
            _dirtyBoundaries.Clear();
            _pendingMeasures.Clear();
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
                element.NotifyLayoutApplied();
            }
        }

        /// <summary>Main thread, at collection: the shader features of each draw segment, if they may have changed.</summary>
        public void UpdateFeatures()
        {
            if (!FeaturesDirty) return;

            FeaturesDirty = false;
            if (!_featureRanges.IsCreated)
            {
                _featureRanges = new NativeList<int2>(8, Allocator.Persistent);
                _features = new NativeList<ShaderFeatures>(8, Allocator.Persistent);
            }

            _featureRanges.Clear();
            foreach (var segment in Segments) _featureRanges.Add(new int2(segment.Start, segment.Count));

            _features.ResizeUninitialized(Segments.Count);
            new FeatureScan
            {
                Order = _order.AsArray(),
                Primitives = YauiSystem.Primitives.AsArray(),
                Segments = _featureRanges.AsArray(),
                Result = _features.AsArray()
            }.Run();

            for (var i = 0; i < Segments.Count; i++)
            {
                var segment = Segments[i];
                segment.Features = _features[i];
                Segments[i] = segment;
            }
        }

        /// <summary>Main thread, at collection: propagates transforms if anything changed. Returns whether it did.</summary>
        public bool UpdateTransforms()
        {
            if (!TransformDirty || _nodes.Length == 0) return false;

            TransformDirty = false;
            var count = _nodes.Length;

            // Whole subtrees of the changed elements, merged; nested ones are covered by their ancestors.
            _transformRanges.Clear();
            if (_allTransformsDirty || _world.Length != count)
            {
                _transformRanges.Add(new int2(0, count));
            }
            else
            {
                _dirtyNodes.Sort();
                var coveredEnd = -1;
                foreach (var index in _dirtyNodes)
                    if (index >= coveredEnd)
                    {
                        coveredEnd = _subtreeEnd[index];
                        _transformRanges.Add(new int2(index, coveredEnd));
                    }
            }

            _allTransformsDirty = false;
            _dirtyNodes.Clear();
            _world.ResizeUninitialized(count);
            _opacity.ResizeUninitialized(count);
            _clipSlot.ResizeUninitialized(count);
            _clipRect.ResizeUninitialized(count);

            var nodeStore = YauiSystem.Nodes;
            var clipStore = YauiSystem.Clips;
            new TransformPass
            {
                Nodes = _nodes.AsArray(),
                Parents = _parents.AsArray(),
                Ranges = _transformRanges.AsArray(),
                Cpu = nodeStore.Cpu,
                Gpu = nodeStore.Gpu.AsArray(),
                GpuDirty = nodeStore.Gpu.DirtyChunks,
                Clips = clipStore.AsArray(),
                ClipsDirty = clipStore.DirtyChunks,
                World = _world.AsArray(),
                Opacity = _opacity.AsArray(),
                ClipSlot = _clipSlot.AsArray(),
                ClipRect = _clipRect.AsArray()
            }.Run();

            // Built when queried: without a pointer, moving elements costs nothing for hit testing.
            _hitTestDirty = true;

            if (!_scanResult.IsCreated) _scanResult = new NativeArray<int>(1, Allocator.Persistent);

            new PixelClipScan
            {
                Nodes = _nodes.AsArray(),
                Gpu = nodeStore.Gpu.AsArray(),
                Clips = clipStore.AsArray(),
                Result = _scanResult
            }.Run();
            NeedsPixelClip = _scanResult[0] != 0;
            return true;
        }

        private void BuildHitTest()
        {
            _hitTestDirty = false;
            if (_world.Length != _nodes.Length) return;

            _hitTest.Build(_nodes.AsArray(), _parents.AsArray(), YauiSystem.Nodes.Cpu, _world.AsArray(),
                _clipRect.AsArray(), CanvasSize);
            _hitElements.Clear();
            _hitElements.AddRange(Elements);
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
            if (_hitTestDirty) BuildHitTest();

            foreach (var index in _hitTest.QueryAll(point))
                if (index < _hitElements.Count && _hitElements[index] != null && _hitElements[index].IsRegisteredTo(this))
                    output.Add((_hitElements[index], index));
        }

        /// <summary>
        /// Main thread: a point in canvas space in the local space of an element's box, with the transforms last
        /// rendered. False if the element has not been rendered in this panel or its transform is degenerate.
        /// </summary>
        public bool TryCanvasToLocal(YauiElement element, float2 canvas, out float2 local)
        {
            local = default;
            var index = element.DfsIndex;
            if (!element.IsRegisteredTo(this) || index < 0 || index >= _world.Length) return false;

            var m = _world[index];
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
            if (!element.IsRegisteredTo(this) || index < 0 || index >= _world.Length) return false;

            matrix = _world[index];
            return true;
        }

        /// <summary>Main thread: the topmost hittable element at a point in canvas space, as last rendered, or null.</summary>
        public YauiElement HitTestCanvas(float2 point)
        {
            if (_hitTestDirty) BuildHitTest();

            var index = _hitTest.Query(point);
            return index >= 0 && index < _hitElements.Count && _hitElements[index] != null &&
                   _hitElements[index].IsRegisteredTo(this)
                ? _hitElements[index]
                : null;
        }

        private void UploadOrder()
        {
            var count = Math.Max(_order.Length, 1);
            if (_orderBuffer == null || _orderBuffer.count < count)
            {
                _orderBuffer?.Dispose();
                _orderBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Math.Max(count, _order.Capacity),
                    sizeof(uint));
            }

            if (_order.Length > 0) _orderBuffer.SetData(_order.AsArray());
        }

        public void Dispose()
        {
            _hitTest.Dispose();
            if (_scanResult.IsCreated) _scanResult.Dispose();
            if (_featureRanges.IsCreated)
            {
                _featureRanges.Dispose();
                _features.Dispose();
            }

            _orderBuffer?.Dispose();
            _orderBuffer = null;
            if (_nodes.IsCreated)
            {
                _nodes.Dispose();
                _parents.Dispose();
                _subtreeEnd.Dispose();
                _dirtyNodes.Dispose();
                _transformRanges.Dispose();
                _order.Dispose();
                _naiveOrder.Dispose();
                _reorderSegments.Dispose();
                _reorderDraws.Dispose();
                _reorderTextures.Dispose();
                _world.Dispose();
                _opacity.Dispose();
                _clipSlot.Dispose();
                _clipRect.Dispose();
            }
        }
    }
}