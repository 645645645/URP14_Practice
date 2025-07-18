#pragma once

#include "Assets/Graphics/PipelineFeature/GTAO/GTAOCommon.hlsl"



Varyings Vert_GTAO(Attributes input)
{
    Varyings output;
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
    output.texcoord = uv;
    return output;
}


//viewSpace
half4 Fragment_ReduceNormal(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float2 uv = input.texcoord;

    float depth = SampleDepth(uv);

    float3 vPos = GetViewPosition(uv, depth);

    half3 vNormal = ReduceNormal(uv, depth, vPos);

    return PackAONormal(vNormal, 0.5);//spec(cosnt)
}

half4 Fragment_ResolveGTAO(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    const float2 uv = input.texcoord;

    float depth = SampleDepth(uv);

    //unity的天空球不写深度。。
    UNITY_BRANCH
    if (depth < SKY_DEPTH_VALUE)
        return PackAONormal(HALF_ZERO, HALF_ONE);

    float3 vPos = GetViewPosition(uv, depth);

    // if (vPos.z > 50)
    //     return PackAONormal(HALF_ZERO, HALF_ONE);

    half3 vNormal = GetViewNormal(uv);

    half3 vDir = normalize(-vPos);

    // half2 radius_thickness = lerp(half2(_AO_Radius, 1), _AO_FadeValues.yw, ComputeDistanceFade(vPos.b).xx);
    half2 radius_thickness = half2(_AO_Radius, 1);
    half radius = radius_thickness.x;
    half thickness = radius_thickness.y;
    
    const int NumCircle = _AO_DirSampler; //半球划分
    const int NumSlice = _AO_SliceSampler; //切面划分

    half stepRadiusMax = max(min((radius * _AO_HalfProjScale) / vPos.z, 256), (half)NumSlice);
    half stepRadius = stepRadiusMax/((half)NumSlice + 1);

    int2 texturePos = uv * _CameraDepthTexture_TexelSize.zw;
    
    half noiseOffset = GTAO_Offsets(texturePos);
    half noiseDirection = GTAO_Noise2(texturePos); //空间噪声

    // half3 noise = GetRandomVector(uv * _CameraDepthTexture_TexelSize.zw);
    
    // half3 randomAndOffset = GetRandomVector(texturePos);
    // half2 noiseOffset = randomAndOffset.xy;
    // half noiseDirection = randomAndOffset.z;

    half initialRayStep = frac(noiseOffset + _AO_TemporalOffsets);

    float angle;

    half AO, bentAngle, projLength, n, cos_n;

    half2 slideDir_TexelSize, h, H, falloff, uvOffset, dsdt, dsdtLength;
    half3 sliceDir, ds, dt, planeNormal, tangent, projectedNormal, BentNormal;
    half4 uvSlice;

    half angleStep = rcp((half)NumCircle) * PI;

    UNITY_LOOP
    for (int i = 0; i < NumCircle; i++)
    {
        angle = (noiseDirection + _AO_TemporalDirections + i) * angleStep;

        sliceDir = half3(cos(angle), sin(angle), 0);

        slideDir_TexelSize = sliceDir.xy * _CameraDepthTexture_TexelSize.xy;
        
        h = -1;
        
        UNITY_LOOP
        for (int j = 0; j < NumSlice; j++)
        {
            uvOffset = slideDir_TexelSize * max(stepRadius * (j + initialRayStep), 1 + j);
            uvSlice = uv.xyxy + float4(uvOffset, -uvOffset);

            ds = GetViewPosition(uvSlice.xy) - vPos;
            dt = GetViewPosition(uvSlice.zw) - vPos;


            dsdt = half2(dot(ds, ds), dot(dt, dt));
            dsdtLength = rsqrt(dsdt);

            falloff = saturate(dsdt.xy * (2 / (radius * radius)));

            H = half2(dot(ds, vDir), dot(dt, vDir)) * dsdtLength;
            h.xy = (H.xy > h.xy) ? lerp(H, h, falloff) : lerp(H, h, thickness);
        }

        planeNormal = normalize(cross(sliceDir, vDir));
        tangent = cross(vDir, planeNormal);
        projectedNormal = vNormal - planeNormal * dot(vNormal, planeNormal);
        projLength = length(projectedNormal);
        

        cos_n = clamp(dot(normalize(projectedNormal), vDir), -1, 1);
        n = -sign(dot(projectedNormal, tangent)) * acos(cos_n);

        // h = acos(clamp(h, -1, 1));
        h = acos(h);
        h.x = n + max(-h.x - n, -HALF_PI);
        h.y = n + min(h.y - n, HALF_PI);

        bentAngle = (h.x + h.y) * 0.5;

        BentNormal += vDir * cos(bentAngle) - tangent * sin(bentAngle);
        AO += projLength * IntegrateArc_CosWeight(h, n);
        // AO += projLength * IntegrateArc_UniformWeight(h);
    }

    BentNormal = normalize(normalize(BentNormal) - vDir * 0.5);
    // BentNormal = normalize(normalize(BentNormal) - vDir * saturate(2 * length(fwidth(vNormal))));

    BentNormal = mul((half3x3)unity_CameraToWorld, half3(BentNormal.xy, BentNormal.z));
    
    AO = saturate(pow(AO * rcp(NumCircle), _AO_Power));//to 使用2 4这种常量
    
    return PackAONormal(BentNormal, AO);
}

