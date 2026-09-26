using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Yaui.Benchmarks.Comparison
{
    /// <summary>
    /// Builds the uGUI objects shared by the scenarios. The sprites share one texture, like an atlas, so that
    /// textures do not break batches.
    /// </summary>
    sealed class UguiFactory
    {
        const int SpriteSize = 64;

        readonly Texture2D _texture;

        public Sprite Panel { get; }
        public Sprite Icon { get; }
        public TMP_FontAsset Font { get; }

        public UguiFactory()
        {
            Font = TMP_Settings.defaultFontAsset;
            if (Font == null)
            {
                throw new System.InvalidOperationException(
                    "TMP default font asset is missing. Import TMP Essential Resources.");
            }

            _texture = new Texture2D(SpriteSize * 2, SpriteSize, TextureFormat.RGBA32, false)
            {
                name = "YauiBenchAtlas",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[SpriteSize * 2 * SpriteSize];
            FillRoundedRect(pixels, 0, ComparisonSpec.CellRadius);
            FillRoundedRect(pixels, SpriteSize, SpriteSize / 2f);
            _texture.SetPixels32(pixels);
            _texture.Apply(false, true);

            const float border = 20f;
            Panel = Sprite.Create(_texture, new Rect(0, 0, SpriteSize, SpriteSize), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            Icon = Sprite.Create(_texture, new Rect(SpriteSize, 0, SpriteSize, SpriteSize), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect);
        }

        static void FillRoundedRect(Color32[] pixels, int offsetX, float radius)
        {
            const int stride = SpriteSize * 2;
            var half = SpriteSize / 2f;
            for (var y = 0; y < SpriteSize; y++)
            {
                for (var x = 0; x < SpriteSize; x++)
                {
                    // Signed distance to a rounded rect, anti-aliased over one pixel.
                    var px = Mathf.Abs(x + 0.5f - half) - (half - radius);
                    var py = Mathf.Abs(y + 0.5f - half) - (half - radius);
                    var outside = new Vector2(Mathf.Max(px, 0f), Mathf.Max(py, 0f)).magnitude;
                    var distance = outside + Mathf.Min(Mathf.Max(px, py), 0f) - radius;
                    var alpha = (byte)(Mathf.Clamp01(0.5f - distance) * 255f);
                    pixels[y * stride + offsetX + x] = new Color32(255, 255, 255, alpha);
                }
            }
        }

        public void Destroy()
        {
            Object.Destroy(Panel);
            Object.Destroy(Icon);
            Object.Destroy(_texture);
        }

        public static Canvas CreateOverlayCanvas(Transform parent)
        {
            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(parent, false);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ComparisonSpec.ReferenceResolution;
            scaler.matchWidthOrHeight = ComparisonSpec.Match;

            return canvas;
        }

        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public Image CreatePanel(string name, Transform parent, Color color)
        {
            var image = CreateRect(name, parent).gameObject.AddComponent<Image>();
            image.sprite = Panel;
            image.type = Image.Type.Sliced;
            image.color = color;
            return image;
        }

        public Image CreateIcon(string name, Transform parent, Color color)
        {
            var image = CreateRect(name, parent).gameObject.AddComponent<Image>();
            image.sprite = Icon;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A label that is not a raycast target and opts out of TMP's per-frame scale check.</summary>
        public TextMeshProUGUI CreateLabel(string name, Transform parent, string text, float fontSize)
        {
            var label = CreateRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = Font;
            label.fontSize = fontSize;
            label.color = Color.black;
            label.text = text;
            label.raycastTarget = false;
            label.isTextObjectScaleStatic = true;
            return label;
        }
    }

    /// <summary>
    /// The grid with uGUI and TextMeshPro, placed by anchors (no layout groups), with the usual optimizations:
    /// the cells that change are in a nested canvas, only the panels are raycast targets, and the labels opt out of
    /// TMP's per-frame scale check.
    /// </summary>
    public sealed class UguiGridScenario : IBenchmarkScenario
    {
        readonly int _cellCount;
        readonly GridMutation _mutation;
        readonly List<RaycastResult> _hits = new();
        Image[] _cells;
        TextMeshProUGUI[] _labels;
        Vector2[] _basePositions;
        UguiFactory _factory;
        EventSystem _eventSystem;
        PointerEventData _pointer;

        public string Name { get; }

        public UguiGridScenario(int cellCount, GridMutation mutation, string name)
        {
            _cellCount = cellCount;
            _mutation = mutation;
            Name = name;
        }

        public void Setup(Transform root)
        {
            _factory = new UguiFactory();
            var spec = new GridSpec(_cellCount);
            var canvas = UguiFactory.CreateOverlayCanvas(root);
            var hasDynamicCells = _mutation is GridMutation.Color or GridMutation.Move or GridMutation.Text;
            Transform dynamicParent = canvas.transform;
            if (hasDynamicCells)
            {
                var dynamicRect = UguiFactory.CreateRect("DynamicCanvas", canvas.transform);
                UguiFactory.Stretch(dynamicRect);
                dynamicRect.gameObject.AddComponent<Canvas>();
                dynamicParent = dynamicRect;
            }

            _cells = new Image[_cellCount];
            _labels = new TextMeshProUGUI[_cellCount];
            _basePositions = new Vector2[_cellCount];
            var columns = spec.Columns;
            var rows = spec.Rows;
            for (var i = 0; i < _cellCount; i++)
            {
                var column = i % columns;
                var row = i / columns;
                var isDynamic = hasDynamicCells && i % ComparisonSpec.MutationStride == 0;
                var background = _factory.CreatePanel("Cell", isDynamic ? dynamicParent : canvas.transform,
                    ComparisonSpec.CellColor(i));
                var rect = background.rectTransform;
                rect.anchorMin = new Vector2((float)column / columns, 1f - (float)(row + 1) / rows);
                rect.anchorMax = new Vector2((float)(column + 1) / columns, 1f - (float)row / rows);
                rect.offsetMin = new Vector2(ComparisonSpec.CellMargin, ComparisonSpec.CellMargin);
                rect.offsetMax = new Vector2(-ComparisonSpec.CellMargin, -ComparisonSpec.CellMargin);

                var icon = _factory.CreateIcon("Icon", rect, ComparisonSpec.IconColor(i));
                icon.rectTransform.anchorMin = new Vector2(0.05f, 0.15f);
                icon.rectTransform.anchorMax = new Vector2(0.35f, 0.85f);
                icon.rectTransform.offsetMin = Vector2.zero;
                icon.rectTransform.offsetMax = Vector2.zero;

                var label = _factory.CreateLabel("Label", rect, ComparisonSpec.Number(i), spec.FontSize);
                label.rectTransform.anchorMin = new Vector2(0.4f, 0f);
                label.rectTransform.anchorMax = new Vector2(1f, 1f);
                label.rectTransform.offsetMin = Vector2.zero;
                label.rectTransform.offsetMax = Vector2.zero;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.textWrappingMode = TextWrappingModes.NoWrap;

                _cells[i] = background;
                _labels[i] = label;
                _basePositions[i] = rect.anchoredPosition;
            }

            if (_mutation == GridMutation.HitTest)
            {
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
                        _cells[i].color = ComparisonSpec.AnimatedCellColor(i, frame);
                    }

                    break;
                case GridMutation.Move:
                    for (var i = 0; i < _cellCount; i += ComparisonSpec.MutationStride)
                    {
                        _cells[i].rectTransform.anchoredPosition =
                            _basePositions[i] + new Vector2(ComparisonSpec.MoveOffset(i, frame), 0f);
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
                        _pointer.position = ComparisonSpec.HitTestPosition(frame, i);
                        _hits.Clear();
                        _eventSystem.RaycastAll(_pointer, _hits);
                    }

                    break;
            }
        }

        public void Teardown()
        {
            _factory.Destroy();
        }
    }

    /// <summary>
    /// The list with uGUI: a ScrollRect with RectMask2D, a VerticalLayoutGroup of rows with HorizontalLayoutGroups.
    /// </summary>
    public sealed class UguiListScenario : IBenchmarkScenario
    {
        readonly int _rowCount;
        readonly ListMutation _mutation;
        TextMeshProUGUI[] _titles;
        UguiFactory _factory;
        ScrollRect _scrollRect;

        public string Name { get; }

        public UguiListScenario(int rowCount, ListMutation mutation, string name)
        {
            _rowCount = rowCount;
            _mutation = mutation;
            Name = name;
        }

        public void Setup(Transform root)
        {
            _factory = new UguiFactory();
            var canvas = UguiFactory.CreateOverlayCanvas(root);

            var scrollView = UguiFactory.CreateRect("ScrollView", canvas.transform);
            UguiFactory.Stretch(scrollView);
            _scrollRect = scrollView.gameObject.AddComponent<ScrollRect>();
            _scrollRect.horizontal = false;
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;

            var viewport = UguiFactory.CreateRect("Viewport", scrollView);
            UguiFactory.Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            _scrollRect.viewport = viewport;

            var content = UguiFactory.CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            // As wide as the viewport (the default size delta would make it 100 wider).
            content.sizeDelta = Vector2.zero;
            var contentLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            var padding = (int)ComparisonSpec.ListPadding;
            contentLayout.padding = new RectOffset(padding, padding, padding, padding);
            contentLayout.spacing = ComparisonSpec.RowSpacing;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scrollRect.content = content;

            _titles = new TextMeshProUGUI[_rowCount];
            for (var i = 0; i < _rowCount; i++)
            {
                var row = _factory.CreatePanel("Row", content, ComparisonSpec.RowColor(i));
                var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                rowLayout.padding = new RectOffset((int)ComparisonSpec.RowPaddingX, (int)ComparisonSpec.RowPaddingX,
                    (int)ComparisonSpec.RowPaddingY, (int)ComparisonSpec.RowPaddingY);
                rowLayout.spacing = ComparisonSpec.RowItemSpacing;
                rowLayout.childAlignment = TextAnchor.MiddleLeft;
                rowLayout.childControlWidth = true;
                rowLayout.childControlHeight = true;
                rowLayout.childForceExpandWidth = false;
                rowLayout.childForceExpandHeight = false;
                row.gameObject.AddComponent<LayoutElement>().minHeight = ComparisonSpec.RowHeight;

                var icon = _factory.CreateIcon("Icon", row.transform, ComparisonSpec.IconColor(i));
                var iconLayout = icon.gameObject.AddComponent<LayoutElement>();
                iconLayout.preferredWidth = ComparisonSpec.IconSize;
                iconLayout.preferredHeight = ComparisonSpec.IconSize;

                var title = _factory.CreateLabel("Title", row.transform, $"Item {i}", ComparisonSpec.RowFontSize);
                title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
                _titles[i] = title;

                var value = _factory.CreateLabel("Value", row.transform, ComparisonSpec.Number(i * 37 % 1000),
                    ComparisonSpec.RowFontSize);
                value.alignment = TextAlignmentOptions.MidlineRight;
            }
        }

        public void Tick(int frame)
        {
            switch (_mutation)
            {
                case ListMutation.Scroll:
                    _scrollRect.verticalNormalizedPosition = 1f - ComparisonSpec.ScrollPosition(frame);
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
            _factory.Destroy();
        }
    }
}
