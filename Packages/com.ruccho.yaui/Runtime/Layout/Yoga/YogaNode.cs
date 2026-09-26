// Vendored for YAUI from microsoft/microsoft-ui-reactor (MIT, see Third Party Notices.md at the package root)
// src/Reactor/Yoga/YogaNode.cs at a58008a1be0d, itself a C# port of Meta's Yoga (MIT).
// Changes: namespace; layout boundaries (dirtiness from children stops at nodes of fixed size, see
// IsLayoutBoundary); the node is an unmanaged record (NodeData) referenced by pointer (YogaNode), so that the
// algorithm runs in Burst; the measure function is called through a function pointer; no baseline functions,
// dirtied callbacks, configs or display: contents.

// C# port of Meta's Yoga layout engine Node.
// Ported from yoga/node/Node.h, yoga/node/Node.cpp

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AOT;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Yaui;

namespace Yaui.Layout.Yoga
{
    /// <summary>
    /// Size returned by measure functions.
    /// </summary>
    internal struct YogaSize
    {
        public float Width;
        public float Height;

        public YogaSize(float width, float height)
        {
            Width = width;
            Height = height;
        }
    }

    /// <summary>
    /// Measure function. Called by Yoga (on the layout thread, possibly from Burst) to determine the intrinsic size of
    /// leaf nodes.
    /// </summary>
    internal delegate YogaSize YogaMeasureFunc(
        YogaNode node, float availableWidth, YogaMeasureMode widthMode,
        float availableHeight, YogaMeasureMode heightMode);

    /// <summary>The record of a node. Records never move, so that nodes can be created while a layout runs.</summary>
    internal unsafe struct NodeData
    {
        public YogaStyle Style;
        public LayoutResults Layout;
        public UnsafeList<YogaNode> Children;
        public NodeData* Owner;

        /// <summary>Id of the measure function (<see cref="YogaMeasure"/>), or 0.</summary>
        public int MeasureId;

        /// <summary>On a root: id of the callback for dirtied layout boundaries (<see cref="YogaNode.BoundaryDirtied"/>).</summary>
        public int BoundaryCallbackId;

        public bool HasNewLayout;
        public bool PropagationStopped;
        public bool MeasureStale;
        public bool IsReferenceBaseline;
        public bool IsDirty;
        public bool AlwaysFormsContainingBlock;
        public bool Allocated;
        public YogaNodeType NodeType;
        public int LineIndex;

        // Processed dimensions: if max == min, use max; otherwise use dimension.
        public YogaValues2 ProcessedDimensions;

        public void Reset()
        {
            Style = YogaStyle.Default;
            Layout = LayoutResults.Default;
            Children.Clear();
            Owner = null;
            MeasureId = 0;
            BoundaryCallbackId = 0;
            HasNewLayout = true;
            PropagationStopped = false;
            MeasureStale = false;
            IsReferenceBaseline = false;
            IsDirty = true;
            AlwaysFormsContainingBlock = false;
            NodeType = YogaNodeType.Default;
            LineIndex = 0;
            ProcessedDimensions[0] = YogaValue.Undefined;
            ProcessedDimensions[1] = YogaValue.Undefined;
        }
    }

    /// <summary>
    /// Allocates node records in blocks that never move. Main thread only.
    /// </summary>
    internal static unsafe class YogaNodeStore
    {
        private const int BlockSize = 256;

        private static readonly List<IntPtr> Blocks = new();
        private static readonly Stack<IntPtr> Free = new();

        public static YogaNode Create()
        {
            if (Free.Count == 0)
            {
                var block = (NodeData*)UnsafeUtility.Malloc(sizeof(NodeData) * BlockSize,
                    UnsafeUtility.AlignOf<NodeData>(),
                    Allocator.Persistent);
                UnsafeUtility.MemClear(block, sizeof(NodeData) * BlockSize);
                Blocks.Add((IntPtr)block);
                for (var i = BlockSize - 1; i >= 0; i--)
                {
                    block[i].Children = new UnsafeList<YogaNode>(0, Allocator.Persistent);
                    Free.Push((IntPtr)(block + i));
                }
            }

            var data = (NodeData*)Free.Pop();
            data->Reset();
            data->Allocated = true;
            return new YogaNode(data);
        }

        /// <summary>Returns a detached node's record for reuse.</summary>
        public static void Destroy(YogaNode node)
        {
            node.Reset();
            node.Data->Allocated = false;
            Free.Push((IntPtr)node.Data);
        }

        /// <summary>Frees all records; every node becomes invalid.</summary>
        public static void DisposeAll()
        {
            foreach (var pointer in Blocks)
            {
                var block = (NodeData*)pointer;
                for (var i = 0; i < BlockSize; i++) block[i].Children.Dispose();

                UnsafeUtility.Free(block, Allocator.Persistent);
            }

            Blocks.Clear();
            Free.Clear();
            YogaMeasure.Clear();
            BoundaryCallbacks.Clear();
        }

