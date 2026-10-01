using System.Collections.Generic;
using System.Reflection;

namespace Yaui.Showcase
{
    /// <summary>Counts what the showcases draw, for their on-screen statistics.</summary>
    public sealed class ShowcaseStats
    {
        // YAUI has no public counter of primitives, so the showcases read the content count by reflection.
        private readonly FieldInfo _contentCount =
            typeof(YauiElement).GetField("_contentCount", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly List<YauiElement> _elements = new();

        public int Elements { get; private set; }

        /// <summary>The quads in the GPU buffer: the visible boxes and the content (glyphs, images) of the elements.</summary>
        public int Primitives { get; private set; }

        public int Panels { get; private set; }

        /// <summary>The draw calls of the panels, without the meshes of custom draws.</summary>
        public int DrawCalls { get; private set; }

        /// <summary>Counts the elements and their primitives: walks every element, so not every frame.</summary>
        public void CountElements(IReadOnlyList<YauiPanel> panels)
        {
            _elements.Clear();
            foreach (var panel in panels)
                if (panel != null && panel.isActiveAndEnabled)
                    _elements.AddRange(panel.GetComponentsInChildren<YauiElement>());

            Elements = _elements.Count;
            var count = 0;
            foreach (var e in _elements)
            {
                var box = e.Box;
                if (box.backgroundColor.a > 0f || (box.borderWidth > 0f && box.borderColor.a > 0f) ||
                    box.shadowColor.a > 0f)
                    count++;

                if (_contentCount != null) count += (int)_contentCount.GetValue(e);
            }

            Primitives = count;
        }

        public void CountDraws(IReadOnlyList<YauiPanel> panels)
        {
            Panels = 0;
            DrawCalls = 0;
            foreach (var panel in panels)
            {
                if (panel == null || !panel.isActiveAndEnabled) continue;

                Panels++;
                DrawCalls += panel.DrawCallCount;
            }
        }
    }
}
