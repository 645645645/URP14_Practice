using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;

namespace UnityEngine.PBD
{
    [GenerateTestsForBurstCompatibility]
    [StructLayout(LayoutKind.Explicit, Size = 4)]
    public struct UNorm8x4
    {
        [FieldOffset(0)] public byte x;
        [FieldOffset(1)] public byte y;
        [FieldOffset(2)] public byte z;
        [FieldOffset(3)] public byte w;

        public UNorm8x4(byte x, byte y, byte z, byte w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public UNorm8x4(in float2 octN, in float2 uv)
        {
            float2 N = math.mad(octN, 0.5f, 0.5f);
            x = (byte)(math.clamp(N.x,  0, 1) * byte.MaxValue);
            y = (byte)(math.clamp(N.y,  0, 1) * byte.MaxValue);
            z = (byte)(math.clamp(uv.x, 0, 1) * byte.MaxValue);
            w = (byte)(math.clamp(uv.y, 0, 1) * byte.MaxValue);
        }
        
        public UNorm8x4(in float2 octN, byte u, byte v)
        {
            float2 N = math.mad(octN, 0.5f, 0.5f);
            x      = (byte)(math.clamp(N.x, 0, 1) * byte.MaxValue);
            y      = (byte)(math.clamp(N.y, 0, 1) * byte.MaxValue);
            z = u;
            w = v;
        }

        public byte this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0:  return x;
                    case 1:  return y;
                    case 2:  return z;
                    case 3:  return w;
                    default: throw new System.IndexOutOfRangeException();
                }
            }
            set
            {
                switch (index)
                {
                    case 0:  x = value; break;
                    case 1:  y = value; break;
                    case 2:  z = value; break;
                    case 3:  w = value; break;
                    default: throw new System.IndexOutOfRangeException();
                }
            }
        }
    }
}