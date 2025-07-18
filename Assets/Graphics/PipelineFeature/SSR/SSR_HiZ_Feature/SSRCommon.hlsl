#pragma once

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"


float4 _MaxMipLevel; // x:maxMipmapLevel y:FrameID, z:unused w:unused
float4 _HZBUvFactorAndInvFactor; // x:uvFactor.x, y:uvFactor.y, z:invFactor.x, w:invFactor.y
float4 _HZBOffsetParams; //offset.xy, -offset.xy,

// .r:Intensity in 0..1 range .g:RoughnessMaskMul, b:EnableDiscard for FPostProcessScreenSpaceReflectionsStencilPS, a:(bTemporalAAIsOn?TemporalAAIndex:StateFrameIndexMod8)*1551
float4 _SSRParams;

#define SSR_INTENSITY _SSRParams.r
#define SSR_SSRStepNums _SSRParams.g
#define SSR_Threshold _SSRParams.b
#define SSR_Dithering _SSRParams.a

#define SSRT_SAMPLE_BATCH_SIZE 4

#define length2(v) dot(v,v)

//Since some platforms don't remove Nans in saturate calls, 
//SafeSaturate function will remove nan/inf.    
//Can be expensive, only call when there's a good reason to expect Nans.
//D3D saturate actually turns NaNs -> 0  since it does the max(0.0f, value) first, and D3D NaN rules specify the non-NaN operand wins in such a case.  
//See: https://docs.microsoft.com/en-us/windows/desktop/direct3dhlsl/saturate
#define SafeSaturate_Def(type)\
type SafeSaturate(type In) \
{\
return saturate(In);\
}

SafeSaturate_Def(float)
SafeSaturate_Def(float2)
SafeSaturate_Def(float3)
SafeSaturate_Def(float4)

static const float ditherArray[16] =
{
    0.0, 0.5, 0.125, 0.625,
    0.75, 0.25, 0.875, 0.375,
    0.187, 0.687, 0.0625, 0.562,
    0.937, 0.437, 0.812, 0.312
};


TEXTURE2D_X(_CameraOpaqueTexture);
SAMPLER(sampler_CameraOpaqueTexture);

TEXTURE2D_X_FLOAT(_CameraDepthTexture);
SAMPLER(sampler_CameraDepthTexture);

TEXTURE2D_X_FLOAT(_HZBDepthTexture);
SAMPLER(sampler_HZBDepthTexture);

half3 SampleSceneColor(float2 uv)
{
    return SAMPLE_TEXTURE2D_X(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, uv.xy).rgb;
}

half3 SampleSceneColorLOD(float2 uv, int lod)
{
    return SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, uv.xy, lod).rgb;
}

float SampleSceneDepth(float2 uv)
{
    return SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_CameraDepthTexture, UnityStereoTransformScreenSpaceTex(uv)).r;
}

float SampleSceneDepthLOD(float2 uv, int lod)
{
    return SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, UnityStereoTransformScreenSpaceTex(uv), lod).r;
}

float GetHizDepth(float2 uv, float mipLevel = 0.0)
{
    #if UNITY_REVERSED_Z
    float rawDepth = SAMPLE_TEXTURE2D_X_LOD(_HZBDepthTexture, sampler_PointClamp, uv, mipLevel).r;
    #else
    float rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1,SAMPLE_TEXTURE2D_X_LOD(_HZBDepthTexture, sampler_PointClamp, uv, mipLevel).r);
    #endif
    return rawDepth;
}


float4 TransformViewToHScreen(float3 vpos, float2 screenSize)
{
    float4 clipPos = TransformWViewToHClip(vpos);
    float fliped = _ProjectionParams.x;
    clipPos.xy = (float2(clipPos.x, clipPos.y * fliped) / clipPos.w);
    clipPos.xy = clipPos.xy * 0.5 + 0.5;
    clipPos.xy *= screenSize;
    return clipPos;
}

