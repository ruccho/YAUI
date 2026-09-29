using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Yaui.Rendering;

namespace Yaui.Core
{
    /// <summary>
    /// Chooses the draws of regions of the draw order: the painter's order as it is, reordered so that primitives of
    /// the same material are drawn together, or reordered by material and shader features so that primitives without
    /// heavy features (such as shadows) are drawn with a lighter variant. The choice weighs the fixed cost of draws
    /// against the cost of the pixels (<see cref="YauiBatching"/>).
    /// </summary>
    /// <remarks>
    /// Reordering keeps the order of every pair that overlaps (on their canvas bounds). Each primitive gets a layer:
    /// the largest over the earlier primitives it overlaps of their layer, plus one if their keys differ. Sorting by
    /// (layer, key, position) keeps the order of overlapping pairs: one of a different key is on a lower layer, one of
    /// the same key on a lower or the same layer and earlier. Primitives with nothing to draw take the latest layer of
    /// their key. Per primitive rather than per element: a glyph that overflows its text would lift the whole text a
    /// layer, and every element after it in a row (60 draws instead of 6 on a grid of overflowing labels).
    /// <para>Scheduled at collection and completed before the stores are written again (PanelState.CompleteReorder):
    /// it reads the primitive, node and clip stores in place.</para>
    /// </remarks>
    [BurstCompile]
    internal struct DrawReorder : IJob
    {
        private const int GridCells = 64;

        // Primitives covering more cells are tested against every primitive instead.
        private const int MaxCellsPerPrimitive = 32;

        /// <summary>Keys combine the material and the shader features: material * FeatureKeys + features.</summary>
        public const int FeatureKeys = ShaderFeaturesExtensions.Count;

        /// <summary>A run of the draw order drawn as a whole.</summary>
        public struct Region
        {
            /// <summary>Draw positions of the region.</summary>
            public int Start;

            public int Count;

            /// <summary>Its draws in the painter's order in <see cref="Segments"/>.</summary>
            public int SegmentsStart;

            public int SegmentCount;

            /// <summary>The number of materials, numbered from zero by first appearance in the region.</summary>
            public int MaterialCount;
        }

        /// <summary>Costs of the choice (<see cref="YauiBatching"/>), in microseconds.</summary>
        public struct Costs
        {
            public float DrawGpu;
            public float DrawCpu;

            /// <summary>Per pixel, by shader features (<see cref="ShaderFeatures"/>).</summary>
            public FixedList128Bytes<float> Pixel;

            public float GpuWeight;
            public float CpuWeight;

            /// <summary>Pixel savings count for 1 / margin: splitting has to save that much more than it costs.</summary>
            public float Margin;

            public bool SplitByFeatures;

            /// <summary>The transforms changed: the choice will likely run again next frame.</summary>
            public bool Moving;

            /// <summary>The cost of reordering a region once, per primitive (CPU, on a worker thread).</summary>
            public float LayeringPerPrimitive;

            /// <summary>Screen pixels per canvas unit, squared.</summary>
            public float PixelsPerUnitSquared;
        }

        [ReadOnly] public NativeArray<Region> Regions;

        /// <summary>Primitive slots of the panel in the painter's order.</summary>
        [ReadOnly] public NativeArray<uint> PainterOrder;

        /// <summary>
        /// The draws of the painter's order (x: start in the region, y: count, z: material, w: 1 if it is drawn with
        /// the uber shader, whose variants follow the shader features).
        /// </summary>
        [ReadOnly] public NativeArray<int4> Segments;

        public float2 CanvasSize;
        public Costs Cost;

        [ReadOnly] public NativeArray<PrimitiveData> Primitives;
        [ReadOnly] public NativeArray<PrimitiveExt> Exts;
        [ReadOnly] public NativeArray<NodeGpuData> Nodes;
        [ReadOnly] public NativeArray<ClipGpuData> Clips;

        /// <summary>Output: the draw order, written over the reordered regions.</summary>
        public NativeArray<uint> Order;

        /// <summary>
        /// Output: the draws of the reordered regions in turn (x: start in the region, y: count, z: key, w: number of
        /// textures).
        /// </summary>
        public NativeList<int4> Draws;

        /// <summary>Output: the number of draws of each region, or -1 to keep the painter's order.</summary>
        public NativeList<int> DrawCounts;

        /// <summary>Output: the textures of the draws in turn.</summary>
        public NativeList<int> Textures;

        public void Execute()
        {
            foreach (var region in Regions) DrawCounts.Add(Choose(region));
        }

