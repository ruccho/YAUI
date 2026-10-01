using UnityEngine;

namespace Yaui.Showcase
{
    /// <summary>Builds layout styles in one expression: <c>Lay.Row.Size(100, 20).Gap(4)</c>.</summary>
    public static class Lay
    {
        public static readonly Length Auto = Length.Auto;

        public static LayoutStyle Col => LayoutStyle.Default;

        public static LayoutStyle Row
        {
            get
            {
                var l = LayoutStyle.Default;
                l.direction = FlexDirection.Row;
                return l;
            }
        }

        public static LayoutStyle W(this LayoutStyle l, Length width)
        {
            l.width = width;
            return l;
        }

        public static LayoutStyle H(this LayoutStyle l, Length height)
        {
            l.height = height;
            return l;
        }

        public static LayoutStyle Size(this LayoutStyle l, Length width, Length height)
        {
            l.width = width;
            l.height = height;
            return l;
        }

        public static LayoutStyle Grow(this LayoutStyle l, float grow = 1f)
        {
            l.grow = grow;
            return l;
        }

        /// <summary>Never shrinks below its size.</summary>
        public static LayoutStyle Fixed(this LayoutStyle l)
        {
            l.shrink = 0f;
            return l;
        }

        public static LayoutStyle Gap(this LayoutStyle l, float gap)
        {
            l.gap = new Vector2(gap, gap);
            return l;
        }

        public static LayoutStyle Pad(this LayoutStyle l, Length all)
        {
            l.padding = new Edges(all);
            return l;
        }

        public static LayoutStyle Pad(this LayoutStyle l, Length horizontal, Length vertical)
        {
            l.padding = new Edges(horizontal, vertical);
            return l;
        }

        public static LayoutStyle Margin(this LayoutStyle l, Length left, Length top, Length right, Length bottom)
        {
            l.margin = new Edges(left, top, right, bottom);
            return l;
        }

        public static LayoutStyle Items(this LayoutStyle l, FlexAlign align)
        {
            l.alignItems = align;
            return l;
        }

        public static LayoutStyle Self(this LayoutStyle l, FlexAlign align)
        {
            l.alignSelf = align;
            return l;
        }

        public static LayoutStyle Justify(this LayoutStyle l, FlexJustify justify)
        {
            l.justifyContent = justify;
            return l;
        }

        public static LayoutStyle Center(this LayoutStyle l)
        {
            l.justifyContent = FlexJustify.Center;
            l.alignItems = FlexAlign.Center;
            return l;
        }

        public static LayoutStyle Wrap(this LayoutStyle l)
        {
            l.wrap = FlexWrap.Wrap;
            return l;
        }

        /// <summary>Absolute, by the insets from the parent's edges (<see cref="Auto"/> to leave one free).</summary>
        public static LayoutStyle Abs(this LayoutStyle l, Length left, Length top, Length right, Length bottom)
        {
            l.position = PositionType.Absolute;
            l.inset = new Edges(left, top, right, bottom);
            return l;
        }

        /// <summary>Absolute, covering the parent.</summary>
        public static LayoutStyle Cover(this LayoutStyle l)
        {
            return l.Abs(0f, 0f, 0f, 0f);
        }
    }

    /// <summary>Builds box styles in one expression: <c>Sty.Fill(color).Radius(8).Border(1, color)</c>.</summary>
    public static class Sty
    {
        public static BoxStyle None => BoxStyle.Default;

        public static BoxStyle Fill(Color color)
        {
            var b = BoxStyle.Default;
            b.backgroundColor = color;
            return b;
        }

        public static BoxStyle Radius(this BoxStyle b, float radius)
        {
            b.cornerRadius = new Vector4(radius, radius, radius, radius);
            return b;
        }

        public static BoxStyle Border(this BoxStyle b, float width, Color color)
        {
            b.borderWidth = width;
            b.borderColor = color;
            return b;
        }

        public static BoxStyle Shadow(this BoxStyle b, Color color, float blur, float spread = 0f,
            Vector2 offset = default)
        {
            b.shadowColor = color;
            b.shadowBlur = blur;
            b.shadowSpread = spread;
            b.shadowOffset = offset;
            return b;
        }
    }

    /// <summary>Creates elements under a parent.</summary>
    public static class Ui
    {
        public static Color Hex(uint rgb, float alpha = 1f)
        {
            return new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, alpha);
        }

        public static string Tag(Color color)
        {
            return "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">";
        }