float InterleavedGradientNoise(float2 uv, float FrameId)
{
    // magic values are found by experimentation
    uv += FrameId * (float2(47, 17) * 0.695f);

    const float3 magic = float3(0.06711056f, 0.00583715f, 52.9829189f);
    return frac(magic.z * frac(dot(uv, magic.xy)));
}

/** Return float multiplier to scale RayStepScreen such that it clip it right at the edge of the screen. */
float GetStepScreenFactorToClipAtScreenEdge(float2 RayStartScreen, float2 RayStepScreen)
{
    // Computes the scale down factor for RayStepScreen required to fit on the X and Y axis in order to clip it in the viewport
    const float RayStepScreenInvFactor = 0.5 * length(RayStepScreen);
    const float2 S = 1 - max(abs(RayStepScreen + RayStartScreen * RayStepScreenInvFactor) - RayStepScreenInvFactor, 0.0f) / abs(RayStepScreen);

    // Rescales RayStepScreen accordingly
    const float RayStepFactor = min(S.x, S.y) / RayStepScreenInvFactor;

    return RayStepFactor;
}

float ComputeRayHitSqrDistance(float3 OriginTranslatedWorld, float3 HitUVz)
{
    // ALU get factored out with ReprojectHit.
    float2 HitScreenPos = HitUVz.xy;
    float HitSceneDepth = Linear01Depth(HitUVz.z, _ZBufferParams);

    float3 HitTranslatedWorld = mul(UNITY_MATRIX_I_VP, float4(HitScreenPos * HitSceneDepth, HitSceneDepth, 1)).xyz;

    return length2(OriginTranslatedWorld - HitTranslatedWorld);
}

float ComputeHitVignetteFromScreenPos(float2 ScreenPos)
{
    float2 Vignette = saturate(abs(ScreenPos) * 5 - 4);

    //PrevScreen sometimes has NaNs or Infs.  DX11 is protected because saturate turns NaNs -> 0.
    //Do a SafeSaturate so other platforms get the same protection.
    return SafeSaturate(1.0 - dot(Vignette, Vignette));
}

half ComputeHitVignetteFromScreenPos(float2 uv, float intensity, float roundness)
{
    // float2 center = float2(0.5, 0.5);
    // center = UnityStereoTransformScreenSpaceTex(center);
    // float2 dist = abs(uv - center) * intensity;
    //
    // #if defined(UNITY_SINGLE_PASS_STEREO)
    // dist.x /= unity_StereoScaleOffset[unity_StereoEyeIndex].x;
    // #endif
    
    float2 dist = abs(uv - 0.5) * intensity;

    dist.x *= roundness;
    float vfactor = pow(saturate(1 - dot(dist, dist)), 4);
    return vfactor;
}

half ComputeVignetteFromScreenPos(float2 uv, float intensity, float roundness)
{
    // float2 center = float2(0.5, 0.5);
    // center = UnityStereoTransformScreenSpaceTex(center);
    // float2 dist = abs(uv - center) * intensity;
    //
    // #if defined(UNITY_SINGLE_PASS_STEREO)
    // dist.x /= unity_StereoScaleOffset[unity_StereoEyeIndex].x;
    // #endif
    
    float2 dist = abs(uv - 0.5) * intensity;

    dist.x *= roundness;
    float vfactor = 1 - pow(saturate(dot(dist, dist)), 4);
    return vfactor;
}

float GetRoughnessFade(in float Roughness)
{
    return SSR_INTENSITY.x * min(Roughness + 0.01, 1);
    // mask SSR to reduce noise and for better performance, roughness of 0 should have SSR, at MaxRoughness we fade to 0
    // return min(Roughness * _SSRParams.y + 2, 1.0) * SSR_INTENSITY.x;
//y本来是roughnessMask，这里没用了
}


