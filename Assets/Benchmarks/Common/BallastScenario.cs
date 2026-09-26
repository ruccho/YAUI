using UnityEngine;

namespace Yaui.Benchmarks
{
    /// <summary>Runs another scenario while <see cref="GpuBallast"/> keeps the GPU saturated.</summary>
    public sealed class BallastScenario : IBenchmarkScenario
    {
        readonly IBenchmarkScenario _inner;
        readonly Shader _shader;
        readonly int _iterations;
        GpuBallast _ballast;

        public string Name => _inner.Name + "+Ballast";

        public BallastScenario(IBenchmarkScenario inner, Shader shader, int iterations)
        {
            this._inner = inner;
            this._shader = shader;
            this._iterations = iterations;
        }

        public void Setup(Transform root)
        {
            _ballast = new GpuBallast(_shader, _iterations);
            _inner.Setup(root);
        }

        public void Tick(int frame) => _inner.Tick(frame);

        public void Teardown()
        {
            _inner.Teardown();
            _ballast.Dispose();
        }
    }
}
