using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Yaui.Core;
using Yaui.Layout.Yoga;
using Yaui.Rendering;

namespace Yaui
{
    /// <summary>
    /// A node of the UI: a flex layout box with a background, border and shadow, that can be transformed.
    /// The GameObject's Transform is ignored except for the hierarchy; its children with a <see cref="YauiElement"/>
    /// are laid out inside this box and drawn on top of it in sibling order.
    /// </summary>
    /// <remarks>
    /// Setters write to the shared stores directly. Direct writes to the serialized fields by Animator
    /// (<see cref="OnDidApplyAnimationProperties"/>) and the Inspector (<see cref="OnValidate"/>) are synchronized
    /// as a whole; only a change of the layout style re-runs the layout.
    /// </remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("YAUI/Element")]
    public class YauiElement : MonoBehaviour
    {
        [SerializeField] private LayoutStyle layout = LayoutStyle.Default;
        [SerializeField] private BoxStyle box = BoxStyle.Default;
        [SerializeField] private TransformStyle renderTransform = TransformStyle.Identity;
        [SerializeField] [Range(0f, 1f)] private float opacity = 1f;

        /// <summary>Clips the descendants to this box (CSS overflow: hidden).</summary>
        [SerializeField] private bool clipChildren;

        /// <summary>
        /// Receives pointer events through <see cref="YauiRaycaster"/>. Only elements that draw something (a
        /// visible box or content) are hit, so invisible containers never block the pointer.
        /// </summary>
        [SerializeField] private bool raycastTarget = true;

        /// <summary>
        /// A custom material for the primitives of this element (not its children), or null for the uber shader.
        /// Its shader builds on Shaders/Yaui.hlsl. A change of material between elements splits the draw call.
        /// </summary>
        [SerializeField] private Material material;

        [NonSerialized] private Color _tint = Color.white;

        // Runtime state. Not serialized, so that a domain reload (which also restores private fields) does not
        // bring back slots of stores that no longer exist.
        [NonSerialized] private PanelState _panel;
        [NonSerialized] private YogaNode _yoga;
        [NonSerialized] private int _nodeSlot;
        [NonSerialized] private int _boxSlot;
        [NonSerialized] private int _extSlot;
        [NonSerialized] private int _clipSlot;
        [NonSerialized] private YauiMask _mask;
        [NonSerialized] private YauiCustomDraw _customDraw;
        [NonSerialized] private bool _boxDrawn;
        [NonSerialized] private LayoutStyle _appliedLayout;
        [NonSerialized] private float _appliedBorderWidth;
        [NonSerialized] private bool _layoutApplied;
        [NonSerialized] internal bool LayoutDirty;
        [NonSerialized] private bool _styleDirty;

        // The content primitives: a block of the primitive store, drawn after the box.
        [NonSerialized] private int _contentStart;
        [NonSerialized] private int _contentCapacity;
        [NonSerialized] private int _contentCount;

        [NonSerialized] private YogaMeasureFunc _contentMeasure;
        [NonSerialized] private bool _contentMeasureDirty;

        /// <summary>Child elements in sibling order, read from the Transform when <see cref="ChildrenDirty"/>.</summary>
        [NonSerialized] internal readonly List<YauiElement> CachedChildren = new();

        [NonSerialized] internal bool ChildrenDirty = true;

        /// <summary>Index in the depth-first order of the panel, or -1.</summary>
        [NonSerialized] internal int DfsIndex = -1;

        internal int NodeSlot => _nodeSlot;
        internal int BoxSlot => _boxSlot;

        internal YogaNode Yoga => _yoga.IsNull ? _yoga = Pools.RentNode() : _yoga;

        private protected PanelState Panel => _panel;

        /// <summary>The state of the panel the element is registered to, or null.</summary>
        internal PanelState PanelState => NodeSlot > 0 ? _panel : null;

        /// <summary>
        /// Whether child elements are laid out and drawn. Elements that measure their content
        /// (<see cref="MeasuresContent"/>) never have children.
        /// </summary>
        protected virtual bool AcceptsChildren => true;

        internal bool LaysOutChildren => AcceptsChildren && !MeasuresContent;

