using UnityEngine;

namespace Yaui
{
    /// <summary>
    /// Masks the descendants of this element to its shape: the box with its corner radii, or, on a
    /// <see cref="YauiImage"/> with a sprite, the sprite's alpha. Masks nest to any depth (up to 255) through the
    /// stencil buffer, at the cost of splitting the draw call at the mask's boundaries.
    /// </summary>
    /// <remarks>
    /// For a single level of rectangle or rounded rectangle clipping, <see cref="YauiElement.ClipChildren"/> is
    /// cheaper: it does not split the draw call. Hit tests clip to the mask's bounding rectangle.
    /// Custom materials inside a mask must declare the stencil properties and Stencil block of Uber.shader.
    /// </remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(YauiElement))]
    [AddComponentMenu("YAUI/Mask")]
    public sealed class YauiMask : MonoBehaviour
    {
        /// <summary>Whether the element itself (its box, image or text) is drawn, or only used as the mask.</summary>
        [SerializeField] private bool showMaskGraphic = true;

        private YauiElement _element;

        public bool ShowMaskGraphic
        {
            get => showMaskGraphic;
            set
            {
                showMaskGraphic = value;
                Element.OnMaskChanged();
            }
        }

        private YauiElement Element => _element != null ? _element : _element = GetComponent<YauiElement>();

        private void OnEnable()
        {
            Element.SetMask(this);
        }

        private void OnDisable()
        {
            if (_element != null) _element.SetMask(null);
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled) Element.OnMaskChanged();
        }
    }
}