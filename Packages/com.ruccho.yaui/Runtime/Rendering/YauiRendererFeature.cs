using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Yaui
{
    /// <summary>
    /// Draws the camera panels (<see cref="PanelRenderMode.Camera"/>) of the cameras that use this renderer: each
    /// <see cref="YauiInjectionPoint"/> listed here is drawn at its event. Panels whose injection point is not listed
    /// are not drawn by this renderer.
    /// </summary>
    public sealed class YauiRendererFeature : ScriptableRendererFeature
    {
        [Serializable]
        public struct Entry
        {
            public YauiInjectionPoint injectionPoint;

            [Tooltip("The event of the point. Any value between URP's events works; passes run in the order of " +
                     "their events.")]
            [YauiRenderPassEvent]
            public int renderPassEvent;
        }

        [SerializeField] private List<Entry> injectionPoints = new();

        private readonly List<Rendering.PanelRenderer.CameraRenderPass> _passes = new();

        /// <summary>The injection points of this renderer and their events.</summary>
        public IReadOnlyList<Entry> InjectionPoints => injectionPoints;

        /// <summary>The event of <paramref name="point"/> in this renderer, if it is listed.</summary>
        public bool TryGetRenderPassEvent(YauiInjectionPoint point, out RenderPassEvent renderPassEvent)
        {
            foreach (var entry in injectionPoints)
                if (entry.injectionPoint != null && entry.injectionPoint == point)
                {
                    renderPassEvent = (RenderPassEvent)entry.renderPassEvent;
                    return true;
                }

            renderPassEvent = default;
            return false;
        }

        public override void Create()
        {
            _passes.Clear();
            foreach (var entry in injectionPoints)
            {
                // The first entry of a point wins, so that its panels are drawn once.
                if (entry.injectionPoint == null || _passes.Exists(p => p.InjectionPoint == entry.injectionPoint))
                    continue;

                _passes.Add(new Rendering.PanelRenderer.CameraRenderPass(entry.injectionPoint,
                    (RenderPassEvent)entry.renderPassEvent));
            }
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;
            if (camera.cameraType is CameraType.SceneView or CameraType.Preview) return;

            foreach (var pass in _passes)
                if (Rendering.PanelRenderer.HasCameraPanels(camera, pass.InjectionPoint))
                    renderer.EnqueuePass(pass);
        }
    }
}
