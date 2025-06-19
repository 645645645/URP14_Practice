using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;

namespace UnityEngine.PBD
{
    [GenerateTestsForBurstCompatibility]
    public struct QuadDistanceConstraints
    {
        private float3 _constraint0;
        private float3 _constraint1;
        private float3 _constraint2;
        private float3 _constraint3;

        private int _cnsCount0;
        private int _cnsCount1;
        private int _cnsCount2;
        private int _cnsCount3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddConstraint(int index, float3 value)
        {
            switch (index)
            {
                case 0:
                    _constraint0 += value;
                    _cnsCount0++;
                    break;
                case 1:
                    _constraint1 += value;
                    _cnsCount1++;
                    break;
                case 2:
                    _constraint2 += value;
                    _cnsCount2++;
                    break;
                case 3:
                    _constraint3 += value;
                    _cnsCount3++;
                    break;
                default: throw new System.IndexOutOfRangeException();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float3x4 GetDelta()
        {
//            if (_cnsCount0 > 0) _constraint0 /= _cnsCount0;
//            if (_cnsCount1 > 0) _constraint1 /= _cnsCount1;
//            if (_cnsCount2 > 0) _constraint2 /= _cnsCount2;
//            if (_cnsCount3 > 0) _constraint3 /= _cnsCount3;
            _constraint0 /= _cnsCount0;
            _constraint1 /= _cnsCount1;
            _constraint2 /= _cnsCount2;
            _constraint3 /= _cnsCount3;

            return new float3x4(_constraint0, _constraint1, _constraint2, _constraint3);
        }

        public void Clear()
        {
            (_constraint0, _constraint1, _constraint2, _constraint3)
                = (float3.zero, float3.zero, float3.zero, float3.zero);

            (_cnsCount0, _cnsCount1, _cnsCount2, _cnsCount3) = (0, 0, 0, 0);
        }
    }
}