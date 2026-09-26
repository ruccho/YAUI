using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Yaui.Core;

namespace Yaui.Rendering
{
    /// <summary>
    /// Draws the panels. Each primitive is a quad of 4 vertices indexed 6 times (one shared index buffer).
    /// <list type="bullet">
    /// <item>Overlay panels: a render graph pass after post-processing on the last game camera that renders to the
    /// screen. One draw call per panel, in sort order.</item>
    /// <item>World space panels: <see cref="Graphics.RenderPrimitivesIndexed"/>, so that URP sorts and depth tests
    /// them with the rest of the transparent objects (planning/poc-p1.md, PoC 7).</item>
    /// </list>
    /// The meshes of custom draws (<see cref="YauiCustomDraw"/>) are drawn between the segments of primitives, one
    /// draw call each.
    /// </summary>
    internal sealed class PanelRenderer : IDisposable
    {
        private const int OverlayPass = 0;

        private static readonly int PrimitivesId = Shader.PropertyToID("_YauiPrimitives");
        private static readonly int ExtsId = Shader.PropertyToID("_YauiExts");
        private static readonly int NodesId = Shader.PropertyToID("_YauiNodes");
        private static readonly int ClipsId = Shader.PropertyToID("_YauiClips");
        private static readonly int OrderId = Shader.PropertyToID("_YauiOrder");
        private static readonly int PixelSizeId = Shader.PropertyToID("_YauiPixelSize");
        private static readonly int PanelMatrixId = Shader.PropertyToID("_YauiPanelMatrix");
        private static readonly int OrderOffsetId = Shader.PropertyToID("_YauiOrderOffset");
        private static readonly int TexIds0Id = Shader.PropertyToID("_YauiTexIds0");
        private static readonly int TexIds1Id = Shader.PropertyToID("_YauiTexIds1");
        private static readonly int AtlasParamsId = Shader.PropertyToID("_YauiAtlasParams");
        private static readonly int NodeId = Shader.PropertyToID("_YauiNode");
        private static readonly int MeshMatrixId = Shader.PropertyToID("_YauiMeshMatrix");
        private static readonly ShaderTagId LightModeTag = new("LightMode");
        private static readonly ShaderTagId YauiOverlayTag = new("YauiOverlay");

        // The Z of custom meshes on the panel: flattened, but not to zero, so that the model matrix inverts.
        private const float FlatZ = 1e-4f;

        private static readonly int[] TextureIds =
        {
            Shader.PropertyToID("_YauiTex0"), Shader.PropertyToID("_YauiTex1"), Shader.PropertyToID("_YauiTex2"),
            Shader.PropertyToID("_YauiTex3"), Shader.PropertyToID("_YauiTex4"), Shader.PropertyToID("_YauiTex5"),
            Shader.PropertyToID("_YauiTex6"), Shader.PropertyToID("_YauiTex7")
        };

        private readonly Vector4[] _atlasParams = new Vector4[TextureRegistry.SlotCount];

        private static readonly int StencilRefId = Shader.PropertyToID("_YauiStencilRef");
        private static readonly int StencilCompId = Shader.PropertyToID("_YauiStencilComp");
        private static readonly int StencilPassId = Shader.PropertyToID("_YauiStencilPass");

        private readonly Material _material;

        // The uber shader with per-pixel clipping, for panels that have rotated nodes or rounded clips.
        private readonly Material _pixelClipMaterial;

        // The shapes of masks, with and without per-pixel clipping.
        private readonly Material _maskMaterial;
        private readonly Material _pixelClipMaskMaterial;

        // Materials with the stencil state of a segment, by base material, kind and depth.
        private readonly Dictionary<(Material, SegmentKind, int), Material> _stencilMaterials = new();
        private readonly OverlayRenderPass _pass;
        private readonly SceneViewRenderPass _sceneViewPass;
        private readonly List<PanelState> _sorted = new();
        private readonly List<DrawItem> _draws = new();
        private readonly List<DrawItem> _worldCustomDraws = new();
        private readonly Dictionary<Shader, (bool Yaui, int ForwardPass)> _customShaders = new();
        private readonly BlockPool _overlayBlocks = new();
        private readonly BlockPool _worldBlocks = new();
        private GraphicsBuffer _indices;
        private int _indexedQuads;
        private bool _overlayNeedsStencil;
        private Camera _target;