        /// <summary>Callbacks of roots for dirtied layout boundaries, by id.</summary>
        internal static readonly Dictionary<int, Action<YogaNode>> BoundaryCallbacks = new();

        private static int _nextCallbackId;

        internal static int AddBoundaryCallback(Action<YogaNode> callback)
        {
            var id = ++_nextCallbackId;
            BoundaryCallbacks[id] = callback;
            return id;
        }
    }

    /// <summary>
    /// Measure functions by id, called from the layout (Burst) through a function pointer.
    /// </summary>
    internal static unsafe class YogaMeasure
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void Callback(int id, NodeData* node, float width, int widthMode, float height, int heightMode,
            YogaSize* result);

        private static readonly Dictionary<int, YogaMeasureFunc> Functions = new();
        private static readonly object Lock = new();
        private static Callback _callback;
        private static int _nextId;

        public static int Add(YogaMeasureFunc function)
        {
            if (_callback == null)
            {
                _callback = Invoke;
                YogaMeasurePointer.Pointer.Data =
                    new FunctionPointer<Callback>(Marshal.GetFunctionPointerForDelegate(_callback));
            }

            lock (Lock)
            {
                var id = ++_nextId;
                Functions[id] = function;
                return id;
            }
        }

        public static void Remove(int id)
        {
            lock (Lock)
            {
                Functions.Remove(id);
            }
        }

        public static void Clear()
        {
            lock (Lock)
            {
                Functions.Clear();
            }
        }

