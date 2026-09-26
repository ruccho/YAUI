using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Yaui.Benchmarks
{
    /// <summary>
    /// Draws an expensive full screen pass before opaques on every game camera to keep the GPU saturated,
    /// so that GPU timings are measured at a steady clock instead of being skewed by DVFS.
    /// </summary>
    public sealed class GpuBallast : IDisposable
    {
        static readonly int IterationsId = Shader.PropertyToID("_Iterations");

        readonly Material _material;
        readonly BallastPass _pass;

        public GpuBallast(Shader shader, int iterations)
        {
            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _material.SetInt(IterationsId, iterations);
            _pass = new BallastPass(_material);
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType == CameraType.Game)
            {
                camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(_pass);
            }
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            UnityEngine.Object.Destroy(_material);
        }

        sealed class BallastPass : ScriptableRenderPass
        {
            readonly Material _material;

            public BallastPass(Material material)
            {
                this._material = material;
                renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
            }

            sealed class PassData
            {
                public Material Material;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                using var builder = renderGraph.AddRasterRenderPass<PassData>("Yaui Benchmark Ballast", out var data);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                builder.AllowPassCulling(false);
                data.Material = _material;
                builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                    context.cmd.DrawProcedural(Matrix4x4.identity, d.Material, 0, MeshTopology.Triangles, 3));
            }
        }
    }
}