        private struct DrawItem
        {
            public int Count;
            public Matrix4x4 Projection;
            public MaterialPropertyBlock Properties;
            public Material Material;
            public int Pass;

            /// <summary>Custom draws: the mesh (null for primitives), its submesh and its model matrix.</summary>
            public Mesh Mesh;

            public int Submesh;
            public Matrix4x4 Model;
        }

        /// <summary>Property blocks reused every frame, for draws of a varying number.</summary>
        private sealed class BlockPool
        {
            private readonly List<MaterialPropertyBlock> _blocks = new();
            private int _used;

            public void Reset()
            {
                _used = 0;
            }

            public MaterialPropertyBlock Next()
            {
                if (_used == _blocks.Count) _blocks.Add(new MaterialPropertyBlock());

                var block = _blocks[_used++];
                block.Clear();
                return block;
            }
        }

        public PanelRenderer()
        {
            var shader = Resources.Load<Shader>("Yaui/Uber");
            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, renderQueue = 3000 };
            _pixelClipMaterial = new Material(_material) { hideFlags = HideFlags.HideAndDontSave };
            _pixelClipMaterial.EnableKeyword("YAUI_PIXEL_CLIP");
            _maskMaterial = new Material(Resources.Load<Shader>("Yaui/Mask"))
            {
                hideFlags = HideFlags.HideAndDontSave, renderQueue = 3000
            };
            _pixelClipMaskMaterial = new Material(_maskMaterial) { hideFlags = HideFlags.HideAndDontSave };
            _pixelClipMaskMaterial.EnableKeyword("YAUI_PIXEL_CLIP");
            _pass = new OverlayRenderPass(this);
            _sceneViewPass = new SceneViewRenderPass(this);
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        /// <summary>
        /// Pixel size of the camera the overlay was last drawn on, or zero. Overlay panels are laid out for this size.
        /// </summary>
        public Vector2Int TargetSize { get; private set; }

        /// <summary>Picks the camera to draw overlays on: the last game camera rendering to the screen.</summary>
        public void SelectCamera(List<Camera> cameras)
        {
            _target = null;
            foreach (var camera in cameras)
                if (camera.cameraType == CameraType.Game && camera.targetTexture == null)
                    _target = camera;

            if (_target != null) TargetSize = new Vector2Int(_target.pixelWidth, _target.pixelHeight);
        }

        /// <summary>Main thread, after the uploads: binds the shared data (read when the draws execute).</summary>
        public void Prepare()
        {
            // Overlay draws need the index buffer too.
            EnsureIndices(MaxDrawCount());
            // Globals, so that custom materials see them too.
            Shader.SetGlobalBuffer(PrimitivesId, YauiSystem.Primitives.Buffer);
            Shader.SetGlobalBuffer(ExtsId, YauiSystem.Exts.Buffer);
            Shader.SetGlobalBuffer(NodesId, YauiSystem.Nodes.Gpu.Buffer);
            Shader.SetGlobalBuffer(ClipsId, YauiSystem.Clips.Buffer);
            YauiSystem.Textures.Atlas.RestoreIfLost();
        }

        /// <summary>
        /// Main thread, at submission: queues the world space panels for the cameras of this frame. Queuing at
        /// collection (inside the render pipeline) is too late; the buffers are bound and filled before they draw.
        /// </summary>
        public void QueueWorldPanels()
        {
            var maxQuads = 0;
            foreach (var panel in YauiSystem.AllPanels) maxQuads = Math.Max(maxQuads, panel.DrawCount);

            EnsureIndices(maxQuads);
            _worldBlocks.Reset();
            foreach (var panel in YauiSystem.AllPanels)
                if (panel.HasDraws && panel.OrderBuffer != null && panel.Panel.RenderMode == PanelRenderMode.World)
                    RenderWorld(panel);
        }

