using UnityEngine;

namespace Yaui.Showcase
{
    /// <summary>
    /// A slanted box: a parallelogram with rounded corners over the element's box, written as one skewed primitive.
    /// With a <see cref="YauiMask"/> on the same GameObject, the children are cut to the parallelogram (the mask
    /// takes the shape of the content).
    /// </summary>
    public sealed class SkewElement : YauiElement
    {
        private static readonly YauiPrimitive[] Buffer = new YauiPrimitive[1];

        private float _skew = 0.1f;
        private float _radius;
        private Color _color = Color.white;
        private Color _secondColor = Color.white;

        /// <summary>How far the top is shifted to the right, in heights of the box (negative: to the left).</summary>
        public float Skew
        {
            get => _skew;
            set
            {
                _skew = value;
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

        public Color Color
        {
            get => _color;
            set
            {
                _color = value;
                Write();
            }
        }

        /// <summary>
        /// Kept in the border color of the primitive, which has no border: the second color of materials that read
        /// it, like the showcases' gradient shader.
        /// </summary>
        public Color SecondColor
        {
            get => _secondColor;
            set
            {
                _secondColor = value;
                Write();
            }
        }

        protected override bool HasVisibleContent => true;

        protected override bool ContentIsMaskShape => true;

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

            // The skewed shape stays inside the box: the rect is narrower by the shift of its top.
            var size = LayoutRect.size;
            var shift = Mathf.Abs(_skew) * size.y;
            var rect = new Rect(_skew >= 0f ? 0f : shift, 0f, Mathf.Max(size.x - shift, 0f), size.y);
            var primitive = YauiPrimitive.Rectangle(rect, _color)
                .WithCornerRadius(new Vector4(_radius, _radius, _radius, _radius))
                .WithBorder(0f, _secondColor)
                .WithSkew(_skew);
            if (ContentCount == 1)
            {
                SetContent(0, primitive);
            }
            else
            {
                Buffer[0] = primitive;
                SetContent(Buffer);
            }
        }
    }
}
