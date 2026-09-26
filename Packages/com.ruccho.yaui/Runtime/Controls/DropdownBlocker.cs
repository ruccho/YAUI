using System;
using UnityEngine.EventSystems;

namespace Yaui
{
    /// <summary>
    /// Covers the panel behind an open dropdown list: it draws nothing, but receives the presses outside the
    /// list, which close it.
    /// </summary>
    internal sealed class DropdownBlocker : YauiElement, IPointerClickHandler
    {
        [NonSerialized] public YauiDropdown Dropdown;

        protected override bool HasVisibleContent => true;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Dropdown != null) Dropdown.Hide();
        }
    }
}