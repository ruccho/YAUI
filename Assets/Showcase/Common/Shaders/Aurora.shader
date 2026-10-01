Shader "Yaui/Showcase/Aurora"
{
    // A custom shader for YauiElement.Material: a slowly flowing backdrop of two colors over a dark base, for a
    // screen without a scene behind it. The element's box is only the mask.
    Properties
    {
        _Base ("Base", Color) = (0.03, 0.02, 0.055, 1)
        _ColorA ("Upper glow", Color) = (0.30, 0.10, 0.45, 1)
        _ColorB ("Lower glow", Color) = (0.04, 0.22, 0.32, 1)
        _Scale ("Canvas height", Float) = 1080

        // Set by the renderer inside masks (YauiMask).
        [HideInInspector] _YauiStencilRef ("Stencil Ref", Float) = 0
        [HideInInspector] _YauiStencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _YauiStencilPass ("Stencil Pass", Float) = 0
    }

    HLSLINCLUDE
    #pragma target 4.5
    // The backdrop is a plain box: nothing else is compiled in.
    #define YAUI_FEATURES 0
    #include "Packages/com.ruccho.yaui/Runtime/Shaders/Yaui.hlsl"

    float4 _Base;
    float4 _ColorA;
    float4 _ColorB;
    float _Scale;

    half4 Aurora(Varyings i, half4 c)
    {
        // canvasMisc.xy: the canvas position.
        float2 uv = i.canvasMisc.xy / _Scale;
        float t = _Time.y * 0.07;
        float a = sin(uv.x * 2.1 + t * 3.0 + sin(uv.y * 1.7 - t * 2.0) * 1.5) * 0.5 + 0.5;
        float b = sin(uv.x * 1.3 - t * 2.2 + uv.y * 2.4 + sin(uv.x * 3.0 + t) * 0.8) * 0.5 + 0.5;
        float3 rgb = _Base.rgb;
        rgb += _ColorA.rgb * a * a * 0.5 * (1.0 - uv.y * 0.6);
        rgb += _ColorB.rgb * b * b * 0.4 * uv.y;
        // Darker toward the corners.
        float2 q = i.local.xy / max(i.local.zw, 1e-4);
        rgb *= 1.0 - 0.45 * dot(q, q) * 0.5;
        return half4(rgb * c.a, c.a);
    }

    half4 FragAuroraOverlay(Varyings i) : SV_Target { return Aurora(i, FragImpl(i, false)); }
    half4 FragAuroraWorld(Varyings i) : SV_Target { return Aurora(i, FragImpl(i, true)); }
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
            #pragma fragment FragAuroraOverlay
            ENDHLSL
        }

        Pass
        {
            Name "World"
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex VertWorld
            #pragma fragment FragAuroraWorld
            ENDHLSL
        }
    }
}
