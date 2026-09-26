using System.Collections.Generic;
using UnityEngine;

namespace Yaui
{
    /// <summary>Makes its toggles exclusive: switching one on switches the others off (radio buttons).</summary>
    [AddComponentMenu("YAUI/Toggle Group")]
    [DisallowMultipleComponent]
    public class YauiToggleGroup : MonoBehaviour
    {
        /// <summary>Whether clicking the toggle that is on switches it off, leaving none on.</summary>
        [SerializeField] private bool allowSwitchOff;

        private readonly List<YauiToggle> _toggles = new();

        public bool AllowSwitchOff
        {
            get => allowSwitchOff;
            set => allowSwitchOff = value;
        }

        /// <summary>The enabled toggles of the group.</summary>
        public IReadOnlyList<YauiToggle> Toggles => _toggles;

        /// <summary>The first toggle that is on, or null.</summary>
        public YauiToggle ActiveToggle
        {
            get
            {
                foreach (var toggle in _toggles)
                    if (toggle.IsOn)
                        return toggle;

                return null;
            }
        }

        public bool AnyTogglesOn()
        {
            return ActiveToggle != null;
        }

        /// <summary>Switches every toggle off, even if switching off is not allowed.</summary>
        public void SetAllTogglesOff(bool notify = true)
        {
            var allow = allowSwitchOff;
            allowSwitchOff = true;
            foreach (var toggle in _toggles.ToArray())
                if (notify)
                    toggle.IsOn = false;
                else
                    toggle.SetIsOnWithoutNotify(false);

            allowSwitchOff = allow;
        }

        internal void Register(YauiToggle toggle)
        {
            if (!_toggles.Contains(toggle)) _toggles.Add(toggle);
        }

        internal void Unregister(YauiToggle toggle)
        {
            _toggles.Remove(toggle);
        }

        internal bool IsOnlyOn(YauiToggle toggle)
        {
            foreach (var other in _toggles)
                if (other != toggle && other.IsOn)
                    return false;

            return true;
        }

        /// <summary>A toggle switched on: the others switch off.</summary>
        internal void NotifyOn(YauiToggle toggle)
        {
            var allow = allowSwitchOff;
            allowSwitchOff = true;
            foreach (var other in _toggles.ToArray())
                if (other != toggle && other.IsOn)
                    other.IsOn = false;

            allowSwitchOff = allow;
        }
    }
}