bool rayCastHiZ(float3 posWS, float3 reflectDirWS, float roughness, float sceneDepth,
                float stepNum, out float3 outHitUVz)
{
    //ndc空间
    float4 startClip = mul(UNITY_MATRIX_VP, float4(posWS, 1));
    float4 endClip = mul(UNITY_MATRIX_VP, float4(posWS + reflectDirWS * sceneDepth, 1));

    float3 startScreen = startClip.xyz * rcp(startClip.w); //start
    float3 endScreen = endClip.xyz * rcp(endClip.w);

    // float4 depthClip = startClip + mul(UNITY_MATRIX_P, float4(0, 0, sceneDepth, 0));
    // float3 depthScreen = depthClip.xyz * rcp(depthClip.w);

    float3 stepScreen = endScreen - startScreen; //end

    float fadeSceenEdge = GetStepScreenFactorToClipAtScreenEdge(startScreen.xy, stepScreen.xy);
    stepScreen *= fadeSceenEdge;

    float compareTolerance = max(abs(stepScreen.z), (startScreen.z - stepScreen.z) * 4);


    //抖动 类似 
    // float2 noiseUV = posWS.xy; 
    float2 noiseUV = startScreen.xy * _ScreenParams.xy; //


    float StepOffset = (InterleavedGradientNoise(noiseUV, _MaxMipLevel.y) - 0.5f) * SSR_Dithering;
    //-------
    const float3 RayStartScreen = startScreen;
    const float3 RayStepScreen = stepScreen;
    
    #ifdef UNITY_UV_STARTS_AT_TOP
    float2 scaleXY = float2(0.5, -0.5);
    #else
    float2 scaleXY = float2(0.5, 0.5);
    #endif

    //[-1,1] -> [-0.5,0.5]且翻转y -> [0,1]
    //转换到Mipmap贴图UV， step是方向不需要加偏移
    float3 RayStartUVz = float3((RayStartScreen.xy * scaleXY + 0.5) * _HZBUvFactorAndInvFactor.xy, RayStartScreen.z);
    float3 RayStepUVz = float3((RayStepScreen.xy * scaleXY) * _HZBUvFactorAndInvFactor.xy, RayStepScreen.z);


    const float Step = 1 * rcp(stepNum);

    float CompareTolerance = compareTolerance * Step;
    

    bool bFoundAnyHit = false;
    RayStepUVz *= Step;

    #if 0
    mipLevel = 0;
    UNITY_LOOP
    for (uint i = 0; i < stepNum; ++i)
    {
        float3 CurrentUVZ = RayStartUVz + RayStepUVz * (i);
        //幻塔的
        float MipLevel = clamp(log2(i) * 0.5, 0, 4);

        float HiZDepth = GetHizDepth(CurrentUVZ.xy, MipLevel);

        if (abs(CurrentUVZ.z - HiZDepth + CompareTolerance) < CompareTolerance *_SSRParams.z )
        {
            bFoundAnyHit = true;
            outHitUVz.xy = CurrentUVZ.xy;
            outHitUVz.z = HiZDepth;
            mipLevel = MipLevel;
            break;
        }
    }

    if (bFoundAnyHit)
    {
        outHitUVz.xy = outHitUVz.xy * _HZBUvFactorAndInvFactor.zw;
    }
    else
    {
        outHitUVz = float3(0, 0, 0);
    }

    #else

    float3 RayUVz = RayStartUVz + RayStepUVz * StepOffset;

    float4 MultipleSampleDepthDiff;
    bool4 bMultipleSampleHit;
    int i;
    float LastDiff = 0;
    uint mipLevel = 1;

    UNITY_LOOP
    for (i = 0; i < stepNum; i += SSRT_SAMPLE_BATCH_SIZE)
    {
        float2 SamplesUV[SSRT_SAMPLE_BATCH_SIZE];
        float4 SamplesZ;
        float4 SamplesMip;


        {
            UNITY_UNROLLX(SSRT_SAMPLE_BATCH_SIZE)
            for (int j = 0; j < SSRT_SAMPLE_BATCH_SIZE; j++)
            {
                SamplesUV[j] = RayUVz.xy + (float(i) + float(j + 1)) * RayStepUVz.xy;
                SamplesZ[j] = RayUVz.z + (float(i) + float(j + 1)) * RayStepUVz.z;
            }

            SamplesMip.xy = mipLevel;
            mipLevel += (8.0 / stepNum) * roughness;

            SamplesMip.zw = mipLevel;
            mipLevel += (8.0 / stepNum) * roughness;
        }

        // Sample the scene depth.
        float4 SampleDepth;
        {
            UNITY_UNROLLX(SSRT_SAMPLE_BATCH_SIZE)
            for (uint j = 0; j < SSRT_SAMPLE_BATCH_SIZE; j++)
            {
                SampleDepth[j] = GetHizDepth(SamplesUV[j], SamplesMip[j]).r;
            }
        }

        // Evaluates the intersections.
        MultipleSampleDepthDiff = SamplesZ - SampleDepth;
        #if UNITY_UV_STARTS_AT_TOP
        bMultipleSampleHit = abs(MultipleSampleDepthDiff + CompareTolerance) < CompareTolerance * SSR_Threshold;
        // bMultipleSampleHit = abs(MultipleSampleDepthDiff + CompareTolerance) < CompareTolerance;
        #else
        bMultipleSampleHit = abs(MultipleSampleDepthDiff + CompareTolerance) < CompareTolerance;
        #endif

        // bMultipleSampleHit = abs(MultipleSampleDepthDiff + CompareTolerance) < CompareTolerance * SSR_Threshold;
        // bMultipleSampleHit = abs(MultipleSampleDepthDiff) < CompareTolerance * SSR_Threshold * 0.2;
        bFoundAnyHit = any(bMultipleSampleHit);

        UNITY_BRANCH
        if (bFoundAnyHit)
            break;

        LastDiff = MultipleSampleDepthDiff.w;
    }


    UNITY_BRANCH
    if (bFoundAnyHit)
    {
        {
            float DepthDiff0 = MultipleSampleDepthDiff[2];
            float DepthDiff1 = MultipleSampleDepthDiff[3];
            float Time0 = 3;

            UNITY_FLATTEN
            if (bMultipleSampleHit[2])
            {
                DepthDiff0 = MultipleSampleDepthDiff[1];
                DepthDiff1 = MultipleSampleDepthDiff[2];
                Time0 = 2;
            }
            UNITY_FLATTEN
            if (bMultipleSampleHit[1])
            {
                DepthDiff0 = MultipleSampleDepthDiff[0];
                DepthDiff1 = MultipleSampleDepthDiff[1];
                Time0 = 1;
            }
            UNITY_FLATTEN
            if (bMultipleSampleHit[0])
            {
                DepthDiff0 = LastDiff;
                DepthDiff1 = MultipleSampleDepthDiff[0];
                Time0 = 0;
            }

            Time0 += float(i);

            // float Time1 = Time0 + 1;


            // Find more accurate hit using line segment intersection
            float TimeLerp = saturate(DepthDiff0 / (DepthDiff0 - DepthDiff1));
            float IntersectTime = Time0 + TimeLerp;
            //float IntersectTime = lerp( Time0, Time1, TimeLerp );

            outHitUVz = RayUVz + RayStepUVz * IntersectTime;
        }
        outHitUVz.xy = outHitUVz.xy * _HZBUvFactorAndInvFactor.zw;
    }
    else
    {
        outHitUVz = float3(0, 0, 0);
    }
    #endif
    return bFoundAnyHit;
}