        /// <summary>Whether the element is in an enabled panel: it has its slots, and content can be written.</summary>
        protected bool IsRegistered => NodeSlot > 0;

        internal bool IsRegisteredTo(PanelState state)
        {
            return _panel == state && NodeSlot > 0;
        }

        #region Properties

        public LayoutStyle Layout
        {
            get => layout;
            set
            {
                layout = value;
                SyncLayout();
            }
        }

        public BoxStyle Box
        {
            get => box;
            set
            {
                box = value;
                SyncBox();
                SyncLayout();
                if (clipChildren) SyncNode();
            }
        }

        public Color BackgroundColor
        {
            get => box.backgroundColor;
            set
            {
                box.backgroundColor = value;
                SyncBox();
            }
        }

        public TransformStyle RenderTransform
        {
            get => renderTransform;
            set
            {
                renderTransform = value;
                SyncNode();
            }
        }

        public Vector2 Translate
        {
            get => renderTransform.translate;
            set
            {
                renderTransform.translate = value;
                SyncNode();
            }
        }

        /// <summary>Degrees, clockwise.</summary>
        public float Rotation
        {
            get => renderTransform.rotation;
            set
            {
                renderTransform.rotation = value;
                SyncNode();
            }
        }

        public Vector2 Scale
        {
            get => renderTransform.scale;
            set
            {
                renderTransform.scale = value;
                SyncNode();
            }
        }

        /// <summary>Multiplies the opacity of this element and its descendants.</summary>
        public float Opacity
        {
            get => opacity;
            set
            {
                opacity = value;
                SyncNode();
            }
        }

        public Material Material
        {
            get => material;
            set
            {
                material = value;
                if (NodeSlot > 0)
                {
                    _panel.OrderDirty = true;
                    YauiSystem.RequestUpdate();
                }
            }
        }

        /// <summary>
        /// Multiplies the colors this element draws (its box and content, not its children), like the color of
        /// uGUI's CanvasRenderer. Not serialized: selectables drive it for their transitions.
        /// </summary>
        public Color Tint
        {
            get => _tint;
            set
            {
                _tint = value;
                SyncNode();
            }
        }

        public bool RaycastTarget
        {
            get => raycastTarget;
            set
            {
                raycastTarget = value;
                SyncHittable();
            }
        }

        public bool ClipChildren
        {
            get => clipChildren;
            set
            {
                clipChildren = value;
                SyncNode();
            }
        }

        /// <summary>
        /// The box from the last layout, relative to the parent's box, in canvas units. Updated right before rendering.
        /// </summary>
        public Rect LayoutRect
        {
            get
            {
                if (NodeSlot <= 0) return default;

                var node = YauiSystem.Nodes[NodeSlot];
                return new Rect(node.LayoutPosition, node.LayoutSize);
            }
        }

        #endregion

        #region Unity messages

        protected virtual void OnEnable()
        {
            Register();
        }

        protected virtual void OnDisable()
        {
            Unregister();
        }

        protected virtual void OnDestroy()
        {
            // The node may still be in the tree of a layout in flight; it is recycled at the next submission.
            if (!_yoga.IsNull && YauiSystem.IsInitialized) Pools.ReleaseNode(_yoga);

            _yoga = YogaNode.Null;
        }

        protected virtual void OnTransformParentChanged()
        {
            if (NodeSlot > 0)
            {
                Unregister();
                Register();
            }
        }

        protected virtual void OnTransformChildrenChanged()
        {
            ChildrenDirty = true;
            _panel?.MarkStructureDirty();
        }

        /// <summary>Main thread, at structure rebuild: reads the child elements from the Transform hierarchy.</summary>
        internal void RefreshChildren()
        {
            ChildrenDirty = false;
            CachedChildren.Clear();
            var t = transform;
            for (var i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                if (child.gameObject.activeInHierarchy && !child.TryGetComponent<YauiPanel>(out _) &&
                    child.TryGetComponent<YauiElement>(out var element))
                    CachedChildren.Add(element);
            }
        }

        /// <summary>This element joined or left the panel: the parent's children changed.</summary>
        private void MarkParentChildrenDirty()
        {
            var parent = transform.parent;
            if (parent != null && parent.TryGetComponent<YauiElement>(out var parentElement))
                parentElement.ChildrenDirty = true;
        }

