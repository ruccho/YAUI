Shader "Yaui/Particle"
{
    // Particles of YauiParticle (or any mesh of a YauiCustomDraw): textured and vertex colored, following the node's
    // transform, opacity, tint, clips and masks (see Yaui.hlsl). Premultiplied alpha, like the uber shader.
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        // 0: alpha blending, 1: additive (premultiplied colors with zero alpha add up).
        _Additive ("Additive", Range(0, 1)) = 0

        // Set by the renderer inside masks (YauiMask).
        [HideInInspector] _YauiStencilRef ("Stencil Ref", Float) = 0
        [HideInInspector] _YauiStencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _YauiStencilPass ("Stencil Pass", Float) = 0
    }

    HLSLINCLUDE
    #pragma target 4.5
    #include "Packages/com.ruccho.yaui/Runtime/Shaders/Yaui.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

    TEXTURE2D(_MainTex);
    SAMPLER(sampler_MainTex);
    float4 _MainTex_ST;
    float4 _Color;
    float _Additive;

    struct Attributes
    {
        float3 positionOS : POSITION;
        float4 color : COLOR;
        float2 uv : TEXCOORD0;
    };

    struct ParticleVaryings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        float4 color : COLOR;
        float2 canvas : TEXCOORD1;
        nointerpolation float4 clipRect : TEXCOORD2;
        nointerpolation float4 clipRounded : TEXCOORD3;
        nointerpolation float4 clipRadii : TEXCOORD4;
    };

    ParticleVaryings Vert(Attributes a, bool world)
    {
        YauiMeshVertexData d = YauiMeshVertex(a.positionOS, world);
        float4 color = a.color;
        #if !defined(UNITY_COLORSPACE_GAMMA)
        // Vertex colors are sRGB, like the colors of the element styles.
        color.rgb = SRGBToLinear(color.rgb);
        #endif

        ParticleVaryings o;
        o.positionCS = d.positionCS;
        o.uv = TRANSFORM_TEX(a.uv, _MainTex);
        o.color = color * _Color * d.color;
        o.canvas = d.canvas;
        o.clipRect = d.clipRect;
        o.clipRounded = d.clipRounded;
        o.clipRadii = d.clipRadii;
        return o;
    }

    half4 Frag(ParticleVaryings i) : SV_Target
    {
        float4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color;
        c.a *= YauiClipCoverage(i.canvas, i.clipRect, i.clipRounded, i.clipRadii);
        return half4(c.rgb * c.a, c.a * (1.0 - _Additive));
    }

    ParticleVaryings VertOverlay(Attributes a) { return Vert(a, false); }
    ParticleVaryings VertWorld(Attributes a) { return Vert(a, true); }
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
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "World"
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex VertWorld
            #pragma fragment Frag
            ENDHLSL
        }
    }
}