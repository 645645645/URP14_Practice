Shader "Hidden/Custom/KuwaharaFilter"
{
    HLSLINCLUDE
    // #pragma exclude_renderers gles
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    uniform float4 _KuwaharaFilterParams;

    float4 _BlitTexture_TexelSize;

    #define _BlurRadius          _KuwaharaFilterParams.x

    #define SampleColor(uv) SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb

    #define SumKernel half4(1, 1, 1, 1)

    #define Pow2(x) x * x

    struct VaryingsKuwahara
    {
        float4 positionCS : SV_POSITION;
        float4 texcoord : TEXCOORD0;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    VaryingsKuwahara VertKuwahara(Attributes input)
    {
        VaryingsKuwahara output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

        #if SHADER_API_GLES
        float4 pos = input.positionOS;
        float2 uv  = input.uv;
        #else
        float4 pos = GetFullScreenTriangleVertexPosition(input.vertexID);
        float2 uv = GetFullScreenTriangleTexCoord(input.vertexID);
        #endif

        output.positionCS = pos;
        // output.texcoord.xy = uv * _BlitScaleBias.xy + _BlitScaleBias.zw;
        output.texcoord.xy = uv;
        output.texcoord.zw = _BlitTexture_TexelSize.xy * _BlurRadius.xx;
        return output;
    }

    //基础版
    half4 FragmentBase(VaryingsKuwahara input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        const float2 uv0 = input.texcoord.xy;

        const float4 step = input.texcoord.zwzw;

        //先读横纵方向的点，缓存
        const float4 sample_L = mad(step, float4(-1, 0, -2, 0), uv0.xyxy);
        const float4 sample_R = mad(step, float4(1, 0, 2, 0), uv0.xyxy);
        const half3 l1 = SampleColor(sample_L.xy);
        const half3 l2 = SampleColor(sample_L.zw);
        const half3 r1 = SampleColor(sample_R.xy);
        const half3 r2 = SampleColor(sample_R.zw);


        const float4 sample_U = mad(step, float4(0, 1, 0, 2), uv0.xyxy);
        const float4 sample_B = mad(step, float4(0, -1, 0, -2), uv0.xyxy);
        const half3 u1 = SampleColor(sample_U.xy);
        const half3 u2 = SampleColor(sample_U.zw);
        const half3 b1 = SampleColor(sample_B.xy);
        const half3 b2 = SampleColor(sample_B.zw);

        float4 sample_Line1 = mad(step, float4(0, 1, 0, 2), sample_L);
        float4 sample_Line2 = mad(step, float4(0, 1, 0, 2), sample_Line1);

        //大结构 mul 编译后全拆成单行 fak
        const half4x3 lu = half4x3(
            SampleColor(sample_Line1.xy),
            SampleColor(sample_Line1.zw),
            SampleColor(sample_Line2.xy),
            SampleColor(sample_Line2.zw)
        );

        sample_Line1 = mad(step, float4(0, 1, 0, 2), sample_R);
        sample_Line2 = mad(step, float4(0, 1, 0, 2), sample_Line1);

        const half4x3 ru = half4x3(
            SampleColor(sample_Line1.xy),
            SampleColor(sample_Line1.zw),
            SampleColor(sample_Line2.xy),
            SampleColor(sample_Line2.zw)
        );

        sample_Line1 = mad(step, float4(0, -1, 0, -2), sample_R);
        sample_Line2 = mad(step, float4(0, -1, 0, -2), sample_Line1);

        const half4x3 rb = half4x3(
            SampleColor(sample_Line1.xy),
            SampleColor(sample_Line1.zw),
            SampleColor(sample_Line2.xy),
            SampleColor(sample_Line2.zw)
        );

        sample_Line1 = mad(step, float4(0, -1, 0, -2), sample_L);
        sample_Line2 = mad(step, float4(0, -1, 0, -2), sample_Line1);

        const half4x3 lb = half4x3(
            SampleColor(sample_Line1.xy),
            SampleColor(sample_Line1.zw),
            SampleColor(sample_Line2.xy),
            SampleColor(sample_Line2.zw)
        );

        const float4x3 edgeLU = float4x3(l1, l2, u1, u2);
        const float4x3 edgeRU = float4x3(r1, r2, u1, u2);
        const float4x3 edgeRB = float4x3(r1, r2, b1, b2);
        const float4x3 edgeLB = float4x3(l1, l2, b1, b2);


        const half3 c0 = SampleColor(uv0);
        const half3 c0Sq = Pow2(c0);

        float4x3 average = 0; //四个对角区域的color值平均
        float4x3 quadraticSum = 1; //四个对角区域的color平方和

        average[0] = mul(SumKernel, lu) + mul(SumKernel, edgeLU) + c0;
        average[1] = mul(SumKernel, ru) + mul(SumKernel, edgeRU) + c0;
        average[2] = mul(SumKernel, rb) + mul(SumKernel, edgeRB) + c0;
        average[3] = mul(SumKernel, lb) + mul(SumKernel, edgeLB) + c0;

        quadraticSum[0] = mul(SumKernel, Pow2(lu)) + mul(SumKernel, Pow2(edgeLU)) + c0Sq;
        quadraticSum[1] = mul(SumKernel, Pow2(ru)) + mul(SumKernel, Pow2(edgeRU)) + c0Sq;
        quadraticSum[2] = mul(SumKernel, Pow2(rb)) + mul(SumKernel, Pow2(edgeRB)) + c0Sq;
        quadraticSum[3] = mul(SumKernel, Pow2(lb)) + mul(SumKernel, Pow2(edgeLB)) + c0Sq;

        const float3 div = rcp(9).xxx;
        const float4x3 divMatrix = float4x3(div, div, div, div);

        average *= divMatrix;

        const float4x3 variance = quadraticSum * divMatrix - Pow2(average);

        const float4 varianceScalar = float4(
            Luminance(variance[0]),
            Luminance(variance[1]),
            Luminance(variance[2]),
            Luminance(variance[3]));


        // 找到最小方差的区域
        int minIndex = 1;
        float minVariance = varianceScalar[0];

        UNITY_UNROLL
        for (int i = 1; i < 4; i++)
        {
            if (varianceScalar[minIndex] < minVariance)
            {
                minVariance = varianceScalar[i];
                minIndex = i;
            }
        }

        return float4(average[minIndex], 1.0);
    }

    half4 FragmentBilinear(VaryingsKuwahara input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        float4x3 average = 0; //四个对角区域的color值平均
        float4x3 quadraticSum = 1; //四个对角区域的color平方和


        const float2 uv0 = input.texcoord.xy;

        const float4 step = input.texcoord.zwzw;
        
        const float3 div = float3(0.25, 0.25, 0.25);
        
        float4 sample_Line1 = mad(step, float4(-0.5, -0.5, -1.5, -0.5), uv0.xyxy);
        float4 sample_Line2 = mad(step, float4(-0.5, -1.5, -1.5, -1.5), uv0.xyxy);
        
        half3 c1 = SampleColor(sample_Line1.xy);
        half3 c2 = SampleColor(sample_Line1.zw);
        half3 c3 = SampleColor(sample_Line2.xy);
        half3 c4 = SampleColor(sample_Line2.zw);
        average[0] = (c1 + c2 + c3 + c4) * div;
        quadraticSum[0] = (Pow2(c1) + Pow2(c2) + Pow2(c3) + Pow2(c4)) * div;


        sample_Line1 = mad(step, float4(-0.5, 0.5, -1.5, 0.5), uv0.xyxy);
        sample_Line2 = mad(step, float4(-0.5, 1.5, -1.5, 1.5), uv0.xyxy);
        c1 = SampleColor(sample_Line1.xy);
        c2 = SampleColor(sample_Line1.zw);
        c3 = SampleColor(sample_Line2.xy);
        c4 = SampleColor(sample_Line2.zw);
        average[1] = (c1 + c2 + c3 + c4) * div;
        quadraticSum[2] = (Pow2(c1) + Pow2(c2) + Pow2(c3) + Pow2(c4)) * div;

        sample_Line1 = mad(step, float4(0.5, 0.5, 1.5, 0.5), uv0.xyxy);
        sample_Line2 = mad(step, float4(0.5, 1.5, 1.5, 1.5), uv0.xyxy);
        c1 = SampleColor(sample_Line1.xy);
        c2 = SampleColor(sample_Line1.zw);
        c3 = SampleColor(sample_Line2.xy);
        c4 = SampleColor(sample_Line2.zw);
        average[2] = (c1 + c2 + c3 + c4) * div;
        quadraticSum[2] = (Pow2(c1) + Pow2(c2) + Pow2(c3) + Pow2(c4)) * div;


        sample_Line1 = mad(step, float4(0.5, -0.5, 1.5, -0.5), uv0.xyxy);
        sample_Line2 = mad(step, float4(0.5, -1.5, 1.5, -1.5), uv0.xyxy);
        c1 = SampleColor(sample_Line1.xy);
        c2 = SampleColor(sample_Line1.zw);
        c3 = SampleColor(sample_Line2.xy);
        c4 = SampleColor(sample_Line2.zw);
        average[3] = (c1 + c2 + c3 + c4) * div;
        quadraticSum[3] = (Pow2(c1) + Pow2(c2) + Pow2(c3) + Pow2(c4)) * div;
        
        const float4x3 variance = quadraticSum - Pow2(average);

        const float4 varianceScalar = float4(
            Luminance(variance[0]),
            Luminance(variance[1]),
            Luminance(variance[2]),
            Luminance(variance[3]));

        // 找到最小方差的区域
        int minIndex = 1;
        float minVariance = varianceScalar[0];

        UNITY_UNROLL
        for (int i = 1; i < 4; i++)
        {
            if (varianceScalar[minIndex] < minVariance)
            {
                minVariance = varianceScalar[i];
                minIndex = i;
            }
        }

        return float4(average[minIndex], 1.0);
    }

    inline float SampleLuminace(float2 uv)
    {
        return Luminance(SampleColor(uv));
    }

    inline float2 Sobel(float2 uv, float2 step)
    {
        float lb = SampleLuminace(mad(step, float2(-1, -1), uv));
        float mb = SampleLuminace(mad(step, float2(0, -1), uv));
        float rb = SampleLuminace(mad(step, float2(1, -1), uv));
        float lu = SampleLuminace(mad(step, float2(-1, 1), uv));
        float mu = SampleLuminace(mad(step, float2(0, 1), uv));
        float ru = SampleLuminace(mad(step, float2(1, 1), uv));
        float lm = SampleLuminace(mad(step, float2(-1, 0), uv));
        float rm = SampleLuminace(mad(step, float2(1, 0), uv));

        float vt = lb + mb * 2 + rb - (lu + mu * 2 + ru);
        float hr = rb + rm * 2 + ru - (lb + lm * 2 + lu);

        return float2(hr, vt);
    }

    inline float2 RotateDir(in float2 dir, in float4 rotateMatrix)
    {
        return float2(dot(dir, rotateMatrix.xy), dot(dir, rotateMatrix.zw));
    }

    inline float4 GetRotateOffset(float u1, float v1, float u2, float v2, in float4 rotateMatrix)
    {
        return float4(RotateDir(float2(u1, v1), rotateMatrix).xy,
                      RotateDir(float2(u2, v2), rotateMatrix).xy);
    }

    half4 FragmentSobel(VaryingsKuwahara input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        
        const float2 uv0 = input.texcoord.xy;

        const float4 step = input.texcoord.zwzw;

        float2 axis = Sobel(uv0, step.xy);

        float2 dir = axis * rsqrt(dot(axis, axis) + HALF_MIN);

        float4 rotateMatrix = float4(dir.x, -dir.y, dir.y, dir.x);

        float2 upDir = RotateDir(float2(0, 1), rotateMatrix);

        float4 midPoints = float4(upDir, -upDir);
        float4 sample_Line = float4(mad(step, midPoints, uv0.xyxy));

        half3 c1 = SampleColor(sample_Line.zw);
        half3 c0 = SampleColor(uv0);
        half3 c2 = SampleColor(sample_Line.xy);

        half3 averageM = c0 + c1 + c2;
        float3 quadraticSumM = Pow2(c0) + Pow2(c1) + Pow2(c2);

        float4 lineOffsetDir1 = GetRotateOffset(1, -1, 2, -1, rotateMatrix);
        float4 lineOffsetDir2 = GetRotateOffset(1, 0, 2, 0, rotateMatrix);
        float4 lineOffsetDir3 = GetRotateOffset(1, 1, 2, 1, rotateMatrix);
        
        float4 sample_Line0 = mad(step, lineOffsetDir1, uv0.xyxy);
        float4 sample_Line1 = mad(step, lineOffsetDir2, uv0.xyxy);
        float4 sample_Line2 = mad(step, lineOffsetDir3, uv0.xyxy);

        c0 = SampleColor(sample_Line0.xy);
        c1 = SampleColor(sample_Line1.xy);
        c2 = SampleColor(sample_Line2.xy);
        half3 averageR = averageM + c0 + c1 + c2;
        float3 quadraticSumR = quadraticSumM + Pow2(c0) + Pow2(c1) + Pow2(c2);
        
        c0 = SampleColor(sample_Line0.zw);
        c1 = SampleColor(sample_Line1.zw);
        c2 = SampleColor(sample_Line2.zw);
        averageR += c0 + c1 + c2;
        quadraticSumR += Pow2(c0) + Pow2(c1) + Pow2(c2);
        
        
        sample_Line0 = mad(step, -lineOffsetDir1, uv0.xyxy);
        sample_Line1 = mad(step, -lineOffsetDir2, uv0.xyxy);
        sample_Line2 = mad(step, -lineOffsetDir3, uv0.xyxy);

        c0 = SampleColor(sample_Line0.xy);
        c1 = SampleColor(sample_Line1.xy);
        c2 = SampleColor(sample_Line2.xy);
        half3 averageL = averageM + c0 + c1 + c2;
        float3 quadraticSumL = quadraticSumM + Pow2(c0) + Pow2(c1) + Pow2(c2);
        
        c0 = SampleColor(sample_Line0.zw);
        c1 = SampleColor(sample_Line1.zw);
        c2 = SampleColor(sample_Line2.zw);
        averageL += c0 + c1 + c2;
        quadraticSumL += Pow2(c0) + Pow2(c1) + Pow2(c2);

        const float3 div = rcp(9).xxx;

        averageR *= div;
        averageL *= div;

        const float3 varianceR = mad(quadraticSumR, div, - Pow2(averageR));
        const float3 varianceL = mad(quadraticSumL, div, - Pow2(averageL));
        
        float varianceScalarR = Luminance(varianceR);
        float varianceScalarL = Luminance(varianceL);
        
        return half4(varianceScalarR < varianceScalarL ? averageR : averageL, 1);
    }
    
    
    half4 FragmentBilinearSobel(VaryingsKuwahara input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        
        const float2 uv0 = input.texcoord.xy;

        const float4 step = input.texcoord.zwzw;

        float2 axis = Sobel(uv0, step.xy);

        float2 dir = axis * rsqrt(dot(axis, axis) + HALF_MIN);

        float4 rotateMatrix = float4(dir.x, -dir.y, dir.y, dir.x);
        
        // todo 干掉efu
        // float angle = atan(axis.y * rcp(axis.x));
        //
        // float cosValue, sinValue;
        // sincos(angle, cosValue, sinValue);
        //
        // float2x2 rotateMatrix = float2x2(cosValue, -sinValue, sinValue, cosValue);

        float4 blockOffsetDir1 = GetRotateOffset(0.5, 0.5, 1.5, 0.5, rotateMatrix);
        float4 blockOffsetDir2 = GetRotateOffset(0.5, 1.5, 1.5, 1.5, rotateMatrix);

        float4 sample_Line1 = mad(step, blockOffsetDir1, uv0.xyxy);
        float4 sample_Line2 = mad(step, blockOffsetDir2, uv0.xyxy);

        half3 c1 = SampleColor(sample_Line1.xy);
        half3 c2 = SampleColor(sample_Line1.zw);
        half3 c3 = SampleColor(sample_Line2.xy);
        half3 c4 = SampleColor(sample_Line2.zw);
        half3 averageA = c1 + c2 + c3 + c4;
        float3 quadraticSumA = Pow2(c1) + Pow2(c2) + Pow2(c3) + Pow2(c4);

        sample_Line1 = mad(step, -blockOffsetDir1, uv0.xyxy);
        sample_Line2 = mad(step, -blockOffsetDir2, uv0.xyxy);
        
        c1 = SampleColor(sample_Line1.xy);
        c2 = SampleColor(sample_Line1.zw);
        c3 = SampleColor(sample_Line2.xy);
        c4 = SampleColor(sample_Line2.zw);
        half3 averageB = c1 + c2 + c3 + c4;
        float3 quadraticSumB = Pow2(c1) + Pow2(c2) + Pow2(c3) + Pow2(c4);

        const float3 div = float3(0.25, 0.25, 0.25);

        averageA *= div;
        averageB *= div;

        const float3 varianceA = mad(quadraticSumA, div, - Pow2(averageA));
        const float3 varianceB = mad(quadraticSumB, div, - Pow2(averageB));

        float varianceScalarA = Luminance(varianceA);
        float varianceScalarB = Luminance(varianceB);

        return half4(varianceScalarA < varianceScalarB ? averageA : averageB, 1);
    }
    ENDHLSL

    SubShader
    {

        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
        }
        ZTest Always
        ZWrite Off
        Blend Off
        Cull Off
        ColorMask RGB

        Pass
        {
            Name "Kuwahara Filter Base"

            HLSLPROGRAM
            #pragma vertex VertKuwahara
            #pragma fragment FragmentBase
            ENDHLSL
        }

        Pass
        {
            Name "Fragment Filter Bilinear"

            HLSLPROGRAM
            #pragma vertex VertKuwahara
            #pragma fragment FragmentBilinear
            ENDHLSL
        }

        
        Pass
        {
            Name "Fragment Filter Sobel"

            HLSLPROGRAM
            #pragma vertex VertKuwahara
            #pragma fragment FragmentSobel
            ENDHLSL
        }

        Pass
        {
            Name "Fragment Filter Bilinear & Sobel"

            HLSLPROGRAM
            #pragma vertex VertKuwahara
            #pragma fragment FragmentBilinearSobel
            ENDHLSL
        }
    }
}