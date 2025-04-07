/*
unity 2020.3.f1c1
urp 10.9.0

Linear

显存导出tex uv.y翻转，SAMPLE 前 revert
*/
Shader "NN4/Eye1"
{
    Properties
    {
        _posm_ShadowMap ("_posm_ShadowMap", 2D) = "white" {}
        _ParallexTex ("视差高度", 2D) = "white" {}
        _Parallex ("视差", Range(0, 1)) = 0.56
        _MainTex ("固有色", 2D) = "white" {}
        _CookieTex ("高光贴图", 2D) = "white" {}
        _NN4AmbientTint("环境光", Color) = (1,1,1,0)
        _NN4Char_LightColor1("角色灯光颜色", Color) = (0.31,0.31,0.31,0)
        _Rim ("角膜亮度", Range(0, 10)) = 3.12
        _CookieMoveSpeed1 ("高光移动速度1", Range(-1, 1)) = -0.18
        _CookieMoveSpeed2 ("高光移动速度2", Range(-1, 1)) = -0.06
        _CookieMoveSpeed3 ("高光移动速度3", Range(-1, 1)) = -0.24
        _CookieOffset ("初始偏移(XY)", Vector) = (0,0.03,0,0)
        _CookieOffset2 ("高光偏移()", Vector) = (-0.23,0.58,0.19,-0.27)
        _CookieIntensity ("高光亮度", Vector) = (1,1,1.12,0)
        _DepthBias ("Depth Offset", Float) = 0

        _EyeUp("_EyeUp", Vector) = (0.00204,0.9986,0.05288,0)
        _EyeRight("_EyeRight", Vector) = (0.98858,-0.00998,0.15038,0)
        _posm_ShadowCamera_Parameter("_posm_ShadowCamera_Parameter", Vector) = (0.01,1.70942,0.58844,-0.00588)
        _posm_SampleBias ("_posm_SampleBias", Float) = 0.001
        _posm_Parameters("_posm_Parameters", Vector) = (1, 1000, 0.00098, 0.00391)
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "LightMode" = "UniversalForward"
        }
        //        LOD 100

        Pass
        {
            Offset 0 , [_DepthBias]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct appdata
            {
                float4 position : POSITION;
                float3 normal : NORMAL;
                float2 uv1 : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
            };

            struct v2f
            {
                float4 clipPos : SV_POSITION;
                float4 v0 : TEXCOORD0;
                float3 v1 : TEXCOORD1;
                float3 v2 : TEXCOORD2;
                float3 v3 : TEXCOORD3;
                float3 v4 : TEXCOORD4;
                float2 v5 : TEXCOORD5;
            };

            TEXTURE2D(_posm_ShadowMap);
            SAMPLER(sampler_posm_ShadowMap);
            TEXTURE2D(_ParallexTex);
            SAMPLER(sampler_ParallexTex);
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_CookieTex);
            SAMPLER(sampler_CookieTex);

            CBUFFER_START(UnityPerMaterial)
            float4x4 _POSM_MATRIX_V =
            {
                -0.95298, 0.1371, 0.31805, 0,
                7.45058E-09, 0.8918, 0.45243, 0,
                -0.30304, -0.43116, 0.84986, 0,
                0.0, -0.6956, -1.24477, 1
            };
            float4x4 _POSM_MATRIX_VP =
            {
                -0.97933, 0.14089, 0.31805, 0,
                7.65656E-09, 0.91645, -0.53246, 0,
                -0.31141, -0.43108, -1.00018, 0,
                0.0, -0.71483, -0.45317, 1
            };
            float4 _posm_ShadowCamera_Parameter;
            float3 _NN4AmbientTint;
            float4 _posm_Parameters;
            float _posm_SampleBias;
            float3 _NN4Char_LightColor1;
            float _Rim;
            float _Parallex;
            float _CookieMoveSpeed1;
            float _CookieMoveSpeed2;
            float _CookieMoveSpeed3;
            float4 _EyeUp;
            float4 _EyeRight;
            float4 _CookieOffset;
            float4 _CookieOffset2;
            float4 _CookieIntensity;
            float _DepthBias;
            CBUFFER_END

            CBUFFER_START(UnityPerDraw)
            // float4x4 unity_ObjectToWorld;
            float4 _unity_SHAr;
            float4 _unity_SHAg;
            float4 _unity_SHAb;
            float4 _unity_SHBr;
            float4 _unity_SHBg;
            float4 _unity_SHBb;
            float4 _unity_SHC;
            CBUFFER_END

            v2f vert(appdata v)
            {
                v2f o;
                // o.clipPos = TransformObjectToHClip(v.position);
                float4 worldPos = mul(unity_ObjectToWorld, v.position);
                float4 clipPos = mul(UNITY_MATRIX_VP, worldPos);
                // o.clipPos.z = clipPos.z - clipPos.w * _DepthBias;
                o.clipPos.xyzw = clipPos.xyzw;

                float objectDepthOffset = v.position.z + 0.033;
                // 安全处理..// 0xFFFFFFFFu  4294967295 || 0
                uint tmp1 = (0.0 < objectDepthOffset) ? 0xFFFFFFFFu : (uint)0;
                uint tmp2 = objectDepthOffset < 0.0 ? 0xFFFFFFFFu : (uint)0;
                // 1 || -1
                int xlati1 = (int)tmp2 - (int)tmp1;
                float safeDepth = clamp(float(xlati1), 0, 1);
                o.v1.z = safeDepth;
                o.v1.xy = v.uv1.xy;
                o.v5.xy = v.uv2.xy;

                float3 worldNormal = normalize(TransformObjectToWorldNormal(v.normal.xyz));
                o.v2.xyz = worldNormal.xyz;

                // float3 worldPos = 
                o.v3.xyz = worldPos.xyz;
                // half4 wNormal = half4(worldNormal.xyz, 1);
                half3 SH_color = SampleSH(worldNormal);
                //pow(x,y) = exp2(log2(x)*y);
                // half3 srgbSH = max(exp2(log2(SH_color) * 0.41666666) * 1.0549999 - 0.055 , 0);
                // half3 srgbSH = max(pow(SH_color, 0.41666666) * 1.0549999 - 0.055 , 0);
                //FastLinearToSRGB.,SH里有这段 如果.Linear项目 不走这
                o.v4.xyz = SH_color * _NN4AmbientTint.xyz;

                float shadowDepth = mul(_POSM_MATRIX_V, worldPos).z * _posm_ShadowCamera_Parameter.z -
                    _posm_ShadowCamera_Parameter.w;
                o.v0.z = shadowDepth + 1;
                float3 shadowCoord = mul(_POSM_MATRIX_VP, worldPos).xyw;
                o.v0.xyw = shadowCoord;


                return o;
            }

            float4 texShadowOffset(float4 shadowcoordv, float4 v,
                float4 shadowcoordh, float4 h, float biasDepth)
            {
                float4 lightDepth;
                // _posm_Parameters = (1, 1000, 0.00098, 0.00391) 猜测是个 Kawase Blur
                float4 shadowcoordParams = shadowcoordv + v * _posm_Parameters.w;
                shadowcoordParams.yw = 1 - shadowcoordParams.yw;//显存导出uv.v翻转
                lightDepth.x = SAMPLE_TEXTURE2D_LOD(_posm_ShadowMap, sampler_posm_ShadowMap, shadowcoordParams.xy, 0).x;
                lightDepth.y = SAMPLE_TEXTURE2D_LOD(_posm_ShadowMap, sampler_posm_ShadowMap, shadowcoordParams.zw, 0).x;
                shadowcoordParams = shadowcoordh + h * _posm_Parameters.w;
                shadowcoordParams.yw = 1 - shadowcoordParams.yw;//
                lightDepth.z = SAMPLE_TEXTURE2D_LOD(_posm_ShadowMap, sampler_posm_ShadowMap, shadowcoordParams.xy, 0).x;
                lightDepth.w = SAMPLE_TEXTURE2D_LOD(_posm_ShadowMap, sampler_posm_ShadowMap, shadowcoordParams.zw, 0).x;
                float4 inshadow = biasDepth.xxxx >= lightDepth;
                return inshadow;
            }

            half4 frag(v2f i) : SV_Target
            {
                float biasDepth = max(i.v0.z, 0.001) + _posm_SampleBias;
                float4 shadowcoord = i.v0.xyxy * 0.5 + 0.5;
                //豪华16次采样
                float4 inshadow =
                    texShadowOffset(shadowcoord.zwzw, float4(-0.1658961, 0.98614317, 0.8875289, 0.14930651),
                                    shadowcoord.zwzw, float4(0.13271689, -0.78891462, -0.69030023, -0.1161273),
                                    biasDepth)
                    + texShadowOffset(shadowcoord.zwzw, float4(-0.35084891, 0.35623729, 0.46310851, 0.45610359),
                                      shadowcoord.zwzw, float4(0.070169792, -0.071247473, -0.2493661, -0.24559429),
                                      biasDepth)
                    + texShadowOffset(shadowcoord.zwzw, float4(0.055099439, 0.2438525, 0.78032809, -0.1763182),
                                      shadowcoord.zwzw, float4(-0.198358, -0.87786913, -0.9754101, 0.2203977),
                                      biasDepth)
                    + texShadowOffset(shadowcoord.zwzw, float4(0.4280726, 0.73433912, 0.25917849, -0.15108439),
                                      shadowcoord.xyzw, float4(-0.3273496, -0.56155342, -0.1727857, 0.100723),
                                      biasDepth);
                float sum = dot(inshadow, float4(0.625, 0.625, 0.625, 0.625));
                float3 shadowColr = sum * _NN4Char_LightColor1.xyz + i.v4.xyz; //v4 -> ambimet, LightColor -> 0.31

                float3 view = normalize(_WorldSpaceCameraPos.xyz - i.v3.xyz);
                // Right (1, 0, 0)  Up(0,1,0) 程序化眼球 代码输入
                float VDotRight = dot(view, _EyeRight.xyz);
                float VDotUp = dot(view, _EyeUp.xyz);
                float2 Vx_y = float2(VDotRight, -VDotUp); // 将view 投影到 眼球的（x， -y） 二维平面
                float V_xLen = VDotRight * VDotRight; // V 在x轴投影的长度平方 0~1
                float2 V_x3_y = float2(-VDotRight * V_xLen, -VDotRight); // 还是个 V 到（-x, -y）的二维平面投影，只是x做了个三次方映射
                float2 cookieUV = V_x3_y.xy * _CookieMoveSpeed1 * _CookieOffset2.xy + _CookieOffset.xy;
                cookieUV = float2(min(cookieUV.x, 0), max(cookieUV.y, 0)) + i.v5.xy;
                //v5 -> uv1
                cookieUV.y = 1 - cookieUV.y;
                float cookie = SAMPLE_TEXTURE2D(_CookieTex, sampler_CookieTex, cookieUV).x;

                float2 cookieUV2 = Vx_y.xy * _CookieMoveSpeed2 + _CookieOffset.xy + i.v5.xy;
                cookieUV2.y = 1 - cookieUV2.y;
                float cookie2 = SAMPLE_TEXTURE2D(_CookieTex, sampler_CookieTex, cookieUV2).y;

                float2 Vx_y_Parallex = Vx_y.xy * _Parallex;
                float2 cookieUV3 = abs(Vx_y.x) * _CookieMoveSpeed3 * _CookieOffset2.zw + _CookieOffset.zw + i.v5.xy;
                cookieUV3.y = 1 - cookieUV3.y;
                float cookie3 = SAMPLE_TEXTURE2D(_CookieTex, sampler_CookieTex, cookieUV3).z;

                Vx_y_Parallex *= 0.5;
                float cookieIntensity = dot(float3(cookie, cookie2, cookie3), _CookieIntensity.xyz);
                float2 nextParallexUV = i.v1.xy;
                //v1 -> uv0
                nextParallexUV.y = 1 - nextParallexUV.y;
                float2 parallex1 = SAMPLE_TEXTURE2D(_ParallexTex, sampler_ParallexTex, nextParallexUV).xy;
                nextParallexUV = Vx_y_Parallex * parallex1.xx + i.v1.xy;
                nextParallexUV.y = 1 - nextParallexUV.y;
                float parallex2 = SAMPLE_TEXTURE2D(_ParallexTex, sampler_ParallexTex, nextParallexUV).x;
                nextParallexUV = Vx_y_Parallex * parallex2.xx + i.v1.xy;
                nextParallexUV.y = 1 - nextParallexUV.y;
                float parallex3 = SAMPLE_TEXTURE2D(_ParallexTex, sampler_ParallexTex, nextParallexUV).x;
                nextParallexUV = Vx_y_Parallex * parallex3.xx + i.v1.xy;
                nextParallexUV.y = 1 - nextParallexUV.y;
                float3 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, nextParallexUV).xyz;
                float3 colorIntensity = color * color + cookieIntensity.xxx;
                // ？？？ 还得回去写一遍V才看得懂
                float3 rotZ = float3(UNITY_MATRIX_V[0].z, UNITY_MATRIX_V[1].z, UNITY_MATRIX_V[2].z);
                rotZ = normalize(rotZ);
                float3 wNormal = normalize(i.v2.xyz);
                float unknow = dot(wNormal, rotZ);
                float unknowRevert = max(1 - abs(unknow), 0); // 这个max可省
                float unknow2 = pow((3 - 2 * unknowRevert) * unknowRevert * unknowRevert, 6) * parallex1.y;
                float3 result = (unknow2 * _Rim + colorIntensity) * shadowColr;
                return float4(result, 1);
            }
            ENDHLSL
        }
    }
}