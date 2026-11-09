using System;
using System.Collections.Generic;
using Unity.Profiling;
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
    /// <item>Camera panels: a render graph pass of the panel's camera at the panel's event, drawn like overlays into
    /// the camera's target. One pass per event, panels in sort order.</item>
    /// </list>
    /// The meshes of custom draws (<see cref="YauiCustomDraw"/>) are drawn between the segments of primitives, one
    /// draw call each. Before a segment whose material has the "YauiBackdrop" tag, a screen pass ends, the target is
    /// copied and blurred, and the next pass binds the result (<c>_YauiBackdropTexture</c>).
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
        private static readonly int BackdropTextureId = Shader.PropertyToID("_YauiBackdropTexture");
        private static readonly int BackdropParamsId = Shader.PropertyToID("_YauiBackdropParams");
        private static readonly int BlurParamsId = Shader.PropertyToID("_YauiBlurParams");
        private static readonly int BlurId = Shader.PropertyToID("_YauiBlur");
        private const string BackdropTag = "YauiBackdrop";
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

        private static readonly ProfilerMarker PrepareOverlayMarker = new("Yaui.Render.Prepare");
        private static readonly ProfilerMarker RecordOverlayMarker = new("Yaui.Render.Record");
        private static readonly ProfilingSampler BlurSampler = new("Yaui Backdrop Blur");
        private static readonly int StencilRefId = Shader.PropertyToID("_YauiStencilRef");
        private static readonly int StencilCompId = Shader.PropertyToID("_YauiStencilComp");
        private static readonly int StencilPassId = Shader.PropertyToID("_YauiStencilPass");

        private readonly Shader _uberShader;

        // Variants of the uber shader, created when first used: by the features of the draw (ShaderFeatures), then
        // twice as many with per-pixel clipping, for panels that have rotated nodes or rounded clips.
        private readonly Material[] _uberMaterials = new Material[ShaderFeaturesExtensions.Count * 2];

        // The shapes of masks, with and without per-pixel clipping.
        private readonly Material _maskMaterial;
        private readonly Material _pixelClipMaskMaterial;

        // The CRC of the custom material each derived material was last copied from.
        private readonly Dictionary<Material, int> _stencilSources = new();

        // Materials with the stencil state of a segment, by base material, kind and depth.
        private readonly Dictionary<(Material, SegmentKind, int), Material> _stencilMaterials = new();
        private readonly OverlayRenderPass _pass;
        private readonly SceneViewRenderPass _sceneViewPass;
        private readonly Dictionary<RenderPassEvent, CameraRenderPass> _cameraPasses = new();
        private readonly HashSet<RenderPassEvent> _enqueuedEvents = new();
        private readonly List<PanelState> _sorted = new();
        private readonly List<DrawItem> _draws = new();
        private readonly List<DrawItem> _worldCustomDraws = new();
        private readonly Dictionary<Shader, (bool Yaui, int ForwardPass)> _customShaders = new();
        private readonly Dictionary<Shader, bool> _backdropShaders = new();
        private readonly Material _blurMaterial;
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

            /// <summary>
            /// Not a draw: the target is copied and blurred here for the draws that follow, with the blur of this
            /// backdrop material.
            /// </summary>
            public Material Backdrop;

            /// <summary>
            /// In the prepared draws of a panel only: a segment (index + 1) prepared again every frame, in place of
            /// its draws (custom draws, and custom materials copied with a stencil state).
            /// </summary>
            public int Segment;

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
            // The uber shader generated with the project's shader modules, if any (YauiShaderSettings).
            _uberShader = YauiShaderModules.UberShader != null
                ? YauiShaderModules.UberShader
                : Resources.Load<Shader>("Yaui/Uber");
            _maskMaterial = new Material(Resources.Load<Shader>("Yaui/Mask"))
            {
                hideFlags = HideFlags.HideAndDontSave, renderQueue = 3000
            };
            _pixelClipMaskMaterial = new Material(_maskMaterial) { hideFlags = HideFlags.HideAndDontSave };
            _pixelClipMaskMaterial.EnableKeyword("YAUI_PIXEL_CLIP");
            _blurMaterial = new Material(Resources.Load<Shader>("Yaui/BackdropBlur"))
            {
                hideFlags = HideFlags.HideAndDontSave
            };
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
            // Globals, so that custom materials see them too. Set again only when a buffer was recreated.
            BindGlobal(PrimitivesId, YauiSystem.Primitives.Buffer, ref _boundPrimitives);
            BindGlobal(ExtsId, YauiSystem.Exts.Buffer, ref _boundExts);
            BindGlobal(NodesId, YauiSystem.Nodes.Gpu.Buffer, ref _boundNodes);
            BindGlobal(ClipsId, YauiSystem.Clips.Buffer, ref _boundClips);
            YauiSystem.Textures.Atlas.RestoreIfLost();
        }

        private GraphicsBuffer _boundPrimitives;
        private GraphicsBuffer _boundExts;
        private GraphicsBuffer _boundNodes;
        private GraphicsBuffer _boundClips;

        private static void BindGlobal(int id, GraphicsBuffer buffer, ref GraphicsBuffer bound)
        {
            if (buffer == bound) return;

            Shader.SetGlobalBuffer(id, buffer);
            bound = buffer;
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
            {
                if (panel.Panel.RenderMode != PanelRenderMode.World) continue;

                panel.FlushOrder();
                if (panel.Visible && panel.OrderBuffer != null) RenderWorld(panel);
            }
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
            // Nothing to draw this frame (for example, a particle system without particles).
            if (custom == null || custom.Draws.Items.Count == 0) return;

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
                    draw.Pass = PassOf(draw.Material, world ? "World" : "Overlay");
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
                // Unity may return the tag in another case (SRPDEFAULTUNLIT).
                var lightMode = shader.FindPassTagValue(i, LightModeTag).name;
                if (string.IsNullOrEmpty(lightMode) ||
                    string.Equals(lightMode, "UniversalForward", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(lightMode, "UniversalForwardOnly", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(lightMode, "SRPDefaultUnlit", StringComparison.OrdinalIgnoreCase))
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

            // Camera panels: one pass per event among the panels of this camera.
            _enqueuedEvents.Clear();
            foreach (var panel in YauiSystem.AllPanels)
            {
                if (!panel.Visible || panel.Panel.RenderMode != PanelRenderMode.Camera ||
                    panel.Panel.WorldCamera != camera) continue;

                var renderPassEvent = panel.Panel.RenderPassEvent;
                if (!_enqueuedEvents.Add(renderPassEvent)) continue;

                if (!_cameraPasses.TryGetValue(renderPassEvent, out var cameraPass))
                {
                    cameraPass = new CameraRenderPass(this, renderPassEvent);
                    _cameraPasses[renderPassEvent] = cameraPass;
                }

                camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(cameraPass);
            }

            if (camera != _target) return;

            foreach (var panel in YauiSystem.AllPanels)
                if (panel.Visible && panel.Panel.RenderMode == PanelRenderMode.Overlay)
                {
                    camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(_pass);
                    return;
                }
        }

        private void PrepareOverlayDraws(Camera camera)
        {
            using var _ = PrepareOverlayMarker.Auto();
            _sorted.Clear();
            foreach (var panel in YauiSystem.AllPanels)
            {
                if (panel.Panel.RenderMode != PanelRenderMode.Overlay) continue;

                panel.FlushOrder();
                if (panel.Visible && panel.OrderBuffer != null) _sorted.Add(panel);
            }

            _sorted.Sort(SortOrderComparer.Instance);
            _overlayNeedsStencil = PrepareScreenDraws(_sorted, camera, _draws, _overlayBlocks);
        }

        /// <summary>
        /// What the draws of a screen pass were prepared from, so that a frame where nothing changed reuses them: the
        /// panels, their canvas sizes, order buffers and segments (ranges, kinds, materials, stencil depths, features
        /// and textures), and the camera's width. The contents of the buffers are read by the GPU and need no
        /// preparation. Panels with custom draws are prepared every frame (their meshes follow the nodes).
        /// </summary>
        internal sealed class ScreenDrawCache
        {
            public bool Valid;
            public int Signature;
            public bool NeedsStencil;
        }

        /// <summary>The signatures of the panels of the last <see cref="ScreenSignature"/>, in order.</summary>
        private readonly List<int> _panelSignatures = new();

        private int ScreenSignature(List<PanelState> panels, Camera camera, out bool cacheable)
        {
            cacheable = true;
            var hash = new HashCode();
            hash.Add(panels.Count);
            _panelSignatures.Clear();
            foreach (var panel in panels)
            {
                var signature = PanelSignature(panel, camera, out var panelCacheable);
                _panelSignatures.Add(signature);
                hash.Add(signature);
                cacheable &= panelCacheable;
            }

            return hash.ToHashCode();
        }

        /// <summary>
        /// What the draws of one panel were prepared from (see <see cref="ScreenDrawCache"/>). Not cacheable as a whole
        /// if some segments are prepared every frame (<see cref="IsDynamic"/>).
        /// </summary>
        private static int PanelSignature(PanelState panel, Camera camera, out bool cacheable)
        {
            cacheable = true;
            var hash = new HashCode();
            hash.Add(camera.pixelWidth);
            var textures = YauiSystem.Textures;
            {
                hash.Add(panel.GetHashCode());
                hash.Add(panel.CanvasSize);
                hash.Add(panel.OrderBuffer != null ? panel.OrderBuffer.GetHashCode() : 0);
                hash.Add(panel.NeedsPixelClip);
                hash.Add(panel.Segments.Count);
                foreach (var segment in panel.Segments)
                {
                    if (IsDynamic(segment)) cacheable = false;

                    hash.Add(segment.Start);
                    hash.Add(segment.Count);
                    hash.Add(segment.Kind);
                    hash.Add(segment.StencilDepth);
                    hash.Add(segment.Features);
                    hash.Add(segment.Material != null ? segment.Material.GetHashCode() : 0);
                    hash.Add(segment.MaskMaterial != null ? segment.MaskMaterial.GetHashCode() : 0);
                    for (var slot = 0; slot < segment.TextureCount; slot++)
                    {
                        var id = panel.SegmentTextures[segment.TextureStart + slot];
                        var texture = textures.Get(id);
                        hash.Add(id);
                        hash.Add(texture != null ? texture.GetHashCode() : 0);
                        hash.Add(textures.Parameters(id));
                    }
                }
            }

            return hash.ToHashCode();
        }

        /// <summary>
        /// The draws of panels drawn over a camera's target in canvas space (overlay and camera panels), in the
        /// order of <paramref name="panels"/>, with a backdrop item before each segment of a backdrop material.
        /// Returns whether a panel has masks (a stencil buffer is needed).
        /// </summary>
        private bool PrepareScreenDraws(List<PanelState> panels, Camera camera, List<DrawItem> draws, BlockPool blocks,
            ScreenDrawCache cache = null)
        {
            if (cache != null)
            {
                var signature = ScreenSignature(panels, camera, out var cacheable);
                if (cacheable && cache.Valid && cache.Signature == signature)
                {
                    _panelSignatures.Clear();
                    return cache.NeedsStencil;
                }

                cache.Valid = cacheable;
                cache.Signature = signature;
                cache.NeedsStencil = PrepareScreenDraws(panels, camera, draws, blocks);
                return cache.NeedsStencil;
            }

            draws.Clear();
            blocks.Reset();
            var needsStencil = false;
            var signed = _panelSignatures.Count == panels.Count;
            for (var p = 0; p < panels.Count; p++)
            {
                var panel = panels[p];
                needsStencil |= panel.HasMasks;

                // Canvas space: origin at the top-left, Y down.
                var projection = Matrix4x4.Ortho(0f, panel.CanvasSize.x, panel.CanvasSize.y, 0f, -1f, 1f);
                var pixelSize = panel.CanvasSize.x / camera.pixelWidth;

                // The draws of the panel are kept while its signature stays; dynamic segments are prepared again.
                var signature = signed ? _panelSignatures[p] : PanelSignature(panel, camera, out _);
                if (!_panelDraws.TryGetValue(panel, out var prepared))
                {
                    prepared = new PanelDraws();
                    _panelDraws[panel] = prepared;
                }

                if (!prepared.Valid || prepared.Signature != signature)
                {
                    prepared.Items.Clear();
                    for (var i = 0; i < panel.Segments.Count; i++)
                        if (IsDynamic(panel.Segments[i]))
                            prepared.Items.Add(new DrawItem { Segment = i + 1 });
                        else
                            AppendSegment(prepared.Items, panel, i, projection, pixelSize, blocks);

                    prepared.Valid = true;
                    prepared.Signature = signature;
                }

                foreach (var item in prepared.Items)
                    if (item.Segment > 0)
                        AppendSegment(draws, panel, item.Segment - 1, projection, pixelSize, blocks);
                    else
                        draws.Add(item);
            }

            _panelSignatures.Clear();
            return needsStencil;
        }

        private sealed class PanelDraws
        {
            public bool Valid;
            public int Signature;
            public readonly List<DrawItem> Items = new();
        }

        private readonly Dictionary<PanelState, PanelDraws> _panelDraws = new();

        /// <summary>
        /// Segments prepared every frame: custom draws (their meshes and matrices follow the nodes) and custom materials
        /// copied with a stencil state (the copy takes the material's properties when prepared).
        /// </summary>
        private static bool IsDynamic(DrawSegment segment)
        {
            return segment.Kind == SegmentKind.Custom || segment.MaskMaterial != null ||
                   segment.Material != null && segment.StencilDepth > 0;
        }

        /// <summary>Appends the draws of one segment of a screen panel.</summary>
        private void AppendSegment(List<DrawItem> draws, PanelState panel, int i, Matrix4x4 projection, float pixelSize,
            BlockPool blocks)
        {
            {
                {
                    var segment = panel.Segments[i];
                    if (segment.Kind == SegmentKind.Custom)
                    {
                        AddCustomDraws(draws, panel, segment, false, Matrix4x4.identity, projection, pixelSize, blocks);
                        return;
                    }

                    var properties = panel.SegmentProperties[i];
                    // The block keeps its values between frames: set them again only when they changed.
                    var binding = SegmentBinding(panel, segment, pixelSize);
                    if (!_screenBindings.TryGetValue(properties, out var bound) || bound != binding)
                    {
                        properties.SetBuffer(OrderId, panel.OrderBuffer);
                        properties.SetInt(OrderOffsetId, segment.Start);
                        properties.SetFloat(PixelSizeId, pixelSize);
                        BindTextures(panel, segment, properties);
                        _screenBindings[properties] = binding;
                    }

                    var customMask = segment.Kind != SegmentKind.Draw && segment.MaskMaterial != null;
                    var segmentMaterial = customMask
                        ? WithStencil(segment.MaskMaterial, segment.Kind, segment.StencilDepth, true)
                        : MaterialOf(panel, segment);
                    var pass = PassOf(segmentMaterial, customMask ? PanelState.MaskOverlayPass : "Overlay");
                    if (pass < 0) return;

                    if (segment.Kind == SegmentKind.Draw && IsBackdrop(segment.Material))
                        draws.Add(new DrawItem { Backdrop = segment.Material });

                    draws.Add(new DrawItem
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

        // Pass indices by shader and pass name: Material.FindPass is a native call by name.
        private readonly Dictionary<(Shader, string), int> _passes = new();

        private int PassOf(Material material, string name)
        {
            var key = (material.shader, name);
            if (_passes.TryGetValue(key, out var pass)) return pass;

            pass = material.FindPass(name);
            _passes[key] = pass;
            return pass;
        }

        // What each segment's block of a screen panel was last bound with (SegmentBinding).
        private readonly Dictionary<MaterialPropertyBlock, int> _screenBindings = new();

        /// <summary>The values a screen draw binds to the block of a segment: order buffer, range, pixel size, textures.</summary>
        private static int SegmentBinding(PanelState panel, DrawSegment segment, float pixelSize)
        {
            var hash = new HashCode();
            hash.Add(panel.OrderBuffer != null ? panel.OrderBuffer.GetHashCode() : 0);
            hash.Add(segment.Start);
            hash.Add(pixelSize);
            var textures = YauiSystem.Textures;
            for (var slot = 0; slot < segment.TextureCount; slot++)
            {
                var id = panel.SegmentTextures[segment.TextureStart + slot];
                var texture = textures.Get(id);
                hash.Add(id);
                hash.Add(texture != null ? texture.GetHashCode() : 0);
                hash.Add(textures.Parameters(id));
            }

            hash.Add(segment.TextureCount);
            return hash.ToHashCode();
        }

        /// <summary>Whether a material reads the backdrop (its shader has the "YauiBackdrop" tag).</summary>
        private bool IsBackdrop(Material material)
        {
            if (material == null || material.shader == null) return false;

            var shader = material.shader;
            if (_backdropShaders.TryGetValue(shader, out var backdrop)) return backdrop;

            // Unity returns tag values in lower case.
            backdrop = string.Equals(material.GetTag(BackdropTag, false), "true", StringComparison.OrdinalIgnoreCase);
            _backdropShaders[shader] = backdrop;
            return backdrop;
        }

        private sealed class ScreenPassData
        {
            public List<DrawItem> Draws;
            public int Start;
            public int End;
            public GraphicsBuffer Indices;
            public bool Restore;
            public Matrix4x4 View;
            public Matrix4x4 Projection;
            public TextureHandle Backdrop;
            public Vector4 BackdropParams;
        }

        private sealed class BlitPassData
        {
            public TextureHandle Source;
            public Material Material;
            public int Pass;
            public Vector4 Params;
        }

        /// <summary>
        /// Records the draws of a screen pass into <paramref name="color"/>: one raster pass, or several when the draws
        /// have backdrops. Before each backdrop the target is copied and blurred, and the passes that follow bind the
        /// result. <paramref name="depthCleared"/>: <paramref name="depth"/> is a transient buffer cleared at its first
        /// use. <paramref name="readable"/>: whether the target can be sampled (not the back buffer); otherwise the
        /// backdrops show their color only. <paramref name="restore"/>: the view and projection to set back at the end of
        /// each pass (camera panels), or null.
        /// </summary>
        private void RecordScreen(RenderGraph renderGraph, ProfilingSampler sampler, List<DrawItem> draws,
            TextureHandle color, TextureHandle depth, bool depthCleared, bool readable, Vector2Int targetSize,
            (Matrix4x4 View, Matrix4x4 Projection)? restore)
        {
            var start = 0;
            var backdrop = TextureHandle.nullHandle;
            var firstDepth = true;
            for (var i = 0; i <= draws.Count; i++)
            {
                if (i < draws.Count && draws[i].Backdrop == null) continue;

                if (i > start)
                {
                    using var builder = renderGraph.AddRasterRenderPass<ScreenPassData>(sampler.name, out var data,
                        sampler);
                    builder.SetRenderAttachment(color, 0);
                    if (depth.IsValid())
                    {
                        builder.SetRenderAttachmentDepth(depth,
                            depthCleared && firstDepth ? AccessFlags.Write : AccessFlags.ReadWrite);
                        firstDepth = false;
                    }

                    // Without a copy (the back buffer, or no backdrop yet), backdrops show their color only.
                    var bound = backdrop.IsValid() ? backdrop : renderGraph.defaultResources.whiteTexture;
                    builder.UseTexture(bound);

                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);
                    data.Draws = draws;
                    data.Start = start;
                    data.End = i;
                    data.Indices = _indices;
                    data.Restore = restore.HasValue;
                    data.View = restore?.View ?? default;
                    data.Projection = restore?.Projection ?? default;
                    data.Backdrop = bound;
                    data.BackdropParams =
                        new Vector4(1f / Mathf.Max(targetSize.x, 1), 1f / Mathf.Max(targetSize.y, 1), 0f, 0f);
                    builder.SetRenderFunc(static (ScreenPassData d, RasterGraphContext context) =>
                    {
                        using var _ = RecordOverlayMarker.Auto();
                        context.cmd.SetGlobalTexture(BackdropTextureId, d.Backdrop);
                        context.cmd.SetGlobalVector(BackdropParamsId, d.BackdropParams);

                        var projection = default(Matrix4x4);
                        var first = true;
                        for (var index = d.Start; index < d.End; index++)
                        {
                            var draw = d.Draws[index];
                            // Once per panel.
                            if (first || draw.Projection != projection)
                            {
                                context.cmd.SetViewProjectionMatrices(Matrix4x4.identity, draw.Projection);
                                projection = draw.Projection;
                                first = false;
                            }

                            if (draw.Mesh != null)
                                context.cmd.DrawMesh(draw.Mesh, draw.Model, draw.Material, draw.Submesh, draw.Pass,
                                    draw.Properties);
                            else
                                context.cmd.DrawProcedural(d.Indices, Matrix4x4.identity, draw.Material, draw.Pass,
                                    MeshTopology.Triangles, draw.Count * 6, 1, draw.Properties);
                        }

                        if (d.Restore) context.cmd.SetViewProjectionMatrices(d.View, d.Projection);
                    });
                }

                if (i == draws.Count) break;

                var material = draws[i].Backdrop;
                backdrop = readable
                    ? RecordBlur(renderGraph, color, material.HasFloat(BlurId) ? material.GetFloat(BlurId) : 0f)
                    : TextureHandle.nullHandle;
                start = i + 1;
            }
        }

        /// <summary>
        /// A blurred copy of <paramref name="source"/>: halved while the blur is wide (each halving averages 2x2
        /// pixels), then a separable Gaussian of the remaining width. <paramref name="sigma"/> is the standard
        /// deviation in pixels of the source.
        /// </summary>
        private TextureHandle RecordBlur(RenderGraph renderGraph, TextureHandle source, float sigma)
        {
            var sourceDesc = renderGraph.GetTextureDesc(source);
            var width = sourceDesc.width;
            var height = sourceDesc.height;
            // About 2 to 4 texels of blur remain after the halvings.
            var levels = sigma > 4f ? Mathf.Clamp(Mathf.FloorToInt(Mathf.Log(sigma / 2f, 2f)), 0, 4) : 0;
            var current = source;
            for (var level = 0; level < levels; level++)
            {
                width = Mathf.Max(1, width / 2);
                height = Mathf.Max(1, height / 2);
                var next = renderGraph.CreateTexture(BlurDesc(sourceDesc, width, height, "Yaui Backdrop Down"));
                AddBlit(renderGraph, current, next, null, 0, default);
                current = next;
            }

            var scaled = sigma / (1 << levels);
            var radius = Mathf.Clamp(Mathf.CeilToInt(scaled * 3f), 0, 24);
            var parameters = new Vector4(1f / width, 1f / height, scaled, radius);
            var horizontal = renderGraph.CreateTexture(BlurDesc(sourceDesc, width, height, "Yaui Backdrop Blur H"));
            AddBlit(renderGraph, current, horizontal, _blurMaterial, 0, parameters);
            var vertical = renderGraph.CreateTexture(BlurDesc(sourceDesc, width, height, "Yaui Backdrop Blur"));
            AddBlit(renderGraph, horizontal, vertical, _blurMaterial, 1, parameters);
            return vertical;
        }

        private static TextureDesc BlurDesc(TextureDesc source, int width, int height, string name)
        {
            return new TextureDesc(width, height)
            {
                name = name,
                format = source.format,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                msaaSamples = MSAASamples.None,
                clearBuffer = false
            };
        }

        /// <summary>A bilinear copy (<paramref name="material"/> null) or a pass of the blur material.</summary>
        private static void AddBlit(RenderGraph renderGraph, TextureHandle source, TextureHandle destination,
            Material material, int pass, Vector4 parameters)
        {
            using var builder = renderGraph.AddRasterRenderPass<BlitPassData>(BlurSampler.name, out var data,
                BlurSampler);
            builder.UseTexture(source);
            builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
            builder.AllowGlobalStateModification(true);
            data.Source = source;
            data.Material = material;
            data.Pass = pass;
            data.Params = parameters;
            builder.SetRenderFunc(static (BlitPassData d, RasterGraphContext context) =>
            {
                if (d.Material == null)
                {
                    Blitter.BlitTexture(context.cmd, d.Source, new Vector4(1f, 1f, 0f, 0f), 0f, true);
                    return;
                }

                context.cmd.SetGlobalVector(BlurParamsId, d.Params);
                Blitter.BlitTexture(context.cmd, d.Source, new Vector4(1f, 1f, 0f, 0f), d.Material, d.Pass);
            });
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
                baseMaterial = UberMaterial(segment.Features, panel.NeedsPixelClip);

            return WithStencil(baseMaterial, segment.Kind, segment.StencilDepth, segment.Material != null);
        }

        /// <summary>The variant of the uber shader with <paramref name="features"/>.</summary>
        private Material UberMaterial(ShaderFeatures features, bool pixelClip)
        {
            var index = (int)features + (pixelClip ? ShaderFeaturesExtensions.Count : 0);
            var material = _uberMaterials[index];
            if (material != null) return material;

            material = new Material(_uberShader) { hideFlags = HideFlags.HideAndDontSave, renderQueue = 3000 };
            if (pixelClip) material.EnableKeyword("YAUI_PIXEL_CLIP");
            if ((features & ShaderFeatures.Text) != 0) material.EnableKeyword("YAUI_TEXT");
            if ((features & ShaderFeatures.Image) != 0) material.EnableKeyword("YAUI_IMAGE");
            if ((features & ShaderFeatures.Border) != 0) material.EnableKeyword("YAUI_BORDER");
            if ((features & ShaderFeatures.Shadow) != 0) material.EnableKeyword("YAUI_SHADOW");
            if ((features & ShaderFeatures.Module) != 0) material.EnableKeyword("YAUI_MODULE");
            _uberMaterials[index] = material;
            return material;
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
                if (custom) _stencilSources[derived] = baseMaterial.ComputeCRC();
            }
            else if (custom)
            {
                // A custom material may have changed since: copy it again when its properties changed.
                var crc = baseMaterial.ComputeCRC();
                if (_stencilSources.TryGetValue(derived, out var copied) && copied == crc) return derived;

                derived.CopyPropertiesFromMaterial(baseMaterial);
                _stencilSources[derived] = crc;
            }
            else
            {
                // The stencil state of a derived uber material is set when it is created.
                return derived;
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
            foreach (var material in _uberMaterials) DestroyMaterial(material);
            DestroyMaterial(_maskMaterial);
            DestroyMaterial(_pixelClipMaskMaterial);
            DestroyMaterial(_blurMaterial);
            foreach (var derived in _stencilMaterials.Values) DestroyMaterial(derived);

            _stencilMaterials.Clear();
            _stencilSources.Clear();
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
                    if (panel.Panel.RenderMode != PanelRenderMode.Overlay) continue;

                    panel.FlushOrder();
                    if (!panel.HasDraws || panel.OrderBuffer == null) continue;

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

        /// <summary>
        /// Draws the camera panels of the rendering camera whose event is this pass's, into the camera's target. The
        /// view and projection of the camera are restored afterwards for the passes that follow.
        /// </summary>
        private sealed class CameraRenderPass : ScriptableRenderPass
        {
            private readonly PanelRenderer _renderer;
            private readonly List<PanelState> _panels = new();
            private readonly List<DrawItem> _draws = new();
            private readonly BlockPool _blocks = new();
            private readonly ScreenDrawCache _cache = new();

            // ScriptableRenderPass.profilingSampler returns null in players without instrumentation, so the pass keeps
            // its own sampler for the name of its render graph passes.
            private readonly ProfilingSampler _sampler;

            public CameraRenderPass(PanelRenderer renderer, RenderPassEvent renderPassEvent)
            {
                _renderer = renderer;
                this.renderPassEvent = renderPassEvent;
                _sampler = new ProfilingSampler("Yaui Camera " + renderPassEvent);
                profilingSampler = _sampler;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                var camera = cameraData.camera;

                _panels.Clear();
                foreach (var panel in YauiSystem.AllPanels)
                {
                    if (panel.Panel.RenderMode != PanelRenderMode.Camera || panel.Panel.WorldCamera != camera ||
                        panel.Panel.RenderPassEvent != renderPassEvent) continue;

                    panel.FlushOrder();
                    if (panel.Visible && panel.OrderBuffer != null) _panels.Add(panel);
                }

                if (_panels.Count == 0) return;

                _panels.Sort(SortOrderComparer.Instance);
                bool needsStencil;
                using (PrepareOverlayMarker.Auto())
                    needsStencil = _renderer.PrepareScreenDraws(_panels, camera, _draws, _blocks, _cache);

                var readable = !resourceData.isActiveTargetBackBuffer;
                var size = new Vector2Int(camera.pixelWidth, camera.pixelHeight);
                var samples = MSAASamples.None;
                if (readable)
                {
                    var colorDesc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
                    size = new Vector2Int(colorDesc.width, colorDesc.height);
                    samples = colorDesc.msaaSamples;
                }

                // Masks use a transient, cleared stencil: the camera's depth buffer may hold stencil values of its own
                // (the 2D renderer's light and sprite masks), and after post-processing it may not match the color
                // target.
                var depth = TextureHandle.nullHandle;
                if (needsStencil)
                {
                    var desc = new TextureDesc(size.x, size.y)
                    {
                        name = "Yaui Stencil",
                        format = SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil),
                        msaaSamples = samples,
                        clearBuffer = true
                    };
                    depth = renderGraph.CreateTexture(desc);
                }

                _renderer.RecordScreen(renderGraph, _sampler, _draws, resourceData.activeColorTexture, depth,
                    false, readable, size, (cameraData.GetViewMatrix(), cameraData.GetProjectionMatrix()));
            }
        }

        private sealed class OverlayRenderPass : ScriptableRenderPass
        {
            private readonly PanelRenderer _renderer;

            private static readonly ProfilingSampler Sampler = new("Yaui Overlay");

            public OverlayRenderPass(PanelRenderer renderer)
            {
                this._renderer = renderer;
                renderPassEvent = RenderPassEvent.AfterRendering + 10;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                _renderer.PrepareOverlayDraws(cameraData.camera);

                // The back buffer has no descriptor: it has the camera's pixel size.
                var width = cameraData.camera.pixelWidth;
                var height = cameraData.camera.pixelHeight;
                var samples = MSAASamples.None;
                var readable = !resourceData.isActiveTargetBackBuffer;
                if (readable)
                {
                    var colorDesc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
                    width = colorDesc.width;
                    height = colorDesc.height;
                    samples = colorDesc.msaaSamples;
                }

                var depth = TextureHandle.nullHandle;
                if (_renderer._overlayNeedsStencil)
                {
                    // The overlay draws after post-processing, where there is no depth-stencil buffer: a transient,
                    // cleared one for the masks.
                    var desc = new TextureDesc(width, height)
                    {
                        name = "Yaui Stencil",
                        format = SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil),
                        msaaSamples = samples,
                        clearBuffer = true
                    };
                    depth = renderGraph.CreateTexture(desc);
                }

                _renderer.RecordScreen(renderGraph, Sampler, _renderer._draws, resourceData.activeColorTexture, depth,
                    true, readable, new Vector2Int(width, height), null);
            }
        }
    }
}