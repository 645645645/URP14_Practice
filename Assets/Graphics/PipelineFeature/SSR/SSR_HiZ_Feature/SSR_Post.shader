Shader "Hidden/Custom/SSR_Hiz"
{
    HLSLINCLUDE
#pragma enable_d3d11_debug_symbols enable_vulkan_debug_symbols
    #include "Assets/Graphics/PipelineFeature/SSR/SSR_HiZ_Feature/SSRCommon.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    TEXTURE2D_X_FLOAT(_CameraNormalsTexture);
    SAMPLER(sampler_CameraNormalsTexture);

    TEXTURECUBE(_Tex);//cubeMap
    SAMPLER(sampler_Tex);

TEXTURE2D_X(_ID2PBR_Tex);
SAMPLER(sampler_ID2PBR_Tex);
TEXTURE2D_X(_ID2Diffuse_Tex);
SAMPLER(sampler_ID2Diffuse_Tex);

    float4x4 _Inverse_View_ProjectionMatrix;

    half4 SampleTexture(TEXTURE2D_PARAM(_Texture, sampler_Texture), float2 uv)
    {
        return SAMPLE_TEXTURE2D_X_LOD(_Texture, sampler_Texture, UnityStereoTransformScreenSpaceTex(uv), 0);
    }

    float SampleDepth(float2 uv)
    {
        return SampleSceneDepth(uv);
    }

    half4 SampleCameraNormal(float2 uv)
    {
        return SampleTexture(TEXTURE2D_ARGS(_CameraNormalsTexture, sampler_CameraNormalsTexture), uv);
    }

    half4 GetWorldNormalSpec(float2 uv)
    {
        return SampleCameraNormal(uv);
    }

    half2 IdToUv(half id)
    {
        int ID = (int)(id * 128.0 + 128.0);
        return half2(ID & 15, ID >> 4) / 16.0;
    }

    half4 Fragment_SSR(Varyings input): SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        float2 uv = input.texcoord;

        float depth = SampleDepth(uv);

        float4 wPos = mul(_Inverse_View_ProjectionMatrix, float4(mad(uv, 2, -1), depth, 1));
        wPos.xyz /= wPos.w;

        half4 wNormal = GetWorldNormalSpec(uv);
        // half roughness = wNormal.w;
        
        half2 matIdUV = IdToUv(wNormal.w);
        half4 pbr = SampleTexture(TEXTURE2D_ARGS(_ID2PBR_Tex, sampler_ID2PBR_Tex), matIdUV);
        half roughness = pbr.x;

        half3 vDir = normalize(wPos.xyz - _WorldSpaceCameraPos.xyz);
        half3 reflectDirWS = reflect(vDir, wNormal.xyz);

        float sceneDepth = LinearEyeDepth(depth, _ZBufferParams);
        float3 outHitUVz = uv.xyy;

        #if UNITY_REVERSED_Z
        UNITY_BRANCH
        bool isHit = depth > 0.0001 && rayCastHiZ(wPos, reflectDirWS, roughness, sceneDepth, SSR_SSRStepNums, outHitUVz);
        #else
        bool isHit = depth < 0.9999 && rayCastHiZ(wPos, reflectDirWS, roughness, sceneDepth, SSR_SSRStepNums, outHitUVz);
        #endif

        //unity_SpecCube0时有时无的
        half3 prefilteredColor = SAMPLE_TEXTURECUBE_LOD(_Tex, sampler_Tex, reflectDirWS, 0).xyz;
        // prefilteredColor = 0;
        UNITY_BRANCH
        if (isHit)
        {
            half3 reflectionColor = SampleSceneColorLOD(outHitUVz.xy, 0);

            // float Vignette = ComputeHitVignetteFromScreenPos(outHitUVz.xy, 1.4, 1);
            float Vignette = ComputeHitVignetteFromScreenPos(outHitUVz.xy, 1, 1);
            
            prefilteredColor = lerp(prefilteredColor, reflectionColor, Vignette);

            // return Vignette;
        }
        float RoughnessFade = GetRoughnessFade(roughness);
        prefilteredColor *= RoughnessFade;
        // prefilteredColor *= ComputeVignetteFromScreenPos(uv, 1.5, 1);

        return half4(prefilteredColor, 0);
    }
    ENDHLSL


    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
        }
        
        ZTest Always
        ZWrite Off
        Blend Off
        Cull Off
        
        
        Pass
        {
            Name "SSR Post"
            
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Fragment_SSR
            ENDHLSL
        }

        
    }

}