bool rayMarchingInScreenSpace(float3 startVS, float3 reflectDirVS, inout float3 outHitUVz)
{
    //根据最大距离求出步进端点
    half magnitude = 100;
    float endZ = (startVS + reflectDirVS * magnitude).z;
    if (endZ > -_ProjectionParams.y)
        magnitude = (-_ProjectionParams.y - startVS.z) / reflectDirVS.z;
    float3 endVS = startVS + reflectDirVS * magnitude;

    float4 startHScreen = TransformViewToHScreen(startVS, _ScreenSize.xy);
    float4 endHScreen = TransformViewToHScreen(endVS, _ScreenSize.xy);

    float2 startScreen = startHScreen.xy;
    float2 endScreen = endHScreen.xy;

    float2 delta = endScreen - startScreen;
    bool permute = false; //交换标志位
    if (abs(delta.x) < abs(delta.y))
    {
        permute = true;
        //保证屏幕步进斜率不超过45
        delta.xy = delta.yx;
        startScreen = startScreen.yx;
        endScreen = endScreen.yx;
    }

    float dir = sign(delta.x);
    // float2 stepScreen = (delta / abs(delta.x)) * 32;
    float2 stepScreen = delta * rcp(SSR_SSRStepNums);

    float2 screenSamplePoint = startScreen;

    // dither
    float2 ditherUV = fmod(screenSamplePoint, 4);
    // half jitter = ditherMartix[ditherUV.x][ditherUV.y];
    float jitter = ditherArray[ditherUV.x * 4 + ditherUV.y];
    jitter = lerp(1, jitter, SSR_Dithering);
    screenSamplePoint += stepScreen * jitter;

    endZ = endScreen.x * dir;
    
    outHitUVz = float3(0, 0, 0);
    // 缓存当前深度和位置
    half curFac = 0.0;
    half lastFac = 0.0;
    // half oriFac = 0.0;
    // half throuth = 0;
    int missCount = 0;
    UNITY_LOOP
    for (int i = 0; i <= 1; i++)
    {
        bool _isHit = false;
        float viewDepth = _ProjectionParams.x * startVS.z;
        float screenDepth = _ProjectionParams.x * startVS.z;
        float2 currUV; //像素坐标
        UNITY_LOOP
        for (int j = 0; j < SSR_SSRStepNums && screenSamplePoint.x * dir < endZ; j++)
        {
            if (missCount > 1.0)
            {
                missCount = 0;
                stepScreen *= 2.0;
            }
            screenSamplePoint += stepScreen;
            currUV = permute ? screenSamplePoint.yx : screenSamplePoint;
            currUV /= _ScaledScreenParams.xy;
            half4 uvSign = half4(sign(currUV.x), sign(1.0h - currUV.x), sign(currUV.y), sign(1.0h - currUV.y));

            if (dot(half4(1, 1, 1, 1), uvSign) < 3.5h)
            {
                break;
            }

            screenDepth = SampleSceneDepthLOD(currUV, 0);
            screenDepth = LinearEyeDepth(screenDepth, _ZBufferParams);

            lastFac = curFac;
            curFac = clamp((screenSamplePoint.x - startScreen.x) / delta.x, 0, 1);
            // oriFac = curFac;
            
            viewDepth = - (startVS.z * endVS.z) / lerp(endVS.z, startVS.z, curFac);
            float lastViewDepth = - (startVS.z * endVS.z) / lerp(endVS.z, startVS.z, lastFac);
            if (lastViewDepth > viewDepth)
            {
                Swap(lastViewDepth, viewDepth);
            }
            if (abs(lastViewDepth - screenDepth) < 0.1)
            {
                _isHit = true;
                break;
            }
        }

        //穿透
        if (_isHit)
        {
            if (abs(viewDepth - screenDepth) < SSR_Threshold * 3)
            {
                outHitUVz.xy = currUV;
                outHitUVz.z = viewDepth;
                return true;
            }
            screenSamplePoint -= stepScreen;
            stepScreen *= 0.5;
        }
        missCount++;
    }
    return false;
}


