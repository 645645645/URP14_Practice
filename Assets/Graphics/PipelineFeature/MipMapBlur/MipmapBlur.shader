Shader "Hidden/Custom/MipmapBlur"
{
    HLSLINCLUDE
    #pragma exclude_renderers gles
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"


    // float4 _BlitTexture_TexelSize;
    uniform float4 _MBParams;

    #define _CurMipLevel          _MBParams.x
    #define _Weight               _MBParams.y
    // #define _BlurLevel            _MBParams.z
    // #define _MipCount             _MBParams.w


    TEXTURE2D_X(_TempMipTexture);

    // float MipBlendWeight(float2 uv)
    // {
    //     
    //     const float sigma2 = _BlurLevel * _BlurLevel;
    //     const float c = 2.0 * PI * sigma2;
    //     const float numerator = (1 << (_CurMipLevel << 2)) * log(4.0);
    //     const float denominator = c * ((1 << (_CurMipLevel << 1)) + c);
    //     return clamp(numerator / denominator, 0.0, 1.0);
    // }


    half4 FragmentCopy(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        return SAMPLE_TEXTURE2D_X_LOD(_TempMipTexture, sampler_LinearClamp, input.texcoord, _CurMipLevel);
    }

    half4 FragmentBlend(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        const half3 c1 = SAMPLE_TEXTURE2D_X_LOD(_TempMipTexture, sampler_LinearClamp, input.texcoord, 0).rgb;
        const half3 c2 = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, input.texcoord, _CurMipLevel).rgb;
        const half4 color = float4(lerp(c1, c2, _Weight), 1.0);
        return color;
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
            Name "MipmapBlur upSample"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragmentCopy
            ENDHLSL
        }

        Pass
        {
            Name "MipmapBlur blend"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragmentBlend
            ENDHLSL
        }
    }
}