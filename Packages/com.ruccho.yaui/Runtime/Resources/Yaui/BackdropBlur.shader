Shader "Hidden/Yaui/BackdropBlur"
{
    // The blur of backdrops (materials whose shader has the "YauiBackdrop" tag): a separable Gaussian on a
    // downsampled copy of the target, drawn with Blitter. _YauiBlurParams is set by the renderer for each pass.
    HLSLINCLUDE
    #pragma target 4.5
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    // xy: one texel of the source, z: sigma in texels of the source, w: radius in texels (taps on each side).
    float4 _YauiBlurParams;

    half4 Blur(float2 uv, float2 direction)
    {
        float2 step = direction * _YauiBlurParams.xy;
        float sigma = max(_YauiBlurParams.z, 1e-3);
        int radius = (int)_YauiBlurParams.w;
        float k = -0.5 / (sigma * sigma);
        half4 sum = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        float total = 1.0;
        // Pairs of texels in one bilinear sample each: the offset between them is weighted by their weights.
        for (int i = 1; i <= radius; i += 2)
        {
            float w0 = exp(k * i * i);
            float w1 = i + 1 <= radius ? exp(k * (i + 1) * (i + 1)) : 0.0;
            float w = w0 + w1;
            float offset = i + w1 / w;
            sum += (SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + step * offset) +
                SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - step * offset)) * w;
            total += 2.0 * w;
        }

        return sum / total;
    }

    half4 FragHorizontal(Varyings input) : SV_Target { return Blur(input.texcoord, float2(1.0, 0.0)); }
    half4 FragVertical(Varyings input) : SV_Target { return Blur(input.texcoord, float2(0.0, 1.0)); }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        Pass
        {
            Name "Horizontal"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragHorizontal
            ENDHLSL
        }

        Pass
        {
            Name "Vertical"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragVertical
            ENDHLSL
        }
    }
}
