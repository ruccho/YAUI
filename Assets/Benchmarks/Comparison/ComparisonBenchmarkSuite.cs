using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Yaui.Benchmarks.Comparison
{
    /// <summary>
    /// The same screens built with YAUI, uGUI (+ TextMeshPro) and UI Toolkit, run one system after another for each
    /// case, so that the systems are measured under the same conditions.
    /// </summary>
    /// <remarks>
    /// Scenario names are <c>Case.System</c> (with <c>#round</c> when there are several rounds). The order of the
    /// systems rotates every round to even out thermal drift.
    /// </remarks>
    public sealed class ComparisonBenchmarkSuite : BenchmarkSuite
    {
        public const string Yaui = "YAUI";
        public const string Ugui = "uGUI";
        public const string Uitk = "UITK";

        static readonly string[] MarkerNames =
        {
            // YAUI.
            "Yaui.EarlySubmit",
            "Yaui.Submit",
            "Yaui.Collect",
            "Yaui.Layout",
            "Yaui.Text.Generate",
            "Yaui.Text.Finish",

            // uGUI.
            "PostLateUpdate.PlayerUpdateCanvases",
            "UIEvents.WillRenderCanvases",
            "Canvas.BuildBatch",
            "UI.RenderOverlays",
            "Layout",

            // UI Toolkit.
            "PreLateUpdate.UIElementsUpdatePanels",
            "PostLateUpdate.UIElementsRepaintPanels",
            "UIElements.UpdateLayout",
            "TextGenerator.GenerateText",
            "UIR.DrawChain",
        };

        [SerializeField] int cellCount = 1500;
        [SerializeField] int rowCount = 500;
        [SerializeField] int rounds = 2;
        [SerializeField] ThemeStyleSheet uitkTheme;
        [SerializeField] bool gpuScenarios = true;
        [SerializeField] Shader ballastShader;
        [SerializeField] int ballastIterations = 128;

        public override string SuiteName => "comparison";

        public override IEnumerable<IBenchmarkScenario> CreateScenarios()
        {
            var systems = new[] { Yaui, Ugui, Uitk };
            for (var round = 0; round < rounds; round++)
            {
                var suffix = rounds > 1 ? $"#{round + 1}" : "";
                var order = Rotate(systems, round);

                yield return new NamedScenario(new EmptyScenario(), "Empty" + suffix);

                foreach (var mutation in (GridMutation[])Enum.GetValues(typeof(GridMutation)))
                {
                    foreach (var system in order)
                    {
                        yield return CreateGrid(system, mutation, $"{GridCaseName(mutation)}.{system}{suffix}");
                    }
                }

                foreach (var mutation in (ListMutation[])Enum.GetValues(typeof(ListMutation)))
                {
                    foreach (var system in order)
                    {
                        yield return CreateList(system, mutation, $"{ListCaseName(mutation)}.{system}{suffix}");
                    }
                }

                if (!gpuScenarios)
                {
                    continue;
                }

                // GPU: under the ballast the GPU runs at a steady clock; the difference to Empty is the UI's cost.
                yield return Ballast(new NamedScenario(new EmptyScenario(), "Empty+Ballast" + suffix));
                foreach (var system in order)
                {
                    yield return Ballast(CreateGrid(system, GridMutation.None, $"GridNone+Ballast.{system}{suffix}"));
                }

                foreach (var system in order)
                {
                    yield return Ballast(CreateList(system, ListMutation.Scroll, $"ListScroll+Ballast.{system}{suffix}"));
                }

                yield return Ballast(new NamedScenario(new EmptyScenario(), "Empty+Ballast2" + suffix));
            }
        }

        public override IEnumerable<string> GetMarkerNames() => MarkerNames;

        static string[] Rotate(string[] items, int offset)
        {
            var result = new string[items.Length];
            for (var i = 0; i < items.Length; i++)
            {
                result[i] = items[(i + offset) % items.Length];
            }

            return result;
        }

        static string GridCaseName(GridMutation mutation) => mutation switch
        {
            GridMutation.None or GridMutation.HitTest => $"Grid{mutation}",
            _ => $"Grid{mutation}10",
        };

        static string ListCaseName(ListMutation mutation) => mutation switch
        {
            ListMutation.Resize => "ListResize10",
            _ => $"List{mutation}",
        };

        IBenchmarkScenario CreateGrid(string system, GridMutation mutation, string name) => system switch
        {
            Yaui => new YauiGridScenario(cellCount, mutation, name),
            Ugui => new UguiGridScenario(cellCount, mutation, name),
            _ => new UitkGridScenario(cellCount, mutation, uitkTheme, name),
        };

        IBenchmarkScenario CreateList(string system, ListMutation mutation, string name) => system switch
        {
            Yaui => new YauiListScenario(rowCount, mutation, name),
            Ugui => new UguiListScenario(rowCount, mutation, name),
            _ => new UitkListScenario(rowCount, mutation, uitkTheme, name),
        };

        IBenchmarkScenario Ballast(IBenchmarkScenario inner) =>
            new NamedScenario(new BallastScenario(inner, ballastShader, ballastIterations), inner.Name);

        /// <summary>Gives another scenario a name.</summary>
        sealed class NamedScenario : IBenchmarkScenario
        {
            readonly IBenchmarkScenario _inner;

            public string Name { get; }

            public NamedScenario(IBenchmarkScenario inner, string name)
            {
                _inner = inner;
                Name = name;
            }

            public void Setup(Transform root) => _inner.Setup(root);

            public void Tick(int frame) => _inner.Tick(frame);

            public void Teardown() => _inner.Teardown();
        }
    }
}
