#pragma once

// // Octahedron Normal Vectors
// FVector2D OctNormal = (FVector2D)ResultNormal / FVector::DotProduct(FVector::OneVector, FMath::Abs(ResultNormal));
// if (ResultNormal.Z <= 0)
// {
//     OctNormal = (FVector2D(1,1) - FMath::Abs(FVector2D(OctNormal.Y, OctNormal.X)))
//     * FVector2D(OctNormal.X >= 0 ? 1 : -1, OctNormal.Y >= 0 ? 1 : -1);
// }

//八面体压缩到双通道


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
