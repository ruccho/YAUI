using UnityEngine;

namespace Yaui
{
    /// <summary>
    /// Shows an <see cref="int"/> field as a value of <see cref="UnityEngine.Rendering.Universal.RenderPassEvent"/>:
    /// any value can be entered, with the nearest named event and, in a 2D Renderer, where the renderer runs the pass.
    /// </summary>
    public sealed class YauiRenderPassEventAttribute : PropertyAttribute
    {
    }
}
