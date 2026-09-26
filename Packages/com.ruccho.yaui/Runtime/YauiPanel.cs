using System;
using Unity.Mathematics;
using UnityEngine;
using Yaui.Core;

namespace Yaui
{
    public enum PanelRenderMode
    {
        /// <summary>Drawn over the screen after post-processing, sized like uGUI's CanvasScaler.</summary>
        Overlay,

        /// <summary>
        /// A quad in the scene at this GameObject's Transform, sorted and depth tested with transparent objects.
        /// The canvas size is <see cref="YauiPanel.ReferenceResolution"/>.
        /// </summary>
        World
    }

    public enum PanelScaleMode
    {
        /// <summary>Scales with the screen towards the reference resolution (uGUI's Scale With Screen Size).</summary>
        ScaleWithScreenSize,

        /// <summary>A fixed number of screen pixels per canvas unit (uGUI's Constant Pixel Size).</summary>
        ConstantScale
    }

    /// <summary>
    /// The root of a YAUI tree. Draws its <see cref="YauiElement"/> and the elements below it as a screen space
    /// overlay, sized like uGUI's CanvasScaler (scale with screen size).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(YauiElement))]
    [AddComponentMenu("YAUI/Panel")]
    public sealed class YauiPanel : MonoBehaviour
    {
        [SerializeField] private PanelRenderMode renderMode;

        /// <summary>Overlay panels with a higher order are drawn on top.</summary>
        [SerializeField] private int sortOrder;

        [SerializeField] private PanelScaleMode scaleMode;

        /// <summary>Overlay, <see cref="PanelScaleMode.ConstantScale"/>: screen pixels per canvas unit.</summary>
        [SerializeField] private float constantScale = 1f;

        [SerializeField] private Vector2 referenceResolution = new(1080f, 1920f);

        /// <summary>0: match the width of the reference resolution, 1: match the height.</summary>
        [SerializeField] [Range(0f, 1f)] private float match = 0.5f;

        /// <summary>World space: world units per canvas unit.</summary>
        [SerializeField] private float worldScale = 0.001f;

        /// <summary>World space: the camera pointer events are seen through (the main camera when null).</summary>
        [SerializeField] private Camera eventCamera;

        [NonSerialized] private PanelState _state;
        [NonSerialized] private YauiElement _element;

        public PanelRenderMode RenderMode
        {
            get => renderMode;
            set
            {
                renderMode = value;
                YauiSystem.RequestUpdate();
            }
        }

        public float WorldScale
        {
            get => worldScale;
            set => worldScale = value;
        }

        public Camera EventCamera
        {
            get => eventCamera != null ? eventCamera : Camera.main;
            set => eventCamera = value;
        }

        /// <summary>
        /// Where the panel appears in the Scene view: world space panels at <see cref="CanvasToWorld"/>, overlay
        /// panels like uGUI's overlay canvases, one world unit per screen pixel with the bottom-left at the origin.
        /// </summary>
        public Matrix4x4 SceneViewCanvasToWorld
        {
            get
            {
                if (renderMode == PanelRenderMode.World) return CanvasToWorld;

                var scale = ScaleFactor;
                return Matrix4x4.Translate(new Vector3(0f, CanvasSize.y * scale, 0f)) *
                       Matrix4x4.Scale(new Vector3(scale, -scale, 1f));
            }
        }

        /// <summary>
        /// World space: canvas space (origin at the top-left, Y down) to world space. The panel is centered on the
        /// Transform in its XY plane.
        /// </summary>
        public Matrix4x4 CanvasToWorld
        {
            get
            {
                var size = CanvasSize;
                return transform.localToWorldMatrix *
                       Matrix4x4.Translate(new Vector3(-size.x * worldScale * 0.5f, size.y * worldScale * 0.5f, 0f)) *
                       Matrix4x4.Scale(new Vector3(worldScale, -worldScale, 1f));
            }
        }

        public int SortOrder
        {
            get => sortOrder;
            set => sortOrder = value;
        }

        public PanelScaleMode ScaleMode
        {
            get => scaleMode;
            set => scaleMode = value;
        }

        public float ConstantScale
        {
            get => constantScale;
            set => constantScale = value;
        }

        public Vector2 ReferenceResolution
        {
            get => referenceResolution;
            set => referenceResolution = value;
        }

        public float Match
        {
            get => match;
            set => match = value;
        }

        /// <summary>Canvas units per screen pixel is the inverse of this.</summary>
        public float ScaleFactor => _state?.ScaleFactor ?? 1f;

        /// <summary>Size of the panel in canvas units.</summary>
        public Vector2 CanvasSize => _state != null ? (Vector2)_state.CanvasSize : Vector2.zero;

        internal PanelState State => _state ??= YauiSystem.CreatePanel(this);

        /// <summary>The state if the panel is active, without creating it.</summary>
        internal PanelState CurrentState => _state;

