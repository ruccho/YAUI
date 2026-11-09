Shader "Yaui/Backdrop Blur"
{
    // A material for YauiElement.Material that shows what was drawn behind the element, blurred, inside the shape of
    // the element and multiplied by its color (backdrop-filter: blur()). The "YauiBackdrop" tag makes the screen
    // passes (overlay and camera panels) split before the draw, copy and blur the target, and bind the result as
    // _YauiBackdropTexture. Elements next to each other with the same material share one copy. World space panels
    // have no backdrop and draw the color only.
    Properties
    {
        // The standard deviation of the blur in pixels of the target.
        _YauiBlur ("Blur (pixels)", Float) = 8

        [HideInInspector] _YauiStencilRef ("Stencil Ref", Float) = 0
        [HideInInspector] _YauiStencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _YauiStencilPass ("Stencil Pass", Float) = 0
    }

    HLSLINCLUDE
    #pragma target 4.5
    #define YAUI_PIXEL_CLIP
    #include "Packages/com.ruccho.yaui/Runtime/Shaders/Yaui.hlsl"

    TEXTURE2D(_YauiBackdropTexture);
    SAMPLER(sampler_YauiBackdropTexture);
    // xy: one pixel of the target in UV.
    float4 _YauiBackdropParams;

    half4 FragBackdropOverlay(Varyings i) : SV_Target
    {
        half4 shape = FragImpl(i, false);
        float2 uv = i.positionCS.xy * _YauiBackdropParams.xy;
        half3 backdrop = SAMPLE_TEXTURE2D(_YauiBackdropTexture, sampler_YauiBackdropTexture, uv).rgb;
        // shape is premultiplied: rgb = color * coverage.
        return half4(backdrop * shape.rgb, shape.a);
    }

    half4 FragBackdropWorld(Varyings i) : SV_Target { return FragImpl(i, true); }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline"
            "YauiBackdrop" = "True"
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

        Pass
        {
            Name "Overlay"
            Tags { "LightMode" = "YauiOverlay" }
            ZTest Always

            HLSLPROGRAM
            #pragma vertex VertOverlay
            #pragma fragment FragBackdropOverlay
            ENDHLSL
        }

        Pass
        {
            Name "World"
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex VertWorld
            #pragma fragment FragBackdropWorld
            ENDHLSL
        }
    }
}
