using UnityEngine;

namespace Yaui
{
    /// <summary>
    /// A named point among the render passes of a camera where camera panels are drawn
    /// (<see cref="YauiPanel.InjectionPoint"/>). A <see cref="YauiRendererFeature"/> gives it an event in each
    /// renderer, so panels do not depend on the event values of URP, and the same point can have a different event in
    /// another renderer.
    /// </summary>
    [CreateAssetMenu(menuName = "YAUI/Injection Point", fileName = "YauiInjectionPoint")]
    public sealed class YauiInjectionPoint : ScriptableObject
    {
        [Tooltip("What the point is for, for the people who set up the renderers.")]
        [TextArea] [SerializeField] private string description = "";

        public string Description => description;
    }
}
