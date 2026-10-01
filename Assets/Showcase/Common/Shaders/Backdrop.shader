Shader "Yaui/Showcase/Backdrop"
{
    // The 3D scene behind the showcase UI (not a YAUI shader): an unlit color with a rim light and a hit flash, or
    // with _Grid, a glowing grid on the ground. Fades to the background color with the distance.
    Properties
    {
        _Color ("Color", Color) = (0.2, 0.3, 0.6, 1)
        _RimColor ("Rim", Color) = (0.5, 0.8, 1, 1)
        _FogColor ("Fog", Color) = (0.03, 0.04, 0.08, 1)
        _Flash ("Flash", Range(0, 1)) = 0
        _Grid ("Grid", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float4 _RimColor;
            float4 _FogColor;
            float _Flash;
            float _Grid;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings Vert(Attributes a)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(a.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(a.normalOS);
                return o;
            }

            float GridLine(float2 p, float spacing, float width)
            {
                float2 g = abs(frac(p / spacing - 0.5) - 0.5) * spacing;
                float2 aa = fwidth(p) * 1.5;
                float2 l = 1.0 - smoothstep(width, width + aa, g);
                return max(l.x, l.y);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 rgb;
                if (_Grid > 0.5)
                {
                    float2 p = i.positionWS.xz;
                    float r = length(p);
                    float lines = GridLine(p, 2.0, 0.025) * 0.3 + GridLine(p, 10.0, 0.06) * 0.45;
                    // The arena: a pulsing ring and a glow around the center.
                    float ring = 1.0 - smoothstep(0.0, 0.35, abs(r - 13.0));
                    ring *= 0.7 + 0.3 * sin(_Time.y * 2.0);
                    float glow = exp(-r * 0.12) * 0.35;
                    rgb = _Color.rgb * (0.5 + glow) + _RimColor.rgb * (lines * 0.5 + ring + glow * 0.4);
                }
                else
                {
                    float rim = pow(1.0 - saturate(dot(n, v)), 2.5);
                    rgb = _Color.rgb * (0.3 + 0.7 * saturate(n.y * 0.5 + 0.5)) + _RimColor.rgb * rim;
                    rgb = lerp(rgb, float3(1.0, 1.0, 1.0), _Flash);
                }

                float fog = saturate((length(i.positionWS - _WorldSpaceCameraPos) - 18.0) / 60.0);
                rgb = lerp(rgb, _FogColor.rgb, fog);
                return half4(rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