bool ScreenSpaceRayMarching(inout float2 P, inout float3 Q, inout float K, float2 dp, float3 dq, float dk, float rayZ, bool permute, out float depthDistance, inout float3 hitUV) {
    // float end = endScreen.x * dir;
    float rayZMin = rayZ;
    float rayZMax = rayZ;
    float preZ = rayZ;
    // 进行屏幕空间射线步近
    UNITY_LOOP
    for (int i = 0; i < SSR_SSRStepNums; i++) {
        // 步近
        P += dp;
        Q += dq;
        K += dk;

        // 得到步近前后两点的深度
        rayZMin = preZ;
        rayZMax = (dq.z * 0.5 + Q.z) / (dk * 0.5 + K);
        preZ = rayZMax;
        if (rayZMin > rayZMax)
            Swap(rayZMin, rayZMax);

        // 得到交点uv
        hitUV.xy = permute > 0.5 ? P.yx : P;
        hitUV.xy *= _ScreenSize.zw;

        if (any(hitUV.xy < 0.0) || any(hitUV.xy > 1.0))
            return false;

        float surfaceDepth = -LinearEyeDepth(SampleSceneDepthLOD(hitUV.xy, 0), _ZBufferParams);
        bool isBehind = (rayZMin + 0.1 <= surfaceDepth); // 加一个bias 防止stride过小，自反射

        depthDistance = abs(surfaceDepth - rayZMax);

        if (isBehind) {
            return true;
        }
    }
    return false;
}