        protected virtual void OnValidate()
        {
            if (NodeSlot > 0)
            {
                SyncAll();
                _panel.OrderDirty = true;
            }
        }

        // Called by Unity after an Animator wrote serialized fields directly.
        protected virtual void OnDidApplyAnimationProperties()
        {
            SyncAll();
        }

        #endregion

        private void Register()
        {
            var owner = GetComponentInParent<YauiPanel>();
            if (owner == null || !owner.enabled) return;

            _panel = owner.State;
            _nodeSlot = YauiSystem.Nodes.Allocate();
            _boxSlot = YauiSystem.Primitives.Allocate();
            _layoutApplied = false;
            _contentMeasureDirty = true;
            MarkParentChildrenDirty();
            _panel.MarkStructureDirty();
            SyncAll();
            OnRegistered();
        }

        /// <summary>
        /// Whether the element draws content besides its box: it is then hittable. Call <see cref="SyncHittable"/>
        /// when the value changes.
        /// </summary>
        protected virtual bool HasVisibleContent => false;

        /// <summary>Updates whether the box receives hits, after the box, the content or the flag changed.</summary>
        protected void SyncHittable()
        {
            if (NodeSlot <= 0) return;

            var hittable = (byte)(raycastTarget && (box.IsVisible || HasVisibleContent) ? 1 : 0);
            ref var node = ref YauiSystem.Nodes[NodeSlot];
            if (node.Hittable != hittable)
            {
                node.Hittable = hittable;
                _panel.MarkHitTestDirty();
            }
        }

        /// <summary>Called after the box style was written (e.g. content that uses its corner radii).</summary>
        protected virtual void OnBoxChanged()
        {
        }

        /// <summary>
        /// Called after the element got its slots in a panel (when enabled, or moved to another panel): write the
        /// content here. Content written before is gone.
        /// </summary>
        protected virtual void OnRegistered()
        {
        }

        /// <summary>
        /// Called before the element releases its slots, including its content. The stores may already be shut down
        /// (domain reload).
        /// </summary>
        protected virtual void OnUnregistering()
        {
        }

        /// <summary>The mask on this element, if enabled.</summary>
        internal YauiMask Mask => _mask;

        internal void SetMask(YauiMask value)
        {
            _mask = value;
            OnMaskChanged();
        }

        /// <summary>The mask was added, removed or changed: the shape, clip and draw order change.</summary>
        internal void OnMaskChanged()
        {
            if (NodeSlot <= 0) return;

            WriteBoxRect();
            SyncNode();
            _panel.OrderDirty = true;
            YauiSystem.RequestUpdate();
        }

        /// <summary>The custom draw on this element, if enabled.</summary>
        internal YauiCustomDraw CustomDraw => _customDraw;

        internal void SetCustomDraw(YauiCustomDraw value)
        {
            if (_customDraw != null && _customDraw != value)
                Debug.LogWarning("[YAUI] An element draws one YauiCustomDraw; the last enabled one is used.", this);

            _customDraw = value;
            OnCustomDrawChanged();
        }

        internal void ClearCustomDraw(YauiCustomDraw value)
        {
            if (_customDraw != value) return;

            _customDraw = null;
            OnCustomDrawChanged();
        }

        /// <summary>The custom draw was added, removed or moved: the draw order changes.</summary>
        internal void OnCustomDrawChanged()
        {
            if (NodeSlot <= 0) return;

            _panel.OrderDirty = true;
            YauiSystem.RequestUpdate();
        }

        /// <summary>Main thread: appends the primitives of this element in draw order: the box, then the content.</summary>
        internal void AppendDrawOrder(NativeList<uint> order)
        {
            // Invisible boxes are not drawn (a mask still uses their shape).
            if (_boxSlot > 0 && box.IsVisible) order.Add((uint)_boxSlot);

            AppendContent(order);
        }

        /// <summary>Main thread: appends the primitives that form the shape of this element's mask.</summary>
        internal void AppendMaskShape(NativeList<uint> order)
        {
            if (ContentIsMaskShape && _contentCapacity > 0)
                AppendContent(order);
            else if (_boxSlot > 0) order.Add((uint)_boxSlot);
        }

