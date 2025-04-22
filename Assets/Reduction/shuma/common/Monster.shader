// 数码是gamma空间的
Shader "NPR/Monster"
{
    Properties
    {
        _MainTex ("Base.RGB", 2D) = "white" {}
        _ShadowTex ("SSS.RGB", 2D) = "white" {}
        _MaskTex ("ILM.RGA", 2D) = "black" {}
        _BloomAlphaMask ("AlphaMask.B", 2D) = "red" {}
        _Matcap ("Matcap.RGBA", 2D) = "white" {}
        _ToonMap ("亮度过渡.GB", 2D) = "black" {}
        _ShadowMap ("地图阴影区域.R", 2D) = "black" {}
        _Intensity("整体强度", Range(0,2)) = 1.0
        _RimColor("边缘颜色", Color) = (1.5,1.5,1.5,1.5)
        _RoleAmbientRim("边缘受环境光强度", Range(0,1)) = 1
        _RoleAmbientColorBright("亮部受环境光影响.RGB", Color) = (1,1,1,1)
        _RoleAmbientColor("暗部受环境光影响.RGB", Color) = (1,1,1,1)
        _ShadowMapParam("ShadowMapParam", Vector) = (1,1,1,1)
        _OutlineWidth ("描边宽度", Range(0, 5)) = 0.4
        _OutlineColor ("描边颜色", Color) = (0,0,0,1)
        _OutlineLerp ("描边宽度随摄像机距离变化", Range(0, 1)) = 0.1
        _OutlineLerpOffset ("描边宽度随摄像机距离变化偏移", Range(0, 1)) = 0.0
        _BloomAlpha("BloomAlpha", Range(0,1)) = 1.0
        _BloomAlphaLerp("BloomAlphaLerp", Range(0,1)) = 0.0
        _MipmapBiass("MipmapBiass", Range(0,10)) = 0.0
        //        _DiffuseX("???", Range(0, 1)) = 0.2
    }
    SubShader
    {
        //SceneView相机没写深度，找不到原因，先注释
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "LightMode" = "UniversalForward"
            "Queue" = "Geometry"
        }
        ZTest LEqual
        ZWrite On
        Cull Back


        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.danbaidong.smoothnormal/Shaders/CompressTools.hlsl"

        struct appdata_shadowCaster
        {
            float4 pos: POSITION;
            // float3 normal: Normal;
        };

        struct appdata_outLine
        {
            float4 pos: POSITION;
            float3 normal: Normal;
            float4 tangent: TANGENT;
            float4 uv : TEXCOORD0;
        };

        struct appdata_base
        {
            // positionOS 变量包含对象空间中的顶点
            float4 vertex : POSITION;
            // 声明包含每个顶点的法线矢量的
            float3 normal : NORMAL;
            float4 tangent : TANGENT;
            // uv 变量包含给定顶点的纹理上的
            float2 uv : TEXCOORD0;
        };

        struct v2f_shadowCaster
        {
            float4 pos : SV_POSITION;
            float2 depth : TEXCOORD0;
        };

        struct v2f_outLine
        {
            float4 pos : SV_POSITION;
        };

        struct v2f
        {
            float4 pos : SV_POSITION;
            float4 texcoord0: TEXCOORD0;
            float3 worldPos: TEXCOORD1;
            float3 worldNormal: TEXCOORD2;
            float3 rootWorldPos: TEXCOORD3;
        };

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_ShadowTex);
        SAMPLER(sampler_ShadowTex);
        TEXTURE2D(_MaskTex);
        SAMPLER(sampler_MaskTex);
        TEXTURE2D(_BloomAlphaMask);
        SAMPLER(sampler_BloomAlphaMask);
        TEXTURE2D(_ShadowMap);
        SAMPLER(sampler_ShadowMap);
        TEXTURE2D(_Matcap);
        SAMPLER(sampler_Matcap);
        TEXTURE2D(_ToonMap);
        SAMPLER(sampler_ToonMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _ShadowMapParam;
            float4 _RoleAmbientColorBright;
            float4 _RoleAmbientColor;
            float4 _RimColor;
            float4 _OutlineColor;
            float _Intensity;
            float _BloomAlpha;
            float _BloomAlphaLerp;
            float _RoleAmbientRim;
            float _MipmapBiass;
            float _OutlineWidth;
            float _OutlineLerp;
            float _OutlineLerpOffset;
        CBUFFER_END

        v2f_shadowCaster vertShadowCaster(appdata_shadowCaster v)
        {
            v2f_shadowCaster o;
            o.pos = TransformObjectToHClip(v.pos.xyz);
            o.depth = o.pos.zw;
            return o;
        }

        v2f_outLine vertOutLine(appdata_outLine v)
        {
            v2f_outLine o;
            float viewLen = length(_WorldSpaceCameraPos.xyz - unity_ObjectToWorld[3].xyz);
            float width = (_OutlineLerpOffset + _OutlineLerp) * (viewLen - 1) * _OutlineWidth + _OutlineWidth;
            
            float3 normal = OctahedronToUnitVector(v.uv.zw);
            float3x3 TBN = float3x3(
                v.tangent.xyz,
                cross(v.normal, v.tangent.xyz)* v.tangent.w,
                v.normal);
            normal = mul(normal,TBN);
            
            float3 linePos = width * normal * 0.01 + v.pos.xyz;
            // float3 linePos = width * v.normal * 0.01 + v.pos.xyz;
            // float4 resultPos = float4(linePos, v.pos.w);
            o.pos = TransformObjectToHClip(linePos);
            return o;
        }

        half4 fragShadowCaster(v2f_shadowCaster i) : SV_Target
        {
            float depth = i.depth.x / i.depth.y;
            #if defined (SHADER_TARGET_GLSL)
			depth = depth*0.5 + 0.5; //(-1, 1)-->(0, 1)
            #elif defined (UNITY_REVERSED_Z)
            depth = 1 - depth; //(1, 0)-->(0, 1)
            #endif
            return depth;
        }

        half4 fragOutLine(v2f_outLine i) : SV_Target
        {
            return _OutlineColor;
        }

        v2f vertNPRMatcap(appdata_base v)
        {
            v2f o;
            float4 worldPos = mul(unity_ObjectToWorld, v.vertex);
            o.worldPos = worldPos.xyz;
            o.pos = mul(UNITY_MATRIX_VP, worldPos);
            //Matcap 
            //MV transforms points from object to eye space
            //IT_MV rotates normals from object to eye space
            float3 eyeNormal = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, v.normal.xyz));
            eyeNormal.xy = eyeNormal.xy * float2(0.5, 0.5) + float2(0.5, 0.5);
            o.texcoord0.zw = eyeNormal.xy;
            // o.texcoord0.xy = TRANSFORM_TEX(v.uv, _MainTex);
            o.texcoord0.xy = (v.uv);
            o.worldNormal = normalize(TransformObjectToWorldNormal(v.normal.xyz));
            o.rootWorldPos = unity_ObjectToWorld[3].xyz;
            return o;
        }

        half4 fragNPRMatcap(v2f i) : SV_Target
        {
            // 安全处理..
            uint tmp1 = 0.0 < _ShadowMapParam.z ? 0xFFFFFFFFu : (uint)0;
            // 0xFFFFFFFFu  4294967295 || 0
            uint tmp2 = _ShadowMapParam.z < 0.0 ? 0xFFFFFFFFu : (uint)0;

            // 0 || 2
            int xlati0 = (int)tmp1 - (int)tmp2 + 1;

            float2 xlat13 = (i.rootWorldPos.xz - _ShadowMapParam.xy) / _ShadowMapParam.zz;

            bool outShadow = SAMPLE_TEXTURE2D(_ShadowMap, sampler_ShadowMap, xlat13.xy).r >= 0.5f; // 1 || 0
            outShadow = int(uint(((uint(outShadow) * 0xFFFFFFFFu) | uint(xlati0)))) != 0;

            float shadowCoverValue = lerp(_ShadowMapParam.w, 1.0, outShadow);

            float3 BaseColor = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, i.texcoord0.xy, _MipmapBiass).xyz;
            float3 sssColor = SAMPLE_TEXTURE2D(_ShadowTex, sampler_ShadowTex, i.texcoord0.xy).xyz;
            float3 ILMColor = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.texcoord0.xy).rga;
            float4 matcap = SAMPLE_TEXTURE2D(_Matcap, sampler_Matcap, i.texcoord0.zw);
            //提高亮部颜色
            float4 matcap2 = 2 * matcap;

            float3 sssColor_Dark = 0.8 * sssColor * _RoleAmbientColor.rgb;
            float3 lightColor = _RoleAmbientColorBright.rgb;
            float3 ambient = _MainLightColor.rgb;
            float3 rimColor = _RimColor.rgb * _RoleAmbientRim;

            float3 matcapLerp_R = lerp(sssColor_Dark, lightColor, matcap2.r);
            float3 matcapLerp_G = lerp(sssColor_Dark, lightColor, matcap2.g);
            float3 matcapLerp_B = lerp(sssColor_Dark, lightColor, matcap2.b);
            float3 matcapLerp_A = lerp(sssColor_Dark, lightColor, matcap2.a);

            //视空间 颜色分层
            float3 matcapAmbient_R = lerp(matcapLerp_R, (matcap.rrr + ambient), matcap.r >= 0.9);
            float3 matcapAmbient_G = lerp(matcapLerp_G, (matcap.ggg + ambient), matcap.g >= 0.9);
            float3 matcapAmbient_B = lerp(matcapLerp_B, (matcap.bbb + ambient), matcap.b >= 0.9);
            float3 matcapAmbient_A = lerp(matcapLerp_A, (matcap.aaa + ambient), matcap.a >= 0.9);

            float3 ilm_r_01 = lerp(matcapAmbient_A, matcapAmbient_B, ILMColor.r >= 0.1);
            float3 ilm_r_05 = lerp(ilm_r_01, matcapAmbient_G, ILMColor.r >= 0.5);
            float3 ilm_r_09 = lerp(ilm_r_05, matcapAmbient_R, ILMColor.r >= 0.9);

            float ilm_g = lerp(ILMColor.g, 1.0, ILMColor.g >= 0.9); //可配置明暗调色的分层 
            float3 ilm_g_09 = lerp(sssColor_Dark, _Intensity * ilm_r_09, ilm_g);

            //toonmap 调整边缘颜色
            float3 worldNormal = normalize(i.worldNormal.xyz);
            float3 viewRootDir = normalize(_WorldSpaceCameraPos.xyz - i.rootWorldPos.xyz);
            // -------------------------
            float2 viewRootDir_zx = float2(-1, 1) * viewRootDir.zx;
            float nv_corss = max(dot(worldNormal.xz, viewRootDir_zx.xy), 0);
            float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - i.worldPos.xyz);
            //取反后 viewRootDir 与 worldNormal 越相似 nv越小
            float nv_dotRevert = 1 - saturate(dot(viewDir.xyz, worldNormal.xyz));

            //弧面水平左侧 该值更小 //注意这个view取的rootWorldPos，只随 相机位置 和 rootPos改变
            float toonmap_g = SAMPLE_TEXTURE2D(_ToonMap, sampler_ToonMap, float2(nv_corss, nv_corss)).g;
            //弧面中心 该值越小 
            float toonmap_b = SAMPLE_TEXTURE2D(_ToonMap, sampler_ToonMap, float2(nv_dotRevert, nv_dotRevert)).b;

            float f = toonmap_g * toonmap_b * ILMColor.b * ilm_g;

            half4 col = half4(1, 1, 1, 1);
            col.rgb = lerp(ilm_g_09, rimColor, f) * BaseColor * shadowCoverValue;
            //col.rgb = ilm_g_09 * BaseColor * shadowCoverValue;

            float mask2_b = SAMPLE_TEXTURE2D(_BloomAlphaMask, sampler_BloomAlphaMask, i.texcoord0.xy).b;
            col.a = _BloomAlphaLerp * (saturate(1.0 - mask2_b) * _BloomAlpha - 1.0) + 1.0;
            return col;
        }
        ENDHLSL

        Pass
        {
            name "NPR_MATCAP"
            Tags
            {
                "Queue"="opaque + 10"
                // SRPDefaultUnlit 和 UniversalForward 的顺序 看urp版本...
                // 同屏角色少无所谓，多角色不应该这样做，batch全断了, so 这俩pass别一起用
                // outLineFeaturePass 或者 附加OnlyOutLineMaterial
                //                "LightMode" = "SRPDefaultUnlit"
                "LightMode" = "UniversalForward"
            }
            //            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vertNPRMatcap
            #pragma fragment fragNPRMatcap
            ENDHLSL
        }

        Pass
        {
            name "OUTLINE"
            Tags
            {
                "Queue"="opaque"
                //                "LightMode" = "UniversalForward"
                "LightMode" = "SRPDefaultUnlit"
            }
            offset 40, 2
            ZWrite On
            ZTest LEqual
            Cull Front

            HLSLPROGRAM
            #pragma vertex vertOutLine
            #pragma fragment fragOutLine
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags
            {
                "LightMode" = "DepthOnly"
            }

            // -------------------------------------
            // Render State Commands
            ZWrite On
            ColorMask R
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            // -------------------------------------
            // Shader Stages
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Tags
            {
                "LightMode" = "MyShadowCaster"
            }
            //            ZWrite On
            //            ZTest LEqual
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vertShadowCaster
            #pragma fragment fragShadowCaster
            ENDHLSL
        }

    }

    Fallback "Diffuse"
}