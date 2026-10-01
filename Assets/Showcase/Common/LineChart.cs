using System.Collections.Generic;
using UnityEngine;

namespace Yaui.Showcase
{
    /// <summary>
    /// A line chart with filled areas, drawn as a mesh in the draw order of its element (a custom draw): lines are
    /// not quads, so they leave the batch of the uber shader. The mesh follows the element's transform, opacity and
    /// clips with the Yaui/Particle shader.
    /// </summary>
    public sealed class LineChart : YauiCustomDraw
    {
        public sealed class Series
        {
            public Color Color;
            public float Width = 2.5f;
            public float Fill = 0.2f;

            /// <summary>Values in 0..1 of the height, oldest first.</summary>
            public readonly List<float> Values = new();
        }

        public readonly List<Series> AllSeries = new();

        /// <summary>The material of the mesh: a shader of YAUI's custom draws with a white texture.</summary>
        public Material Material;

        /// <summary>How far the newest point has moved in from the right, in 0..1 of a step: scrolls smoothly.</summary>
        public float Phase = 1f;

        private Mesh _mesh;
        private readonly List<Vector3> _vertices = new();
        private readonly List<Color> _colors = new();
        private readonly List<Vector2> _uvs = new();
        private readonly List<int> _indices = new();
        private readonly List<Vector2> _points = new();

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }

        protected override void OnCollectDraws(YauiDrawList draws)
        {
            if (Material == null || AllSeries.Count == 0) return;

            var size = Element.LayoutRect.size;
            if (size.x <= 0f || size.y <= 0f) return;

            _vertices.Clear();
            _colors.Clear();
            _uvs.Clear();
            _indices.Clear();
            foreach (var series in AllSeries) Build(series, size);

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "LineChart", hideFlags = HideFlags.HideAndDontSave };
                _mesh.MarkDynamic();
            }

            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetTriangles(_indices, 0, false);
            // The mesh is in the local space of the element's box: canvas units, Y down.
            draws.DrawMesh(_mesh, Material, Matrix4x4.identity);
        }

        private void Build(Series series, Vector2 size)
        {
            var count = series.Values.Count;
            if (count < 3) return;

            // The first and the last steps are partly outside the box: the element's parent clips them.
            var step = size.x / (count - 3);
            _points.Clear();
            for (var i = 0; i < count; i++)
                _points.Add(new Vector2((i - Phase) * step, (1f - series.Values[i]) * size.y));

            if (series.Fill > 0f)
            {
                var top = series.Color;
                top.a = series.Fill;
                var bottom = series.Color;
                bottom.a = 0f;
                var first = _vertices.Count;
                foreach (var p in _points)
                {
                    Add(p, Color.Lerp(top, bottom, p.y / size.y));
                    Add(new Vector2(p.x, size.y), bottom);
                }

                for (var i = 0; i < count - 1; i++) Quad(first + i * 2, first + i * 2 + 1, first + i * 2 + 2, first + i * 2 + 3);
            }

            // The line: a solid core with a feathered pixel on both sides.
            var clear = series.Color;
            clear.a = 0f;
            var half = series.Width * 0.5f;
            var start = _vertices.Count;
            for (var i = 0; i < count; i++)
            {
                var previous = _points[Mathf.Max(i - 1, 0)];
                var next = _points[Mathf.Min(i + 1, count - 1)];
                var direction = (next - previous).normalized;
                var normal = new Vector2(-direction.y, direction.x);
                var p = _points[i];
                Add(p - normal * (half + 1f), clear);
                Add(p - normal * half, series.Color);
                Add(p + normal * half, series.Color);
                Add(p + normal * (half + 1f), clear);
            }

            for (var i = 0; i < count - 1; i++)
            for (var k = 0; k < 3; k++)
            {
                var a = start + i * 4 + k;
                Quad(a, a + 1, a + 4, a + 5);
            }
        }

        private void Add(Vector2 position, Color color)
        {
            _vertices.Add(position);
            _colors.Add(color);
            _uvs.Add(new Vector2(0.5f, 0.5f));
        }

        private void Quad(int a, int b, int c, int d)
        {
            _indices.Add(a);
            _indices.Add(b);
            _indices.Add(c);
            _indices.Add(c);
            _indices.Add(b);
            _indices.Add(d);
        }
    }
}