        private void AppendContent(NativeList<uint> order)
        {
            // Unused slots of the block are empty quads: a smaller count keeps the order.
            for (var i = 0; i < _contentCapacity; i++) order.Add((uint)(_contentStart + i));
        }

        #region Content

        /// <summary>
        /// Whether a <see cref="YauiMask"/> on this element takes the shape of the content (the alpha of its images)
        /// instead of the box's, while there is content.
        /// </summary>
        protected virtual bool ContentIsMaskShape => false;

        /// <summary>The number of content primitives.</summary>
        protected int ContentCount => _contentCount;

        /// <summary>
        /// Replaces the content: primitives drawn on top of the box and under the children, in the order given.
        /// They follow the element's transform, opacity, tint and clips. Does nothing unless
        /// <see cref="IsRegistered"/>; write the content in <see cref="OnRegistered"/>.
        /// </summary>
        protected void SetContent(ReadOnlySpan<YauiPrimitive> primitives)
        {
            if (NodeSlot <= 0) return;

            ResizeContent(primitives.Length);
            for (var i = 0; i < primitives.Length; i++) WriteContent(i, primitives[i].Data);
        }

        /// <summary>Replaces one content primitive, below <see cref="ContentCount"/>.</summary>
        protected void SetContent(int index, in YauiPrimitive primitive)
        {
            if (NodeSlot <= 0) return;

            if ((uint)index >= (uint)_contentCount) throw new ArgumentOutOfRangeException(nameof(index));

            WriteContent(index, primitive.Data);
            YauiSystem.RequestUpdate();
        }

        /// <summary>Removes the content.</summary>
        protected void ClearContent()
        {
            if (NodeSlot > 0) ResizeContent(0);
        }

        /// <summary>
        /// Sets the number of content primitives. Primitives below the count keep their values unless the block
        /// moves (the capacity changes); the caller writes them all.
        /// </summary>
        internal void ResizeContent(int count)
        {
            var primitives = YauiSystem.Primitives;
            var capacity = count == 0 ? 0 : GpuStore<PrimitiveData>.RangeCapacity(count);
            if (capacity != _contentCapacity)
            {
                if (_contentCapacity > 0) primitives.FreeRange(_contentStart, _contentCapacity);

                _contentStart = capacity > 0 ? primitives.AllocateRange(capacity) : 0;
                _contentCapacity = capacity;
                _panel.OrderDirty = true;
            }
            else
            {
                for (var i = count; i < _contentCount; i++) WriteContent(i, default);
            }

            _contentCount = count;
            YauiSystem.RequestUpdate();
        }

        /// <summary>Writes a content primitive of this element's node, below the count.</summary>
        internal void WriteContent(int index, in PrimitiveData data)
        {
            ref var p = ref YauiSystem.Primitives[_contentStart + index];

            // Draws are split by the textures they use.
            if (PrimitiveTexture.IdOf(p.Flags) != PrimitiveTexture.IdOf(data.Flags)) _panel.OrderDirty = true;

            p = data;
            p.Node = (uint)NodeSlot;
        }

        /// <summary>The block of the content primitives (tests).</summary>
        internal (int Start, int Capacity) ContentRange => (_contentStart, _contentCapacity);

        #endregion

        #region Measurement

        /// <summary>
        /// Whether the size of the element comes from <see cref="MeasureContent"/>, like a text. Such an element has
        /// no children. Must not change while the element is enabled.
        /// </summary>
        protected virtual bool MeasuresContent => false;

        /// <summary>
        /// The size of the content (inside the padding and the border) within the given constraints. Called during
        /// the layout, on a worker thread, possibly several times with different constraints and concurrently with
        /// other elements: read only state captured in <see cref="OnPrepareMeasure"/>, and no Unity objects.
        /// </summary>
        protected virtual Vector2 MeasureContent(float width, YauiMeasureMode widthMode, float height,
            YauiMeasureMode heightMode)
        {
            return Vector2.zero;
        }

