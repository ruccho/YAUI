using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using Yaui.Core;

namespace Yaui
{
    public enum SelectionState
    {
        Normal,
        Highlighted,
        Pressed,
        Selected,
        Disabled
    }

    public enum SelectableTransition
    {
        None,

        /// <summary>Multiplies the target's colors (<see cref="YauiElement.Tint"/>), fading between states.</summary>
        ColorTint,

        /// <summary>Draws another sprite on the target image (<see cref="YauiImage.OverrideSprite"/>).</summary>
        SpriteSwap,

        /// <summary>Sets a trigger of the Animator on the selectable's GameObject.</summary>
        Animation
    }

    [Serializable]
    public struct TransitionColors
    {
        public Color normal;
        public Color highlighted;
        public Color pressed;
        public Color selected;
        public Color disabled;

        [Range(1f, 5f)] public float colorMultiplier;

        /// <summary>Seconds to fade from one color to the next (unscaled time).</summary>
        public float fadeDuration;

        public static TransitionColors Default => new()
        {
            normal = Color.white,
            highlighted = new Color32(245, 245, 245, 255),
            pressed = new Color32(200, 200, 200, 255),
            selected = new Color32(245, 245, 245, 255),
            disabled = new Color32(200, 200, 200, 128),
            colorMultiplier = 1f,
            fadeDuration = 0.1f
        };
    }

    /// <summary>The sprites of the states besides normal (the target image's own sprite). Null keeps the own sprite.</summary>
    [Serializable]
    public struct TransitionSprites
    {
        public Sprite highlighted;
        public Sprite pressed;
        public Sprite selected;
        public Sprite disabled;
    }

    [Serializable]
    public struct TransitionTriggers
    {
        public string normal;
        public string highlighted;
        public string pressed;
        public string selected;
        public string disabled;

        public static TransitionTriggers Default => new()
        {
            normal = "Normal",
            highlighted = "Highlighted",
            pressed = "Pressed",
            selected = "Selected",
            disabled = "Disabled"
        };
    }

    public enum NavigationMode
    {
        None,

        /// <summary>Automatic, left and right only.</summary>
        Horizontal,

        /// <summary>Automatic, up and down only.</summary>
        Vertical,

        /// <summary>The nearest selectable of the panel in the direction of the move.</summary>
        Automatic,

        /// <summary>The selectables set for each direction.</summary>
        Explicit
    }

    [Serializable]
    public struct SelectableNavigation
    {
        public NavigationMode mode;

        /// <summary>Automatic modes: moving past the last selectable in a direction selects the first.</summary>
        public bool wrapAround;

        /// <summary>Explicit mode: the selectables of each direction. Other modes use them where set.</summary>
        public YauiSelectable up;

        public YauiSelectable down;
        public YauiSelectable left;
        public YauiSelectable right;

        public static SelectableNavigation Default => new() { mode = NavigationMode.Automatic };
    }

    /// <summary>
    /// The base of controls: tracks the pointer and the EventSystem's selection, shows the state with a transition
    /// on a target element (like uGUI's Selectable), and moves the selection with the navigation events (gamepad,
    /// keyboard). Sits next to a <see cref="YauiElement"/>; events on child elements bubble up to it.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(YauiElement))]
    public abstract class YauiSelectable : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler, IMoveHandler, ITicker
    {
        private static readonly List<YauiSelectable> All = new();

        [SerializeField] private bool interactable = true;
        [SerializeField] private SelectableTransition transition = SelectableTransition.ColorTint;

        /// <summary>The element the transition applies to; this GameObject's element when null.</summary>
        [SerializeField] private YauiElement targetElement;

        [SerializeField] private TransitionColors colors = TransitionColors.Default;
        [SerializeField] private TransitionSprites sprites;
        [SerializeField] private TransitionTriggers triggers = TransitionTriggers.Default;
        [SerializeField] private SelectableNavigation navigation = SelectableNavigation.Default;

        [NonSerialized] private YauiElement _element;
        [NonSerialized] private bool _pointerInside;
        [NonSerialized] private bool _pointerDown;
        [NonSerialized] private bool _selected;
        [NonSerialized] private SelectionState _appliedState = (SelectionState)(-1);

