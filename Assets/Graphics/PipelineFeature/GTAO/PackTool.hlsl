#pragma once

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

inline half2 UnitVectorToOctahedron(half3 N)
{
    half2 Oct = N.xy;
    if (N.z < 0)
    {
        Oct = (1 - abs(N.yx)) * half2(N.x >= 0 ? 1 : -1, N.y >= 0 ? 1 : -1);
    }
    return Oct;
}

inline half3 OctahedronToUnitVector(half2 Oct)
{
    half3 N = half3(Oct, 1 - dot(1, abs(Oct)));
    if (N.z < 0)
    {
        N.xy = (1 - abs(N.yx)) * (N.xy >= 0 ? half2(1, 1) : half2(-1, -1));
    }
    return normalize(N);
}