bool BinarySearchRaymarching(float3 startView, float3 rDir, inout float3 hitUV) {
    float magnitude = 100;

    float end = startView.z + rDir.z * magnitude;
    if (end > -_ProjectionParams.y)
        magnitude = (-_ProjectionParams.y - startView.z) / rDir.z;
    float3 endView = startView + rDir * magnitude;

    // 齐次屏幕空间坐标
    float4 startHScreen = TransformViewToHScreen(startView, _ScreenSize.xy);
    float4 endHScreen = TransformViewToHScreen(endView, _ScreenSize.xy);

    // inverse w
    float startK = 1.0 / startHScreen.w;
    float endK = 1.0 / endHScreen.w;

    //  结束屏幕空间坐标
    float2 startScreen = startHScreen.xy;
    float2 endScreen = endHScreen.xy;

    // 经过齐次除法的视角坐标
    float3 startQ = startView * startK;
    float3 endQ = endView * endK;

    float stride = 50;

    float depthDistance = 0.0;

    bool permute = false;

    // 根据斜率将dx=1 dy = delta
    float2 diff = endScreen - startScreen;
    if (abs(diff.x) < abs(diff.y)) {
        permute = true;

        diff = diff.yx;
        startScreen = startScreen.yx;
        endScreen = endScreen.yx;
    }

    // 计算屏幕坐标、齐次视坐标、inverse-w的线性增量
    float dir = sign(diff.x);
    float invdx = dir / diff.x;
    float2 dp = float2(dir, invdx * diff.y);
    float3 dq = (endQ - startQ) * invdx;
    float dk = (endK - startK) * invdx;

    // 缓存当前深度和位置
    float rayZ = startView.z;

    float2 P = startScreen;
    float3 Q = startQ;
    float K = startK;

    float2 ditherUV = fmod(P, 4);  
    float jitter = ditherArray[ditherUV.x * 4 + ditherUV.y];
    jitter = lerp(1, jitter, SSR_Dithering);
    stride *= jitter;
    dp *= stride;
    dq *= stride;
    dk *= stride;
    
    hitUV.z = 0.0;
    UNITY_LOOP
    for (int i = 0; i < 2; i++) {
        if (ScreenSpaceRayMarching(P, Q, K, dp, dq, dk, rayZ, permute, depthDistance, hitUV)) {
            if (depthDistance < SSR_Threshold * 0.5)
                return true;
            P -= dp;
            Q -= dq;
            K -= dk;
            rayZ = Q.z / K;

            dp *= 0.5;
            dq *= 0.5;
            dk *= 0.5;
        }
        else {
            return false;
        }
    }
    return false;
}



