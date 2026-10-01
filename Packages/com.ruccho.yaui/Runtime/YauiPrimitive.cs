using System;
using Unity.Mathematics;
using UnityEngine;
using Yaui.Core;
using Yaui.Rendering;

namespace Yaui
{
    /// <summary>
    /// A quad drawn by the uber shader in the draw call of the elements around it: the content of an element that
    /// derives from <see cref="YauiElement"/> (<see cref="YauiElement.SetContent"/>). Built by the static methods;
    /// the <c>With*</c> methods return a modified copy.
    /// </summary>
    /// <remarks>
    /// Rects are in the local space of the element's box: canvas units, origin at the top-left, Y down. UV rects are
    /// in the texture's space (origin at the bottom-left, Y up), like <see cref="YauiRawImage.UvRect"/>: the top-left
    /// of the rect samples (<c>uv.xMin</c>, <c>uv.yMax</c>).
    /// </remarks>
    public struct YauiPrimitive
    {
        internal PrimitiveData Data;

        /// <summary>A filled rectangle, which can have rounded corners and a border.</summary>
        public static YauiPrimitive Rectangle(Rect rect, Color color)
        {
            return new YauiPrimitive
            {
                Data = new PrimitiveData { Rect = ToFloat4(rect), Color = GpuPacking.Color(color) }
            };
        }

        /// <summary>
        /// A texture multiplied by <paramref name="color"/>, which can have rounded corners and a border like
        /// <see cref="Rectangle"/>. Primitives with more than 8 textures in a row split the draw call.
        /// </summary>
        public static YauiPrimitive Image(Rect rect, YauiTexture texture, Rect uv, Color color)
        {
            return Textured(rect, texture, uv, color, PrimitiveFlags.Image);
        }

        /// <summary>
        /// A glyph of a signed distance field atlas registered with <see cref="YauiTexture.AcquireDistanceField"/>:
        /// the alpha channel is the distance to the outline, 0.5 on it. The quad should cover the distance field
        /// around the glyph that outlines and shadows reach.
        /// </summary>
        public static YauiPrimitive DistanceField(Rect rect, YauiTexture texture, Rect uv, Color color)
        {
            return Textured(rect, texture, uv, color, PrimitiveFlags.Text);
        }

        /// <summary>
        /// The shadow of a <see cref="DistanceField"/> glyph: the glyph dilated by <paramref name="dilate"/> and
        /// softened over <paramref name="softness"/> (canvas units), in one color. Offset its rect to move it; put it
        /// before the glyphs so that it is drawn under them.
        /// </summary>
        public static YauiPrimitive DistanceFieldShadow(Rect rect, YauiTexture texture, Rect uv, Color color,
            float dilate, float softness)
        {
            var p = Textured(rect, texture, uv, color, PrimitiveFlags.Text | PrimitiveFlags.Shadow);
            p.Data.Radii = GpuPacking.Half4(new float4(math.max(softness, 0f), 0f, 0f, 0f));
            p.Data.BorderWidthAndSkew = GpuPacking.Half2(math.max(dilate, 0f), 0f);
            return p;
        }

        /// <summary>Rounds the corners (top-left, top-right, bottom-right, bottom-left) of a rectangle or an image.</summary>
        public readonly YauiPrimitive WithCornerRadius(Vector4 radii)
        {
            var p = this;
            if ((p.Data.Flags & PrimitiveFlags.Text) == 0) p.Data.Radii = GpuPacking.Half4(radii);

            return p;
        }

        /// <summary>
        /// A border inside the shape of a rectangle or an image, or an outline around a distance field glyph (as far
        /// as the atlas's spread reaches). Replaces a radial fill.
        /// </summary>
        public readonly YauiPrimitive WithBorder(float width, Color color)
        {
            var p = this;
            if ((p.Data.Flags & PrimitiveFlags.Shadow) != 0) return p;

            p.Data.Flags &= ~PrimitiveFlags.RadialFill;
            if (width > 0f)
                p.Data.Flags |= PrimitiveFlags.Border;
            else
                p.Data.Flags &= ~PrimitiveFlags.Border;

            p.Data.BorderColor = GpuPacking.Color(color);
            p.Data.BorderWidthAndSkew = GpuPacking.Half2(math.max(width, 0f), Skew);
            return p;
        }

