Shader "Hidden/Yaui/Mask"
{
    // The shapes of masks (YauiMask) into the stencil: the coverage of a box or the alpha of an image, without color.
    // A separate shader so that its world pass is the one URP draws for a mask draw.
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
    // A mask's shape has no border or shadow (FragMaskImpl).
    #define YAUI_FEATURES (YAUI_FEATURE_TEXT + YAUI_FEATURE_IMAGE + YAUI_FEATURE_RADIAL_FILL)
    #include "Packages/com.ruccho.yaui/Runtime/Shaders/Yaui.hlsl"
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline"
        }
        ZWrite Off
        Cull Off
        ColorMask 0
        Stencil
        {
            Ref [_YauiStencilRef]
            Comp [_YauiStencilComp]
            Pass [_YauiStencilPass]
        }

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
            #pragma fragment FragMaskOverlay
            ENDHLSL
        }

        Pass
        {
            Name "World"
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex VertWorld
            #pragma fragment FragMaskWorld
            ENDHLSL
        }
    }
}