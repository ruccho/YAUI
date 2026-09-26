using UnityEngine;

namespace Yaui.Benchmarks
{
    /// <summary>
    /// Renders no UI. Used as the baseline of frame timings, especially the GPU time of the render pipeline itself.
    /// </summary>
    public sealed class EmptyScenario : IBenchmarkScenario
    {
        public string Name => "Empty";

        public void Setup(Transform root)
        {
        }

        public void Tick(int frame)
        {
        }

        public void Teardown()
        {
        }
    }
}