        /// <summary>
        /// Draws only a sector of a rectangle or an image, like the radial fills of <see cref="YauiImage"/>.
        /// <paramref name="center"/> is in 0..1 of the rect (Y down); angles are in radians, clockwise on screen from
        /// the right, and a negative sweep goes counter-clockwise. A sweep of a full turn or more draws the whole
        /// shape. Replaces a border.
        /// </summary>
        public readonly YauiPrimitive WithRadialFill(Vector2 center, float startAngle, float sweep)
        {
            var p = this;
            if ((p.Data.Flags & PrimitiveFlags.Text) != 0) return p;

            // The border and the fill share their data: either one replaces the other.
            p.Data.Flags &= ~(PrimitiveFlags.Border | PrimitiveFlags.RadialFill);
            p.Data.BorderWidthAndSkew = GpuPacking.Half2(0f, Skew);

            // A full turn is decided here, before the angles are packed to halves: 2π rounds down to 6.28125, which
            // the shader would draw as a sector with a seam along its start.
            if (math.abs(sweep) >= math.PI * 2f)
            {
                p.Data.BorderColor = default;
                return p;
            }

            p.Data.Flags |= PrimitiveFlags.RadialFill;
            p.Data.BorderColor = GpuPacking.Half4(new float4(center.x, center.y, startAngle, sweep));
            return p;
        }

        /// <summary>
        /// Slants the quad horizontally around the bottom of its rect, like italic glyphs: x moves by
        /// <paramref name="skew"/> times the height above the bottom.
        /// </summary>
        public readonly YauiPrimitive WithSkew(float skew)
        {
            var p = this;
            var width = math.f16tof32(p.Data.BorderWidthAndSkew & 0xffffu);
            p.Data.BorderWidthAndSkew = GpuPacking.Half2(width, skew);
            return p;
        }

        /// <summary>Synthesized bold: dilates a distance field glyph.</summary>
        public readonly YauiPrimitive WithBold()
        {
            var p = this;
            if ((p.Data.Flags & PrimitiveFlags.Text) != 0) p.Data.Flags |= PrimitiveFlags.TextBold;

            return p;
        }

        /// <summary>The rect in the local space of the element's box.</summary>
        public readonly Rect Rect => new(Data.Rect.x, Data.Rect.y, Data.Rect.z, Data.Rect.w);

        /// <summary>Moves the quad (and keeps its UVs), for example to offset a shadow.</summary>
        public readonly YauiPrimitive WithRect(Rect rect)
        {
            var p = this;
            p.Data.Rect = ToFloat4(rect);
            return p;
        }

        private readonly float Skew => math.f16tof32(Data.BorderWidthAndSkew >> 16);

        private static YauiPrimitive Textured(Rect rect, YauiTexture texture, Rect uv, Color color,
            PrimitiveFlags flags)
        {
            if (!texture.IsValid) throw new ArgumentException("The texture is not acquired.", nameof(texture));

            return new YauiPrimitive
            {
                Data = new PrimitiveData
                {
                    Rect = ToFloat4(rect),
                    UvRect = GpuPacking.Unorm16X4(new float4(uv.xMin, uv.yMax, uv.xMax, uv.yMin)),
                    Color = GpuPacking.Color(color),
                    Flags = PrimitiveTexture.With(flags, texture.Id)
                }
            };
        }

        private static float4 ToFloat4(Rect rect)
        {
            return new float4(rect.x, rect.y, rect.width, rect.height);
        }
    }

