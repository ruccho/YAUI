using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;
using UnityEngine.Rendering;
using Yaui.Layout.Yoga;
using Yaui.Rendering;
using Yaui.Text;

namespace Yaui.Core
{
    /// <summary>
    /// Owns the data shared by all panels and runs the frame pipeline (planning/design-core.md):
    /// <list type="number">
    /// <item>Setters write to the stores and mark things dirty at any time on the main thread.</item>
    /// <item>Submission (start of PostLateUpdate, the deadline for changes): rebuilds changed structures, applies
    /// changed layout styles to Yoga and schedules the layout on a worker thread.</item>
    /// <item>Collection (<see cref="RenderPipelineManager.beginContextRendering"/>): waits for the layout, applies
    /// it, propagates transforms, and uploads the dirty chunks.</item>
    /// </list>
    /// Jobs only touch data that setters do not write (Yoga nodes are updated at submission only).
    /// </summary>
    internal static class YauiSystem
    {
        private static readonly ProfilerMarker SubmitMarker = new("Yaui.Submit");
        private static readonly ProfilerMarker EarlySubmitMarker = new("Yaui.EarlySubmit");
        private static readonly ProfilerMarker CollectMarker = new("Yaui.Collect");
        private static readonly ProfilerMarker FeaturesMarker = new("Yaui.Collect.Features");
        private static readonly ProfilerMarker ReorderMarker = new("Yaui.Collect.Reorder");
        private static readonly ProfilerMarker LayoutMarker = new("Yaui.Layout");

        private static bool _initialized;
        private static readonly List<PanelState> Panels = new();
        private static readonly List<PanelState> LayoutPanels = new();
        private static readonly List<YauiCustomDraw> CustomDraws = new();
        private static NativeList<RootLayout> _layoutRoots;
        private static NativeList<IntPtr> _layoutBoundaries;
        private static NativeList<IntPtr> _parallelBoundaries;
        private static JobHandle _layoutJob;
        private static GCHandle _layoutJobPanels;
        private static bool _layoutInFlight;
        private static bool _submittedSinceCollect;

        public static bool IsInitialized => _initialized;

        /// <summary>Counts the initializations, so that handles from before a shutdown are recognized as stale.</summary>
        public static int Generation { get; private set; }

        public static NodeStore Nodes { get; private set; }
        public static GpuStore<PrimitiveData> Primitives { get; private set; }
        public static GpuStore<PrimitiveExt> Exts { get; private set; }
        public static GpuStore<ClipGpuData> Clips { get; private set; }

        internal static PanelRenderer Renderer { get; private set; }

        internal static TextureRegistry Textures { get; private set; }

        /// <summary>A concrete list, so that foreach does not box the enumerator.</summary>
        internal static List<PanelState> AllPanels => Panels;

        public static void EnsureInitialized()
        {
            if (_initialized) return;

            _initialized = true;
            Generation++;
            Nodes = new NodeStore(1024);
            Primitives = new GpuStore<PrimitiveData>(1024, 1);
            Exts = new GpuStore<PrimitiveExt>(64, 1);
            Clips = new GpuStore<ClipGpuData>(64, 1);
            Clips[0] = new ClipGpuData { Rect = ClipGpuData.NoClip };
            Textures = new TextureRegistry();
            TextPipeline.Subscribe();
            _layoutRoots = new NativeList<RootLayout>(4, Allocator.Persistent);
            _layoutBoundaries = new NativeList<IntPtr>(64, Allocator.Persistent);
            _parallelBoundaries = new NativeList<IntPtr>(64, Allocator.Persistent);
            Renderer = new PanelRenderer();

            InstallPlayerLoop();
            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
            RenderPipelineManager.endContextRendering += OnEndContextRendering;
            Application.quitting += Shutdown;
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            UnityEditor.EditorApplication.update += EditorUpdate;
#endif
        }

#if UNITY_EDITOR
        /// <summary>Edit mode has no player loop: submits from the editor update, before the views render.</summary>
        private static void EditorUpdate()
        {
            if (!Application.isPlaying && !_submittedSinceCollect) Submit();
        }
#endif

