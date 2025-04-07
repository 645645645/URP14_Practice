//Create form unity 2019某个LTS build-in，不兼容是正常的，

Shader "NPR/NPC"
{
    Properties
    {
        _MainTex ("Base.RGB", 2D) = "white" {}
        _ShadowTex ("Shadow Color Tex.RGB", 2D) = "white" {}
        _MaskTex ("MaskTex.RGA", 2D) = "black" {}
        _MaskTex2 ("MaskTex2.B", 2D) = "red" {}
        _ToonMap ("过渡贴图.GB", 2D) = "black" {}
        _ShadowMap ("ShadowMap.R", 2D) = "black" {}
        _ShadowMapTex("灯光阴影贴图", 2D) = "white"{}
        _MaskSpecularIntensity("高光遮罩强度", Range(0,2)) = 1.0
        _SpecularRimMask("边缘受环境光影响", Range(0,1)) = 0.5
        _FlowMask("FlowMask", Float) = 0.0
        _RimColor("边缘颜色", Color) = (1.5,1.5,1.5,1.5)
        _RoleAmbientColorBright("亮部受环境光影响.RGB", Color) = (1,1,1,1)
        _RoleAmbientColor("暗部受环境光影响.RGB", Color) = (1,1,1,1)
        _ShadowMapParam("ShadowMapParam.RGA", Vector) = (1,1,1,1)
        //_LightShadowData("_LightShadowData.R", Vector) = (0.9,1,1,1)
        _OutlineWidth ("描边宽度", Range(0, 5)) = 0.4
        _OutlineColor ("描边颜色", Color) = (0,0,0,1)
        _OutlineLerp ("描边宽度随摄像机距离变化", Range(0, 1)) = 0.1
        _OutlineLerpOffset ("描边宽度随摄像机距离变化偏移", Range(0, 1)) = 0.0
        _MipmapBiass("MipmapBiass", Range(0,10)) = 0.0
    }
    SubShader
    {
        //Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        
        Tags { "RenderType"="Opaque" }
        //LOD 100

        UsePass "NPR/OutLineWithTex/OutLineWithTex"

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work
            //#pragma multi_compile_fog

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"
            #pragma multi_compile_fwdbase
            //#pragma multi_compile _ SHADOWS_SCREEN

            struct appdata
            {
                float4 pos: POSITION;
                float3 normal: Normal;
                float2 uv: TEXCOORD0;
                //float3 texcoord1: TEXCOORD1;
                //float3 texcoord2: TEXCOORD2;
                //float3 texcoord3: TEXCOORD3;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                //float3 normal: Normal;
                float2 uv: TEXCOORD0;
                float4 worldPos: TEXCOORD1;
                float3 worldNormal: TEXCOORD2;
                float3 rootWorldPos: TEXCOORD3;
                float3 texcoord4: TEXCOORD4;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _ShadowTex;
            sampler2D _MaskTex;
            sampler2D _MaskTex2;
            sampler2D _ShadowMap;
            sampler2D _ToonMap;
            sampler2D _ShadowMapTex;
            uniform float _MaskSpecularIntensity;
            uniform float4 _ShadowMapParam;
            uniform float4 _RoleAmbientColorBright;
            uniform float4 _RoleAmbientColor;
            uniform float _BloomAlpha;
            uniform float _BloomAlphaLerp;
            uniform float4 _RimColor;
            uniform float _SpecularRimMask;
            uniform float _MipmapBiass;
            uniform float _FlowMask;
            int xlati0;
            int xlati9;
            float xlat0;
            float4 xlat1;
            float3 xlat3;
            float3 xlat4;
            float3 xlat6;
            float3 xlat9;
            float xlat28;
            float xlat30;
            float2 xlat6_0;
            float3 BaseColor;
            float3 xlat6_1;
            float3 xlat6_2;
            float3 xlat6_4;
            float3 xlat6_5;
            float3 xlat6_7;
            float3 xlat6_8;
            float3 xlat6_9;
            float3 xlat6_11;
            float xlat6_14;
            float xlat6_29;
            static bool xlatB0;
            static bool xlatB1;
            static bool xlatB9;
            static bool xlatB28;
            static bool xlatB30;


            v2f vert (appdata v)
            {
                v2f o;
                //o.pos = UnityObjectToClipPos(v.pos);
                float4 worldPos = mul(unity_ObjectToWorld, v.pos);
                //o.texcoord1 = worldPos;
                //o.texcoord1 = (unity_ObjectToWorld[3].xyz * v.pos.www) + worldPos.xyz - unity_ObjectToWorld[3].xyz;
                //float4 clipPos = mul(UNITY_MATRIX_VP, worldPos);
                o.pos = mul(UNITY_MATRIX_VP, worldPos);
                o.uv = v.uv;
                o.worldPos = worldPos;
                o.worldNormal = normalize(mul(v.normal.xyz, (float3x3)unity_WorldToObject)).xyz;
                o.rootWorldPos = unity_ObjectToWorld[3].xyz;
                o.texcoord4 = mul(unity_WorldToShadow[0], worldPos);
                //unity_WorldToLight
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                //_ShadowMapParam = ( 地图原点水平坐标X, 地图原点水平坐标Y, 地图尺寸, 阴影遮罩值 ) 
                //（7.7, -2.3, 144.6000061 , 0.85 ）
                // 安全处理..
                uint tmp1 = 0.0 < _ShadowMapParam.z ? 0xFFFFFFFFu : (uint)0;
                // 0xFFFFFFFFu  4294967295 || 0
                uint tmp2 = _ShadowMapParam.z < 0.0 ? 0xFFFFFFFFu : (uint)0;
                
                // 0 || 2
                int xlati0 = (int) tmp1 - (int) tmp2 + 1;
                //转UV
                float2 xlat13 = (i.rootWorldPos.xz - _ShadowMapParam.xy)/_ShadowMapParam.zz;
                //场景地图明暗的 贴图
                bool outShadow = tex2D(_ShadowMap, xlat13.xy).r >= 0.5f;// 1 || 0
                outShadow = int(uint(((uint(outShadow)* 0xFFFFFFFFu)| uint(xlati0)))) != 0;
                // 1 || _ShadowMapParam.w（w为阴影遮罩值） 压暗整体
                float shadowCover = lerp(_ShadowMapParam.w, 1.0 , outShadow);

                //SSSColor   = sss.rgb * BaseColor
                float3 sss = tex2D(_ShadowTex, i.uv).rgb;//暗部由shadowTex控制
                //BaseColor
                BaseColor.xyz = tex2Dlod(_MainTex, float4(i.uv.x, i.uv.y, 0.0, _MipmapBiass)).xyz;
                float3 sssColor = sss * BaseColor.rgb * _RoleAmbientColor.rgb * shadowCover;
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - i.worldPos.xyz);
                float3 halfDir = normalize(viewDir + lightDir);
                float3 worldNormal = normalize(i.worldNormal.xyz);
                float nh = saturate(dot(halfDir.xyz , worldNormal.xyz));
                //xlat30 = (nh >= _SpecularRimMask) ? 1 : 0;
                //xlat28 = nh * xlat30;
                nh = lerp(0, nh, nh >= _SpecularRimMask);
                xlat30 = nh * _MaskSpecularIntensity;
                float3 ilmColor = tex2D(_MaskTex, i.uv).rgb;
                xlat28 = nh * ilmColor.b;
                xlat28 = (xlat28 >= _FlowMask) * xlat30;
                xlat6.xyz = BaseColor.xyz * xlat28;
                xlat6_7.xyz = BaseColor.xyz * _LightColor0.xyz + xlat6.xyz;
                xlat6_8.xyz = BaseColor.xyz * _RimColor.xyz;
                xlat1.xyz = xlat6_7.xyz * _RoleAmbientColorBright.xyz;
                xlat6_7.xyz = xlat1.xyz * shadowCover - sssColor.xyz;
                xlat6_29 = (ilmColor.y >= 0.9) ? 1 : 0;
                xlat6_29 += ilmColor.y;
                xlat6_29 = min(xlat6_29, 1);
                xlat6_2.x = dot(worldNormal, lightDir);
                xlat6_2.x = max(xlat6_2, 0);
                xlat6_1.xy = tex2D(_ToonMap, xlat6_2.xx).xw;
                xlat6_2.x = xlat6_1.y - xlat6_1.x;
                xlat6_2.x = xlat6_2.x * ilmColor.x + xlat6_1.x;
                xlat6_2.x = xlat6_29 * xlat6_2.x;
                float4 vec = float4(i.texcoord4.x, i.texcoord4.y, i.texcoord4.z, 0);
                //(_u_xlat6_1.x = gl_texture2D_comparisonLod(_hlslcc_zcmp_ShadowMapTexture, vec, 0.0));
                //xlat6_1.x = tex2Dcmplod(_ShadowMapTex, vec, 0.0);
                xlat6_1.x = tex2D(_ShadowMapTex, vec.xy) >= vec.z;
                // _LightShadowData.x 0.9 阴影强度
                xlat6_11.x = saturate(1 - (1 - lerp(_LightShadowData.x, 1.0, xlat6_1.x)) * 8);
                xlat6_2.x = xlat6_11.x * xlat6_2.x;
                xlat6_11.xyz = xlat6_2.x * xlat6_7.xyz + sssColor.xyz;
                xlat6_7.xyz = xlat6_8.xyz * shadowCover - xlat6_11.xyz;
                xlat6_14 = 1 - saturate(dot(xlat3.xyz , worldNormal.xyz));
                xlat6_0.x = tex2D(_ToonMap, float2(xlat6_14,xlat6_14)).z;
                xlat1 = (_WorldSpaceCameraPos.yxzy - i.rootWorldPos.yxzy);
                xlat9.x = dot(xlat1.yzw, xlat1.yzw);
                xlat9.x = rsqrt(xlat9.x);
                xlat1 = xlat9.x * xlat1;
                xlat9.xy = xlat1.zw * float2(1.0, 0.0);
                xlat9.xy = xlat1.xy * float2(0.0, 1.0) - xlat9.xy;
                xlat6_5.x = max(dot(xlat6_5.xz, xlat9.xy), 0);
                xlat6_9.x = tex2D(_ToonMap, xlat6_9.xx).y;
                xlat6_5.x = xlat6_9.x * xlat6_0.x;
                xlat6_0.xy = tex2D(_MaskTex2, i.uv).xz;
                xlat6_5.x = xlat6_0.x * xlat6_5.x;
                xlat6_14 = saturate(1 - xlat6_0.x);
                xlat0 = xlat6_14 * _BloomAlpha - 1;
                xlat0 = _BloomAlphaLerp * xlat0 + 1;
                xlat6_2.x = xlat6_2.x * xlat6_5.x;


                float4 col = (1,1,1,1);
                col.rgb = xlat6_2.x * xlat6_7.xyz + xlat6_11.xyz;
                col.a = xlat0;
                return col;
            }
            ENDCG
        }
    }
}