    /// <summary>
    /// A texture registered for <see cref="YauiPrimitive"/>s. Reference counted: every acquisition needs a
    /// <see cref="Release"/>, after which the value must not be used. Invalid after the stores shut down (domain
    /// reload), where releasing does nothing.
    /// </summary>
    public readonly struct YauiTexture : IEquatable<YauiTexture>
    {
        internal readonly int Id;
        private readonly int _generation;

        internal YauiTexture(int id)
        {
            Id = id;
            _generation = YauiSystem.Generation;
        }

        public bool IsValid => Id > 0 && YauiSystem.IsInitialized && _generation == YauiSystem.Generation;

        public Texture Texture => IsValid ? YauiSystem.Textures.Get(Id) : null;

        /// <summary>Registers a texture sampled as a color image. Textures are never atlased.</summary>
        public static YauiTexture Acquire(Texture texture)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));

            YauiSystem.EnsureInitialized();
            return new YauiTexture(YauiSystem.Textures.Acquire(texture));
        }

        /// <summary>
        /// Registers a signed distance field atlas for <see cref="YauiPrimitive.DistanceField"/>:
        /// <paramref name="spread"/> is how far (in texels) the field reaches from the outline to 0 or 1. Acquire it
        /// again after the texture was resized, so that the shader sees the new size.
        /// </summary>
        public static YauiTexture AcquireDistanceField(Texture texture, float spread)
        {
            var result = Acquire(texture);
            YauiSystem.Textures.SetDistanceField(result.Id, spread);
            return result;
        }

        public void Release()
        {
            if (IsValid) YauiSystem.Textures.Release(Id);
        }

        public bool Equals(YauiTexture other)
        {
            return Id == other.Id && _generation == other._generation;
        }

        public override bool Equals(object obj)
        {
            return obj is YauiTexture other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Id, _generation);
        }

        public static bool operator ==(YauiTexture a, YauiTexture b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(YauiTexture a, YauiTexture b)
        {
            return !a.Equals(b);
        }
    }

    /// <summary>
    /// A sprite as drawn: small sprites are packed into a shared dynamic atlas, the others use their own texture.
    /// Every acquisition needs a <see cref="Release"/>, like <see cref="YauiTexture"/>.
    /// </summary>
    public readonly struct YauiSpriteTexture
    {
        internal readonly SpriteTexture Value;
        private readonly Sprite _sprite;
        private readonly int _generation;

        private YauiSpriteTexture(Sprite sprite, SpriteTexture value)
        {
            this._sprite = sprite;
            Value = value;
            _generation = YauiSystem.Generation;
        }

        public bool IsValid => Value.IsValid && YauiSystem.IsInitialized && _generation == YauiSystem.Generation;

        public YauiTexture Texture => IsValid ? new YauiTexture(Value.TextureId) : default;

        /// <summary>The sprite's rect in <see cref="Texture"/>, in UVs (origin at the bottom-left).</summary>
        public Rect Uv => Rect.MinMaxRect(Value.Uv.x, Value.Uv.y, Value.Uv.z, Value.Uv.w);

        /// <summary>The size of <see cref="Texture"/> in texels.</summary>
        public Vector2 TextureSize => Value.TextureSize;

        public static YauiSpriteTexture Acquire(Sprite sprite)
        {
            if (sprite == null) throw new ArgumentNullException(nameof(sprite));

            YauiSystem.EnsureInitialized();
            return new YauiSpriteTexture(sprite, YauiSystem.Textures.AcquireSprite(sprite));
        }

        public void Release()
        {
            if (IsValid) YauiSystem.Textures.ReleaseSprite(_sprite, Value);
        }
    }

    /// <summary>How a dimension passed to <see cref="YauiElement.MeasureContent"/> constrains the size.</summary>
    public enum YauiMeasureMode
    {
        /// <summary>No constraint: the natural size. The value is NaN.</summary>
        Undefined = 0,

        /// <summary>The size is the value.</summary>
        Exactly = 1,

        /// <summary>The size is at most the value.</summary>
        AtMost = 2
    }
}
