#pragma once

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

#include "Assets/Graphics/PipelineFeature/GTAO/PackTool.hlsl"

#define KERNEL_RADIUS 8


float _AO_TemporalDirections;
float _AO_TemporalOffsets;
float _AO_HalfProjScale;

float2 _AO_FadeParams;
float4 _AO_UVToView;
float4 _AO_FadeValues;

float4 _AO_ResolveParams;
float4 _AO_PostParams;

// float4x4 _View_Matrix;
// float4x4 _Inverse_View_Matrix;
float4x4 _Inverse_View_ProjectionMatrix;

#define _AO_DirSampler          (uint)_AO_ResolveParams.x
#define _AO_SliceSampler        (uint)_AO_ResolveParams.y
#define _AO_Radius              _AO_ResolveParams.z
#define _AO_Power               _AO_ResolveParams.w

#define _AO_Intensity           _AO_PostParams.x
#define _AO_Sharpeness          _AO_PostParams.y
#define _AO_TemporalScale       _AO_PostParams.z
#define _AO_TemporalResponse    _AO_PostParams.w

TEXTURE2D_X(_ID2PBR_Tex);
SAMPLER(sampler_ID2PBR_Tex);
TEXTURE2D_X(_ID2Diffuse_Tex);
SAMPLER(sampler_ID2Diffuse_Tex);

TEXTURE2D_X(_BentNormalTexture);
SAMPLER(sampler_BentNormalTexture);

TEXTURE2D_X_FLOAT(_CameraNormalsTexture);
SAMPLER(sampler_CameraNormalsTexture);

TEXTURE2D_X(_CameraColorTexture);
SAMPLER(sampler_CameraColorTexture);

TEXTURE2D_X(_CameraReflectionsTexture);//ssr
SAMPLER(sampler_CameraReflectionsTexture);

TEXTURE2D_X(_MotionVectorTexture);
SAMPLER(sampler_MotionVectorTexture);

TEXTURE2D_X(_PrevRT);
SAMPLER(sampler_PrevRT);

TEXTURE2D_X(_CurrRT);
SAMPLER(sampler_CurrRT);

float4 _CameraDepthTexture_TexelSize;
float4 _BlitTexture_TexelSize;

static const float SKY_DEPTH_VALUE = 0.00001;
static const half HALF_POINT_ONE = half(0.1);
static const half HALF_MINUS_ONE = half(-1.0);
static const half HALF_ZERO = half(0.0);
static const half HALF_HALF = half(0.5);
static const half HALF_ONE = half(1.0);
static const half4 HALF4_ONE = half4(1.0, 1.0, 1.0, 1.0);
static const half HALF_TWO = half(2.0);
static const half HALF_TWO_PI = half(6.28318530717958647693);
static const half HALF_FOUR = half(4.0);
static const half HALF_NINE = half(9.0);
static const half HALF_HUNDRED = half(100.0);


#define DOWNSAMPLE 1// down 0.5

#define ADJUSTED_DEPTH_UV(uv) uv.xy + ((_CameraDepthTexture_TexelSize.xy * 0.5) * (1.0 - (DOWNSAMPLE - 0.5) * 2.0))





half4 EncodeFloatRGBA(half v)
{
    float4 enc = float4(1.0, 255.0, 65025.0, 16581375.0) * v;
    half4 encValue = frac(enc);
    encValue -= encValue.yzww * half4(0.0039215686275f, 0.0039215686275f, 0.0039215686275f, 0.0f);
    return encValue;
}


half DecodeFloatRGBA(half4 rgba)
{
    return dot(rgba, half4(1.0, 0.0039215686275f, 1.53787e-5f, 6.03086294e-8f));
}


half ComputeDistanceFade(const half distance)
{
    return saturate(max(0, distance - _AO_FadeParams.x) * _AO_FadeParams.y);
}


half4 PackAONormal(half3 n, half ao)
{
    n = mad(n , HALF_HALF , HALF_HALF);
    return half4(n, ao);
}

half3 GetPackedNormal(half4 p)
{
    return mad(p.xyz, HALF_TWO, -HALF_ONE);
}

half3 GetPackedNormal(half3 p)
{
    return mad(p, HALF_TWO, -HALF_ONE);
}

half GetPackedAO(half4 p)
{
    return p.w;
}


