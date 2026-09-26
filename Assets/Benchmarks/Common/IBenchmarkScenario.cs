using UnityEngine;

namespace Yaui.Benchmarks
{
    /// <summary>
    /// A single benchmark case. The runner calls <see cref="Setup"/> once, <see cref="Tick"/> every frame
    /// during warmup and measurement, then <see cref="Teardown"/>.
    /// </summary>
    public interface IBenchmarkScenario
    {
        string Name { get; }

        void Setup(Transform root);

        void Tick(int frame);

        void Teardown();
    }
}
