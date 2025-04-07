Shader "URPForward/SSR/WorldSpace_SSR_Forward"
{
    Properties
    {
        [Header(Base Settings)]
        _WaterColor("Water Color", Color) = (0.2, 0.6, 1, 0.5)
        [Normal][NoScaleOffset]_NormalMap("Normal Map", 2D) = "bump" {}
        _NormalStrength("Normal Strength", Range(0,2)) = 0.5
        _ReflectionIntensity("Reflection Intensity", Range(0,1)) = 0.8
        _Smoothness("Smoothness", Range(0,1)) = 0.9
        [NoScaleOffset]_CubeMap("Environment Cubemap", CUBE) = "" {}

        [Header(SSR Settings)]
        _SSRSteps("SSR Steps", Int) = 32
        _SSRStepSize("SSR Step Size", Range(0.01, 1)) = 0.3
        _DepthThreshold("Depth Threshold", Range(0.01,1)) = 0.1
        _MaxRayLength("Max Ray Length", Range(5,100)) = 50

        [Header(Advanced)]
        _FresnelPower("Fresnel Power", Range(0,5)) = 3
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent+1000"
            "RenderPipeline"="UniversalPipeline"
            "LightMode" = "UniversalForward"
            "IgnoreProjector"="True"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            Name "Water"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma prefer_hlslcc gles
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Graphics/PipelineFeature/SSR/SSR_HiZ_Feature/SSRCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 viewDirWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3;
                float3 positionWS : TEXCOORD4;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _WaterColor;
                float4 _NormalMap_ST;
                half _NormalStrength;
                half _ReflectionIntensity;
                half _Smoothness;
                half _FresnelPower;
                int _SSRSteps;
                float _SSRStepSize;
                float _DepthThreshold;
                float _MaxRayLength;
                half _FallbackBlend;
            CBUFFER_END

            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);
            TEXTURECUBE(_CubeMap);
            SAMPLER(sampler_CubeMap);

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.viewDirWS = -half3(GetWorldSpaceViewDir(output.positionWS)); //这个最好别在顶点里单位化
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = half4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w);
                output.uv = TRANSFORM_TEX(input.uv, _NormalMap);

                return output;
            }

            float2 World2ClipPos(float3 postionWS)
            {
                float4 cPos = TransformWorldToHClip(postionWS);
                cPos.xyz = cPos.xyz / cPos.w;
                cPos.xy = cPos.xy * 0.5 + 0.5;
                #if UNITY_UV_STARTS_AT_TOP
                cPos.y = 1 - cPos.y;
                #endif
                return cPos.xy;
            }

            inline bool rayMarchingInWorldSpace(float3 start, float3 direction, out float2 hitUV)
            {
                float3 end = start;
                float through = 0;
                // float stepsize = sqrt(_MaxRayLength ) * _SSRStepSize;

                // float stepsize = log2(_MaxRayLength);
                float stepsize = _SSRStepSize;
                uint missCount = 0;

                UNITY_LOOP
                for (int i = 1; i <= _SSRSteps; i++)
                {
                    if (missCount > 2)
                    {
                        missCount = 0;
                        stepsize *= 2; // 步长加倍
                    }
                    end += stepsize * direction; // 当前位置
                    through += stepsize; // 走过距离
                    if (through > _MaxRayLength)
                    {
                        return false;
                    }

                    float2 currUV = World2ClipPos(end);
                    float4 uvSign = float4(sign(currUV.x), sign(1.0 - currUV.x), sign(currUV.y), sign(1.0 - currUV.y));
                    if (dot(float4(1, 1, 1, 1), uvSign) < 3.6f)
                    {
                        return false;
                    }

                    float currentDepth = SampleSceneDepthLOD(currUV, 0); //deviceDepth
                    currentDepth = LinearEyeDepth(currentDepth, _ZBufferParams);
                    float3 stepViewPos = TransformWorldToView(end);
                    stepViewPos.z *= -1;

                    float zDistance = stepViewPos.z - currentDepth;
                    //穿透
                    if (zDistance > 0)
                    {
                        end -= stepsize * direction; // 回退一步
                        through -= stepsize; // 回退走过距离
                        stepsize *= 0.5f; // 步长缩小一半
                    }
                    //有效命中
                    if (abs(zDistance) < _DepthThreshold)
                    {
                        hitUV.xy = currUV;

                        return true;
                    }
                    missCount++;
                }

                return false;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 水面法线计算
                half3 normalTangent = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv));
                normalTangent.xy *= half(_NormalStrength);
                half3x3 TBN = half3x3(
                    input.tangentWS.xyz,
                    cross(input.normalWS, input.tangentWS.xyz) * input.tangentWS.w,
                    input.normalWS
                );

                half3 wNormal = (mul(normalTangent, TBN));

                float3 wPos = input.positionWS;

                // half3 wViewDir = -GetWorldSpaceViewDir(wPos);
                half3 wViewDir = normalize(input.viewDirWS);
                half3 reflectionDirWS = reflect(wViewDir, wNormal);

                reflectionDirWS = normalize(reflectionDirWS);

                float2 hitUV = 0;
                float3 reflectionColor = 0;
                if (rayMarchingInWorldSpace(wPos, reflectionDirWS, hitUV))
                {
                    half3 hitColor = SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, hitUV, 0)
                        .xyz;
                    reflectionColor += hitColor;
                }
                else
                {
                    // half3 IBL = SAMPLE_TEXTURECUBE_LOD(_CubeMap, sampler_CubeMap, reflectionDir, 0).xyz;
                    half3 IBL = SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, reflectionDirWS, 0).xyz;
                    reflectionColor += IBL;
                }
                half4 BaseColr = _WaterColor;
                half fresnel = pow(max(HALF_MIN, 1 - dot(wNormal, wViewDir)), _FresnelPower);
                half3 result = BaseColr.rgb * (1 - _ReflectionIntensity) + reflectionColor * fresnel * _ReflectionIntensity;

                return half4(result, BaseColr.a);
            }
            ENDHLSL
        }
    }
}