using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using Yaui.Core;

namespace Yaui
{
    public enum ToggleTransition
    {
        /// <summary>The graphic appears and disappears at once.</summary>
        None,

        /// <summary>The graphic fades in and out.</summary>
        Fade
    }

    /// <summary>
    /// A selectable that switches on and off when clicked or submitted, showing <see cref="Graphic"/> (through its
    /// opacity) while on. Toggles of a <see cref="YauiToggleGroup"/> are exclusive.
    /// </summary>
    [AddComponentMenu("YAUI/Toggle")]
    public class YauiToggle : YauiSelectable, IPointerClickHandler, ISubmitHandler
    {
        [Serializable]
        public class ToggleEvent : UnityEvent<bool>
        {
        }

        [SerializeField] private bool isOn = true;

        /// <summary>Shown while on (the checkmark). Its opacity is driven by the toggle.</summary>
        [SerializeField] private YauiElement graphic;

        [SerializeField] private ToggleTransition toggleTransition = ToggleTransition.Fade;
        [SerializeField] private YauiToggleGroup group;
        [SerializeField] private ToggleEvent onValueChanged = new();

        [NonSerialized] private OpacityFade _fade;

        public bool IsOn
        {
            get => isOn;
            set => Set(value, true);
        }

        public YauiElement Graphic
        {
            get => graphic;
            set
            {
                graphic = value;
                ShowGraphic(true);
            }
        }

        public ToggleTransition ToggleTransition
        {
            get => toggleTransition;
            set => toggleTransition = value;
        }

        public YauiToggleGroup Group
        {
            get => group;
            set
            {
                if (group == value) return;

                if (group != null && isActiveAndEnabled) group.Unregister(this);

                group = value;
                if (group != null && isActiveAndEnabled)
                {
                    group.Register(this);
                    if (isOn) group.NotifyOn(this);
                }
            }
        }

        public ToggleEvent OnValueChanged
        {
            get => onValueChanged;
            set => onValueChanged = value ?? new ToggleEvent();
        }

        /// <summary>Switches without invoking <see cref="OnValueChanged"/>.</summary>
        public void SetIsOnWithoutNotify(bool value)
        {
            Set(value, false);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (group != null)
            {
                group.Register(this);
                if (isOn) group.NotifyOn(this);
            }

            ShowGraphic(true);
        }

        protected override void OnDisable()
        {
            if (group != null) group.Unregister(this);

            _fade?.Stop();
            base.OnDisable();
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            if (isActiveAndEnabled) ShowGraphic(true);
        }

        private void Set(bool value, bool notify)
        {
            if (isOn == value) return;

            // A group that does not allow all toggles off keeps the last one on.
            if (!value && group != null && isActiveAndEnabled && !group.AllowSwitchOff && group.IsOnlyOn(this)) return;

            isOn = value;
            if (isOn && group != null && isActiveAndEnabled) group.NotifyOn(this);

            ShowGraphic(toggleTransition == ToggleTransition.None);
            if (notify) onValueChanged.Invoke(isOn);
        }

        private void ShowGraphic(bool instant)
        {
            if (graphic == null) return;

            var target = isOn ? 1f : 0f;
            if (instant || !Application.isPlaying)
            {
                _fade?.Stop();
                graphic.Opacity = target;
                return;
            }

            _fade ??= new OpacityFade();
            _fade.Start(graphic, target, 0.1f);
        }

        public virtual void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && IsInteractable) IsOn = !isOn;
        }

        public virtual void OnSubmit(BaseEventData eventData)
        {
            if (IsInteractable)
            {
                IsOn = !isOn;
                FlashPressed();
            }
        }
    }

    /// <summary>Fades the opacity of an element.</summary>
    internal sealed class OpacityFade : ITicker
    {
        private YauiElement _target;
        private float _from;
        private float _to;
        private float _start;
        private float _duration;

        public void Start(YauiElement element, float opacity, float seconds)
        {
            _target = element;
            _from = element.Opacity;
            _to = opacity;
            _start = Time.realtimeSinceStartup;
            _duration = Mathf.Max(seconds, 1e-4f);
            Tickers.Add(this);
        }

        public void Stop()
        {
            Tickers.Remove(this);
            _target = null;
        }

        public bool Tick(float time)
        {
            if (_target == null) return false;

            var t = Mathf.Clamp01((time - _start) / _duration);
            _target.Opacity = Mathf.Lerp(_from, _to, t);
            return t < 1f;
        }
    }
}