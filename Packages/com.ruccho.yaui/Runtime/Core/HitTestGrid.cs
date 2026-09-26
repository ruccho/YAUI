using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using Yaui.Rendering;

namespace Yaui.Core
{
    /// <summary>
    /// Point queries over the hittable elements of a panel: their transformed boxes are sorted into a uniform grid
    /// by a Burst job (planning/poc-p1.md, PoC 9). Items are in depth-first (draw) order, so the last match in a
    /// cell is the topmost.
    /// </summary>
    internal sealed class HitTestGrid : IDisposable
    {
        private const int GridSize = 32;

        private static readonly ProfilerMarker BuildMarker = new("Yaui.HitTest.Build");
        private static readonly ProfilerMarker QueryMarker = new("Yaui.HitTest.Query");

        private NativeArray<int> _cellStart = new(GridSize * GridSize + 1, Allocator.Persistent);
        private NativeList<int> _cellItems = new(256, Allocator.Persistent);

        // Per depth-first index, copied when built so that queries stay valid until the next build.
        private NativeList<float2x3> _inverseWorld = new(64, Allocator.Persistent);
        private NativeList<float4> _hitBounds = new(64, Allocator.Persistent);
        private NativeList<float4> _clipRects = new(64, Allocator.Persistent);
        private NativeList<float2> _sizes = new(64, Allocator.Persistent);
        private NativeList<int> _results = new(8, Allocator.Persistent);
        private float2 _canvasSize = 1f;

        /// <summary>Main thread: sorts the hittable elements into the grid.</summary>
        public void Build(NativeArray<int> nodes, NativeArray<int> parents, NativeArray<NodeCpuData> cpu,
            NativeArray<float2x3> world, NativeArray<float4> clipRects, float2 canvasSize)
        {
            using var _ = BuildMarker.Auto();
            this._canvasSize = math.max(canvasSize, 1f);
            var count = nodes.Length;
            _inverseWorld.ResizeUninitialized(count);
            _hitBounds.ResizeUninitialized(count);
            this._clipRects.ResizeUninitialized(count);
            _sizes.ResizeUninitialized(count);
            new BuildJob
            {
                Nodes = nodes,
                Parents = parents,
                Cpu = cpu,
                World = world,
                InheritedClips = clipRects,
                CanvasSize = this._canvasSize,
                InverseWorld = _inverseWorld.AsArray(),
                HitBounds = _hitBounds.AsArray(),
                ClipRects = this._clipRects.AsArray(),
                Sizes = _sizes.AsArray(),
                CellStart = _cellStart,
                CellItems = _cellItems
            }.Run();
        }

        /// <summary>Main thread: the depth-first index of the topmost element at a canvas point, or -1.</summary>
        public int Query(float2 point)
        {
            QueryAll(point);
            return _results.Length > 0 ? _results[0] : -1;
        }

        /// <summary>Main thread: the depth-first indices of all elements at a canvas point, topmost first.</summary>
        public NativeArray<int> QueryAll(float2 point)
        {
            _results.Clear();
            if (_hitBounds.Length == 0) return _results.AsArray();

            using var _ = QueryMarker.Auto();
            new QueryJob
            {
                Point = point,
                CanvasSize = _canvasSize,
                CellStart = _cellStart,
                CellItems = _cellItems.AsArray(),
                InverseWorld = _inverseWorld.AsArray(),
                ClipRects = _clipRects.AsArray(),
                Sizes = _sizes.AsArray(),
                Results = _results
            }.Run();
            return _results.AsArray();
        }

        public void Dispose()
        {
            _cellStart.Dispose();
            _cellItems.Dispose();
            _inverseWorld.Dispose();
            _hitBounds.Dispose();
            _clipRects.Dispose();
            _sizes.Dispose();
            _results.Dispose();
        }

        private static int2 ToCell(float2 p, float2 canvasSize)
        {
            return math.clamp((int2)math.floor(p / canvasSize * GridSize), 0, GridSize - 1);
        }

        [BurstCompile]
        private struct BuildJob : IJob
        {
            [ReadOnly] public NativeArray<int> Nodes;
            [ReadOnly] public NativeArray<int> Parents;
            [ReadOnly] public NativeArray<NodeCpuData> Cpu;
            [ReadOnly] public NativeArray<float2x3> World;

            /// <summary>The clip each depth-first index gives to its children.</summary>
            [ReadOnly] public NativeArray<float4> InheritedClips;

