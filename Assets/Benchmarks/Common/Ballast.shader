Shader "Hidden/Yaui/Benchmarks/Ballast"
{
    // A deliberately expensive full screen pass that keeps the GPU saturated so that DVFS runs it at a steady clock.
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            int _Iterations;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(uint vertexId : SV_VertexID)
            {
                Varyings o;
                float2 uv = float2((vertexId << 1) & 2, vertexId & 2);
                o.positionCS = float4(uv * 2.0 - 1.0, 0.0, 1.0);
                o.uv = uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float v = i.uv.x + i.uv.y;
                for (int n = 0; n < _Iterations; n++)
                {
                    v = sin(v * 1.618 + i.uv.x) + cos(v * 0.577 - i.uv.y);
                }

                return half4(0.12, 0.12, 0.15, 1.0) + half4(frac(v), 0.0, 0.0, 0.0) * 0.01;
            }
            ENDHLSL
        }
    }
}
