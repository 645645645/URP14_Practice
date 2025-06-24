using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;

//浮点转顶点，整形正常用
namespace UnityEngine.PBD
{
    [GenerateTestsForBurstCompatibility]
    public struct VInt
    {
        public int i;

        public float scalar
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => FixedPointUtils.Fixed2Float(i);
        }

        public VInt(int i)
        {
            this.i  = i;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public VInt(float f)
        {
            this.i  = FixedPointUtils.Float2Fixed(f);
        }

        public static readonly VInt zero = new VInt(0);
        
        public static readonly VInt one = new VInt(FixedPointUtils.ScaleInv);

        public override string ToString()
        {
            return this.scalar.ToString("F4");
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator VInt(float f)
        {
            return new VInt((int)math.round((double)(f * FixedPointUtils.ScaleInv)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator VInt(int i)
        {
            return new VInt(i);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator float(VInt ob)
        {
            return (float)ob.i * FixedPointUtils.Scale;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator long(VInt ob)
        {
            return (long)ob.i;
        }
        
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt operator +(VInt a)
        {
            return new VInt(a.i);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt operator -(VInt a)
        {
            return new VInt(-a.i);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt operator +(VInt a, VInt b)
        {
            return new VInt(a.i + b.i);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt operator -(VInt a, VInt b)
        {
            return new VInt(a.i - b.i);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt operator *(VInt a, VInt b)
        {
            long temp = (long)a.i * b.i + FixedPointUtils.HalfScaleInv;//四舍五入
            return new VInt((int)(temp / FixedPointUtils.ScaleInv));
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt operator *(VInt a, int b)
        {
            long temp = (long)a.i * b;
            return new VInt((int)temp);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt operator *(int a, VInt b)
        {
            long temp = (long)a * b.i;
            return new VInt((int)temp);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt operator /(VInt a, VInt b)
        {
            long scaledDividend = ((long)a.i     * FixedPointUtils.ScaleInv) + (b.i > 0 ? b.i / 2 : -b.i / 2);
            return new VInt((int)(scaledDividend / b.i));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt operator /(VInt a, int b)
        {
            return new VInt(FixedPointUtils.Divide(a.i, b));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static VInt operator /(int a, VInt b)
        {
            return new VInt(a * FixedPointUtils.ScaleInv) / b;//。。。
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(VInt a, VInt b)
        {
            return a.i == b.i;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(VInt a, VInt b)
        {
            return a.i != b.i;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >=(VInt a, VInt b)
        {
            return a.i >= b.i;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <=(VInt a, VInt b)
        {
            return a.i <= b.i;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(VInt a, VInt b)
        {
            return a.i > b.i;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(VInt a, VInt b)
        {
            return a.i < b.i;
        }
    }
}