half4 PackAODepth(float depth, half ao)
{
    half4 enc = EncodeFloatRGBA(depth);
    enc.a = ao;
    return enc;
}

float GetPackedDepth(half4 p)
{
    return DecodeFloatRGBA(p);
}


half4 SampleTexture(TEXTURE2D_PARAM(_Texture, sampler_Texture), float2 uv)
{
    return SAMPLE_TEXTURE2D_X_LOD(_Texture, sampler_Texture, UnityStereoTransformScreenSpaceTex(uv), 0);
}

half3 SampleColor(float2 uv)
{
    return SampleTexture(TEXTURE2D_ARGS(_CameraColorTexture, sampler_CameraColorTexture), uv).xyz;
}

half3 SampleDiffuse(float2 uv)
{
    return SAMPLE_TEXTURE2D_X_LOD(_CameraColorTexture, sampler_CameraColorTexture, UnityStereoTransformScreenSpaceTex(uv), 1);
}

float SampleDepth(float2 uv)
{
    return SampleSceneDepth(uv);
}

half4 SampleCameraNormal(float2 uv)
{
    return SampleTexture(TEXTURE2D_ARGS(_CameraNormalsTexture, sampler_CameraNormalsTexture), uv);
}

half3 GetViewNormal(float2 uv)
{
    return normalize(mul((half3x3)unity_WorldToCamera, SampleCameraNormal(uv).xyz));
}

half4 GetWorldNormalSpec(float2 uv)
{
    return SampleCameraNormal(uv);
}

half4 SampleBlitTexture(float2 uv)
{
    return SampleTexture(TEXTURE2D_ARGS(_BlitTexture, sampler_PointClamp), uv);
}

float3 GetViewPosition(float2 uv, float depth)
{
    float linearDepth = LinearEyeDepth(depth, _ZBufferParams);
    return float3((uv * _AO_UVToView.xy + _AO_UVToView.zw) * linearDepth, linearDepth);
}

float3 GetViewPosition(float2 uv)
{
    float depth = SampleDepth(uv);
    float linearDepth = LinearEyeDepth(depth, _ZBufferParams);
    return float3((uv * _AO_UVToView.xy + _AO_UVToView.zw) * linearDepth, linearDepth);
}





half GTAO_Offsets(int2 position)
{
    return 0.25 * (half)((position.y - position.x) & 0x3);
}

half GTAO_Noise(int2 position)
{
    float noise = ( 1.0 / 16.0 ) * ( ( ( ( position.x + position.y ) & 0x3 ) << 2 ) +
                                                    ( position.x & 0x3 ) );
    return noise;
}

half GTAO_Noise2(float2 position)
{
    return frac(52.9829189 * frac(dot(position, half2( 0.06711056, 0.00583715))));
}

half IntegrateArc_UniformWeight(half2 h)
{
    half2 Arc = 1 - cos(h);
    return Arc.x + Arc.y;
}

half IntegrateArc_CosWeight(half2 h, half n)
{
    half2 Arc = -cos(2 * h - n) + cos(n) + 2 * h * sin(n);
    return 0.25 * (Arc.x + Arc.y);
}

//---//---//----//----//-------//----//----//----//-----//----//-----//----//----MultiBounce & ReflectionOcclusion//---//---//----//----//-------//----//----//----//-----//----//-----//----//----
inline float ApproximateConeConeIntersection(float ArcLength0, float ArcLength1, float AngleBetweenCones)
{
    float AngleDifference = abs(ArcLength0 - ArcLength1);

    float Intersection = smoothstep(0, 1, 1 - saturate((AngleBetweenCones - AngleDifference) / (ArcLength0 + ArcLength1 - AngleDifference)));

    return Intersection;
}

inline half ReflectionOcclusion(half3 BentNormal, half3 ReflectionVector, half Roughness, half OcclusionStrength)
{
    half BentNormalLength = length(BentNormal);
    half ReflectionConeAngle = max(Roughness, 0.1) * PI;
    half UnoccludedAngle = BentNormalLength * PI * OcclusionStrength;

    half AngleBetween = acos(dot(BentNormal, ReflectionVector) / max(BentNormalLength, 0.001));
    half ReflectionOcclusion = ApproximateConeConeIntersection(ReflectionConeAngle, UnoccludedAngle, AngleBetween);
    ReflectionOcclusion = lerp(0, ReflectionOcclusion, saturate((UnoccludedAngle - 0.1) / 0.2));
    return ReflectionOcclusion;
}

