Shader "Yaui/Showcase/Sheen"
{
    // A custom shader for YauiElement.Material: a band of light sweeping over the element's shape, added to what is
    // under it. The element's box (white, with its corner radii) is only the mask of the light.
    Properties
    {
        _SheenColor ("Color", Color) = (1, 1, 1, 0.6)
        _Speed ("Sweeps per second", Float) = 0.45
        _Width ("Band width", Range(0.05, 1)) = 0.35
        _Base ("Constant glow", Range(0, 1)) = 0.06

        // Set by the renderer inside masks (YauiMask).
        [HideInInspector] _YauiStencilRef ("Stencil Ref", Float) = 0
        [HideInInspector] _YauiStencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _YauiStencilPass ("Stencil Pass", Float) = 0
    }

    HLSLINCLUDE
    #pragma target 4.5
    #include "Packages/com.ruccho.yaui/Runtime/Shaders/Yaui.hlsl"

    float4 _SheenColor;
    float _Speed;
    float _Width;
    float _Base;

    half4 Sheen(Varyings i, half4 c)
    {
        float2 p = i.local.xy;
        float2 h = max(i.local.zw, 1e-4);
        // -1..1 along the diagonal; the band crosses it and rests outside for the rest of the period. The canvas
        // position offsets the phase, so that neighbors light up one after another.
        float d = (p.x + p.y) / (h.x + h.y);
        float phase = frac(_Time.y * _Speed - i.canvasMisc.x * 0.0012) * 5.0 - 1.5;
        float band = saturate(1.0 - abs(d - phase) / _Width);
        float light = (band * band + _Base) * _SheenColor.a * c.a;
        // Premultiplied with zero alpha: additive.
        return half4(_SheenColor.rgb * light, 0.0);
    }

    half4 FragSheenOverlay(Varyings i) : SV_Target { return Sheen(i, FragImpl(i, false)); }
    half4 FragSheenWorld(Varyings i) : SV_Target { return Sheen(i, FragImpl(i, true)); }
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
            #pragma fragment FragSheenOverlay
            ENDHLSL
        }

        Pass
        {
            Name "World"
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex VertWorld
            #pragma fragment FragSheenWorld
            ENDHLSL
        }
    }
}
