using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Yaui.Rendering;

namespace Yaui.Core
{
    /// <summary>
    /// Whether any node of a panel needs per-pixel clipping: it is rotated or skewed (the vertex shader can only
    /// shrink axis-aligned quads to the clip rect), or clipped by a rounded clip.
    /// </summary>
    [BurstCompile]
    internal struct PixelClipScan : IJob
    {
        [ReadOnly] public NativeArray<int> Nodes;
        [ReadOnly] public NativeArray<NodeGpuData> Gpu;
        [ReadOnly] public NativeArray<ClipGpuData> Clips;
        public NativeArray<int> Result;

        public void Execute()
        {
            Result[0] = 0;
            foreach (var slot in Nodes)
            {
                var node = Gpu[slot];
                if (node.Matrix.y != 0f || node.Matrix.z != 0f ||
                    (node.Clip > 0 && Clips[(int)node.Clip].Rounded.z > 0f))
                {
                    Result[0] = 1;
                    return;
                }
            }
        }
    }

    /// <summary>The shader features each draw segment uses: the union over the flags of its primitives.</summary>
    [BurstCompile]
    internal struct FeatureScan : IJob
    {
        [ReadOnly] public NativeArray<uint> Order;
        [ReadOnly] public NativeArray<PrimitiveData> Primitives;

        /// <summary>Draw positions of the segments (x: start, y: count).</summary>
        [ReadOnly] public NativeArray<int2> Segments;

        public NativeArray<ShaderFeatures> Result;

        public void Execute()
        {
            for (var s = 0; s < Segments.Length; s++)
            {
                var flags = PrimitiveFlags.None;
                var range = Segments[s];
                for (var i = range.x; i < range.x + range.y; i++) flags |= Primitives[(int)Order[i]].Flags;

                Result[s] = ShaderFeaturesExtensions.Of(flags);
            }
        }
    }

    /// <summary>
    /// Propagates world transforms, opacity and clips from the root of a panel to its leaves, in depth-first order
    /// (parents before children), and writes the node and clip records. Only the given ranges of depth-first
    /// indices (whole subtrees) are computed; their parents' results from earlier passes are reused.
    /// </summary>
    [BurstCompile]
    internal struct TransformPass : IJob
    {
        /// <summary>Node slots in depth-first order.</summary>
        [ReadOnly] public NativeArray<int> Nodes;

        /// <summary>Depth-first index of the parent, or -1 for the root.</summary>
        [ReadOnly] public NativeArray<int> Parents;

        /// <summary>Ranges of depth-first indices to compute (x: start, y: end), each a whole subtree.</summary>
        [ReadOnly] public NativeArray<int2> Ranges;

        [ReadOnly] public NativeArray<NodeCpuData> Cpu;

        public NativeArray<NodeGpuData> Gpu;
        public NativeArray<ulong> GpuDirty;
        public NativeArray<ClipGpuData> Clips;
        public NativeArray<ulong> ClipsDirty;

        // Per depth-first index.
        public NativeArray<float2x3> World;
        public NativeArray<float> Opacity;
        public NativeArray<int> ClipSlot;
        public NativeArray<float4> ClipRect;

        public void Execute()
        {
            foreach (var range in Ranges)
                for (var i = range.x; i < range.y; i++)
                    Compute(i);
        }

        private void Compute(int i)
        {
            {
                var slot = Nodes[i];
                var node = Cpu[slot];
                var parent = Parents[i];

                var parentWorld = parent >= 0 ? World[parent] : Identity;
                var parentOpacity = parent >= 0 ? Opacity[parent] : 1f;
                var parentClip = parent >= 0 ? ClipSlot[parent] : 0;
                var parentClipRect = parent >= 0 ? ClipRect[parent] : ClipGpuData.NoClip;

                var world = Mul(parentWorld, Local(node));
                var opacity = parentOpacity * node.Opacity;
                World[i] = world;
                Opacity[i] = opacity;

                Gpu[slot] = new NodeGpuData
                {
                    Matrix = new float4(world.c0.x, world.c1.x, world.c0.y, world.c1.y),
                    Translation = world.c2,
                    OpacityAndClip = NodeGpuData.PackOpacityAndClip(opacity, parentClip),
                    Tint = node.Tint
                };
                GpuStore<NodeGpuData>.MarkDirty(GpuDirty, slot);

                if (node.ClipSlot > 0)
                {
                    // Clips the descendants to the box. Rotated boxes clip to their bounds for now.
                    var bounds = Bounds(world, node.LayoutSize);
                    var rect = Intersect(parentClipRect, bounds);
                    var clip = new ClipGpuData { Rect = rect };
                    var axisAligned = world.c0.y == 0f && world.c1.x == 0f;
                    if (axisAligned && math.any(node.ClipRadii > 0f))
                    {
                        // Rounded corners of this box, in canvas units.
                        clip.Rounded = new float4((bounds.xy + bounds.zw) * 0.5f, (bounds.zw - bounds.xy) * 0.5f);
                        clip.RoundedRadii = node.ClipRadii * math.min(math.abs(world.c0.x), math.abs(world.c1.y));
                    }
                    else if (parentClip > 0)
                    {
                        // Keeps the nearest rounded clip of the ancestors.
                        var parentRecord = Clips[parentClip];
                        clip.Rounded = parentRecord.Rounded;
                        clip.RoundedRadii = parentRecord.RoundedRadii;
                    }

                    Clips[node.ClipSlot] = clip;
                    GpuStore<ClipGpuData>.MarkDirty(ClipsDirty, node.ClipSlot);
                    ClipSlot[i] = node.ClipSlot;
                    ClipRect[i] = rect;
                }
                else
                {
                    ClipSlot[i] = parentClip;
                    ClipRect[i] = parentClipRect;
                }
            }
        }

        private static float2x3 Identity => new(1f, 0f, 0f, 0f, 1f, 0f);

        /// <summary>
        /// Layout position, then the CSS transform around the pivot: translate, rotate, scale.
        /// </summary>
        private static float2x3 Local(in NodeCpuData node)
        {
            math.sincos(node.Rotation, out var s, out var c);
            var linear = new float2x2(c, -s, s, c);
            linear = math.mul(linear, float2x2.Scale(node.Scale));
            var pivot = node.Pivot * node.LayoutSize;
            var translation = node.LayoutPosition + node.Translate + pivot - math.mul(linear, pivot);
            return new float2x3(linear.c0, linear.c1, translation);
        }

        private static float2x3 Mul(float2x3 a, float2x3 b)
        {
            var linearA = new float2x2(a.c0, a.c1);
            return new float2x3(math.mul(linearA, b.c0), math.mul(linearA, b.c1), math.mul(linearA, b.c2) + a.c2);
        }

        private static float2 Transform(float2x3 m, float2 p)
        {
            return m.c0 * p.x + m.c1 * p.y + m.c2;
        }

        private static float4 Bounds(float2x3 world, float2 size)
        {
            var p0 = Transform(world, 0f);
            var p1 = Transform(world, new float2(size.x, 0f));
            var p2 = Transform(world, new float2(0f, size.y));
            var p3 = Transform(world, size);
            return new float4(math.min(math.min(p0, p1), math.min(p2, p3)),
                math.max(math.max(p0, p1), math.max(p2, p3)));
        }

        private static float4 Intersect(float4 a, float4 b)
        {
            return new float4(math.max(a.xy, b.xy), math.min(a.zw, b.zw));
        }
    }
}