// Made with Amplify Shader Editor
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "SVTX/Vertex Color"
{
    Properties
    {
        [HideInInspector] __dirty( "", Int ) = 1
    }

    SubShader
    {
        Pass
        {
            Tags
            {
                "RenderType" = "Opaque" "Queue" = "Geometry+0" "IsEmissive" = "true"
            }
            Cull Back
            HLSLPROGRAM
            // #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
                float4 uv3 : TEXCOORD3;
                float4 uv4 : TEXCOORD4;
                float4 uv5 : TEXCOORD5;
                float4 uv6 : TEXCOORD6;
                float4 uv7 : TEXCOORD7;
                float4 vertexColor : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
                float4 uv3 : TEXCOORD3;
                float4 uv4 : TEXCOORD4;
                float4 uv5 : TEXCOORD5;
                float4 uv6 : TEXCOORD6;
                float4 uv7 : TEXCOORD7;
                float4 vertexColor : TEXCOORD9;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.vertexColor = input.vertexColor;
                output.uv0 = input.uv0;
                output.uv1 = input.uv1;
                output.uv2 = input.uv2;
                output.uv3 = input.uv3;
                output.uv4 = input.uv4;
                output.uv5 = input.uv5;
                output.uv6 = input.uv6;
                output.uv7 = input.uv7;

                return output;
            }

            
            CBUFFER_START(UnityPerMaterial)
            int _PreviewLayer;
            int _PreviewChannel;
            CBUFFER_END

            float4 frag(Varyings input) : SV_Target
            {
                bool oneChannel = _PreviewChannel >= 0 && _PreviewChannel < 3.5;
                switch (_PreviewLayer)
                {
                    case 0:
                        return oneChannel ? input.uv0[_PreviewChannel] : input.uv0;
                    case 1:
                        return oneChannel ? input.uv1[_PreviewChannel] : input.uv1;
                    case 2:
                        return oneChannel ? input.uv2[_PreviewChannel] : input.uv2;
                    case 3:
                        return oneChannel ? input.uv3[_PreviewChannel] : input.uv3;
                    case 4:
                        return oneChannel ? input.uv4[_PreviewChannel] : input.uv4;
                    case 5:
                        return oneChannel ? input.uv5[_PreviewChannel] : input.uv5;
                    case 6:
                        return oneChannel ? input.uv6[_PreviewChannel] : input.uv6;
                    case 7:
                        return oneChannel ? input.uv7[_PreviewChannel] : input.uv7;
                    default:
                        return oneChannel ? input.vertexColor[_PreviewChannel] : input.vertexColor;
                }
                // return input.vertexColor;
            }
            ENDHLSL
        }
    }
    Fallback "Diffuse"
}