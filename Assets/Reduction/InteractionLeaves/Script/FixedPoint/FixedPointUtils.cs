// using System;
// using System.Threading;
// using Unity.Mathematics;
//
// namespace UnityEngine.PBD
// {
//     public struct FixedPoint : IEquatable<FixedPoint>, IComparable<FixedPoint>
//     {
//         public int RawValue;
//
//         public const int FRACTION_BITS = 9;
//         public const int FRACTION_MASK = (1 << FRACTION_BITS) - 1;
//         public const int ONE_RAW_VALUE = 1 << FRACTION_BITS;
//
//         public static readonly FixedPoint One = new FixedPoint(ONE_RAW_VALUE);
//
//         public static readonly FixedPoint Zero = new FixedPoint(0);
//         
//         public static readonly FixedPoint Half = new FixedPoint(ONE_RAW_VALUE >> 1);
//
//         public static FixedPoint MaxValue => new FixedPoint(int.MaxValue);
//
//         public static FixedPoint MinValue => new FixedPoint(int.MinValue);
//
//         public FixedPoint(long rawValue)
//         {
// #if UNITY_EDITOR
//             if (rawValue > int.MaxValue || rawValue < int.MinValue)
//                 throw new ArgumentException("long value is out of int range.");
// #endif
//             RawValue = (int)rawValue;
//         }
//
//         public FixedPoint(int value)
//         {
//             RawValue = value << FRACTION_BITS;
//         }
//
//         public FixedPoint(float value)
//         {
//             RawValue = (int)math.round(value * ONE_RAW_VALUE);
//         }
//
//         public FixedPoint(double value)
//         {
// #if UNITY_EDITOR
//             if (value > int.MaxValue || value < int.MinValue)
//                 throw new ArgumentException("long value is out of int range.");
// #endif
//             RawValue = (int)math.round(value * ONE_RAW_VALUE);
//         }
//
//
//         public static FixedPoint operator +(FixedPoint a) => a;
//         
//         public static FixedPoint operator -(FixedPoint a) => new FixedPoint(-a.RawValue);
//         
//         public static FixedPoint operator +(FixedPoint a, FixedPoint b) => 
//             new FixedPoint((long)a.RawValue + b.RawValue);
//         
//         public static FixedPoint operator -(FixedPoint a, FixedPoint b) => 
//             new FixedPoint((long)a.RawValue - b.RawValue);
//
//         public static FixedPoint operator *(FixedPoint a, FixedPoint b)
//         {
//             //可能溢出
//             // long product = ((long)a.RawValue * b.RawValue) >> FRACTION_BITS;
//             
//             //分解为高低位计算
//             int aHi = a.RawValue >> FRACTION_BITS;
//             int aLo = a.RawValue & FRACTION_MASK;
//             int bHi = b.RawValue >> FRACTION_BITS;
//             int bLo = b.RawValue & FRACTION_MASK;
//
//             long hiProduct   = (long)aHi * bHi;
//             long midProduct1 = (long)aHi * bLo;
//             long midProduct2 = (long)aLo * bHi;
//             long loProduct   = (long)aLo * bLo;
//
//             long midSum  = midProduct1                  + midProduct2;
//             long product = (hiProduct << FRACTION_BITS) + midSum + (loProduct >> FRACTION_BITS);
//             
//             return new FixedPoint(product);
//         }
//
//         public static FixedPoint operator /(FixedPoint a, FixedPoint b)
//         {
// #if UNITY_EDITOR            
//             if (b.RawValue == 0) throw new DivideByZeroException();
// #endif
//             long quotient = ((long)a.RawValue << FRACTION_BITS) / b.RawValue;
//             return new FixedPoint(quotient);
//         }
//         
//         
//         public static FixedPoint operator +(FixedPoint a, int b) => a + new FixedPoint(b);
//         public static FixedPoint operator -(FixedPoint a, int b) => a - new FixedPoint(b);
//         public static FixedPoint operator *(FixedPoint a, int b) => a * new FixedPoint(b);
//         public static FixedPoint operator /(FixedPoint a, int b) => a / new FixedPoint(b);
//         
//         public static FixedPoint operator +(int a, FixedPoint b) => new FixedPoint(a) + b;
//         public static FixedPoint operator -(int a, FixedPoint b) => new FixedPoint(a) - b;
//         public static FixedPoint operator *(int a, FixedPoint b) => new FixedPoint(a) * b;
//         public static FixedPoint operator /(int a, FixedPoint b) => new FixedPoint(a) / b;
//
//         public static bool operator ==(FixedPoint a, FixedPoint b) => a.RawValue == b.RawValue;
//         public static bool operator !=(FixedPoint a, FixedPoint b) => a.RawValue != b.RawValue;
//         public static bool operator <(FixedPoint  a, FixedPoint b) => a.RawValue < b.RawValue;
//         public static bool operator >(FixedPoint  a, FixedPoint b) => a.RawValue > b.RawValue;
//         public static bool operator <=(FixedPoint a, FixedPoint b) => a.RawValue <= b.RawValue;
//         public static bool operator >=(FixedPoint a, FixedPoint b) => a.RawValue >= b.RawValue;
//         
//         public static FixedPoint operator ~(FixedPoint a) => 
//             new FixedPoint(~a.RawValue);
//     
//         /// <summary>
//         /// 按位与
//         /// </summary>
//         public static FixedPoint operator &(FixedPoint a, FixedPoint b) => 
//             new FixedPoint(a.RawValue & b.RawValue);
//     
//         /// <summary>
//         /// 按位或
//         /// </summary>
//         public static FixedPoint operator |(FixedPoint a, FixedPoint b) => 
//             new FixedPoint(a.RawValue | b.RawValue);
//     
//         /// <summary>
//         /// 按位异或
//         /// </summary>
//         public static FixedPoint operator ^(FixedPoint a, FixedPoint b) => 
//             new FixedPoint(a.RawValue ^ b.RawValue);
//     
//         /// <summary>
//         /// 左移位
//         /// </summary>
//         public static FixedPoint operator <<(FixedPoint a, int shift) => 
//             new FixedPoint(a.RawValue << shift);
//     
//         /// <summary>
//         /// 右移位
//         /// </summary>
//         public static FixedPoint operator >>(FixedPoint a, int shift) => 
//             new FixedPoint(a.RawValue >> shift);
//
//     
//         /// <summary>
//         /// 定点数取模（保留小数部分）
//         /// </summary>
//         public static FixedPoint operator %(FixedPoint a, FixedPoint b)
//         {
// #if UNITY_EDITOR       
//             if (b.RawValue == 0) throw new DivideByZeroException();
// #endif
//             return new FixedPoint(a.RawValue % b.RawValue);
//         }
//     
//         /// <summary>
//         /// 定点数与整数取模
//         /// </summary>
//         public static FixedPoint operator %(FixedPoint a, int b)
//         {
// #if UNITY_EDITOR       
//             if (b == 0) throw new DivideByZeroException();
// #endif
//             return a.RawValue % (b * One);
//         }
//     
//         /// <summary>
//         /// 整数与定点数取模
//         /// </summary>
//         public static FixedPoint operator %(int a, FixedPoint b)
//         {
// #if UNITY_EDITOR   
//             if (b.RawValue == 0) throw new DivideByZeroException();
// #endif
//             return (a * One) % b.RawValue;
//         }
//         
//         public static FixedPoint Sqrt(FixedPoint x)
//         {
// #if UNITY_EDITOR       
//             if (x.RawValue < 0) throw new ArgumentException("Value must be non-negative");
// #endif
//             long raw    = x.RawValue << FRACTION_BITS;
//             long result = 0;
//             long bit    = 1L << 62;
//             
//             while (bit > raw) bit >>= 2;
//             
//             while (bit != 0)
//             {
//                 if (raw >= result + bit)
//                 {
//                     raw    -= result        + bit;
//                     result =  (result >> 1) + bit;
//                 }
//                 else
//                 {
//                     result >>= 1;
//                 }
//                 bit >>= 2;
//             }
//
//             return new FixedPoint(result);
//         }
//
//
//         public static FixedPoint Abs(FixedPoint x) =>
//             new FixedPoint(x.RawValue < 0 ? -x.RawValue : x.RawValue);
//
//         public static FixedPoint Max(FixedPoint a, FixedPoint b) =>
//             a > b ? a : b;
//
//         public static FixedPoint Min(FixedPoint a, FixedPoint b) =>
//             a < b ? a : b;
//         
//         public static FixedPoint Sign(FixedPoint x) => 
//             (x.RawValue < 0 ? -One : (x.RawValue > 0 ? One : 0));
//
//         public static FixedPoint Floor(FixedPoint x) => 
//             new FixedPoint(x.RawValue & ~FRACTION_MASK);
//         
//         public static FixedPoint Ceiling(FixedPoint x) => 
//             Floor(x) + (x.RawValue % One != 0 ? One : Zero);
//         
//         public static FixedPoint Round(FixedPoint x)
//         {
//             long fractional = x.RawValue & FRACTION_MASK;
//             return fractional < (1L << (FRACTION_BITS - 1)) 
//                 ? Floor(x) 
//                 : Ceiling(x);
//         }
//         
//         /// <summary>
//         /// 包装值到[0, max)范围（类似角度包装）
//         /// </summary>
//         public static FixedPoint Wrap(FixedPoint value, FixedPoint max)
//         {
//             FixedPoint mod = value % max;
//             return mod < Zero ? mod + max : mod;
//         }
//     
//         /// <summary>
//         /// 包装值到[min, max)范围
//         /// </summary>
//         public static FixedPoint Wrap(FixedPoint value, FixedPoint min, FixedPoint max)
//         {
//             FixedPoint range  = max - min;
//             FixedPoint offset = (value - min) % range;
//             return offset < Zero ? offset + range + min : offset + min;
//         }
//     
//         /// <summary>
//         /// 浮点取模（保留小数精度）
//         /// </summary>
//         public static FixedPoint FMod(FixedPoint a, FixedPoint b)
//         {
//             if (b == Zero) throw new DivideByZeroException();
//         
//             // 计算整数商
//             FixedPoint quotient    = a / b;
//             FixedPoint integerPart = Floor(quotient);
//         
//             // 返回余数
//             return a - b * integerPart;
//         }
//     
//         /// <summary>
//         /// 周期位置映射（如纹理坐标包装）
//         /// </summary>
//         public static FixedPoint Periodic(FixedPoint position, FixedPoint period)
//         {
//             FixedPoint mod = position % period;
//             return mod < Zero ? mod + period : mod;
//         }
//         
//         
//         /// <summary>
//         /// 获取整数部分（截断）
//         /// </summary>
//         public FixedPoint Truncate() => 
//             RawValue & ~FRACTION_MASK;
//         
//         
//         /// <summary>
//         /// 获取小数部分
//         /// </summary>
//         public FixedPoint Fractional() => 
//              RawValue & FRACTION_MASK;
//         
//         /// <summary>
//         /// 设置小数部分
//         /// </summary>
//         public FixedPoint SetFractional(FixedPoint fractional)
//         {
//             long intPart  = (long)(RawValue            & ~(One - 1));
//             long fracPart = (long)(fractional.RawValue & (One - 1));
//             return new FixedPoint(intPart | fracPart);
//         }
//         
//         public bool IsBitSet(int bitIndex)
//         {
// #if UNITY_EDITOR
//             if (bitIndex < 0 || bitIndex > 63)
//                 throw new ArgumentOutOfRangeException(nameof(bitIndex));
// #endif
//             return (RawValue & (1L << bitIndex)) != 0;
//         }
//         
//         public FixedPoint SetBit(int bitIndex, bool value)
//         {
// #if UNITY_EDITOR
//             if (bitIndex < 0 || bitIndex > 63) 
//                 throw new ArgumentOutOfRangeException(nameof(bitIndex));
// #endif
//             long mask = 1L << bitIndex;
//             return value 
//                 ? new FixedPoint(RawValue | mask) 
//                 : new FixedPoint(RawValue & ~mask);
//         }
//         
//         
//         public FixedPoint ReverseBits()
//         {
//             long value  = RawValue;
//             long result = 0;
//             for (int i = 0; i < 64; i++)
//             {
//                 result <<= 1;
//                 result |=  value & 1;
//                 value  >>= 1;
//             }
//             return new FixedPoint(result);
//         }
//         
//         public static explicit operator float(FixedPoint x) =>
//             (float)x.RawValue / (1 << FRACTION_BITS);
//         
//         public static explicit operator double(FixedPoint x) =>
//             (double)x.RawValue / (1 << FRACTION_BITS);
//         
//         public static explicit operator int(FixedPoint x) => 
//             (int)(x.RawValue >> FRACTION_BITS);
//         
//         public static explicit operator long(FixedPoint x) => 
//             x.RawValue >> FRACTION_BITS;
//         
//         public static implicit operator FixedPoint(int x) => 
//             new FixedPoint(x);
//         
//         public static implicit operator FixedPoint(long x) => 
//             new FixedPoint(x);
//         
//         public static implicit operator FixedPoint(float x) => 
//             new FixedPoint(x);
//         
//         public static implicit operator FixedPoint(double x) => 
//             new FixedPoint(x);
//         
//         
//         public static FixedPoint AtomicAdd(ref FixedPoint location, FixedPoint value)
//         {
//             long newValue = Interlocked.Add(ref location.RawValue, value.RawValue);
//             return new FixedPoint(newValue - value.RawValue);
//         }
//
//         public static FixedPoint AtomicSub(ref FixedPoint localtion, FixedPoint value)
//         {
//             return AtomicAdd(ref localtion, -value);
//         }
//
//         public static FixedPoint AtomicCompareExchange(ref FixedPoint location, FixedPoint value, FixedPoint comparand)
//         {
//             long old = Interlocked.CompareExchange(ref location.RawValue, 
//                                                    value.RawValue, 
//                                                    comparand.RawValue);
//
//             return new FixedPoint(old);
//         }
//
//
//
//         // public static void SetGlobalFractionBits(int bits)
//         // {
//         //     if (bits < 0 || bits > 32)
//         //         throw new ArgumentOutOfRangeException("bits must be between 0 and 32");
//         //
//         //     FRACTION_BITS = bits;
//         // }
//         
//         public float  ToFloat()               => (float)this;
//         public double ToDouble()              => (double)this;
//         public long   GetRawValue()           => RawValue;
//         public void   SetRawValue(long value) => RawValue = (int)value;
//
//
//         public bool Equals(FixedPoint other)
//         {
//             return RawValue == other.RawValue;
//         }
//
//         public int CompareTo(FixedPoint other)
//         {
//             return RawValue.CompareTo(other.RawValue);
//         }
//
//         public override int GetHashCode()
//         {
//             return RawValue.GetHashCode();
//         }
//
//         public override string ToString()
//         {
//             return ToFloat().ToString("F4");
//         }
//     }
// }