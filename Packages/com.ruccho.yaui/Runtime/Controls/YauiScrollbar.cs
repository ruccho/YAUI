using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace Yaui
{
    /// <summary>
    /// A selectable with a handle whose size is a fraction of its track (the visible part of a scrolled content)
    /// and whose position is a value from 0 to 1. Dragging the handle moves it; pressing the track pages toward the
    /// pointer. The handle is positioned absolutely in its parent (the track) by the scrollbar.
    /// </summary>
    [AddComponentMenu("YAUI/Scrollbar")]
    public class YauiScrollbar : YauiSelectable, IBeginDragHandler, IDragHandler, IInitializePotentialDragHandler
    {
        [Serializable]
        public class ScrollEvent : UnityEvent<float>
        {
        }

        [SerializeField] private YauiElement handle;
        [SerializeField] private TrackDirection direction = TrackDirection.LeftToRight;
        [SerializeField] [Range(0f, 1f)] private float value;
        [SerializeField] [Range(0f, 1f)] private float size = 0.2f;

        /// <summary>The number of positions the value snaps to, or 0 (or 1) for none.</summary>
        [SerializeField] [Range(0, 11)] private int numberOfSteps;

        [SerializeField] private ScrollEvent onValueChanged = new();

        [NonSerialized] private float _grabOffset;
        [NonSerialized] private TrackDirection? _appliedDirection;
        [NonSerialized] private bool _dragging;

        public YauiElement Handle
        {
            get => handle;
            set
            {
                handle = value;
                UpdateVisuals();
            }
        }

        public TrackDirection Direction
        {
            get => direction;
            set
            {
                direction = value;
                UpdateVisuals();
            }
        }

        public float Value
        {
            get => Snap(value);
            set => Set(value, true);
        }

        /// <summary>The length of the handle as a fraction of the track.</summary>
        public float Size
        {
            get => size;
            set
            {
                var clamped = Mathf.Clamp01(value);
                if (clamped == size) return;

                size = clamped;
                UpdateVisuals();
            }
        }

        public int NumberOfSteps
        {
            get => numberOfSteps;
            set
            {
                numberOfSteps = Mathf.Max(value, 0);
                Set(this.value, true);
            }
        }

        public ScrollEvent OnValueChanged
        {
            get => onValueChanged;
            set => onValueChanged = value ?? new ScrollEvent();
        }

        public void SetValueWithoutNotify(float input)
        {
            Set(input, false);
        }

        private YauiElement TrackElement => (handle != null ? Track.ParentOf(handle) : null) ?? Element;

        protected override void OnEnable()
        {
            base.OnEnable();
            UpdateVisuals();
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            if (isActiveAndEnabled) UpdateVisuals();
        }

        private float Snap(float input)
        {
            var clamped = Mathf.Clamp01(input);
            if (numberOfSteps > 1) clamped = Mathf.Round(clamped * (numberOfSteps - 1)) / (numberOfSteps - 1);

            return clamped;
        }

        private void Set(float input, bool notify)
        {
            var snapped = Snap(input);
            var changed = snapped != Snap(value);
            value = Mathf.Clamp01(input);
            UpdateVisuals();
            if (changed && notify) onValueChanged.Invoke(snapped);
        }

        private void UpdateVisuals()
        {
            // The inspector may change the direction: what the other axis had is cleared.
            if (_appliedDirection is { } previous && previous != direction) Track.ClearAxis(handle, previous, direction);

            _appliedDirection = direction;
            if (handle != null) Track.Place(handle, direction, Value * (1f - size), size, 0f, true);
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            if (!IsInteractable || eventData.button != PointerEventData.InputButton.Left) return;

            _grabOffset = 0f;
            _dragging = false;
            if (handle != null && handle.ScreenToLocal(eventData.position, out var inHandle) &&
                new Rect(Vector2.zero, handle.LayoutRect.size).Contains(inHandle))
            {
                // Grabbing the handle keeps the point under the pointer.
                _grabOffset = Track.Along(direction, inHandle);
                if (Track.IsReversed(direction)) _grabOffset -= Track.Along(direction, handle.LayoutRect.size);

                _dragging = true;
                return;
            }

            // Pressing the track pages toward the pointer.
            if (Track.TryFractionAt(TrackElement, direction, eventData.position, 0f, out var fraction))
            {
                var start = Value * (1f - size);
                if (fraction < start)
                    Value -= Page;
                else if (fraction > start + size) Value += Page;
            }
        }

        /// <summary>A page: the visible part, as a change of the value.</summary>
        private float Page => size >= 1f ? 1f : size / (1f - size);

        public virtual void OnInitializePotentialDrag(PointerEventData eventData)
        {
            eventData.useDragThreshold = false;
        }

        public virtual void OnBeginDrag(PointerEventData eventData)
        {
        }

        public virtual void OnDrag(PointerEventData eventData)
        {
            if (!IsInteractable || !_dragging || eventData.button != PointerEventData.InputButton.Left) return;

            // The fraction of the track where the handle starts, over the room it has to move.
            if (Track.TryFractionAt(TrackElement, direction, eventData.position, _grabOffset, out var fraction) &&
                size < 1f)
                Value = Mathf.Clamp01(fraction / (1f - size));
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            base.OnPointerUp(eventData);
            _dragging = false;
        }

        public override void OnMove(AxisEventData eventData)
        {
            if (!IsInteractable)
            {
                base.OnMove(eventData);
                return;
            }

            var vertical = Track.IsVertical(direction);
            var sign = eventData.moveDir switch
            {
                MoveDirection.Right when !vertical => 1f,
                MoveDirection.Left when !vertical => -1f,
                MoveDirection.Up when vertical => 1f,
                MoveDirection.Down when vertical => -1f,
                _ => 0f
            };

            if (sign == 0f)
            {
                base.OnMove(eventData);
                return;
            }

            if (direction is TrackDirection.RightToLeft or TrackDirection.TopToBottom) sign = -sign;

            Value += sign * (numberOfSteps > 1 ? 1f / (numberOfSteps - 1) : 0.1f);
        }

        public override YauiSelectable FindSelectableOnLeft()
        {
            return Navigation.mode == NavigationMode.Automatic && !Track.IsVertical(direction)
                ? null
                : base.FindSelectableOnLeft();
        }

        public override YauiSelectable FindSelectableOnRight()
        {
            return Navigation.mode == NavigationMode.Automatic && !Track.IsVertical(direction)
                ? null
                : base.FindSelectableOnRight();
        }

        public override YauiSelectable FindSelectableOnUp()
        {
            return Navigation.mode == NavigationMode.Automatic && Track.IsVertical(direction)
                ? null
                : base.FindSelectableOnUp();
        }

        public override YauiSelectable FindSelectableOnDown()
        {
            return Navigation.mode == NavigationMode.Automatic && Track.IsVertical(direction)
                ? null
                : base.FindSelectableOnDown();
        }
    }
}