        /// <summary>
        /// Main thread, before a layout that measures the content again: capture what <see cref="MeasureContent"/>
        /// reads. The layout may run until rendering, while the main thread goes on.
        /// </summary>
        protected virtual void OnPrepareMeasure()
        {
        }

        /// <summary>The size of the content changed: measures it again at the next layout.</summary>
        protected void MarkMeasureDirty()
        {
            if (NodeSlot <= 0) return;

            _contentMeasureDirty = true;
            _panel.MarkLayoutDirty(this);
        }

        private YogaSize InvokeMeasureContent(YogaNode node, float width, YogaMeasureMode widthMode, float height,
            YogaMeasureMode heightMode)
        {
            try
            {
                var size = MeasureContent(width, (YauiMeasureMode)widthMode, height, (YauiMeasureMode)heightMode);
                return new YogaSize(size.x, size.y);
            }
            catch (Exception e)
            {
                // It must not unwind through the layout job.
                Debug.LogException(e);
                return default;
            }
        }

        #endregion

        /// <summary>
        /// Converts a screen position (pixels, origin at the bottom-left, like <c>PointerEventData.position</c>)
        /// to the local space of this element's box (canvas units, origin at the top-left, Y down), with the
        /// transforms last rendered. False if the element is not in an active panel or the point is not on it.
        /// </summary>
        public bool ScreenToLocal(Vector2 screenPosition, out Vector2 local)
        {
            local = default;
            if (NodeSlot <= 0 || _panel.Panel == null ||
                !_panel.Panel.TryScreenToCanvas(screenPosition, out var canvas, out _, out _) ||
                !_panel.TryCanvasToLocal(this, canvas, out var result))
                return false;

            local = result;
            return true;
        }

        /// <summary>
        /// The content box (inside the border and the padding) from the last layout, in the local space of the
        /// element's box.
        /// </summary>
        public Rect ContentBox
        {
            get
            {
                if (NodeSlot <= 0) return default;

                var r = ContentRect();
                return new Rect(r.x, r.y, r.z, r.w);
            }
        }

        /// <summary>
        /// Converts a point in the local space of this element's box to a screen position (pixels, origin at the
        /// bottom-left), with the transforms last rendered.
        /// </summary>
        public bool LocalToScreen(Vector2 local, out Vector2 screenPosition)
        {
            screenPosition = default;
            if (NodeSlot <= 0 || _panel.Panel == null || !_panel.TryGetWorld(this, out var world)) return false;

            var canvas = world.c0 * local.x + world.c1 * local.y + world.c2;
            return _panel.Panel.TryCanvasToScreen(canvas, out screenPosition);
        }

        /// <summary>The content box (inside the border and the padding) from the last layout: xy: min, zw: size.</summary>
        private protected float4 ContentRect()
        {
            var y = Yoga;
            var min = new float2(y.LayoutBorderLeft + y.LayoutPaddingLeft, y.LayoutBorderTop + y.LayoutPaddingTop);
            var size = new float2(
                y.LayoutWidth - y.LayoutPaddingLeft - y.LayoutPaddingRight - y.LayoutBorderLeft - y.LayoutBorderRight,
                y.LayoutHeight - y.LayoutPaddingTop - y.LayoutPaddingBottom - y.LayoutBorderTop -
                y.LayoutBorderBottom);
            return float.IsNaN(size.x) || float.IsNaN(size.y) ? float4.zero : new float4(min, math.max(size, 0f));
        }

        /// <summary>
        /// Main thread, once a frame right before rendering: the layout of this element changed
        /// (<see cref="LayoutRect"/>, <see cref="ContentBox"/>). Content written here is drawn in this frame.
        /// </summary>
        protected virtual void OnLayoutApplied()
        {
        }

        internal void NotifyLayoutApplied()
        {
            OnLayoutApplied();
        }

        /// <summary>Registers again to the panel above, for example after the panel was re-enabled.</summary>
        internal void Reregister()
        {
            if (!isActiveAndEnabled) return;

            Unregister();
            Register();
        }

