using UnityEngine;

namespace Yaui
{
    /// <summary>
    /// A shader module: HLSL that shades some primitives inside the uber shader, so that they draw with the boxes,
    /// images and texts around them instead of splitting the draws with a material of their own. Packages provide
    /// modules; a project lists the ones it uses in <see cref="YauiShaderSettings"/> and generates an uber shader with
    /// them.
    /// </summary>
    /// <remarks>
    /// The include defines <see cref="FunctionName"/> as <c>half4 Name(Varyings i, bool world)</c>, which returns the
    /// premultiplied color of the pixel (the clip coverage is applied after it). It includes Yaui.hlsl's types through
    /// the generated shader, which includes Yaui.hlsl first. Primitives choose the module with
    /// <see cref="YauiPrimitive.WithModule"/>; they read the varyings of their own shader features (the UV for images,
    /// the border color for borders).
    /// </remarks>
    [CreateAssetMenu(menuName = "YAUI/Shader Module", fileName = "YauiShaderModule")]
    public sealed class YauiShaderModule : ScriptableObject
    {
        [Tooltip("Identifies the module for the code that uses it (YauiShaderModules.IdOf).")]
        [SerializeField] private string key = "";

        [Tooltip("The HLSL file of the module, as an include path (Packages/... or Assets/...).")]
        [SerializeField] private string includePath = "";

        [Tooltip("The fragment function of the module: half4 Name(Varyings i, bool world).")]
        [SerializeField] private string functionName = "";

        public string Key => key;
        public string IncludePath => includePath;
        public string FunctionName => functionName;
    }
}
