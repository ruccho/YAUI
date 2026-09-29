using System.Collections.Generic;
using UnityEngine;
using Yaui.Core;

namespace Yaui
{
    /// <summary>
    /// Settings of the dynamic atlas, which packs small sprites into shared textures so that more images are drawn
    /// in one draw call (like UI Toolkit's dynamic atlas). Changes apply to sprites added afterwards.
    /// </summary>
    public static class YauiTextureAtlas
    {
        /// <summary>Whether small sprites are packed at all.</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>Sprites up to this size (in texels, both sides) are packed.</summary>
        public static int MaxSubTextureSize { get; set; } = 64;

        /// <summary>Size of an atlas page (a power of two). More pages are added as needed.</summary>
        public static int PageSize { get; set; } = 1024;

        /// <summary>Adds the pages of the atlas to <paramref name="pages"/> (for debugging).</summary>
        public static void GetPages(List<Texture> pages)
        {
            if (!YauiSystem.IsInitialized) return;

            foreach (var page in YauiSystem.Textures.Atlas.Pages) pages.Add(page.Texture);
        }

        /// <summary>Adds the sprites packed in the atlas to <paramref name="entries"/> (for debugging).</summary>
        public static void GetEntries(List<YauiAtlasEntry> entries)
        {
            if (!YauiSystem.IsInitialized) return;

            foreach (var entry in YauiSystem.Textures.Atlas.Entries)
                entries.Add(new YauiAtlasEntry(entry.Sprite, entry.Page.Texture, entry.Rect));
        }
    }

    /// <summary>A sprite packed in the dynamic atlas.</summary>
    public readonly struct YauiAtlasEntry
    {
        public readonly Sprite Sprite;

        /// <summary>The page the sprite is in.</summary>
        public readonly Texture Page;

        /// <summary>The sprite's texels in the page (origin at the bottom-left), without the padding.</summary>
        public readonly RectInt Rect;

        public YauiAtlasEntry(Sprite sprite, Texture page, RectInt rect)
        {
            Sprite = sprite;
            Page = page;
            Rect = rect;
        }
    }
}