inline half ReflectionOcclusion_Approch(half NoV, half Roughness, half AO)
{
    return saturate(pow(NoV + AO, Roughness * Roughness) - 1 + AO);
}

float3 MultiBounce( float3 BaseColor, float AO )
{
    float3 a =  2.0404 * BaseColor - 0.3324;
    float3 b = -4.7951 * BaseColor + 0.6417;
    float3 c =  2.7552 * BaseColor + 0.6903;
    return max( AO, ( ( AO * a + b ) * AO + c ) * AO );
}



//

inline half Luma4(half3 Color)
{
    return (Color.g * 2) + (Color.r + Color.b);
}

inline half HdrWeight4(half3 Color, half Exposure)
{
    return rcp(Luma4(Color) * Exposure + 4);
}
    
inline void ResolverAABB(TEXTURE2D_PARAM(_CurrColor, sampler_CurrColor), half Sharpness, half ExposureScale, half AABBScale, half2 uv, half2 pixelSize, inout half4 minColor, inout half4 maxColor, inout half4 filterColor)
{
    half4 TopLeft = SampleTexture(TEXTURE2D_ARGS(_CurrColor, sampler_CurrColor), mad(float2(-1, -1), pixelSize, uv));
    half4 TopCenter = SampleTexture(TEXTURE2D_ARGS(_CurrColor, sampler_CurrColor), mad(float2(0, -1), pixelSize, uv));
    half4 TopRight = SampleTexture(TEXTURE2D_ARGS(_CurrColor, sampler_CurrColor), mad(float2(1, -1), pixelSize, uv));
    half4 MiddleLeft = SampleTexture(TEXTURE2D_ARGS(_CurrColor, sampler_CurrColor), mad(float2(-1, 0), pixelSize, uv));
    half4 MiddleCenter = SampleTexture(TEXTURE2D_ARGS(_CurrColor, sampler_CurrColor), mad(float2(0, 0), pixelSize, uv));
    half4 MiddleRight = SampleTexture(TEXTURE2D_ARGS(_CurrColor, sampler_CurrColor), mad(float2(1, 0), pixelSize, uv));
    half4 BottomLeft = SampleTexture(TEXTURE2D_ARGS(_CurrColor, sampler_CurrColor), mad(float2(-1, 1), pixelSize, uv));
    half4 BottomCenter = SampleTexture(TEXTURE2D_ARGS(_CurrColor, sampler_CurrColor), mad(float2(0, 1), pixelSize, uv));
    half4 BottomRight = SampleTexture(TEXTURE2D_ARGS(_CurrColor, sampler_CurrColor), mad(float2(1, 1), pixelSize, uv));

    // Resolver filtter 
    #if AA_Filter
    half SampleWeights[9];
    SampleWeights[0] = HdrWeight4(TopLeft.rgb, ExposureScale);
    SampleWeights[1] = HdrWeight4(TopCenter.rgb, ExposureScale);
    SampleWeights[2] = HdrWeight4(TopRight.rgb, ExposureScale);
    SampleWeights[3] = HdrWeight4(MiddleLeft.rgb, ExposureScale);
    SampleWeights[4] = HdrWeight4(MiddleCenter.rgb, ExposureScale);
    SampleWeights[5] = HdrWeight4(MiddleRight.rgb, ExposureScale);
    SampleWeights[6] = HdrWeight4(BottomLeft.rgb, ExposureScale);
    SampleWeights[7] = HdrWeight4(BottomCenter.rgb, ExposureScale);
    SampleWeights[8] = HdrWeight4(BottomRight.rgb, ExposureScale);

    half TotalWeight = SampleWeights[0] + SampleWeights[1] + SampleWeights[2] + SampleWeights[3] + SampleWeights[4] + SampleWeights[5] + SampleWeights[6] + SampleWeights[7] + SampleWeights[8];  
    half4 Filtered = (TopLeft * SampleWeights[0] + TopCenter * SampleWeights[1] + TopRight * SampleWeights[2] + MiddleLeft * SampleWeights[3] + MiddleCenter * SampleWeights[4] + MiddleRight * SampleWeights[5] + BottomLeft * SampleWeights[6] + BottomCenter * SampleWeights[7] + BottomRight * SampleWeights[8]) / TotalWeight;
    #endif

    half4 m1, m2, mean, stddev;
    #if AA_VARIANCE
    //
    m1 = TopLeft + TopCenter + TopRight + MiddleLeft + MiddleCenter + MiddleRight + BottomLeft + BottomCenter + BottomRight;
    m2 = TopLeft * TopLeft + TopCenter * TopCenter + TopRight * TopRight + MiddleLeft * MiddleLeft + MiddleCenter * MiddleCenter + MiddleRight * MiddleRight + BottomLeft * BottomLeft + BottomCenter * BottomCenter + BottomRight * BottomRight;

    mean = m1 / 9;
    stddev = sqrt(m2 / 9 - mean * mean);
        
    minColor = mean - AABBScale * stddev;
    maxColor = mean + AABBScale * stddev;
    //
    #else 
    //
    minColor = min(TopLeft, min(TopCenter, min(TopRight, min(MiddleLeft, min(MiddleCenter, min(MiddleRight, min(BottomLeft, min(BottomCenter, BottomRight))))))));
    maxColor = max(TopLeft, max(TopCenter, max(TopRight, max(MiddleLeft, max(MiddleCenter, max(MiddleRight, max(BottomLeft, max(BottomCenter, BottomRight))))))));
            
    half4 center = (minColor + maxColor) * 0.5;
    minColor = (minColor - center) * AABBScale + center;
    maxColor = (maxColor - center) * AABBScale + center;

    //
    #endif

    
    #if AA_Filter
    filterColor = Filtered;
    minColor = min(minColor, Filtered);
    maxColor = max(maxColor, Filtered);
    #else 
    filterColor = MiddleCenter;
    minColor = min(minColor, MiddleCenter);
    maxColor = max(maxColor, MiddleCenter);
    #endif

    //half4 corners = 4 * (TopLeft + BottomRight) - 2 * filterColor;
    //filterColor += (filterColor - (corners * 0.166667)) * 2.718282 * (Sharpness * 0.25);
}



