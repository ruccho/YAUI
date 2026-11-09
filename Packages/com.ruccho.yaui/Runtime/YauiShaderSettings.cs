using System.Collections.Generic;
using UnityEngine;

namespace Yaui
{
    /// <summary>
    /// The shader modules a project uses (<see cref="YauiShaderModule"/>) and the uber shader generated with them.
    /// Load it from a <c>Resources</c> folder as <c>YauiShaderSettings</c>; without it, or without a generated shader,
    /// YAUI uses its own uber shader and no module.
    /// </summary>
    /// <remarks>
    /// Generate the shader with the button of the inspector after changing the modules. A module's id is its position
    /// in <see cref="Modules"/> plus one, so the generated shader must match the list.
    /// </remarks>
    [CreateAssetMenu(menuName = "YAUI/Shader Settings", fileName = ResourceName)]
    public sealed class YauiShaderSettings : ScriptableObject
    {
        public const string ResourceName = "YauiShaderSettings";

        [SerializeField] private List<YauiShaderModule> modules = new();

        [Tooltip("The uber shader generated with the modules. Generated next to this asset.")]
        [SerializeField] private Shader uberShader;

        public IReadOnlyList<YauiShaderModule> Modules => modules;

        public Shader UberShader
        {
            get => uberShader;
            set => uberShader = value;
        }
    }

    /// <summary>The shader modules of the project (<see cref="YauiShaderSettings"/>) at runtime.</summary>
    public static class YauiShaderModules
    {
        private static bool s_Loaded;
        private static YauiShaderSettings s_Settings;

        private static YauiShaderSettings Settings
        {
            get
            {
                if (s_Loaded) return s_Settings;

                s_Loaded = true;
                s_Settings = Resources.Load<YauiShaderSettings>(YauiShaderSettings.ResourceName);
                return s_Settings;
            }
        }

        /// <summary>The generated uber shader, or null to use YAUI's own.</summary>
        public static Shader UberShader => Settings != null ? Settings.UberShader : null;

        /// <summary>
        /// The id of the module with <paramref name="key"/> (1..31) for <see cref="YauiPrimitive.WithModule"/>, or 0 if
        /// the project does not use it or has no generated uber shader. Code that gets 0 draws without the module, for
        /// example with a material of its own.
        /// </summary>
        public static int IdOf(string key)
        {
            var settings = Settings;
            if (settings == null || settings.UberShader == null) return 0;

            var modules = settings.Modules;
            for (var i = 0; i < modules.Count && i < YauiPrimitive.MaxModuleId; i++)
                if (modules[i] != null && modules[i].Key == key)
                    return i + 1;

            return 0;
        }

#if UNITY_EDITOR
        /// <summary>Forgets the loaded settings (the editor changed them).</summary>
        public static void Reload()
        {
            s_Loaded = false;
            s_Settings = null;
        }
#endif
    }
}
