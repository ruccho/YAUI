using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Yaui.Rendering
{
    /// <summary>A sprite as drawn: the texture (the sprite's own or a dynamic atlas page) and the sprite's UVs in it.</summary>
    internal struct SpriteTexture
    {
        public int TextureId;

        /// <summary>xy: UV of the bottom-left, zw: UV of the top-right of the sprite's rect.</summary>
        public float4 Uv;

        public float2 TextureSize;

        public bool IsValid => TextureId > 0;
    }

    /// <summary>
    /// Textures by id, as written into primitives (the upper 16 bits of their flags). Each draw call binds up to
    /// <see cref="SlotCount"/> of them; the draw order is split where a run of primitives uses more
    /// (like UI Toolkit's texture slots). Small sprites go into a <see cref="DynamicAtlas"/> so that they share a
    /// texture.
    /// </summary>
    internal sealed class TextureRegistry : IDisposable
    {
        public const int SlotCount = 8;

        private readonly List<Texture> _textures = new() { null };
        private readonly List<Vector4> _parameters = new() { Vector4.zero };
        private readonly List<int> _references = new() { 0 };
        private readonly Stack<int> _free = new();
        private readonly Dictionary<Texture, int> _ids = new();
        private readonly Dictionary<Sprite, AtlasedSprite> _atlasedSprites = new();

        private struct AtlasedSprite
        {
            public DynamicAtlas.Entry Entry;
            public int References;
        }

        public TextureRegistry()
        {
            Atlas = new DynamicAtlas(this);
        }

        public DynamicAtlas Atlas { get; }

        public Texture Get(int id)
        {
            return id > 0 && id < _textures.Count ? _textures[id] : null;
        }

        /// <summary>x: width in texels, y: distance field spread in texels, z: height in texels.</summary>
        public Vector4 Parameters(int id)
        {
            return id > 0 && id < _parameters.Count ? _parameters[id] : Vector4.zero;
        }

        /// <summary>The id of a texture that stays registered (font atlases).</summary>
        public int GetPermanent(Texture texture, float spread)
        {
            var id = Acquire(texture);
            if (_references[id] > 1) _references[id]--;

            _parameters[id] = new Vector4(texture.width, spread, texture.height, 0f);
            return id;
        }

        /// <summary>Marks a texture as a distance field atlas and updates its size (after a resize).</summary>
        public void SetDistanceField(int id, float spread)
        {
            var texture = _textures[id];
            _parameters[id] = new Vector4(texture.width, spread, texture.height, 0f);
        }

        /// <summary>Registers a texture, or adds a reference to it. Released with <see cref="Release"/>.</summary>
        public int Acquire(Texture texture)
        {
            if (_ids.TryGetValue(texture, out var id))
            {
                _references[id]++;
                return id;
            }

            if (_free.Count > 0)
            {
                id = _free.Pop();
                _textures[id] = texture;
                _parameters[id] = new Vector4(texture.width, 0f, texture.height, 0f);
                _references[id] = 1;
            }
            else
            {
                id = _textures.Count;
                if (id > ushort.MaxValue) throw new InvalidOperationException("[YAUI] Too many textures in use.");

                _textures.Add(texture);
                _parameters.Add(new Vector4(texture.width, 0f, texture.height, 0f));
                _references.Add(1);
            }

            _ids[texture] = id;
            return id;
        }

        public void Release(int id)
        {
            if (id <= 0 || id >= _references.Count || _references[id] <= 0 || --_references[id] > 0) return;

            if (_textures[id] != null) _ids.Remove(_textures[id]);

            _textures[id] = null;
            _free.Push(id);
        }

        /// <summary>A sprite to draw: in the dynamic atlas if it qualifies, otherwise its own texture.</summary>
        public SpriteTexture AcquireSprite(Sprite sprite)
        {
            if (_atlasedSprites.TryGetValue(sprite, out var atlased))
            {
                atlased.References++;
                _atlasedSprites[sprite] = atlased;
                return AtlasedTexture(atlased.Entry);
            }

            if (Atlas.TryAdd(sprite, out var entry))
            {
                _atlasedSprites[sprite] = new AtlasedSprite { Entry = entry, References = 1 };
                return AtlasedTexture(entry);
            }

            var texture = sprite.texture;
            var r = sprite.textureRect;
            return new SpriteTexture
            {
                TextureId = Acquire(texture),
                Uv = new float4(r.xMin / texture.width, r.yMin / texture.height, r.xMax / texture.width,
                    r.yMax / texture.height),
                TextureSize = new float2(texture.width, texture.height)
            };
        }

        public void ReleaseSprite(Sprite sprite, SpriteTexture texture)
        {
            if (sprite != null && _atlasedSprites.TryGetValue(sprite, out var atlased))
            {
                if (--atlased.References == 0)
                {
                    _atlasedSprites.Remove(sprite);
                    Atlas.Remove(atlased.Entry);
                }
                else
                {
                    _atlasedSprites[sprite] = atlased;
                }

                return;
            }

            Release(texture.TextureId);
        }

        private SpriteTexture AtlasedTexture(DynamicAtlas.Entry entry)
        {
            var size = new float2(entry.Page.Texture.width, entry.Page.Texture.height);
            return new SpriteTexture
            {
                TextureId = entry.Page.TextureId,
                Uv = new float4(entry.Rect.xMin, entry.Rect.yMin, entry.Rect.xMax, entry.Rect.yMax) / size.xyxy,
                TextureSize = size
            };
        }

        public void Dispose()
        {
            Atlas.Dispose();
        }
    }
}