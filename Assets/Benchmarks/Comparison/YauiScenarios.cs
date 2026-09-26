using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Yaui.Benchmarks.Comparison
{
    static class YauiBuild
    {
        public static YauiPanel CreatePanel(Transform root)
        {
            var go = new GameObject("Panel", typeof(YauiPanel));
            go.transform.SetParent(root, false);
            var panel = go.GetComponent<YauiPanel>();
            panel.ReferenceResolution = ComparisonSpec.ReferenceResolution;
            panel.Match = ComparisonSpec.Match;
            return panel;
        }

        public static T Create<T>(string name, Transform parent) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<T>();
        }

        public static EventSystem CreateEventSystem(Transform root)
        {
            var go = new GameObject("EventSystem", typeof(EventSystem));
            go.transform.SetParent(root, false);
            return go.GetComponent<EventSystem>();
        }
    }

    /// <summary>The grid with YAUI: fixed-size cells in a wrapping flex row.</summary>
    public sealed class YauiGridScenario : IBenchmarkScenario
    {
        readonly int _cellCount;
        readonly GridMutation _mutation;
        readonly List<RaycastResult> _hits = new();
        YauiElement[] _cells;
        YauiText[] _labels;
        EventSystem _eventSystem;
        PointerEventData _pointer;

        public string Name { get; }

        public YauiGridScenario(int cellCount, GridMutation mutation, string name)
        {
            _cellCount = cellCount;
            _mutation = mutation;
            Name = name;
        }

        public void Setup(Transform root)
        {
            var spec = new GridSpec(_cellCount);
            var panel = YauiBuild.CreatePanel(root);
            var panelElement = panel.GetComponent<YauiElement>();
            var layout = LayoutStyle.Default;
            layout.direction = FlexDirection.Row;
            layout.wrap = FlexWrap.Wrap;
            layout.alignContent = FlexAlign.FlexStart;
            panelElement.Layout = layout;

            _cells = new YauiElement[_cellCount];
            _labels = new YauiText[_cellCount];
            for (var i = 0; i < _cellCount; i++)
            {
                var cell = YauiBuild.Create<YauiElement>("Cell", panel.transform);
                var cellLayout = LayoutStyle.Default;
                cellLayout.width = spec.CellSize.x - ComparisonSpec.CellMargin * 2f;
                cellLayout.height = spec.CellSize.y - ComparisonSpec.CellMargin * 2f;
                cellLayout.margin = new Edges(ComparisonSpec.CellMargin);
                cellLayout.direction = FlexDirection.Row;
                cellLayout.alignItems = FlexAlign.Center;
                cellLayout.shrink = 0f;
                cell.Layout = cellLayout;
                var box = BoxStyle.Default;
                box.backgroundColor = ComparisonSpec.CellColor(i);
                var radius = ComparisonSpec.CellRadius;
                box.cornerRadius = new Vector4(radius, radius, radius, radius);
                cell.Box = box;
                _cells[i] = cell;

                var icon = YauiBuild.Create<YauiElement>("Icon", cell.transform);
                var iconLayout = LayoutStyle.Default;
                iconLayout.width = spec.IconSize.x;
                iconLayout.height = spec.IconSize.y;
                iconLayout.margin = new Edges(spec.IconMargin, 0f, spec.IconMargin, 0f);
                icon.Layout = iconLayout;
                var iconBox = BoxStyle.Default;
                iconBox.backgroundColor = ComparisonSpec.IconColor(i);
                iconBox.cornerRadius = new Vector4(1e4f, 1e4f, 1e4f, 1e4f);
                icon.Box = iconBox;
                icon.RaycastTarget = false;

                var label = YauiBuild.Create<YauiText>("Label", cell.transform);
                var labelLayout = LayoutStyle.Default;
                labelLayout.grow = 1f;
                label.Layout = labelLayout;
                label.FontSize = spec.FontSize;
                label.Color = Color.black;
                label.WordWrap = false;
                label.Text = ComparisonSpec.Number(i);
                label.RaycastTarget = false;
                _labels[i] = label;
            }

            if (_mutation == GridMutation.HitTest)
            {
                panel.gameObject.AddComponent<YauiRaycaster>();
                _eventSystem = YauiBuild.CreateEventSystem(root);
                _pointer = new PointerEventData(_eventSystem);
            }
        }

        public void Tick(int frame)
        {
            switch (_mutation)
            {
                case GridMutation.Color:
                    for (var i = 0; i < _cellCount; i += ComparisonSpec.MutationStride)
                    {
                        _cells[i].BackgroundColor = ComparisonSpec.AnimatedCellColor(i, frame);
                    }

                    break;
                case GridMutation.Move:
                    for (var i = 0; i < _cellCount; i += ComparisonSpec.MutationStride)
                    {
                        _cells[i].Translate = new Vector2(ComparisonSpec.MoveOffset(i, frame), 0f);
                    }

                    break;
                case GridMutation.Text:
                    for (var i = 0; i < _cellCount; i += ComparisonSpec.MutationStride)
                    {
                        _labels[i].Text = ComparisonSpec.Number(frame + i);
                    }

                    break;
                case GridMutation.HitTest:
                    for (var i = 0; i < ComparisonSpec.HitTestsPerFrame; i++)
                    {
                        _pointer.position = ComparisonSpec.HitTestPosition(frame, i);
                        _hits.Clear();
                        _eventSystem.RaycastAll(_pointer, _hits);
                    }

                    break;
            }
        }

        public void Teardown()
        {
        }
    }

    /// <summary>The list with YAUI: a flex column in a clipping scroll view.</summary>
    public sealed class YauiListScenario : IBenchmarkScenario
    {
        readonly int _rowCount;
        readonly ListMutation _mutation;
        YauiScrollView _scrollView;
        YauiText[] _titles;

        public string Name { get; }

        public YauiListScenario(int rowCount, ListMutation mutation, string name)
        {
            _rowCount = rowCount;
            _mutation = mutation;
            Name = name;
        }

        public void Setup(Transform root)
        {
            var panel = YauiBuild.CreatePanel(root);

            var view = YauiBuild.Create<YauiElement>("ScrollView", panel.transform);
            var viewLayout = LayoutStyle.Default;
            viewLayout.grow = 1f;
            view.Layout = viewLayout;
            view.ClipChildren = true;
            _scrollView = view.gameObject.AddComponent<YauiScrollView>();
            _scrollView.Horizontal = false;
            _scrollView.MovementType = ScrollMovement.Clamped;

            var content = YauiBuild.Create<YauiElement>("Content", view.transform);
            var contentLayout = LayoutStyle.Default;
            contentLayout.position = PositionType.Absolute;
            contentLayout.inset = new Edges(0f, 0f, 0f, Length.Auto);
            contentLayout.padding = new Edges(ComparisonSpec.ListPadding);
            content.Layout = contentLayout;
            _scrollView.Content = content;

            _titles = new YauiText[_rowCount];
            for (var i = 0; i < _rowCount; i++)
            {
                var row = YauiBuild.Create<YauiElement>("Row", content.transform);
                var rowLayout = LayoutStyle.Default;
                rowLayout.direction = FlexDirection.Row;
                rowLayout.alignItems = FlexAlign.Center;
                rowLayout.shrink = 0f;
                rowLayout.minHeight = ComparisonSpec.RowHeight;
                rowLayout.padding = new Edges(ComparisonSpec.RowPaddingX, ComparisonSpec.RowPaddingY);
                rowLayout.margin = new Edges(0f, 0f, 0f, i < _rowCount - 1 ? ComparisonSpec.RowSpacing : 0f);
                row.Layout = rowLayout;
                var box = BoxStyle.Default;
                box.backgroundColor = ComparisonSpec.RowColor(i);
                var radius = ComparisonSpec.CellRadius;
                box.cornerRadius = new Vector4(radius, radius, radius, radius);
                row.Box = box;

                var icon = YauiBuild.Create<YauiElement>("Icon", row.transform);
                var iconLayout = LayoutStyle.Default;
                iconLayout.width = ComparisonSpec.IconSize;
                iconLayout.height = ComparisonSpec.IconSize;
                iconLayout.shrink = 0f;
                iconLayout.margin = new Edges(0f, 0f, ComparisonSpec.RowItemSpacing, 0f);
                icon.Layout = iconLayout;
                var iconBox = BoxStyle.Default;
                iconBox.backgroundColor = ComparisonSpec.IconColor(i);
                iconBox.cornerRadius = new Vector4(1e4f, 1e4f, 1e4f, 1e4f);
                icon.Box = iconBox;
                icon.RaycastTarget = false;

                var title = YauiBuild.Create<YauiText>("Title", row.transform);
                var titleLayout = LayoutStyle.Default;
                titleLayout.grow = 1f;
                titleLayout.margin = new Edges(0f, 0f, ComparisonSpec.RowItemSpacing, 0f);
                title.Layout = titleLayout;
                title.FontSize = ComparisonSpec.RowFontSize;
                title.Color = Color.black;
                title.Text = $"Item {i}";
                title.RaycastTarget = false;
                _titles[i] = title;

                var value = YauiBuild.Create<YauiText>("Value", row.transform);
                value.FontSize = ComparisonSpec.RowFontSize;
                value.Color = Color.black;
                value.WordWrap = false;
                value.Align = TextAlign.Right;
                value.Text = ComparisonSpec.Number(i * 37 % 1000);
                value.RaycastTarget = false;
            }
        }

        public void Tick(int frame)
        {
            switch (_mutation)
            {
                case ListMutation.Scroll:
                    _scrollView.NormalizedPosition = new Vector2(0f, ComparisonSpec.ScrollPosition(frame));
                    break;
                case ListMutation.Resize:
                    for (var i = 0; i < _rowCount; i += ComparisonSpec.MutationStride)
                    {
                        _titles[i].Text = ComparisonSpec.ResizeText(i, frame);
                    }

                    break;
            }
        }

        public void Teardown()
        {
        }
    }
}