half4 Fragment_CopyAONormal(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    const float2 uv = input.texcoord;

    half ao = GetPackedAO(SampleBlitTexture(uv));
    
    #ifdef _PACKNORMALAO
    half4 normal = GetWorldNormalSpec(uv);
    return PackAONormal(normal.xyz, ao);
    #else
    float depth = SampleSceneDepth(uv);
    return PackAODepth(depth, ao);
    #endif

}



half4 Fragment_HBlur(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    const float2 uv = input.texcoord;
    
    const float2 delta = float2(_BlitTexture_TexelSize.x,0.0);
    
    #ifdef _PACKNORMALAO
    half3 wNormal;
    return BlurNormal(uv, delta, wNormal);
    #else
    return  BlurDepth(uv, delta);
    #endif
}


half4 Fragment_VBlur(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    const float2 uv = input.texcoord;
    
    const float2 delta = float2(0.0, _BlitTexture_TexelSize.y);

    #ifdef _PACKNORMALAO
    half3 wNormal;
    half4 packAO = BlurNormal(uv, delta, wNormal);
    float depth = SampleDepth(uv);
    #else
    half4 packAO = BlurDepth(uv, delta);
    float depth = GetPackedDepth(packAO);
    #endif
    
    half ao = GetPackedAO(packAO);
    
    half3 bentNormal = SampleTexture(TEXTURE2D_ARGS(_BentNormalTexture, sampler_BentNormalTexture), uv).xyz;
    bentNormal = GetPackedNormal(bentNormal);
    // bentNormal.z = -bentNormal.z;
    
    
    //////Reflection Occlusion
    half4 wNormalSpec = GetWorldNormalSpec(uv);
    // half roughness = wNormalSpec.w;

    half2 matIdUV = IdToUv(wNormalSpec.w);
    
    half4 pbr = SampleTexture(TEXTURE2D_ARGS(_ID2PBR_Tex, sampler_ID2PBR_Tex), matIdUV);
    half roughness = pbr.x;
    
    float4 wPos = mul(_Inverse_View_ProjectionMatrix, float4(mad(uv.xy, 2, -1), depth, 1));
    wPos.xyz /= wPos.w;

    half3 vDir = normalize(wPos.xyz - _WorldSpaceCameraPos.xyz);
    half3 reflectDir = reflect(vDir, wNormalSpec.xyz);
    half ro = ReflectionOcclusion(bentNormal, reflectDir, roughness, 0.5);

    half2 result = lerp(1, half2(ao, ro), _AO_Intensity);

    // return bentNormal.xyzz;
    return half4(result.x, result.y, roughness, 0);
}


