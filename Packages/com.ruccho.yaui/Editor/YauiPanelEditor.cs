using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Yaui.Editor
{
    /// <summary>
    /// The inspector of <see cref="YauiPanel"/>: the default fields, and for camera panels, the event the camera's
    /// renderer draws the panel at, or why it does not draw it.
    /// </summary>
    [CustomEditor(typeof(YauiPanel))]
    [CanEditMultipleObjects]
    internal sealed class YauiPanelEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (targets.Length != 1) return;

            var panel = (YauiPanel)target;
            if (panel.RenderMode != PanelRenderMode.Camera) return;

            var message = Describe(panel, out var type);
            EditorGUILayout.HelpBox(message, type);
        }

        private static string Describe(YauiPanel panel, out MessageType type)
        {
            type = MessageType.Warning;
            var camera = panel.WorldCamera;
            if (camera == null) return "Set the camera (World Camera) that draws the panel.";

            var point = panel.InjectionPoint;
            if (point == null) return "Set the injection point where the camera draws the panel.";

            var data = RendererDataOf(camera);
            if (data == null) return $"The renderer of {camera.name} was not found in the active URP asset.";

            foreach (var feature in data.rendererFeatures)
            {
                if (feature is not YauiRendererFeature yaui || !yaui.isActive) continue;
                if (!yaui.TryGetRenderPassEvent(point, out var renderPassEvent)) continue;

                type = MessageType.Info;
                var is2D = data.GetType().Name == "Renderer2DData"; // URP 2D is not referenced
                var description = YauiRenderPassEventDrawer.Describe((int)renderPassEvent, is2D);
                return $"Drawn by {data.name} at {(int)renderPassEvent} ({description}).";
            }

            return $"{data.name}, the renderer of {camera.name}, has no active YauiRendererFeature that lists " +
                   $"{point.name}. The panel is not drawn.";
        }

        /// <summary>The renderer data that <paramref name="camera"/> renders with, in the active URP asset.</summary>
        private static ScriptableRendererData RendererDataOf(Camera camera)
        {
            if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset asset) return null;

            var pipeline = new SerializedObject(asset);
            var list = pipeline.FindProperty("m_RendererDataList");
            var index = -1;
            if (camera.TryGetComponent(out UniversalAdditionalCameraData cameraData))
                index = new SerializedObject(cameraData).FindProperty("m_RendererIndex").intValue;
            if (index < 0) index = pipeline.FindProperty("m_DefaultRendererIndex").intValue;
            if (list == null || index < 0 || index >= list.arraySize) return null;

            return list.GetArrayElementAtIndex(index).objectReferenceValue as ScriptableRendererData;
        }
    }
}
