using UnityEngine;

namespace Yaui.Showcase
{
    /// <summary>
    /// A sector of the element's box (rounded by its corner radii) in one color, drawn in the batch by the uber
    /// shader: cooldown sweeps, gauges and radar beams.
    /// </summary>
    public sealed class RadialElement : YauiElement
    {
        private static readonly YauiPrimitive[] Buffer = new YauiPrimitive[1];

        private Color _color = Color.white;
        private float _start = -90f;
        private float _sweep = 360f;

        public Color Color
        {
            get => _color;
            set
            {
                _color = value;
                Write();
            }
        }

        /// <summary>Degrees, clockwise on screen; the start is measured from the right (-90 is the top).</summary>
        public void SetArc(float start, float sweep)
        {
            if (start == _start && sweep == _sweep) return;

            _start = start;
            _sweep = sweep;
            Write();
        }

        protected override bool HasVisibleContent => true;

        protected override void OnRegistered()
        {
            Write();
        }

        protected override void OnLayoutApplied()
        {
            Write();
        }

        protected override void OnBoxChanged()
        {
            Write();
        }

        private void Write()
        {
            if (!IsRegistered) return;

            var size = LayoutRect.size;
            var primitive = YauiPrimitive.Rectangle(new Rect(0f, 0f, size.x, size.y), _color)
                .WithCornerRadius(Box.cornerRadius)
                .WithRadialFill(new Vector2(0.5f, 0.5f), _start * Mathf.Deg2Rad, _sweep * Mathf.Deg2Rad);
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