        internal void Unregister()
        {
            if (NodeSlot <= 0) return;

            OnUnregistering();

            // The stores are gone after a shutdown (domain reload), and so are the slots.
            if (YauiSystem.IsInitialized)
            {
                if (_extSlot > 0) YauiSystem.Exts.Free(_extSlot);

                if (_clipSlot > 0) YauiSystem.Clips.Free(_clipSlot);

                if (_contentCapacity > 0) YauiSystem.Primitives.FreeRange(_contentStart, _contentCapacity);

                YauiSystem.Primitives.Free(_boxSlot);
                YauiSystem.Nodes.Free(_nodeSlot);
                MarkParentChildrenDirty();
                _panel.MarkStructureDirty();
            }

            _extSlot = 0;
            _clipSlot = 0;
            _contentStart = 0;
            _contentCapacity = 0;
            _contentCount = 0;
            _boxSlot = 0;
            _nodeSlot = 0;
            DfsIndex = -1;
            LayoutDirty = false;
            _panel = null;
        }

        private void SyncAll()
        {
            if (NodeSlot <= 0) return;

            SyncLayout();
            SyncBox();
            SyncNode();
        }

        private void SyncLayout()
        {
            if (NodeSlot <= 0) return;

            if (!_layoutApplied || !layout.Equals(_appliedLayout) || box.borderWidth != _appliedBorderWidth)
            {
                _styleDirty = true;
                _panel.MarkLayoutDirty(this);
            }
        }

        private void SyncBox()
        {
            if (NodeSlot <= 0) return;

            var flags = PrimitiveFlags.None;
            if (box.borderWidth > 0f) flags |= PrimitiveFlags.Border;

            if (box.HasShadow)
            {
                flags |= PrimitiveFlags.Shadow;
                if (_extSlot == 0) _extSlot = YauiSystem.Exts.Allocate();

                YauiSystem.Exts[_extSlot] = new PrimitiveExt
                {
                    ShadowColor = GpuPacking.Color(box.shadowColor),
                    Shadow = GpuPacking.Half4(new float4(box.shadowOffset, box.shadowBlur, box.shadowSpread))
                };
            }
            else if (_extSlot > 0)
            {
                YauiSystem.Exts.Free(_extSlot);
                _extSlot = 0;
            }

            ref var p = ref YauiSystem.Primitives[BoxSlot];
            p.Node = (uint)NodeSlot;
            p.Flags = flags;
            p.Color = GpuPacking.Color(box.backgroundColor);
            p.BorderColor = GpuPacking.Color(box.borderColor);
            p.Radii = GpuPacking.Half4(box.cornerRadius);
            p.BorderWidthAndSkew = GpuPacking.Half2(box.borderWidth, 0f);
            p.Ext = (uint)_extSlot;
            WriteBoxRect();
            SyncHittable();
            OnBoxChanged();
            YauiSystem.RequestUpdate();
        }

        /// <summary>The box primitive covers the laid-out size, or nothing if the box is invisible.</summary>
        internal void WriteBoxRect()
        {
            var size = YauiSystem.Nodes[NodeSlot].LayoutSize;
            YauiSystem.Primitives[BoxSlot].Rect =
                box.IsVisible || _mask != null ? new float4(0f, 0f, size) : float4.zero;
            if (_boxDrawn != box.IsVisible)
            {
                _boxDrawn = box.IsVisible;
                _panel.OrderDirty = true;
            }
        }

        private void SyncNode()
        {
            if (NodeSlot <= 0) return;

            // A mask also clips to its bounding rectangle: hit tests, and the vertex shader shrinks the quads.
            var clips = clipChildren || _mask != null;
            if (clips && _clipSlot == 0)
            {
                _clipSlot = YauiSystem.Clips.Allocate();
                if (_clipSlot > ushort.MaxValue)
                {
                    // The node record keeps the clip index in 16 bits.
                    Debug.LogError("[YAUI] Too many clipping elements.", this);
                    YauiSystem.Clips.Free(_clipSlot);
                    _clipSlot = 0;
                }
            }
            else if (!clips && _clipSlot > 0)
            {
                YauiSystem.Clips.Free(_clipSlot);
                _clipSlot = 0;
            }

            ref var node = ref YauiSystem.Nodes[NodeSlot];
            node.Translate = renderTransform.translate;
            node.Rotation = math.radians(renderTransform.rotation);
            node.Scale = renderTransform.scale;
            node.Pivot = renderTransform.pivot;
            node.Opacity = opacity;
            node.Tint = GpuPacking.Rgba8(_tint);
            node.ClipSlot = _clipSlot;
            // The rounded shape of a mask is in the stencil; only ClipChildren clips to it per pixel.
            node.ClipRadii = clipChildren ? (float4)box.cornerRadius : float4.zero;
            node.Hittable = (byte)(raycastTarget && (box.IsVisible || HasVisibleContent) ? 1 : 0);
            _panel.MarkTransformDirty(this);
        }

