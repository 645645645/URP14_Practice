
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace UnityEngine.PBD
{
    public static unsafe class FixedPointUtils
    {
        public const float Scale        = 1f / ScaleInv;
        public const int   ScaleInv     = short.MaxValue;
        public const int   HalfScaleInv = ScaleInv / 2;

        public static readonly VInt3 half = new VInt3(HalfScaleInv, HalfScaleInv, HalfScaleInv);
        public static readonly VInt3 one = new VInt3(ScaleInv, ScaleInv, ScaleInv);
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Float2Fixed(float f)
        {
            return (int)math.round((double)(f * ScaleInv));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Fixed2Float(int i)
        {
            return (float)i * Scale;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Fixed2Int(VInt i)
        {
            return i.i / ScaleInv;
        }
                
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Fixed2Int(int i)
        {
            return i / ScaleInv;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt IntToFixed(int i)
        {
            return new VInt(i * ScaleInv);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt3 IntToFixed(int3 i)
        {
            return new VInt3(i.x * ScaleInv, i.y * ScaleInv, i.z * ScaleInv);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt3 IntToFixed(int x, int y, int z)
        {
            return new VInt3(x * ScaleInv, y * ScaleInv, z * ScaleInv);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt Min(VInt x, VInt y)
        {
            return new VInt(math.min(x.i, y.i));
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt Max(VInt x, VInt y)
        {
            return new VInt(math.max(x.i, y.i));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt Clamp(VInt x, VInt min, VInt max)
        {
            return new VInt(math.min(math.max(x.i, min.i), max.i));
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt Clamp(VInt x, int min, int max)
        {
            return new VInt(math.min(math.max(x.i, min), max));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt3 Min(VInt3 x, VInt3 y)
        {
            return new VInt3(math.min(x.x, y.x), math.min(x.y, y.y), math.min(x.z, y.z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt3 Max(VInt3 x, VInt3 y)
        {
            return new VInt3(math.max(x.x, y.x), math.max(x.y, y.y), math.max(x.z, y.z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt3 Clamp(VInt3 valueToClamp, VInt3 lowerBound, VInt3 upperBound)
        {
            return Max(lowerBound, Min(upperBound, valueToClamp));
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt Floor(VInt value)
        {
            return new VInt(value.i - (value.i % ScaleInv));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt3 Floor(VInt3 value)
        {
            return new VInt3(Floor(value.x), Floor(value.y), Floor(value.z));
        }
        
        
        public static long Divide(long a, long b)
        {
            long num  = (long)((ulong)((a ^ b) & -9223372036854775808L) >> 63);
            long num2 = num * -2L + 1L;
            return (a + b   / 2L * num2) / b;
        }

        public static int Divide(int a, int b)
        {
            int num  = (int)((uint)((a ^ b) & -2147483648) >> 31);
            int num2 = num * -2 + 1;
            return (a + b  / 2 * num2) / b;
        }
        
        public static long DivideRounded(long a, long b)
        {
            long sign  = (a ^ b) >> 63;                
            long round = (b >> 1) * ((sign << 1) | 1);
            return (a + round) / b;                   
        }
        
        public static int DivideRounded(int a, int b)
        {
            int sign  = (a ^ b) >> 31;
            int round = (b >> 1) * ((sign << 1) | 1);
            return (a + round) / b;
        }
    }
}