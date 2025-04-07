Shader "NPR/OutLineWithTex"
{
    Properties
    {
        _OutlineWidth ("描边宽度", Range(0, 5)) = 0.4
        _OutlineColor ("描边颜色", Color) = (0,0,0,1)
        _MaskTex2("Mask2", 2D) = "white"{}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        //LOD 100

        Pass
        {
            Name "OutLineWithTex"

            Cull Front

            CGPROGRAM
            
            #pragma vertex vertOutLine
            #pragma fragment fragOutLine
            #include "UnityCG.cginc"

            float _OutlineWidth;
            fixed4 _OutlineColor;
            sampler2D _MaskTex2;
            
            //#pragma multi_compile_fwdbase
            struct appdata_outLine
            {
                float4 pos: POSITION;
                float3 normal: Normal;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vertOutLine (appdata_outLine v)
            {
                v2f o;
                //fbx尺寸问题 normalize 一下
                float3 linePos = normalize(v.normal) * _OutlineWidth * 0.01 + v.pos;
                o.pos = UnityObjectToClipPos(linePos);
                o.uv = v.uv;
                return o;
            }

            fixed4 fragOutLine (v2f i) : SV_Target
            {
                fixed val = tex2D(_MaskTex2, i.uv).g - 0.1;
                if(int(val < 0.0) * -1 != 0){
                    discard;
                }
                return _OutlineColor;
            }

            ENDCG
        }
    }
}
