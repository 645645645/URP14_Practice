Shader "Hidden/SSS_Skin_Test"
{
    Properties
    {
        _MainTex ("BaseMap", 2D) = "white" {}
        _NormalStrength("Normal Lod", Range(0,10)) = 1
        [Normal][NoScaleOffset]_NormalMap("Normal Map", 2D) = "bump" {}
        _CurveFactor ("SSS Lut CurveFactor", Range(0.0, 1.0)) = 0.5//应该叫整体厚度
//        _Roughness("Roughness", Range(0,1)) = 1
//        _SpecColor("Specular", Color) = (0.2, 0.2, 0.2)
        [NoScaleOffset]_SpecGlossMap("Specular", 2D) = "white" {}
        _OcclusionStrength("Occlusion Strength", Range(0.0, 1.0)) = 1.0
        [NoScaleOffset]_OcclusionMap("Occlusion", 2D) = "white" {}
        [NoScaleOffset]_SSSLutTex ("SSSLut", 2D) = "white" {}
        [NoScaleOffset]_EnvironmentCubemap ("Environment", Cube) = "black" {}

    }
    SubShader
    {
        // No culling or depth
        //        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float4 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv : TEXCOORD0;
                half4 tangentWS : TEXCOORD1;
                half4 normalWS : TEXCOORD2;
                half4 bitangentWS : TEXCOORD3;
                half3 viewDirWS : TEXCOORD4;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;

                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.tangentWS = half4(normalInput.tangentWS, positionWS.x);
                output.bitangentWS = half4(normalInput.bitangentWS, positionWS.y);
                output.normalWS = half4(normalInput.normalWS, positionWS.z);
                output.viewDirWS = -half3(GetWorldSpaceViewDir(positionWS));
                return output;
            }

            CBUFFER_START(UnityPerMaterial)
                float _CurveFactor;
                float _OcclusionStrength;
                float _NormalStrength;
                // float _Roughness;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);
            TEXTURE2D(_SpecGlossMap);
            SAMPLER(sampler_SpecGlossMap);
            TEXTURE2D(_OcclusionMap);
            SAMPLER(sampler_OcclusionMap);
            TEXTURE2D(_SSSLutTex);
            SAMPLER(sampler_SSSLutTex);
            TEXTURECUBE(_EnvironmentCubemap);
            SAMPLER(sampler_EnvironmentCubemap);

            float Pow5(float x)
            {
                return x * Pow4(x);
            }

            // GGX / Trowbridge-Reitz
            // [Walter et al. 2007, "Microfacet models for refraction through rough surfaces"]
            float D_GGX_Unreal(float a2, float NoH)
            {
                float d = (NoH * a2 - NoH) * NoH + 1; // 2 mad
                return a2 / (PI * d * d); // 4 mul, 1 rcp
            }

            // Smith term for GGX
            // [Smith 1967, "Geometrical shadowing of a random rough surface"]
            float Vis_Smith_Unreal(float a2, float NoV, float NoL)
            {
                float Vis_SmithV = NoV + sqrt(NoV * (NoV - NoV * a2) + a2);
                float Vis_SmithL = NoL + sqrt(NoL * (NoL - NoL * a2) + a2);
                return rcp(Vis_SmithV * Vis_SmithL);
            }

            // [Schlick 1994, "An Inexpensive BRDF Model for Physically-Based Rendering"]
            float3 F_Schlick_Unreal(float3 SpecularColor, float VoH)
            {
                float Fc = Pow5(1 - VoH); // 1 sub, 3 mul
                return Fc + (1 - Fc) * SpecularColor; // 1 add, 3 mad

                // Anything less than 2% is physically impossible and is instead considered to be shadowing
                //return saturate( 50.0 * SpecularColor.g ) * Fc + (1 - Fc) * SpecularColor;
            }

            half3 DirectBRDFSpecularSmith(float roughness, half NoH, half LoH, half NoL, half NoV)
            {
                float D = D_GGX_Unreal(roughness * roughness * roughness * roughness, NoH);
                float V = Vis_Smith_Unreal(roughness * roughness, NoV, NoL);

                float3 result = D * V;
                return result;
            }
            
            half2 EnvBRDFApproxLazarov(half Roughness, half NoV)
            {
                // [ Lazarov 2013, "Getting More Physical in Call of Duty: Black Ops II" ]
                // Adaptation to fit our G term.
                const half4 c0 = {-1, -0.0275, -0.572, 0.022};
                const half4 c1 = {1, 0.0425, 1.04, -0.04};
                half4 r = Roughness * c0 + c1;
                half a004 = min(r.x * r.x, exp2(-9.28 * NoV)) * r.x + r.y;
                half2 AB = half2(-1.04, 1.04) * a004 + r.zw;
                return AB;
            }

            half4 frag(Varyings input) : SV_Target
            {
                Light mainLight = GetMainLight();
                half3 lightWS = mainLight.direction;
                float2 uv = input.uv.xy;
                half3 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
                half4 specGloss = SAMPLE_TEXTURE2D(_SpecGlossMap, sampler_SpecGlossMap, uv);
                half ao = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uv).g;

                // half metallic = specGloss.r;
                half metallic = 0.04;
                half3 specularColor = specGloss.rgb;
                half smoothness = specGloss.a;
                half rouss = (1 - smoothness);
                half roughness = max(rouss, 0.089);
                half roughnessOffset = max(rouss * 0.7, 0.089);

                float3 positionWS = float3(input.tangentWS.w, input.bitangentWS.w, input.normalWS.w);
                half3 viewWS = -normalize(GetWorldSpaceViewDir(positionWS));
                half3 halfWS = SafeNormalize(viewWS + lightWS);

                half3 normalTangent = UnpackNormal(SAMPLE_TEXTURE2D_X_LOD(_NormalMap, sampler_NormalMap, input.uv, _NormalStrength));
                half3x3 TBN = half3x3(
                    input.tangentWS.xyz,
                    input.bitangentWS.xyz,
                    input.normalWS.xyz
                );
                // normalTangent.xy *= _NormalStrength.xx;
                half3 normalWS = normalize(mul(normalTangent, TBN));
                half3 reflectDirWS = normalize(reflect(viewWS, normalWS));

                float NdotL = dot(normalWS, lightWS);

                half nl = saturate(NdotL);
                half nh = saturate(dot(normalWS, halfWS));
                half lh = saturate(dot(halfWS, lightWS));
                // half vh = saturate(dot(viewWS, halfWS));
                half nv = saturate(dot(normalWS, viewWS));
                half3 F0 = specularColor * lerp(0.04, albedo.rgb, metallic);

                half3 diffuseColor = albedo.rgb * (1 - metallic);
                float3 kd = (1 - F0) * diffuseColor;
                float3 F = F_Schlick_Unreal(F0, lh); //LoH == VoH

                half3 directBRDFSpecular1 = DirectBRDFSpecularSmith(roughness, nh, lh, nl, nv);
                half3 directBRDFSpecular2 = DirectBRDFSpecularSmith(roughnessOffset, nh, lh, nl, nv);
                half3 directBRDFSpecular = (directBRDFSpecular1 + directBRDFSpecular2) * F;


                // float invR = saturate(length(fwidth(normalWS)) * 0.05 / length(fwidth(positionWS)));
                float invR = input.uv.z;
                float2 lutUV = float2((nl + 0.2) * 0.3472 + 0.5, invR);
                // float2 lutUV = float2(NdotL * 0.5 + 0.5, invR);
                half3 lut = SAMPLE_TEXTURE2D_X(_SSSLutTex, sampler_SSSLutTex, lutUV).rgb;
                half3 diff = lerp(kd * nl, lut * albedo.rgb, _CurveFactor);

                half3 color = (diff + nl * directBRDFSpecular) * mainLight.color;
                
                
                half4 irrandiance = SAMPLE_TEXTURECUBE_LOD(_EnvironmentCubemap, sampler_EnvironmentCubemap, normalWS, roughness * 8);
                half4 prefilteredColor = SAMPLE_TEXTURECUBE_LOD(_EnvironmentCubemap, sampler_EnvironmentCubemap, reflectDirWS, roughness * 4);
                irrandiance.xyz = DecodeHDREnvironment(irrandiance, unity_SpecCube0_HDR);
                prefilteredColor.xyz = DecodeHDREnvironment(prefilteredColor, unity_SpecCube0_HDR);
                
                half3 diffIBL = lerp(kd, lut * albedo.rgb, _CurveFactor) * irrandiance.xyz;
                
                half2 scale_bias = EnvBRDFApproxLazarov(roughness, nv);
                half3 envBRDF = F0 * scale_bias.xxx + scale_bias.yyy;
                half3 specIBL = prefilteredColor.xyz * envBRDF;
                half3 envColor = max(specIBL + diffIBL, 0.0) * LerpWhiteTo(ao , _OcclusionStrength);
                
                color += envColor;
                
                // return half4(lut.xyz, 1.0);
                // return half4(normalWS.xyz, 1.0);
                // return half4(nl.xxx, 1.0);
                // return half4(invR.xxx, 1.0);
                return half4(color.xyz, 1.0);
            }
            ENDHLSL
        }
    }
}