        private static int MaxDrawCount()
        {
            var max = 0;
            foreach (var panel in YauiSystem.AllPanels)
                // Text glyph blocks may grow at collection; leave room.
                max = Math.Max(max, panel.DrawCount);

            return max;
        }

        private void EnsureIndices(int quads)
        {
            if (_indices != null && _indexedQuads >= quads) return;

            _indexedQuads = Math.Max(1024, Mathf.NextPowerOfTwo(quads));
            _indices?.Dispose();
            _indices = new GraphicsBuffer(GraphicsBuffer.Target.Index, _indexedQuads * 6, sizeof(uint));
            var data = new uint[_indexedQuads * 6];
            for (var q = 0; q < _indexedQuads; q++)
            {
                var v = (uint)q * 4;
                // Two triangles: (0,0) (0,1) (1,0) / (1,0) (0,1) (1,1), corner = x | y << 1.
                data[q * 6 + 0] = v;
                data[q * 6 + 1] = v + 2;
                data[q * 6 + 2] = v + 1;
                data[q * 6 + 3] = v + 1;
                data[q * 6 + 4] = v + 2;
                data[q * 6 + 5] = v + 3;
            }

            _indices.SetData(data);
        }

        private readonly List<Camera> _worldCameras = new();

        /// <summary>Binds the textures of a segment to the slots of its draw.</summary>
        private void BindTextures(PanelState panel, DrawSegment segment, MaterialPropertyBlock properties)
        {
            var registry = YauiSystem.Textures;
            var ids0 = new Vector4(-1f, -1f, -1f, -1f);
            var ids1 = ids0;
            for (var slot = 0; slot < TextureRegistry.SlotCount; slot++)
            {
                var id = slot < segment.TextureCount ? panel.SegmentTextures[segment.TextureStart + slot] : 0;
                var texture = registry.Get(id);
                properties.SetTexture(TextureIds[slot], texture != null ? texture : Texture2D.whiteTexture);
                _atlasParams[slot] = registry.Parameters(id);
                if (id > 0)
                {
                    if (slot < 4)
                        ids0[slot] = id;
                    else
                        ids1[slot - 4] = id;
                }
            }

            properties.SetVector(TexIds0Id, ids0);
            properties.SetVector(TexIds1Id, ids1);
            properties.SetVectorArray(AtlasParamsId, _atlasParams);
        }

