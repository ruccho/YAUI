using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TextCore;
using TextGenerationSettings = UnityEngine.TextCore.Generation.TextGenerationSettings;
using TextGenerator = UnityEngine.TextCore.Generation.TextGenerator;

namespace Yaui.Text
{
    /// <summary>
    /// Text through the public <see cref="TextGenerator"/>, when the internal ATG APIs do not match this Unity
    /// version (<see cref="AtgText.IsSupported"/>). Main thread only and synchronous: the layout measures the last
    /// generation, so wrapped text settles one frame later.
    /// </summary>
    internal sealed class FallbackText
    {
        private static readonly List<Vector3> Vertices = new();
        private static readonly List<Vector2> Uvs = new();
        private static readonly List<Color32> Colors = new();
        private static readonly List<int> Indices = new();

        private Mesh _mesh;
        private Material[] _materials;
        private string _text;
        private TextGenerationSettings _settings;

        public bool IsGenerated { get; private set; }

        /// <summary>The width of the last generation, or a negative value if unconstrained.</summary>
        public float GeneratedWidth { get; private set; } = -1f;

        public float2 Size { get; private set; }

        /// <summary>Main thread: sets what to generate.</summary>
        public void Prepare(string value, Font font, float fontSize, Color color, TextAlign align, bool wordWrap,
            bool richText)
        {
            _text = value ?? string.Empty;
            _settings = TextGenerationSettings.Default;
            _settings.text = _text;
            _settings.font = font;
            _settings.fontSize = fontSize;
            _settings.color = color;
            _settings.richText = richText;
            _settings.wrapMode = wordWrap ? TextWrapMode.Wrap : TextWrapMode.NoWrap;
            _settings.horizontalAlignment = align switch
            {
                TextAlign.Center => HorizontalAlignment.Center,
                TextAlign.Right => HorizontalAlignment.Right,
                TextAlign.Justified => HorizontalAlignment.Justified,
                _ => HorizontalAlignment.Left
            };
            _settings.verticalAlignment = VerticalAlignment.Top;
            IsGenerated = false;
        }

        /// <summary>Main thread: generates within <paramref name="width"/> (negative: unconstrained).</summary>
        public void Generate(float width)
        {
            _settings.extents = new Vector2(width >= 0f ? width : -1f, -1f);
            var result = TextGenerator.GenerateText(_settings);
            _mesh ??= new Mesh { hideFlags = HideFlags.HideAndDontSave };
            _mesh.Clear();
            result.FillMesh(_mesh);
            _materials = result.materials;
            Size = result.size;
            GeneratedWidth = width >= 0f ? width : -1f;
            IsGenerated = true;
        }

        /// <summary>Main thread: the glyph quads of the last generation (4 vertices each: TL, TR, BR, BL; Y up).</summary>
        public void Convert(List<GlyphQuad> output)
        {
            output.Clear();
            if (_mesh == null || _materials == null) return;

            _mesh.GetVertices(Vertices);
            _mesh.GetUVs(0, Uvs);
            _mesh.GetColors(Colors);
            for (var sub = 0; sub < _mesh.subMeshCount && sub < _materials.Length; sub++)
            {
                var material = _materials[sub];
                var atlas = material != null ? material.mainTexture : null;
                if (atlas == null) continue;

                var spread = material.HasFloat("_GradientScale") ? material.GetFloat("_GradientScale") : 0f;
                _mesh.GetIndices(Indices, sub);
                for (var i = 0; i + 5 < Indices.Count; i += 6)
                {
                    var first = Indices[i];
                    var topLeft = Vertices[first];
                    var topRight = Vertices[first + 1];
                    var bottomRight = Vertices[first + 2];
                    var bottomLeft = Vertices[first + 3];
                    var top = -topLeft.y;
                    var bottom = -bottomLeft.y;
                    output.Add(new GlyphQuad
                    {
                        Min = new float2(bottomLeft.x, top),
                        Max = new float2(bottomRight.x, bottom),
                        Skew = bottom > top ? (topLeft.x - bottomLeft.x) / (bottom - top) : 0f,
                        Uv = new float4(Uvs[first + 3].x, Uvs[first].y, Uvs[first + 2].x, Uvs[first + 3].y),
                        Color = Colors.Count > first ? Colors[first] : (Color32)Color.white,
                        Atlas = atlas,
                        Spread = spread
                    });
                }
            }
        }
    }
}