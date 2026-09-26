using System;
using UnityEngine;

namespace Yaui
{
    public enum LengthUnit
    {
        /// <summary>Decided by the layout (for min / max: no limit).</summary>
        Auto,

        /// <summary>Canvas units.</summary>
        Point,

        /// <summary>Percent of the parent's size.</summary>
        Percent
    }

    [Serializable]
    public struct Length : IEquatable<Length>
    {
        public float value;
        public LengthUnit unit;

        public Length(float value, LengthUnit unit)
        {
            this.value = value;
            this.unit = unit;
        }

        public static Length Auto => new(0f, LengthUnit.Auto);

        public static Length Points(float value)
        {
            return new Length(value, LengthUnit.Point);
        }

        public static Length Percent(float value)
        {
            return new Length(value, LengthUnit.Percent);
        }

        public static implicit operator Length(float points)
        {
            return Points(points);
        }

        public bool Equals(Length other)
        {
            return value.Equals(other.value) && unit == other.unit;
        }

        public override bool Equals(object obj)
        {
            return obj is Length other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(value, (int)unit);
        }

        public override string ToString()
        {
            return unit switch
            {
                LengthUnit.Auto => "auto",
                LengthUnit.Percent => $"{value}%",
                _ => value.ToString()
            };
        }
    }

    [Serializable]
    public struct Edges : IEquatable<Edges>
    {
        public Length left;
        public Length top;
        public Length right;
        public Length bottom;

        public Edges(Length all)
        {
            left = top = right = bottom = all;
        }

        public Edges(Length horizontal, Length vertical)
        {
            left = right = horizontal;
            top = bottom = vertical;
        }

        public Edges(Length left, Length top, Length right, Length bottom)
        {
            this.left = left;
            this.top = top;
            this.right = right;
            this.bottom = bottom;
        }

        public static Edges Zero => new(Length.Points(0f));

        public static Edges Auto => new(Length.Auto);

        public bool Equals(Edges other)
        {
            return left.Equals(other.left) && top.Equals(other.top) && right.Equals(other.right) &&
                   bottom.Equals(other.bottom);
        }

        public override bool Equals(object obj)
        {
            return obj is Edges other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(left, top, right, bottom);
        }
    }

    public enum PositionType
    {
        /// <summary>Placed by the flex layout of the parent, then offset by the insets.</summary>
        Relative,

        /// <summary>Placed by the insets relative to the parent, outside of the flex layout.</summary>
        Absolute
    }

    /// <summary>Properties that affect the layout. Changing them re-runs the layout of the panel.</summary>
    [Serializable]
    public struct LayoutStyle : IEquatable<LayoutStyle>
    {
        public PositionType position;
        public Edges inset;

        public FlexDirection direction;
        public FlexWrap wrap;
        public FlexJustify justifyContent;
        public FlexAlign alignItems;
        public FlexAlign alignSelf;
        public FlexAlign alignContent;

        public float grow;
        public float shrink;
        public Length basis;

        public Length width;
        public Length height;
        public Length minWidth;
        public Length minHeight;
        public Length maxWidth;
        public Length maxHeight;

        public Edges margin;
        public Edges padding;

        /// <summary>x: between columns, y: between rows.</summary>
        public Vector2 gap;

        /// <summary>CSS defaults, except that the direction is a column as in Yoga and UI Toolkit.</summary>
        public static LayoutStyle Default => new()
        {
            position = PositionType.Relative,
            inset = Edges.Auto,
            direction = FlexDirection.Column,
            wrap = FlexWrap.NoWrap,
            justifyContent = FlexJustify.FlexStart,
            alignItems = FlexAlign.Stretch,
            alignSelf = FlexAlign.Auto,
            alignContent = FlexAlign.FlexStart,
            grow = 0f,
            shrink = 1f,
            basis = Length.Auto,
            width = Length.Auto,
            height = Length.Auto,
            minWidth = Length.Auto,
            minHeight = Length.Auto,
            maxWidth = Length.Auto,
            maxHeight = Length.Auto,
            margin = Edges.Zero,
            padding = Edges.Zero,
            gap = Vector2.zero
        };

        public bool Equals(LayoutStyle other)
        {
            return position == other.position && inset.Equals(other.inset) && direction == other.direction &&
                   wrap == other.wrap && justifyContent == other.justifyContent && alignItems == other.alignItems &&
                   alignSelf == other.alignSelf && alignContent == other.alignContent && grow.Equals(other.grow) &&
                   shrink.Equals(other.shrink) && basis.Equals(other.basis) && width.Equals(other.width) &&
                   height.Equals(other.height) && minWidth.Equals(other.minWidth) &&
                   minHeight.Equals(other.minHeight) &&
                   maxWidth.Equals(other.maxWidth) && maxHeight.Equals(other.maxHeight) &&
                   margin.Equals(other.margin) &&
                   padding.Equals(other.padding) && gap.Equals(other.gap);
        }

        public override bool Equals(object obj)
        {
            return obj is LayoutStyle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(direction, width, height, margin, padding);
        }
    }

    public enum TextAlign
    {
        Left,
        Center,
        Right,
        Justified
    }

    public enum VerticalAlign
    {
        Top,
        Middle,
        Bottom
    }

    /// <summary>How a text that does not fit its content box ends.</summary>
    public enum TextOverflow
    {
        /// <summary>The text overflows the box (clip it with <see cref="YauiElement.ClipChildren"/> on a parent).</summary>
        Visible,

        /// <summary>
        /// The text is cut where it overflows the width (without word wrap) or the height (with word wrap), and ends
        /// with an ellipsis.
        /// </summary>
        Ellipsis
    }

    /// <summary>The look of the element's box. Drawn by the uber shader without breaking the batch.</summary>
    [Serializable]
    public struct BoxStyle
    {
        public Color backgroundColor;

        /// <summary>x: top-left, y: top-right, z: bottom-right, w: bottom-left.</summary>
        public Vector4 cornerRadius;

        /// <summary>Also insets the content in the layout, like CSS.</summary>
        public float borderWidth;

        public Color borderColor;

        /// <summary>No shadow when the alpha is zero.</summary>
        public Color shadowColor;

        public Vector2 shadowOffset;

        /// <summary>Gaussian sigma in canvas units.</summary>
        public float shadowBlur;

        public float shadowSpread;

        public static BoxStyle Default => new()
        {
            backgroundColor = Color.clear,
            cornerRadius = Vector4.zero,
            borderWidth = 0f,
            borderColor = Color.black,
            shadowColor = Color.clear,
            shadowOffset = Vector2.zero,
            shadowBlur = 0f,
            shadowSpread = 0f
        };

        internal bool HasShadow => shadowColor.a > 0f;

        internal bool IsVisible => backgroundColor.a > 0f || (borderWidth > 0f && borderColor.a > 0f) || HasShadow;
    }

    /// <summary>
    /// A 2D transform applied after the layout, without re-running it (CSS transform).
    /// Applies to the descendants too.
    /// </summary>
    [Serializable]
    public struct TransformStyle
    {
        public Vector2 translate;

        /// <summary>Degrees, clockwise.</summary>
        public float rotation;

        public Vector2 scale;

        /// <summary>Origin of the rotation and scale, normalized in the element's box.</summary>
        public Vector2 pivot;

        public static TransformStyle Identity => new()
        {
            translate = Vector2.zero,
            rotation = 0f,
            scale = Vector2.one,
            pivot = new Vector2(0.5f, 0.5f)
        };
    }
}