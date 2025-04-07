Shader "Hidden/URPForward/HZBBuilder"
{
    SubShader
    {
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Assets/Graphics/PipelineFeature/SSR/SSR_HiZ_Feature/ReductionCommon.hlsl"
        ENDHLSL

        ColorMask R
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "HiMipmapGenerateForSSR"
            Tags
            {
                "RenderPipeline" = "UniversalPipeline"
            }
            HLSLPROGRAM
            #pragma target 2.0
            #pragma editor_sync_compilation
            #pragma vertex Vert
            #pragma fragment fragGenerateMipmap

            // uniform float4 _DispatchThreadIdToBufferUV; // x: 1/width, y: 1/height, offset
            // uniform float4 _InputViewportMaxBound; // xy: 1/width, y: 1/height, zw: MaxUV

            float4 _BlitTexture_TexelSize;
            float4 GetSource(float2 uv, float2 offset = 0.0)
            {
                uv = mad(offset.xy, _BlitTexture_TexelSize.xy, uv.xy);
                uv = clamp(uv, 0, 1);
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, _BlitMipLevel);
            }

            float4 fragGenerateMipmap(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float c1 = GetSource(uv, float2(0.5, 0.5)).r;
                float c2 = GetSource(uv, float2(0.5, -0.5)).r;
                float c3 = GetSource(uv, float2(-0.5, 0.5)).r;
                float c4 = GetSource(uv, float2(-0.5, -0.5)).r;

                return GetDeviceMinDepth(float4(c1, c2, c3, c4));
                
                //需要target4.5
                // float4 c = GATHER_RED_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);
                // return GetDeviceMinDepth(c);
            }
            ENDHLSL
        }
    }
}