        /// <summary>Chooses the draws of a region and returns their number, or -1 for the painter's order.</summary>
        private int Choose(Region region)
        {
            var count = region.Count;
            var order = PainterOrder.GetSubArray(region.Start, count);
            var segments = Segments.GetSubArray(region.SegmentsStart, region.SegmentCount);
            var materialsMerge = region.SegmentCount > region.MaterialCount;

            // Without draws to merge or heavy features to split off, the painter's order stays (reading the flags
            // only: most panels, every frame something moves).
            if (!materialsMerge && !(Cost.SplitByFeatures && HasHeavyFeatures(order, segments))) return -1;

            var materials = new NativeArray<int>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var features = new NativeArray<int>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var bounds = new NativeArray<float4>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var areas = new NativeArray<float>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            // The painter's order: its draws and the pixels of each at the variant of its draw. And for each set of
            // features to split by (SplitMask), at best, the pixels saved by drawing every primitive without those
            // it does not use.
            var painterPixels = 0f;
            var bestSplit = new float4();
            foreach (var segment in segments)
            {
                var lastNode = uint.MaxValue;
                var node = default(NodeGpuData);
                var union = 0;
                var area = 0f;
                for (var i = segment.x; i < segment.x + segment.y; i++)
                {
                    var p = Primitives[(int)order[i]];
                    if (p.Node != lastNode)
                    {
                        lastNode = p.Node;
                        node = Nodes[(int)p.Node];
                    }

                    var b = Bounds(p, node);
                    var a = b.x < b.z && b.y < b.w ? (b.z - b.x) * (b.w - b.y) * Cost.PixelsPerUnitSquared : 0f;
                    var f = segment.w != 0 ? (int)ShaderFeaturesExtensions.Of(p.Flags) : 0;
                    materials[i] = segment.z;
                    features[i] = f;
                    bounds[i] = b;
                    areas[i] = a;
                    union |= f;
                    area += a;
                }

                painterPixels += area * Cost.Pixel[union];
                for (var i = segment.x; i < segment.x + segment.y; i++)
                for (var m = 1; m < 4; m++)
                {
                    var mask = SplitMask(m);
                    bestSplit[m] += areas[i] * (Cost.Pixel[union] - Cost.Pixel[(union & ~mask) | (features[i] & mask)]);
                }
            }

            var drawCost = Cost.DrawGpu * Cost.GpuWeight + Cost.DrawCpu * Cost.CpuWeight;
            var bestCost = region.SegmentCount * drawCost + painterPixels * Cost.GpuWeight / Cost.Margin;
            var best = -1;
            var reordered = new NativeArray<uint>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var draws = new NativeList<int4>(16, Allocator.Temp);
            var textures = new NativeList<int>(16, Allocator.Temp);

            // By material, then also by the heavier features if that could pay for a draw. Splitting by light
            // features (text) would part texts from the boxes under them, for many draws.
            var firstDraw = Draws.Length;
            var firstTexture = Textures.Length;
            var lastSaving = 0f;
            // While the panel moves, the choice runs every frame: an option has to save more than its layering costs.
            var decision = Cost.Moving ? count * Cost.LayeringPerPrimitive * Cost.CpuWeight : 0f;
            var deferred = false;
            for (var m = 0; m < 4; m++)
            {
                if (m == 0 ? !materialsMerge
                        : !Cost.SplitByFeatures || bestSplit[m] * Cost.GpuWeight / Cost.Margin <= drawCost ||
                          (bestSplit[m] - lastSaving) * Cost.GpuWeight / Cost.Margin <= drawCost)
                    continue;

                var saving = m == 0
                    ? (region.SegmentCount - region.MaterialCount) * drawCost
                    : bestSplit[m] * Cost.GpuWeight / Cost.Margin - drawCost;
                if (saving <= decision)
                {
                    deferred = true;
                    continue;
                }

                if (m > 0) lastSaving = bestSplit[m];

                draws.Clear();
                textures.Clear();
                var pixels = Reorder(region, order, materials, features, bounds, areas, SplitMask(m), reordered,
                    draws, textures);
                var cost = draws.Length * drawCost + pixels * Cost.GpuWeight / Cost.Margin;
                if (cost >= bestCost) continue;

                // Replaces the option chosen before, if any.
                bestCost = cost;
                best = draws.Length;
                NativeArray<uint>.Copy(reordered, 0, Order, region.Start, count);
                Draws.ResizeUninitialized(firstDraw);
                Draws.AddRange(draws.AsArray());
                Textures.ResizeUninitialized(firstTexture);
                Textures.AddRange(textures.AsArray());
            }

            return best >= 0 ? best : deferred ? Deferred : -1;
        }

