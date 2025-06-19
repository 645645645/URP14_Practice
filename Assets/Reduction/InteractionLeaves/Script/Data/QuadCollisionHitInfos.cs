using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;

namespace UnityEngine.PBD
{
    [GenerateTestsForBurstCompatibility]
    public struct QuadCollisionHitInfos
    {
        private PBDCollisionHit _hit0;
        private PBDCollisionHit _hit1;
        private PBDCollisionHit _hit2;
        private PBDCollisionHit _hit3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetHit(int index, PBDCollisionHit hit)
        {
            switch (index)
            {
                case 0:  _hit0 = hit; break;
                case 1:  _hit1 = hit; break;
                case 2:  _hit2 = hit; break;
                case 3:  _hit3 = hit; break;
                default: throw new System.IndexOutOfRangeException();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PBDCollisionHit GetHit(int index)
        {
            switch (index)
            {
                case 0:  return _hit0;
                case 1:  return _hit1;
                case 2:  return _hit2;
                case 3:  return _hit3;
                default: throw new System.IndexOutOfRangeException();
            }
        }

        public void Clear()
        {
            var mewHit = new PBDCollisionHit();
            
            _hit0 = mewHit;
            _hit1 = mewHit;
            _hit2 = mewHit;
            _hit3 = mewHit;
        }
    }
    
    


    [GenerateTestsForBurstCompatibility]
    public struct PBDCollisionHit
    {
        public byte   hitCount;
        public float insertDepth;

        public float3 hitSurfacePos;

        public float3 hitDelta;
        public float3 hitNormal;

        // public float3 hitActorCenter;
        public float3 hitConcatNormal;

        // public float3 hitOldVelocity;
        // public float3 hitNewVelocuty;
        public float3 hitConcatDelta;
    }
}