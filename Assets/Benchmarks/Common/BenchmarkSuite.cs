using System.Collections.Generic;
using UnityEngine;

namespace Yaui.Benchmarks
{
    /// <summary>
    /// Provides the scenarios to run and the profiler markers that are specific to the UI system under test.
    /// </summary>
    public abstract class BenchmarkSuite : MonoBehaviour
    {
        public abstract string SuiteName { get; }

        public abstract IEnumerable<IBenchmarkScenario> CreateScenarios();

        public abstract IEnumerable<string> GetMarkerNames();
    }
}
