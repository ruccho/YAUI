using System;
using UnityEngine;

namespace Yaui.Showcase
{
    /// <summary>
    /// A small bar chart in one element: a rounded bar per value (0..1) over the content box, growing from the
    /// bottom, written as primitives of the uber shader. Sparklines of a table need no element per bar.
    /// </summary>
    public sealed class BarsElement : YauiElement
    {
        private static YauiPrimitive[] _buffer = new YauiPrimitive[32];

        private float[] _values = Array.Empty<float>();
        private Color _color = Color.white;
        private Color _lastColor = Color.white;
        private float _gap = 2f;
        private float _radius = 1.5f;

        public Color Color
        {
            get => _color;
            set
            {
                _color = _lastColor = value;
                Write();
            }
        }

        /// <summary>The color of the last bar (the current value).</summary>
        public Color LastColor
        {
            get => _lastColor;
            set
            {
                _lastColor = value;
                Write();
            }
        }

        public float Gap
        {
            get => _gap;
            set
            {
                _gap = value;
                Write();
            }
        }

        public float Radius
        {
            get => _radius;
            set
            {
                _radius = value;
                Write();
            }
        }

        /// <summary>The values are read now; the array can be reused.</summary>
        public void SetValues(float[] values)
        {
            if (_values.Length != values.Length) _values = new float[values.Length];

            Array.Copy(values, _values, values.Length);
            Write();
        }

        protected override bool HasVisibleContent => _values.Length > 0;

        protected override void OnRegistered()
        {
            Write();
        }

        protected override void OnLayoutApplied()
        {
            Write();
        }

        private void Write()
        {
            if (!IsRegistered) return;

            var count = _values.Length;
            var box = ContentBox;
            if (count == 0 || box.width <= 0f || box.height <= 0f)
            {
                ClearContent();
                return;
            }

            if (_buffer.Length < count) _buffer = new YauiPrimitive[count];

            var width = (box.width - _gap * (count - 1)) / count;
            var radii = new Vector4(_radius, _radius, _radius, _radius);
            for (var i = 0; i < count; i++)
            {
                var height = Mathf.Max(Mathf.Clamp01(_values[i]) * box.height, 1f);
                var rect = new Rect(box.x + i * (width + _gap), box.yMax - height, width, height);
                _buffer[i] = YauiPrimitive.Rectangle(rect, i == count - 1 ? _lastColor : _color)
                    .WithCornerRadius(radii);
            }

            SetContent(new ReadOnlySpan<YauiPrimitive>(_buffer, 0, count));
        }
    }
}
