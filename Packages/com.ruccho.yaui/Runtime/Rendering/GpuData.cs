using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace Yaui.Rendering
{
    /// <summary>Must match the <c>YAUI_FLAG_*</c> defines in Uber.shader.</summary>
    [Flags]
    internal enum PrimitiveFlags : uint
    {
        None = 0,

        /// <summary>Sampled from an SDF font atlas instead of an analytic shape.</summary>
        Text = 1 << 0,

        /// <summary>Draws a border inside the shape. Text: an outline around the glyph (border width, border color).</summary>
        Border = 1 << 1,

        /// <summary>
        /// Draws a drop shadow under the shape (from the extension record). The quad covers the shadow. Text: the
        /// primitive is a shadow of the glyph, dilated by the border width and blurred by the first radius.
        /// </summary>
        Shadow = 1 << 2,

        /// <summary>Synthesized bold for text.</summary>
        TextBold = 1 << 3,

        /// <summary>Multiplies the color by the texel of the texture.</summary>
        Image = 1 << 5,

        /// <summary>
        /// Draws only a sector of the shape (radial fill of images). The border color holds, as halves, the center
        /// (0..1 of the rect, Y down), the start angle and the signed sweep in radians (positive: clockwise).
        /// </summary>
        RadialFill = 1 << 6
    }

    /// <summary>
    /// The features of the uber shader a draw compiles in, one keyword each (YAUI_TEXT, YAUI_IMAGE, YAUI_BORDER,
    /// YAUI_SHADOW in Yaui.hlsl). A draw uses the variant with the features of its primitives only.
    /// </summary>
    [Flags]
    internal enum ShaderFeatures
    {
        None = 0,
        Text = 1 << 0,
        Image = 1 << 1,

        /// <summary>Borders and radial fills (both read the border color).</summary>
        Border = 1 << 2,

        Shadow = 1 << 3,
        All = Text | Image | Border | Shadow
    }

    internal static class ShaderFeaturesExtensions
    {
        public const int Count = 16;

        public static ShaderFeatures Of(PrimitiveFlags flags)
        {
            var features = ShaderFeatures.None;
            if ((flags & PrimitiveFlags.Text) != 0) features |= ShaderFeatures.Text;
            if ((flags & PrimitiveFlags.Image) != 0) features |= ShaderFeatures.Image;
            if ((flags & (PrimitiveFlags.Border | PrimitiveFlags.RadialFill)) != 0) features |= ShaderFeatures.Border;
            if ((flags & PrimitiveFlags.Shadow) != 0) features |= ShaderFeatures.Shadow;
            return features;
        }
    }

    internal static class PrimitiveTexture
    {
        /// <summary>The flags of a primitive sampling the texture of <paramref name="textureId"/> (TextureRegistry).</summary>
        public static PrimitiveFlags With(PrimitiveFlags flags, int textureId)
        {
            return flags | (PrimitiveFlags)((uint)textureId << 16);
        }

        public static int IdOf(PrimitiveFlags flags)
        {
            return (int)((uint)flags >> 16);
        }
    }

    /// <summary>
    /// One quad in node-local space. 64 bytes. Must match <c>PrimitiveData</c> in Uber.shader.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PrimitiveData
    {
        /// <summary>xy: min, zw: size, in the local space of <see cref="Node"/>.</summary>
        public float4 Rect;

        /// <summary>unorm16 x4: UV at the min corner of <see cref="Rect"/> (xy) and at the max corner (zw).</summary>
        public uint2 UvRect;

        /// <summary>half4, linear, straight alpha.</summary>
        public uint2 Color;

        /// <summary>half4. The outline color for text.</summary>
        public uint2 BorderColor;

        /// <summary>half x4: top-left, top-right, bottom-right, bottom-left.</summary>
        public uint2 Radii;

        /// <summary>Index of the node record.</summary>
        public uint Node;

        public PrimitiveFlags Flags;

        /// <summary>half x2: border width, horizontal skew.</summary>
        public uint BorderWidthAndSkew;

        /// <summary>Index of the extension record, or 0.</summary>
        public uint Ext;
    }

    /// <summary>Optional data of a primitive. 32 bytes. Must match <c>PrimitiveExt</c> in Uber.shader.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PrimitiveExt
    {
        /// <summary>half4.</summary>
        public uint2 ShadowColor;

        /// <summary>half x4: offset x, offset y, blur (Gaussian sigma), spread.</summary>
        public uint2 Shadow;

        public uint2 Reserved0;
        public uint2 Reserved1;
    }

    /// <summary>
    /// World (canvas space) transform, inherited opacity, clip and tint of a node. 32 bytes.
    /// Must match <c>NodeData</c> in Uber.shader.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NodeGpuData
    {
        /// <summary>Linear part of the 2x3 matrix. xy: first row (m00, m01), zw: second row (m10, m11).</summary>
        public float4 Matrix;

        public float2 Translation;

        /// <summary>Low 16 bits: the inherited opacity (half). High 16 bits: the index of the clip record, or 0.</summary>
        public uint OpacityAndClip;

        /// <summary>RGBA8, linear: multiplies the colors of the node's own primitives (not of its children).</summary>
        public uint Tint;

        public uint Clip => OpacityAndClip >> 16;

        public static uint PackOpacityAndClip(float opacity, int clip)
        {
            return math.f32tof16(opacity) | ((uint)clip << 16);
        }
    }

    /// <summary>A clip in canvas space. 64 bytes. Must match <c>ClipData</c> in Uber.shader.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ClipGpuData
    {
        /// <summary>xy: min, zw: max. The intersection of the axis-aligned clips of the ancestors.</summary>
        public float4 Rect;

        /// <summary>
        /// The nearest rounded clip, tested per pixel: xy: center, zw: half size (canvas space, axis-aligned boxes
        /// only). Zero size if none. Nested rounded clips beyond the nearest one need stencil masks.
        /// </summary>
        public float4 Rounded;

        /// <summary>Corner radii of <see cref="Rounded"/> in canvas units: top-left, top-right, bottom-right, bottom-left.</summary>
        public float4 RoundedRadii;

        public float4 Reserved;

        public static readonly float4 NoClip = new(-1e7f, -1e7f, 1e7f, 1e7f);
    }

    internal static class GpuPacking
    {
        public static uint Half2(float x, float y)
        {
            return math.f32tof16(x) | (math.f32tof16(y) << 16);
        }

        public static uint2 Half4(float4 v)
        {
            return new uint2(Half2(v.x, v.y), Half2(v.z, v.w));
        }

        private static bool? _linear;

        /// <summary>Converts to linear space when the project uses it, like vertex colors of UI.</summary>
        public static uint2 Color(Color color)
        {
            // The color space does not change at runtime; querying it is a native call.
            _linear ??= QualitySettings.activeColorSpace == ColorSpace.Linear;
            return Half4(_linear.Value ? (Vector4)color.linear : (Vector4)color);
        }

        private static uint Unorm16(float v)
        {
            return (uint)math.round(math.saturate(v) * 65535f);
        }

        private static uint Unorm8(float v)
        {
            return (uint)math.round(math.saturate(v) * 255f);
        }

        /// <summary>A color as RGBA8, converted to linear space like <see cref="Color(UnityEngine.Color)"/>.</summary>
        public static uint Rgba8(Color color)
        {
            _linear ??= QualitySettings.activeColorSpace == ColorSpace.Linear;
            var c = _linear.Value ? color.linear : color;
            return Unorm8(c.r) | (Unorm8(c.g) << 8) | (Unorm8(c.b) << 16) | (Unorm8(c.a) << 24);
        }

        public const uint White8 = 0xffffffffu;

        public static uint2 Unorm16X4(float4 v)
        {
            return new uint2(Unorm16(v.x) | (Unorm16(v.y) << 16), Unorm16(v.z) | (Unorm16(v.w) << 16));
        }
    }
}