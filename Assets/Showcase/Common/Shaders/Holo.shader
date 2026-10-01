Shader "Yaui/Showcase/Holo"
{
    // A custom shader for YauiElement.Material: a holographic frame. A ring of flowing rainbow colors along the edge
    // of the element's rounded box, and a faint band of light over its inside. The element's box (white) is only
    // the mask.
    Properties
    {
        _Width ("Ring width", Float) = 3
        _Speed ("Speed", Float) = 0.35
        _Inner ("Inner light", Range(0, 1)) = 0.16

        // Set by the renderer inside masks (YauiMask).
        [HideInInspector] _YauiStencilRef ("Stencil Ref", Float) = 0
        [HideInInspector] _YauiStencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _YauiStencilPass ("Stencil Pass", Float) = 0
    }

    HLSLINCLUDE
    #pragma target 4.5
    // The frames scroll inside a rounded clip.
    #define YAUI_PIXEL_CLIP
    #include "Packages/com.ruccho.yaui/Runtime/Shaders/Yaui.hlsl"

    float _Width;
    float _Speed;
    float _Inner;

    float3 Hue(float h)
    {
        return saturate(abs(frac(h + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
    }

    half4 Holo(Varyings i, half4 c)
    {
        // local: position relative to the shape center, and its half size (canvas units).
        float2 p = i.local.xy;
        float2 h = max(i.local.zw, 1e-4);
        float radius = min(i.radii.x, min(h.x, h.y));
        float distance = RoundedBoxDistance(p, h, radius);
        float ring = saturate(distance + _Width + 0.5);

        float t = _Time.y * _Speed;
        float angle = atan2(p.y, p.x) / 6.2831853;
        // The canvas position offsets the colors, so that neighbors differ.
        float3 color = lerp(1.0, Hue(angle + t + i.canvasMisc.x * 0.0011), 0.8);

        float d = (p.x - p.y) / (h.x + h.y);
        float phase = frac(t * 0.9 + i.canvasMisc.y * 0.0007) * 4.0 - 1.5;
        float band = saturate(1.0 - abs(d - phase) / 0.4);
        float light = band * band * _Inner * (1.0 - ring);

        // The ring covers what is under it; the light is added (premultiplied with zero alpha).
        float a = ring * c.a;
        return half4(color * (a + light * c.a), a);
    }

    half4 FragHoloOverlay(Varyings i) : SV_Target { return Holo(i, FragImpl(i, false)); }
    half4 FragHoloWorld(Varyings i) : SV_Target { return Holo(i, FragImpl(i, true)); }
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
            #pragma fragment FragHoloOverlay
            ENDHLSL
        }

        Pass
        {
            Name "World"
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex VertWorld
            #pragma fragment FragHoloWorld
            ENDHLSL
        }
    }
}
