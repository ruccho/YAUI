using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace Yaui
{
    /// <summary>
    /// A selectable that picks a value in a range by dragging along a track, or with the navigation keys along its
    /// direction. The fill and the handle are positioned absolutely in their parents (the tracks) by the slider:
    /// the fill from the start to the value, the handle centered on the value.
    /// </summary>
    [AddComponentMenu("YAUI/Slider")]
    public class YauiSlider : YauiSelectable, IDragHandler, IInitializePotentialDragHandler
    {
        [Serializable]
        public class SliderEvent : UnityEvent<float>
        {
        }

        [SerializeField] private YauiElement fill;
        [SerializeField] private YauiElement handle;
        [SerializeField] private TrackDirection direction = TrackDirection.LeftToRight;
        [SerializeField] private float minValue;
        [SerializeField] private float maxValue = 1f;
        [SerializeField] private bool wholeNumbers;
        [SerializeField] private float value;
        [SerializeField] private SliderEvent onValueChanged = new();

        // Where in the handle the pointer grabbed it, along the axis in canvas units.
        [NonSerialized] private float _grabOffset;
        [NonSerialized] private TrackDirection? _appliedDirection;

        public YauiElement Fill
        {
            get => fill;
            set
            {
                fill = value;
                UpdateVisuals();
            }
        }

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

        public float MinValue
        {
            get => minValue;
            set
            {
                minValue = value;
                Set(this.value, true);
            }
        }

        public float MaxValue
        {
            get => maxValue;
            set
            {
                maxValue = value;
                Set(this.value, true);
            }
        }

        public bool WholeNumbers
        {
            get => wholeNumbers;
            set
            {
                wholeNumbers = value;
                Set(this.value, true);
            }
        }

        public float Value
        {
            get => wholeNumbers ? Mathf.Round(value) : value;
            set => Set(value, true);
        }

        /// <summary>The value as a fraction of the range.</summary>
        public float NormalizedValue
        {
            get => Mathf.Approximately(minValue, maxValue) ? 0f : Mathf.InverseLerp(minValue, maxValue, Value);
            set => Value = Mathf.Lerp(minValue, maxValue, value);
        }

        public SliderEvent OnValueChanged
        {
            get => onValueChanged;
            set => onValueChanged = value ?? new SliderEvent();
        }

        public void SetValueWithoutNotify(float input)
        {
            Set(input, false);
        }

        /// <summary>The element the pointer is mapped on: the parent of the handle, or of the fill.</summary>
        private YauiElement TrackElement =>
            (handle != null ? Track.ParentOf(handle) : null) ?? (fill != null ? Track.ParentOf(fill) : null) ?? Element;

        protected override void OnEnable()
        {
            base.OnEnable();
            Set(value, false);
            UpdateVisuals();
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            if (wholeNumbers)
            {
                minValue = Mathf.Round(minValue);
                maxValue = Mathf.Round(maxValue);
            }

            if (isActiveAndEnabled)
            {
                value = Clamp(value);
                UpdateVisuals();
            }
        }

        private float Clamp(float input)
        {
            var clamped = Mathf.Clamp(input, Mathf.Min(minValue, maxValue), Mathf.Max(minValue, maxValue));
            return wholeNumbers ? Mathf.Round(clamped) : clamped;
        }

        private void Set(float input, bool notify)
        {
            var clamped = Clamp(input);
            var changed = value != clamped;
            value = clamped;
            UpdateVisuals();
            if (changed && notify) onValueChanged.Invoke(clamped);
        }

        private void UpdateVisuals()
        {
            var n = NormalizedValue;
            if (fill != null) Track.Fill(fill, direction, n);

            // The inspector may change the direction: what the other axis had is cleared.
            if (_appliedDirection is { } previous && previous != direction) Track.ClearAxis(handle, previous, direction);

            _appliedDirection = direction;
            if (handle != null) Track.Place(handle, direction, n, -1f, Track.Extent(handle, direction) * 0.5f, false);
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            if (!IsInteractable || eventData.button != PointerEventData.InputButton.Left) return;

            // Grabbing the handle keeps the point under the pointer; elsewhere the value jumps there.
            _grabOffset = 0f;
            if (handle != null && handle.ScreenToLocal(eventData.position, out var inHandle) &&
                new Rect(Vector2.zero, handle.LayoutRect.size).Contains(inHandle))
            {
                _grabOffset = Track.Along(direction, inHandle) - Track.Extent(handle, direction) * 0.5f;
                return;
            }

            MoveTo(eventData.position);
        }

        public virtual void OnInitializePotentialDrag(PointerEventData eventData)
        {
            eventData.useDragThreshold = false;
        }

        public virtual void OnDrag(PointerEventData eventData)
        {
            if (IsInteractable && eventData.button == PointerEventData.InputButton.Left) MoveTo(eventData.position);
        }

        private void MoveTo(Vector2 screenPosition)
        {
            if (Track.TryFractionAt(TrackElement, direction, screenPosition, _grabOffset, out var fraction))
                NormalizedValue = Mathf.Clamp01(fraction);
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

            // The screen direction of the key, in the direction of the value.
            if (direction is TrackDirection.RightToLeft or TrackDirection.TopToBottom) sign = -sign;

            var step = wholeNumbers ? 1f : (maxValue - minValue) * 0.1f;
            Value += sign * step;
        }

        // The keys along the slider change its value instead of moving the selection.
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