using System.Collections.Generic;
using UnityEngine;

namespace Yaui.Samples.DynamicAtlas
{
    /// <summary>
    /// Adds and removes images of small sprites, and shows the pages of the dynamic atlas with the rects of the
    /// sprites packed in them. With the atlas on, the images share a few textures and draw in one or two draw calls;
    /// off, each sprite takes a texture slot and the draws split every 8 textures.
    /// </summary>
    public class DynamicAtlasSample : MonoBehaviour
    {
        [SerializeField] YauiPanel panel;
        [SerializeField] Sprite[] sprites;
        [SerializeField] YauiElement images;
        [SerializeField] YauiElement pages;
        [SerializeField] YauiText stats;
        [SerializeField] YauiButton addButton;
        [SerializeField] YauiButton removeButton;
        [SerializeField] YauiButton clearButton;
        [SerializeField] YauiButton atlasButton;
        [SerializeField] YauiText atlasLabel;

        /// <summary>Small pages fill up quickly, so that more pages are added (the default is 1024).</summary>
        [SerializeField] int pageSize = 256;

        [SerializeField] int addCount = 8;
        [SerializeField] int initialCount = 24;
        [SerializeField] float pageViewSize = 256f;
        [SerializeField] float minImageSize = 48f;

        readonly List<YauiImage> _images = new();
        readonly List<Texture> _pages = new();
        readonly List<YauiAtlasEntry> _entries = new();
        readonly List<YauiRawImage> _pageViews = new();
        readonly List<YauiElement> _outlines = new();
        int _previousPageSize;
        int _shownHash;
        string _shownStats;

        void Awake()
        {
            // Pages are created with the size set when they are needed.
            _previousPageSize = YauiTextureAtlas.PageSize;
            YauiTextureAtlas.PageSize = pageSize;
        }

        void OnEnable()
        {
            addButton.OnClick.AddListener(Add);
            removeButton.OnClick.AddListener(Remove);
            clearButton.OnClick.AddListener(Clear);
            atlasButton.OnClick.AddListener(ToggleAtlas);
        }

        void Start()
        {
            for (var i = 0; i < initialCount; i++) AddImage(sprites[i % sprites.Length]);

            UpdateAtlasLabel();
        }

        void OnDisable()
        {
            addButton.OnClick.RemoveListener(Add);
            removeButton.OnClick.RemoveListener(Remove);
            clearButton.OnClick.RemoveListener(Clear);
            atlasButton.OnClick.RemoveListener(ToggleAtlas);
        }

        void OnDestroy()
        {
            YauiTextureAtlas.PageSize = _previousPageSize;
            YauiTextureAtlas.Enabled = true;
        }

        void Add()
        {
            for (var i = 0; i < addCount; i++) AddImage(sprites[Random.Range(0, sprites.Length)]);
        }

        /// <summary>Removes random images. A sprite leaves the atlas with its last image, freeing its rect.</summary>
        void Remove()
        {
            for (var i = 0; i < addCount && _images.Count > 0; i++)
            {
                var index = Random.Range(0, _images.Count);
                Destroy(_images[index].gameObject);
                _images.RemoveAt(index);
            }
        }

        void Clear()
        {
            foreach (var image in _images) Destroy(image.gameObject);

            _images.Clear();
        }

        /// <summary>
        /// The setting applies to sprites added afterwards, so the images let go of their sprites (which leave the
        /// atlas) and take them again.
        /// </summary>
        void ToggleAtlas()
        {
            var bound = new Sprite[_images.Count];
            for (var i = 0; i < _images.Count; i++)
            {
                bound[i] = _images[i].Sprite;
                _images[i].Sprite = null;
            }

            YauiTextureAtlas.Enabled = !YauiTextureAtlas.Enabled;
            for (var i = 0; i < _images.Count; i++) _images[i].Sprite = bound[i];

            UpdateAtlasLabel();
        }

        void UpdateAtlasLabel()
        {
            atlasLabel.Text = YauiTextureAtlas.Enabled ? "Atlas: On" : "Atlas: Off";
        }