float4 HierarchicalZScreenSpaceRayMarching(float3 startView, float3 rDir, inout float3 hitUV) {
    float magnitude = 100;

    float end = startView.z + rDir.z * magnitude;
    if (end > -_ProjectionParams.y)
        magnitude = (-_ProjectionParams.y - startView.z) / rDir.z;
    float3 endView = startView + rDir * magnitude;

    // 齐次屏幕空间坐标
    float4 startHScreen = TransformViewToHScreen(startView, _ScreenSize.xy);
    float4 endHScreen = TransformViewToHScreen(endView, _ScreenSize.xy);

    // inverse w
    float startK = 1.0 / startHScreen.w;
    float endK = 1.0 / endHScreen.w;

    //  结束屏幕空间坐标
    float2 startScreen = startHScreen.xy;
    float2 endScreen = endHScreen.xy;

    // 经过齐次除法的视角坐标
    float3 startQ = startView * startK;
    float3 endQ = endView * endK;

    float stride = 30;

    // float depthDistance = 0.0;

    bool permute = false;

    // 根据斜率将dx=1 dy = delta
    float2 diff = endScreen - startScreen;
    if (abs(diff.x) < abs(diff.y)) {
        permute = true;

        diff = diff.yx;
        startScreen = startScreen.yx;
        endScreen = endScreen.yx;
    }

    // 计算屏幕坐标、齐次视坐标、inverse-w的线性增量
    float dir = sign(diff.x);
    float invdx = dir / diff.x;
    float2 dp = float2(dir, invdx * diff.y);
    float3 dq = (endQ - startQ) * invdx;
    float dk = (endK - startK) * invdx;

    // 缓存当前深度和位置
    float rayZ = startView.z;

    float2 P = startScreen;
    float3 Q = startQ;
    float K = startK;

    float2 ditherUV = fmod(P, 4);  
    float jitter = ditherArray[ditherUV.x * 4 + ditherUV.y];
    jitter = lerp(1, jitter, SSR_Dithering);
    stride *= jitter;
    dp *= stride;
    dq *= stride;
    dk *= stride;
    float rayZMin = rayZ;
    float rayZMax = rayZ;
    float preZ = rayZ;

    float mipLevel = 0.0;

    // hitUV = 0.0;

    // 进行屏幕空间射线步近
    UNITY_LOOP
    for (int i = 0; i < SSR_SSRStepNums; i++) {
        // 步近
        P += dp * exp2(mipLevel);
        Q += dq * exp2(mipLevel);
        K += dk * exp2(mipLevel);

        // 得到步近前后两点的深度
        rayZMin = preZ;
        rayZMax = (dq.z * exp2(mipLevel) * 0.5 + Q.z) / (dk * exp2(mipLevel) * 0.5 + K);
        preZ = rayZMax;
        if (rayZMin > rayZMax)
            Swap(rayZMin, rayZMax);

        // 得到交点uv
        hitUV.xy = permute ? P.yx : P;
        hitUV.xy *= _ScreenSize.zw;

        if (any(hitUV < 0.0) || any(hitUV > 1.0))
            return false;

        float2 sampleUV =  hitUV.xy * _HZBUvFactorAndInvFactor.xy;
        float rawDepth = SAMPLE_TEXTURE2D_X_LOD(_HZBDepthTexture, sampler_HZBDepthTexture, sampleUV.xy, mipLevel).r;
        float surfaceDepth = -LinearEyeDepth(rawDepth, _ZBufferParams);

        bool behind = (rayZMin + 0.1) <= surfaceDepth;

        if (!behind) {
            mipLevel = min(mipLevel + 1, _MaxMipLevel.x);
        }
        else {
            if (mipLevel == 0) {
                if (abs(surfaceDepth - rayZMax) < SSR_Threshold)
                {
                    hitUV.z = rayZMin;
                    return true;
                    // return float4(hitUV.xy, rayZMin, 1.0);
                // return float4(hitUV, rayZMin, 0.0);
                }
            }
            else {
                P -= dp * exp2(mipLevel);
                Q -= dq * exp2(mipLevel);
                K -= dk * exp2(mipLevel);
                preZ = Q.z / K;

                mipLevel--;
            }
        }

    }

    hitUV.z = rayZMin;
    return false;
    // return float4(hitUV.xy, rayZMin, 0.0);
}
