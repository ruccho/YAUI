using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using Yaui.Rendering;

namespace Yaui.Core
{
    /// <summary>Main thread data of a node that the transform pass reads.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NodeCpuData
    {
        /// <summary>Top-left of the box relative to the parent's box, from the layout.</summary>
        public float2 LayoutPosition;

        public float2 LayoutSize;

        public float2 Translate;

        /// <summary>Radians, clockwise (canvas Y points down).</summary>
        public float Rotation;

        public float2 Scale;

        /// <summary>Normalized in the box.</summary>
        public float2 Pivot;

        public float Opacity;

        /// <summary>Clip record for the descendants, or 0 if the node does not clip.</summary>
        public int ClipSlot;

        /// <summary>Corner radii of the clip when <see cref="ClipSlot"/> is set.</summary>
        public float4 ClipRadii;

        /// <summary>RGBA8, linear: multiplies the colors of this node's own primitives.</summary>
        public uint Tint;

        /// <summary>Nonzero if the box receives pointer hits.</summary>
        public byte Hittable;
    }

    /// <summary>
    /// Nodes by slot: <see cref="NodeCpuData"/> on the CPU and <see cref="NodeGpuData"/> (computed by
    /// <see cref="TransformPass"/>) on the GPU. Slot 0 is reserved.
    /// </summary>
    internal sealed class NodeStore : IDisposable
    {
        private NativeList<NodeCpuData> _cpu;

        public NodeStore(int capacity)
        {
            Gpu = new GpuStore<NodeGpuData>(capacity, 1);
            _cpu = new NativeList<NodeCpuData>(capacity, Allocator.Persistent);
            _cpu.Add(default);
        }

        public GpuStore<NodeGpuData> Gpu { get; }

        public NativeArray<NodeCpuData> Cpu => _cpu.AsArray();

        public int Allocate()
        {
            var slot = Gpu.Allocate();
            while (_cpu.Length <= slot) _cpu.Add(default);

            _cpu[slot] = new NodeCpuData { Scale = 1f, Pivot = 0.5f, Opacity = 1f, Tint = GpuPacking.White8 };
            return slot;
        }

        public void Free(int slot)
        {
            Gpu.Free(slot);
        }

        public ref NodeCpuData this[int slot] => ref _cpu.ElementAt(slot);

        public void Dispose()
        {
            Gpu.Dispose();
            if (_cpu.IsCreated) _cpu.Dispose();
        }
    }
}