using UnityEngine;

namespace Yaui.Text
{
    /// <summary>
    /// Data that text generation needs in players: the ICU data for line breaking and word boundaries (a built-in
    /// resource of the editor, like the one UI Toolkit's PanelSettings reference). Kept in Resources so that
    /// builds include it; without it ATG falls back to minimal segmentation (e.g. no Japanese line breaking rules).
    /// </summary>
    internal sealed class YauiTextData : ScriptableObject
    {
        public const string ResourcePath = "Yaui/TextData";

        /// <summary>The name of the ICU data among the editor's built-in resources.</summary>
        public const string IcuDataName = "icudt73l";

        [SerializeField] private TextAsset icuData;

        public TextAsset IcuData
        {
            get => icuData;
            set => icuData = value;
        }
    }
}