        public static T Add<T>(Component parent, string name, LayoutStyle layout, BoxStyle box) where T : YauiElement
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var element = go.AddComponent<T>();
            element.Layout = layout;
            element.Box = box;
            element.RaycastTarget = false;
            return element;
        }

        public static YauiElement El(Component parent, string name, LayoutStyle layout)
        {
            return Add<YauiElement>(parent, name, layout, Sty.None);
        }

        public static YauiElement El(Component parent, string name, LayoutStyle layout, BoxStyle box)
        {
            return Add<YauiElement>(parent, name, layout, box);
        }

        public static YauiText Text(Component parent, string name, string text, float size, Color color)
        {
            return Text(parent, name, text, size, color, Lay.Col.Fixed());
        }

        public static YauiText Text(Component parent, string name, string text, float size, Color color,
            LayoutStyle layout)
        {
            var t = Add<YauiText>(parent, name, layout, Sty.None);
            t.WordWrap = false;
            t.FontSize = size;
            t.Color = color;
            t.Text = text;
            return t;
        }

        /// <summary>A text with a soft dark shadow, readable over the scene.</summary>
        public static YauiText Shadowed(this YauiText text, float blur = 3f)
        {
            text.ShadowColor = new Color(0f, 0f, 0f, 0.85f);
            text.ShadowOffset = new Vector2(0f, 1.5f);
            text.ShadowBlur = blur;
            return text;
        }

        public static YauiImage Image(Component parent, string name, Sprite sprite, float size, Color color)
        {
            return Image(parent, name, sprite, color, Lay.Col.Size(size, size).Fixed());
        }

        public static YauiImage Image(Component parent, string name, Sprite sprite, Color color, LayoutStyle layout)
        {
            var image = Add<YauiImage>(parent, name, layout, Sty.None);
            image.Sprite = sprite;
            image.Color = color;
            return image;
        }

        /// <summary>Makes the element receive pointer events (the showcases' elements do not by default).</summary>
        public static T Hit<T>(this T element) where T : YauiElement
        {
            element.RaycastTarget = true;
            return element;
        }

        /// <summary>Tints for a control on a light or colored surface: darker when hovered and pressed.</summary>
        public static TransitionColors Tints(float highlighted = 0.93f, float pressed = 0.84f)
        {
            var colors = TransitionColors.Default;
            colors.highlighted = colors.selected = new Color(highlighted, highlighted, highlighted, 1f);
            colors.pressed = new Color(pressed, pressed, pressed, 1f);
            return colors;
        }

        public static RadialElement Radial(Component parent, string name, LayoutStyle layout, float radius,
            Color color)
        {
            var radial = Add<RadialElement>(parent, name, layout, Sty.None.Radius(radius));
            radial.Color = color;
            return radial;
        }
    }

    /// <summary>A bar whose fill is a child sized in percent, with an optional trail that follows a drop.</summary>
    public sealed class Bar
    {
        public YauiElement Track;
        public YauiElement Fill;
        public YauiElement Trail;
        public float Value = 1f;

        private float _shown = -1f;
        private float _trail = 1f;
        private float _trailShown = -1f;

        public static Bar Create(Component parent, string name, LayoutStyle layout, float radius, Color color,
            Material material = null, bool trail = false)
        {
            var bar = new Bar
            {
                Track = Ui.El(parent, name, layout,
                    Sty.Fill(new Color(0.02f, 0.03f, 0.06f, 0.85f)).Radius(radius)
                        .Border(1f, new Color(1f, 1f, 1f, 0.16f)))
            };
            var inner = Mathf.Max(radius - 1f, 0f);
            var fill = Lay.Col.Abs(0f, 0f, Lay.Auto, 0f).W(Length.Percent(100f));
            if (trail)
                bar.Trail = Ui.El(bar.Track, "Trail", fill, Sty.Fill(new Color(1f, 1f, 1f, 0.75f)).Radius(inner));

            bar.Fill = Ui.El(bar.Track, "Fill", fill, Sty.Fill(color).Radius(inner));
            bar.Fill.Material = material;
            return bar;
        }

        public void Tick(float deltaTime)
        {
            Apply(Fill, ref _shown, Value);
            if (Trail == null) return;

            _trail = Value >= _trail ? Value : Mathf.MoveTowards(_trail, Value, deltaTime * 0.3f);
            Apply(Trail, ref _trailShown, _trail);
        }

        // Only a visible change of the width runs the layout (of the bar: its fixed size is a layout boundary).
        private static void Apply(YauiElement element, ref float shown, float value)
        {
            var percent = Mathf.Round(Mathf.Clamp01(value) * 400f) * 0.25f;
            if (percent == shown) return;

            shown = percent;
            var layout = element.Layout;
            layout.width = Length.Percent(percent);
            element.Layout = layout;
        }
    }
}
