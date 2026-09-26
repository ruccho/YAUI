using System;
using UnityEngine;

namespace Yaui
{
    /// <summary>
    /// An element that draws any texture (a render texture, a video, a texture without a sprite) over its content
    /// box, on top of its box. The corner radii of the box also round the image. Textures are never atlased: each
    /// one takes a texture slot of the draw.
    /// </summary>
    [AddComponentMenu("YAUI/Raw Image")]
    public class YauiRawImage : YauiElement
    {
        [SerializeField] private Texture texture;
        [SerializeField] private Color color = Color.white;

        /// <summary>The part of the texture drawn, in UVs within 0..1 (Y up). Textures do not repeat.</summary>
        [SerializeField] private Rect uvRect = new(0f, 0f, 1f, 1f);

        [NonSerialized] private YauiTexture boundTexture;

        public Texture Texture
        {
            get => texture;
            set
            {
                texture = value;
                SyncImage();
            }
        }

        public Color Color
        {
            get => color;
            set
            {
                color = value;
                SyncImage();
            }
        }

        public Rect UvRect
        {
            get => uvRect;
            set
            {
                uvRect = value;
                SyncImage();
            }
        }

        protected override bool HasVisibleContent => texture != null;

        protected override bool ContentIsMaskShape => true;

        protected override void OnValidate()
        {
            base.OnValidate();
            SyncImage();
        }

        protected override void OnDidApplyAnimationProperties()
        {
            base.OnDidApplyAnimationProperties();
            SyncImage();
        }

        protected override void OnRegistered()
        {
            SyncImage();
        }

        protected override void OnUnregistering()
        {
            boundTexture.Release();
            boundTexture = default;
        }

        protected override void OnLayoutApplied()
        {
            SyncImage();
        }

        protected override void OnBoxChanged()
        {
            SyncImage();
        }

        /// <summary>Writes the image primitive from the texture and the laid-out content box.</summary>
        private void SyncImage()
        {
            if (!IsRegistered) return;

            SyncHittable();
            if (texture != boundTexture.Texture)
            {
                boundTexture.Release();
                boundTexture = texture != null ? YauiTexture.Acquire(texture) : default;
            }

            if (texture == null)
            {
                ClearContent();
                return;
            }

            Span<YauiPrimitive> primitive = stackalloc YauiPrimitive[1];
            primitive[0] = YauiPrimitive.Image(ContentBox, boundTexture, uvRect, color)
                .WithCornerRadius(Box.CornerRadius);
            SetContent(primitive);
        }
    }
}