half4 Fragmen_Temporal(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    const float2 uv = input.texcoord;

    half2 velocity = SAMPLE_TEXTURE2D_LOD(_MotionVectorTexture, sampler_MotionVectorTexture, uv, 0).xy;

    half4 filterColor = 0;
	half4 minColor, maxColor;

    //ao ro
    ResolverAABB(TEXTURE2D_ARGS(_BlitTexture, sampler_PointClamp),0, 0, _AO_TemporalScale, uv, _CameraDepthTexture_TexelSize.xy, minColor, maxColor, filterColor);

    half4 currColor = filterColor;
    half4 lastColor = SampleTexture(TEXTURE2D_ARGS(_PrevRT, sampler_PrevRT), uv - velocity);
    lastColor = clamp(lastColor, minColor, maxColor);
    if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1)
    {
        lastColor = filterColor;
    }
    half weight = saturate(clamp(_AO_TemporalResponse, 0, 0.98) * (1 - length(velocity) * 8));

    half4 temporalColor = lerp(currColor, lastColor, weight);
    return temporalColor;
}


half3 Fragmen_Combine(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    const float2 uv = input.texcoord;

    half4 Occlusion = SampleBlitTexture(uv);
    half3 ao = Occlusion.xxx;
    half ro = Occlusion.y;

    half4 wNormalSpec = GetWorldNormalSpec(uv);
    half3 wNormal = wNormalSpec.xyz;
    
    half2 matIdUV = IdToUv(wNormalSpec.w);
    half4 pbr = SampleTexture(TEXTURE2D_ARGS(_ID2PBR_Tex, sampler_ID2PBR_Tex), matIdUV);
    half3 albedo = SampleTexture(TEXTURE2D_ARGS(_ID2Diffuse_Tex, sampler_ID2Diffuse_Tex), matIdUV).xyz;

    half roughness = max(pbr.x, HALF_MIN);
    half metallic = pbr.y;
    ao = MultiBounce(albedo, ao);

    half3 F0 = lerp(0.04, albedo.rgb, metallic);
    
    float depth = SampleDepth(uv);
    float4 wPos = mul(_Inverse_View_ProjectionMatrix, float4(mad(uv.xy, 2, -1), depth, 1));
    wPos.xyz /= wPos.w;
    half3 viewDirWS = normalize(wPos.xyz - _WorldSpaceCameraPos.xyz);
    
    half nv = saturate(dot(wNormal, viewDirWS));
    
    half2 scale_bias = EnvBRDFApproxLazarov(roughness, nv);
    half3 envBRDF = F0 * scale_bias.x + scale_bias.y;
    

    half3 ssrColor = SampleTexture(TEXTURE2D_ARGS(_CameraReflectionsTexture, sampler_CameraReflectionsTexture), uv).xyz;

    half3 sceneColor = ao * SampleColor(uv);
    
    ssrColor *= ro * envBRDF;

    return sceneColor + ssrColor;
}


//--------------------------------------------------------------------------------------

half4 Fragment_DebugBentNormal(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float2 uv = input.texcoord;
    
    half4 bentNormal = SampleTexture(TEXTURE2D_ARGS(_BentNormalTexture, sampler_BentNormalTexture), uv);

    bentNormal.xyz = GetPackedNormal(bentNormal);
    
    return bentNormal;
}


half4 Fragment_DebugAO(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float2 uv = input.texcoord;
    
    half4 Occlusion = SampleBlitTexture(uv);
    half3 ao = Occlusion.xxx;

    // half3 albedo = SampleDiffuse(uv);//Diffuse..
    half3 albedo = 0.8;
    ao = MultiBounce(albedo, ao);

    return ao.xyzz;
}


half4 Fragment_DebugRO(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float2 uv = input.texcoord;
    
    half4 Occlusion = SampleBlitTexture(uv);
    half ro = Occlusion.y;

    // return Occlusion;
    return ro.xxxx;
}

half4 Fragment_DebugReflections(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float2 uv = input.texcoord;
    
    half4 ssrColor = SampleTexture(TEXTURE2D_ARGS(_CameraReflectionsTexture, sampler_CameraReflectionsTexture), uv);

    return ssrColor;
}