            public float2 CanvasSize;
            public NativeArray<float2x3> InverseWorld;
            public NativeArray<float4> HitBounds;
            public NativeArray<float4> ClipRects;
            public NativeArray<float2> Sizes;
            public NativeArray<int> CellStart;
            public NativeList<int> CellItems;

            public void Execute()
            {
                for (var i = 0; i < CellStart.Length; i++) CellStart[i] = 0;

                // Bounds of each hittable box in canvas space, clipped by the ancestors; empty if not hittable.
                for (var i = 0; i < Nodes.Length; i++)
                {
                    var node = Cpu[Nodes[i]];
                    var parent = Parents[i];
                    var clip = parent >= 0 ? InheritedClips[parent] : ClipGpuData.NoClip;
                    var world = World[i];
                    ClipRects[i] = clip;
                    Sizes[i] = node.LayoutSize;
                    InverseWorld[i] = Inverse(world);

                    var bounds = new float4(1f, 1f, 0f, 0f);
                    if (node.Hittable != 0 && math.all(node.LayoutSize > 0f))
                    {
                        bounds = Bounds(world, node.LayoutSize);
                        bounds = new float4(math.max(bounds.xy, clip.xy), math.min(bounds.zw, clip.zw));
                    }

                    HitBounds[i] = bounds;
                    if (math.any(bounds.xy >= bounds.zw)) continue;

                    var a = ToCell(bounds.xy, CanvasSize);
                    var b = ToCell(bounds.zw, CanvasSize);
                    for (var y = a.y; y <= b.y; y++)
                    for (var x = a.x; x <= b.x; x++)
                        CellStart[y * GridSize + x + 1]++;
                }

                // Counting sort into cells, in depth-first order.
                for (var i = 1; i < CellStart.Length; i++) CellStart[i] += CellStart[i - 1];

                CellItems.ResizeUninitialized(CellStart[CellStart.Length - 1]);
                var cursor = new NativeArray<int>(GridSize * GridSize, Allocator.Temp);
                NativeArray<int>.Copy(CellStart, cursor, cursor.Length);
                for (var i = 0; i < Nodes.Length; i++)
                {
                    var bounds = HitBounds[i];
                    if (math.any(bounds.xy >= bounds.zw)) continue;

                    var a = ToCell(bounds.xy, CanvasSize);
                    var b = ToCell(bounds.zw, CanvasSize);
                    for (var y = a.y; y <= b.y; y++)
                    for (var x = a.x; x <= b.x; x++)
                        CellItems[cursor[y * GridSize + x]++] = i;
                }
            }

            private static float2x3 Inverse(float2x3 m)
            {
                var linear = new float2x2(m.c0, m.c1);
                var det = math.determinant(linear);
                if (math.abs(det) < 1e-12f) return new float2x3(0f, 0f, float.NaN, 0f, 0f, float.NaN);

                var inverse = math.inverse(linear);
                return new float2x3(inverse.c0, inverse.c1, -math.mul(inverse, m.c2));
            }

            private static float4 Bounds(float2x3 world, float2 size)
            {
                var p0 = world.c2;
                var p1 = world.c0 * size.x + world.c2;
                var p2 = world.c1 * size.y + world.c2;
                var p3 = world.c0 * size.x + world.c1 * size.y + world.c2;
                return new float4(math.min(math.min(p0, p1), math.min(p2, p3)),
                    math.max(math.max(p0, p1), math.max(p2, p3)));
            }
        }

        [BurstCompile]
        private struct QueryJob : IJob
        {
            public float2 Point;
            public float2 CanvasSize;
            [ReadOnly] public NativeArray<int> CellStart;
            [ReadOnly] public NativeArray<int> CellItems;
            [ReadOnly] public NativeArray<float2x3> InverseWorld;
            [ReadOnly] public NativeArray<float4> ClipRects;
            [ReadOnly] public NativeArray<float2> Sizes;
            public NativeList<int> Results;

            public void Execute()
            {
                if (math.any((Point < 0f) | (Point >= CanvasSize))) return;

                var c = ToCell(Point, CanvasSize);
                var cell = c.y * GridSize + c.x;
                for (var i = CellStart[cell + 1] - 1; i >= CellStart[cell]; i--)
                {
                    var index = CellItems[i];
                    var clip = ClipRects[index];
                    if (math.any((Point < clip.xy) | (Point >= clip.zw))) continue;

                    // Exact test in the box's local space, so that rotated boxes hit correctly.
                    var inverse = InverseWorld[index];
                    var local = inverse.c0 * Point.x + inverse.c1 * Point.y + inverse.c2;
                    if (math.all((local >= 0f) & (local < Sizes[index]))) Results.Add(index);
                }
            }
        }
    }
}