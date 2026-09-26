using System.Collections.Generic;
using UnityEngine;

namespace Yaui
{
    /// <summary>
    /// Draws the ParticleSystems on this GameObject and its descendants (up to other elements) in the draw order of
    /// the element, like ParticleEffectForUGUI does for uGUI. The particles are baked into meshes every frame; their
    /// renderers are disabled while this is enabled.
    /// </summary>
    /// <remarks>
    /// The particle systems simulate in local space, centered on the element's box: one unit of the system is
    /// <see cref="Scale"/> canvas units, with Y up. Their materials use the Yaui/Particle shader to follow the
    /// element's opacity, clips and masks; other shaders follow its transform only. Transforms of the systems below
    /// this GameObject place them relative to it. World simulation space is not supported (YAUI ignores Transforms).
    /// </remarks>
    [ExecuteAlways]
    [AddComponentMenu("YAUI/Particle")]
    public sealed class YauiParticle : YauiCustomDraw
    {
        /// <summary>Canvas units per unit of the particle systems.</summary>
        [SerializeField] private float scale = 1f;

        private readonly List<ParticleSystem> systems = new();
        private readonly List<ParticleSystemRenderer> renderers = new();
        private readonly List<Mesh> meshes = new();
        private readonly List<Mesh> trailMeshes = new();

        // Renderers this component disabled, to enable again.
        private readonly List<ParticleSystemRenderer> disabled = new();
        private bool systemsDirty = true;

        private static Camera bakeCamera;

        public float Scale
        {
            get => scale;
            set => scale = value;
        }

        /// <summary>Finds the particle systems again, after they were added or removed below this GameObject.</summary>
        public void Refresh()
        {
            systemsDirty = true;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            FindSystems();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            RestoreRenderers();
        }

        private void OnDestroy()
        {
            foreach (var mesh in meshes) DestroyObject(mesh);
            foreach (var mesh in trailMeshes) DestroyObject(mesh);

            meshes.Clear();
            trailMeshes.Clear();
        }

        private void OnTransformChildrenChanged()
        {
            systemsDirty = true;
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            systemsDirty = true;
        }

        protected override void OnCollectDraws(YauiDrawList draws)
        {
            if (systemsDirty) FindSystems();

            var camera = BakeCamera;
            var size = Element.LayoutRect.size;
            // Y up in the systems, Y down on the canvas.
            var toBox = Matrix4x4.Translate(new Vector3(size.x * 0.5f, size.y * 0.5f, 0f)) *
                        Matrix4x4.Scale(new Vector3(scale, -scale, scale)) * transform.worldToLocalMatrix;
            for (var i = 0; i < systems.Count; i++)
            {
                var system = systems[i];
                var renderer = renderers[i];
                if (system == null || renderer == null || !system.gameObject.activeInHierarchy) continue;

                // The meshes are in the local space of each system.
                var local = toBox * system.transform.localToWorldMatrix;
                if (system.particleCount > 0 && renderer.sharedMaterial != null)
                {
                    var mesh = MeshAt(meshes, i);
                    renderer.BakeMesh(mesh, camera, ParticleSystemBakeMeshOptions.Default);
                    draws.DrawMesh(mesh, renderer.sharedMaterial, local);
                }

                if (system.trails.enabled && renderer.trailMaterial != null)
                {
                    var mesh = MeshAt(trailMeshes, i);
                    renderer.BakeTrailsMesh(mesh, camera, ParticleSystemBakeMeshOptions.Default);
                    if (mesh.vertexCount > 0) draws.DrawMesh(mesh, renderer.trailMaterial, local);
                }
            }
        }

        private void FindSystems()
        {
            systemsDirty = false;
            RestoreRenderers();
            systems.Clear();
            renderers.Clear();
            FindIn(transform);
            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled) continue;

                renderer.enabled = false;
                disabled.Add(renderer);
            }

            // Systems that are not rendered would stop simulating.
            if (Application.isPlaying)
                foreach (var system in systems)
                {
                    var main = system.main;
                    main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                }
        }

        private void FindIn(Transform t)
        {
            if (t.TryGetComponent<ParticleSystem>(out var system))
            {
                systems.Add(system);
                renderers.Add(system.GetComponent<ParticleSystemRenderer>());
            }

            for (var i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                // Other elements draw their own.
                if (!child.TryGetComponent<YauiElement>(out _)) FindIn(child);
            }
        }

        private void RestoreRenderers()
        {
            foreach (var renderer in disabled)
                if (renderer != null)
                    renderer.enabled = true;

            disabled.Clear();
        }

        private static Mesh MeshAt(List<Mesh> list, int index)
        {
            while (list.Count <= index) list.Add(null);

            if (list[index] == null)
            {
                list[index] = new Mesh { hideFlags = HideFlags.HideAndDontSave, name = "YauiParticle" };
                list[index].MarkDynamic();
            }

            return list[index];
        }

        /// <summary>
        /// An orthographic camera looking along +Z, so that billboards face the canvas. Large, since the renderer's
        /// Max Particle Size is a fraction of the camera's view.
        /// </summary>
        private static Camera BakeCamera
        {
            get
            {
                if (bakeCamera != null) return bakeCamera;

                var go = new GameObject("YauiParticle Bake Camera") { hideFlags = HideFlags.HideAndDontSave };
                bakeCamera = go.AddComponent<Camera>();
                bakeCamera.enabled = false;
                bakeCamera.orthographic = true;
                bakeCamera.orthographicSize = 1e5f;
                bakeCamera.nearClipPlane = -1e5f;
                bakeCamera.farClipPlane = 1e5f;
                return bakeCamera;
            }
        }

        private static void DestroyObject(Object o)
        {
            if (o == null) return;

            if (Application.isPlaying)
                Destroy(o);
            else
                DestroyImmediate(o);
        }
    }
}