        /// <summary>
        /// A result of <see cref="DrawCounts"/>: the painter's order for now, as the panel moves; choose again when it
        /// stops.
        /// </summary>
        public const int Deferred = -2;

        /// <summary>
        /// The sets of features draws split by, from none: shadows, shadows and borders (with radial fills), all.
        /// </summary>
        private static int SplitMask(int index)
        {
            return index switch
            {
                0 => 0,
                1 => (int)ShaderFeatures.Shadow,
                2 => (int)(ShaderFeatures.Shadow | ShaderFeatures.Border),
                _ => (int)ShaderFeatures.All
            };
        }

        /// <summary>Whether some primitives of the uber shader in the region use shadows or borders.</summary>
        private bool HasHeavyFeatures(NativeArray<uint> order, NativeArray<int4> segments)
        {
            const PrimitiveFlags heavy = PrimitiveFlags.Shadow | PrimitiveFlags.Border | PrimitiveFlags.RadialFill;
            foreach (var segment in segments)
            {
                if (segment.w == 0) continue;

                for (var i = segment.x; i < segment.x + segment.y; i++)
                    if ((Primitives[(int)order[i]].Flags & heavy) != 0)
                        return true;
            }

            return false;
        }

        /// <summary>
        /// Reorders a region by material and by the features in <paramref name="mask"/> into
        /// <paramref name="reordered"/>, splits it into draws, and returns the cost of its pixels.
        /// </summary>
        private float Reorder(Region region, NativeArray<uint> order, NativeArray<int> materials,
            NativeArray<int> features, NativeArray<float4> bounds, NativeArray<float> areas, int mask,
            NativeArray<uint> reordered, NativeList<int4> draws, NativeList<int> textures)
        {
            var count = region.Count;
            var keyCount = region.MaterialCount * FeatureKeys;
            var keys = new NativeArray<int>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (var i = 0; i < count; i++) keys[i] = materials[i] * FeatureKeys + (features[i] & mask);

            var layers = new NativeArray<int>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var lastLayer = new NativeArray<int>(keyCount, Allocator.Temp);
            var heads = new NativeArray<int>(GridCells * GridCells, Allocator.Temp);
            for (var c = 0; c < heads.Length; c++) heads[c] = -1;

            // Per cell, a list of the primitives in it: (position, next entry).
            var entries = new NativeList<int2>(count * 2, Allocator.Temp);
            var large = new NativeList<int>(16, Allocator.Temp);
            var cellSize = math.max(math.cmax(CanvasSize) / GridCells, 1f);
            var maxLayer = 0;
            for (var i = 0; i < count; i++)
            {
                var key = keys[i];
                var b = bounds[i];
                if (!(b.x < b.z && b.y < b.w))
                {
                    layers[i] = lastLayer[key];
                    continue;
                }

                var c0 = math.clamp((int2)math.floor(b.xy / cellSize), 0, GridCells - 1);
                var c1 = math.clamp((int2)math.floor(b.zw / cellSize), 0, GridCells - 1);
                var layer = 0;
                for (var y = c0.y; y <= c1.y; y++)
                for (var x = c0.x; x <= c1.x; x++)
                    for (var e = heads[y * GridCells + x]; e >= 0; e = entries[e].y)
                        layer = math.max(layer, Constraint(i, entries[e].x, bounds, keys, layers));

                foreach (var other in large) layer = math.max(layer, Constraint(i, other, bounds, keys, layers));

                layers[i] = layer;
                lastLayer[key] = layer;
                maxLayer = math.max(maxLayer, layer);

                var covered = c1 - c0 + 1;
                if (covered.x * covered.y > MaxCellsPerPrimitive)
                {
                    large.Add(i);
                    continue;
                }

                for (var y = c0.y; y <= c1.y; y++)
                for (var x = c0.x; x <= c1.x; x++)
                {
                    var cell = y * GridCells + x;
                    entries.Add(new int2(i, heads[cell]));
                    heads[cell] = entries.Length - 1;
                }
            }

            // Counting sort by (layer, key), stable in position.
            var buckets = new NativeArray<int>((maxLayer + 1) * keyCount + 1, Allocator.Temp);
            for (var i = 0; i < count; i++) buckets[layers[i] * keyCount + keys[i] + 1]++;

            for (var b = 1; b < buckets.Length; b++) buckets[b] += buckets[b - 1];

            var sorted = new NativeArray<int>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (var i = 0; i < count; i++) sorted[buckets[layers[i] * keyCount + keys[i]]++] = i;

            // The new order, split where the key changes or a draw would need a ninth texture. The pixels of each draw
            // cost as much as the variant of all its features.
            var pixels = 0f;
            var position = 0;
            var drawKey = -1;
            var drawStart = 0;
            var drawTextures = 0;
            var drawUnion = 0;
            var drawArea = 0f;
            foreach (var i in sorted)
            {
                var key = keys[i];
                var slot = order[i];
                var texture = PrimitiveTexture.IdOf(Primitives[(int)slot].Flags);
                if (key != drawKey || !AddTexture(textures, texture, drawTextures))
                {
                    if (position > drawStart)
                    {
                        draws.Add(new int4(drawStart, position - drawStart, drawKey, textures.Length - drawTextures));
                        pixels += drawArea * Cost.Pixel[drawUnion];
                    }

                    drawKey = key;
                    drawStart = position;
                    drawTextures = textures.Length;
                    drawUnion = 0;
                    drawArea = 0f;
                    AddTexture(textures, texture, drawTextures);
                }

                drawUnion |= features[i];
                drawArea += areas[i];
                reordered[position++] = slot;
            }

            if (position > drawStart)
            {
                draws.Add(new int4(drawStart, position - drawStart, drawKey, textures.Length - drawTextures));
                pixels += drawArea * Cost.Pixel[drawUnion];
            }

            return pixels;
        }

