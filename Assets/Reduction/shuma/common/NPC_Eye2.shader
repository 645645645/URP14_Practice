Shader "NPR/NPC_Eye_2"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Bloom("Bloom", Range(0,1)) = 0.1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        //LOD 100

        Pass
        {
            //Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"


            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 worldNormal: TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Bloom;

            v2f vert (appdata_base v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.worldNormal = normalize(mul(v.normal.xyz, (float3x3)unity_WorldToObject)).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                //大概是默认人物在原点了
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float nl = saturate(dot(i.worldNormal, lightDir));
                fixed4 col = tex2D(_MainTex, i.uv);
                col.rgb *= (nl * (col.w * _Bloom + 1.0));
                col.w = 1;
                return col;
            }
            ENDCG
        }
    }
}
