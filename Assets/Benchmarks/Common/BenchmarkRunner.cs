using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace Yaui.Benchmarks
{
    /// <summary>
    /// Runs every scenario of a <see cref="BenchmarkSuite"/> in sequence and writes the results as CSV.
    /// </summary>
    public sealed class BenchmarkRunner : MonoBehaviour
    {
        static readonly ProfilerMarker ScenarioTickMarker = new(BenchmarkRecorder.ScenarioTickMarkerName);

        [SerializeField] BenchmarkSuite suite;
        [SerializeField] int warmupFrames = 120;
        [SerializeField] int measureFrames = 600;
        [SerializeField] int cooldownFrames = 30;
        [SerializeField] int targetFrameRate = 120;
        [SerializeField] bool quitOnComplete;

        IBenchmarkScenario _currentScenario;
        int _frame;

        IEnumerator Start()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFrameRate;

            var results = new List<(string Scenario, List<MetricSummary> Summaries)>();
            foreach (var scenario in suite.CreateScenarios())
            {
                Debug.Log($"[YauiBench] Running {scenario.Name}...");

                var root = new GameObject(scenario.Name).transform;
                scenario.Setup(root);
                _currentScenario = scenario;

                for (var i = 0; i < warmupFrames; i++)
                {
                    yield return null;
                }

                // Markers are registered lazily, so resolve them after warmup.
                using (var recorder = new BenchmarkRecorder(suite.GetMarkerNames()))
                {
                    if (recorder.MissingMarkers.Count > 0)
                    {
                        Debug.LogWarning(
                            $"[YauiBench] {scenario.Name}: missing markers: {string.Join(", ", recorder.MissingMarkers)}");
                    }

                    for (var i = 0; i < measureFrames; i++)
                    {
                        yield return null;
                        recorder.Sample();
                    }

                    var summaries = new List<MetricSummary>();
                    foreach (var metric in recorder.Metrics)
                    {
                        summaries.Add(metric.Summarize());
                    }

                    results.Add((scenario.Name, summaries));
                }

                _currentScenario = null;
                scenario.Teardown();
                Destroy(root.gameObject);
                yield return Resources.UnloadUnusedAssets();
                GC.Collect();
                for (var i = 0; i < cooldownFrames; i++)
                {
                    yield return null;
                }
            }

            var report = WriteResults(results);

            // Added only after measurement so that IMGUI does not affect the results.
            gameObject.AddComponent<BenchmarkReportView>().Report = report;

            if (quitOnComplete)
            {
                Application.Quit();
            }
        }

        void Update()
        {
            if (_currentScenario == null)
            {
                return;
            }

            using (ScenarioTickMarker.Auto())
            {
                _currentScenario.Tick(_frame);
            }

            _frame++;
        }

        string WriteResults(List<(string Scenario, List<MetricSummary> Summaries)> results)
        {
            var csv = new StringBuilder();
            csv.AppendLine($"# suite,{suite.SuiteName}");
            csv.AppendLine($"# unity,{Application.unityVersion}");
            csv.AppendLine($"# device,{SystemInfo.deviceModel}");
            csv.AppendLine($"# os,{SystemInfo.operatingSystem}");
            csv.AppendLine($"# cpu,{SystemInfo.processorType}");
            csv.AppendLine($"# gpu,{SystemInfo.graphicsDeviceName},{SystemInfo.graphicsDeviceType}");
            csv.AppendLine($"# screen,{Screen.width}x{Screen.height}");
            csv.AppendLine($"# development,{Debug.isDebugBuild}");
            csv.AppendLine("scenario,metric,count,mean,median,p95,max");

            var text = new StringBuilder();
            text.AppendLine($"{suite.SuiteName} / {SystemInfo.deviceModel} / {SystemInfo.graphicsDeviceType}");
            foreach (var (scenario, summaries) in results)
            {
                text.AppendLine();
                text.AppendLine($"== {scenario} ==  (mean / p95)");
                foreach (var s in summaries)
                {
                    csv.AppendLine(string.Join(",", scenario, s.Name, s.Count.ToString(CultureInfo.InvariantCulture),
                        Format(s.Mean), Format(s.Median), Format(s.P95), Format(s.Max)));
                    text.AppendLine($"{s.Name}: {Format(s.Mean)} / {Format(s.P95)}");
                }
            }

            var fileName = $"yaui-bench-{suite.SuiteName}-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
            var path = Path.Combine(Application.persistentDataPath, fileName);
            File.WriteAllText(path, csv.ToString());

            Debug.Log($"[YauiBench] Results written to {path}\n{csv}");
            return text.ToString();
        }

        static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
