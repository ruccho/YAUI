namespace Yaui.Benchmarks.Comparison
{
    // Inside the namespace, so that UI Toolkit's types win over YAUI's of the same name (FlexDirection, Length...).
    using UnityEngine;
    using UnityEngine.UIElements;

    static class UitkBuild
    {
        static Font _font;

        /// <summary>
        /// A document with the runtime theme, scaled like the other systems, and the same font as YAUI.
        /// </summary>
        public static UIDocument CreateDocument(Transform root, ThemeStyleSheet theme, out PanelSettings settings)
        {
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = theme;
            settings.scaleMode = UnityEngine.UIElements.PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = Vector2Int.RoundToInt(ComparisonSpec.ReferenceResolution);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = ComparisonSpec.Match;

            var go = new GameObject("UIDocument");
            go.SetActive(false);
            go.transform.SetParent(root, false);
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = settings;
            go.SetActive(true);

            _font ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var rootElement = document.rootVisualElement;
            rootElement.style.unityFontDefinition = FontDefinition.FromFont(_font);
            return document;
        }

        public static void SetRadius(IStyle style, float radius)
        {
            style.borderTopLeftRadius = radius;
            style.borderTopRightRadius = radius;
            style.borderBottomLeftRadius = radius;
            style.borderBottomRightRadius = radius;
        }

        public static Label CreateLabel(string text, float fontSize)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.fontSize = fontSize;
            label.style.color = Color.black;
            label.style.marginLeft = 0f;
            label.style.marginRight = 0f;
            label.style.marginTop = 0f;
            label.style.marginBottom = 0f;
            label.style.paddingLeft = 0f;
            label.style.paddingRight = 0f;
            label.style.paddingTop = 0f;
            label.style.paddingBottom = 0f;
            return label;
        }
    }

    /// <summary>
    /// The grid with UI Toolkit: fixed-size cells in a wrapping flex row. Usage hints (the counterpart of
    /// uGUI's nested canvas) mark the cells that change.
    /// </summary>
    public sealed class UitkGridScenario : IBenchmarkScenario
    {
        readonly int _cellCount;
        readonly GridMutation _mutation;
        readonly ThemeStyleSheet _theme;
        VisualElement[] _cells;
        Label[] _labels;
        PanelSettings _settings;
        IPanel _panel;

        public string Name { get; }

        public UitkGridScenario(int cellCount, GridMutation mutation, ThemeStyleSheet theme, string name)
        {
            _cellCount = cellCount;
            _mutation = mutation;
            _theme = theme;
            Name = name;
        }

        public void Setup(Transform root)
        {
            var spec = new GridSpec(_cellCount);
            var document = UitkBuild.CreateDocument(root, _theme, out _settings);
            var container = document.rootVisualElement;
            _panel = container.panel;
            container.style.flexDirection = FlexDirection.Row;
            container.style.flexWrap = Wrap.Wrap;
            container.style.alignContent = Align.FlexStart;

            var hints = _mutation switch
            {
                GridMutation.Color => UsageHints.DynamicColor,
                GridMutation.Move => UsageHints.DynamicTransform,
                _ => UsageHints.None,
            };

            _cells = new VisualElement[_cellCount];
            _labels = new Label[_cellCount];
            for (var i = 0; i < _cellCount; i++)
            {
                var cell = new VisualElement();
                var style = cell.style;
                style.width = spec.CellSize.x - ComparisonSpec.CellMargin * 2f;
                style.height = spec.CellSize.y - ComparisonSpec.CellMargin * 2f;
                style.marginLeft = ComparisonSpec.CellMargin;
                style.marginRight = ComparisonSpec.CellMargin;
                style.marginTop = ComparisonSpec.CellMargin;
                style.marginBottom = ComparisonSpec.CellMargin;
                style.flexDirection = FlexDirection.Row;
                style.alignItems = Align.Center;
                style.flexShrink = 0f;
                style.backgroundColor = ComparisonSpec.CellColor(i);
                UitkBuild.SetRadius(style, ComparisonSpec.CellRadius);
                if (i % ComparisonSpec.MutationStride == 0)
                {
                    cell.usageHints = hints;
                }

                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.style.width = spec.IconSize.x;
                icon.style.height = spec.IconSize.y;
                icon.style.marginLeft = spec.IconMargin;
                icon.style.marginRight = spec.IconMargin;
                icon.style.flexShrink = 0f;
                icon.style.backgroundColor = ComparisonSpec.IconColor(i);
                UitkBuild.SetRadius(icon.style, Mathf.Min(spec.IconSize.x, spec.IconSize.y) * 0.5f);
                cell.Add(icon);

                var label = UitkBuild.CreateLabel(ComparisonSpec.Number(i), spec.FontSize);
                label.style.flexGrow = 1f;
                label.style.whiteSpace = WhiteSpace.NoWrap;
                cell.Add(label);

                container.Add(cell);
                _cells[i] = cell;
                _labels[i] = label;
            }
        }

        public void Tick(int frame)
        {
            switch (_mutation)
            {
                case GridMutation.Color:
                    for (var i = 0; i < _cellCount; i += ComparisonSpec.MutationStride)
                    {
                        _cells[i].style.backgroundColor = ComparisonSpec.AnimatedCellColor(i, frame);
                    }

                    break;
                case GridMutation.Move:
                    for (var i = 0; i < _cellCount; i += ComparisonSpec.MutationStride)
                    {
                        _cells[i].style.translate = new Translate(ComparisonSpec.MoveOffset(i, frame), 0f);
                    }

                    break;
                case GridMutation.Text:
                    for (var i = 0; i < _cellCount; i += ComparisonSpec.MutationStride)
                    {
                        _labels[i].text = ComparisonSpec.Number(frame + i);
                    }

                    break;
                case GridMutation.HitTest:
                    for (var i = 0; i < ComparisonSpec.HitTestsPerFrame; i++)
                    {
                        var screen = ComparisonSpec.HitTestPosition(frame, i);
                        // ScreenToPanel takes the screen position from the top.
                        var position = RuntimePanelUtils.ScreenToPanel(_panel,
                            new Vector2(screen.x, Screen.height - screen.y));
                        _panel.Pick(position);
                    }

                    break;
            }
        }

        public void Teardown()
        {
            Object.Destroy(_settings);
        }
    }

    /// <summary>The list with UI Toolkit: a ScrollView (hidden scrollers) with a flex column of rows.</summary>
    public sealed class UitkListScenario : IBenchmarkScenario
    {
        readonly int _rowCount;
        readonly ListMutation _mutation;
        readonly ThemeStyleSheet _theme;
        Label[] _titles;
        PanelSettings _settings;
        ScrollView _scrollView;

        public string Name { get; }

        public UitkListScenario(int rowCount, ListMutation mutation, ThemeStyleSheet theme, string name)
        {
            _rowCount = rowCount;
            _mutation = mutation;
            _theme = theme;
            Name = name;
        }

        public void Setup(Transform root)
        {
            var document = UitkBuild.CreateDocument(root, _theme, out _settings);
            _scrollView = new ScrollView(ScrollViewMode.Vertical)
            {
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Hidden,
                touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped,
            };
            _scrollView.style.flexGrow = 1f;
            document.rootVisualElement.Add(_scrollView);

            var content = _scrollView.contentContainer;
            content.style.paddingLeft = ComparisonSpec.ListPadding;
            content.style.paddingRight = ComparisonSpec.ListPadding;
            content.style.paddingTop = ComparisonSpec.ListPadding;
            content.style.paddingBottom = ComparisonSpec.ListPadding;

            _titles = new Label[_rowCount];
            for (var i = 0; i < _rowCount; i++)
            {
                var row = new VisualElement();
                var style = row.style;
                style.flexDirection = FlexDirection.Row;
                style.alignItems = Align.Center;
                style.flexShrink = 0f;
                style.minHeight = ComparisonSpec.RowHeight;
                style.paddingLeft = ComparisonSpec.RowPaddingX;
                style.paddingRight = ComparisonSpec.RowPaddingX;
                style.paddingTop = ComparisonSpec.RowPaddingY;
                style.paddingBottom = ComparisonSpec.RowPaddingY;
                style.marginBottom = i < _rowCount - 1 ? ComparisonSpec.RowSpacing : 0f;
                style.backgroundColor = ComparisonSpec.RowColor(i);
                UitkBuild.SetRadius(style, ComparisonSpec.CellRadius);

                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.style.width = ComparisonSpec.IconSize;
                icon.style.height = ComparisonSpec.IconSize;
                icon.style.flexShrink = 0f;
                icon.style.marginRight = ComparisonSpec.RowItemSpacing;
                icon.style.backgroundColor = ComparisonSpec.IconColor(i);
                UitkBuild.SetRadius(icon.style, ComparisonSpec.IconSize * 0.5f);
                row.Add(icon);

                var title = UitkBuild.CreateLabel($"Item {i}", ComparisonSpec.RowFontSize);
                title.style.flexGrow = 1f;
                title.style.flexShrink = 1f;
                title.style.marginRight = ComparisonSpec.RowItemSpacing;
                title.style.whiteSpace = WhiteSpace.Normal;
                row.Add(title);
                _titles[i] = title;

                var value = UitkBuild.CreateLabel(ComparisonSpec.Number(i * 37 % 1000), ComparisonSpec.RowFontSize);
                value.style.unityTextAlign = TextAnchor.MiddleRight;
                value.style.whiteSpace = WhiteSpace.NoWrap;
                row.Add(value);

                content.Add(row);
            }
        }

        public void Tick(int frame)
        {
            switch (_mutation)
            {
                case ListMutation.Scroll:
                    var range = Mathf.Max(0f,
                        _scrollView.contentContainer.layout.height - _scrollView.contentViewport.layout.height);
                    _scrollView.scrollOffset = new Vector2(0f, ComparisonSpec.ScrollPosition(frame) * range);
                    break;
                case ListMutation.Resize:
                    for (var i = 0; i < _rowCount; i += ComparisonSpec.MutationStride)
                    {
                        _titles[i].text = ComparisonSpec.ResizeText(i, frame);
                    }

                    break;
            }
        }

        public void Teardown()
        {
            Object.Destroy(_settings);
        }
    }
}