        /// <summary>Adds a texture to the draw whose textures start at <paramref name="first"/>, if it has room.</summary>
        private static bool AddTexture(NativeList<int> textures, int id, int first)
        {
            if (id == 0) return true;

            for (var i = first; i < textures.Length; i++)
                if (textures[i] == id)
                    return true;

            if (textures.Length - first >= TextureRegistry.SlotCount) return false;

            textures.Add(id);
            return true;
        }

        /// <summary>The lowest layer of <paramref name="i"/> given the earlier <paramref name="other"/>.</summary>
        private static int Constraint(int i, int other, NativeArray<float4> bounds, NativeArray<int> keys,
            NativeArray<int> layers)
        {
            var a = bounds[i];
            var b = bounds[other];
            if (!(a.x < b.z && b.x < a.z && a.y < b.w && b.y < a.w)) return 0;

            return layers[other] + (keys[other] != keys[i] ? 1 : 0);
        }

        /// <summary>The canvas bounds (min, max) of what a primitive may cover, empty if it draws nothing.</summary>
        private float4 Bounds(in PrimitiveData p, in NodeGpuData node)
        {
            var rect = p.Rect;
            if (rect.z <= 0f || rect.w <= 0f) return float4.zero;

            // Glyph quads already cover their outlines and shadows (YauiText); skew slants them horizontally.
            var min = rect.xy;
            var max = rect.xy + rect.zw;
            var extent = new float2(math.abs(math.f16tof32(p.BorderWidthAndSkew >> 16)) * rect.w, 0f);
            if ((p.Flags & (PrimitiveFlags.Shadow | PrimitiveFlags.Text)) == PrimitiveFlags.Shadow && p.Ext != 0)
            {
                var shadow = Exts[(int)p.Ext].Shadow;
                var offset = new float2(math.f16tof32(shadow.x & 0xffffu), math.f16tof32(shadow.x >> 16));
                var blurAndSpread = new float2(math.f16tof32(shadow.y & 0xffffu), math.f16tof32(shadow.y >> 16));
                extent += math.abs(offset) + blurAndSpread.x * 3f + blurAndSpread.y;
            }

            min -= extent;
            max += extent;

            var m = node.Matrix;
            var c0 = Transform(m, node.Translation, min);
            var c1 = Transform(m, node.Translation, new float2(max.x, min.y));
            var c2 = Transform(m, node.Translation, new float2(min.x, max.y));
            var c3 = Transform(m, node.Translation, max);
            var result = new float4(math.min(math.min(c0, c1), math.min(c2, c3)),
                math.max(math.max(c0, c1), math.max(c2, c3)));

            var clip = (int)node.Clip;
            if (clip > 0)
            {
                var r = Clips[clip].Rect;
                result = new float4(math.max(result.xy, r.xy), math.min(result.zw, r.zw));
            }

            return result;
        }

        private static float2 Transform(float4 m, float2 translation, float2 p)
        {
            return new float2(m.x * p.x + m.y * p.y, m.z * p.x + m.w * p.y) + translation;
        }
    }
}