        /// <summary>
        /// Converts a screen position (pixels, origin at the bottom-left) to canvas space (origin at the top-left,
        /// Y down). World space panels are seen through <see cref="EventCamera"/>; false if it does not see the
        /// panel's plane there.
        /// </summary>
        public bool TryScreenToCanvas(Vector2 screenPosition, out Vector2 canvas)
        {
            var hit = TryScreenToCanvas(screenPosition, out var point, out _, out _);
            canvas = point;
            return hit;
        }

        /// <summary>Converts a point in canvas space to a screen position (pixels, origin at the bottom-left).</summary>
        public bool TryCanvasToScreen(Vector2 canvas, out Vector2 screenPosition)
        {
            screenPosition = default;
            if (_state == null) return false;

            if (renderMode == PanelRenderMode.World)
            {
                var camera = EventCamera;
                if (camera == null) return false;

                var screen =
                    camera.WorldToScreenPoint(CanvasToWorld.MultiplyPoint3x4(new Vector3(canvas.x, canvas.y, 0f)));
                screenPosition = screen;
                return screen.z > 0f;
            }

            var scale = _state.ScaleFactor;
            screenPosition = new Vector2(canvas.x * scale, _state.CanvasSize.y * scale - canvas.y * scale);
            return true;
        }

        internal bool TryScreenToCanvas(Vector2 screenPosition, out float2 canvas, out float distance,
            out Vector3 worldPosition)
        {
            canvas = default;
            distance = 0f;
            worldPosition = Vector3.zero;
            if (_state == null) return false;

            if (renderMode == PanelRenderMode.World)
            {
                var camera = EventCamera;
                if (camera == null) return false;

                var ray = camera.ScreenPointToRay(screenPosition);
                var plane = new Plane(transform.forward, transform.position);
                if (!plane.Raycast(ray, out distance)) return false;

                worldPosition = ray.GetPoint(distance);
                var local = CanvasToWorld.inverse.MultiplyPoint3x4(worldPosition);
                canvas = new float2(local.x, local.y);
                return true;
            }

            var screenHeight = _state.CanvasSize.y * _state.ScaleFactor;
            canvas = new float2(screenPosition.x, screenHeight - screenPosition.y) / _state.ScaleFactor;
            return true;
        }

        /// <summary>
        /// The topmost element that receives hits at a screen position (pixels, origin at the bottom-left), as last
        /// rendered, or null. See <see cref="YauiElement.RaycastTarget"/>.
        /// </summary>
        public YauiElement HitTest(Vector2 screenPosition)
        {
            return _state?.HitTest(screenPosition);
        }

        /// <summary>
        /// Lays out, generates texts and uploads all panels now, so that <see cref="YauiElement.LayoutRect"/> and
        /// hit tests reflect the changes made so far in this frame (like uGUI's Canvas.ForceUpdateCanvases). Costs
        /// the overlap with other work that the frame pipeline gets.
        /// </summary>
        public static void ForceUpdate()
        {
            YauiSystem.ForceUpdate();
        }

        internal YauiElement Element => _element != null ? _element : _element = GetComponent<YauiElement>();

        private void OnEnable()
        {
            _ = State;

            // Elements enabled before this panel registered nowhere, or to a previous state.
            foreach (var e in GetComponentsInChildren<YauiElement>()) e.Reregister();
        }

        private void OnDisable()
        {
            if (_state == null) return;

            var old = _state;
            _state = null;
            foreach (var e in GetComponentsInChildren<YauiElement>(true))
                if (e.IsRegisteredTo(old))
                    e.Unregister();

            YauiSystem.DestroyPanel(old);
        }

        private void OnValidate()
        {
            YauiSystem.RequestUpdate();
        }

        /// <summary>Main thread, at submission: updates the canvas size from the screen size.</summary>
        internal void UpdateCanvas(PanelState s)
        {
            if (renderMode == PanelRenderMode.World)
            {
                s.ScaleFactor = 1f;
                s.CanvasSize = math.max((float2)referenceResolution, 1f);
                return;
            }

            var screen = ScreenSize();
            if (scaleMode == PanelScaleMode.ConstantScale)
            {
                s.ScaleFactor = math.max(constantScale, 1e-4f);
                s.CanvasSize = math.max(screen, 1f) / s.ScaleFactor;
                return;
            }

            var logWidth = math.log2(math.max(screen.x, 1f) / math.max(referenceResolution.x, 1f));
            var logHeight = math.log2(math.max(screen.y, 1f) / math.max(referenceResolution.y, 1f));
            s.ScaleFactor = math.exp2(math.lerp(logWidth, logHeight, match));
            s.CanvasSize = math.max(screen, 1f) / s.ScaleFactor;
        }

        private static float2 ScreenSize()
        {
            // The camera the overlay is drawn on. Screen reports the size of whichever view is rendering in the editor.
            var target = YauiSystem.Renderer.TargetSize;
            if (target.x > 0 && target.y > 0) return new float2(target.x, target.y);

#if UNITY_EDITOR
            var size = UnityEditor.Handles.GetMainGameViewSize();
            return new float2(size.x, size.y);
#else
            return new float2(Screen.width, Screen.height);
#endif
        }
    }
}