//---//---//----//----//-------//----//----//----//-----//----//-----//----//----BilateralBlur//---//---//----//----//-------//----//----//----//-----//----//-----//----//----
inline half4 FetchAoAndDepth(float2 uv, inout float ao, inout float depth)
{
    half4 p = SampleBlitTexture(uv);
    depth = GetPackedDepth(p);
    depth = LinearEyeDepth(depth, _ZBufferParams);
    ao = GetPackedAO(p);
    return p;
}

inline float CrossBilateralWeight(float r, float d, float d0) {
    const float BlurSigma = (float)KERNEL_RADIUS * 0.5;
    const float BlurFalloff = 1 / (2 * BlurSigma * BlurSigma);

    float dz = (d0 - d) * _ProjectionParams.z * _AO_Sharpeness;
    return exp2(-r * r * BlurFalloff - dz * dz);
}

inline void ProcessSample(float2 aoz, float r, float d0, inout float totalAO, inout float totalW) {
    float w = CrossBilateralWeight(r, d0, aoz.y);
    totalW += w;
    totalAO += w * aoz.x;
}

inline void ProcessRadius(float2 uv0, float2 deltaUV, float d0, inout float totalAO, inout float totalW) {
    float ao, z;
    float2 uv;
    float r = 1;

    UNITY_UNROLL
    for (; r <= KERNEL_RADIUS / 2; r += 1) {
        uv = uv0 + r * deltaUV;
        FetchAoAndDepth(uv, ao, z);
        ProcessSample(float2(ao, z), r, d0, totalAO, totalW);
    }

    UNITY_UNROLL
    for (; r <= KERNEL_RADIUS; r += 2) {
        uv = uv0 + (r + 0.5) * deltaUV;
        FetchAoAndDepth(uv, ao, z);
        ProcessSample(float2(ao, z), r, d0, totalAO, totalW);
    }
		
}

inline half4 BlurDepth(float2 uv0, float2 deltaUV)
{
    float totalAO, depth;
    half4 p = FetchAoAndDepth(uv0, totalAO, depth);
    float totalW = 1;
		
    ProcessRadius(uv0, -deltaUV, depth, totalAO, totalW);
    ProcessRadius(uv0, deltaUV, depth, totalAO, totalW);

    p.a = totalAO / totalW;
    return p;
}



// The constant below controls the geometry-awareness of the bilateral
// filter. The higher value, the more sensitive it is.
static const half kGeometryCoeff = half(0.8);

