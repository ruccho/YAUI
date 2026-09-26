using UnityEngine;

namespace Yaui.Benchmarks.Comparison
{
    public enum GridMutation
    {
        /// <summary>Nothing changes after setup.</summary>
        None,

        /// <summary>The background color of 10% of the cells changes every frame.</summary>
        Color,

        /// <summary>10% of the cells move every frame (render transform, no layout).</summary>
        Move,

        /// <summary>The label of 10% of the cells changes every frame (same number of digits).</summary>
        Text,

        /// <summary>Nothing changes; 10 hit tests per frame at spread positions.</summary>
        HitTest,
    }

    public enum ListMutation
    {
        /// <summary>The list scrolls every frame.</summary>
        Scroll,

        /// <summary>The title of 10% of the rows switches between one and two lines every frame (layout).</summary>
        Resize,
    }

    /// <summary>
    /// The screens every UI system builds the same way, so that the scenarios differ only in the UI system.
    /// </summary>
    /// <remarks>
    /// Grid: a screen filled with cells, each a rounded panel with a round icon and a number label.
    /// List: a non-virtualized vertical list of rows (panel, icon, title and value) in a clipping scroll view.
    /// The canvas matches the reference resolution like CanvasScaler (scale with screen size, match 0.5).
    /// </remarks>
    public static class ComparisonSpec
    {
        public static readonly Vector2 ReferenceResolution = new(1080f, 1920f);

        public const float Match = 0.5f;
        public const float MutationRatio = 0.1f;
        public const int MutationStride = 10;
        public const int HitTestsPerFrame = 10;

        public const float CellMargin = 2f;
        public const float CellRadius = 16f;

        public const float RowHeight = 120f;
        public const float RowFontSize = 40f;
        public const float ListPadding = 16f;
        public const float RowSpacing = 8f;
        public const float RowPaddingX = 16f;
        public const float RowPaddingY = 12f;
        public const float RowItemSpacing = 16f;
        public const float IconSize = RowHeight - 24f;

        public static readonly string[] ResizeTexts =
        {
            "Short",
            "A considerably longer title that wraps onto a second line",
        };

        static string[] _numbers;

        /// <summary>Strings of every number the scenarios show, so that the scenarios themselves do not allocate.</summary>
        public static string[] Numbers
        {
            get
            {
                if (_numbers == null)
                {
                    _numbers = new string[8192];
                    for (var i = 0; i < _numbers.Length; i++)
                    {
                        _numbers[i] = i.ToString();
                    }
                }

                return _numbers;
            }
        }

        public static string Number(int value) => Numbers[value % Numbers.Length];

        public static Vector2 CanvasSize
        {
            get
            {
                var screen = new Vector2(Screen.width, Screen.height);
                var scale = Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(screen.x / ReferenceResolution.x, 2f),
                    Mathf.Log(screen.y / ReferenceResolution.y, 2f), Match));
                return screen / scale;
            }
        }

        public static Color CellColor(int i) => Color.HSVToRGB(i * 0.618f % 1f, 0.3f, 1f);

        public static Color AnimatedCellColor(int i, int frame) =>
            Color.HSVToRGB((frame * 0.01f + i * 0.618f) % 1f, 0.3f, 1f);

        public static Color IconColor(int i) => Color.HSVToRGB(i * 0.382f % 1f, 0.7f, 0.9f);

        public static Color RowColor(int i) => Color.HSVToRGB(i * 0.618f % 1f, 0.15f, 1f);

        public static float MoveOffset(int i, int frame) => Mathf.Sin(frame * 0.1f + i) * 4f;

        /// <summary>Screen position of a hit test, spread over the screen.</summary>
        public static Vector2 HitTestPosition(int frame, int i)
        {
            var n = frame * HitTestsPerFrame + i;
            return new Vector2(n * 0.6180339f % 1f * Screen.width, n * 0.4142136f % 1f * Screen.height);
        }

        /// <summary>Normalized scroll position from the top.</summary>
        public static float ScrollPosition(int frame) => 0.5f - Mathf.Cos(frame * 0.01f) * 0.5f;

        public static string ResizeText(int row, int frame) => ResizeTexts[(frame + row / MutationStride) & 1];
    }

    /// <summary>The grid's dimensions in canvas units.</summary>
    public readonly struct GridSpec
    {
        public readonly int Columns;
        public readonly int Rows;
        public readonly Vector2 CellSize;
        public readonly float FontSize;

        public GridSpec(int cellCount)
        {
            var reference = ComparisonSpec.ReferenceResolution;
            var canvas = ComparisonSpec.CanvasSize;
            Columns = Mathf.CeilToInt(Mathf.Sqrt(cellCount * reference.x / reference.y));
            Rows = Mathf.CeilToInt((float)cellCount / Columns);
            CellSize = new Vector2(Mathf.Floor(canvas.x / Columns), Mathf.Floor(canvas.y / Rows));
            FontSize = reference.y / Rows * 0.3f;
        }

        /// <summary>The icon: 30% x 70% of the cell, 5% from the left.</summary>
        public Vector2 IconSize => new(CellSize.x * 0.3f, CellSize.y * 0.7f);

        public float IconMargin => CellSize.x * 0.05f;
    }
}
