using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;

namespace UnityEngine.PBD
{
    [GenerateTestsForBurstCompatibility]
    public struct PBDBounds
    {
        public float3 Min;
        public float3 Max;
        public float3 Center => (Min + Max) * 0.5f;
        public float3 Size   => Max - Min;

        public float Volume
        {
            get
            {
                float3 size = Max - Min;
                return size.x * size.y * size.z;
            }
        }
    }
}