        // The tint fade.
        [NonSerialized] private Color _fadeFrom;
        [NonSerialized] private Color _fadeTo;
        [NonSerialized] private float _fadeStart;
        [NonSerialized] private float _fadeDuration;
        [NonSerialized] private YauiElement _fadeTarget;

        // Until when a submit shows the pressed state (unscaled seconds), or 0.
        [NonSerialized] private float _flashUntil;

        /// <summary>All enabled selectables.</summary>
        public static IReadOnlyList<YauiSelectable> AllSelectables => All;

        /// <summary>The element of this GameObject.</summary>
        public YauiElement Element => _element != null ? _element : _element = GetComponent<YauiElement>();

        public bool Interactable
        {
            get => interactable;
            set
            {
                if (interactable == value) return;

                interactable = value;
                if (!interactable && EventSystem.current != null &&
                    EventSystem.current.currentSelectedGameObject == gameObject)
                    EventSystem.current.SetSelectedGameObject(null);

                OnInteractableChanged();
                UpdateState(false);
            }
        }

        public SelectableTransition Transition
        {
            get => transition;
            set
            {
                ClearTransition();
                transition = value;
                UpdateState(true);
            }
        }

        public YauiElement TargetElement
        {
            get => targetElement != null ? targetElement : Element;
            set
            {
                ClearTransition();
                targetElement = value;
                UpdateState(true);
            }
        }

        public TransitionColors Colors
        {
            get => colors;
            set
            {
                colors = value;
                UpdateState(false);
            }
        }

        public TransitionSprites Sprites
        {
            get => sprites;
            set
            {
                sprites = value;
                UpdateState(true);
            }
        }

        public TransitionTriggers Triggers
        {
            get => triggers;
            set => triggers = value;
        }

        public SelectableNavigation Navigation
        {
            get => navigation;
            set => navigation = value;
        }

        /// <summary>Whether the selectable reacts: interactable, enabled and active.</summary>
        public bool IsInteractable => interactable && isActiveAndEnabled;

        public SelectionState CurrentState
        {
            get
            {
                if (!IsInteractable) return SelectionState.Disabled;

                if (_pointerDown || _flashUntil > 0f) return SelectionState.Pressed;

                if (_selected) return SelectionState.Selected;

                return _pointerInside ? SelectionState.Highlighted : SelectionState.Normal;
            }
        }

        /// <summary>Whether the pointer that is down went down on this selectable.</summary>
        protected bool IsPressed => IsInteractable && _pointerDown;

        protected virtual void OnEnable()
        {
            All.Add(this);
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject)
                _selected = true;

            UpdateState(true);
        }

        protected virtual void OnDisable()
        {
            All.Remove(this);
            _pointerInside = false;
            _pointerDown = false;
            _selected = false;
            _flashUntil = 0f;
            ClearTransition();
        }

        protected virtual void OnValidate()
        {
            colors.fadeDuration = Mathf.Max(colors.fadeDuration, 0f);
            if (isActiveAndEnabled) UpdateState(true);
        }

