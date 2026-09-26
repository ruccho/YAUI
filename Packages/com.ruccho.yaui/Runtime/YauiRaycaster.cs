using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Yaui
{
    /// <summary>
    /// Plugs a <see cref="YauiPanel"/> into the EventSystem (and so the Input System UI Input Module), like uGUI's
    /// GraphicRaycaster. Events go to the GameObject of the topmost hit element and bubble up the hierarchy.
    /// </summary>
    /// <remarks>
    /// Hits are tested against the state last rendered, with the boxes of the elements (transformed and clipped).
    /// All elements under the pointer are reported with their draw order as the depth, topmost first.
    /// </remarks>
    [RequireComponent(typeof(YauiPanel))]
    [AddComponentMenu("YAUI/Raycaster")]
    public sealed class YauiRaycaster : BaseRaycaster
    {
        private static readonly List<(YauiElement, int)> Hits = new();

        private YauiPanel _panel;

        /// <summary>Overlay panels are not seen through a camera; world space panels are.</summary>
        public override Camera eventCamera =>
            Panel != null && Panel.RenderMode == PanelRenderMode.World ? Panel.EventCamera : null;

        public override int sortOrderPriority =>
            Panel != null && Panel.RenderMode == PanelRenderMode.Overlay ? Panel.SortOrder : int.MinValue;

        /// <summary>Overlays are on top of everything rendered by cameras.</summary>
        public override int renderOrderPriority =>
            Panel != null && Panel.RenderMode == PanelRenderMode.Overlay ? int.MaxValue : int.MinValue;

        private YauiPanel Panel => _panel != null ? _panel : _panel = GetComponent<YauiPanel>();

        public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
        {
            var state = Panel != null ? Panel.CurrentState : null;
            if (state == null) return;

            if (!Panel.TryScreenToCanvas(eventData.position, out var canvas, out var distance,
                    out var worldPosition)) return;

            Hits.Clear();
            state.HitTestCanvasAll(canvas, Hits);
            foreach (var (element, depth) in Hits)
                resultAppendList.Add(new RaycastResult
                {
                    gameObject = element.gameObject,
                    module = this,
                    distance = distance,
                    worldPosition = worldPosition,
                    worldNormal = Panel.RenderMode == PanelRenderMode.World ? -Panel.transform.forward : Vector3.zero,
                    screenPosition = eventData.position,
                    index = resultAppendList.Count,
                    depth = depth,
                    sortingOrder = Panel.RenderMode == PanelRenderMode.Overlay ? Panel.SortOrder : 0
                });
        }
    }
}