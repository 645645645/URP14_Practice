Shader "_URPForward/Leaves"
{
    Properties
    {
        [NoScaleOffset]_MainTex ("Texture", 2D) = "white" {}
        _ZBias("ZOffset", Range(-50, 10)) = 0.1
    }
    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
        }
        LOD 100

        Pass
        {
            Name "Leaves"
            Cull Off
            //            Offset [_ZBias], -10
            HLSLPROGRAM
            #pragma enable_d3d11_debug_symbols
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #pragma shader_feature_local_vertex _ _INDIRECTDRAWON
            #pragma shader_feature_local_vertex _ _COMPACTNORMALUV

            struct Attributes
            {
                float3 positionOS : POSITION;
                
#ifdef _COMPACTNORMALUV
                half4 normalOS : NORMAL;
#else
                half2 normalOS : NORMAL;
                half2 uv : TEXCOORD0;
#endif
                
            };


            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD1;
                half2 uv : TEXCOORD0;
            };

            TEXTURE2D_X(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half _ZBias;
            CBUFFER_END

            inline float2 UnitVectorToOctahedron(float3 N)
            {
                float2 Oct = N.xy;
                if (N.z < 0)
                {
                    Oct = (1 - abs(N.yx)) * float2(N.x >= 0 ? 1 : -1, N.y >= 0 ? 1 : -1);
                }
                return Oct;
            }

            inline float3 OctahedronToUnitVector(float2 Oct)
            {
                float3 N = float3(Oct, 1 - dot(1, abs(Oct)));
                if (N.z < 0)
                {
                    N.xy = (1 - abs(N.yx)) * (N.xy >= 0 ? float2(1, 1) : float2(-1, -1));
                }
                return normalize(N);
            }

            #ifdef _INDIRECTDRAWON

            StructuredBuffer<float3> _Positions;
            StructuredBuffer<float3> _Normals;
            StructuredBuffer<float2> _UVs;//pc只有float
            
            Varyings vert(uint svVertexID: SV_VertexID, uint svInstanceID : SV_InstanceID)
            {
                Varyings output;
                // uint vertexID = _Triangles[svVertexID];
                uint vertexID = svVertexID + svInstanceID * 4;
                
                float3 posWS = _Positions[vertexID];
                float3 normalWS = (_Normals[vertexID]);
                half2 uv = _UVs[vertexID];
                float4 clipPos = TransformWorldToHClip(posWS);
                
            #if UNITY_REVERSED_Z
                    clipPos.z += _ZBias;
            #else
                    clipPos.z -= _ZBias;
            #endif
                
                output.positionCS = clipPos;
                output.normalWS = normalWS;
                output.uv = uv;
                
                return output;
            }
            #else

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 normalWS = OctahedronToUnitVector(input.normalOS.xy * 2 - 1);
                float3 posWS = input.positionOS.xyz;
                // float3 posVS = TransformWorldToView(posWS);
                // posVS.z += _ViewZBasic;
                // output.positionCS = TransformWViewToHClip(posVS);
                float4 clipPos = TransformWorldToHClip(posWS);

                #if UNITY_REVERSED_Z
                clipPos.z += _ZBias;
                #else
                    clipPos.z -= _ZBias;
                #endif

                output.positionCS = clipPos;
                output.normalWS = normalWS;

                #ifdef  _COMPACTNORMALUV
                output.uv = input.normalOS.zw;
                #else
                output.uv = input.uv;
                #endif

                return output;
            }
            #endif

            half4 frag(Varyings input, bool frontFacing : SV_IsFrontFace) : SV_Target
            {
                half4 base = SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, input.uv);
                clip(base.a - 0.9);
                Light mainLight = GetMainLight();
                half3 lightColor = mainLight.color;
                half3 lightDirWS = mainLight.direction;
                half3 normalWS = normalize(input.normalWS);
                normalWS *= frontFacing ? 1 : -1;
                half nl = dot(normalWS, lightDirWS);
                half halfLambert = mad(nl, 0.5, 0.5);
                half4 col = half4(lightColor * base.rgb * halfLambert.xxx, 1);
                // col.rgb = normalWS;
                return col;
            }
            ENDHLSL
        }
    }
}