        internal static void Shutdown()
        {
            if (!_initialized) return;

            CompleteLayout();
            TextPipeline.Complete();
            TextPipeline.Unsubscribe();
            Tickers.Clear();
            RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
            RenderPipelineManager.endContextRendering -= OnEndContextRendering;
            Application.quitting -= Shutdown;
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            UnityEditor.EditorApplication.update -= EditorUpdate;
#endif
            UninstallPlayerLoop();
            foreach (var panel in Panels) panel.Dispose();

            Panels.Clear();
            Renderer.Dispose();
            Textures.Dispose();
            Nodes.Dispose();
            YogaNodeStore.DisposeAll();
            _layoutRoots.Dispose();
            _layoutBoundaries.Dispose();
            _parallelBoundaries.Dispose();
            Primitives.Dispose();
            Exts.Dispose();
            Clips.Dispose();
            _initialized = false;
        }

        public static PanelState CreatePanel(YauiPanel panel)
        {
            EnsureInitialized();
            var state = new PanelState(panel);
            Panels.Add(state);
            return state;
        }

        /// <summary>Main thread: a custom draw records its draws at every collection while enabled.</summary>
        public static void AddCustomDraw(YauiCustomDraw draw)
        {
            if (!CustomDraws.Contains(draw)) CustomDraws.Add(draw);

            RequestUpdate();
        }

        public static void RemoveCustomDraw(YauiCustomDraw draw)
        {
            CustomDraws.Remove(draw);
        }

        /// <summary>Main thread: runs the pipeline now, so that changes are laid out and uploaded immediately.</summary>
        public static void ForceUpdate()
        {
            if (!_initialized) return;

            // Applies what was submitted before, then submits the changes made since.
            if (_submittedSinceCollect) Collect();

            Submit();
            Collect();
            CompleteReorders();
        }

        /// <summary>The choice of draws changed (<see cref="YauiBatching"/>): every panel plans its draws again.</summary>
        public static void InvalidateDraws()
        {
            if (!_initialized) return;

            foreach (var panel in Panels) panel.OrderDirty = true;

            RequestUpdate();
        }

        /// <summary>
        /// Completes the reordering of every panel: it reads the stores, which scripts write after rendering.
        /// </summary>
        private static void CompleteReorders()
        {
            foreach (var panel in Panels) panel.CompleteReorder();
        }

        private static void OnEndContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            CompleteReorders();
        }

        public static void DestroyPanel(PanelState state)
        {
            if (!_initialized) return;

            CompleteLayout();
            Panels.Remove(state);
            state.Dispose();
        }

        /// <summary>Asks the editor to render a frame after a change in edit mode.</summary>
        public static void RequestUpdate()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        #region Player loop

        private struct YauiSubmit
        {
        }

        private struct YauiEarlySubmit
        {
        }

