using System;
using UnityEngine;

namespace Yaui.Showcase
{
    public enum Icon
    {
        Star,
        Bolt,
        Shield,
        Flame,
        Crescent,
        Cross,
        Diamond,
        Ring,
        Hexagon,
        Arrow,
        Sword,
        Burst,
        Heart,
        Target,
        Snowflake,
        Circle,
        Check,
        Chevron,
        Search,
        Grid,
        Bars,
        User
    }

    /// <summary>
    /// Icons rasterized from distance functions at startup, so that the showcase needs no art. They are white, tinted
    /// by the image color, and small enough (64 px) for YAUI to pack them into its dynamic atlas.
    /// </summary>
    public static class IconFactory
    {
        private const int Size = 64;

        public static Sprite[] CreateAll()
        {
            var shapes = Shapes();
            var sprites = new Sprite[shapes.Length];
            for (var i = 0; i < shapes.Length; i++) sprites[i] = Rasterize(((Icon)i).ToString(), shapes[i], Size);

            return sprites;
        }

        /// <summary>An icon at a larger size, for art that fills a banner. Too large for the dynamic atlas.</summary>
        public static Sprite CreateLarge(Icon icon, int size = 256)
        {
            return Rasterize(icon + " Large", Shapes()[(int)icon], size);
        }

        // The signed distance to each icon, in -1..1 with Y up.
        private static Func<Vector2, float>[] Shapes()
        {
            return new Func<Vector2, float>[]
            {
                p => Polygon(p, StarPoints(5, 0.9f, 0.4f)),
                p => Polygon(p, V(0.2f, 0.9f), V(-0.45f, -0.05f), V(-0.02f, -0.05f), V(-0.2f, -0.9f),
                    V(0.45f, 0.1f), V(0.02f, 0.1f)),
                p => Polygon(p, V(-0.62f, 0.72f), V(0.62f, 0.72f), V(0.62f, 0f), V(0f, -0.82f), V(-0.62f, 0f)) -
                     0.06f,
                p => Mathf.Min(Circle(p - V(0f, -0.3f), 0.5f),
                    Polygon(p, V(-0.47f, -0.14f), V(0.08f, 0.92f), V(0.47f, -0.14f))),
                p => Mathf.Max(Circle(p, 0.78f), -Circle(p - V(0.36f, 0.16f), 0.64f)),
                p => Mathf.Min(Box(p, V(0.78f, 0.24f)), Box(p, V(0.24f, 0.78f))),
                p => Polygon(p, V(0f, 0.88f), V(0.58f, 0f), V(0f, -0.88f), V(-0.58f, 0f)),
                p => Mathf.Abs(Circle(p, 0.6f)) - 0.17f,
                p => Polygon(p, StarPoints(3, 0.84f, 0.84f)),
                p => Polygon(p, V(0f, 0.86f), V(0.66f, 0.1f), V(0.25f, 0.1f), V(0.25f, -0.8f), V(-0.25f, -0.8f),
                    V(-0.25f, 0.1f), V(-0.66f, 0.1f)),
                p => Mathf.Min(
                    Polygon(p, V(0f, 0.92f), V(0.15f, 0.66f), V(0.15f, -0.3f), V(-0.15f, -0.3f), V(-0.15f, 0.66f)),
                    Mathf.Min(Box(p - V(0f, -0.38f), V(0.46f, 0.085f)), Box(p - V(0f, -0.66f), V(0.085f, 0.24f)))),
                p => Polygon(p, StarPoints(8, 0.9f, 0.42f)),
                p => Mathf.Min(Mathf.Min(Circle(p - V(-0.31f, 0.27f), 0.37f), Circle(p - V(0.31f, 0.27f), 0.37f)),
                    Polygon(p, V(-0.64f, 0.1f), V(0f, 0.3f), V(0.64f, 0.1f), V(0f, -0.78f))),
                p => Mathf.Min(Mathf.Abs(Circle(p, 0.66f)) - 0.1f, Circle(p, 0.22f)),
                p => Mathf.Min(Box(p, V(0.86f, 0.1f)),
                    Mathf.Min(Box(Rotate(p, 60f), V(0.86f, 0.1f)), Box(Rotate(p, 120f), V(0.86f, 0.1f)))),
                p => Circle(p, 0.8f),
                p => Polygon(p, V(-0.78f, 0.02f), V(-0.52f, 0.28f), V(-0.2f, -0.04f), V(0.52f, 0.68f),
                    V(0.78f, 0.42f), V(-0.2f, -0.56f)),
                p => Polygon(p, V(-0.65f, 0.3f), V(-0.45f, 0.5f), V(0f, 0.05f), V(0.45f, 0.5f), V(0.65f, 0.3f),
                    V(0f, -0.35f)),
                p => Mathf.Min(Mathf.Abs(Circle(p - V(-0.15f, 0.15f), 0.45f)) - 0.12f,
                    Box(Rotate(p - V(0.5f, -0.5f), 45f), V(0.32f, 0.11f))),
                p => Mathf.Min(
                    Mathf.Min(Box(p - V(-0.42f, 0.42f), V(0.3f, 0.3f)), Box(p - V(0.42f, 0.42f), V(0.3f, 0.3f))),
                    Mathf.Min(Box(p - V(-0.42f, -0.42f), V(0.3f, 0.3f)), Box(p - V(0.42f, -0.42f), V(0.3f, 0.3f)))) -
                     0.06f,
                p => Mathf.Min(Box(p - V(-0.55f, -0.4f), V(0.17f, 0.35f)),
                    Mathf.Min(Box(p - V(0f, -0.1f), V(0.17f, 0.65f)), Box(p - V(0.55f, -0.25f), V(0.17f, 0.5f)))),
                p => Mathf.Min(Circle(p - V(0f, 0.38f), 0.34f), Box(p - V(0f, -0.5f), V(0.5f, 0.22f)) - 0.1f)
            };
        }

