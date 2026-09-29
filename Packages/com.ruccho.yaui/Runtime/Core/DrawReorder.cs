using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Yaui.Rendering;

namespace Yaui.Core
{
    /// <summary>
    /// Reorders regions of the draw order so that primitives of the same batch key (the material of their draw) are
    /// drawn together, keeping the order of every pair that overlaps (on their canvas bounds), and splits the results
    /// into draws.
    /// </summary>
    /// <remarks>
    /// Each primitive gets a layer: the largest over the earlier primitives it overlaps of their layer, plus one if
    /// their keys differ. Sorting by (layer, key, position) keeps the order of overlapping pairs: one of a different
    /// key is on a lower layer, one of the same key on a lower or the same layer and earlier. Primitives with nothing
    /// to draw take the latest layer of their key. Per primitive rather than per element: a glyph that overflows its
    /// text would lift the whole text a layer, and every element after it in a row (60 draws instead of 6 on a grid
    /// of overflowing labels).
    /// <para>Scheduled at collection and completed before the stores are written again (PanelState.CompleteReorder):
    /// it reads the primitive, node and clip stores in place.</para>
    /// </remarks>
    [BurstCompile]
    internal struct DrawReorder : IJob
    {
        private const int GridCells = 64;

        // Primitives covering more cells are tested against every primitive instead.
        private const int MaxCellsPerPrimitive = 32;

        /// <summary>A run of the draw order reordered as a whole.</summary>
        public struct Region
        {
            /// <summary>Draw positions of the region.</summary>
            public int Start;

            public int Count;

            /// <summary>Its draws in the painter's order in <see cref="Segments"/>.</summary>
            public int SegmentsStart;

            public int SegmentCount;

            /// <summary>Keys are numbered from zero by first appearance in each region.</summary>
            public int KeyCount;
        }

        [ReadOnly] public NativeArray<Region> Regions;

        /// <summary>Primitive slots of the panel in the painter's order.</summary>
        [ReadOnly] public NativeArray<uint> PainterOrder;

        /// <summary>The draws of the painter's order (x: start in the region, y: count, z: key).</summary>
        [ReadOnly] public NativeArray<int3> Segments;

        public float2 CanvasSize;

        [ReadOnly] public NativeArray<PrimitiveData> Primitives;
        [ReadOnly] public NativeArray<PrimitiveExt> Exts;
        [ReadOnly] public NativeArray<NodeGpuData> Nodes;
        [ReadOnly] public NativeArray<ClipGpuData> Clips;

        /// <summary>Output: the draw order, written over the regions (a copy of the painter's order elsewhere).</summary>

        public NativeArray<uint> Order;

        /// <summary>
        /// Output: the draws of the regions in turn (x: start in the region, y: count, z: key, w: number of
        /// textures).
        /// </summary>
        public NativeList<int4> Draws;

        /// <summary>Output: the number of draws of each region.</summary>
        public NativeList<int> DrawCounts;

        /// <summary>Output: the textures of the draws in turn.</summary>
        public NativeList<int> Textures;

        public void Execute()
        {
            foreach (var region in Regions)
            {
                var draws = Draws.Length;
                Reorder(region);
                DrawCounts.Add(Draws.Length - draws);
            }
        }

        private void Reorder(Region region)
        {
            var count = region.Count;
            var order = PainterOrder.GetSubArray(region.Start, count);
            var keys = new NativeArray<int>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var bounds = new NativeArray<float4>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            foreach (var segment in Segments.GetSubArray(region.SegmentsStart, region.SegmentCount))
            {
                var lastNode = uint.MaxValue;
                var node = default(NodeGpuData);
                for (var i = segment.x; i < segment.x + segment.y; i++)
                {
                    var p = Primitives[(int)order[i]];
                    if (p.Node != lastNode)
                    {
                        lastNode = p.Node;
                        node = Nodes[(int)p.Node];
                    }

                    keys[i] = segment.z;
                    bounds[i] = Bounds(p, node);
                }
            }

            var layers = new NativeArray<int>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var lastLayer = new NativeArray<int>(region.KeyCount, Allocator.Temp);
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
            var keyCount = region.KeyCount;
            var buckets = new NativeArray<int>((maxLayer + 1) * keyCount + 1, Allocator.Temp);
            for (var i = 0; i < count; i++) buckets[layers[i] * keyCount + keys[i] + 1]++;

            for (var b = 1; b < buckets.Length; b++) buckets[b] += buckets[b - 1];

            var sorted = new NativeArray<int>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (var i = 0; i < count; i++) sorted[buckets[layers[i] * keyCount + keys[i]]++] = i;

            // The new order, split where the key changes or a draw would need a ninth texture.
            var reordered = Order.GetSubArray(region.Start, count);
            var position = 0;
            var drawKey = -1;
            var drawStart = 0;
            var drawTextures = 0;
            foreach (var i in sorted)
            {
                var key = keys[i];
                var slot = order[i];
                var texture = PrimitiveTexture.IdOf(Primitives[(int)slot].Flags);
                if (key != drawKey || !AddTexture(texture, drawTextures))
                {
                    if (position > drawStart)
                        Draws.Add(new int4(drawStart, position - drawStart, drawKey, Textures.Length - drawTextures));

                    drawKey = key;
                    drawStart = position;
                    drawTextures = Textures.Length;
                    AddTexture(texture, drawTextures);
                }

                reordered[position++] = slot;
            }

            if (position > drawStart)
                Draws.Add(new int4(drawStart, position - drawStart, drawKey, Textures.Length - drawTextures));
        }

        /// <summary>Adds a texture to the draw whose textures start at <paramref name="first"/>, if it has room.</summary>
        private bool AddTexture(int id, int first)
        {
            if (id == 0) return true;

            for (var i = first; i < Textures.Length; i++)
                if (Textures[i] == id)
                    return true;

            if (Textures.Length - first >= TextureRegistry.SlotCount) return false;

            Textures.Add(id);
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
