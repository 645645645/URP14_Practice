using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;

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
        public static unsafe float3 TrilinearStandard(in float3 prePos, ref float* X, ref float* Y, ref float* Z, in int3 dim)
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
        
        [ExcludeFromBurstCompatTesting("Task out")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe VInt TrilinearStandard(in VInt3 prePos, ref VInt* data, int3 dim)
        {
            VInt3 min = FixedPointUtils.Floor(prePos);

            VInt3 frac    = prePos              - min;
            VInt3 invFrac = FixedPointUtils.one - frac;

            // 提取分量
            VInt ifx = invFrac.x, fx = frac.x;
            VInt ify = invFrac.y, fy = frac.y;
            VInt ifz = invFrac.z, fz = frac.z;

            int4 stride = GetStride(dim);

            (int stepX, int stepY, int stepZ) = (stride.x, stride.y, stride.z);

            int p000 = FixedPointUtils.Fixed2Int(GetIndex(min.x, min.y, min.z, dim));
            int p100 = p000 + stepX;
            int p010 = p000 + stepY;
            int p110 = p100 + stepY;
            int p001 = p000 + stepZ;
            int p101 = p100 + stepZ;
            int p011 = p010 + stepZ;
            int p111 = p110 + stepZ;

            //朴实无华
            VInt v000 = data[p000];

            VInt v100 = data[p100];

            VInt v010 = data[p010];

            VInt v110 = data[p110];

            VInt v001 = data[p001];

            VInt v101 = data[p101];

            VInt v011 = data[p011];

            VInt v111 = data[p111];

            // 1. X方向插值
            VInt x00 = v000 * ifx + v100 * fx; // Z=min.z, Y=min.y
            VInt x01 = v001 * ifx + v101 * fx; // Z=min.z+1, Y=min.y
            VInt x10 = v010 * ifx + v110 * fx; // Z=min.z, Y=min.y+1
            VInt x11 = v011 * ifx + v111 * fx; // Z=min.z+1, Y=min.y+1

            // 2. Y方向插值
            VInt y0 = x00 * ify + x01 * fy; // Z=min.z
            VInt y1 = x10 * ify + x11 * fy; // Z=min.z+1

            // 3. Z方向插值
            return y0 * ifz + y1 * fz;
        }
        
        [ExcludeFromBurstCompatTesting("Task out")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe VInt3 TrilinearStandard(in VInt3 prePos, ref VInt* X, ref VInt* Y, ref VInt* Z, in int3 dim)
        {
            VInt3 min = FixedPointUtils.Floor(prePos);

            VInt3 frac    = prePos              - min;
            VInt3 invFrac = FixedPointUtils.one - frac;

            // 提取分量
            VInt ifx = invFrac.x, fx = frac.x;
            VInt ify = invFrac.y, fy = frac.y;
            VInt ifz = invFrac.z, fz = frac.z;

            int4 stride = GetStride(dim);

            (int stepX, int stepY, int stepZ) = (stride.x, stride.y, stride.z);

            int p000 = FixedPointUtils.Fixed2Int(GetIndex(min.x, min.y, min.z, dim));
            int p100 = p000 + stepX;
            int p010 = p000 + stepY;
            int p110 = p100 + stepY;
            int p001 = p000 + stepZ;
            int p101 = p100 + stepZ;
            int p011 = p010 + stepZ;
            int p111 = p110 + stepZ;


            //朴实无华
            VInt3 v000 = new(X[p000], Y[p000], Z[p000]);

            VInt3 v100 = new(X[p100], Y[p100], Z[p100]);

            VInt3 v010 = new(X[p010], Y[p010], Z[p010]);

            VInt3 v110 = new(X[p110], Y[p110], Z[p110]);

            VInt3 v001 = new(X[p001], Y[p001], Z[p001]);

            VInt3 v101 = new(X[p101], Y[p101], Z[p101]);

            VInt3 v011 = new(X[p011], Y[p011], Z[p011]);

            VInt3 v111 = new(X[p111], Y[p111], Z[p111]);

            // 1. X方向插值
            VInt3 x00 = v000 * ifx + v100 * fx; // Z=min.z, Y=min.y
            VInt3 x01 = v001 * ifx + v101 * fx; // Z=min.z+1, Y=min.y
            VInt3 x10 = v010 * ifx + v110 * fx; // Z=min.z, Y=min.y+1
            VInt3 x11 = v011 * ifx + v111 * fx; // Z=min.z+1, Y=min.y+1

            // 2. Y方向插值
            VInt3 y0 = x00 * ify + x01 * fy; // Z=min.z
            VInt3 y1 = x10 * ify + x11 * fy; // Z=min.z+1

            // 3. Z方向插值
            return y0 * ifz + y1 * fz;
        }

        [ExcludeFromBurstCompatTesting("Task out")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void TrilinearStandardWrite(in float3 prePos, ref float* X,        ref float* Y, ref float* Z, in int3 dim,
                                                         in float3 energy)
        {
            int3 min = (int3)math.floor(prePos);

            float3 frac    = prePos              - min;
            float3 invFrac = 1 - frac;

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

            float w000 = ifx * ify * ifz,
                  w100 = fx  * ify * ifz,
                  w010 = ifx * fy  * ifz,
                  w110 = fx  * fy  * ifz,
                  w001 = ifx * ify * fz,
                  w101 = fx  * ify * fz,
                  w011 = ifx * fy  * fz,
                  w111 = fx  * fy  * fz;

            float3 e000 = energy * w000,
                  e100 = energy * w100,
                  e010 = energy * w010,
                  e110 = energy * w110,
                  e001 = energy * w001,
                  e101 = energy * w101,
                  e011 = energy * w011,
                  e111 = energy * w111;

            (X[p000], Y[p000], Z[p000]) = (X[p000] + e000.x,
                                           Y[p000] + e000.y,
                                           Z[p000] + e000.z);
            
            (X[p100], Y[p100], Z[p100]) = (X[p100] + e100.x,
                                           Y[p100] + e100.y,
                                           Z[p100] + e100.z);
            
            (X[p010], Y[p010], Z[p010]) = (X[p010] + e010.x,
                                           Y[p010] + e010.y,
                                           Z[p010] + e010.z);
            
            (X[p110], Y[p110], Z[p110]) = (X[p110] + e110.x,
                                           Y[p110] + e110.y,
                                           Z[p110] + e110.z);
            
            (X[p001], Y[p001], Z[p001]) = (X[p001] + e001.x,
                                           Y[p001] + e001.y,
                                           Z[p001] + e001.z);
            
            (X[p101], Y[p101], Z[p101]) = (X[p101] + e101.x,
                                           Y[p101] + e101.y,
                                           Z[p101] + e101.z);
            
            (X[p011], Y[p011], Z[p011]) = (X[p011] + e011.x,
                                           Y[p011] + e011.y,
                                           Z[p011] + e011.z);
            
            (X[p111], Y[p111], Z[p111]) = (X[p111] + e111.x,
                                           Y[p111] + e111.y,
                                           Z[p111] + e111.z);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void TrilinearStandardWrite(in VInt3 prePos, ref VInt* X,        ref VInt* Y, ref VInt* Z, in int3 dim,
                                                         in VInt3 energy, int       minValue, int       maxValue)
        {
            VInt3 min = FixedPointUtils.Floor(prePos);

            VInt3 frac    = prePos              - min;
            VInt3 invFrac = FixedPointUtils.one - frac;

            // 提取分量
            VInt ifx = invFrac.x, fx = frac.x;
            VInt ify = invFrac.y, fy = frac.y;
            VInt ifz = invFrac.z, fz = frac.z;

            int4 stride = GetStride(dim);

            (int stepX, int stepY, int stepZ) = (stride.x, stride.y, stride.z);

            int p000 = FixedPointUtils.Fixed2Int(GetIndex(min.x, min.y, min.z, dim));
            int p100 = p000 + stepX;
            int p010 = p000 + stepY;
            int p110 = p100 + stepY;
            int p001 = p000 + stepZ;
            int p101 = p100 + stepZ;
            int p011 = p010 + stepZ;
            int p111 = p110 + stepZ;

            VInt w000 = ifx * ify * ifz,
                 w100 = fx  * ify * ifz,
                 w010 = ifx * fy  * ifz,
                 w110 = fx  * fy  * ifz,
                 w001 = ifx * ify * fz,
                 w101 = fx  * ify * fz,
                 w011 = ifx * fy  * fz,
                 w111 = fx  * fy  * fz;

            VInt3 e000 = energy * w000,
                  e100 = energy * w100,
                  e010 = energy * w010,
                  e110 = energy * w110,
                  e001 = energy * w001,
                  e101 = energy * w101,
                  e011 = energy * w011,
                  e111 = energy * w111;

            (X[p000], Y[p000], Z[p000]) = (FixedPointUtils.Clamp(X[p000] + e000.x, minValue, maxValue),
                                           FixedPointUtils.Clamp(Y[p000] + e000.y, minValue, maxValue),
                                           FixedPointUtils.Clamp(Z[p000] + e000.z, minValue, maxValue));
            
            (X[p100], Y[p100], Z[p100]) = (FixedPointUtils.Clamp(X[p100] + e100.x, minValue, maxValue),
                                           FixedPointUtils.Clamp(Y[p100] + e100.y, minValue, maxValue),
                                           FixedPointUtils.Clamp(Z[p100] + e100.z, minValue, maxValue));
            
            (X[p010], Y[p010], Z[p010]) = (FixedPointUtils.Clamp(X[p010] + e010.x, minValue, maxValue),
                                           FixedPointUtils.Clamp(Y[p010] + e010.y, minValue, maxValue),
                                           FixedPointUtils.Clamp(Z[p010] + e010.z, minValue, maxValue));
            
            (X[p110], Y[p110], Z[p110]) = (FixedPointUtils.Clamp(X[p110] + e110.x, minValue, maxValue),
                                           FixedPointUtils.Clamp(Y[p110] + e110.y, minValue, maxValue),
                                           FixedPointUtils.Clamp(Z[p110] + e110.z, minValue, maxValue));
            
            (X[p001], Y[p001], Z[p001]) = (FixedPointUtils.Clamp(X[p001] + e001.x, minValue, maxValue),
                                           FixedPointUtils.Clamp(Y[p001] + e001.y, minValue, maxValue),
                                           FixedPointUtils.Clamp(Z[p001] + e001.z, minValue, maxValue));
            
            (X[p101], Y[p101], Z[p101]) = (FixedPointUtils.Clamp(X[p101] + e101.x, minValue, maxValue),
                                           FixedPointUtils.Clamp(Y[p101] + e101.y, minValue, maxValue),
                                           FixedPointUtils.Clamp(Z[p101] + e101.z, minValue, maxValue));
            
            (X[p011], Y[p011], Z[p011]) = (FixedPointUtils.Clamp(X[p011] + e011.x, minValue, maxValue),
                                           FixedPointUtils.Clamp(Y[p011] + e011.y, minValue, maxValue),
                                           FixedPointUtils.Clamp(Z[p011] + e011.z, minValue, maxValue));
            
            (X[p111], Y[p111], Z[p111]) = (FixedPointUtils.Clamp(X[p111] + e111.x, minValue, maxValue),
                                           FixedPointUtils.Clamp(Y[p111] + e111.y, minValue, maxValue),
                                           FixedPointUtils.Clamp(Z[p111] + e111.z, minValue, maxValue));
        }
        

        [ExcludeFromBurstCompatTesting("Task out")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void TrilinearStandardAtomicWrite(in float3 prePos, ref float* back,     in int3 dim,
                                                               in float  energy, float      minValue, float   maxValue)
        {
            int3 min = (int3)math.floor(prePos);

            float3 frac    = prePos - min;
            float3 invFrac = 1f     - frac;

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

            float w000 = ifx * ify * ifz,
                  w100 = fx  * ify * ifz,
                  w010 = ifx * fy  * ifz,
                  w110 = fx  * fy  * ifz,
                  w001 = ifx * ify * fz,
                  w101 = fx  * ify * fz,
                  w011 = ifx * fy  * fz,
                  w111 = fx  * fy  * fz;

            float e000 = energy * w000,
                  e100 = energy * w100,
                  e010 = energy * w010,
                  e110 = energy * w110,
                  e001 = energy * w001,
                  e101 = energy * w101,
                  e011 = energy * w011,
                  e111 = energy * w111;

            AtomicAddSat(ref back, p000, e000, minValue, maxValue);
            AtomicAddSat(ref back, p100, e100, minValue, maxValue);
            AtomicAddSat(ref back, p010, e010, minValue, maxValue);
            AtomicAddSat(ref back, p110, e110, minValue, maxValue);
            AtomicAddSat(ref back, p001, e001, minValue, maxValue);
            AtomicAddSat(ref back, p101, e101, minValue, maxValue);
            AtomicAddSat(ref back, p011, e011, minValue, maxValue);
            AtomicAddSat(ref back, p111, e111, minValue, maxValue);
        }
        

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void TrilinearStandardAtomicWrite(in VInt3 prePos, ref VInt* back,     in int3 dim,
                                                               in VInt energy, int       minValue, int     maxValue)
        {
            VInt3 min = FixedPointUtils.Floor(prePos);

            VInt3 frac    = prePos              - min;
            VInt3 invFrac = FixedPointUtils.one - frac;

            // 提取分量
            VInt ifx = invFrac.x, fx = frac.x;
            VInt ify = invFrac.y, fy = frac.y;
            VInt ifz = invFrac.z, fz = frac.z;

            int4 stride = GetStride(dim);

            (int stepX, int stepY, int stepZ) = (stride.x, stride.y, stride.z);

            int p000 = FixedPointUtils.Fixed2Int(GetIndex(min.x, min.y, min.z, dim));
            int p100 = p000 + stepX;
            int p010 = p000 + stepY;
            int p110 = p100 + stepY;
            int p001 = p000 + stepZ;
            int p101 = p100 + stepZ;
            int p011 = p010 + stepZ;
            int p111 = p110 + stepZ;

            VInt w000 = ifx * ify * ifz,
                 w100 = fx  * ify * ifz,
                 w010 = ifx * fy  * ifz,
                 w110 = fx  * fy  * ifz,
                 w001 = ifx * ify * fz,
                 w101 = fx  * ify * fz,
                 w011 = ifx * fy  * fz,
                 w111 = fx  * fy  * fz;

            VInt e000 = energy * w000,
                  e100 = energy * w100,
                  e010 = energy * w010,
                  e110 = energy * w110,
                  e001 = energy * w001,
                  e101 = energy * w101,
                  e011 = energy * w011,
                  e111 = energy * w111;

            AtomicAddSat(ref back, p000, e000.i, minValue, maxValue);
            AtomicAddSat(ref back, p100, e100.i, minValue, maxValue);
            AtomicAddSat(ref back, p010, e010.i, minValue, maxValue);
            AtomicAddSat(ref back, p110, e110.i, minValue, maxValue);
            AtomicAddSat(ref back, p001, e001.i, minValue, maxValue);
            AtomicAddSat(ref back, p101, e101.i, minValue, maxValue);
            AtomicAddSat(ref back, p011, e011.i, minValue, maxValue);
            AtomicAddSat(ref back, p111, e111.i, minValue, maxValue);
        }


        [ExcludeFromBurstCompatTesting("Task out")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void TrilinearStandardAtomicWrite(in float3 prePos, ref float* X,        ref float* Y, ref float* Z, 
                                                               in int3   dim,    in float3 energy, float      minValue, float      maxValue)
        {
            int3 min = (int3)math.floor(prePos);

            float3 frac    = prePos - min;
            float3 invFrac = 1f     - frac;

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

            float w000 = ifx * ify * ifz,
                  w100 = fx  * ify * ifz,
                  w010 = ifx * fy  * ifz,
                  w110 = fx  * fy  * ifz,
                  w001 = ifx * ify * fz,
                  w101 = fx  * ify * fz,
                  w011 = ifx * fy  * fz,
                  w111 = fx  * fy  * fz;

            float3 e000 = energy * w000,
                  e100 = energy * w100,
                  e010 = energy * w010,
                  e110 = energy * w110,
                  e001 = energy * w001,
                  e101 = energy * w101,
                  e011 = energy * w011,
                  e111 = energy * w111;

            AtomicAddSat(ref X, p000, e000.x, minValue, maxValue);
            AtomicAddSat(ref X, p100, e100.x, minValue, maxValue);
            AtomicAddSat(ref X, p010, e010.x, minValue, maxValue);
            AtomicAddSat(ref X, p110, e110.x, minValue, maxValue);
            AtomicAddSat(ref X, p001, e001.x, minValue, maxValue);
            AtomicAddSat(ref X, p101, e101.x, minValue, maxValue);
            AtomicAddSat(ref X, p011, e011.x, minValue, maxValue);
            AtomicAddSat(ref X, p111, e111.x, minValue, maxValue);

            AtomicAddSat(ref Y, p000, e000.y, minValue, maxValue);
            AtomicAddSat(ref Y, p100, e100.y, minValue, maxValue);
            AtomicAddSat(ref Y, p010, e010.y, minValue, maxValue);
            AtomicAddSat(ref Y, p110, e110.y, minValue, maxValue);
            AtomicAddSat(ref Y, p001, e001.y, minValue, maxValue);
            AtomicAddSat(ref Y, p101, e101.y, minValue, maxValue);
            AtomicAddSat(ref Y, p011, e011.y, minValue, maxValue);
            AtomicAddSat(ref Y, p111, e111.y, minValue, maxValue);
            
            AtomicAddSat(ref Z, p000, e000.z, minValue, maxValue);
            AtomicAddSat(ref Z, p100, e100.z, minValue, maxValue);
            AtomicAddSat(ref Z, p010, e010.z, minValue, maxValue);
            AtomicAddSat(ref Z, p110, e110.z, minValue, maxValue);
            AtomicAddSat(ref Z, p001, e001.z, minValue, maxValue);
            AtomicAddSat(ref Z, p101, e101.z, minValue, maxValue);
            AtomicAddSat(ref Z, p011, e011.z, minValue, maxValue);
            AtomicAddSat(ref Z, p111, e111.z, minValue, maxValue);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void TrilinearStandardAtomicWrite(in VInt3 prePos, ref VInt* X,        ref VInt* Y, ref VInt* Z,
                                                               in int3  dim,    in VInt3 energy, int       minValue, int       maxValue)
        {
            VInt3 min = FixedPointUtils.Floor(prePos);

            VInt3 frac    = prePos              - min;
            VInt3 invFrac = FixedPointUtils.one - frac;

            // 提取分量
            VInt ifx = invFrac.x, fx = frac.x;
            VInt ify = invFrac.y, fy = frac.y;
            VInt ifz = invFrac.z, fz = frac.z;

            int4 stride = GetStride(dim);

            (int stepX, int stepY, int stepZ) = (stride.x, stride.y, stride.z);

            int p000 = FixedPointUtils.Fixed2Int(GetIndex(min.x, min.y, min.z, dim));
            int p100 = p000 + stepX;
            int p010 = p000 + stepY;
            int p110 = p100 + stepY;
            int p001 = p000 + stepZ;
            int p101 = p100 + stepZ;
            int p011 = p010 + stepZ;
            int p111 = p110 + stepZ;

            VInt w000 = ifx * ify * ifz,
                 w100 = fx  * ify * ifz,
                 w010 = ifx * fy  * ifz,
                 w110 = fx  * fy  * ifz,
                 w001 = ifx * ify * fz,
                 w101 = fx  * ify * fz,
                 w011 = ifx * fy  * fz,
                 w111 = fx  * fy  * fz;

            VInt3 e000 = energy * w000,
                  e100 = energy * w100,
                  e010 = energy * w010,
                  e110 = energy * w110,
                  e001 = energy * w001,
                  e101 = energy * w101,
                  e011 = energy * w011,
                  e111 = energy * w111;

            AtomicAddSat(ref X, p000, e000.x, minValue, maxValue);
            AtomicAddSat(ref X, p100, e100.x, minValue, maxValue);
            AtomicAddSat(ref X, p010, e010.x, minValue, maxValue);
            AtomicAddSat(ref X, p110, e110.x, minValue, maxValue);
            AtomicAddSat(ref X, p001, e001.x, minValue, maxValue);
            AtomicAddSat(ref X, p101, e101.x, minValue, maxValue);
            AtomicAddSat(ref X, p011, e011.x, minValue, maxValue);
            AtomicAddSat(ref X, p111, e111.x, minValue, maxValue);

            AtomicAddSat(ref Y, p000, e000.y, minValue, maxValue);
            AtomicAddSat(ref Y, p100, e100.y, minValue, maxValue);
            AtomicAddSat(ref Y, p010, e010.y, minValue, maxValue);
            AtomicAddSat(ref Y, p110, e110.y, minValue, maxValue);
            AtomicAddSat(ref Y, p001, e001.y, minValue, maxValue);
            AtomicAddSat(ref Y, p101, e101.y, minValue, maxValue);
            AtomicAddSat(ref Y, p011, e011.y, minValue, maxValue);
            AtomicAddSat(ref Y, p111, e111.y, minValue, maxValue);
            
            AtomicAddSat(ref Z, p000, e000.z, minValue, maxValue);
            AtomicAddSat(ref Z, p100, e100.z, minValue, maxValue);
            AtomicAddSat(ref Z, p010, e010.z, minValue, maxValue);
            AtomicAddSat(ref Z, p110, e110.z, minValue, maxValue);
            AtomicAddSat(ref Z, p001, e001.z, minValue, maxValue);
            AtomicAddSat(ref Z, p101, e101.z, minValue, maxValue);
            AtomicAddSat(ref Z, p011, e011.z, minValue, maxValue);
            AtomicAddSat(ref Z, p111, e111.z, minValue, maxValue);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int AtomicAddSat(ref VInt* address, int index, int value, int minValue, int maxValue)
        {
            if (value == 0)
                return value;
            
            ref int target = ref (address + index)->i;

            int oldVal,
                newVal = target;

            if (value > 0)    
            {
                do
                {
                    oldVal = newVal;
                    newVal = math.min(newVal + value, maxValue);
                    newVal = Interlocked.CompareExchange(ref target, newVal, oldVal);
                } while (oldVal != newVal && oldVal < maxValue);
            }
            else
            {
                do
                {
                    oldVal = newVal;
                    newVal = math.max(newVal + value, minValue);
                    newVal = Interlocked.CompareExchange(ref target, newVal, oldVal);
                } while (oldVal != newVal && oldVal > minValue);
            }

            return oldVal;
        }
        
        [ExcludeFromBurstCompatTesting("Task out")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe float AtomicAddSat(ref float* address, int index, float value, float minValue, float maxValue)
        {
            if (math.abs(value) < math.EPSILON)
                return value;
            
            ref float target = ref address[index];

            float oldVal,
                newVal = target;

            if (value > 0)    
            {
                do
                {
                    oldVal = newVal;
                    newVal = math.min(newVal + value, maxValue);
                    newVal = Interlocked.CompareExchange(ref target, newVal, oldVal);
                } while (!Mathf.Approximately(oldVal , newVal) && oldVal < maxValue);
            }
            else
            {
                do
                {
                    oldVal = newVal;
                    newVal = math.max(newVal + value, minValue);
                    newVal = Interlocked.CompareExchange(ref target, newVal, oldVal);
                } while (!Mathf.Approximately(oldVal , newVal) && oldVal > minValue);
            }

            return oldVal;
        }
        
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void SetBoundary(ref float* data, int b, in int4 stride, in int3 N, in int3 dim)
        {
            SetFaceBoundaries(ref data, b, in stride, in N, in dim);
            SetEdgeBoundaries(ref data, b, in stride, in N, in dim);
            SetCornerBoundaries(ref data, b, in stride, in N, in dim);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void SetFaceBoundaries(ref float* data, int b, in int4 stride, in int3 N, in int3 dim)
        {
            switch (b)
            {
                case 1:
                    //设置内面x6
                    for (int y = 1; y <= N.y; y++)
                    {
                        for (int z = 1; z <= N.z; z++)
                        {
                            //int x = 0;
                            //int x = n.x + 1;
                            int index = GetIndex(0, y, z, dim);
                            data[index] = -data[index + stride.x];

                            index       += stride.x * N.x + stride.x;
                            data[index] =  -data[index - stride.x];
                        }

                        for (int x = 1; x <= N.x; x++)
                        {
                            //int z = 0;
                            //int z = n.z + 1;
                            int index = GetIndex(x, y, 0, dim);
                            data[index] = data[index + stride.z];

                            index       += stride.z * N.z + stride.z;
                            data[index] =  data[index - stride.z];
                        }
                    }

                    for (int z = 1; z <= N.z; z++)
                    {
                        for (int x = 1; x <= N.x; x++)
                        {
                            //int y = 0;
                            //int y = n.y + 1;
                            int index = GetIndex(x, 0, z, dim);
                            data[index] = data[index + stride.y];

                            index       += stride.y * N.y + stride.y;
                            data[index] =  data[index - stride.y];
                        }
                    }
                    break;
                case 2:
                    for (int y = 1; y <= N.y; y++)
                    {
                        for (int z = 1; z <= N.z; z++)
                        {
                            //int x = 0;
                            //int x = n.x + 1;
                            int index = GetIndex(0, y, z, dim);
                            data[index] = data[index + stride.x];

                            index       += stride.x * N.x + stride.x;
                            data[index] =  data[index - stride.x];
                        }

                        for (int x = 1; x <= N.x; x++)
                        {
                            //int z = 0;
                            //int z = n.z + 1;
                            int index = GetIndex(x, y, 0, dim);
                            data[index] = data[index + stride.z];

                            index       += stride.z * N.z + stride.z;
                            data[index] =  data[index - stride.z];
                        }
                    }

                    for (int z = 1; z <= N.z; z++)
                    {
                        for (int x = 1; x <= N.x; x++)
                        {
                            //int y = 0;
                            //int y = n.y + 1;
                            int index = GetIndex(x, 0, z, dim);
                            data[index] = -data[index + stride.y];

                            index       += stride.y * N.y + stride.y;
                            data[index] =  -data[index - stride.y];
                        }
                    }
                    break;
                case 3:
                    for (int y = 1; y <= N.y; y++)
                    {
                        for (int z = 1; z <= N.z; z++)
                        {
                            //int x = 0;
                            //int x = n.x + 1;
                            int index = GetIndex(0, y, z, dim);
                            data[index] = data[index + stride.x];

                            index       += stride.x * N.x + stride.x;
                            data[index] =  data[index - stride.x];
                        }

                        for (int x = 1; x <= N.x; x++)
                        {
                            //int z = 0;
                            //int z = n.z + 1;
                            int index = GetIndex(x, y, 0, dim);
                            data[index] = -data[index + stride.z];

                            index       += stride.z * N.z + stride.z;
                            data[index] =  -data[index - stride.z];
                        }
                    }

                    for (int z = 1; z <= N.z; z++)
                    {
                        for (int x = 1; x <= N.x; x++)
                        {
                            //int y = 0;
                            //int y = n.y + 1;
                            int index = GetIndex(x, 0, z, dim);
                            data[index] = data[index + stride.y];

                            index       += stride.y * N.y + stride.y;
                            data[index] =  data[index - stride.y];
                        }
                    }
                    break;
                case 0:
                default:
                    for (int y = 1; y <= N.y; y++)
                    {
                        for (int z = 1; z <= N.z; z++)
                        {
                            //int x = 0;
                            //int x = n.x + 1;
                            int index = GetIndex(0, y, z, dim);
                            data[index] = data[index + stride.x];

                            index       += stride.x * N.x + stride.x;
                            data[index] =  data[index - stride.x];
                        }

                        for (int x = 1; x <= N.x; x++)
                        {
                            //int z = 0;
                            //int z = n.z + 1;
                            int index = GetIndex(x, y, 0, dim);
                            data[index] = data[index + stride.z];

                            index       += stride.z * N.z + stride.z;
                            data[index] =  data[index - stride.z];
                        }
                    }

                    for (int z = 1; z <= N.z; z++)
                    {
                        for (int x = 1; x <= N.x; x++)
                        {
                            //int y = 0;
                            //int y = n.y + 1;
                            int index = GetIndex(x, 0, z, dim);
                            data[index] = data[index + stride.y];

                            index       += stride.y * N.y + stride.y;
                            data[index] =  data[index - stride.y];
                        }
                    }

                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void SetEdgeBoundaries(ref float* data, int b, in int4 stride, in int3 N, in int3 dim)
        {
            //设置边x12
            for (int y = 1; y <= N.y; y++)
            {
                int4 indexes = new int4()
                {
                    x = GetIndex(0, y, 0, dim),

                    y = GetIndex(N.x + 1, y, 0, dim),

                    z = GetIndex(0, y, N.z + 1, dim),

                    w = GetIndex(N.x + 1, y, N.z + 1, dim)
                };

                data[indexes.x] = (data[indexes.x + stride.x] + data[indexes.x + stride.z]) * 0.5f;
                data[indexes.y] = (data[indexes.y - stride.x] + data[indexes.y + stride.z]) * 0.5f;
                data[indexes.z] = (data[indexes.z + stride.x] + data[indexes.z - stride.z]) * 0.5f;
                data[indexes.w] = (data[indexes.w - stride.x] + data[indexes.w - stride.z]) * 0.5f;
            }

            for (int z = 1; z <= N.y; z++)
            {
                int4 indexes = new int4()
                {
                    x = GetIndex(0, 0, z, dim),

                    y = GetIndex(N.x + 1, 0, z, dim),

                    z = GetIndex(0, N.y + 1, z, dim),

                    w = GetIndex(N.x + 1, N.y + 1, z, dim)
                };

                data[indexes.x] = (data[indexes.x + stride.x] + data[indexes.x + stride.y]) * 0.5f;
                data[indexes.y] = (data[indexes.y - stride.x] + data[indexes.y + stride.y]) * 0.5f;
                data[indexes.z] = (data[indexes.z + stride.x] + data[indexes.z - stride.y]) * 0.5f;
                data[indexes.w] = (data[indexes.w - stride.x] + data[indexes.w - stride.y]) * 0.5f;
            }

            for (int x = 1; x <= N.y; x++)
            {
                int4 indexes = new int4()
                {
                    x = GetIndex(x, 0, 0, dim),

                    y = GetIndex(x, N.y + 1, 0, dim),

                    z = GetIndex(x, 0, N.z + 1, dim),

                    w = GetIndex(x, N.y + 1, N.z + 1, dim)
                };

                data[indexes.x] = (data[indexes.x + stride.y] + data[indexes.x + stride.z]) * 0.5f;
                data[indexes.y] = (data[indexes.y - stride.y] + data[indexes.y + stride.z]) * 0.5f;
                data[indexes.z] = (data[indexes.z + stride.y] + data[indexes.z - stride.z]) * 0.5f;
                data[indexes.w] = (data[indexes.w - stride.y] + data[indexes.w - stride.z]) * 0.5f;
            }
        }

        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void SetCornerBoundaries(ref float* data, int b, in int4 stride, in int3 N, in int3 dim)
        {
            //设置角x8
            
            for (int y = 0; y < N.y + 1; y += N.y + 1)
            {
                for (int z = 0; z < N.z + 1; z += N.z + 1)
                {
                    for (int x = 0; x < N.x + 1; x += N.x + 1)
                    {
                        int index = GetIndex(x, y, z, dim);
                        data[index] = (data[index + math.select(-stride.x, stride.x, x == 0)] +
                                       data[index + math.select(-stride.y, stride.y, y == 0)] +
                                       data[index + math.select(-stride.z, stride.z, z == 0)]) * 0.3333333f;
                    }
                }
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void SetBoundary(ref VInt* data, int b, in int4 stride, in int3 N, in int3 dim)
        {
            SetFaceBoundaries(ref data, b, in stride, in N, in dim);
            SetEdgeBoundaries(ref data, b, in stride, in N, in dim);
            SetCornerBoundaries(ref data, b, in stride, in N, in dim);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void SetFaceBoundaries(ref VInt* data, int b, in int4 stride, in int3 N, in int3 dim)
        {
            switch (b)
            {
                case 1:
                    //设置内面x6
                    for (int y = 1; y <= N.y; y++)
                    {
                        for (int z = 1; z <= N.z; z++)
                        {
                            //int x = 0;
                            //int x = n.x + 1;
                            int index = GetIndex(0, y, z, dim);
                            data[index] = -data[index + stride.x];

                            index       += stride.x * N.x + stride.x;
                            data[index] =  -data[index - stride.x];
                        }

                        for (int x = 1; x <= N.x; x++)
                        {
                            //int z = 0;
                            //int z = n.z + 1;
                            int index = GetIndex(x, y, 0, dim);
                            data[index] = data[index + stride.z];

                            index       += stride.z * N.z + stride.z;
                            data[index] =  data[index - stride.z];
                        }
                    }

                    for (int z = 1; z <= N.z; z++)
                    {
                        for (int x = 1; x <= N.x; x++)
                        {
                            //int y = 0;
                            //int y = n.y + 1;
                            int index = GetIndex(x, 0, z, dim);
                            data[index] = data[index + stride.y];

                            index       += stride.y * N.y + stride.y;
                            data[index] =  data[index - stride.y];
                        }
                    }
                    break;
                case 2:
                    for (int y = 1; y <= N.y; y++)
                    {
                        for (int z = 1; z <= N.z; z++)
                        {
                            //int x = 0;
                            //int x = n.x + 1;
                            int index = GetIndex(0, y, z, dim);
                            data[index] = data[index + stride.x];

                            index       += stride.x * N.x + stride.x;
                            data[index] =  data[index - stride.x];
                        }

                        for (int x = 1; x <= N.x; x++)
                        {
                            //int z = 0;
                            //int z = n.z + 1;
                            int index = GetIndex(x, y, 0, dim);
                            data[index] = data[index + stride.z];

                            index       += stride.z * N.z + stride.z;
                            data[index] =  data[index - stride.z];
                        }
                    }

                    for (int z = 1; z <= N.z; z++)
                    {
                        for (int x = 1; x <= N.x; x++)
                        {
                            //int y = 0;
                            //int y = n.y + 1;
                            int index = GetIndex(x, 0, z, dim);
                            data[index] = -data[index + stride.y];

                            index       += stride.y * N.y + stride.y;
                            data[index] =  -data[index - stride.y];
                        }
                    }
                    break;
                case 3:
                    for (int y = 1; y <= N.y; y++)
                    {
                        for (int z = 1; z <= N.z; z++)
                        {
                            //int x = 0;
                            //int x = n.x + 1;
                            int index = GetIndex(0, y, z, dim);
                            data[index] = data[index + stride.x];

                            index       += stride.x * N.x + stride.x;
                            data[index] =  data[index - stride.x];
                        }

                        for (int x = 1; x <= N.x; x++)
                        {
                            //int z = 0;
                            //int z = n.z + 1;
                            int index = GetIndex(x, y, 0, dim);
                            data[index] = -data[index + stride.z];

                            index       += stride.z * N.z + stride.z;
                            data[index] =  -data[index - stride.z];
                        }
                    }

                    for (int z = 1; z <= N.z; z++)
                    {
                        for (int x = 1; x <= N.x; x++)
                        {
                            //int y = 0;
                            //int y = n.y + 1;
                            int index = GetIndex(x, 0, z, dim);
                            data[index] = data[index + stride.y];

                            index       += stride.y * N.y + stride.y;
                            data[index] =  data[index - stride.y];
                        }
                    }
                    break;
                case 0:
                default:
                    for (int y = 1; y <= N.y; y++)
                    {
                        for (int z = 1; z <= N.z; z++)
                        {
                            //int x = 0;
                            //int x = n.x + 1;
                            int index = GetIndex(0, y, z, dim);
                            data[index] = data[index + stride.x];

                            index       += stride.x * N.x + stride.x;
                            data[index] =  data[index - stride.x];
                        }

                        for (int x = 1; x <= N.x; x++)
                        {
                            //int z = 0;
                            //int z = n.z + 1;
                            int index = GetIndex(x, y, 0, dim);
                            data[index] = data[index + stride.z];

                            index       += stride.z * N.z + stride.z;
                            data[index] =  data[index - stride.z];
                        }
                    }

                    for (int z = 1; z <= N.z; z++)
                    {
                        for (int x = 1; x <= N.x; x++)
                        {
                            //int y = 0;
                            //int y = n.y + 1;
                            int index = GetIndex(x, 0, z, dim);
                            data[index] = data[index + stride.y];

                            index       += stride.y * N.y + stride.y;
                            data[index] =  data[index - stride.y];
                        }
                    }

                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void SetEdgeBoundaries(ref VInt* data, int b, in int4 stride, in int3 N, in int3 dim)
        {
            //设置边x12
            for (int y = 1; y <= N.y; y++)
            {
                int4 indexes = new int4()
                {
                    x = GetIndex(0, y, 0, dim),

                    y = GetIndex(N.x + 1, y, 0, dim),

                    z = GetIndex(0, y, N.z + 1, dim),

                    w = GetIndex(N.x + 1, y, N.z + 1, dim)
                };

                data[indexes.x] = (data[indexes.x + stride.x] + data[indexes.x + stride.z]) / 2;
                data[indexes.y] = (data[indexes.y - stride.x] + data[indexes.y + stride.z]) / 2;
                data[indexes.z] = (data[indexes.z + stride.x] + data[indexes.z - stride.z]) / 2;
                data[indexes.w] = (data[indexes.w - stride.x] + data[indexes.w - stride.z]) / 2;
            }

            for (int z = 1; z <= N.y; z++)
            {
                int4 indexes = new int4()
                {
                    x = GetIndex(0, 0, z, dim),

                    y = GetIndex(N.x + 1, 0, z, dim),

                    z = GetIndex(0, N.y + 1, z, dim),

                    w = GetIndex(N.x + 1, N.y + 1, z, dim)
                };

                data[indexes.x] = (data[indexes.x + stride.x] + data[indexes.x + stride.y]) / 2;
                data[indexes.y] = (data[indexes.y - stride.x] + data[indexes.y + stride.y]) / 2;
                data[indexes.z] = (data[indexes.z + stride.x] + data[indexes.z - stride.y]) / 2;
                data[indexes.w] = (data[indexes.w - stride.x] + data[indexes.w - stride.y]) / 2;
            }

            for (int x = 1; x <= N.y; x++)
            {
                int4 indexes = new int4()
                {
                    x = GetIndex(x, 0, 0, dim),

                    y = GetIndex(x, N.y + 1, 0, dim),

                    z = GetIndex(x, 0, N.z + 1, dim),

                    w = GetIndex(x, N.y + 1, N.z + 1, dim)
                };

                data[indexes.x] = (data[indexes.x + stride.y] + data[indexes.x + stride.z]) / 2;
                data[indexes.y] = (data[indexes.y - stride.y] + data[indexes.y + stride.z]) / 2;
                data[indexes.z] = (data[indexes.z + stride.y] + data[indexes.z - stride.z]) / 2;
                data[indexes.w] = (data[indexes.w - stride.y] + data[indexes.w - stride.z]) / 2;
            }
        }

        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void SetCornerBoundaries(ref VInt* data, int b, in int4 stride, in int3 N, in int3 dim)
        {
            //设置角x8
            
            for (int y = 0; y < N.y + 1; y += N.y + 1)
            {
                for (int z = 0; z < N.z + 1; z += N.z + 1)
                {
                    for (int x = 0; x < N.x + 1; x += N.x + 1)
                    {
                        int index = GetIndex(x, y, z, dim);
                        data[index] = (data[index + math.select(-stride.x, stride.x, x == 0)] +
                                       data[index + math.select(-stride.y, stride.y, y == 0)] +
                                       data[index + math.select(-stride.z, stride.z, z == 0)]) / 3;
                    }
                }
            }
        }
    }
}