Shader "Yaui/Showcase/Liquid"
{
    // A custom shader for YauiElement.Material: the fill of a gauge, shaded like a liquid on top of the uber shader's
    // result (FragImpl) - a gloss, flowing stripes and a wavy, glowing leading edge.
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

    half4 Liquid(Varyings i, half4 c)
    {
        // local: position relative to the shape center, and its half size (canvas units).
        float2 p = i.local.xy;
        float2 h = max(i.local.zw, 1e-4);
        float t = _Time.y;

        // The leading edge waves.
        float edge = h.x - 3.0 + sin(p.y * 0.45 + t * 7.0) * 2.0 + sin(p.y * 0.9 - t * 4.0);
        float cut = saturate(edge - p.x + 0.5);

        float v = p.y / h.y;
        float3 rgb = c.rgb * (1.0 - 0.3 * v);
        float gloss = (1.0 - smoothstep(0.0, 0.55, abs(v + 0.45))) * 0.3;
        float s = frac((p.x + p.y * 0.8) / 30.0 - t * 0.8);
        float stripe = smoothstep(0.0, 0.5, s) * (1.0 - smoothstep(0.5, 1.0, s)) * 0.16;
        float lead = (1.0 - smoothstep(0.0, 16.0, edge - p.x)) * 0.55;
        rgb += (gloss + stripe + lead) * c.a;
        return half4(rgb * cut, c.a * cut);
    }

    half4 FragLiquidOverlay(Varyings i) : SV_Target { return Liquid(i, FragImpl(i, false)); }
    half4 FragLiquidWorld(Varyings i) : SV_Target { return Liquid(i, FragImpl(i, true)); }
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
            #pragma fragment FragLiquidOverlay
            ENDHLSL
        }

        Pass
        {
            Name "World"
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex VertWorld
            #pragma fragment FragLiquidWorld
            ENDHLSL
        }
    }
}
