Shader "Hidden/SeparableSubsurfaceScatter"
{
    Properties
    {
        _RefValue ("Ref Value", Float) = 2
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #define nSamples 7

    float _SSSSDepthFalloff;
    float _DistanceToProjectionWindow;
    float2 _SSSSDirection;
    float4 _Kernel[nSamples];
    float4 _CameraDepthTexture_TexelSize;

    #pragma target 3.0


    half4 frag(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord.xy;
        float4 colorM = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
        float dSceneDepth = SampleSceneDepth(uv);
        float depthM = LinearEyeDepth(dSceneDepth, _ZBufferParams);

        float scale = _DistanceToProjectionWindow / depthM;
        float2 finalStep = _SSSSDirection.xy * scale * _CameraDepthTexture_TexelSize.xy;

        float4 colorBlurred = colorM;
        colorBlurred.rgb *= _Kernel[0].rgb;


        for (int i = 1; i < nSamples; i++)
        {
            float2 offset = uv + _Kernel[i].a * finalStep;
            float4 color = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, offset, 0);
            #ifdef SSSS_FOLLOW_SURFACE
					float depth = LinearEyeDepth(SampleSceneDepth(offset), _ZBufferParams);
            #if 0
					float s = 1 - exp2(-_SSSSDepthFalloff * 10 * _DistanceToProjectionWindow * abs(depthM - depth));
            #else
					float s = saturate(_SSSSDepthFalloff * 10 * _DistanceToProjectionWindow * abs(depthM - depth));
            #endif
					color.rgb = lerp(color.rgb, colorM.rgb, s);
            #endif
            colorBlurred.rgb += _Kernel[i].rgb * color.rgb;
        }

        return colorBlurred;
    }
    ENDHLSL

    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "4S Pass"
            ColorMask RGB
            Stencil
            {
                Ref [_RefValue]
                Comp Equal
            }

            HLSLPROGRAM
            #pragma multi_compile _ SSSS_FOLLOW_SURFACE
            #pragma vertex Vert
            #pragma fragment frag
            ENDHLSL
        }
    }
}