        /// <summary>Makes this the EventSystem's selected object.</summary>
        public virtual void Select()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem != null && !eventSystem.alreadySelecting) eventSystem.SetSelectedGameObject(gameObject);
        }

        protected virtual void OnInteractableChanged()
        {
        }

        /// <summary>Shows the pressed state for a moment, for presses without a pointer (submit).</summary>
        protected void FlashPressed()
        {
            if (!IsInteractable || !Application.isPlaying) return;

            _flashUntil = Time.realtimeSinceStartup + Mathf.Max(colors.fadeDuration, 0.05f);
            UpdateState(false);
            Tickers.Add(this);
        }

        #region Events

        public virtual void OnPointerEnter(PointerEventData eventData)
        {
            _pointerInside = true;
            UpdateState(false);
        }

        public virtual void OnPointerExit(PointerEventData eventData)
        {
            _pointerInside = false;
            UpdateState(false);
        }

        public virtual void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;

            // Pointer presses select, unless the selectable takes no part in the navigation.
            if (IsInteractable && navigation.mode != NavigationMode.None && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(gameObject, eventData);

            _pointerDown = true;
            UpdateState(false);
        }

        public virtual void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;

            _pointerDown = false;
            UpdateState(false);
        }

        public virtual void OnSelect(BaseEventData eventData)
        {
            _selected = true;
            UpdateState(false);
        }

        public virtual void OnDeselect(BaseEventData eventData)
        {
            _selected = false;
            UpdateState(false);
        }

        public virtual void OnMove(AxisEventData eventData)
        {
            var next = eventData.moveDir switch
            {
                MoveDirection.Left => FindSelectableOnLeft(),
                MoveDirection.Right => FindSelectableOnRight(),
                MoveDirection.Up => FindSelectableOnUp(),
                MoveDirection.Down => FindSelectableOnDown(),
                _ => null
            };

            if (next != null && next.IsInteractable) eventData.selectedObject = next.gameObject;
        }

        #endregion

        #region Navigation

        public virtual YauiSelectable FindSelectableOnLeft()
        {
            return navigation.left != null || navigation.mode == NavigationMode.Explicit
                ? navigation.left
                : navigation.mode is NavigationMode.Automatic or NavigationMode.Horizontal
                    ? FindSelectable(new float2(-1f, 0f))
                    : null;
        }

        public virtual YauiSelectable FindSelectableOnRight()
        {
            return navigation.right != null || navigation.mode == NavigationMode.Explicit
                ? navigation.right
                : navigation.mode is NavigationMode.Automatic or NavigationMode.Horizontal
                    ? FindSelectable(new float2(1f, 0f))
                    : null;
        }

        public virtual YauiSelectable FindSelectableOnUp()
        {
            return navigation.up != null || navigation.mode == NavigationMode.Explicit
                ? navigation.up
                : navigation.mode is NavigationMode.Automatic or NavigationMode.Vertical
                    ? FindSelectable(new float2(0f, -1f))
                    : null;
        }

        public virtual YauiSelectable FindSelectableOnDown()
        {
            return navigation.down != null || navigation.mode == NavigationMode.Explicit
                ? navigation.down
                : navigation.mode is NavigationMode.Automatic or NavigationMode.Vertical
                    ? FindSelectable(new float2(0f, 1f))
                    : null;
        }

        /// <summary>
        /// The selectable of the same panel that is nearest in <paramref name="direction"/> (canvas space, Y down),
        /// weighted toward the direction like uGUI: the score is the distance along the direction over the squared
        /// distance.
        /// </summary>
        public YauiSelectable FindSelectable(Vector2 direction)
        {
            var dir = math.normalizesafe((float2)direction);
            if (!TryGetCanvasBounds(this, out var own, out var panel)) return null;

            // Starts from the edge of the own box in the direction, so that overlapping boxes still order.
            var origin = own.Center + dir * own.HalfSize;
            YauiSelectable best = null;
            var bestScore = float.NegativeInfinity;
            YauiSelectable farthest = null;
            var farthestDistance = float.NegativeInfinity;
            foreach (var candidate in All)
            {
                if (candidate == this || !candidate.IsInteractable ||
                    candidate.navigation.mode == NavigationMode.None ||
                    !TryGetCanvasBounds(candidate, out var bounds, out var candidatePanel) || candidatePanel != panel)
                    continue;

                var toCandidate = bounds.Center - origin;
                var along = math.dot(dir, toCandidate);
                if (along > 0f)
                {
                    var score = along / math.max(math.lengthsq(toCandidate), 1e-6f);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }
                else if (navigation.wrapAround)
                {
                    // Wrapping around goes to the farthest one on the other side.
                    var distance = -math.dot(dir, bounds.Center - own.Center);
                    if (distance > farthestDistance)
                    {
                        farthestDistance = distance;
                        farthest = candidate;
                    }
                }
            }

            return best != null ? best : farthest;
        }

        private struct CanvasBounds
        {
            public float2 Center;
            public float2 HalfSize;
        }

        private static bool TryGetCanvasBounds(YauiSelectable selectable, out CanvasBounds bounds, out object panel)
        {
            bounds = default;
            panel = null;
            var e = selectable.Element;
            if (e == null || e.NodeSlot <= 0) return false;

            var state = e.PanelState;
            if (state == null || !state.TryGetWorld(e, out var world)) return false;

            var size = (float2)e.LayoutRect.size;
            var center = world.c0 * (size.x * 0.5f) + world.c1 * (size.y * 0.5f) + world.c2;
            var half = math.abs(world.c0) * (size.x * 0.5f) + math.abs(world.c1) * (size.y * 0.5f);
            bounds = new CanvasBounds { Center = center, HalfSize = half };
            panel = state;
            return true;
        }

        #endregion

        #region Transitions

        /// <summary>Shows the current state. Instant skips the fade.</summary>
        protected void UpdateState(bool instant)
        {
            if (!isActiveAndEnabled) return;

            var state = CurrentState;
            if (state == _appliedState && !instant) return;

            _appliedState = state;
            ApplyTransition(state, instant);
        }

        protected virtual void ApplyTransition(SelectionState state, bool instant)
        {
            var target = TargetElement;
            switch (transition)
            {
                case SelectableTransition.ColorTint:
                    FadeTint(target, ColorOf(state) * colors.colorMultiplier, instant ? 0f : colors.fadeDuration);
                    break;
                case SelectableTransition.SpriteSwap:
                    if (target is YauiImage image)
                        image.OverrideSprite = state switch
                        {
                            SelectionState.Highlighted => sprites.highlighted,
                            SelectionState.Pressed => sprites.pressed,
                            SelectionState.Selected => sprites.selected,
                            SelectionState.Disabled => sprites.disabled,
                            _ => null
                        };

                    break;
                case SelectableTransition.Animation:
                    SetTrigger(state switch
                    {
                        SelectionState.Highlighted => triggers.highlighted,
                        SelectionState.Pressed => triggers.pressed,
                        SelectionState.Selected => triggers.selected,
                        SelectionState.Disabled => triggers.disabled,
                        _ => triggers.normal
                    });
                    break;
            }
        }

        private Color ColorOf(SelectionState state)
        {
            return state switch
            {
                SelectionState.Highlighted => colors.highlighted,
                SelectionState.Pressed => colors.pressed,
                SelectionState.Selected => colors.selected,
                SelectionState.Disabled => colors.disabled,
                _ => colors.normal
            };
        }

        private void FadeTint(YauiElement target, Color color, float duration)
        {
            if (target == null) return;

            if (duration <= 0f || !Application.isPlaying)
            {
                _fadeTarget = null;
                target.Tint = color;
                return;
            }

            _fadeTarget = target;
            _fadeFrom = target.Tint;
            _fadeTo = color;
            _fadeStart = Time.realtimeSinceStartup;
            _fadeDuration = duration;
            Tickers.Add(this);
        }

        bool ITicker.Tick(float time)
        {
            if (_flashUntil > 0f && time >= _flashUntil)
            {
                _flashUntil = 0f;
                UpdateState(false);
            }

            var running = _flashUntil > 0f;
            if (_fadeTarget != null)
            {
                var t = Mathf.Clamp01((time - _fadeStart) / _fadeDuration);
                _fadeTarget.Tint = Color.Lerp(_fadeFrom, _fadeTo, t);
                if (t < 1f)
                    running = true;
                else
                    _fadeTarget = null;
            }

            return running;
        }

        private void SetTrigger(string trigger)
        {
            if (!Application.isPlaying || !TryGetComponent<Animator>(out var animator) ||
                !animator.isActiveAndEnabled ||
                animator.runtimeAnimatorController == null || string.IsNullOrEmpty(trigger))
                return;

            animator.ResetTrigger(triggers.normal);
            animator.ResetTrigger(triggers.highlighted);
            animator.ResetTrigger(triggers.pressed);
            animator.ResetTrigger(triggers.selected);
            animator.ResetTrigger(triggers.disabled);
            animator.SetTrigger(trigger);
        }

        /// <summary>Removes what the transition applied to the target.</summary>
        private void ClearTransition()
        {
            Tickers.Remove(this);
            _fadeTarget = null;
            _appliedState = (SelectionState)(-1);
            var target = TargetElement;
            if (target == null) return;

            if (transition == SelectableTransition.ColorTint)
                target.Tint = Color.white;
            else if (transition == SelectableTransition.SpriteSwap && target is YauiImage image)
                image.OverrideSprite = null;
        }

        #endregion
    }
}