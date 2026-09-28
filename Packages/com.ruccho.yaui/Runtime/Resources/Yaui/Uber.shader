Shader "Hidden/Yaui/Uber"
{
    // The uber shader of YAUI: rounded boxes, borders and drop shadows as signed distance fields, SDF text and
    // images, on the data bound by the renderer (see Shaders/Yaui.hlsl).
    Properties
    {
        // Masks (YauiMask): set by the renderer on derived materials. Custom shaders declare the same properties and
        // Stencil block to be masked.
        [HideInInspector] _YauiStencilRef ("Stencil Ref", Float) = 0
        [HideInInspector] _YauiStencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _YauiStencilPass ("Stencil Pass", Float) = 0
    }

    HLSLINCLUDE
    #pragma target 4.5
    // Per-pixel clips (rotated quads, rounded clips), only for panels that need them.
    #pragma multi_compile_local _ YAUI_PIXEL_CLIP
    // Features, only for the draws whose primitives use them: the unused ones cost registers and varyings even when
    // no pixel takes their branches (planning/shader-variants.md).
    #pragma multi_compile_local _ YAUI_TEXT
    #pragma multi_compile_local _ YAUI_IMAGE
    #pragma multi_compile_local _ YAUI_BORDER
    #pragma multi_compile_local _ YAUI_SHADOW
    #define YAUI_FEATURE_KEYWORDS
    #include "Packages/com.ruccho.yaui/Runtime/Shaders/Yaui.hlsl"
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline"
        }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Stencil
        {
            Ref [_YauiStencilRef]
            Comp [_YauiStencilComp]
            Pass [_YauiStencilPass]
        }

        // Screen space overlay, drawn explicitly by the overlay pass.
        Pass
        {
            Name "Overlay"
            Tags
            {
                "LightMode" = "YauiOverlay"
            }
            ZTest Always

            HLSLPROGRAM
            #pragma vertex VertOverlay
            #pragma fragment FragOverlay
            ENDHLSL
        }

        // World space panels, drawn by URP in the transparent pass (Graphics.RenderPrimitivesIndexed).
        Pass
        {
            Name "World"
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex VertWorld
            #pragma fragment FragWorld
            ENDHLSL
        }
    }
}