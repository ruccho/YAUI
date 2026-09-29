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
        private NativeList<DrawReorder.Region> _jobRegions;
        private NativeList<int4> _reorderSegments;
        private NativeList<int4> _reorderDraws;
        private NativeList<int> _reorderDrawCounts;
        private NativeList<int> _reorderTextures;
        private bool _reorderable;
        private bool _reorderScheduled;
        private JobHandle _reorderJob;

        // The transforms changed since the order was last reordered.
        private bool _transformsChanged;

        // Some region has draws of the same material to merge (PlanRegions).
        private bool _canMergeMaterials;

        // Some draw of the uber shader uses heavy features, which draws may split by (UpdateFeatures).
        private bool _hasHeavyFeatures;

        // The draws are those of the painter's order.
        private bool _painterShown = true;

        // The draw order changed since it was last uploaded.
        private bool _orderChanged;
        private int _drawCount;

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

        private static readonly Unity.Profiling.ProfilerMarker ReorderWaitMarker = new("Yaui.Reorder.Wait");

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
            _jobRegions = new NativeList<DrawReorder.Region>(4, Allocator.Persistent);
            _reorderSegments = new NativeList<int4>(16, Allocator.Persistent);
            _reorderDraws = new NativeList<int4>(16, Allocator.Persistent);
            _reorderDrawCounts = new NativeList<int>(4, Allocator.Persistent);
            _reorderTextures = new NativeList<int>(16, Allocator.Persistent);
            _world = new NativeList<float2x3>(64, Allocator.Persistent);
            _opacity = new NativeList<float>(64, Allocator.Persistent);
            _clipSlot = new NativeList<int>(64, Allocator.Persistent);
            _clipRect = new NativeList<float4>(64, Allocator.Persistent);
        }

        public YauiElement Root => Panel != null ? Panel.Element : null;

        /// <summary>The draw order, uploaded if it changed (<see cref="FlushOrder"/>).</summary>
        public GraphicsBuffer OrderBuffer => _orderBuffer;

        public int DrawCount => _drawCount;

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
            CompleteReorder();
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

            _drawCount = _order.Length;
            _painterShown = true;
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

            _orderChanged = true;
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
            // The materials of the region, in _reorderMaterials.
            _reorderMaterials.Clear();
            for (var i = start; i < end; i++)
                if (!_reorderMaterials.Contains(segments[i].Material))
                    _reorderMaterials.Add(segments[i].Material);

            return end - start >= 3 && end - start > _reorderMaterials.Count;
        }

        /// <summary>
        /// Main thread, at collection, after the transforms: schedules the reordering of the painter's order so that
        /// draws of the same material merge where no primitives overlap (<see cref="DrawReorder"/>), if the order, the
        /// transforms or the primitives changed. Only panels where some draws could merge are reordered. The draws are
        /// ready after <see cref="CompleteReorder"/>.
        /// </summary>
        public void ScheduleReorder()
        {
            if (!_reorderable || !(ReorderDirty || _transformsChanged)) return;

            // Moving panels without draws to merge or heavy features to split off stay as they are, without a job.
            if (_painterShown && !_canMergeMaterials && !(YauiBatching.SplitByShaderFeatures && _hasHeavyFeatures))
            {
                ReorderDirty = false;
                _transformsChanged = false;
                return;
            }

            var costs = ReorderCosts();
            ReorderDirty = false;
            _transformsChanged = false;
            _order.CopyFrom(_naiveOrder);
            _reorderDraws.Clear();
            _reorderDrawCounts.Clear();
            _reorderTextures.Clear();
            _reorderJob = new DrawReorder
            {
                Regions = _jobRegions.AsArray(),
                PainterOrder = _naiveOrder.AsArray(),
                Segments = _reorderSegments.AsArray(),
                CanvasSize = CanvasSize,
                Cost = costs,
                Primitives = YauiSystem.Primitives.AsArray(),
                Exts = YauiSystem.Exts.AsArray(),
                Nodes = YauiSystem.Nodes.Gpu.AsArray(),
                Clips = YauiSystem.Clips.AsArray(),
                Order = _order.AsArray(),
                Draws = _reorderDraws,
                DrawCounts = _reorderDrawCounts,
                Textures = _reorderTextures
            }.Schedule();
            _reorderScheduled = true;
        }

        private DrawReorder.Costs ReorderCosts()
        {
            var costs = new DrawReorder.Costs
            {
                DrawGpu = YauiBatching.DrawGpuMicroseconds,
                DrawCpu = YauiBatching.DrawCpuMicroseconds,
                GpuWeight = YauiBatching.GpuWeight,
                CpuWeight = YauiBatching.CpuWeight,
                Margin = YauiBatching.SplitMargin,
                SplitByFeatures = YauiBatching.SplitByShaderFeatures,
                Moving = _transformsChanged,
                LayeringPerPrimitive = YauiBatching.LayeringMicroseconds,
                PixelsPerUnitSquared = ScaleFactor * ScaleFactor
            };
            for (var i = 0; i < ShaderFeaturesExtensions.Count; i++)
            {
                var features = (ShaderFeatures)i;
                var cost = YauiBatching.PixelBaseMicroseconds;
                if ((features & ShaderFeatures.Text) != 0) cost += YauiBatching.PixelTextMicroseconds;
                if ((features & ShaderFeatures.Image) != 0) cost += YauiBatching.PixelImageMicroseconds;
                if ((features & ShaderFeatures.Border) != 0) cost += YauiBatching.PixelBorderMicroseconds;
                if ((features & ShaderFeatures.Shadow) != 0) cost += YauiBatching.PixelShadowMicroseconds;
                costs.Pixel.Add(cost);
            }

            return costs;
        }

        /// <summary>
        /// Main thread: waits for the reordering and builds the draws. Before the stores are written again (the job
        /// reads them) and before the draws are read.
        /// </summary>
        public void CompleteReorder()
        {
            if (!_reorderScheduled) return;

            _reorderScheduled = false;
            using (ReorderWaitMarker.Auto()) _reorderJob.Complete();

            // The painter's order again: the order (copied when scheduled), the draws and their features stay.
            var reordered = false;
            foreach (var drawCount in _reorderDrawCounts)
            {
                reordered |= drawCount >= 0;
                if (drawCount == DrawReorder.Deferred) ReorderDirty = true;
            }

            if (!reordered && _painterShown) return;

            _painterShown = !reordered;

            Segments.Clear();
            SegmentTextures.Clear();
            var mergeable = 0;
            var draw = 0;
            var texture = 0;
            foreach (var region in _regions)
            {
                if (!region.Mergeable)
                {
                    for (var i = region.Start; i < region.End; i++) CopySegment(_naiveSegments[i]);
                    continue;
                }

                var drawCount = _reorderDrawCounts[mergeable];
                var job = _jobRegions[mergeable++];

                // The painter's order (as copied when scheduled) costs the least, or does while the panel moves.
                if (drawCount < 0)
                {
                    for (var i = region.Start; i < region.End; i++) CopySegment(_naiveSegments[i]);
                    continue;
                }

                for (var d = draw; d < draw + drawCount; d++)
                {
                    var info = _reorderDraws[d];
                    Segments.Add(new DrawSegment
                    {
                        Start = job.Start + info.x, Count = info.y,
                        Material = _regionMaterials[region.MaterialsStart + info.z / DrawReorder.FeatureKeys],
                        Kind = SegmentKind.Draw, StencilDepth = _naiveSegments[region.Start].StencilDepth,
                        TextureStart = SegmentTextures.Count, TextureCount = info.w
                    });
                    for (var i = 0; i < info.w; i++) SegmentTextures.Add(_reorderTextures[texture + i]);

                    texture += info.w;
                }

                draw += drawCount;
            }

            while (SegmentProperties.Count < Segments.Count) SegmentProperties.Add(new MaterialPropertyBlock());

            FeaturesDirty = true;
            UpdateFeatures();
            _orderChanged = true;
        }

        /// <summary>A run of the naive segments reordered as a whole (<see cref="RegionEnd"/>).</summary>
        private struct Region
        {
            public int Start;
            public int End;

            /// <summary>
            /// Its draws could merge or split (<see cref="DrawReorder"/>). These regions are those of _jobRegions, in
            /// turn.
            /// </summary>
            public bool Mergeable;

            /// <summary>Mergeable: the materials of its keys in _regionMaterials.</summary>
            public int MaterialsStart;
        }

        private readonly List<Region> _regions = new();

        // The materials of the keys of the mergeable regions.
        private readonly List<Material> _regionMaterials = new();

        /// <summary>Splits the segments into regions and prepares the mergeable ones for the reordering.</summary>
        private void PlanRegions()
        {
            _regions.Clear();
            _regionMaterials.Clear();
            _reorderSegments.Clear();
            _jobRegions.Clear();
            _reorderable = false;
            _canMergeMaterials = false;
            for (var s = 0; s < Segments.Count;)
            {
                var e = RegionEnd(Segments, s);
                var region = new Region { Start = s, End = e };

                // Some draws could merge, or split by shader features (in draws of the uber shader).
                var merge = CanMerge(Segments, s, e);
                _canMergeMaterials |= merge;
                region.Mergeable = merge || (YauiBatching.SplitByShaderFeatures &&
                                             Segments[s].Kind == SegmentKind.Draw &&
                                             _reorderMaterials.Contains(null));
                if (region.Mergeable)
                {
                    _reorderable = true;
                    region.MaterialsStart = _regionMaterials.Count;
                    _regionMaterials.AddRange(_reorderMaterials);

                    // Keys by material, numbered by first appearance.
                    var from = Segments[s].Start;
                    var last = Segments[e - 1];
                    _jobRegions.Add(new DrawReorder.Region
                    {
                        Start = from, Count = last.Start + last.Count - from, SegmentsStart = _reorderSegments.Length,
                        SegmentCount = e - s, MaterialCount = _reorderMaterials.Count
                    });
                    for (var i = s; i < e; i++)
                    {
                        var segment = Segments[i];
                        _reorderSegments.Add(new int4(segment.Start - from, segment.Count,
                            _reorderMaterials.IndexOf(segment.Material), segment.Material == null ? 1 : 0));
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

        /// <summary>
        /// Main thread, at collection: the shader features of each draw segment, if they may have changed. While the
        /// order is being reordered, <see cref="CompleteReorder"/> does it.
        /// </summary>
        public void UpdateFeatures()
        {
            if (!FeaturesDirty || _reorderScheduled) return;

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

            _hasHeavyFeatures = false;
            for (var i = 0; i < Segments.Count; i++)
            {
                var segment = Segments[i];
                segment.Features = _features[i];
                Segments[i] = segment;
                _hasHeavyFeatures |= segment.Material == null && segment.Kind == SegmentKind.Draw &&
                                     (segment.Features & (ShaderFeatures.Shadow | ShaderFeatures.Border)) != 0;
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
            _transformsChanged = true;
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

        /// <summary>
        /// Main thread, before the draws are recorded or queued: completes the reordering and uploads the draw order
        /// if it changed. World space panels queue their draws at submission, before the collection, so they upload
        /// then: the order stays in step with the segments of the queued draws.
        /// </summary>
        public void FlushOrder()
        {
            CompleteReorder();
            if (!_orderChanged) return;

            _orderChanged = false;
            UploadOrder();
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
            _reorderJob.Complete();
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
                _jobRegions.Dispose();
                _reorderSegments.Dispose();
                _reorderDraws.Dispose();
                _reorderDrawCounts.Dispose();
                _reorderTextures.Dispose();
                _world.Dispose();
                _opacity.Dispose();
                _clipSlot.Dispose();
                _clipRect.Dispose();
            }
        }
    }
}