using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using Yaui.Core;

namespace Yaui
{
    /// <summary>
    /// A selectable that picks one of its options from a list, like uGUI's Dropdown. The list is a copy of
    /// <see cref="Template"/> (an inactive element, typically a scroll view) shown on top of the panel below the
    /// dropdown, with a copy of the template's item (the toggle around <see cref="ItemText"/>) for each option.
    /// Presses outside the list close it.
    /// </summary>
    [AddComponentMenu("YAUI/Dropdown")]
    public class YauiDropdown : YauiSelectable, IPointerClickHandler, ISubmitHandler, ICancelHandler
    {
        [Serializable]
        public class OptionData
        {
            public string text;
            public Sprite image;

            public OptionData()
            {
            }

            public OptionData(string text, Sprite image = null)
            {
                this.text = text;
                this.image = image;
            }
        }

        [Serializable]
        public class DropdownEvent : UnityEvent<int>
        {
        }

        /// <summary>The list, inactive: an element with the item somewhere inside.</summary>
        [SerializeField] private YauiElement template;

        /// <summary>Shows the text of the selected option.</summary>
        [SerializeField] private YauiText captionText;

        /// <summary>Shows the image of the selected option, if any.</summary>
        [SerializeField] private YauiImage captionImage;

        /// <summary>The text of the item in the template. The toggle around it is the item.</summary>
        [SerializeField] private YauiText itemText;

        [SerializeField] private YauiImage itemImage;
        [SerializeField] private List<OptionData> options = new();
        [SerializeField] private int value;
        [SerializeField] private DropdownEvent onValueChanged = new();

        [NonSerialized] private GameObject _list;
        [NonSerialized] private GameObject _blocker;
        [NonSerialized] private readonly List<YauiToggle> _items = new();

        public YauiElement Template
        {
            get => template;
            set => template = value;
        }

        public YauiText CaptionText
        {
            get => captionText;
            set
            {
                captionText = value;
                RefreshShownValue();
            }
        }

        public YauiImage CaptionImage
        {
            get => captionImage;
            set
            {
                captionImage = value;
                RefreshShownValue();
            }
        }

        public YauiText ItemText
        {
            get => itemText;
            set => itemText = value;
        }

        public YauiImage ItemImage
        {
            get => itemImage;
            set => itemImage = value;
        }

        /// <summary>The options. Call <see cref="RefreshShownValue"/> after changing them in place.</summary>
        public List<OptionData> Options
        {
            get => options;
            set
            {
                options = value ?? new List<OptionData>();
                RefreshShownValue();
            }
        }

        /// <summary>The index of the selected option.</summary>
        public int Value
        {
            get => value;
            set => Set(value, true);
        }

        public DropdownEvent OnValueChanged
        {
            get => onValueChanged;
            set => onValueChanged = value ?? new DropdownEvent();
        }

        /// <summary>The shown list and its items (tests).</summary>
        internal GameObject ListObject => _list;

        internal IReadOnlyList<YauiToggle> Items => _items;

        internal GameObject BlockerObject => _blocker;

        /// <summary>Whether the list is shown.</summary>
        public bool IsExpanded => _list != null;

        public void SetValueWithoutNotify(int input)
        {
            Set(input, false);
        }

        public void AddOptions(IEnumerable<string> texts)
        {
            foreach (var text in texts) options.Add(new OptionData(text));

            RefreshShownValue();
        }

        public void AddOptions(IEnumerable<OptionData> data)
        {
            options.AddRange(data);
            RefreshShownValue();
        }

        public void ClearOptions()
        {
            options.Clear();
            value = 0;
            RefreshShownValue();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            RefreshShownValue();
        }

        protected override void OnDisable()
        {
            Hide();
            base.OnDisable();
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            if (isActiveAndEnabled) RefreshShownValue();
        }

        private void Set(int input, bool notify)
        {
            var clamped = options.Count == 0 ? 0 : Mathf.Clamp(input, 0, options.Count - 1);
            if (clamped == value) return;

            value = clamped;
            RefreshShownValue();
            if (notify) onValueChanged.Invoke(value);
        }

        /// <summary>Shows the selected option in the caption.</summary>
        public void RefreshShownValue()
        {
            var option = value >= 0 && value < options.Count ? options[value] : null;
            if (captionText != null) captionText.Text = option?.text ?? "";

            if (captionImage != null)
            {
                captionImage.Sprite = option?.image;
                captionImage.Opacity = option?.image != null ? 1f : 0f;
            }
        }

        #region Events