        void AddImage(Sprite sprite)
        {
            var go = new GameObject(sprite.name);
            go.transform.SetParent(images.transform, false);
            var image = go.AddComponent<YauiImage>();
            // Tiny pixel art sprites are scaled up so that their texels can be seen.
            var size = sprite.rect.size * Mathf.Max(1f, Mathf.Floor(minImageSize / sprite.rect.width));
            var layout = LayoutStyle.Default;
            layout.width = size.x;
            layout.height = size.y;
            layout.shrink = 0f;
            image.Layout = layout;
            image.RaycastTarget = false;
            image.Sprite = sprite;
            _images.Add(image);
        }

        void Update()
        {
            _pages.Clear();
            _entries.Clear();
            YauiTextureAtlas.GetPages(_pages);
            YauiTextureAtlas.GetEntries(_entries);

            var text = $"Images: {_images.Count}    Packed sprites: {_entries.Count}    " +
                       $"Atlas pages: {_pages.Count}\nDraw calls: {panel.DrawCallCount}";
            if (text != _shownStats)
            {
                _shownStats = text;
                stats.Text = text;
            }

            var hash = _pages.Count;
            foreach (var entry in _entries)
                hash = (hash * 31 + entry.Rect.GetHashCode()) * 31 + _pages.IndexOf(entry.Page);

            if (hash == _shownHash) return;

            _shownHash = hash;
            ShowPages();
        }

        /// <summary>Shows each page with an outline around the rect of each sprite packed in it.</summary>
        void ShowPages()
        {
            while (_pageViews.Count < _pages.Count)
            {
                var go = new GameObject($"Page {_pageViews.Count}");
                go.transform.SetParent(pages.transform, false);
                var view = go.AddComponent<YauiRawImage>();
                var layout = LayoutStyle.Default;
                layout.width = pageViewSize;
                layout.height = pageViewSize;
                view.Layout = layout;
                var box = BoxStyle.Default;
                box.backgroundColor = new Color(0.08f, 0.08f, 0.1f);
                view.Box = box;
                view.RaycastTarget = false;
                _pageViews.Add(view);
            }

            for (var i = 0; i < _pageViews.Count; i++)
            {
                _pageViews[i].gameObject.SetActive(i < _pages.Count);
                if (i < _pages.Count) _pageViews[i].Texture = _pages[i];
            }

            var used = 0;
            foreach (var entry in _entries)
            {
                var page = _pages.IndexOf(entry.Page);
                if (page < 0) continue;

                var outline = used < _outlines.Count ? _outlines[used] : CreateOutline(_pageViews[page].transform);
                used++;
                if (outline.transform.parent != _pageViews[page].transform)
                    outline.transform.SetParent(_pageViews[page].transform, false);

                outline.gameObject.SetActive(true);

                // The rect is in texels from the bottom-left; the canvas goes down from the top-left.
                var scale = pageViewSize / entry.Page.width;
                var r = entry.Rect;
                var layout = outline.Layout;
                layout.inset = new Edges(r.xMin * scale, (entry.Page.height - r.yMax) * scale, Length.Auto,
                    Length.Auto);
                layout.width = r.width * scale;
                layout.height = r.height * scale;
                outline.Layout = layout;
            }

            for (var i = used; i < _outlines.Count; i++) _outlines[i].gameObject.SetActive(false);
        }

        YauiElement CreateOutline(Transform parent)
        {
            var go = new GameObject("Outline");
            go.transform.SetParent(parent, false);
            var outline = go.AddComponent<YauiElement>();
            var layout = LayoutStyle.Default;
            layout.position = PositionType.Absolute;
            outline.Layout = layout;
            var box = BoxStyle.Default;
            box.backgroundColor = Color.clear;
            box.borderWidth = 1f;
            box.borderColor = new Color(1f, 0.85f, 0.2f, 0.9f);
            outline.Box = box;
            outline.RaycastTarget = false;
            _outlines.Add(outline);
            return outline;
        }
    }
}