        /// <summary>A soft dot for particles.</summary>
        public static Texture2D CreateGlow(int size = 32)
        {
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var p = new Vector2((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);
                var a = Mathf.Clamp01(1f - p.magnitude);
                pixels[y * size + x] = new Color(1f, 1f, 1f, a * a);
            }

            return CreateTexture("Glow", size, pixels);
        }

        private static Sprite Rasterize(string name, Func<Vector2, float> distance, int size)
        {
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                // -1..1 with Y up, and a margin for the antialiased edge.
                var p = new Vector2((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f) * 1.08f;
                var coverage = Mathf.Clamp01(0.5f - distance(p) * size * 0.5f / 1.08f);
                var shade = Mathf.Lerp(0.78f, 1f, p.y * 0.5f + 0.5f);
                pixels[y * size + x] = new Color(shade, shade, shade, coverage);
            }

            var texture = CreateTexture(name, size, pixels);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size, 0,
                SpriteMeshType.FullRect);
            sprite.name = name;
            return sprite;
        }

        private static Texture2D CreateTexture(string name, int size, Color32[] pixels)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            texture.SetPixels32(pixels);
            // Not readable afterwards: YAUI packs only textures that cannot change into its dynamic atlas.
            texture.Apply(false, true);
            return texture;
        }

        private static Vector2 V(float x, float y)
        {
            return new Vector2(x, y);
        }

        private static Vector2 Rotate(Vector2 p, float degrees)
        {
            var r = degrees * Mathf.Deg2Rad;
            var c = Mathf.Cos(r);
            var s = Mathf.Sin(r);
            return new Vector2(p.x * c - p.y * s, p.x * s + p.y * c);
        }

        private static float Circle(Vector2 p, float radius)
        {
            return p.magnitude - radius;
        }

        private static float Box(Vector2 p, Vector2 half)
        {
            var q = new Vector2(Mathf.Abs(p.x) - half.x, Mathf.Abs(p.y) - half.y);
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f);
        }

        private static Vector2[] StarPoints(int points, float outer, float inner)
        {
            var result = new Vector2[points * 2];
            for (var i = 0; i < result.Length; i++)
            {
                var angle = Mathf.PI * 0.5f + i * Mathf.PI / points;
                var radius = (i & 1) == 0 ? outer : inner;
                result[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }

            return result;
        }

        // Signed distance to a simple polygon.
        private static float Polygon(Vector2 p, params Vector2[] v)
        {
            var d = (p - v[0]).sqrMagnitude;
            var inside = false;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
            {
                var e = v[j] - v[i];
                var w = p - v[i];
                var b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / e.sqrMagnitude);
                d = Mathf.Min(d, b.sqrMagnitude);
                if (v[i].y > p.y != v[j].y > p.y && p.x < e.x * (p.y - v[i].y) / e.y + v[i].x) inside = !inside;
            }

            return inside ? -Mathf.Sqrt(d) : Mathf.Sqrt(d);
        }
    }
}
