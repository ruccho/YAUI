Shader "Yaui/Showcase/Gradient"
{
    // A custom shader for YauiElement.Material: a vertical gradient from the element's background color (top) to
    // its border color (bottom). The colors are per element, so one material draws every gradient. The elements
    // have no border (a width of zero) and no shadow.
    Properties
    {
        // Set by the renderer inside masks (YauiMask).
        [HideInInspector] _YauiStencilRef ("Stencil Ref", Float) = 0
        [HideInInspector] _YauiStencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _YauiStencilPass ("Stencil Pass", Float) = 0
    }

    HLSLINCLUDE
    #pragma target 4.5
    #include "Packages/com.ruccho.yaui/Runtime/Shaders/Yaui.hlsl"

    half4 Gradient(Varyings i, half4 c)
    {
        // local: position relative to the shape center, and its half size. color and borderColor: straight alpha.
        float t = saturate(i.local.y / max(i.local.w, 1e-4) * 0.5 + 0.5);
        float coverage = saturate(c.a / max(i.color.a, 1e-4));
        float4 g = lerp(i.color, i.borderColor, t);
        return half4(g.rgb * g.a, g.a) * coverage;
    }

    half4 FragGradientOverlay(Varyings i) : SV_Target { return Gradient(i, FragImpl(i, false)); }
    half4 FragGradientWorld(Varyings i) : SV_Target { return Gradient(i, FragImpl(i, true)); }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
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
            #pragma fragment FragGradientOverlay
            ENDHLSL
        }

        Pass
        {
            Name "World"
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex VertWorld
            #pragma fragment FragGradientWorld
            ENDHLSL
        }
    }
}
