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

        [NonSerialized] private Color tint = Color.white;

        // Runtime state. Not serialized, so that a domain reload (which also restores private fields) does not
        // bring back slots of stores that no longer exist.
        [NonSerialized] private PanelState panel;
        [NonSerialized] private YogaNode yoga;
        [NonSerialized] private int nodeSlot;
        [NonSerialized] private int boxSlot;
        [NonSerialized] private int extSlot;
        [NonSerialized] private int clipSlot;
        [NonSerialized] private YauiMask mask;
        [NonSerialized] private YauiCustomDraw customDraw;
        [NonSerialized] private bool boxDrawn;
        [NonSerialized] private LayoutStyle appliedLayout;
        [NonSerialized] private float appliedBorderWidth;
        [NonSerialized] private bool layoutApplied;
        [NonSerialized] internal bool LayoutDirty;
        [NonSerialized] private bool styleDirty;

        // The content primitives: a block of the primitive store, drawn after the box.
        [NonSerialized] private int contentStart;
        [NonSerialized] private int contentCapacity;
        [NonSerialized] private int contentCount;

        [NonSerialized] private YogaMeasureFunc contentMeasure;
        [NonSerialized] private bool contentMeasureDirty;

        /// <summary>Child elements in sibling order, read from the Transform when <see cref="ChildrenDirty"/>.</summary>
        [NonSerialized] internal readonly List<YauiElement> CachedChildren = new();

        [NonSerialized] internal bool ChildrenDirty = true;

        /// <summary>Index in the depth-first order of the panel, or -1.</summary>
        [NonSerialized] internal int DfsIndex = -1;

        internal int NodeSlot => nodeSlot;
        internal int BoxSlot => boxSlot;

        internal YogaNode Yoga => yoga.IsNull ? yoga = Pools.RentNode() : yoga;

        private protected PanelState Panel => panel;

        /// <summary>The state of the panel the element is registered to, or null.</summary>
        internal PanelState PanelState => NodeSlot > 0 ? panel : null;

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
            return panel == state && NodeSlot > 0;
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
            get => box.BackgroundColor;
            set
            {
                box.BackgroundColor = value;
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
            get => renderTransform.Translate;
            set
            {
                renderTransform.Translate = value;
                SyncNode();
            }
        }

        /// <summary>Degrees, clockwise.</summary>
        public float Rotation
        {
            get => renderTransform.Rotation;
            set
            {
                renderTransform.Rotation = value;
                SyncNode();
            }
        }

        public Vector2 Scale
        {
            get => renderTransform.Scale;
            set
            {
                renderTransform.Scale = value;
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
                    panel.OrderDirty = true;
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
            get => tint;
            set
            {
                tint = value;
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
            if (!yoga.IsNull && YauiSystem.IsInitialized) Pools.ReleaseNode(yoga);

            yoga = YogaNode.Null;
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
            panel?.MarkStructureDirty();
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
                panel.OrderDirty = true;
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

            panel = owner.State;
            nodeSlot = YauiSystem.Nodes.Allocate();
            boxSlot = YauiSystem.Primitives.Allocate();
            layoutApplied = false;
            contentMeasureDirty = true;
            MarkParentChildrenDirty();
            panel.MarkStructureDirty();
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
                panel.MarkHitTestDirty();
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
        internal YauiMask Mask => mask;

        internal void SetMask(YauiMask value)
        {
            mask = value;
            OnMaskChanged();
        }

        /// <summary>The mask was added, removed or changed: the shape, clip and draw order change.</summary>
        internal void OnMaskChanged()
        {
            if (NodeSlot <= 0) return;

            WriteBoxRect();
            SyncNode();
            panel.OrderDirty = true;
            YauiSystem.RequestUpdate();
        }

        /// <summary>The custom draw on this element, if enabled.</summary>
        internal YauiCustomDraw CustomDraw => customDraw;

        internal void SetCustomDraw(YauiCustomDraw value)
        {
            if (customDraw != null && customDraw != value)
                Debug.LogWarning("[YAUI] An element draws one YauiCustomDraw; the last enabled one is used.", this);

            customDraw = value;
            OnCustomDrawChanged();
        }

        internal void ClearCustomDraw(YauiCustomDraw value)
        {
            if (customDraw != value) return;

            customDraw = null;
            OnCustomDrawChanged();
        }

        /// <summary>The custom draw was added, removed or moved: the draw order changes.</summary>
        internal void OnCustomDrawChanged()
        {
            if (NodeSlot <= 0) return;

            panel.OrderDirty = true;
            YauiSystem.RequestUpdate();
        }

        /// <summary>Main thread: appends the primitives of this element in draw order: the box, then the content.</summary>
        internal void AppendDrawOrder(NativeList<uint> order)
        {
            // Invisible boxes are not drawn (a mask still uses their shape).
            if (boxSlot > 0 && box.IsVisible) order.Add((uint)boxSlot);

            AppendContent(order);
        }

        /// <summary>Main thread: appends the primitives that form the shape of this element's mask.</summary>
        internal void AppendMaskShape(NativeList<uint> order)
        {
            if (ContentIsMaskShape && contentCapacity > 0)
                AppendContent(order);
            else if (boxSlot > 0) order.Add((uint)boxSlot);
        }

        private void AppendContent(NativeList<uint> order)
        {
            // Unused slots of the block are empty quads: a smaller count keeps the order.
            for (var i = 0; i < contentCapacity; i++) order.Add((uint)(contentStart + i));
        }

        #region Content

        /// <summary>
        /// Whether a <see cref="YauiMask"/> on this element takes the shape of the content (the alpha of its images)
        /// instead of the box's, while there is content.
        /// </summary>
        protected virtual bool ContentIsMaskShape => false;

        /// <summary>The number of content primitives.</summary>
        protected int ContentCount => contentCount;

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

            if ((uint)index >= (uint)contentCount) throw new ArgumentOutOfRangeException(nameof(index));

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
            if (capacity != contentCapacity)
            {
                if (contentCapacity > 0) primitives.FreeRange(contentStart, contentCapacity);

                contentStart = capacity > 0 ? primitives.AllocateRange(capacity) : 0;
                contentCapacity = capacity;
                panel.OrderDirty = true;
            }
            else
            {
                for (var i = count; i < contentCount; i++) WriteContent(i, default);
            }

            contentCount = count;
            YauiSystem.RequestUpdate();
        }

        /// <summary>Writes a content primitive of this element's node, below the count.</summary>
        internal void WriteContent(int index, in PrimitiveData data)
        {
            ref var p = ref YauiSystem.Primitives[contentStart + index];

            // Draws are split by the textures they use.
            if (PrimitiveTexture.IdOf(p.Flags) != PrimitiveTexture.IdOf(data.Flags)) panel.OrderDirty = true;

            p = data;
            p.Node = (uint)NodeSlot;
        }

        /// <summary>The block of the content primitives (tests).</summary>
        internal (int Start, int Capacity) ContentRange => (contentStart, contentCapacity);

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

            contentMeasureDirty = true;
            panel.MarkLayoutDirty(this);
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
            if (NodeSlot <= 0 || panel.Panel == null ||
                !panel.Panel.TryScreenToCanvas(screenPosition, out var canvas, out _, out _) ||
                !panel.TryCanvasToLocal(this, canvas, out var result))
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
            if (NodeSlot <= 0 || panel.Panel == null || !panel.TryGetWorld(this, out var world)) return false;

            var canvas = world.c0 * local.x + world.c1 * local.y + world.c2;
            return panel.Panel.TryCanvasToScreen(canvas, out screenPosition);
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
                if (extSlot > 0) YauiSystem.Exts.Free(extSlot);

                if (clipSlot > 0) YauiSystem.Clips.Free(clipSlot);

                if (contentCapacity > 0) YauiSystem.Primitives.FreeRange(contentStart, contentCapacity);

                YauiSystem.Primitives.Free(boxSlot);
                YauiSystem.Nodes.Free(nodeSlot);
                MarkParentChildrenDirty();
                panel.MarkStructureDirty();
            }

            extSlot = 0;
            clipSlot = 0;
            contentStart = 0;
            contentCapacity = 0;
            contentCount = 0;
            boxSlot = 0;
            nodeSlot = 0;
            DfsIndex = -1;
            LayoutDirty = false;
            panel = null;
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

            if (!layoutApplied || !layout.Equals(appliedLayout) || box.BorderWidth != appliedBorderWidth)
            {
                styleDirty = true;
                panel.MarkLayoutDirty(this);
            }
        }

        private void SyncBox()
        {
            if (NodeSlot <= 0) return;

            var flags = PrimitiveFlags.None;
            if (box.BorderWidth > 0f) flags |= PrimitiveFlags.Border;

            if (box.HasShadow)
            {
                flags |= PrimitiveFlags.Shadow;
                if (extSlot == 0) extSlot = YauiSystem.Exts.Allocate();

                YauiSystem.Exts[extSlot] = new PrimitiveExt
                {
                    ShadowColor = GpuPacking.Color(box.ShadowColor),
                    Shadow = GpuPacking.Half4(new float4(box.ShadowOffset, box.ShadowBlur, box.ShadowSpread))
                };
            }
            else if (extSlot > 0)
            {
                YauiSystem.Exts.Free(extSlot);
                extSlot = 0;
            }

            ref var p = ref YauiSystem.Primitives[BoxSlot];
            p.Node = (uint)NodeSlot;
            p.Flags = flags;
            p.Color = GpuPacking.Color(box.BackgroundColor);
            p.BorderColor = GpuPacking.Color(box.BorderColor);
            p.Radii = GpuPacking.Half4(box.CornerRadius);
            p.BorderWidthAndSkew = GpuPacking.Half2(box.BorderWidth, 0f);
            p.Ext = (uint)extSlot;
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
                box.IsVisible || mask != null ? new float4(0f, 0f, size) : float4.zero;
            if (boxDrawn != box.IsVisible)
            {
                boxDrawn = box.IsVisible;
                panel.OrderDirty = true;
            }
        }

        private void SyncNode()
        {
            if (NodeSlot <= 0) return;

            // A mask also clips to its bounding rectangle: hit tests, and the vertex shader shrinks the quads.
            var clips = clipChildren || mask != null;
            if (clips && clipSlot == 0)
            {
                clipSlot = YauiSystem.Clips.Allocate();
                if (clipSlot > ushort.MaxValue)
                {
                    // The node record keeps the clip index in 16 bits.
                    Debug.LogError("[YAUI] Too many clipping elements.", this);
                    YauiSystem.Clips.Free(clipSlot);
                    clipSlot = 0;
                }
            }
            else if (!clips && clipSlot > 0)
            {
                YauiSystem.Clips.Free(clipSlot);
                clipSlot = 0;
            }

            ref var node = ref YauiSystem.Nodes[NodeSlot];
            node.Translate = renderTransform.Translate;
            node.Rotation = math.radians(renderTransform.Rotation);
            node.Scale = renderTransform.Scale;
            node.Pivot = renderTransform.Pivot;
            node.Opacity = opacity;
            node.Tint = GpuPacking.Rgba8(tint);
            node.ClipSlot = clipSlot;
            // The rounded shape of a mask is in the stencil; only ClipChildren clips to it per pixel.
            node.ClipRadii = clipChildren ? (float4)box.CornerRadius : float4.zero;
            node.Hittable = (byte)(raycastTarget && (box.IsVisible || HasVisibleContent) ? 1 : 0);
            panel.MarkTransformDirty(this);
        }

        /// <summary>Main thread, at submission: updates the Yoga node for the changes since the last submission.</summary>
        internal virtual void PrepareLayout()
        {
            if (styleDirty)
            {
                styleDirty = false;
                ApplyLayoutStyle();
            }

            if (!MeasuresContent) return;

            var y = Yoga;
            if (!y.HasMeasureFunc)
            {
                y.SetMeasureFunction(contentMeasure ??= InvokeMeasureContent);
                contentMeasureDirty = true;
            }

            if (contentMeasureDirty)
            {
                contentMeasureDirty = false;
                OnPrepareMeasure();
                y.MarkDirty();
            }
        }

        private void ApplyLayoutStyle()
        {
            var y = Yoga;
            y.PositionType = layout.Position == PositionType.Absolute
                ? FlexPositionType.Absolute
                : FlexPositionType.Relative;
            SetEdges(y, layout.Inset, EdgeKind.Inset);
            y.FlexDirection = layout.Direction;
            y.FlexWrap = layout.Wrap;
            y.JustifyContent = layout.JustifyContent;
            y.AlignItems = layout.AlignItems;
            y.AlignSelf = layout.AlignSelf;
            y.AlignContent = layout.AlignContent;
            y.FlexGrow = layout.Grow;
            y.FlexShrink = layout.Shrink;
            y.FlexBasis = ToYoga(layout.Basis, YogaValue.Auto);
            y.Width = ToYoga(layout.Width, YogaValue.Auto);
            y.Height = ToYoga(layout.Height, YogaValue.Auto);
            y.MinWidth = ToYoga(layout.MinWidth, YogaValue.Undefined);
            y.MinHeight = ToYoga(layout.MinHeight, YogaValue.Undefined);
            y.MaxWidth = ToYoga(layout.MaxWidth, YogaValue.Undefined);
            y.MaxHeight = ToYoga(layout.MaxHeight, YogaValue.Undefined);
            SetEdges(y, layout.Margin, EdgeKind.Margin);
            SetEdges(y, layout.Padding, EdgeKind.Padding);
            y.SetBorder(YogaEdge.All, box.BorderWidth);
            y.SetGap(YogaGutter.Column, layout.Gap.x);
            y.SetGap(YogaGutter.Row, layout.Gap.y);

            appliedLayout = layout;
            appliedBorderWidth = box.BorderWidth;
            layoutApplied = true;

            // The panel sizes its root element.
            if (TryGetComponent<YauiPanel>(out _)) panel.ResetRootSize();
        }

        private enum EdgeKind
        {
            Inset,
            Margin,
            Padding
        }

        private static void SetEdges(YogaNode y, Edges edges, EdgeKind kind)
        {
            Set(YogaEdge.Left, edges.Left);
            Set(YogaEdge.Top, edges.Top);
            Set(YogaEdge.Right, edges.Right);
            Set(YogaEdge.Bottom, edges.Bottom);

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
            return length.Unit switch
            {
                LengthUnit.Point => YogaValue.Point(length.Value),
                LengthUnit.Percent => YogaValue.Percent(length.Value),
                _ => auto
            };
        }
    }
}