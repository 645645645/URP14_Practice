using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;

namespace UnityEngine.PBD
{
    [GenerateTestsForBurstCompatibility]
    [StructLayout(LayoutKind.Explicit, Size = 4)]
    public struct UNorm16x2
    {
        [FieldOffset(0)] public ushort x;
        [FieldOffset(2)] public ushort y;

        public UNorm16x2(ushort x, ushort y)
        {
            this.x = x;
            this.y = y;
        }

        public UNorm16x2(in float2 value)
        {
            x = (ushort)(math.clamp(value.x, 0, 1) * ushort.MaxValue);
            y = (ushort)(math.clamp(value.y, 0, 1) * ushort.MaxValue);
        }

        public UNorm16x2(in float2 value, bool toNormal = false)
        {
            float2 v = toNormal ? math.mad(value, 0.5f, 0.5f) : value;
            x = (ushort)(math.clamp(v.x, 0, 1) * ushort.MaxValue);
            y = (ushort)(math.clamp(v.y, 0, 1) * ushort.MaxValue);
        }

        public ushort this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0:  return x;
                    case 1:  return y;
                    default: throw new System.IndexOutOfRangeException();
                }
            }
            set
            {
                switch (index)
                {
                    case 0:  x = value; break;
                    case 1:  y = value; break;
                    default: throw new System.IndexOutOfRangeException();
                }
            }
        }
    }
}