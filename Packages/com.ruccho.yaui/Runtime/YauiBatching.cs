using UnityEngine;
using Yaui.Core;

namespace Yaui
{
    /// <summary>
    /// How YAUI trades draw calls against GPU work when it splits the primitives of a panel into draws.
    /// </summary>
    /// <remarks>
    /// Primitives are drawn with the uber shader variant of the features their draw uses. Splitting a draw lets
    /// primitives without heavy features (such as shadows) use a lighter variant, but every draw has a fixed cost on
    /// the GPU and on the CPU. YAUI keeps the draws whose estimated cost is the lowest:
    /// <c>draws * (GPU cost * GpuWeight + CPU cost * CpuWeight) + pixel cost * GpuWeight / SplitMargin</c>.
    /// <para>The default weights count GPU and CPU time the same, which minimizes the total work (and so the power
    /// and heat) at a capped frame rate. A project bound by the GPU can raise <see cref="GpuWeight"/>, one bound by
    /// the CPU <see cref="CpuWeight"/>, for instance per quality level.</para>
    /// </remarks>
    public static class YauiBatching
    {
        private static bool _splitByShaderFeatures = true;
        private static float _gpuWeight = 1f;
        private static float _cpuWeight = 1f;
        private static float _splitMargin = 2f;

        /// <summary>Whether draws are split so that primitives without heavy features use a lighter variant.</summary>
        public static bool SplitByShaderFeatures
        {
            get => _splitByShaderFeatures;
            set => Set(ref _splitByShaderFeatures, value);
        }

        /// <summary>The weight of GPU time in the choice of draws.</summary>
        public static float GpuWeight
        {
            get => _gpuWeight;
            set => Set(ref _gpuWeight, Mathf.Max(value, 0f));
        }

        /// <summary>The weight of CPU time (the render thread and the main thread) in the choice of draws.</summary>
        public static float CpuWeight
        {
            get => _cpuWeight;
            set => Set(ref _cpuWeight, Mathf.Max(value, 0f));
        }

        /// <summary>
        /// How many times its cost a split has to save in pixels, at least 1. The estimates of pixel costs vary by GPU
        /// and by device state; a margin keeps the draws from splitting on a small or uncertain gain.
        /// </summary>
        public static float SplitMargin
        {
            get => _splitMargin;
            set => Set(ref _splitMargin, Mathf.Max(value, 1f));
        }

        // Estimates in microseconds, measured on a Pixel 5 (Adreno 620, planning/shader-variants.md): the fixed cost of
        // a draw, and the cost of a pixel by the features of its variant, at the lower end (large quads whose pixels
        // mostly skip the distance fields), so that splits are not overestimated.
        internal const float DrawGpuMicroseconds = 7.5f;
        internal const float DrawCpuMicroseconds = 10f;
        internal const float PixelBaseMicroseconds = 0.41e-3f;
        internal const float PixelTextMicroseconds = 0.015e-3f;
        internal const float PixelImageMicroseconds = 0.02e-3f;
        internal const float PixelBorderMicroseconds = 0.07e-3f;
        internal const float PixelShadowMicroseconds = 0.185e-3f;

        // Choosing the draws of a panel again, per primitive (on a worker thread): while it moves, every frame.
        internal const float LayeringMicroseconds = 0.17f;

        private static void Set<T>(ref T field, T value)
        {
            if (Equals(field, value)) return;

            field = value;
            YauiSystem.InvalidateDraws();
        }
    }
}
