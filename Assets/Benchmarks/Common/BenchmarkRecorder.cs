using System;
using System.Collections.Generic;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace Yaui.Benchmarks
{
    /// <summary>
    /// Collects per-frame samples of frame timings, render counters and profiler markers.
    /// </summary>
    public sealed class BenchmarkRecorder : IDisposable
    {
        public const string ScenarioTickMarkerName = "YauiBench.ScenarioTick";

        static readonly string[] RenderCounterNames =
        {
            "Batches Count",
            "SetPass Calls Count",
            "Draw Calls Count",
            "Vertices Count",
        };

        readonly List<Metric> _metrics = new();
        readonly List<ProfilerRecorder> _recorders = new();
        readonly FrameTiming[] _frameTimings = new FrameTiming[1];

        public IReadOnlyList<Metric> Metrics => _metrics;

        /// <summary>
        /// Marker names that could not be resolved. Markers are registered lazily, so a marker that never ran
        /// in the scenario is listed here too.
        /// </summary>
        public List<string> MissingMarkers { get; } = new();

        public BenchmarkRecorder(IEnumerable<string> markerNames)
        {
            _metrics.Add(new Metric("CPU Main (ms)"));
            _metrics.Add(new Metric("CPU Main Present Wait (ms)"));
            _metrics.Add(new Metric("CPU Render (ms)"));
            _metrics.Add(new Metric("GPU (ms)"));

            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var handleByName = new Dictionary<string, ProfilerRecorderHandle>();
            foreach (var handle in handles)
            {
                handleByName.TryAdd(ProfilerRecorderHandle.GetDescription(handle).Name, handle);
            }

            AddRecorder(handleByName, "GC Allocated In Frame", "GC Alloc (bytes)", 1.0);
            foreach (var name in RenderCounterNames)
            {
                AddRecorder(handleByName, name, name, 1.0);
            }

            AddRecorder(handleByName, ScenarioTickMarkerName, ScenarioTickMarkerName + " (ms)", 1e-6);
            foreach (var name in markerNames)
            {
                AddRecorder(handleByName, name, name + " (ms)", 1e-6);
            }
        }

        void AddRecorder(Dictionary<string, ProfilerRecorderHandle> handleByName, string statName, string label,
            double scale)
        {
            if (!handleByName.TryGetValue(statName, out var handle))
            {
                MissingMarkers.Add(statName);
                return;
            }

            var recorder = new ProfilerRecorder(handle, 1, ProfilerRecorderOptions.Default);
            recorder.Start();
            _recorders.Add(recorder);
            _metrics.Add(new Metric(label, scale));
        }

        /// <summary>
        /// Samples values of the previous frame. Call once per frame.
        /// </summary>
        public void Sample()
        {
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _frameTimings) > 0)
            {
                var timing = _frameTimings[0];
                _metrics[0].Add(timing.cpuMainThreadFrameTime);
                _metrics[1].Add(timing.cpuMainThreadPresentWaitTime);
                _metrics[2].Add(timing.cpuRenderThreadFrameTime);
                _metrics[3].Add(timing.gpuFrameTime);
            }

            const int fixedMetricCount = 4;
            for (var i = 0; i < _recorders.Count; i++)
            {
                var metric = _metrics[fixedMetricCount + i];
                metric.Add(_recorders[i].LastValue * metric.Scale);
            }
        }

        public void Dispose()
        {
            foreach (var recorder in _recorders)
            {
                recorder.Dispose();
            }

            _recorders.Clear();
        }

        public sealed class Metric
        {
            readonly List<double> _samples = new();

            public string Name { get; }
            public double Scale { get; }

            public Metric(string name, double scale = 1.0)
            {
                Name = name;
                Scale = scale;
            }

            public void Add(double value) => _samples.Add(value);

            public MetricSummary Summarize()
            {
                if (_samples.Count == 0)
                {
                    return new MetricSummary(Name, 0, double.NaN, double.NaN, double.NaN, double.NaN);
                }

                var sorted = new List<double>(_samples);
                sorted.Sort();
                var sum = 0.0;
                foreach (var sample in sorted)
                {
                    sum += sample;
                }

                return new MetricSummary(Name, sorted.Count, sum / sorted.Count, Percentile(sorted, 0.5),
                    Percentile(sorted, 0.95), sorted[^1]);
            }

            static double Percentile(List<double> sorted, double p)
            {
                var index = (int)Math.Round(p * (sorted.Count - 1));
                return sorted[index];
            }
        }
    }

    public readonly struct MetricSummary
    {
        public readonly string Name;
        public readonly int Count;
        public readonly double Mean;
        public readonly double Median;
        public readonly double P95;
        public readonly double Max;

        public MetricSummary(string name, int count, double mean, double median, double p95, double max)
        {
            Name = name;
            Count = count;
            Mean = mean;
            Median = median;
            P95 = p95;
            Max = max;
        }
    }
}
