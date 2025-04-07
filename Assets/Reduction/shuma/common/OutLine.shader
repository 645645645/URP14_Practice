Shader "NPR/OutLine"
{
    Properties
    {
        _OutlineWidth ("描边宽度", Range(0, 5)) = 0.4
        _OutlineColor ("描边颜色", Color) = (0,0,0,1)
        _OutlineLerp ("描边宽度随摄像机距离变化", Range(0, 1)) = 0.1
        _OutlineLerpOffset ("描边宽度随摄像机距离变化偏移", Range(0, 1)) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        //LOD 100

        Pass
        {
            Name "OUTLINE"

            Cull Front

            CGPROGRAM
            
            #pragma vertex vertOutLine
            #pragma fragment fragOutLine
            #include "UnityCG.cginc"

            float _OutlineWidth;
            float _OutlineLerp;
            float _OutlineLerpOffset;
            fixed4 _OutlineColor;
            
            //#pragma multi_compile_fwdbase
            struct appdata_outLine
            {
                float4 pos: POSITION;
                float3 normal: Normal;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vertOutLine (appdata_outLine v)
            {
                v2f o;
                float viewLen = length(_WorldSpaceCameraPos.xyz - unity_ObjectToWorld[3].xyz);
                float width = (_OutlineLerpOffset + _OutlineLerp) * (viewLen - 1) * _OutlineWidth + _OutlineWidth;
                //float width = (_OutlineLerpOffset + _OutlineLerp + 1.4) * _OutlineWidth;
                float3 linePos = width * v.normal * 0.01 + v.pos.xyz;
                float4 resultPos = float4(linePos, v.pos.w);
                o.pos = UnityObjectToClipPos(resultPos);
                return o;
            }

            fixed4 fragOutLine (v2f i) : SV_Target
            {
                return _OutlineColor;
            }

            ENDCG
        }
    }
}