// 控制法线和深度相似度的权重系数
#define NORMAL_WEIGHT 0.7

half CompareNormal(half3 d1, half3 d2)
{
    return smoothstep(kGeometryCoeff, HALF_ONE, dot(d1, d2));
}

half CompareDepth(half d1, half d2)
{
    half depthDiff = abs(d1 - d2);
    return  exp2(-depthDiff * _ProjectionParams.z);
}

half CompareNormal(half3 n1, half3 n2, half d1, half d2)
{
    half normalSim = CompareNormal(n1,n2);
    
    half depthSim = CompareDepth(d1, d2);
    
    return lerp(depthSim, normalSim, NORMAL_WEIGHT);
}

// Geometry-aware separable bilateral filter
half4 BlurNormal(const float2 uv, const float2 delta, out half3 n0) : SV_Target
{
    half4 p0 =  SampleBlitTexture(uv                       );
    half4 p1a = SampleBlitTexture(uv - delta * 1.3846153846);
    half4 p1b = SampleBlitTexture(uv + delta * 1.3846153846);
    half4 p2a = SampleBlitTexture(uv - delta * 3.2307692308);
    half4 p2b = SampleBlitTexture(uv + delta * 3.2307692308);

    n0 = GetPackedNormal(p0);

    half w0  =                                           half(0.2270270270);
    half w1a = CompareNormal(n0, GetPackedNormal(p1a)) * half(0.3162162162);
    half w1b = CompareNormal(n0, GetPackedNormal(p1b)) * half(0.3162162162);
    half w2a = CompareNormal(n0, GetPackedNormal(p2a)) * half(0.0702702703);
    half w2b = CompareNormal(n0, GetPackedNormal(p2b)) * half(0.0702702703);

    half s = half(0.0);
    s += GetPackedAO(p0)  * w0;
    s += GetPackedAO(p1a) * w1a;
    s += GetPackedAO(p1b) * w1b;
    s += GetPackedAO(p2a) * w2a;
    s += GetPackedAO(p2b) * w2b;
    s *= rcp(w0 + w1a + w1b + w2a + w2b);

    p0.w = s;
    return p0;
}



//-----------------------------------------------------------------------
half2 IdToUv(half id)
{
    int ID = (int)(id * 128.0 + 128.0);
    return  half2(ID & 15, ID >> 4) / 16.0;
}

half2 EnvBRDFApproxLazarov(half Roughness, half NoV)
{
    // [ Lazarov 2013, "Getting More Physical in Call of Duty: Black Ops II" ]
    // Adaptation to fit our G term.
    const half4 c0 = { -1, -0.0275, -0.572, 0.022 };
    const half4 c1 = { 1, 0.0425, 1.04, -0.04 };
    half4 r = Roughness * c0 + c1;
    half a004 = min(r.x * r.x, exp2(-9.28 * NoV)) * r.x + r.y;
    half2 AB = half2(-1.04, 1.04) * a004 + r.zw;
    return AB;
}

//UE4部分
//-----------------------------------------------------------------------


// max absolute error 9.0x10^-3
// Eberly's polynomial degree 1 - respect bounds
// 4 VGPR, 12 FR (8 FR, 1 QR), 1 scalar
// input [-1, 1] and output [0, PI]
half acosFastHalf(half inX)
{
    half x = abs(inX);
    half res = -0.156583f * x + (HALF_PI);
    res *= sqrt(1.0f - x);
    return (inX >= 0) ? res : PI - res;
}

// max absolute error 9.0x10^-3
// Eberly's polynomial degree 1 - respect bounds
// 4 VGPR, 12 FR (8 FR, 1 QR), 1 scalar
// input [-1, 1] and output [0, PI]
float acosFast(float inX)
{
    float x = abs(inX);
    float res = -0.156583f * x + (HALF_PI);
    res *= sqrt(1.0f - x);
    return (inX >= 0) ? res : PI - res;
}

// input [-1, 1] and output [-PI/2, PI/2]
half asinFast(half x)
{
    return (0.5 * PI) - acosFast(x);
}

// Same cost as acosFast + 1 FR
// Same error
// input [-1, 1] and output [-PI/2, PI/2]
float asinFast(float x)
{
    return (0.5 * PI) - acosFast(x);
}