        private void RenderWorld(PanelState panel)
        {
            var matrix = panel.Panel.CanvasToWorld;
            var size = (Vector2)panel.CanvasSize;
            var bounds = new Bounds(matrix.MultiplyPoint3x4(Vector3.zero), Vector3.zero);
            bounds.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(size.x, 0f)));
            bounds.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(0f, size.y)));
            bounds.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(size.x, size.y)));
            for (var i = 0; i < panel.Segments.Count; i++)
            {
                if (panel.Segments[i].Kind == SegmentKind.Custom) continue;

                var properties = panel.SegmentProperties[i];
                properties.SetBuffer(OrderId, panel.OrderBuffer);
                properties.SetInt(OrderOffsetId, panel.Segments[i].Start);
                properties.SetMatrix(PanelMatrixId, matrix);
                BindTextures(panel, panel.Segments[i], properties);
            }

            if (panel.Segments.Count == 1)
            {
                RenderSegment(panel, 0, matrix, bounds, null);
                return;
            }

            // URP sorts transparent draws back to front and reorders ties by material, which would break the order
            // of the segments (a mask's shape, its content, and its removal). Each segment is placed a little closer
            // to each camera than the one before.
            CollectWorldCameras();
            foreach (var camera in _worldCameras)
            {
                var center = bounds.center;
                var towards = camera.orthographic
                    ? -camera.transform.forward
                    : (camera.transform.position - center).normalized;
                var step = 1e-4f + 1e-5f * Vector3.Distance(camera.transform.position, center);
                for (var i = 0; i < panel.Segments.Count; i++)
                {
                    var shifted = bounds;
                    shifted.center = center + towards * (step * i);
                    RenderSegment(panel, i, matrix, shifted, camera);
                }
            }
        }

        private void RenderSegment(PanelState panel, int index, Matrix4x4 panelMatrix, Bounds bounds, Camera camera)
        {
            if (panel.Segments[index].Kind == SegmentKind.Custom)
            {
                RenderCustomSegment(panel, index, panelMatrix, bounds, camera);
                return;
            }

            var renderParams = new RenderParams(MaterialOf(panel, panel.Segments[index]))
            {
                camera = camera,
                worldBounds = bounds,
                matProps = panel.SegmentProperties[index],
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                layer = panel.Panel.gameObject.layer
            };
            Graphics.RenderPrimitivesIndexed(renderParams, MeshTopology.Triangles, _indices,
                panel.Segments[index].Count * 6);
        }

        /// <summary>
        /// The meshes of a custom draw on a world space panel, at the bounds of the panel so that URP keeps them in
        /// the order of the segments. They were recorded at the last collection.
        /// </summary>
        private void RenderCustomSegment(PanelState panel, int index, Matrix4x4 panelMatrix, Bounds bounds,
            Camera camera)
        {
            _worldCustomDraws.Clear();
            AddCustomDraws(_worldCustomDraws, panel, panel.Segments[index], true, panelMatrix, default, 0f,
                _worldBlocks);
            foreach (var draw in _worldCustomDraws)
            {
                var renderParams = new RenderParams(draw.Material)
                {
                    camera = camera,
                    worldBounds = bounds,
                    matProps = draw.Properties,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                    layer = panel.Panel.gameObject.layer
                };
                Graphics.RenderMesh(renderParams, draw.Mesh, draw.Submesh, draw.Model);
            }
        }

        /// <summary>
        /// Appends the meshes of a custom segment. <paramref name="panelMatrix"/>: canvas space to the space of the
        /// view-projection (identity for overlays). Materials of YAUI shaders read the node on the GPU; others get
        /// the model matrix only.
        /// </summary>
        private void AddCustomDraws(List<DrawItem> output, PanelState panel, DrawSegment segment, bool world,
            Matrix4x4 panelMatrix, Matrix4x4 projection, float pixelSize, BlockPool blocks)
        {
            var custom = segment.CustomDraw;
            if (custom == null) return;

            var element = custom.OwnerElement;
            if (element == null || element.NodeSlot <= 0 || !panel.TryGetWorld(element, out var node)) return;

            var nodeMatrix = Matrix4x4.identity;
            nodeMatrix.m00 = node.c0.x;
            nodeMatrix.m10 = node.c0.y;
            nodeMatrix.m01 = node.c1.x;
            nodeMatrix.m11 = node.c1.y;
            nodeMatrix.m03 = node.c2.x;
            nodeMatrix.m13 = node.c2.y;
            nodeMatrix.m22 = FlatZ;
            var toTarget = panelMatrix * nodeMatrix;
            foreach (var item in custom.Draws.Items)
            {
                if (item.Mesh == null || item.Material == null) continue;

                var draw = new DrawItem
                {
                    Mesh = item.Mesh,
                    Submesh = item.Submesh,
                    Model = toTarget * item.LocalMatrix,
                    Projection = projection
                };
                var shader = ShaderInfo(item.Material);
                if (shader.Yaui)
                {
                    draw.Material = WithStencil(item.Material, SegmentKind.Draw, segment.StencilDepth, true);
                    draw.Pass = draw.Material.FindPass(world ? "World" : "Overlay");
                    var properties = blocks.Next();
                    properties.SetInt(NodeId, element.NodeSlot);
                    properties.SetMatrix(MeshMatrixId, item.LocalMatrix);
                    properties.SetFloat(PixelSizeId, pixelSize);
                    properties.SetMatrix(PanelMatrixId, panelMatrix);
                    draw.Properties = properties;
                }
                else
                {
                    draw.Material = item.Material;
                    draw.Pass = shader.ForwardPass;
                }

                if (draw.Pass >= 0) output.Add(draw);
            }
        }

        /// <summary>
        /// Whether a shader is a YAUI shader (an "Overlay" pass with the YauiOverlay light mode), and otherwise its
        /// first pass that URP draws in the forward path.
        /// </summary>
        private (bool Yaui, int ForwardPass) ShaderInfo(Material material)
        {
            var shader = material.shader;
            if (_customShaders.TryGetValue(shader, out var info)) return info;

            var overlay = material.FindPass("Overlay");
            info.Yaui = overlay >= 0 && shader.FindPassTagValue(overlay, LightModeTag) == YauiOverlayTag;
            info.ForwardPass = -1;
            for (var i = 0; i < shader.passCount && info.ForwardPass < 0; i++)
            {
                var lightMode = shader.FindPassTagValue(i, LightModeTag).name;
                if (string.IsNullOrEmpty(lightMode) || lightMode is "UniversalForward" or "UniversalForwardOnly" or
                        "SRPDefaultUnlit")
                    info.ForwardPass = i;
            }

            _customShaders[shader] = info;
            return info;
        }

        private void CollectWorldCameras()
        {
            _worldCameras.Clear();
            foreach (var camera in Camera.allCameras) _worldCameras.Add(camera);
#if UNITY_EDITOR
            foreach (UnityEditor.SceneView view in UnityEditor.SceneView.sceneViews)
                if (view.camera != null)
                    _worldCameras.Add(view.camera);
#endif
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType == CameraType.SceneView)
            {
                // Overlay panels as quads in the scene (world space panels are drawn by URP in every camera).
                foreach (var panel in YauiSystem.AllPanels)
                    if (panel.HasDraws && panel.Panel.RenderMode == PanelRenderMode.Overlay)
                    {
                        camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(_sceneViewPass);
                        return;
                    }

                return;
            }

            if (camera != _target) return;

            foreach (var panel in YauiSystem.AllPanels)
                if (panel.HasDraws && panel.Panel.RenderMode == PanelRenderMode.Overlay)
                {
                    camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(_pass);
                    return;
                }
        }

        private void PrepareOverlayDraws(Camera camera)
        {
            _sorted.Clear();
            foreach (var panel in YauiSystem.AllPanels)
                if (panel.HasDraws && panel.OrderBuffer != null &&
                    panel.Panel.RenderMode == PanelRenderMode.Overlay)
                    _sorted.Add(panel);

            _sorted.Sort(SortOrderComparer.Instance);
            _draws.Clear();
            _overlayBlocks.Reset();
            _overlayNeedsStencil = false;
            foreach (var panel in _sorted)
            {
                _overlayNeedsStencil |= panel.HasMasks;

                // Canvas space: origin at the top-left, Y down.
                var projection = Matrix4x4.Ortho(0f, panel.CanvasSize.x, panel.CanvasSize.y, 0f, -1f, 1f);
                var pixelSize = panel.CanvasSize.x / camera.pixelWidth;
                for (var i = 0; i < panel.Segments.Count; i++)
                {
                    var segment = panel.Segments[i];
                    if (segment.Kind == SegmentKind.Custom)
                    {
                        AddCustomDraws(_draws, panel, segment, false, Matrix4x4.identity, projection, pixelSize,
                            _overlayBlocks);
                        continue;
                    }

                    var properties = panel.SegmentProperties[i];
                    properties.SetBuffer(OrderId, panel.OrderBuffer);
                    properties.SetInt(OrderOffsetId, segment.Start);
                    properties.SetFloat(PixelSizeId, pixelSize);
                    BindTextures(panel, segment, properties);
                    var segmentMaterial = MaterialOf(panel, segment);
                    var pass = segmentMaterial.FindPass("Overlay");
                    if (pass < 0) continue;

                    _draws.Add(new DrawItem
                    {
                        Count = segment.Count,
                        Projection = projection,
                        Properties = properties,
                        Material = segmentMaterial,
                        Pass = pass
                    });
                }
            }
        }

        /// <summary>
        /// The material of a draw: the segment's own or the uber shader (the mask shader for mask shapes), with or
        /// without per-pixel clips, and with the stencil state of the segment.
        /// </summary>
        private Material MaterialOf(PanelState panel, DrawSegment segment)
        {
            Material baseMaterial;
            if (segment.Kind != SegmentKind.Draw)
                baseMaterial = panel.NeedsPixelClip ? _pixelClipMaskMaterial : _maskMaterial;
            else if (segment.Material != null)
                baseMaterial = segment.Material;
            else
                baseMaterial = panel.NeedsPixelClip ? _pixelClipMaterial : _material;

            return WithStencil(baseMaterial, segment.Kind, segment.StencilDepth, segment.Material != null);
        }

        /// <summary>
        /// A material with the stencil state of a draw inside <paramref name="depth"/> masks. <paramref name="custom"/>:
        /// a user's material, whose properties may have changed since the last draw.
        /// </summary>
        private Material WithStencil(Material baseMaterial, SegmentKind kind, int depth, bool custom)
        {
            if (kind == SegmentKind.Draw && depth == 0) return baseMaterial;

            var key = (baseMaterial, kind, depth);
            if (!_stencilMaterials.TryGetValue(key, out var derived) || derived == null)
            {
                derived = new Material(baseMaterial) { hideFlags = HideFlags.HideAndDontSave };
                _stencilMaterials[key] = derived;
            }
            else if (custom)
            {
                // A custom material may have changed since.
                derived.CopyPropertiesFromMaterial(baseMaterial);
            }

            switch (kind)
            {
                case SegmentKind.MaskPush:
                    derived.SetFloat(StencilRefId, depth - 1);
                    derived.SetFloat(StencilCompId, (float)CompareFunction.Equal);
                    derived.SetFloat(StencilPassId, (float)StencilOp.IncrementSaturate);
                    break;
                case SegmentKind.MaskPop:
                    derived.SetFloat(StencilRefId, depth);
                    derived.SetFloat(StencilCompId, (float)CompareFunction.Equal);
                    derived.SetFloat(StencilPassId, (float)StencilOp.DecrementSaturate);
                    break;
                default:
                    derived.SetFloat(StencilRefId, depth);
                    derived.SetFloat(StencilCompId, (float)CompareFunction.Equal);
                    derived.SetFloat(StencilPassId, (float)StencilOp.Keep);
                    break;
            }

            return derived;
        }

        private static void DestroyMaterial(Material m)
        {
            if (m == null) return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(m);
            else
                UnityEngine.Object.DestroyImmediate(m);
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            _indices?.Dispose();
            _indices = null;
            DestroyMaterial(_material);
            DestroyMaterial(_pixelClipMaterial);
            DestroyMaterial(_maskMaterial);
            DestroyMaterial(_pixelClipMaskMaterial);
            foreach (var derived in _stencilMaterials.Values) DestroyMaterial(derived);

            _stencilMaterials.Clear();
        }

        private sealed class SortOrderComparer : IComparer<PanelState>
        {
            public static readonly SortOrderComparer Instance = new();

            public int Compare(PanelState a, PanelState b)
            {
                return a.Panel.SortOrder.CompareTo(b.Panel.SortOrder);
            }
        }

        /// <summary>Draws overlay panels in the Scene view with the world pass, at <see cref="YauiPanel.SceneViewCanvasToWorld"/>.</summary>
        private sealed class SceneViewRenderPass : ScriptableRenderPass
        {
            private readonly PanelRenderer _renderer;
            private readonly List<DrawItem> _draws = new();
            private readonly BlockPool _blocks = new();

            public SceneViewRenderPass(PanelRenderer renderer)
            {
                this._renderer = renderer;
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            }

            private sealed class PassData
            {
                public List<DrawItem> Draws;
                public GraphicsBuffer Indices;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                _draws.Clear();
                _blocks.Reset();
                foreach (var panel in YauiSystem.AllPanels)
                {
                    if (!panel.HasDraws || panel.OrderBuffer == null ||
                        panel.Panel.RenderMode != PanelRenderMode.Overlay)
                        continue;

                    var matrix = panel.Panel.SceneViewCanvasToWorld;
                    for (var i = 0; i < panel.Segments.Count; i++)
                    {
                        var segment = panel.Segments[i];
                        if (segment.Kind == SegmentKind.Custom)
                        {
                            _renderer.AddCustomDraws(_draws, panel, segment, true, matrix, default, 0f, _blocks);
                            continue;
                        }

                        var segmentMaterial = _renderer.MaterialOf(panel, segment);
                        var pass = segmentMaterial.FindPass("World");
                        if (pass < 0) continue;

                        // A separate block: the game view draws of this frame use the segment's own.
                        var properties = new MaterialPropertyBlock();
                        properties.SetBuffer(OrderId, panel.OrderBuffer);
                        properties.SetInt(OrderOffsetId, segment.Start);
                        properties.SetMatrix(PanelMatrixId, matrix);
                        _renderer.BindTextures(panel, segment, properties);
                        _draws.Add(new DrawItem
                        {
                            Count = segment.Count,
                            Properties = properties,
                            Material = segmentMaterial,
                            Pass = pass
                        });
                    }
                }

                using var builder = renderGraph.AddRasterRenderPass<PassData>("Yaui Scene View", out var data);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);
                data.Draws = _draws;
                data.Indices = _renderer._indices;
                builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                {
                    foreach (var draw in d.Draws)
                        if (draw.Mesh != null)
                            context.cmd.DrawMesh(draw.Mesh, draw.Model, draw.Material, draw.Submesh, draw.Pass,
                                draw.Properties);
                        else
                            context.cmd.DrawProcedural(d.Indices, Matrix4x4.identity, draw.Material, draw.Pass,
                                MeshTopology.Triangles, draw.Count * 6, 1, draw.Properties);
                });
            }
        }

        private sealed class OverlayRenderPass : ScriptableRenderPass
        {
            private readonly PanelRenderer _renderer;

            public OverlayRenderPass(PanelRenderer renderer)
            {
                this._renderer = renderer;
                renderPassEvent = RenderPassEvent.AfterRendering + 10;
            }

            private sealed class PassData
            {
                public List<DrawItem> Draws;
                public GraphicsBuffer Indices;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                _renderer.PrepareOverlayDraws(cameraData.camera);

                using var builder = renderGraph.AddRasterRenderPass<PassData>("Yaui Overlay", out var data);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                if (_renderer._overlayNeedsStencil)
                {
                    // The overlay draws after post-processing, where there is no depth-stencil buffer: a transient,
                    // cleared one for the masks.
                    // The back buffer has no descriptor: it has the camera's pixel size.
                    var width = cameraData.camera.pixelWidth;
                    var height = cameraData.camera.pixelHeight;
                    var samples = MSAASamples.None;
                    if (!resourceData.isActiveTargetBackBuffer)
                    {
                        var colorDesc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
                        width = colorDesc.width;
                        height = colorDesc.height;
                        samples = colorDesc.msaaSamples;
                    }

                    var desc = new TextureDesc(width, height)
                    {
                        name = "Yaui Stencil",
                        format = SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil),
                        msaaSamples = samples,
                        clearBuffer = true
                    };
                    builder.SetRenderAttachmentDepth(renderGraph.CreateTexture(desc), AccessFlags.Write);
                }

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                data.Draws = _renderer._draws;
                data.Indices = _renderer._indices;
                builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                {
                    foreach (var draw in d.Draws)
                    {
                        context.cmd.SetViewProjectionMatrices(Matrix4x4.identity, draw.Projection);
                        if (draw.Mesh != null)
                            context.cmd.DrawMesh(draw.Mesh, draw.Model, draw.Material, draw.Submesh, draw.Pass,
                                draw.Properties);
                        else
                            context.cmd.DrawProcedural(d.Indices, Matrix4x4.identity, draw.Material, draw.Pass,
                                MeshTopology.Triangles, draw.Count * 6, 1, draw.Properties);
                    }
                });
            }
        }
    }
}