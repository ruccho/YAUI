using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace Yaui
{
    public enum ScrollMovement
    {
        /// <summary>The content moves freely.</summary>
        Unrestricted,

        /// <summary>The content can be pulled past its edges and springs back.</summary>
        Elastic,

        /// <summary>The content stops at its edges.</summary>
        Clamped
    }

    /// <summary>
    /// Scrolls a content element inside a viewport element by dragging, the scroll wheel and scrollbars, like uGUI's
    /// ScrollRect. The content moves by its render transform (<see cref="YauiElement.Translate"/>, driven by the
    /// scroll view), so scrolling never lays out again. The content keeps its own layout: typically the viewport
    /// clips its children, and the content is its absolutely positioned child at the top-left, as wide as the
    /// viewport for vertical scrolling, and as tall as its children.
    /// </summary>
    /// <remarks>Scroll positions are in canvas units, from the top-left; normalized positions are 0 at the top-left.</remarks>
    [AddComponentMenu("YAUI/Scroll View")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(YauiElement))]
    public class YauiScrollView : MonoBehaviour, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler,
        IEndDragHandler, IScrollHandler
    {
        [Serializable]
        public class ScrollViewEvent : UnityEvent<Vector2>
        {
        }

        [SerializeField] private YauiElement content;

        /// <summary>The element that shows the content; this GameObject's element when null.</summary>
        [SerializeField] private YauiElement viewport;

        [SerializeField] private bool horizontal = true;
        [SerializeField] private bool vertical = true;
        [SerializeField] private ScrollMovement movementType = ScrollMovement.Elastic;

        /// <summary>Elastic: seconds (roughly) the content takes to spring back.</summary>
        [SerializeField] private float elasticity = 0.1f;

        [SerializeField] private bool inertia = true;

        /// <summary>The fraction of the velocity left after a second.</summary>
        [SerializeField] [Range(0f, 1f)] private float decelerationRate = 0.135f;

        /// <summary>Canvas units per step of the scroll wheel.</summary>
        [SerializeField] private float scrollSensitivity = 20f;

        /// <summary>Driven by the scroll view: its size is the visible part, its value the normalized position.</summary>
        [SerializeField] private YauiScrollbar horizontalScrollbar;

        /// <summary>Like <see cref="horizontalScrollbar"/>; with the direction TopToBottom, since 0 is the top.</summary>
        [SerializeField] private YauiScrollbar verticalScrollbar;

        [SerializeField] private ScrollViewEvent onValueChanged = new();

        [NonSerialized] private Vector2 _position;
        [NonSerialized] private Vector2 _velocity;
        [NonSerialized] private bool _dragging;
        [NonSerialized] private Vector2 _dragStartPointer;
        [NonSerialized] private Vector2 _dragStartPosition;
        [NonSerialized] private Vector2 _previousPosition = new(float.NaN, float.NaN);
        [NonSerialized] private Vector2 _previousRange = new(float.NaN, float.NaN);
        [NonSerialized] private bool _updatingScrollbars;

        public YauiElement Content
        {
            get => content;
            set => content = value;
        }

        public YauiElement Viewport
        {
            get => viewport != null ? viewport : GetComponent<YauiElement>();
            set => viewport = value;
        }

        public bool Horizontal
        {
            get => horizontal;
            set => horizontal = value;
        }

        public bool Vertical
        {
            get => vertical;
            set => vertical = value;
        }

        public ScrollMovement MovementType
        {
            get => movementType;
            set => movementType = value;
        }

        public float Elasticity
        {
            get => elasticity;
            set => elasticity = value;
        }

        public bool Inertia
        {
            get => inertia;
            set => inertia = value;
        }

        public float DecelerationRate
        {
            get => decelerationRate;
            set => decelerationRate = value;
        }

        public float ScrollSensitivity
        {
            get => scrollSensitivity;
            set => scrollSensitivity = value;
        }

        public YauiScrollbar HorizontalScrollbar
        {
            get => horizontalScrollbar;
            set => SetScrollbar(ref horizontalScrollbar, value, OnHorizontalScrollbar);
        }

        public YauiScrollbar VerticalScrollbar
        {
            get => verticalScrollbar;
            set => SetScrollbar(ref verticalScrollbar, value, OnVerticalScrollbar);
        }

        /// <summary>Invoked with <see cref="NormalizedPosition"/> when the content moves.</summary>
        public ScrollViewEvent OnValueChanged
        {
            get => onValueChanged;
            set => onValueChanged = value ?? new ScrollViewEvent();
        }

        public Vector2 Velocity
        {
            get => _velocity;
            set => _velocity = value;
        }

        /// <summary>How far the content is scrolled from its laid-out place, in canvas units (positive: toward its end).</summary>
        public Vector2 ScrollPosition
        {
            get => _position;
            set
            {
                _position = value;
                Apply();
            }
        }

        /// <summary>How far the content can scroll on each axis.</summary>
        public Vector2 ScrollRange
        {
            get
            {
                if (content == null) return Vector2.zero;

                var view = Viewport.LayoutRect.size;
                var size = content.LayoutRect.size;
                return Vector2.Max(size - view, Vector2.zero);
            }
        }

        /// <summary>The scroll position as a fraction of <see cref="ScrollRange"/> (0 at the top-left).</summary>
        public Vector2 NormalizedPosition
        {
            get
            {
                var range = ScrollRange;
                return new Vector2(range.x > 0f ? _position.x / range.x : 0f, range.y > 0f ? _position.y / range.y : 0f);
            }
            set
            {
                var range = ScrollRange;
                _position = Vector2.Scale(value, range);
                _velocity = Vector2.zero;
                Apply();
            }
        }

        public void StopMovement()
        {
            _velocity = Vector2.zero;
        }

        /// <summary>Whether the content can move along an axis (0: horizontal, 1: vertical).</summary>
        private bool CanScroll(int axis)
        {
            return (axis == 0 ? horizontal : vertical) && ScrollRange[axis] > 0f;
        }

        /// <summary>
        /// Sends an event to the handler above this scroll view. A drag handed over keeps going there: the input
        /// module sends the rest of the drag to <see cref="PointerEventData.pointerDrag"/>.
        /// </summary>
        private bool PassToParent<T>(PointerEventData eventData, ExecuteEvents.EventFunction<T> function)
            where T : IEventSystemHandler
        {
            var parent = transform.parent;
            var handler = parent != null ? ExecuteEvents.GetEventHandler<T>(parent.gameObject) : null;
            if (handler == null) return false;

            if (typeof(T) == typeof(IBeginDragHandler))
            {
                eventData.pointerDrag = handler;
                ExecuteEvents.Execute(handler, eventData, ExecuteEvents.initializePotentialDrag);
            }

            ExecuteEvents.Execute(handler, eventData, function);
            return true;
        }

        /// <summary>Scrolls as little as needed to show a descendant of the content.</summary>
        public void ScrollIntoView(YauiElement target)
        {
            if (content == null || target == null) return;

            // The target's box in the content.
            var offset = Vector2.zero;
            var current = target;
            while (current != null && current != content)
            {
                offset += current.LayoutRect.position;
                current = Track.ParentOf(current);
            }

            if (current != content) return;

            var size = target.LayoutRect.size;
            var view = Viewport.LayoutRect.size;
            var scrolled = _position;
            for (var axis = 0; axis < 2; axis++)
                if (offset[axis] < scrolled[axis])
                    scrolled[axis] = offset[axis];
                else if (offset[axis] + size[axis] > scrolled[axis] + view[axis])
                    scrolled[axis] = offset[axis] + size[axis] - view[axis];

            _velocity = Vector2.zero;
            _position = scrolled - Overscroll(scrolled);
            Apply();
        }

        protected virtual void OnEnable()
        {
            if (horizontalScrollbar != null) horizontalScrollbar.OnValueChanged.AddListener(OnHorizontalScrollbar);

            if (verticalScrollbar != null) verticalScrollbar.OnValueChanged.AddListener(OnVerticalScrollbar);

            _previousPosition = new Vector2(float.NaN, float.NaN);
            _previousRange = new Vector2(float.NaN, float.NaN);
        }

        protected virtual void OnDisable()
        {
            if (horizontalScrollbar != null) horizontalScrollbar.OnValueChanged.RemoveListener(OnHorizontalScrollbar);

            if (verticalScrollbar != null) verticalScrollbar.OnValueChanged.RemoveListener(OnVerticalScrollbar);

            _dragging = false;
            _velocity = Vector2.zero;
        }

        private void SetScrollbar(ref YauiScrollbar field, YauiScrollbar value, UnityAction<float> listener)
        {
            if (field != null && isActiveAndEnabled) field.OnValueChanged.RemoveListener(listener);

            field = value;
            if (field != null && isActiveAndEnabled) field.OnValueChanged.AddListener(listener);

            _previousRange = new Vector2(float.NaN, float.NaN);
        }

        private void OnHorizontalScrollbar(float value)
        {
            if (!_updatingScrollbars)
            {
                _position.x = value * ScrollRange.x;
                _velocity.x = 0f;
                Apply();
            }
        }

        private void OnVerticalScrollbar(float value)
        {
            if (!_updatingScrollbars)
            {
                _position.y = value * ScrollRange.y;
                _velocity.y = 0f;
                Apply();
            }
        }

        #region Events

        public virtual void OnInitializePotentialDrag(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) _velocity = Vector2.zero;
        }

        public virtual void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !isActiveAndEnabled) return;

            // Nested scroll views: a drag along an axis this one cannot scroll goes to the one above.
            var moved = eventData.position - eventData.pressPosition;
            var axis = Mathf.Abs(moved.x) >= Mathf.Abs(moved.y) ? 0 : 1;
            if (!CanScroll(axis) && PassToParent(eventData, ExecuteEvents.beginDragHandler)) return;

            if (!Viewport.ScreenToLocal(eventData.position, out _dragStartPointer)) return;

            _dragStartPosition = _position;
            _dragging = true;
        }

        public virtual void OnDrag(PointerEventData eventData)
        {
            if (!_dragging || eventData.button != PointerEventData.InputButton.Left ||
                !Viewport.ScreenToLocal(eventData.position, out var pointer))
                return;

            var target = _dragStartPosition - (pointer - _dragStartPointer);
            if (movementType == ScrollMovement.Elastic)
            {
                // Pulled past an edge, the content follows less and less.
                var over = Overscroll(target);
                var view = Viewport.LayoutRect.size;
                target.x -= over.x - RubberDelta(over.x, view.x);
                target.y -= over.y - RubberDelta(over.y, view.y);
            }

            SetPosition(target);
        }

        public virtual void OnEndDrag(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) _dragging = false;
        }

        public virtual void OnScroll(PointerEventData eventData)
        {
            if (!isActiveAndEnabled) return;

            // Content that fits leaves the wheel to the scroll view above.
            if (!CanScroll(0) && !CanScroll(1) && PassToParent(eventData, ExecuteEvents.scrollHandler)) return;

            // The wheel scrolls vertically, or horizontally if the content only scrolls horizontally.
            var delta = eventData.scrollDelta;
            delta.y = -delta.y;
            if (vertical && !horizontal && Mathf.Abs(delta.x) > Mathf.Abs(delta.y)) delta.y = delta.x;

            if (horizontal && !vertical && Mathf.Abs(delta.y) > Mathf.Abs(delta.x)) delta.x = delta.y;

            var target = _position + delta * scrollSensitivity;
            if (movementType == ScrollMovement.Clamped) target -= Overscroll(target);

            _velocity = Vector2.zero;
            SetPosition(target);
        }

        #endregion

        private void SetPosition(Vector2 target)
        {
            if (!horizontal) target.x = _position.x;

            if (!vertical) target.y = _position.y;

            _position = target;
            Apply();
        }

        /// <summary>How far a position is past the ends of the range (negative before the start).</summary>
        private Vector2 Overscroll(Vector2 at)
        {
            var range = ScrollRange;
            return new Vector2(Over(at.x, range.x), Over(at.y, range.y));

            static float Over(float value, float max)
            {
                return value < 0f ? value : value > max ? value - max : 0f;
            }
        }

        private static float RubberDelta(float over, float viewSize)
        {
            return (1f - 1f / (Mathf.Abs(over) * 0.55f / Mathf.Max(viewSize, 1f) + 1f)) * viewSize * Mathf.Sign(over);
        }

        protected virtual void LateUpdate()
        {
            if (content == null) return;

            var dt = Time.unscaledDeltaTime;
            if (!_dragging && dt > 0f)
            {
                var over = Overscroll(_position);
                var target = _position;
                for (var axis = 0; axis < 2; axis++)
                    if (movementType == ScrollMovement.Elastic && over[axis] != 0f)
                    {
                        // Springs back toward the edge.
                        var v = _velocity[axis];
                        target[axis] = Mathf.SmoothDamp(_position[axis], _position[axis] - over[axis], ref v,
                            Mathf.Max(elasticity, 1e-3f), Mathf.Infinity, dt);
                        if (Mathf.Abs(v) < 1f) v = 0f;

                        _velocity[axis] = v;
                    }
                    else if (inertia && _velocity[axis] != 0f)
                    {
                        _velocity[axis] *= Mathf.Pow(decelerationRate, dt);
                        if (Mathf.Abs(_velocity[axis]) < 1f) _velocity[axis] = 0f;

                        target[axis] += _velocity[axis] * dt;
                    }
                    else
                    {
                        _velocity[axis] = 0f;
                    }

                if (movementType == ScrollMovement.Clamped)
                {
                    var clampedOver = Overscroll(target);
                    if (clampedOver.x != 0f) _velocity.x = 0f;

                    if (clampedOver.y != 0f) _velocity.y = 0f;

                    target -= clampedOver;
                }

                if (target != _position) SetPosition(target);
            }

            if (_dragging && inertia && dt > 0f)
            {
                // The velocity of the drag, smoothed.
                var current = (_position - _previousPositionForVelocity) / dt;
                _velocity = Vector2.Lerp(_velocity, current, dt * 10f);
            }

            _previousPositionForVelocity = _position;

            // The range changes when the content or the viewport is laid out again.
            if (ScrollRange != _previousRange) Apply();
        }

        [NonSerialized] private Vector2 _previousPositionForVelocity;

        /// <summary>Moves the content and updates the scrollbars and the listeners.</summary>
        private void Apply()
        {
            if (content == null) return;

            var range = ScrollRange;
            if (movementType == ScrollMovement.Clamped) _position -= Overscroll(_position);

            var translate = -_position;
            if (content.Translate != translate) content.Translate = translate;

            var normalized = NormalizedPosition;
            _updatingScrollbars = true;
            try
            {
                var view = Viewport.LayoutRect.size;
                var size = content.LayoutRect.size;
                if (horizontalScrollbar != null)
                {
                    horizontalScrollbar.Size = size.x > 0f
                        ? Mathf.Clamp01((view.x - Mathf.Abs(Overscroll(_position).x)) / size.x)
                        : 1f;
                    horizontalScrollbar.Value = normalized.x;
                }

                if (verticalScrollbar != null)
                {
                    verticalScrollbar.Size = size.y > 0f
                        ? Mathf.Clamp01((view.y - Mathf.Abs(Overscroll(_position).y)) / size.y)
                        : 1f;
                    verticalScrollbar.Value = normalized.y;
                }
            }
            finally
            {
                _updatingScrollbars = false;
            }

            if (_position != _previousPosition)
            {
                _previousPosition = _position;
                onValueChanged.Invoke(normalized);
            }

            _previousRange = range;
        }
    }
}