        public virtual void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) Show();
        }

        public virtual void OnSubmit(BaseEventData eventData)
        {
            Show();
        }

        public virtual void OnCancel(BaseEventData eventData)
        {
            Hide();
        }

        #endregion

        /// <summary>Shows the list below the dropdown (above it if there is no room below).</summary>
        public void Show()
        {
            if (_list != null || !IsInteractable || template == null || itemText == null ||
                !itemText.transform.IsChildOf(template.transform))
                return;

            var state = Element.PanelState;
            if (state == null || state.Panel == null || !state.TryGetWorld(Element, out var world)) return;

            var panel = state.Panel;
            var root = panel.Element;
            var size = Element.LayoutRect.size;

            // Behind the list, a blocker takes the presses outside it.
            _blocker = new GameObject("Dropdown Blocker") { hideFlags = HideFlags.HideAndDontSave };
            _blocker.transform.SetParent(root.transform, false);
            var blockerElement = _blocker.AddComponent<DropdownBlocker>();
            blockerElement.Dropdown = this;
            var blockerLayout = blockerElement.Layout;
            blockerLayout.position = PositionType.Absolute;
            blockerLayout.inset = new Edges(Length.Points(0f));
            blockerElement.Layout = blockerLayout;

            // The list is a copy of the template at the end of the panel, drawn on top of everything.
            _list = Instantiate(template.gameObject, root.transform, false);
            _list.name = "Dropdown List";
            _list.hideFlags = HideFlags.HideAndDontSave;
            var listElement = _list.GetComponent<YauiElement>();

            // Canvas position of the dropdown's bottom-left, in the root's padding box.
            var border = root.Box.borderWidth;
            var topLeft = (Vector2)world.c2 - new Vector2(border, border);
            var bottom = topLeft.y + ((Vector2)world.c1).y * size.y;
            var layout = listElement.Layout;
            layout.position = PositionType.Absolute;
            if (layout.width.unit == LengthUnit.Auto) layout.width = Length.Points(((Vector2)world.c0).x * size.x);

            var height = layout.height.unit == LengthUnit.Point ? layout.height.value : 0f;
            var above = height > 0f && bottom + height > panel.CanvasSize.y && topLeft.y - height >= 0f;
            layout.inset = new Edges(Length.Points(topLeft.x), Length.Points(above ? topLeft.y - height : bottom),
                Length.Auto, Length.Auto);
            listElement.Layout = layout;

            // The item: the toggle around the item text, copied for each option.
            var itemTextCopy = Corresponding(itemText, template.transform, _list.transform);
            var itemImageCopy = itemImage != null && itemImage.transform.IsChildOf(template.transform)
                ? Corresponding(itemImage, template.transform, _list.transform)
                : null;
            var itemToggle = itemTextCopy != null ? itemTextCopy.GetComponentInParent<YauiToggle>(true) : null;
            if (itemToggle == null)
            {
                Debug.LogWarning("[YAUI] The item text of the dropdown template must be inside a toggle.", this);
                Hide();
                return;
            }

            var itemObject = itemToggle.gameObject;
            for (var i = 0; i < options.Count; i++)
            {
                var copy = Instantiate(itemObject, itemObject.transform.parent, false);
                copy.name = $"Item {i}: {options[i].text}";
                var toggle = copy.GetComponent<YauiToggle>();
                var text = Corresponding(itemTextCopy, itemObject.transform, copy.transform);
                if (text != null) text.Text = options[i].text;

                if (itemImageCopy != null)
                {
                    var image = Corresponding(itemImageCopy, itemObject.transform, copy.transform);
                    if (image != null)
                    {
                        image.Sprite = options[i].image;
                        image.Opacity = options[i].image != null ? 1f : 0f;
                    }
                }

                toggle.Group = null;
                toggle.SetIsOnWithoutNotify(i == value);
                var index = i;
                toggle.OnValueChanged.AddListener(_ => OnItemSelected(index));
                copy.AddComponent<DropdownItem>().Dropdown = this;
                copy.SetActive(true);
                _items.Add(toggle);
            }

            itemObject.SetActive(false);

            // The keys move through the items, in order.
            for (var i = 0; i < _items.Count; i++)
                _items[i].Navigation = new SelectableNavigation
                {
                    mode = NavigationMode.Explicit,
                    up = i > 0 ? _items[i - 1] : null,
                    down = i + 1 < _items.Count ? _items[i + 1] : null
                };

            _list.SetActive(true);
            if (value < _items.Count)
            {
                var selected = _items[value];
                EventSystem.current?.SetSelectedGameObject(selected.gameObject);
                var scrollView = _list.GetComponentInChildren<YauiScrollView>();
                if (scrollView != null) Tickers.Add(new ScrollWhenLaidOut(scrollView, selected.Element));
            }
        }

        /// <summary>Hides the list.</summary>
        public void Hide()
        {
            _items.Clear();
            DestroyObject(_list);
            DestroyObject(_blocker);
            _list = null;
            _blocker = null;
        }

        private void OnItemSelected(int index)
        {
            Value = index;
            Hide();
            Select();
        }

        private static void DestroyObject(GameObject go)
        {
            if (go == null) return;

            if (Application.isPlaying)
            {
                // Inactive right away: destroying waits for the end of the frame.
                go.SetActive(false);
                Destroy(go);
            }
            else
            {
                DestroyImmediate(go);
            }
        }

        /// <summary>The component at the same place in a copy of a hierarchy.</summary>
        private static T Corresponding<T>(T original, Transform originalRoot, Transform copyRoot) where T : Component
        {
            var path = new List<int>();
            for (var t = original.transform; t != originalRoot; t = t.parent)
            {
                if (t == null) return null;

                path.Add(t.GetSiblingIndex());
            }

            var current = copyRoot;
            for (var i = path.Count - 1; i >= 0; i--)
            {
                if (path[i] >= current.childCount) return null;

                current = current.GetChild(path[i]);
            }

            return current.GetComponent<T>();
        }

        /// <summary>Scrolls the list to the selected item once it is laid out.</summary>
        private sealed class ScrollWhenLaidOut : ITicker
        {
            private readonly YauiScrollView _view;
            private readonly YauiElement _target;
            private int _frames;

            public ScrollWhenLaidOut(YauiScrollView view, YauiElement target)
            {
                this._view = view;
                this._target = target;
            }

            public bool Tick(float time)
            {
                if (_view == null || _target == null || ++_frames > 10) return false;

                if (_target.LayoutRect.height <= 0f) return true;

                _view.ScrollIntoView(_target);
                return false;
            }
        }
    }
}