float asqrtFast(float x)
{
    return asfloat(0x1FBD1DF5 + (asint(x) >> 1));
}

float TakeSmallerAbsDelta(float left, float mid, float right)
{
    float a = mid - left;
    float b = right - mid;

    return (abs(a) < abs(b)) ? a : b;
}

half3 GetRandomVector(uint2 TexturePos)
{
    TexturePos.y = 16384 - TexturePos.y;

    half3 RandomVec = half3(0, 0, 0);
    half3 RandomTexVec = half3(0, 0, 0);
    half ScaleOffset;

    const half TemporalCos = 0.8660253882f;
    const half TemporalSin = 0.50f;

    half GradientNoise = GTAO_Noise((float2) TexturePos);

    RandomTexVec.x = cos(GradientNoise * PI);
    RandomTexVec.y = sin(GradientNoise * PI);

    ScaleOffset = (1.0 / 4.0) * ((TexturePos.y - TexturePos.x) & 3);
    //	ScaleOffset = (1.0/5.0)  *  (( TexturePos.y - TexturePos.x) % 5);

    RandomVec.x = dot(RandomTexVec.xy, half2(TemporalCos, -TemporalSin));
    RandomVec.y = dot(RandomTexVec.xy, half2(TemporalSin, TemporalCos));
    RandomVec.z = frac(ScaleOffset + 0.025f);

    return RandomVec;
}

float2 GetRandomAngleOffset(uint2 TexturePos )
{
    TexturePos.y = 4096-TexturePos.y;
    float Angle  = GTAO_Noise(float2(TexturePos));
    float Offset = (1.0/4.0)  *  (( TexturePos.y - TexturePos.x)&3);
    return float2(Angle, Offset);
}

//-----------------------------------------------------------------------




half3 ReduceNormal(float2 uv, float depth, float3 vPos)
{
    // return half3(normalize(cross(ddy(vPos), ddx(vPos))));

    float2 delta = float2(_CameraDepthTexture_TexelSize.xy);
    
    float2 lUV = float2(-delta.x, 0.0);
    float2 rUV = float2(delta.x, 0.0);
    float2 uUV = float2(0.0, delta.y);
    float2 dUV = float2(0.0, -delta.y);
    
    // //unity
    // float3 l1 = float3(uv + lUV, 0.0); l1.z = SampleDepth(l1.xy); // Left1
    // float3 r1 = float3(uv + rUV, 0.0); r1.z = SampleDepth(r1.xy); // Right1
    // float3 u1 = float3(uv + uUV, 0.0); u1.z = SampleDepth(u1.xy); // Up1
    // float3 d1 = float3(uv + dUV, 0.0); d1.z = SampleDepth(d1.xy); // Down1
    //
    // uint closest_horizontal = l1.z > r1.z ? 0 : 1;
    // uint closest_vertical = d1.z > u1.z ? 0 : 1;
    //
    // float3 P1, P2;
    // if (closest_vertical == 0)
    // {
    //     P1 = half3(closest_horizontal == 0 ? l1 : d1);
    //     P2 = half3(closest_horizontal == 0 ? d1 : r1);
    // }
    // else
    // {
    //     P1 = half3(closest_horizontal == 0 ? u1 : r1);
    //     P2 = half3(closest_horizontal == 0 ? l1 : u1);
    // }
    //
    // return half3(normalize(cross(GetViewPosition(P2.xy,P2.z) - vPos, GetViewPosition(P1.xy,P1.z) - vPos)));

    //ue4
    float DeviceZ = depth;
    float DeviceZLeft = SampleDepth(uv + lUV);
    float DeviceZTop = SampleDepth(uv + uUV);
    float DeviceZRight = SampleDepth(uv + rUV);
    float DeviceZBottom = SampleDepth(uv + dUV);
    
    float DeviceZDdx = TakeSmallerAbsDelta(DeviceZLeft, DeviceZ, DeviceZRight);
    float DeviceZDdy = TakeSmallerAbsDelta(DeviceZTop, DeviceZ, DeviceZBottom);
    
    float ZRight = (DeviceZ + DeviceZDdx);
    float ZDown = (DeviceZ + DeviceZDdy);
    
    float3 Right = GetViewPosition(uv + rUV, ZRight) - vPos;
    float3 Down = GetViewPosition(uv + dUV, ZDown) - vPos;
    return half3(normalize(cross(Right, Down)));
    
}