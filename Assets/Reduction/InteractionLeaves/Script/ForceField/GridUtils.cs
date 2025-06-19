using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;

namespace UnityEngine.PBD
{
    //XZY
    [GenerateTestsForBurstCompatibility]
    public static class GridUtils
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetIndex(int3 gridPos, int3 dim)
        {
            return gridPos.x         +
                   gridPos.z * dim.x +
                   gridPos.y * dim.x * dim.z;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetIndex(int x, int y, int z, int3 dim)
        {
            return x         +
                   z * dim.x +
                   y * dim.x * dim.z;

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int3 GetGridPos(int index, int3 dim)
        {
            int batch = index / dim.x;

            int x = index % dim.x;

            int z = batch % dim.z;

            int y = batch / dim.z;
            return new int3(x, y, z);
        }

        /// <summary>
        /// w:三个方向上前进一格的offset
        /// </summary>
        /// <param name="dim"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int4 GetStride(int3 dim)
        {
            return new int4(1,
                            dim.x * dim.z,
                            dim.x,
                            1 + dim.x * dim.z + dim.x);
        }

        [ExcludeFromBurstCompatTesting("Task out")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe float TrilinearSimple(in float3 prePos, ref float* data, int3 dim)
        {
            int3   min     = (int3)prePos;
            float3 frac    = prePos - min;
            float3 invFrac = 1.0f   - frac;
            float  result  = 0;
            //分支多 弃用
            for (int i = 0; i < 2; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    for (int k = 0; k < 2; k++)
                    {
                        float weight = (i == 0 ? invFrac.x : frac.x) *
                                       (j == 0 ? invFrac.y : frac.y) *
                                       (k == 0 ? invFrac.z : frac.z);

                        result += weight * data[GetIndex(i, j, k, dim)];
                    }
                }
            }

            return result;
        }


        [ExcludeFromBurstCompatTesting("Task out")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe float TrilinearStandard(in float3 prePos, ref float* data, int3 dim)
        {
            int3 min = (int3)math.floor(prePos);

            float3 frac    = prePos - min;
            float3 invFrac = 1.0f   - frac;

            // 提取分量
            float ifx = invFrac.x, fx = frac.x;
            float ify = invFrac.y, fy = frac.y;
            float ifz = invFrac.z, fz = frac.z;

            int4 stride = GetStride(dim);

            (int stepX, int stepY, int stepZ) = (stride.x, stride.y, stride.z);

            int p000 = GetIndex(min.x, min.y, min.z, dim);
            int p100 = p000 + stepX;
            int p010 = p000 + stepY;
            int p110 = p100 + stepY;
            int p001 = p000 + stepZ;
            int p101 = p100 + stepZ;
            int p011 = p010 + stepZ;
            int p111 = p110 + stepZ;

            //朴实无华
            float v000 = data[p000];

            float v100 = data[p100];

            float v010 = data[p010];

            float v110 = data[p110];

            float v001 = data[p001];

            float v101 = data[p101];

            float v011 = data[p011];

            float v111 = data[p111];

            // 1. X方向插值
            float x00 = v000 * ifx + v100 * fx; // Z=min.z, Y=min.y
            float x01 = v001 * ifx + v101 * fx; // Z=min.z+1, Y=min.y
            float x10 = v010 * ifx + v110 * fx; // Z=min.z, Y=min.y+1
            float x11 = v011 * ifx + v111 * fx; // Z=min.z+1, Y=min.y+1

            // 2. Y方向插值
            float y0 = x00 * ify + x01 * fy; // Z=min.z
            float y1 = x10 * ify + x11 * fy; // Z=min.z+1

            // 3. Z方向插值
            return y0 * ifz + y1 * fz;
        }


        [ExcludeFromBurstCompatTesting("Task out")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe float3 TrilinearStandard(in float3 prePos, ref float* X, ref float* Y, ref float* Z, int3 dim)
        {
            int3 min = (int3)math.floor(prePos);

            float3 frac    = prePos - min;
            float3 invFrac = 1.0f   - frac;

            // 提取分量
            float ifx = invFrac.x, fx = frac.x;
            float ify = invFrac.y, fy = frac.y;
            float ifz = invFrac.z, fz = frac.z;

            int4 stride = GetStride(dim);

            (int stepX, int stepY, int stepZ) = (stride.x, stride.y, stride.z);

            int p000 = GetIndex(min.x, min.y, min.z, dim);
            int p100 = p000 + stepX;
            int p010 = p000 + stepY;
            int p110 = p100 + stepY;
            int p001 = p000 + stepZ;
            int p101 = p100 + stepZ;
            int p011 = p010 + stepZ;
            int p111 = p110 + stepZ;


            //朴实无华
            float3 v000 = new(X[p000], Y[p000], Z[p000]);

            float3 v100 = new(X[p100], Y[p100], Z[p100]);

            float3 v010 = new(X[p010], Y[p010], Z[p010]);

            float3 v110 = new(X[p110], Y[p110], Z[p110]);

            float3 v001 = new(X[p001], Y[p001], Z[p001]);

            float3 v101 = new(X[p101], Y[p101], Z[p101]);

            float3 v011 = new(X[p011], Y[p011], Z[p011]);

            float3 v111 = new(X[p111], Y[p111], Z[p111]);

            // 1. X方向插值
            float3 x00 = v000 * ifx + v100 * fx; // Z=min.z, Y=min.y
            float3 x01 = v001 * ifx + v101 * fx; // Z=min.z+1, Y=min.y
            float3 x10 = v010 * ifx + v110 * fx; // Z=min.z, Y=min.y+1
            float3 x11 = v011 * ifx + v111 * fx; // Z=min.z+1, Y=min.y+1

            // 2. Y方向插值
            float3 y0 = x00 * ify + x01 * fy; // Z=min.z
            float3 y1 = x10 * ify + x11 * fy; // Z=min.z+1

            // 3. Z方向插值
            return y0 * ifz + y1 * fz;
        }
    }
}