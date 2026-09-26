using System;
using UnityEngine;

namespace Yaui.Tests.External
{
    /// <summary>
    /// A text-like element built on the public API only, as a package outside YAUI would: monospace glyphs from a
    /// distance field atlas of 16 x 16 cells indexed by character code, wrapped at the available width. This assembly
    /// has no access to the internals of YAUI.
    /// </summary>
    [AddComponentMenu("")]
    public class MonospaceLabel : YauiElement
    {
        public const float Advance = 10f;
        public const float LineHeight = 20f;
        public const float Spread = 4f;

        [SerializeField] private string text = "";
        [SerializeField] private Texture2D atlas;
        [SerializeField] private Color color = Color.black;
        [SerializeField] private bool shadow;
        [SerializeField] private bool underline;

        private YauiTexture atlasTexture;

        // What the layout thread measures.
        private int measuredLength;

        private YauiPrimitive[] buffer = new YauiPrimitive[16];

        public string Text
        {
            get => text;
            set
            {
                if ((value ?? "").Length != text.Length) MarkMeasureDirty();

                text = value ?? "";
                Rebuild();
            }
        }

        public Texture2D Atlas
        {
            get => atlas;
            set
            {
                atlas = value;
                Rebuild();
            }
        }

        public bool Shadow
        {
            get => shadow;
            set
            {
                shadow = value;
                Rebuild();
            }
        }

        public bool Underline
        {
            get => underline;
            set
            {
                underline = value;
                Rebuild();
            }
        }

        /// <summary>The texture as registered, for tests.</summary>
        public YauiTexture AtlasTexture => atlasTexture;

        public int PrimitiveCount => ContentCount;

        protected override bool MeasuresContent => true;

        protected override bool HasVisibleContent => text.Length > 0;

        protected override void OnPrepareMeasure()
        {
            measuredLength = text.Length;
        }

        protected override Vector2 MeasureContent(float width, YauiMeasureMode widthMode, float height,
            YauiMeasureMode heightMode)
        {
            var columns = Columns(measuredLength, widthMode == YauiMeasureMode.Undefined ? float.PositiveInfinity : width);
            var lines = measuredLength == 0 ? 0 : (measuredLength + columns - 1) / columns;
            var size = new Vector2(Math.Min(measuredLength, columns) * Advance, lines * LineHeight);
            if (widthMode == YauiMeasureMode.Exactly) size.x = width;
            else if (widthMode == YauiMeasureMode.AtMost) size.x = Math.Min(size.x, width);

            if (heightMode == YauiMeasureMode.Exactly) size.y = height;
            else if (heightMode == YauiMeasureMode.AtMost) size.y = Math.Min(size.y, height);

            return size;
        }

        private static int Columns(int length, float width)
        {
            return Math.Max(1, float.IsInfinity(width) ? length : (int)(width / Advance));
        }

        protected override void OnRegistered()
        {
            Rebuild();
        }

        protected override void OnUnregistering()
        {
            atlasTexture.Release();
            atlasTexture = default;
        }

        protected override void OnLayoutApplied()
        {
            Rebuild();
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            MarkMeasureDirty();
            Rebuild();
        }

        private void Rebuild()
        {
            if (!IsRegistered) return;

            SyncHittable();
            if (atlas != atlasTexture.Texture)
            {
                atlasTexture.Release();
                atlasTexture = atlas != null ? YauiTexture.AcquireDistanceField(atlas, Spread) : default;
            }

            if (atlas == null || text.Length == 0)
            {
                ClearContent();
                return;
            }

            var box = ContentBox;
            var columns = Columns(text.Length, box.width);
            var lines = (text.Length + columns - 1) / columns;
            var count = text.Length * (shadow ? 2 : 1) + (underline ? lines : 0);
            if (buffer.Length < count) Array.Resize(ref buffer, count);

            // Shadows first, under all glyphs.
            var written = 0;
            if (shadow)
                for (var i = 0; i < text.Length; i++)
                {
                    var rect = GlyphRect(box, i, columns);
                    rect.position += new Vector2(1f, 1f);
                    buffer[written++] = YauiPrimitive.DistanceFieldShadow(rect, atlasTexture, GlyphUv(text[i]),
                        new Color(0f, 0f, 0f, 0.5f), 1f, 2f);
                }

            for (var i = 0; i < text.Length; i++)
                buffer[written++] = YauiPrimitive.DistanceField(GlyphRect(box, i, columns), atlasTexture,
                    GlyphUv(text[i]), color);

            if (underline)
                for (var line = 0; line < lines; line++)
                {
                    var length = Math.Min(columns, text.Length - line * columns);
                    buffer[written++] = YauiPrimitive.Rectangle(
                        new Rect(box.x, box.y + (line + 1) * LineHeight - 2f, length * Advance, 2f), color);
                }

            SetContent(buffer.AsSpan(0, written));
        }

        private static Rect GlyphRect(Rect box, int index, int columns)
        {
            return new Rect(box.x + index % columns * Advance, box.y + index / columns * LineHeight, Advance,
                LineHeight);
        }

        private static Rect GlyphUv(char c)
        {
            var cell = c & 0xff;
            return new Rect(cell % 16 / 16f, cell / 16 / 16f, 1f / 16f, 1f / 16f);
        }
    }
}