        private static void InstallPlayerLoop()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (Insert(ref loop)) PlayerLoop.SetPlayerLoop(loop);
        }

        private static bool Insert(ref PlayerLoopSystem loop)
        {
            var inserted = false;
            for (var i = 0; i < loop.subSystemList.Length; i++)
            {
                ref var system = ref loop.subSystemList[i];
                if (system.type == typeof(PreLateUpdate))
                    inserted |= InsertFirst(ref system, typeof(YauiEarlySubmit), EarlySubmit);
                else if (system.type == typeof(PostLateUpdate))
                    inserted |= InsertFirst(ref system, typeof(YauiSubmit), Submit);
            }

            return inserted;
        }

        private static bool InsertFirst(ref PlayerLoopSystem system, Type type,
            PlayerLoopSystem.UpdateFunction function)
        {
            foreach (var sub in system.subSystemList)
                if (sub.type == type)
                    return false;

            var list = new List<PlayerLoopSystem>(system.subSystemList);
            list.Insert(0, new PlayerLoopSystem { type = type, updateDelegate = function });
            system.subSystemList = list.ToArray();
            return true;
        }

        private static void UninstallPlayerLoop()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            for (var i = 0; i < loop.subSystemList.Length; i++)
            {
                ref var system = ref loop.subSystemList[i];
                if (system.type == typeof(PreLateUpdate) || system.type == typeof(PostLateUpdate))
                    system.subSystemList = Array.FindAll(system.subSystemList,
                        s => s.type != typeof(YauiSubmit) && s.type != typeof(YauiEarlySubmit));
            }

            PlayerLoop.SetPlayerLoop(loop);
        }

        #endregion

        /// <summary>
        /// Main thread, after Update: starts generating the texts changed so far, so that the generation overlaps
        /// with the animation and LateUpdate.
        /// </summary>
        private static void EarlySubmit()
        {
            if (_initialized)
            {
                using var _ = EarlySubmitMarker.Auto();
                TextPipeline.Schedule();
            }
        }

        /// <summary>Main thread: the deadline of changes for this frame. Starts the heavy work on worker threads.</summary>
        private static void Submit()
        {
            if (!_initialized) return;

            using var _ = SubmitMarker.Auto();
            Tickers.Tick();

            // A frame without rendering leaves the previous work uncollected.
            if (_layoutInFlight) Collect();

            _submittedSinceCollect = true;
            Pools.RecycleReleasedNodes();
            TextPipeline.Schedule();
            LayoutPanels.Clear();
            _layoutRoots.Clear();
            _layoutBoundaries.Clear();
            _parallelBoundaries.Clear();
            foreach (var panel in Panels)
            {
                if (panel.Root == null) continue;

                panel.Panel.UpdateCanvas(panel);
                if (panel.StructureDirty) panel.RebuildStructure();

                if (panel.PrepareLayout())
                {
                    panel.LayoutScheduled = true;
                    LayoutPanels.Add(panel);
                    _layoutRoots.Add(panel.RootLayout);
                }
            }

            Renderer.QueueWorldPanels();
            if (LayoutPanels.Count == 0) return;

            _layoutJobPanels = GCHandle.Alloc(LayoutPanels);
            // Measuring texts reads their generation.
            // Measures of changed texts (managed; they may dirty layout boundaries), then the layout (Burst).
            _layoutJob = new LayoutJob { Panels = _layoutJobPanels, Boundaries = _layoutBoundaries }
                .Schedule(TextPipeline.Handle);
            _layoutJob = new TreeLayoutJob
            {
                Roots = _layoutRoots, Boundaries = _layoutBoundaries, Parallel = _parallelBoundaries
            }.Schedule(_layoutJob);
            _layoutJob = new BoundaryLayoutJob { Boundaries = _parallelBoundaries.AsDeferredJobArray() }
                .Schedule(_parallelBoundaries, 4, _layoutJob);
            _layoutJob = new BoundaryFixupJob { Boundaries = _layoutBoundaries }.Schedule(_layoutJob);
            JobHandle.ScheduleBatchedJobs();
            _layoutInFlight = true;
        }

        private static void CompleteLayout()
        {
            if (!_layoutInFlight) return;

            _layoutJob.Complete();
            _layoutJobPanels.Free();
            _layoutInFlight = false;
        }

        private static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            // Selected first, so that a synchronous submission (edit mode) lays out for this camera.
            Renderer.SelectCamera(cameras);
            Collect();
        }

        /// <summary>Main thread, right before rendering: applies the results and uploads the changes.</summary>
        private static void Collect()
        {
            if (!_initialized) return;

            // Edit mode (and anything else that skips the player loop) runs the pipeline synchronously.
            if (!_submittedSinceCollect) Submit();

            _submittedSinceCollect = false;
            using var _ = CollectMarker.Auto();
            CompleteReorders();
            CompleteLayout();
            TextPipeline.Complete();
            foreach (var panel in Panels)
                if (panel.LayoutScheduled)
                    panel.ApplyLayout();

            TextPipeline.Finish();
            foreach (var panel in Panels)
            {
                if (panel.OrderDirty) panel.RebuildOrder();

                panel.UpdateTransforms();
            }

            // After the transforms: custom draws place their meshes on the nodes as rendered.
            // By index: user code may enable or disable custom draws.
            for (var i = 0; i < CustomDraws.Count; i++) CustomDraws[i].Collect();

            // Reordering runs while the render pipeline records the cameras, until the draws are recorded.
            foreach (var panel in Panels)
            {
                using (FeaturesMarker.Auto()) panel.UpdateFeatures();
                using (ReorderMarker.Auto()) panel.ScheduleReorder();
            }

            JobHandle.ScheduleBatchedJobs();

            Primitives.Upload();
            Exts.Upload();
            Nodes.Gpu.Upload();
            Clips.Upload();
            Renderer.Prepare();
        }

        /// <summary>Resolves the measures of changed texts and collects the dirty layout boundaries (managed).</summary>
        private struct LayoutJob : IJob
        {
            public GCHandle Panels;
            public NativeList<IntPtr> Boundaries;

            public void Execute()
            {
                using var _ = LayoutMarker.Auto();
                foreach (var panel in (List<PanelState>)Panels.Target) panel.ResolveMeasures(Boundaries);
            }
        }
    }
}