        [MonoPInvokeCallback(typeof(Callback))]
        private static void Invoke(int id, NodeData* node, float width, int widthMode, float height, int heightMode,
            YogaSize* result)
        {
            YogaMeasureFunc function;
            lock (Lock)
            {
                Functions.TryGetValue(id, out function);
            }

            *result = function != null
                ? function(new YogaNode(node), width, (YogaMeasureMode)widthMode, height, (YogaMeasureMode)heightMode)
                : default;
        }
    }

    /// <summary>
    /// The function pointer of <see cref="YogaMeasure"/>, readable from Burst (apart from its managed state).
    /// </summary>
    internal static class YogaMeasurePointer
    {
        private struct Key
        {
        }

        public static readonly SharedStatic<FunctionPointer<YogaMeasure.Callback>> Pointer =
            SharedStatic<FunctionPointer<YogaMeasure.Callback>>.GetOrCreate<Key>();
    }

    /// <summary>
    /// A node of the Yoga layout tree: a reference to its record. Holds style, layout results and children.
    /// </summary>
    internal readonly unsafe struct YogaNode : IEquatable<YogaNode>
    {
        internal readonly NodeData* Data;

        internal YogaNode(NodeData* data)
        {
            Data = data;
        }

        /// <summary>The record, for jobs.</summary>
        internal IntPtr Pointer => (IntPtr)Data;

        public bool IsNull => Data == null;

        public static YogaNode Null => default;

        public bool Equals(YogaNode other)
        {
            return Data == other.Data;
        }

        public override bool Equals(object obj)
        {
            return obj is YogaNode other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((IntPtr)Data).GetHashCode();
        }

        public static bool operator ==(YogaNode a, YogaNode b)
        {
            return a.Data == b.Data;
        }

        public static bool operator !=(YogaNode a, YogaNode b)
        {
            return a.Data != b.Data;
        }

        /// <summary>YAUI: back to a new node's state, detached from its owner and its children.</summary>
        internal void Reset()
        {
            if (Data->Owner != null)
                Owner.RemoveChild(this);
            for (var i = 0; i < Data->Children.Length; i++)
                Data->Children[i].Data->Owner = null;
            if (Data->MeasureId != 0)
                YogaMeasure.Remove(Data->MeasureId);
            if (Data->BoundaryCallbackId != 0)
                YogaNodeStore.BoundaryCallbacks.Remove(Data->BoundaryCallbackId);
            Data->Reset();
        }

        // ── Tree structure ──

        public YogaNode Owner => new(Data->Owner);
        public int ChildCount => Data->Children.Length;

        public YogaNode GetChild(int index)
        {
            return Data->Children[index];
        }

        public void InsertChild(YogaNode child, int index)
        {
            if (child == this)
                throw new InvalidOperationException("InsertChild: cannot insert a node into itself.");
            if (child.Data->Owner != null && child.Data->Owner != Data)
                throw new InvalidOperationException(
                    "InsertChild: child already has a different parent. Remove from prior parent first.");
            if (Data->MeasureId != 0)
                throw new InvalidOperationException("Cannot add children to a node with a measure function.");
            ref var children = ref Data->Children;
            children.Add(child);
            for (var i = children.Length - 1; i > index; i--)
                children[i] = children[i - 1];
            children[index] = child;
            child.Data->Owner = Data;
            MarkDirtyAndPropagate();
        }

        public bool RemoveChild(YogaNode child)
        {
            ref var children = ref Data->Children;
            for (var i = 0; i < children.Length; i++)
                if (children[i] == child)
                {
                    RemoveChild(i);
                    return true;
                }

            return false;
        }

        public void RemoveChild(int index)
        {
            ref var children = ref Data->Children;
            var child = children[index];
            for (var i = index; i < children.Length - 1; i++)
                children[i] = children[i + 1];
            children.Length--;
            child.Data->Owner = null;
            MarkDirtyAndPropagate();
        }

        public void ClearChildren()
        {
            ref var children = ref Data->Children;
            for (var i = 0; i < children.Length; i++)
                children[i].Data->Owner = null;
            children.Clear();
            MarkDirtyAndPropagate();
        }

        /// <summary>The children to lay out (YAUI does not use display: contents).</summary>
        internal LayoutChildren GetLayoutChildren()
        {
            return new LayoutChildren(this);
        }

        internal readonly struct LayoutChildren
        {
            private readonly YogaNode _node;

            internal LayoutChildren(YogaNode node)
            {
                _node = node;
            }

            public Enumerator GetEnumerator()
            {
                return new Enumerator(_node);
            }

            public struct Enumerator
            {
                private readonly NodeData* _data;
                private int _index;

                internal Enumerator(YogaNode node)
                {
                    _data = node.Data;
                    _index = -1;
                }

                public readonly YogaNode Current => _data->Children[_index];

                public bool MoveNext()
                {
                    return ++_index < _data->Children.Length;
                }
            }
        }

        internal void CollectLayoutChildren(ref UnsafeList<YogaNode> result)
        {
            for (var i = 0; i < Data->Children.Length; i++)
                result.Add(Data->Children[i]);
        }

        internal int GetLayoutChildCount()
        {
            return Data->Children.Length;
        }

        // ── Style access ──

        internal ref YogaStyle Style => ref Data->Style;

        internal ref LayoutResults Layout => ref Data->Layout;

        // ── Public style property accessors ──
        // These setters dirty the node unconditionally (see the original notes on Yoga's measurement caches).

        public FlexDirection FlexDirection
        {
            get => Data->Style.FlexDirection;
            set
            {
                Data->Style.FlexDirection = value;
                MarkDirtyAndPropagate();
            }
        }

        public FlexJustify JustifyContent
        {
            get => Data->Style.JustifyContent;
            set
            {
                Data->Style.JustifyContent = value;
                MarkDirtyAndPropagate();
            }
        }

        public FlexAlign AlignItems
        {
            get => Data->Style.AlignItems;
            set
            {
                Data->Style.AlignItems = value;
                MarkDirtyAndPropagate();
            }
        }

        public FlexAlign AlignSelf
        {
            get => Data->Style.AlignSelf;
            set
            {
                Data->Style.AlignSelf = value;
                MarkDirtyAndPropagate();
            }
        }

        public FlexAlign AlignContent
        {
            get => Data->Style.AlignContent;
            set
            {
                Data->Style.AlignContent = value;
                MarkDirtyAndPropagate();
            }
        }

        public FlexWrap FlexWrap
        {
            get => Data->Style.FlexWrap;
            set
            {
                Data->Style.FlexWrap = value;
                MarkDirtyAndPropagate();
            }
        }

        public FlexPositionType PositionType
        {
            get => Data->Style.PositionType;
            set
            {
                Data->Style.PositionType = value;
                MarkDirtyAndPropagate();
            }
        }

        public YogaDisplay Display
        {
            get => Data->Style.Display;
            set
            {
                if (value == YogaDisplay.Grid || value == YogaDisplay.Contents)
                    throw new NotImplementedException("Grid and contents are not implemented in this port of Yoga.");
                Data->Style.Display = value;
                MarkDirtyAndPropagate();
            }
        }

        public YogaOverflow Overflow
        {
            get => Data->Style.Overflow;
            set
            {
                Data->Style.Overflow = value;
                MarkDirtyAndPropagate();
            }
        }

        public float FlexGrow
        {
            get => Data->Style.FlexGrow;
            set
            {
                Data->Style.FlexGrow = value;
                MarkDirtyAndPropagate();
            }
        }

        public float FlexShrink
        {
            get => Data->Style.FlexShrink;
            set
            {
                Data->Style.FlexShrink = value;
                MarkDirtyAndPropagate();
            }
        }

        public YogaValue FlexBasis
        {
            get => Data->Style.FlexBasis;
            set
            {
                Data->Style.FlexBasis = value;
                MarkDirtyAndPropagate();
            }
        }

        public YogaValue Width
        {
            get => Data->Style.Dimensions[(int)YogaDimension.Width];
            set
            {
                Data->Style.Dimensions[(int)YogaDimension.Width] = value;
                MarkDirtyAndPropagate();
            }
        }

        public YogaValue Height
        {
            get => Data->Style.Dimensions[(int)YogaDimension.Height];
            set
            {
                Data->Style.Dimensions[(int)YogaDimension.Height] = value;
                MarkDirtyAndPropagate();
            }
        }

        public YogaValue MinWidth
        {
            get => Data->Style.MinDimensions[(int)YogaDimension.Width];
            set
            {
                Data->Style.MinDimensions[(int)YogaDimension.Width] = value;
                MarkDirtyAndPropagate();
            }
        }

        public YogaValue MinHeight
        {
            get => Data->Style.MinDimensions[(int)YogaDimension.Height];
            set
            {
                Data->Style.MinDimensions[(int)YogaDimension.Height] = value;
                MarkDirtyAndPropagate();
            }
        }

        public YogaValue MaxWidth
        {
            get => Data->Style.MaxDimensions[(int)YogaDimension.Width];
            set
            {
                Data->Style.MaxDimensions[(int)YogaDimension.Width] = value;
                MarkDirtyAndPropagate();
            }
        }

        public YogaValue MaxHeight
        {
            get => Data->Style.MaxDimensions[(int)YogaDimension.Height];
            set
            {
                Data->Style.MaxDimensions[(int)YogaDimension.Height] = value;
                MarkDirtyAndPropagate();
            }
        }

        public float AspectRatio
        {
            get => Data->Style.AspectRatio;
            set
            {
                // Degenerate aspect ratios act as auto
                var normalized = value == 0 || float.IsInfinity(value) ? float.NaN : value;
                Data->Style.AspectRatio = normalized;
                MarkDirtyAndPropagate();
            }
        }

        public void SetMargin(YogaEdge edge, YogaValue value)
        {
            Data->Style.Margin[(int)edge] = value;
            MarkDirtyAndPropagate();
        }

        public void SetPadding(YogaEdge edge, YogaValue value)
        {
            Data->Style.Padding[(int)edge] = value;
            MarkDirtyAndPropagate();
        }

        public void SetBorder(YogaEdge edge, float value)
        {
            Data->Style.Border[(int)edge] = YogaValue.Point(value);
            MarkDirtyAndPropagate();
        }

        public void SetPosition(YogaEdge edge, YogaValue value)
        {
            Data->Style.Position[(int)edge] = value;
            MarkDirtyAndPropagate();
        }

        public void SetGap(YogaGutter gutter, float value)
        {
            Data->Style.Gap[(int)gutter] = YogaValue.Point(value);
            MarkDirtyAndPropagate();
        }

        public void SetGap(YogaGutter gutter, YogaValue value)
        {
            Data->Style.Gap[(int)gutter] = value;
            MarkDirtyAndPropagate();
        }

        // ── Measure callback ──

        /// <summary>Main thread: sets the measure function (called on the layout thread), or clears it with null.</summary>
        public void SetMeasureFunction(YogaMeasureFunc function)
        {
            if (function != null && Data->Children.Length > 0)
                throw new InvalidOperationException("Cannot set measure function on a node with children.");
            if (Data->MeasureId != 0)
                YogaMeasure.Remove(Data->MeasureId);
            Data->MeasureId = function != null ? YogaMeasure.Add(function) : 0;
            Data->NodeType = function != null ? YogaNodeType.Text : YogaNodeType.Default;
            MarkDirtyAndPropagate();
        }

        public bool HasMeasureFunc => Data->MeasureId != 0;
        public bool HasBaselineFunc => false;

        internal YogaSize Measure(float availableWidth, YogaMeasureMode widthMode, float availableHeight,
            YogaMeasureMode heightMode)
        {
            YogaSize size;
            YogaMeasurePointer.Pointer.Data.Invoke(Data->MeasureId, Data, availableWidth, (int)widthMode,
                availableHeight,
                (int)heightMode, &size);
            // Validate measure result
            if (YogaFloat.IsUndefined(size.Width) || size.Width < 0)
                size.Width = YogaFloat.MaxOrDefined(0, size.Width);
            if (YogaFloat.IsUndefined(size.Height) || size.Height < 0)
                size.Height = YogaFloat.MaxOrDefined(0, size.Height);
            return size;
        }

        internal float Baseline(float width, float height)
        {
            return float.NaN;
        }

        // ── Layout results (read after CalculateLayout) ──

        public float LayoutX => Data->Layout.GetPosition(YogaPhysicalEdge.Left);
        public float LayoutY => Data->Layout.GetPosition(YogaPhysicalEdge.Top);
        public float LayoutWidth => Data->Layout.GetDimension(YogaDimension.Width);
        public float LayoutHeight => Data->Layout.GetDimension(YogaDimension.Height);

        public float LayoutMarginLeft => Data->Layout.GetMargin(YogaPhysicalEdge.Left);
        public float LayoutMarginTop => Data->Layout.GetMargin(YogaPhysicalEdge.Top);
        public float LayoutMarginRight => Data->Layout.GetMargin(YogaPhysicalEdge.Right);
        public float LayoutMarginBottom => Data->Layout.GetMargin(YogaPhysicalEdge.Bottom);

        public float LayoutPaddingLeft => Data->Layout.GetPadding(YogaPhysicalEdge.Left);
        public float LayoutPaddingTop => Data->Layout.GetPadding(YogaPhysicalEdge.Top);
        public float LayoutPaddingRight => Data->Layout.GetPadding(YogaPhysicalEdge.Right);
        public float LayoutPaddingBottom => Data->Layout.GetPadding(YogaPhysicalEdge.Bottom);

        public float LayoutBorderLeft => Data->Layout.GetBorder(YogaPhysicalEdge.Left);
        public float LayoutBorderTop => Data->Layout.GetBorder(YogaPhysicalEdge.Top);
        public float LayoutBorderRight => Data->Layout.GetBorder(YogaPhysicalEdge.Right);
        public float LayoutBorderBottom => Data->Layout.GetBorder(YogaPhysicalEdge.Bottom);

        public bool HasNewLayout
        {
            get => Data->HasNewLayout;
            set => Data->HasNewLayout = value;
        }

        // ── Config ──

        public YogaConfig Config => default;

        // ── Dirty tracking ──

        public bool IsDirty => Data->IsDirty;

        internal void SetDirty(bool isDirty)
        {
            if (Data->IsDirty == isDirty) return;
            Data->IsDirty = isDirty;
            if (!isDirty)
            {
                Data->PropagationStopped = false;
                Data->MeasureStale = false;
            }
        }

        /// <summary>Main thread only.</summary>
        internal void MarkDirtyAndPropagate()
        {
            if (!Data->IsDirty || Data->MeasureStale)
            {
                Data->MeasureStale = false;
                SetDirty(true);
                Data->Layout.ComputedFlexBasis = float.NaN;
                if (Data->Owner != null)
                    Owner.MarkDirtyFromChild();
            }
            else if (Data->PropagationStopped)
            {
                // Dirty from a child before, but now the node itself changed: the owner is affected too.
                Data->PropagationStopped = false;
                Data->Layout.ComputedFlexBasis = float.NaN;
                if (Data->Owner != null)
                    Owner.MarkDirtyFromChild();
            }
        }

        // YAUI: a change inside a layout boundary does not reach its owner. Main thread only.
        private void MarkDirtyFromChild()
        {
            if (Data->IsDirty && !Data->MeasureStale)
                return;

            if (!IsLayoutBoundary)
            {
                MarkDirtyAndPropagate();
                return;
            }

            Data->MeasureStale = false;
            SetDirty(true);
            Data->PropagationStopped = true;
            var root = Data;
            while (root->Owner != null)
                root = root->Owner;
            if (root->BoundaryCallbackId != 0 &&
                YogaNodeStore.BoundaryCallbacks.TryGetValue(root->BoundaryCallbackId, out var callback))
                callback(this);
        }

        /// <summary>
        /// YAUI: a node whose size does not depend on its content (fixed width and height), so that a change of its
        /// descendants never changes the layout outside of it. Such nodes are laid out on their own with
        /// <see cref="YogaAlgorithm.CalculateSubtreeLayout"/>. (Baseline alignment of the owner is not considered.)
        /// </summary>
        internal bool IsLayoutBoundary =>
            Data->Owner != null &&
            Data->Style.Dimensions.Get((int)YogaDimension.Width).Unit == YogaUnit.Point &&
            Data->Style.Dimensions.Get((int)YogaDimension.Height).Unit == YogaUnit.Point;

        /// <summary>
        /// YAUI: set on a root; called on the main thread when a layout boundary below it becomes dirty from its
        /// content. Null clears it.
        /// </summary>
        internal void SetBoundaryDirtied(Action<YogaNode> callback)
        {
            if (Data->BoundaryCallbackId != 0)
                YogaNodeStore.BoundaryCallbacks.Remove(Data->BoundaryCallbackId);
            Data->BoundaryCallbackId = callback != null ? YogaNodeStore.AddBoundaryCallback(callback) : 0;
        }

        /// <summary>
        /// YAUI: the content changed but not its size at the last layout, so the owner does not need a layout. The
        /// node is dirty (its cached measurements are not used) without dirtying the owner, and a later change still
        /// propagates.
        /// </summary>
        internal void MarkMeasureStale()
        {
            if (!Data->IsDirty)
            {
                SetDirty(true);
                Data->MeasureStale = true;
            }
        }

        /// <summary>YAUI: dirty from a descendant only, without having dirtied the owner.</summary>
        internal bool IsDirtyWithinBoundary => Data->IsDirty && Data->PropagationStopped;

        /// <summary>Mark this node dirty (for leaf nodes with measure functions). Main thread only.</summary>
        public void MarkDirty()
        {
            if (Data->MeasureId == 0)
                throw new InvalidOperationException("Only leaf nodes with measure functions can be marked dirty.");

            // YAUI: a change of the content, which does not reach the owner if the size is fixed.
            MarkDirtyFromChild();
        }

        // ── Internal state ──

        internal bool AlwaysFormsContainingBlock
        {
            get => Data->AlwaysFormsContainingBlock;
            set => Data->AlwaysFormsContainingBlock = value;
        }

        internal YogaNodeType NodeType
        {
            get => Data->NodeType;
            set => Data->NodeType = value;
        }

        internal bool IsReferenceBaseline
        {
            get => Data->IsReferenceBaseline;
            set => Data->IsReferenceBaseline = value;
        }

        internal ref int LineIndex => ref Data->LineIndex;
        internal ref YogaValues2 ProcessedDimensions => ref Data->ProcessedDimensions;

        internal bool HasErrata(YogaErrata errata)
        {
            return Config.HasErrata(errata);
        }

        // ── Computed helpers ──

        internal float DimensionWithMargin(FlexDirection axis, float widthSize)
        {
            return Data->Layout.GetMeasuredDimension(FlexDirectionHelper.Dimension(axis))
                   + Data->Style.ComputeMarginForAxis(axis, widthSize);
        }

        internal bool IsLayoutDimensionDefined(FlexDirection axis)
        {
            var value = Data->Layout.GetMeasuredDimension(FlexDirectionHelper.Dimension(axis));
            return YogaFloat.IsDefined(value) && value >= 0;
        }

        internal bool HasDefiniteLength(YogaDimension dimension, float ownerSize)
        {
            var usedValue = Data->ProcessedDimensions.Get((int)dimension).Resolve(ownerSize);
            return YogaFloat.IsDefined(usedValue) && usedValue >= 0;
        }

        internal float GetResolvedDimension(FlexLayoutDirection direction, YogaDimension dimension,
            float referenceLength, float ownerWidth)
        {
            var value = Data->ProcessedDimensions.Get((int)dimension).Resolve(referenceLength);
            if (Data->Style.BoxSizing == YogaBoxSizing.BorderBox)
                return value;

            var paddingAndBorder = Data->Style.ComputePaddingAndBorderForDimension(direction, dimension, ownerWidth);
            var pb = YogaFloat.IsDefined(paddingAndBorder) ? paddingAndBorder : 0;
            return YogaFloat.IsDefined(value) ? value + pb : value;
        }

        internal YogaValue ProcessFlexBasis()
        {
            var flexBasis = Data->Style.FlexBasis;
            if (!flexBasis.IsAuto && !flexBasis.IsUndefined)
                return flexBasis;
            if (YogaFloat.IsDefined(Data->Style.Flex) && Data->Style.Flex > 0)
                return Config.UseWebDefaults ? YogaValue.Auto : YogaValue.Zero;
            return YogaValue.Auto;
        }

        internal float ResolveFlexBasis(FlexLayoutDirection direction, FlexDirection flexDirection,
            float referenceLength, float ownerWidth)
        {
            var value = ProcessFlexBasis().Resolve(referenceLength);
            if (Data->Style.BoxSizing == YogaBoxSizing.BorderBox)
                return value;

            var dim = FlexDirectionHelper.Dimension(flexDirection);
            var paddingAndBorder = Data->Style.ComputePaddingAndBorderForDimension(direction, dim, ownerWidth);
            var pb = YogaFloat.IsDefined(paddingAndBorder) ? paddingAndBorder : 0;
            return YogaFloat.IsDefined(value) ? value + pb : value;
        }

        internal void ProcessDimensions()
        {
            for (var d = 0; d < 2; d++)
            {
                var maxDim = Data->Style.MaxDimensions.Get(d);
                var minDim = Data->Style.MinDimensions.Get(d);
                if (maxDim.IsDefined && YogaFloat.InexactEquals(maxDim.Value, minDim.Value) &&
                    maxDim.Unit == minDim.Unit)
                    Data->ProcessedDimensions[d] = maxDim;
                else
                    Data->ProcessedDimensions[d] = Data->Style.Dimensions.Get(d);
            }
        }

        internal FlexLayoutDirection ResolveDirection(FlexLayoutDirection ownerDirection)
        {
            if (Data->Style.Direction == FlexLayoutDirection.Inherit)
                return ownerDirection != FlexLayoutDirection.Inherit ? ownerDirection : FlexLayoutDirection.Ltr;
            return Data->Style.Direction;
        }

        internal float ResolveFlexGrow()
        {
            if (Data->Owner == null) return 0;
            if (YogaFloat.IsDefined(Data->Style.FlexGrow))
                return Data->Style.FlexGrow;
            if (YogaFloat.IsDefined(Data->Style.Flex) && Data->Style.Flex > 0)
                return Data->Style.Flex;
            return YogaStyle.DefaultFlexGrow;
        }

        internal float ResolveFlexShrink()
        {
            if (Data->Owner == null) return 0;
            if (YogaFloat.IsDefined(Data->Style.FlexShrink))
                return Data->Style.FlexShrink;
            if (!Config.UseWebDefaults && YogaFloat.IsDefined(Data->Style.Flex) && Data->Style.Flex < 0)
                return -Data->Style.Flex;
            return Config.UseWebDefaults ? YogaStyle.WebDefaultFlexShrink : YogaStyle.DefaultFlexShrink;
        }

        internal bool IsNodeFlexible()
        {
            return Data->Style.PositionType != FlexPositionType.Absolute
                   && (ResolveFlexGrow() != 0 || ResolveFlexShrink() != 0);
        }

        internal float RelativePosition(FlexDirection axis, FlexLayoutDirection direction, float axisSize)
        {
            ref var style = ref Data->Style;
            if (style.PositionType == FlexPositionType.Static)
                return 0;
            if (style.IsInlineStartPositionDefined(axis, direction) &&
                !style.IsInlineStartPositionAuto(axis, direction))
                return style.ComputeInlineStartPosition(axis, direction, axisSize);
            return -1 * style.ComputeInlineEndPosition(axis, direction, axisSize);
        }

        internal void SetPosition(FlexLayoutDirection direction, float ownerWidth, float ownerHeight)
        {
            ref var style = ref Data->Style;
            var directionRespectingRoot = Data->Owner != null ? direction : FlexLayoutDirection.Ltr;
            var mainAxis = FlexDirectionHelper.ResolveDirection(style.FlexDirection, directionRespectingRoot);
            var crossAxis = FlexDirectionHelper.ResolveCrossDirection(mainAxis, directionRespectingRoot);

            var relativePositionMain = RelativePosition(mainAxis, directionRespectingRoot,
                FlexDirectionHelper.IsRow(mainAxis) ? ownerWidth : ownerHeight);
            var relativePositionCross = RelativePosition(crossAxis, directionRespectingRoot,
                FlexDirectionHelper.IsRow(mainAxis) ? ownerHeight : ownerWidth);

            var mainAxisLeadingEdge = FlexDirectionHelper.InlineStartEdge(mainAxis, direction);
            var mainAxisTrailingEdge = FlexDirectionHelper.InlineEndEdge(mainAxis, direction);
            var crossAxisLeadingEdge = FlexDirectionHelper.InlineStartEdge(crossAxis, direction);
            var crossAxisTrailingEdge = FlexDirectionHelper.InlineEndEdge(crossAxis, direction);

            Data->Layout.SetPosition(mainAxisLeadingEdge,
                style.ComputeInlineStartMargin(mainAxis, direction, ownerWidth) + relativePositionMain);
            Data->Layout.SetPosition(mainAxisTrailingEdge,
                style.ComputeInlineEndMargin(mainAxis, direction, ownerWidth) + relativePositionMain);
            Data->Layout.SetPosition(crossAxisLeadingEdge,
                style.ComputeInlineStartMargin(crossAxis, direction, ownerWidth) + relativePositionCross);
            Data->Layout.SetPosition(crossAxisTrailingEdge,
                style.ComputeInlineEndMargin(crossAxis, direction, ownerWidth) + relativePositionCross);
        }

        // ── Layout result setters (used by algorithm) ──

        internal void SetLayoutDirection(FlexLayoutDirection direction)
        {
            Data->Layout.Direction = direction;
        }

        internal void SetLayoutMargin(float margin, YogaPhysicalEdge edge)
        {
            Data->Layout.SetMargin(edge, margin);
        }

        internal void SetLayoutBorder(float border, YogaPhysicalEdge edge)
        {
            Data->Layout.SetBorder(edge, border);
        }

        internal void SetLayoutPadding(float padding, YogaPhysicalEdge edge)
        {
            Data->Layout.SetPadding(edge, padding);
        }

        internal void SetLayoutPosition(float position, YogaPhysicalEdge edge)
        {
            Data->Layout.SetPosition(edge, position);
        }

        internal void SetLayoutMeasuredDimension(float measuredDimension, YogaDimension dimension)
        {
            Data->Layout.SetMeasuredDimension(dimension, measuredDimension);
        }

        internal void SetLayoutHadOverflow(bool hadOverflow)
        {
            Data->Layout.HadOverflow = hadOverflow;
        }

        internal void SetLayoutLastOwnerDirection(FlexLayoutDirection direction)
        {
            Data->Layout.LastOwnerDirection = direction;
        }

        internal void SetLayoutComputedFlexBasis(float value)
        {
            Data->Layout.ComputedFlexBasis = value;
        }

        internal void SetLayoutComputedFlexBasisGeneration(uint gen)
        {
            Data->Layout.ComputedFlexBasisGeneration = gen;
        }

        internal void SetLayoutDimension(float lengthValue, YogaDimension dimension)
        {
            Data->Layout.SetDimension(dimension, lengthValue);
            Data->Layout.SetRawDimension(dimension, lengthValue);
        }

        // ── Public layout entry point ──

        /// <summary>
        /// Calculates the layout for this node and all its children, on the calling thread. YAUI lays out through
        /// the Burst jobs (<see cref="TreeLayoutJob"/>); the built-in Burst of Unity 6.7 compiles jobs, not direct
        /// calls of static methods.
        /// </summary>
        public void CalculateLayout(float availableWidth = float.NaN, float availableHeight = float.NaN)
        {
            YogaAlgorithm.CalculateLayout(this, availableWidth, availableHeight, FlexLayoutDirection.Ltr);
        }
    }

    /// <summary>A tree to lay out: its root and the available size.</summary>
    internal struct RootLayout
    {
        public IntPtr Root;
        public float Width;
        public float Height;
    }

    /// <summary>
    /// Lays out the trees whose root is dirty, then picks the layout boundaries that can be laid out in parallel:
    /// those without a dirty ancestor (Burst).
    /// </summary>
    [BurstCompile]
    internal unsafe struct TreeLayoutJob : Unity.Jobs.IJob
    {
        private static readonly Unity.Profiling.ProfilerMarker Marker = new("Yaui.Layout.Tree");

        [ReadOnly] public NativeList<RootLayout> Roots;

        /// <summary>Layout boundaries whose content changed.</summary>
        [ReadOnly] public NativeList<IntPtr> Boundaries;

        public NativeList<IntPtr> Parallel;

        public void Execute()
        {
            using var _ = Marker.Auto();
            foreach (var root in Roots)
            {
                var node = new YogaNode((NodeData*)root.Root);
                if (node.IsDirty)
                    YogaAlgorithm.CalculateLayout(node, root.Width, root.Height, FlexLayoutDirection.Ltr);
            }

            foreach (var pointer in Boundaries)
            {
                var boundary = new YogaNode((NodeData*)pointer);

                // A boundary below a dirty node may be laid out by the tree or an outer boundary at the same time.
                var covered = false;
                for (var n = boundary.Owner; !n.IsNull && !covered; n = n.Owner)
                    covered = n.IsDirty;
                if (!covered)
                    Parallel.Add(pointer);
            }
        }
    }

    /// <summary>Lays out layout boundaries whose content changed, on their own and in parallel (Burst).</summary>
    [BurstCompile]
    internal unsafe struct BoundaryLayoutJob : Unity.Jobs.IJobParallelForDefer
    {
        private static readonly Unity.Profiling.ProfilerMarker Marker = new("Yaui.Layout.Boundary");

        [ReadOnly] public NativeArray<IntPtr> Boundaries;

        public void Execute(int index)
        {
            var boundary = new YogaNode((NodeData*)Boundaries[index]);
            if (boundary.IsDirtyWithinBoundary)
            {
                using var _ = Marker.Auto();
                YogaAlgorithm.CalculateSubtreeLayout(boundary);
            }
        }
    }

    /// <summary>Lays out the boundaries still dirty after the parallel pass (below a dirty node) (Burst).</summary>
    [BurstCompile]
    internal unsafe struct BoundaryFixupJob : Unity.Jobs.IJob
    {
        [ReadOnly] public NativeList<IntPtr> Boundaries;

        public void Execute()
        {
            foreach (var pointer in Boundaries)
            {
                var boundary = new YogaNode((NodeData*)pointer);
                if (boundary.IsDirtyWithinBoundary)
                    YogaAlgorithm.CalculateSubtreeLayout(boundary);
            }
        }
    }
}