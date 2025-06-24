using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace UnityEngine.PBD
{
    public struct VInt3
    {
        public int x;
        public int y;
        public int z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VInt3(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VInt3(float x, float y, float z)
        {
            this.x = FixedPointUtils.Float2Fixed(x);
            this.y = FixedPointUtils.Float2Fixed(y);
            this.z = FixedPointUtils.Float2Fixed(z);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VInt3(float3 a)
        {
            this.x = FixedPointUtils.Float2Fixed(a.x);
            this.y = FixedPointUtils.Float2Fixed(a.y);
            this.z = FixedPointUtils.Float2Fixed(a.z);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VInt3(VInt x, VInt y, VInt z)
        {
            this.x = x.i;
            this.y = y.i;
            this.z = z.i;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt3 operator *(VInt3 a, VInt3 b)
        {
            long tempx = (long)a.x * b.x + FixedPointUtils.HalfScaleInv,
                 tempy = (long)a.y * b.y + FixedPointUtils.HalfScaleInv,
                 tempz = (long)a.z * b.z + FixedPointUtils.HalfScaleInv;
            
            return new VInt3((int)tempx / FixedPointUtils.ScaleInv,
                             (int)tempy / FixedPointUtils.ScaleInv,
                             (int)tempz / FixedPointUtils.ScaleInv);
        }
        
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt3 operator *(VInt3 a, VInt b)
        {
            long tempx = (long)a.x * b.i + FixedPointUtils.HalfScaleInv,
                 tempy = (long)a.y * b.i + FixedPointUtils.HalfScaleInv,
                 tempz = (long)a.z * b.i + FixedPointUtils.HalfScaleInv;
            
            return new VInt3((int)tempx / FixedPointUtils.ScaleInv,
                             (int)tempy / FixedPointUtils.ScaleInv,
                             (int)tempz / FixedPointUtils.ScaleInv);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt3 operator *(VInt3 a, int3 b)
        {
            long tempx = (long)a.x * b.x,
                 tempy = (long)a.y * b.y,
                 tempz = (long)a.z * b.z;
            
            return new VInt3((int)tempx, (int)tempy, (int)tempz);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt3 operator /(VInt3 lhs, VInt3 rhs)
        {
            long scaledDividendx = ((long)lhs.x    * FixedPointUtils.ScaleInv) + (rhs.x > 0 ? rhs.x / 2 : -rhs.x / 2);
            long scaledDividendy = ((long)lhs.y    * FixedPointUtils.ScaleInv) + (rhs.y > 0 ? rhs.y / 2 : -rhs.y / 2);
            long scaledDividendz = ((long)lhs.z    * FixedPointUtils.ScaleInv) + (rhs.z > 0 ? rhs.z / 2 : -rhs.z / 2);
            return new VInt3((int)(scaledDividendx / rhs.x), (int)(scaledDividendy / rhs.y), (int)(scaledDividendz / rhs.z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt3 operator /(VInt3 lhs, int3 rhs)
        {
            long scaledDividendx = ((long)lhs.x) + (rhs.x > 0 ? rhs.x / 2 : -rhs.x / 2);
            long scaledDividendy = ((long)lhs.y) + (rhs.y > 0 ? rhs.y / 2 : -rhs.y / 2);
            long scaledDividendz = ((long)lhs.z) + (rhs.z > 0 ? rhs.z / 2 : -rhs.z / 2);
            return new VInt3((int)(scaledDividendx / rhs.x), (int)(scaledDividendy / rhs.y), (int)(scaledDividendz / rhs.z));
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt3 operator - (VInt3 rhs) { return new VInt3 (- rhs.x, - rhs.y, - rhs.z); }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt3 operator - (VInt3 lhs, VInt3 rhs) { return new VInt3 (lhs.x - rhs.x, lhs.y - rhs.y, lhs.z - rhs.z); }
        
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt3 operator + (VInt3 lhs, VInt3 rhs) { return new VInt3 (lhs.x + rhs.x, lhs.y + rhs.y, lhs.z + rhs.z); }
    }
}