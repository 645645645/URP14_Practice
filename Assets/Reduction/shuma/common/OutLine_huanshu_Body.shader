
Shader "NPR/OutLine_HuanShu_Body"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _OutlineWidth ("描边宽度", Range(0, 5)) = 0.025
        _OutlineColor ("描边颜色", Color) = (0,0,0,1)
        _DiffuseX("???", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            Name "OUTLINE"

            Cull Front
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _OutlineColor;
            float _OutlineWidth;
            //网易自研特色？ 
            //材质的diffuse（float4）是像pos normal一样通过appdata传进来的
            float _DiffuseX;

            v2f vert (appdata_base v)
            {
                v2f o;
                float3 normal = normalize(v.normal);
                float diffuseX = _DiffuseX;
                float4 worldPos = mul(unity_ObjectToWorld, v.vertex);
                float len = length(_WorldSpaceCameraPos.xyz - worldPos);
                // _View[1][1] = 0.9965421
                float width = max(min(sqrt(len / UNITY_MATRIX_V[1][1]) * _OutlineWidth * 0.5 * diffuseX, 0.02), 0);
                float3 offset = width * normal + v.vertex.xyz;
                float4 resultPos = float4(offset, v.vertex.w);
                o.vertex = UnityObjectToClipPos(resultPos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                //fixed4 col = _OutlineColor;
                fixed4 col = float4(0.36, 0.2, 0.2, 0);
                col.rgb = max(col.rgb, 0.001);
                //???
                col.rgb = pow(col.rgb, 2.2);
                float4 _AddColor = (1, 1, 1, 1);
                return col * _AddColor;
            }
            ENDCG
        }
    }
}
