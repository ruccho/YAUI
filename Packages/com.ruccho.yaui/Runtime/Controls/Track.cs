using UnityEngine;

namespace Yaui
{
    public enum TrackDirection
    {
        LeftToRight,
        RightToLeft,
        BottomToTop,
        TopToBottom
    }

    /// <summary>
    /// The shared parts of sliders and scrollbars: positions along a track element, and the layout that places
    /// their parts (absolutely positioned, in percent of the track).
    /// </summary>
    internal static class Track
    {
        public static bool IsVertical(TrackDirection direction)
        {
            return direction is TrackDirection.BottomToTop or TrackDirection.TopToBottom;
        }

        /// <summary>Whether the value grows against the canvas axis (right to left, or up since Y points down).</summary>
        public static bool IsReversed(TrackDirection direction)
        {
            return direction is TrackDirection.RightToLeft or TrackDirection.BottomToTop;
        }

        /// <summary>The coordinate along the track's axis of a local point, and the track's length.</summary>
        public static float Along(TrackDirection direction, Vector2 local)
        {
            return IsVertical(direction) ? local.y : local.x;
        }

        public static float Length(YauiElement track, TrackDirection direction)
        {
            return IsVertical(direction) ? track.LayoutRect.height : track.LayoutRect.width;
        }

        /// <summary>A screen position as a fraction of the track in the direction of the value (unclamped).</summary>
        public static bool TryFractionAt(YauiElement track, TrackDirection direction, Vector2 screenPosition,
            float offset, out float fraction)
        {
            fraction = 0f;
            if (track == null) return false;

            var length = Length(track, direction);
            if (length <= 0f || !track.ScreenToLocal(screenPosition, out var local)) return false;

            fraction = (Along(direction, local) - offset) / length;
            if (IsReversed(direction)) fraction = 1f - fraction;

            return true;
        }

        /// <summary>The extent of an element along the axis: its fixed size, or its size from the last layout.</summary>
        public static float Extent(YauiElement element, TrackDirection direction)
        {
            var size = IsVertical(direction) ? element.Layout.height : element.Layout.width;
            if (size.unit == LengthUnit.Point) return size.value;

            return IsVertical(direction) ? element.LayoutRect.height : element.LayoutRect.width;
        }

        /// <summary>
        /// Places an element along the track: its start at <paramref name="start"/> (a fraction of the track in the
        /// direction of the value), with a length of <paramref name="length"/> (a fraction, or negative to keep the
        /// element's own) and shifted back by <paramref name="shift"/> canvas units (centering a handle).
        /// With <paramref name="stretch"/>, the element also covers the track across; otherwise the other axis is
        /// left to the element.
        /// </summary>
        public static void Place(YauiElement element, TrackDirection direction, float start, float length,
            float shift, bool stretch)
        {
            var layout = element.Layout;
            if (stretch)
            {
                var zero = Yaui.Length.Points(0f);
                if (IsVertical(direction))
                {
                    layout.inset.left = zero;
                    layout.inset.right = zero;
                    layout.width = Yaui.Length.Auto;
                }
                else
                {
                    layout.inset.top = zero;
                    layout.inset.bottom = zero;
                    layout.height = Yaui.Length.Auto;
                }
            }

            layout.position = PositionType.Absolute;
            var vertical = IsVertical(direction);
            var reversed = IsReversed(direction);
            var from = Yaui.Length.Percent(start * 100f);
            var margin = Yaui.Length.Points(-shift);
            if (vertical)
            {
                layout.inset.top = reversed ? Yaui.Length.Auto : from;
                layout.inset.bottom = reversed ? from : Yaui.Length.Auto;
                layout.margin.top = reversed ? layout.margin.top : margin;
                layout.margin.bottom = reversed ? margin : layout.margin.bottom;
                if (length >= 0f) layout.height = Yaui.Length.Percent(length * 100f);
            }
            else
            {
                layout.inset.left = reversed ? Yaui.Length.Auto : from;
                layout.inset.right = reversed ? from : Yaui.Length.Auto;
                layout.margin.left = reversed ? layout.margin.left : margin;
                layout.margin.right = reversed ? margin : layout.margin.right;
                if (length >= 0f) layout.width = Yaui.Length.Percent(length * 100f);
            }

            // Writing the same layout would still re-run the layout.
            if (!layout.Equals(element.Layout)) element.Layout = layout;
        }

        /// <summary>A fill from the start of the track to <paramref name="fraction"/>, covering the other axis.</summary>
        public static void Fill(YauiElement element, TrackDirection direction, float fraction)
        {
            var layout = element.Layout;
            layout.position = PositionType.Absolute;
            var full = Yaui.Length.Points(0f);
            var size = Yaui.Length.Percent(fraction * 100f);
            if (IsVertical(direction))
            {
                layout.inset.left = full;
                layout.inset.right = full;
                layout.width = Yaui.Length.Auto;
                layout.height = size;
                layout.inset.top = IsReversed(direction) ? Yaui.Length.Auto : full;
                layout.inset.bottom = IsReversed(direction) ? full : Yaui.Length.Auto;
            }
            else
            {
                layout.inset.top = full;
                layout.inset.bottom = full;
                layout.height = Yaui.Length.Auto;
                layout.width = size;
                layout.inset.left = IsReversed(direction) ? Yaui.Length.Auto : full;
                layout.inset.right = IsReversed(direction) ? full : Yaui.Length.Auto;
            }

            if (!layout.Equals(element.Layout)) element.Layout = layout;
        }

        /// <summary>
        /// Removes what <see cref="Place"/> set on the axis of <paramref name="previous"/>, when the direction
        /// changes to the other axis.
        /// </summary>
        public static void ClearAxis(YauiElement element, TrackDirection previous, TrackDirection next)
        {
            if (element == null || IsVertical(previous) == IsVertical(next)) return;

            var layout = element.Layout;
            var zero = Yaui.Length.Points(0f);
            if (IsVertical(previous))
            {
                layout.inset.top = Yaui.Length.Auto;
                layout.inset.bottom = Yaui.Length.Auto;
                layout.margin.top = zero;
                layout.margin.bottom = zero;
            }
            else
            {
                layout.inset.left = Yaui.Length.Auto;
                layout.inset.right = Yaui.Length.Auto;
                layout.margin.left = zero;
                layout.margin.right = zero;
            }

            element.Layout = layout;
        }

        /// <summary>The parent element of an element, or null.</summary>
        public static YauiElement ParentOf(YauiElement element)
        {
            var parent = element.transform.parent;
            return parent != null && parent.TryGetComponent<YauiElement>(out var e) ? e : null;
        }
    }
}