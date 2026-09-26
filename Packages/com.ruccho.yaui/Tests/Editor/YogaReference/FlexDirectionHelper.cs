#nullable enable annotations
// Vendored for YAUI from microsoft/microsoft-ui-reactor (MIT, see Third Party Notices.md at the package root)
// src/Reactor/Yoga/FlexDirectionHelper.cs at a58008a1be0d, itself a C# port of Meta's Yoga (MIT).
// Changes: namespace, C# 9 compatibility (InlineArray buffers replaced with arrays).

// C# port of Meta's Yoga layout engine FlexDirection utilities.
// Ported from yoga/algorithm/FlexDirection.h


using System;
using System.Collections.Generic;
using Yaui;

namespace Yaui.Tests.YogaReference
{
    /// <summary>
    /// Direction-aware utility functions for resolving flex axes to physical edges.
    /// </summary>
    internal static class FlexDirectionHelper
    {
        public static bool IsRow(FlexDirection flexDirection)
        {
            return flexDirection == FlexDirection.Row || flexDirection == FlexDirection.RowReverse;
        }

        public static bool IsColumn(FlexDirection flexDirection)
        {
            return flexDirection == FlexDirection.Column || flexDirection == FlexDirection.ColumnReverse;
        }

        /// <summary>Apply RTL transformation to flex direction.</summary>
        public static FlexDirection ResolveDirection(FlexDirection flexDirection, FlexLayoutDirection direction)
        {
            if (direction == FlexLayoutDirection.Rtl)
            {
                if (flexDirection == FlexDirection.Row) return FlexDirection.RowReverse;
                if (flexDirection == FlexDirection.RowReverse) return FlexDirection.Row;
            }

            return flexDirection;
        }

        /// <summary>Get the perpendicular (cross) direction.</summary>
        public static FlexDirection ResolveCrossDirection(FlexDirection flexDirection, FlexLayoutDirection direction)
        {
            return IsColumn(flexDirection)
                ? ResolveDirection(FlexDirection.Row, direction)
                : FlexDirection.Column;
        }

        /// <summary>Get the physical edge at the flex-start of an axis.</summary>
        public static YogaPhysicalEdge FlexStartEdge(FlexDirection flexDirection)
        {
            return flexDirection switch
            {
                FlexDirection.Column => YogaPhysicalEdge.Top,
                FlexDirection.ColumnReverse => YogaPhysicalEdge.Bottom,
                FlexDirection.Row => YogaPhysicalEdge.Left,
                FlexDirection.RowReverse => YogaPhysicalEdge.Right,
                _ => throw new ArgumentOutOfRangeException(nameof(flexDirection))
            };
        }

        /// <summary>Get the physical edge at the flex-end of an axis.</summary>
        public static YogaPhysicalEdge FlexEndEdge(FlexDirection flexDirection)
        {
            return flexDirection switch
            {
                FlexDirection.Column => YogaPhysicalEdge.Bottom,
                FlexDirection.ColumnReverse => YogaPhysicalEdge.Top,
                FlexDirection.Row => YogaPhysicalEdge.Right,
                FlexDirection.RowReverse => YogaPhysicalEdge.Left,
                _ => throw new ArgumentOutOfRangeException(nameof(flexDirection))
            };
        }

        /// <summary>Get the inline-start edge (direction-aware).</summary>
        public static YogaPhysicalEdge InlineStartEdge(FlexDirection flexDirection, FlexLayoutDirection direction)
        {
            if (IsRow(flexDirection))
                return direction == FlexLayoutDirection.Rtl ? YogaPhysicalEdge.Right : YogaPhysicalEdge.Left;
            return YogaPhysicalEdge.Top;
        }

        /// <summary>Get the inline-end edge (direction-aware).</summary>
        public static YogaPhysicalEdge InlineEndEdge(FlexDirection flexDirection, FlexLayoutDirection direction)
        {
            if (IsRow(flexDirection))
                return direction == FlexLayoutDirection.Rtl ? YogaPhysicalEdge.Left : YogaPhysicalEdge.Right;
            return YogaPhysicalEdge.Bottom;
        }

        /// <summary>Get the dimension (Width or Height) for a flex direction.</summary>
        public static YogaDimension Dimension(FlexDirection flexDirection)
        {
            return flexDirection switch
            {
                FlexDirection.Column => YogaDimension.Height,
                FlexDirection.ColumnReverse => YogaDimension.Height,
                FlexDirection.Row => YogaDimension.Width,
                FlexDirection.RowReverse => YogaDimension.Width,
                _ => throw new ArgumentOutOfRangeException(nameof(flexDirection))
            };
        }
    }
}