        /// <summary>Main thread, at submission: updates the Yoga node for the changes since the last submission.</summary>
        internal virtual void PrepareLayout()
        {
            if (_styleDirty)
            {
                _styleDirty = false;
                ApplyLayoutStyle();
            }

            if (!MeasuresContent) return;

            var y = Yoga;
            if (!y.HasMeasureFunc)
            {
                y.SetMeasureFunction(_contentMeasure ??= InvokeMeasureContent);
                _contentMeasureDirty = true;
            }

            if (_contentMeasureDirty)
            {
                _contentMeasureDirty = false;
                OnPrepareMeasure();
                y.MarkDirty();
            }
        }

        private void ApplyLayoutStyle()
        {
            var y = Yoga;
            y.PositionType = layout.position == PositionType.Absolute
                ? FlexPositionType.Absolute
                : FlexPositionType.Relative;
            SetEdges(y, layout.inset, EdgeKind.Inset);
            y.FlexDirection = layout.direction;
            y.FlexWrap = layout.wrap;
            y.JustifyContent = layout.justifyContent;
            y.AlignItems = layout.alignItems;
            y.AlignSelf = layout.alignSelf;
            y.AlignContent = layout.alignContent;
            y.FlexGrow = layout.grow;
            y.FlexShrink = layout.shrink;
            y.FlexBasis = ToYoga(layout.basis, YogaValue.Auto);
            y.Width = ToYoga(layout.width, YogaValue.Auto);
            y.Height = ToYoga(layout.height, YogaValue.Auto);
            y.MinWidth = ToYoga(layout.minWidth, YogaValue.Undefined);
            y.MinHeight = ToYoga(layout.minHeight, YogaValue.Undefined);
            y.MaxWidth = ToYoga(layout.maxWidth, YogaValue.Undefined);
            y.MaxHeight = ToYoga(layout.maxHeight, YogaValue.Undefined);
            SetEdges(y, layout.margin, EdgeKind.Margin);
            SetEdges(y, layout.padding, EdgeKind.Padding);
            y.SetBorder(YogaEdge.All, box.borderWidth);
            y.SetGap(YogaGutter.Column, layout.gap.x);
            y.SetGap(YogaGutter.Row, layout.gap.y);

            _appliedLayout = layout;
            _appliedBorderWidth = box.borderWidth;
            _layoutApplied = true;

            // The panel sizes its root element.
            if (TryGetComponent<YauiPanel>(out _)) _panel.ResetRootSize();
        }

        private enum EdgeKind
        {
            Inset,
            Margin,
            Padding
        }

        private static void SetEdges(YogaNode y, Edges edges, EdgeKind kind)
        {
            Set(YogaEdge.Left, edges.left);
            Set(YogaEdge.Top, edges.top);
            Set(YogaEdge.Right, edges.right);
            Set(YogaEdge.Bottom, edges.bottom);

            void Set(YogaEdge edge, Length length)
            {
                switch (kind)
                {
                    case EdgeKind.Inset:
                        y.SetPosition(edge, ToYoga(length, YogaValue.Undefined));
                        break;
                    case EdgeKind.Margin:
                        y.SetMargin(edge, ToYoga(length, YogaValue.Auto));
                        break;
                    default:
                        y.SetPadding(edge, ToYoga(length, YogaValue.Zero));
                        break;
                }
            }
        }

        private static YogaValue ToYoga(Length length, YogaValue auto)
        {
            return length.unit switch
            {
                LengthUnit.Point => YogaValue.Point(length.value),
                LengthUnit.Percent => YogaValue.Percent(length.value),
                _ => auto
            };
        }
    }
}