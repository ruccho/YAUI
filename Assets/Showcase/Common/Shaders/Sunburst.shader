Shader "Yaui/Showcase/Sunburst"
{
    // A custom shader for YauiElement.Material: rays turning around the center of the element, between two colors
    // over a vertical gradient - a backdrop, or with _Fade and _Additive, a glow behind something. The element's box
    // is only the mask.
    Properties
    {
        _ColorA ("Top", Color) = (0.35, 0.7, 1, 1)
        _ColorB ("Bottom", Color) = (1, 0.6, 0.85, 1)
        _RayColor ("Rays", Color) = (1, 1, 1, 0.18)
        _Rays ("Ray count", Float) = 14
        _Speed ("Speed", Float) = 0.12
        _Center ("Center (0..1, Y down)", Vector) = (0.5, 0.5, 0, 0)
        _Fade ("Radial fade", Range(0, 1)) = 0
        _Additive ("Additive", Range(0, 1)) = 0

        // Set by the renderer inside masks (YauiMask).
        [HideInInspector] _YauiStencilRef ("Stencil Ref", Float) = 0
        [HideInInspector] _YauiStencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _YauiStencilPass ("Stencil Pass", Float) = 0
    }

    HLSLINCLUDE
    #pragma target 4.5
    // The element is a plain box: nothing else is compiled in.
    #define YAUI_FEATURES 0
    #include "Packages/com.ruccho.yaui/Runtime/Shaders/Yaui.hlsl"

    float4 _ColorA;
    float4 _ColorB;
    float4 _RayColor;
    float4 _Center;
    float _Rays;
    float _Speed;
    float _Fade;
    float _Additive;

    half4 Sunburst(Varyings i, half4 c)
    {
        // local: position relative to the shape center, and its half size (canvas units).
        float2 h = max(i.local.zw, 1e-4);
        float2 p = i.local.xy - (_Center.xy * 2.0 - 1.0) * h;
        float radius = length(p);
        float angle = atan2(p.y, p.x) + _Time.y * _Speed;
        // Antialiased by the width of a pixel along the circle.
        float wave = sin(angle * _Rays);
        float pixel = _Rays * i.canvasMisc.z / max(radius, 1.0);
        float rays = smoothstep(-pixel, pixel, wave);

        float3 rgb = lerp(_ColorA.rgb, _ColorB.rgb, saturate(i.local.y / h.y * 0.5 + 0.5));
        rgb = lerp(rgb, _RayColor.rgb, rays * _RayColor.a);
        float fade = saturate(1.0 - radius / min(h.x, h.y));
        float a = lerp(1.0, fade * fade * (0.55 + 0.45 * rays), _Fade) * c.a;
        return half4(rgb * a, a * (1.0 - _Additive));
    }

    half4 FragSunburstOverlay(Varyings i) : SV_Target { return Sunburst(i, FragImpl(i, false)); }
    half4 FragSunburstWorld(Varyings i) : SV_Target { return Sunburst(i, FragImpl(i, true)); }
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
            #pragma fragment FragSunburstOverlay
            ENDHLSL
        }

        Pass
        {
            Name "World"
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex VertWorld
            #pragma fragment FragSunburstWorld
            ENDHLSL
        }
    }
}
