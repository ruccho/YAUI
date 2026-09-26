using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Yaui.Rendering
{
    /// <summary>
    /// Records mirrored to a structured buffer. Slots are allocated and freed (reused through a free list) and never
    /// move. Writes mark 64-record chunks dirty, and <see cref="Upload"/> sends only the dirty chunks.
    /// </summary>
    internal sealed class GpuStore<T> : IDisposable where T : unmanaged
    {
        public const int ChunkShift = 6;

        private NativeList<T> _items;
        private NativeList<int> _free;

        // Free blocks of AllocateRange by capacity.
        private readonly Dictionary<int, Stack<int>> _freeRanges = new();

        // One bit per chunk.
        private NativeList<ulong> _dirtyChunks;
        private GraphicsBuffer _buffer;

        /// <param name="reserved">Slots at the start that are never allocated, such as index 0 meaning "none".</param>
        public GpuStore(int capacity, int reserved = 0)
        {
            _items = new NativeList<T>(Math.Max(capacity, reserved + 1), Allocator.Persistent);
            _free = new NativeList<int>(64, Allocator.Persistent);
            _dirtyChunks = new NativeList<ulong>(16, Allocator.Persistent);
            for (var i = 0; i < reserved; i++) _items.Add(default);

            EnsureDirtyCapacity();
            MarkAllDirty();
        }

        public int Length => _items.Length;

        public GraphicsBuffer Buffer => _buffer;

        /// <summary>Records by slot, for jobs. Valid until the next <see cref="Allocate"/>.</summary>
        public NativeArray<T> AsArray()
        {
            return _items.AsArray();
        }

        /// <summary>Dirty chunk bits, for jobs that write records. Valid until the next <see cref="Allocate"/>.</summary>
        public NativeArray<ulong> DirtyChunks => _dirtyChunks.AsArray();

        public int Allocate()
        {
            int slot;
            if (_free.Length > 0)
            {
                slot = _free[_free.Length - 1];
                _free.RemoveAt(_free.Length - 1);
                _items[slot] = default;
            }
            else
            {
                slot = _items.Length;
                _items.Add(default);
                EnsureDirtyCapacity();
            }

            MarkDirty(slot);
            return slot;
        }

        /// <summary>Rounds a count up to the capacity of a block (a power of two, at least 4).</summary>
        public static int RangeCapacity(int count)
        {
            return Math.Max(4, (int)Unity.Mathematics.math.ceilpow2((uint)count));
        }

        /// <summary>Allocates <paramref name="capacity"/> contiguous slots (see <see cref="RangeCapacity"/>).</summary>
        public int AllocateRange(int capacity)
        {
            if (_freeRanges.TryGetValue(capacity, out var stack) && stack.Count > 0) return stack.Pop();

            var start = _items.Length;
            for (var i = 0; i < capacity; i++) _items.Add(default);

            EnsureDirtyCapacity();
            for (var i = 0; i < capacity; i += 1 << ChunkShift) MarkDirty(start + i);

            MarkDirty(start + capacity - 1);
            return start;
        }

        public void FreeRange(int start, int capacity)
        {
            for (var i = 0; i < capacity; i++)
            {
                _items[start + i] = default;
                MarkDirty(start + i);
            }

            if (!_freeRanges.TryGetValue(capacity, out var stack)) _freeRanges[capacity] = stack = new Stack<int>();

            stack.Push(start);
        }

        public void Free(int slot)
        {
            _items[slot] = default;
            MarkDirty(slot);
            _free.Add(slot);
        }

        /// <summary>Returns the record for writing and marks its chunk dirty.</summary>
        public ref T this[int slot]
        {
            get
            {
                MarkDirty(slot);
                return ref _items.ElementAt(slot);
            }
        }

        public T Read(int slot)
        {
            return _items[slot];
        }

        public void MarkDirty(int slot)
        {
            MarkDirty(_dirtyChunks.AsArray(), slot);
        }

        /// <summary>Marks the chunk of <paramref name="slot"/> dirty. Usable from jobs with <see cref="DirtyChunks"/>.</summary>
        public static void MarkDirty(NativeArray<ulong> dirtyChunks, int slot)
        {
            var chunk = slot >> ChunkShift;
            dirtyChunks[chunk >> 6] |= 1ul << (chunk & 63);
        }

        private void MarkAllDirty()
        {
            for (var i = 0; i < _dirtyChunks.Length; i++) _dirtyChunks[i] = ~0ul;
        }

        private void EnsureDirtyCapacity()
        {
            var chunks = (_items.Length + (1 << ChunkShift) - 1) >> ChunkShift;
            var words = (chunks + 63) >> 6;
            while (_dirtyChunks.Length < words) _dirtyChunks.Add(0);
        }

        /// <summary>Sends the dirty chunks, recreating the buffer if it is too small. Returns the buffer.</summary>
        public GraphicsBuffer Upload()
        {
            if (_buffer == null || _buffer.count < _items.Length)
            {
                _buffer?.Dispose();
                _buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Math.Max(_items.Capacity, 64),
                    UnsafeUtility.SizeOf<T>());
                MarkAllDirty();
            }

            var array = _items.AsArray();
            var chunkSize = 1 << ChunkShift;
            var runStart = -1;
            for (var word = 0; word < _dirtyChunks.Length; word++)
            {
                var bits = _dirtyChunks[word];
                if (bits == 0ul && runStart < 0) continue;

                for (var bit = 0; bit < 64; bit++)
                {
                    var chunk = (word << 6) + bit;
                    var dirty = (bits & (1ul << bit)) != 0;
                    if (dirty && runStart < 0)
                    {
                        runStart = chunk;
                    }
                    else if (!dirty && runStart >= 0)
                    {
                        UploadRun(array, runStart * chunkSize, chunk * chunkSize);
                        runStart = -1;
                    }
                }

                _dirtyChunks[word] = 0ul;
            }

            if (runStart >= 0) UploadRun(array, runStart * chunkSize, array.Length);

            return _buffer;
        }

        private void UploadRun(NativeArray<T> array, int start, int end)
        {
            end = Math.Min(end, array.Length);
            if (end > start) _buffer.SetData(array, start, start, end - start);
        }

        public void Dispose()
        {
            _buffer?.Dispose();
            _buffer = null;
            if (_items.IsCreated)
            {
                _items.Dispose();
                _free.Dispose();
                _dirtyChunks.Dispose();
            }
        }
    }
}