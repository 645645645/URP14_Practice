Shader "URPForward/SSRWater/SSR_Water"
{
    Properties
    {
        [Header(Base Settings)]
        _WaterColor("Water Color", Color) = (0.2, 0.6, 1, 0.5)
        [NoScaleOffset]_MainTex("Main Texture", 2D) = "white" {}
        _UVScale("UV Scale", Range(0.1,4)) = 1
        _NormalStrength("Normal Strength", Range(0,1)) = 0.5
        [Normal][NoScaleOffset]_NormalMap("Normal Map", 2D) = "bump" {}
        [NoScaleOffset]_PBR("PBR metallic roughness envMask", 2D) = "white" {}
        //        [NoScaleOffset]_AO("Ambient Occlusion", 2D) = "white"{}
        //        _ReflectionIntensity("Reflection Intensity", Range(0,1)) = 0.8
        _Smoothness("Smoothness", Range(0,1)) = 0.9
        [NoScaleOffset]_CubeMap("Irradiance Cubemap", CUBE) = "" {}

    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent+800"
            "RenderPipeline"="UniversalPipeline"
            "LightMode" = "UniversalForward"
            "IgnoreProjector"="True"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            Name "SSRWater"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma prefer_hlslcc gles
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Graphics/PipelineFeature/SSR/SSR_HiZ_Feature/SSRCommon.hlsl"

            #pragma multi_compile_fragment _ _SSRHIZ _SSRHIZUE4
            #pragma enable_d3d11_debug_symbols

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
                // float2 uv : TEXCOORD0;
                half3 viewDirWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3;
                float3 positionWS : TEXCOORD4;
                float4 vertex : TEXCOORD5;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _WaterColor;
                // float4 _NormalMap_ST;
                half _NormalStrength;
                // half _ReflectionIntensity;
                half _Smoothness;
                float _UVScale;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            TEXTURE2D(_NormalMap);
            TEXTURE2D(_PBR);
            // TEXTURE2D(_AO);
            TEXTURECUBE(_CubeMap);
            SAMPLER(sampler_CubeMap);


            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.viewDirWS = -half3(GetWorldSpaceViewDir(output.positionWS));
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = half4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w);
                // output.uv = TRANSFORM_TEX(input.uv, _NormalMap);
                output.vertex = output.positionCS;
                return output;
            }


            half4 frag(Varyings input) : SV_Target
            {
                //pbr部分扒的幻书启示录的，出自UE4 大概老版本
                half4 BaseColr = _WaterColor;
                float2 texUV = input.positionWS.xz * _UVScale * 0.2f;
                half4 albdo = SAMPLE_TEXTURE2D(_MainTex, sampler_LinearRepeat, texUV) * BaseColr;
                Light mainLight = GetMainLight();
                half3 lightColor = mainLight.color;
                half3 lightDirWS = mainLight.direction;
                // 水面法线计算
                half3 normalTangent = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_LinearRepeat, texUV));
                normalTangent.xy *= half(_NormalStrength);
                half3x3 TBN = half3x3(
                    input.tangentWS.xyz,
                    cross(input.normalWS, input.tangentWS.xyz) * input.tangentWS.w,
                    input.normalWS
                );
                half4 pbr = SAMPLE_TEXTURE2D(_PBR, sampler_LinearRepeat, texUV);
                // half4 ao = SAMPLE_TEXTURE2D(_AO, sampler_LinearRepeat, texUV);
                half metallic = pbr.r;
                half roughness = max(pbr.g * (1 - _Smoothness), HALF_MIN);
                half envMask = pbr.b;
                half3 F0 = lerp(0.04, albdo.rgb, metallic);
                half3 diffuseColor = albdo.rgb * (1 - metallic);

                half3 wNormal = mul(normalTangent, TBN);
                half3 viewDirWS = normalize(input.viewDirWS);
                //顶点插值有误差但不多
                // half3 viewDirWS = -normalize(GetWorldSpaceViewDir(input.positionWS));
                half3 halfDirWS = normalize(lightDirWS + viewDirWS);
                half3 reflectDirWS = normalize(reflect(viewDirWS, wNormal));
                // half3 reflectDirVS = mul((float3x3)unity_WorldToCamera, reflectDirWS);
                half3 reflectDirVS = TransformWorldToViewDir(reflectDirWS, true);

                half nl = saturate(dot(wNormal, lightDirWS));
                half nh = saturate(dot(wNormal, halfDirWS));
                half vh = saturate(dot(viewDirWS, halfDirWS));
                half nv = saturate(dot(wNormal, viewDirWS));

                // float f = F0 + (1 - F0) * pow(1 - vh, 5);
                // Spherical Gaussian近似来替代power，稍微提高了计算效率，并且差异微不可察
                half3 F = F0 + (1 - F0) * pow(2, (-5.55473 * vh - 6.98316) * vh);
                // float F = F0 + (1 - F0) * pow(2.0, (-5.5547299 * nh - 6.98316) * nh);
                half3 kd = (1 - F0) * diffuseColor;
                // half3 kd = (1 - F) * diffuseColor;

                half roughness2 = pow(roughness, 2);
                half roughness4 = pow(roughness2, 2);

                half D = roughness4 / pow((roughness4 * nh - nh) * nh + 1, 2);
                half L = ((1 - roughness2) * nv + roughness2) * nl;
                half V = ((1 - roughness2) * nv + roughness2) * nv;
                half G = clamp(0.5 / (L + V), 0, 1);
                half3 DFG = D * F * G;
                half3 dirLightResult = nl * (kd + DFG) * lightColor; /* * mainLight.intensity;*/

                float3 clipPos = input.vertex.xyz / input.vertex.w;
                clipPos.xyz = clipPos.xyz * 0.5 + 0.5;
                // #ifdef UNITY_UV_STARTS_AT_TOP
                // clipPos.y = 1 - clipPos.y;
                // #endif
                // #ifdef   UNITY_REVERSED_Z
                // clipPos.z = 1 - clipPos.z;
                // #endif
                // float cDepth = SampleSceneDepthLOD(clipPos.xy, 0);
                // 这三效果不一样 注 UNITY_UV_STARTS_AT_TOP 是渲染到纹理的设置
                //  unity_CameraInvProjection 相机z取反, 没处理 UNITY_UV_STARTS_AT_TOP
                //  _InvProjMatrix            没取反，  处理了 UNITY_UV_STARTS_AT_TOP
                //  unity_MatrixInvP         相机z取反，没处理 UNITY_UV_STARTS_AT_TOP
                // float3 viewPos = ComputeViewSpacePosition(clipPos.xy, clipPos.z, unity_CameraInvProjection); 
                // 前向拿ViewPos比延迟方便很多..
                float3 posVS = TransformWorldToView(input.positionWS);
                // posVS.z *= -1;
                // return half4(posVS.xxx, 1);


                half3 Irradiance = SAMPLE_TEXTURECUBE_LOD(_CubeMap, sampler_CubeMap, wNormal, 5).xyz;
                half3 diffIBL = kd * Irradiance * envMask;


                /* UE4.27 BRDF.ush
                half2 EnvBRDFApproxLazarov(half Roughness, half NoV)
                {
                    // [ Lazarov 2013, "Getting More Physical in Call of Duty: Black Ops II" ]
                    // Adaptation to fit our G term.
                    const half4 c0 = { -1, -0.0275, -0.572, 0.022 };
                    const half4 c1 = { 1, 0.0425, 1.04, -0.04 };
                    half4 r = Roughness * c0 + c1;
                    half a004 = min(r.x * r.x, exp2(-9.28 * NoV)) * r.x + r.y;
                    half2 AB = half2(-1.04, 1.04) * a004 + r.zw;
                    return AB;
                }
                */
                half4 bias = half4(1.0, 0.0425, 1.04, -0.04) - roughness.xxxx;
                half2 scale_bias = (min(pow(bias.x, 2), exp2(-9.2799997 * nv)) * bias.x + bias.y) * float2(-1.04, 1.04) + bias.zw;
                half3 envBRDF = F0 * scale_bias.x + scale_bias.y;


                float3 outHitUVz;

                #if defined(_SSRHIZUE4)
                float mipLevel = 0;
                float3 posWS = input.positionWS;
                float sceneDepth = clipPos.z;
                bool isHit = rayCastHiZ(posWS, reflectDirWS, roughness, sceneDepth, SSR_SSRStepNums, outHitUVz);
                #elif defined(_SSRHIZ)
                bool isHit = HierarchicalZScreenSpaceRayMarching(posVS, reflectDirVS, outHitUVz);
                #else
                // bool isHit = rayMarchingInScreenSpace(posVS, reflectDirVS, outHitUVz);
                bool isHit = BinarySearchRaymarching(posVS, reflectDirVS, outHitUVz);
                
                #endif


                half3 prefilteredColor = SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, reflectDirWS, roughness * 5).xyz;
                // return half4(outHitUVz.xyz, 1);

                UNITY_BRANCH
                if (isHit)
                {
                    // float ClosestHitDistanceSqr = ComputeRayHitSqrDistance(posWS, outHitUVz);

                    float Vignette = ComputeHitVignetteFromScreenPos(outHitUVz.xy);
                    half3 reflectionColor = SampleSceneColor(outHitUVz.xy);

                    prefilteredColor = lerp(prefilteredColor, reflectionColor, Vignette);

                    float RoughnessFade = GetRoughnessFade(roughness);
                    prefilteredColor *= RoughnessFade;
                }

                half3 specularIBL = envBRDF * prefilteredColor * envMask;

                float3 envIBLResult = max(0, diffIBL + specularIBL);

                half3 color = min(10, (dirLightResult * 1 + envIBLResult));

                // return half4(outHitUVz.xyz, 1);

                return half4(color.xyz, BaseColr.a);
            }
            ENDHLSL
        }
    }
}