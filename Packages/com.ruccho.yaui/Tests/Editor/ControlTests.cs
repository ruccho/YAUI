using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Yaui.Tests
{
    /// <summary>
    /// Controls driven by synthesized events. The panel is a 1000 x 1000 world space panel seen by an orthographic
    /// camera that renders to a 1000 x 1000 texture, so that a screen position is (x, 1000 - y) of the canvas.
    /// </summary>
    public class ControlTests
    {
        private readonly List<Object> _created = new();
        private YauiPanel _panel;
        private EventSystem _eventSystem;

        [SetUp]
        public void SetUp()
        {
            var texture = new RenderTexture(1000, 1000, 0);
            _created.Add(texture);
            var cameraObject = new GameObject("TestCamera");
            _created.Add(cameraObject);
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 0.5f;
            camera.targetTexture = texture;
            camera.enabled = false;

            var panelObject = new GameObject("TestPanel");
            _created.Add(panelObject);
            _panel = panelObject.AddComponent<YauiPanel>();
            _panel.RenderMode = PanelRenderMode.World;
            _panel.ReferenceResolution = new Vector2(1000f, 1000f);
            _panel.WorldScale = 0.001f;
            _panel.EventCamera = camera;
            var root = _panel.GetComponent<YauiElement>();
            var layout = root.Layout;
            layout.alignItems = FlexAlign.FlexStart;
            root.Layout = layout;

            var eventSystemObject = new GameObject("TestEventSystem");
            _created.Add(eventSystemObject);
            _eventSystem = eventSystemObject.AddComponent<EventSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null)
                    Object.DestroyImmediate(o);

            _created.Clear();
        }

        private YauiElement Root => _panel.GetComponent<YauiElement>();

        private static T Add<T>(Component parent, float width, float height, string name = null) where T : Component
        {
            var go = new GameObject(name ?? typeof(T).Name);
            go.transform.SetParent(parent.transform, false);
            // Elements are the component itself; other components get a plain element.
            var isElement = typeof(YauiElement).IsAssignableFrom(typeof(T));
            var element = isElement
                ? (YauiElement)(Component)go.AddComponent(typeof(T))
                : go.AddComponent<YauiElement>();
            var layout = element.Layout;
            layout.width = width;
            layout.height = height;
            layout.shrink = 0f;
            element.Layout = layout;
            var box = element.Box;
            box.backgroundColor = Color.gray;
            element.Box = box;
            return isElement ? go.GetComponent<T>() : go.AddComponent<T>();
        }

        /// <summary>A pointer at a canvas position.</summary>
        private PointerEventData Pointer(float x, float y)
        {
            return new PointerEventData(_eventSystem)
            {
                position = new Vector2(x, 1000f - y),
                button = PointerEventData.InputButton.Left
            };
        }

        [Test]
        public void ScreenToLocalFollowsTheLayoutAndTheTransform()
        {
            var box = Add<YauiElement>(Root, 200f, 100f);
            var layout = box.Layout;
            layout.margin = new Edges(Length.Points(100f), Length.Points(50f), Length.Auto, Length.Auto);
            box.Layout = layout;
            box.Translate = new Vector2(10f, 20f);
            YauiPanel.ForceUpdate();

            Assert.IsTrue(box.ScreenToLocal(new Vector2(160f, 1000f - 90f), out var local));
            Assert.AreEqual(50f, local.x, 0.01f);
            Assert.AreEqual(20f, local.y, 0.01f);
        }

        [Test]
        public void ButtonClicksOnlyWhenInteractable()
        {
            var button = Add<YauiButton>(Root, 100f, 40f);
            var clicks = 0;
            button.OnClick.AddListener(() => clicks++);
            YauiPanel.ForceUpdate();

            ExecuteEvents.Execute(button.gameObject, Pointer(50f, 20f), ExecuteEvents.pointerClickHandler);
            Assert.AreEqual(1, clicks);

            button.Interactable = false;
            ExecuteEvents.Execute(button.gameObject, Pointer(50f, 20f), ExecuteEvents.pointerClickHandler);
            Assert.AreEqual(1, clicks);
            Assert.AreEqual(SelectionState.Disabled, button.CurrentState);
        }

        [Test]
        public void ColorTintFollowsTheState()
        {
            var button = Add<YauiButton>(Root, 100f, 40f);
            var colors = button.Colors;
            colors.highlighted = Color.red;
            colors.pressed = Color.green;
            colors.disabled = Color.blue;
            button.Colors = colors;
            YauiPanel.ForceUpdate();
            Assert.AreEqual(Color.white, button.Element.Tint);

            // Fades are instant outside play mode.
            button.OnPointerEnter(Pointer(50f, 20f));
            Assert.AreEqual(Color.red, button.Element.Tint);
            button.OnPointerDown(Pointer(50f, 20f));
            Assert.AreEqual(Color.green, button.Element.Tint);
            button.OnPointerUp(Pointer(50f, 20f));
            button.OnPointerExit(Pointer(500f, 500f));
            Assert.AreEqual(Color.white, button.Element.Tint);
            button.Interactable = false;
            Assert.AreEqual(Color.blue, button.Element.Tint);

            // The tint reaches the node record.
            YauiPanel.ForceUpdate();
            var node = Core.YauiSystem.Nodes.Gpu.Read(button.Element.NodeSlot);
            Assert.AreEqual(Rendering.GpuPacking.Rgba8(Color.blue), node.Tint);
        }

        [Test]
        public void TogglesOfAGroupAreExclusive()
        {
            var group = Root.gameObject.AddComponent<YauiToggleGroup>();
            var a = Add<YauiToggle>(Root, 40f, 40f, "A");
            var b = Add<YauiToggle>(Root, 40f, 40f, "B");
            var check = Add<YauiElement>(b, 20f, 20f, "Check");
            b.Graphic = check;
            b.SetIsOnWithoutNotify(false);
            a.Group = group;
            b.Group = group;
            Assert.IsTrue(a.IsOn);
            Assert.IsFalse(b.IsOn);
            Assert.AreEqual(0f, check.Opacity);

            var changes = new List<bool>();
            a.OnValueChanged.AddListener(changes.Add);
            b.OnPointerClick(Pointer(0f, 0f));
            Assert.IsFalse(a.IsOn);
            Assert.IsTrue(b.IsOn);
            Assert.AreEqual(1f, check.Opacity);
            CollectionAssert.AreEqual(new[] { false }, changes);

            // Without switching off, the toggle that is on stays on.
            b.OnPointerClick(Pointer(0f, 0f));
            Assert.IsTrue(b.IsOn);
            group.AllowSwitchOff = true;
            b.OnPointerClick(Pointer(0f, 0f));
            Assert.IsFalse(b.IsOn);
            Assert.IsFalse(group.AnyTogglesOn());
        }

        [Test]
        public void SliderFollowsThePointerAndPlacesItsParts()
        {
            var slider = Add<YauiSlider>(Root, 200f, 20f);
            var fillArea = Add<YauiElement>(slider, 200f, 20f, "FillArea");
            var fill = Add<YauiElement>(fillArea, 0f, 0f, "Fill");
            var handleArea = Add<YauiElement>(slider, 200f, 20f, "HandleArea");
            var fillAreaLayout = fillArea.Layout;
            fillAreaLayout.position = PositionType.Absolute;
            fillArea.Layout = fillAreaLayout;
            var handleAreaLayout = handleArea.Layout;
            handleAreaLayout.position = PositionType.Absolute;
            handleArea.Layout = handleAreaLayout;
            var handle = Add<YauiElement>(handleArea, 20f, 20f, "Handle");
            slider.Fill = fill;
            slider.Handle = handle;
            slider.MinValue = 0f;
            slider.MaxValue = 10f;
            YauiPanel.ForceUpdate();

            // Pressing the track jumps to the pointer.
            slider.OnPointerDown(Pointer(150f, 10f));
            Assert.AreEqual(7.5f, slider.Value, 1e-3f);
            YauiPanel.ForceUpdate();
            Assert.AreEqual(150f, fill.LayoutRect.width, 0.5f);
            Assert.AreEqual(140f, handle.LayoutRect.x, 0.5f, "The handle is centered on the value.");

            // Grabbing the handle off its center keeps the offset while dragging.
            slider.OnPointerUp(Pointer(150f, 10f));
            slider.OnPointerDown(Pointer(155f, 10f));
            Assert.AreEqual(7.5f, slider.Value, 1e-3f);
            slider.OnDrag(Pointer(55f, 10f));
            Assert.AreEqual(2.5f, slider.Value, 1e-3f);
            slider.OnDrag(Pointer(57f, 10f));

            slider.WholeNumbers = true;
            Assert.AreEqual(3f, slider.Value);
            slider.OnDrag(Pointer(-100f, 10f));
            Assert.AreEqual(0f, slider.Value);
        }

        [Test]
        public void ScrollbarDragsItsHandleAndPages()
        {
            var scrollbar = Add<YauiScrollbar>(Root, 200f, 20f);
            var handle = Add<YauiElement>(scrollbar, 0f, 20f, "Handle");
            scrollbar.Handle = handle;
            scrollbar.Size = 0.25f;
            YauiPanel.ForceUpdate();
            Assert.AreEqual(50f, handle.LayoutRect.width, 0.5f);
            Assert.AreEqual(0f, handle.LayoutRect.x, 0.5f);

            // Pressing the track after the handle pages forward by the visible part.
            scrollbar.OnPointerDown(Pointer(190f, 10f));
            scrollbar.OnPointerUp(Pointer(190f, 10f));
            Assert.AreEqual(1f / 3f, scrollbar.Value, 1e-4f);
            YauiPanel.ForceUpdate();
            Assert.AreEqual(50f, handle.LayoutRect.x, 0.5f);

            scrollbar.OnPointerDown(Pointer(60f, 10f));
            scrollbar.OnDrag(Pointer(160f, 10f));
            Assert.AreEqual(1f, scrollbar.Value, 1e-4f);
        }

        [Test]
        public void ScrollViewMovesItsContentAndScrollbar()
        {
            var view = Add<YauiScrollView>(Root, 200f, 100f);
            view.Viewport.ClipChildren = true;
            var content = Add<YauiElement>(view, 200f, 400f, "Content");
            var contentLayout = content.Layout;
            contentLayout.position = PositionType.Absolute;
            content.Layout = contentLayout;
            var bar = Add<YauiScrollbar>(Root, 20f, 100f, "Bar");
            var handle = Add<YauiElement>(bar, 20f, 0f, "Handle");
            bar.Handle = handle;
            bar.Direction = TrackDirection.TopToBottom;
            view.Content = content;
            view.VerticalScrollbar = bar;
            view.Horizontal = false;
            view.MovementType = ScrollMovement.Clamped;
            YauiPanel.ForceUpdate();

            Assert.AreEqual(new Vector2(0f, 300f), view.ScrollRange);
            view.NormalizedPosition = new Vector2(0f, 0.5f);
            Assert.AreEqual(new Vector2(0f, -150f), content.Translate);
            Assert.AreEqual(0.25f, bar.Size, 1e-4f);
            Assert.AreEqual(0.5f, bar.Value, 1e-4f);
            YauiPanel.ForceUpdate();
            // The vertical handle fills the track across, after being placed horizontally first.
            Assert.AreEqual(20f, handle.LayoutRect.width, 0.5f);
            Assert.AreEqual(25f, handle.LayoutRect.height, 1f);
            Assert.AreEqual(37.5f, handle.LayoutRect.y, 1f);

            // Clamped at the end, and hit tests see the moved content.
            view.ScrollPosition = new Vector2(0f, 1000f);
            Assert.AreEqual(300f, view.ScrollPosition.y);
            YauiPanel.ForceUpdate();
            Assert.IsTrue(content.ScreenToLocal(new Vector2(10f, 1000f - 10f), out var local));
            Assert.AreEqual(310f, local.y, 0.01f);
        }

        [Test]
        public void AutomaticNavigationFindsTheNearestInTheDirection()
        {
            var row = Add<YauiElement>(Root, 600f, 200f, "Row");
            var rowLayout = row.Layout;
            rowLayout.direction = FlexDirection.Row;
            rowLayout.wrap = FlexWrap.Wrap;
            rowLayout.alignContent = FlexAlign.FlexStart;
            row.Layout = rowLayout;
            var a = Add<YauiButton>(row, 100f, 50f, "A");
            var b = Add<YauiButton>(row, 100f, 50f, "B");
            var c = Add<YauiButton>(row, 100f, 50f, "C");
            var spacer = Add<YauiElement>(row, 300f, 50f, "Spacer");
            spacer.Box = BoxStyle.Default;
            var below = Add<YauiButton>(row, 100f, 50f, "Below");
            YauiPanel.ForceUpdate();

            Assert.AreSame(b, a.FindSelectableOnRight());
            Assert.AreSame(c, b.FindSelectableOnRight());
            Assert.AreSame(a, b.FindSelectableOnLeft());
            Assert.AreSame(below, a.FindSelectableOnDown());
            Assert.IsNull(a.FindSelectableOnUp());

            var navigation = a.Navigation;
            navigation.mode = NavigationMode.Explicit;
            navigation.right = c;
            a.Navigation = navigation;
            Assert.AreSame(c, a.FindSelectableOnRight());
            Assert.IsNull(a.FindSelectableOnDown());
        }

        private YauiInputField CreateInputField(out YauiText text)
        {
            var field = Add<YauiInputField>(Root, 300f, 60f);
            field.Element.ClipChildren = true;
            text = Add<YauiText>(field, 0f, 0f, "Text");
            // A single line as wide as its text, scrolled inside the field.
            var textLayout = LayoutStyle.Default;
            textLayout.alignSelf = FlexAlign.FlexStart;
            text.Layout = textLayout;
            text.FontSize = 32f;
            text.Box = BoxStyle.Default;
            field.TextComponent = text;
            field.SelectAllOnFocus = false;
            return field;
        }

        private static void Type(YauiInputField field, string characters)
        {
            foreach (var c in characters) field.ProcessKey(new Event { type = EventType.KeyDown, character = c });
        }

        private static bool Key(YauiInputField field, string key)
        {
            return field.ProcessKey(Event.KeyboardEvent(key));
        }

        [Test]
        public void InputFieldEditsItsText()
        {
            var field = CreateInputField(out var text);
            var changes = new List<string>();
            field.OnValueChanged.AddListener(changes.Add);
            field.ActivateInputField();
            Assert.IsTrue(field.IsFocused);

            Type(field, "Hello");
            Assert.AreEqual("Hello", field.Text);
            Assert.AreEqual("Hello", text.Text);
            Assert.AreEqual(5, field.CaretPosition);
            Assert.AreEqual(5, changes.Count);

            Key(field, "backspace");
            Key(field, "#left");
            Key(field, "#left");
            Assert.AreEqual("ll", field.SelectedText);
            Type(field, "y");
            Assert.AreEqual("Hey", field.Text);

            Key(field, "home");
            Type(field, "Oh ");
            Assert.AreEqual("Oh Hey", field.Text);
            Key(field, "^a");
            Assert.AreEqual("Oh Hey", field.SelectedText);

            // Escape brings back the text from before editing.
            var ended = new List<string>();
            field.OnEndEdit.AddListener(ended.Add);
            Assert.IsFalse(Key(field, "escape"));
            Assert.IsFalse(field.IsFocused);
            Assert.AreEqual("", field.Text);
            CollectionAssert.AreEqual(new[] { "" }, ended);
        }

        [Test]
        public void InputFieldSubmitsAndValidates()
        {
            var field = CreateInputField(out var text);
            field.ContentType = InputContentType.IntegerNumber;
            field.CharacterLimit = 4;
            var submitted = new List<string>();
            field.OnSubmitText.AddListener(submitted.Add);
            field.ActivateInputField();

            Type(field, "-12a-345");
            Assert.AreEqual("-123", field.Text);

            // A single line ends with Enter, without a new line.
            Assert.IsFalse(Key(field, "return"));
            CollectionAssert.AreEqual(new[] { "-123" }, submitted);
            Assert.IsFalse(field.IsFocused);

            field.ContentType = InputContentType.Password;
            field.CharacterLimit = 0;
            field.ActivateInputField();
            Type(field, "secret");
            Assert.AreEqual("-123secret", field.Text);
            Assert.AreEqual(new string('*', 10), text.Text);
        }

        [Test]
        public void InputFieldPlacesTheCaretFromTheGeneration()
        {
            var field = CreateInputField(out var text);
            field.ActivateInputField();
            Type(field, "Hello");
            YauiPanel.ForceUpdate();

            var caret = field.CaretElement;
            Assert.IsNotNull(caret);
            var end = caret.RenderTransform.translate.x;
            Assert.AreEqual(text.LayoutRect.width, end, 2f, "The caret is at the end of the text.");
            Assert.Greater(caret.RenderTransform.scale.y, 20f, "The caret is as tall as the line.");

            Key(field, "home");
            Assert.AreEqual(0f, caret.RenderTransform.translate.x, 1f);

            // A press on the text puts the caret at the nearest character boundary.
            field.OnPointerDown(Pointer(end - 1f, 20f));
            Assert.AreEqual(5, field.CaretPosition);

            Key(field, "#left");
            Key(field, "#left");
            Assert.AreEqual(1, field.VisibleSelectionBoxes);

            // A long text scrolls to keep the caret in the field.
            Type(field, " world, and a much longer text than the field");
            YauiPanel.ForceUpdate();
            Assert.Less(text.Translate.x, 0f);
            Assert.LessOrEqual(caret.RenderTransform.translate.x, 300f,
                $"text {text.LayoutRect} translate {text.Translate} caret {field.CaretPosition}/{field.Text.Length} rendered {text.RenderedLength}");
        }

        private YauiDropdown CreateDropdown(out YauiText caption)
        {
            var spacer = Add<YauiElement>(Root, 100f, 50f, "Spacer");
            spacer.Box = BoxStyle.Default;
            var dropdown = Add<YauiDropdown>(Root, 200f, 40f);
            var dropdownLayout = dropdown.Element.Layout;
            dropdownLayout.margin = new Edges(Length.Points(30f), Length.Points(0f), Length.Auto, Length.Auto);
            dropdown.Element.Layout = dropdownLayout;
            caption = Add<YauiText>(dropdown, 0f, 0f, "Caption");
            caption.Layout = LayoutStyle.Default;

            var template = Add<YauiElement>(dropdown, 0f, 0f, "Template");
            var templateLayout = LayoutStyle.Default;
            templateLayout.height = 150f;
            template.Layout = templateLayout;
            var item = Add<YauiToggle>(template, 0f, 30f, "Item");
            var itemLayout = item.Element.Layout;
            itemLayout.width = Length.Auto;
            item.Element.Layout = itemLayout;
            var itemText = Add<YauiText>(item, 0f, 0f, "ItemText");
            itemText.Layout = LayoutStyle.Default;
            template.gameObject.SetActive(false);

            dropdown.Template = template;
            dropdown.CaptionText = caption;
            dropdown.ItemText = itemText;
            dropdown.AddOptions(new[] { "Apple", "Banana", "Cherry" });
            return dropdown;
        }

        [Test]
        public void DropdownShowsItsOptionsAndPicks()
        {
            var dropdown = CreateDropdown(out var caption);
            var picked = new List<int>();
            dropdown.OnValueChanged.AddListener(picked.Add);
            YauiPanel.ForceUpdate();
            Assert.AreEqual("Apple", caption.Text);

            dropdown.OnPointerClick(Pointer(100f, 70f));
            Assert.IsTrue(dropdown.IsExpanded);
            Assert.AreEqual(3, dropdown.Items.Count);
            Assert.AreEqual("Item 1: Banana", dropdown.Items[1].name);
            Assert.AreEqual("Banana", dropdown.Items[1].GetComponentInChildren<YauiText>().Text);
            Assert.IsTrue(dropdown.Items[0].IsOn);

            // The list is at the end of the panel, below the dropdown and as wide.
            YauiPanel.ForceUpdate();
            var list = dropdown.ListObject.GetComponent<YauiElement>().LayoutRect;
            Assert.AreEqual(new Rect(30f, 90f, 200f, 150f), list);
            Assert.AreSame(dropdown.ListObject.transform, Root.transform.GetChild(Root.transform.childCount - 1));

            dropdown.Items[2].OnPointerClick(Pointer(0f, 0f));
            Assert.IsFalse(dropdown.IsExpanded);
            Assert.AreEqual(2, dropdown.Value);
            Assert.AreEqual("Cherry", caption.Text);
            CollectionAssert.AreEqual(new[] { 2 }, picked);
        }

        [Test]
        public void DropdownClosesOnPressesOutside()
        {
            var dropdown = CreateDropdown(out _);
            YauiPanel.ForceUpdate();
            dropdown.Show();
            YauiPanel.ForceUpdate();

            // The blocker covers the panel without drawing, and takes the press.
            var hit = HitAt(900f, 900f);
            Assert.AreSame(dropdown.BlockerObject, hit);
            // Outside play mode the list is destroyed at once, so the handler is called directly.
            hit.GetComponent<IPointerClickHandler>().OnPointerClick(Pointer(900f, 900f));
            Assert.IsFalse(dropdown.IsExpanded);
            Assert.AreEqual(0, dropdown.Value);
        }

        private GameObject HitAt(float x, float y)
        {
            var element = _panel.CurrentState.HitTestCanvas(new Unity.Mathematics.float2(x, y));
            return element != null ? element.gameObject : null;
        }

        [Test]
        public void NestedScrollViewsHandOverDragsAcrossTheirAxis()
        {
            var outer = Add<YauiScrollView>(Root, 300f, 200f, "Outer");
            outer.Viewport.ClipChildren = true;
            var outerContent = Add<YauiElement>(outer, 900f, 200f, "OuterContent");
            var outerLayout = outerContent.Layout;
            outerLayout.position = PositionType.Absolute;
            outerContent.Layout = outerLayout;
            outer.Content = outerContent;
            outer.Vertical = false;

            var inner = Add<YauiScrollView>(outerContent, 300f, 200f, "Inner");
            var innerContent = Add<YauiElement>(inner, 300f, 600f, "InnerContent");
            var innerLayout = innerContent.Layout;
            innerLayout.position = PositionType.Absolute;
            innerContent.Layout = innerLayout;
            inner.Content = innerContent;
            inner.Horizontal = false;
            YauiPanel.ForceUpdate();

            // Across the inner view's axis, the drag goes to the outer view and stays there.
            var sideways = Pointer(150f, 100f);
            sideways.pressPosition = sideways.position + new Vector2(40f, 0f);
            sideways.pointerDrag = inner.gameObject;
            inner.OnBeginDrag(sideways);
            Assert.AreSame(outer.gameObject, sideways.pointerDrag);

            // Along it, the inner view scrolls.
            var down = Pointer(150f, 100f);
            down.pressPosition = down.position + new Vector2(0f, 40f);
            down.pointerDrag = inner.gameObject;
            inner.OnBeginDrag(down);
            Assert.AreSame(inner.gameObject, down.pointerDrag);
            inner.OnDrag(Pointer(150f, 60f));
            Assert.AreEqual(40f, inner.ScrollPosition.y, 0.5f);
        }
    }
}