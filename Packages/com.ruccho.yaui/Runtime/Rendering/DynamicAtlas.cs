using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Yaui.Rendering
{
    /// <summary>
    /// Packs small sprites into shared render textures so that they do not use texture slots of their own (like UI
    /// Toolkit's dynamic atlas). Sprites are drawn into the pages on the GPU, so compressed sources work too (at
    /// the cost of decompression into 8 bits per component).
    /// </summary>
    /// <remarks>
    /// Sprites are excluded if they are larger than <see cref="YauiTextureAtlas.MaxSubTextureSize"/>, readable,
    /// in a format of more than 8 bits per component, in a color space the pages do not match, or tightly packed
    /// in a sprite atlas. Pages of the same filter mode are added as needed; they do not grow or move entries.
    /// </remarks>
    internal sealed class DynamicAtlas : IDisposable
    {
        private const int Padding = 1;

        private static readonly int SourceId = Shader.PropertyToID("_YauiAtlasSource");
        private static readonly int SourceRectId = Shader.PropertyToID("_YauiAtlasSourceRect");
        private static readonly int DestinationRectId = Shader.PropertyToID("_YauiAtlasDestinationRect");
        private static readonly int InnerRectId = Shader.PropertyToID("_YauiAtlasInnerRect");

        private readonly TextureRegistry _registry;
        private readonly List<Page> _pages = new();
        private readonly List<Entry> _entries = new();
        private readonly CommandBuffer _commands = new() { name = "Yaui Atlas" };
        private readonly MaterialPropertyBlock _properties = new();
        private Material _blit;

        public DynamicAtlas(TextureRegistry registry)
        {
            this._registry = registry;
        }

        public List<Page> Pages => _pages;

        public List<Entry> Entries => _entries;

        public sealed class Page
        {
            public RenderTexture Texture;
            public int TextureId;
            public FilterMode FilterMode;
            public ShelfAllocator Allocator;
        }

        public sealed class Entry
        {
            public Page Page;
            public Sprite Sprite;

            /// <summary>The sprite's pixels in the page (without the padding).</summary>
            public RectInt Rect;

            public RectInt Allocation;
        }

        /// <summary>Adds a sprite if it qualifies. Each sprite is added once; the registry counts the references.</summary>
        public bool TryAdd(Sprite sprite, out Entry entry)
        {
            entry = null;
            if (!YauiTextureAtlas.Enabled || !Qualifies(sprite)) return false;

            var texture = sprite.texture;
            var rect = sprite.textureRect;
            var width = Mathf.CeilToInt(rect.width);
            var height = Mathf.CeilToInt(rect.height);
            var filter = texture.filterMode == FilterMode.Point ? FilterMode.Point : FilterMode.Bilinear;
            Page page = null;
            var allocation = default(RectInt);
            foreach (var p in _pages)
                if (p.FilterMode == filter && p.Allocator.TryAllocate(width + Padding * 2, height + Padding * 2,
                        out allocation))
                {
                    page = p;
                    break;
                }

            if (page == null)
            {
                page = CreatePage(filter);
                if (!page.Allocator.TryAllocate(width + Padding * 2, height + Padding * 2, out allocation))
                    return false;
            }

            entry = new Entry
            {
                Page = page,
                Sprite = sprite,
                Allocation = allocation,
                Rect = new RectInt(allocation.x + Padding, allocation.y + Padding, width, height)
            };
            _entries.Add(entry);
            Draw(entry);
            return true;
        }

        public void Remove(Entry entry)
        {
            _entries.Remove(entry);
            entry.Page.Allocator.Free(entry.Allocation);
        }

        private static bool Qualifies(Sprite sprite)
        {
            var texture = sprite.texture;
            if (texture == null || texture.isReadable || texture.dimension != TextureDimension.Tex2D) return false;

            // Tightly packed sprites have no rectangle in their atlas.
            if (sprite.packed && sprite.packingMode == SpritePackingMode.Tight) return false;

            var rect = sprite.textureRect;
            var max = YauiTextureAtlas.MaxSubTextureSize;
            if (rect.width > max || rect.height > max) return false;

            var format = texture.graphicsFormat;
            if (GraphicsFormatUtility.IsHDRFormat(format) || GraphicsFormatUtility.IsIEEE754Format(format) ||
                GraphicsFormatUtility.IsSNormFormat(format) || GraphicsFormatUtility.IsIntegerFormat(format))
                return false;

            // In linear projects the pages are sRGB, so only sRGB textures keep their values.
            return QualitySettings.activeColorSpace != ColorSpace.Linear || texture.isDataSRGB;
        }

        private Page CreatePage(FilterMode filter)
        {
            var size = Mathf.Clamp(Mathf.NextPowerOfTwo(YauiTextureAtlas.PageSize), 64, SystemInfo.maxTextureSize);
            var texture = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32,
                QualitySettings.activeColorSpace == ColorSpace.Linear
                    ? RenderTextureReadWrite.sRGB
                    : RenderTextureReadWrite.Linear)
            {
                name = $"Yaui Atlas {_pages.Count} ({filter})",
                filterMode = filter,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.Create();
            Clear(texture);
            var page = new Page
            {
                Texture = texture,
                FilterMode = filter,
                Allocator = new ShelfAllocator(size, size)
            };
            page.TextureId = _registry.Acquire(texture);
            _pages.Add(page);
            return page;
        }

        private void Clear(RenderTexture texture)
        {
            _commands.Clear();
            _commands.SetRenderTarget(texture);
            _commands.ClearRenderTarget(false, true, Color.clear);
            Graphics.ExecuteCommandBuffer(_commands);
        }

        /// <summary>Draws the sprite into its rect, extending its edges into the padding for bilinear filtering.</summary>
        private void Draw(Entry entry)
        {
            if (_blit == null)
                _blit = new Material(Resources.Load<Shader>("Yaui/AtlasBlit")) { hideFlags = HideFlags.HideAndDontSave };

            var texture = entry.Sprite.texture;
            var source = entry.Sprite.textureRect;
            var page = entry.Page.Texture;
            _properties.Clear();
            _properties.SetTexture(SourceId, texture);
            _properties.SetVector(SourceRectId, new Vector4(source.xMin / texture.width, source.yMin / texture.height,
                source.xMax / texture.width, source.yMax / texture.height));

            // The allocation in normalized device coordinates, and where the sprite's rect lies in it.
            var a = entry.Allocation;
            var r = entry.Rect;
            _properties.SetVector(DestinationRectId, new Vector4(
                (float)a.xMin / page.width, (float)a.yMin / page.height,
                (float)a.xMax / page.width, (float)a.yMax / page.height));
            _properties.SetVector(InnerRectId, new Vector4(
                (float)r.xMin / page.width, (float)r.yMin / page.height,
                (float)r.xMax / page.width, (float)r.yMax / page.height));

            _commands.Clear();
            _commands.SetRenderTarget(page);
            _commands.DrawProcedural(Matrix4x4.identity, _blit, 0, MeshTopology.Triangles, 6, 1, _properties);
            Graphics.ExecuteCommandBuffer(_commands);
        }

        /// <summary>
        /// Main thread: redraws the pages if their contents were lost (render textures can be discarded, e.g. when
        /// the app is paused on mobile).
        /// </summary>
        public void RestoreIfLost()
        {
            foreach (var page in _pages)
            {
                if (page.Texture.IsCreated()) continue;

                page.Texture.Create();
                Clear(page.Texture);
                foreach (var entry in _entries)
                    if (entry.Page == page && entry.Sprite != null)
                        Draw(entry);
            }
        }

        public void Dispose()
        {
            foreach (var page in _pages)
            {
                _registry.Release(page.TextureId);
                page.Texture.Release();
                DestroyObject(page.Texture);
            }

            _pages.Clear();
            _entries.Clear();
            _commands.Release();
            DestroyObject(_blit);
        }

        private static void DestroyObject(Object o)
        {
            if (o == null) return;

            if (Application.isPlaying)
                Object.Destroy(o);
            else
                Object.DestroyImmediate(o);
        }
    }

    /// <summary>
    /// Allocates rectangles in shelves (rows) of power-of-two heights, left to right. Freed spans are merged and
    /// reused within their shelf; an empty shelf at the top is given back.
    /// </summary>
    internal sealed class ShelfAllocator
    {
        private readonly int _width;
        private readonly int _height;
        private readonly List<Shelf> _shelves = new();
        private int _top;

        private sealed class Shelf
        {
            public int Y;
            public int Height;

            // Free spans (x, width), sorted by x.
            public readonly List<Vector2Int> Free = new();
        }

        public ShelfAllocator(int width, int height)
        {
            this._width = width;
            this._height = height;
        }

        public bool TryAllocate(int w, int h, out RectInt rect)
        {
            rect = default;
            if (w > _width || h > _height) return false;

            var shelfHeight = Mathf.Max(8, Mathf.NextPowerOfTwo(h));
            foreach (var shelf in _shelves)
                if (shelf.Height == shelfHeight && TryTake(shelf, w, out var x))
                {
                    rect = new RectInt(x, shelf.Y, w, h);
                    return true;
                }

            if (_top + shelfHeight > _height) return false;

            var added = new Shelf { Y = _top, Height = shelfHeight };
            added.Free.Add(new Vector2Int(0, _width));
            _shelves.Add(added);
            _top += shelfHeight;
            TryTake(added, w, out var first);
            rect = new RectInt(first, added.Y, w, h);
            return true;
        }

        private static bool TryTake(Shelf shelf, int w, out int x)
        {
            for (var i = 0; i < shelf.Free.Count; i++)
            {
                var span = shelf.Free[i];
                if (span.y < w) continue;

                x = span.x;
                if (span.y == w)
                    shelf.Free.RemoveAt(i);
                else
                    shelf.Free[i] = new Vector2Int(span.x + w, span.y - w);

                return true;
            }

            x = 0;
            return false;
        }

        public void Free(RectInt rect)
        {
            foreach (var shelf in _shelves)
            {
                if (shelf.Y != rect.y) continue;

                // Insert and merge with the neighbours.
                var index = 0;
                while (index < shelf.Free.Count && shelf.Free[index].x < rect.x) index++;

                shelf.Free.Insert(index, new Vector2Int(rect.x, rect.width));
                if (index + 1 < shelf.Free.Count &&
                    shelf.Free[index].x + shelf.Free[index].y == shelf.Free[index + 1].x)
                {
                    shelf.Free[index] =
                        new Vector2Int(shelf.Free[index].x, shelf.Free[index].y + shelf.Free[index + 1].y);
                    shelf.Free.RemoveAt(index + 1);
                }

                if (index > 0 && shelf.Free[index - 1].x + shelf.Free[index - 1].y == shelf.Free[index].x)
                {
                    shelf.Free[index - 1] = new Vector2Int(shelf.Free[index - 1].x,
                        shelf.Free[index - 1].y + shelf.Free[index].y);
                    shelf.Free.RemoveAt(index);
                }

                // An empty topmost shelf can take another height.
                while (_shelves.Count > 0)
                {
                    var last = _shelves[^1];
                    if (last.Free.Count != 1 || last.Free[0].y != _width) break;

                    _top = last.Y;
                    _shelves.RemoveAt(_shelves.Count - 1);
                }

                return;
            }
        }
    }
}