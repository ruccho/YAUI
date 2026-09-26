# Benchmarks

The same screens built with YAUI, uGUI (with TextMeshPro) and UI Toolkit, to compare their costs. The results and the scenarios are described in the [benchmarks](https://ruccho.com/YAUI/benchmarks) of the documentation.

| Path | Contents |
|---|---|
| `Common/` | The runner (`BenchmarkRunner`), which runs the scenarios of a suite and writes the results as CSV, and the GPU ballast |
| `Comparison/` | The scenarios of each UI system and the scene `ComparisonBenchmark.unity` |
| `Results~/` | The raw results shown in the documentation |
| `Tools~/chart.py` | Prints the tables and draws the chart from a result CSV |

## Running

1. Import the TextMeshPro essential resources (**Window > TextMeshPro > Import TMP Essential Resources**).
2. Build `Comparison/ComparisonBenchmark.unity` alone for the device. Enable **Frame Timing Stats** in the Player Settings. A development build also records a breakdown by profiler markers.
3. The app runs all the scenarios (about 25 minutes on a Pixel 5), shows the results and writes `yaui-bench-comparison-*.csv` to `Application.persistentDataPath`.
4. `python Tools~/chart.py <csv> <output directory>` prints the tables and writes `benchmark-light.svg` and `benchmark-dark.svg`.

The number of rounds and elements are set on `ComparisonBenchmarkSuite`, the number of frames on `BenchmarkRunner`, in the scene.
