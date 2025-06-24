using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;

namespace UnityEngine.PBD
{
    //还有力的优先级/计算顺序，不想整了(懒
    [GenerateTestsForBurstCompatibility]
    public struct PBDForceField
    {
        public PBDForceType ForceType;

        public PBDForceApplicationOrder Order;

        public float Radius;
        public float DeltaTime;
        public float LerpRatio;

        public float3 Force;
        public float3 Center;

        public float3 C0; //real Pos
        public float3 C1; //lerp Pos
        public float3 MoveDir;
        public float3 MoveDir1; //lerp PosDir

        //bounds
        public float3 boundsMin;
        public float3 boundsMax;
        
        //world
        public float4x4 loacl2World;

        // private float r2;
        // private float invR;
        // private float maxF;

        private float4 DisParams;

        public bool IsLargeVolume
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => math.lengthsq(boundsMax - boundsMin) > 9;
        }

        public PBDBounds Bounds => new() { Min = boundsMin, Max = boundsMax };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Prepare()
        {
            if (ForceType == PBDForceType.None)
                return;

            // r2 = Radius * Radius;
            // invR = 1 / Radius;
            // maxF = math.max(math.max(Force.x, Force.y), Force.z);
            DisParams = new float4()
            {
                x = Radius * Radius,
                y = 1      / Radius,
                z = Force.x,
                w = 0,
            };
            float3 newPos  = MathematicsUtil.MatrixMultiplyPoint3x4(loacl2World, Center, 1);
            float3 move    = newPos - C0;
            float  moveLen = math.length(move);

            float3 lerpPos  = math.lerp(C1, newPos, DeltaTime * LerpRatio);
            float3 move1    = lerpPos - C1;
            float  moveLen1 = math.length(move1);
            C0 = newPos;
            switch (ForceType)
            {
                case PBDForceType.Vortex:
                case PBDForceType.Repulsion:
                    Order = PBDForceApplicationOrder.Dynamics;
                    break;

                case PBDForceType.BernoulliForce:
                    Order = PBDForceApplicationOrder.Dynamics;
                    // DisParams.w = math.min(moveLen / DeltaTime, 1);
                    DisParams.w = moveLen / DeltaTime;
                    MoveDir     = moveLen > 1e-5f ? move / moveLen : float3.zero;

                    MoveDir1 = moveLen1 > 1e-5f ? move1 / moveLen1 : float3.zero;
                    C1       = lerpPos;
                    break;

                case PBDForceType.DelayBernoulliForce:
                    Order = PBDForceApplicationOrder.Dynamics;
                    // DisParams.w = math.min(moveLen / DeltaTime, 1);
                    DisParams.w = moveLen / DeltaTime;
                    MoveDir     = moveLen > 1e-5f ? move / moveLen : float3.zero;

                    MoveDir1 = moveLen1 > 1e-5f ? move1 / moveLen1 : float3.zero;
                    C1       = lerpPos;
                    break;

                case PBDForceType.Viscosity:
                case PBDForceType.ViscosityHeart:
                    Order = PBDForceApplicationOrder.PostDynamics;
                    break;

                default:
                    break;
            }

            boundsMin = C0 - Radius;
            boundsMax = C0 + Radius;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsInRange(in float3 position)
        {
            switch (ForceType)
            {
                case PBDForceType.None:
                    return false;
                // case PBDForceType.Gravity:
                //     return true;
                case PBDForceType.Vortex:
                    return InVortexRange(position);

                case PBDForceType.Repulsion:
                    return IsRepulsionRange(position);

                case PBDForceType.Viscosity:
                    return IsViscosityRange(position);

                case PBDForceType.ViscosityHeart:
                    return IsViscosityHeartRange(position);

                case PBDForceType.BernoulliForce:
                    return IsBernoulliForceRange(position);

                case PBDForceType.DelayBernoulliForce:
                    return IsDelayBernoulliForceRange(position);

                default:
                    return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float3 CaculateForce(in float3 position, in float3 velocity)
        {
            switch (ForceType)
            {
                case PBDForceType.None:
                    return float3.zero;
                // case PBDForceType.Gravity:
                //     return CaculateGravity(position, velocity);
                case PBDForceType.Vortex:
                    return CalculateVortexForce(position, velocity);

                case PBDForceType.Repulsion:
                    return CalculateRepulsionForce(position, velocity);

                case PBDForceType.Viscosity:
                    return CalculateViscosityForce(position, velocity);

                case PBDForceType.ViscosityHeart:
                    return CalculateViscosityHeartForce(position, velocity);

                case PBDForceType.BernoulliForce:
                    return CaculateBernoulliForceForce(position, velocity);

                case PBDForceType.DelayBernoulliForce:
                    return CaculateDelayBernoulliForceForce(position, velocity);

                default:
                    return float3.zero;
            }
        }

        // private float3 CaculateGravity(in float3 position, in float3 velocity)
        // {
        //     return Force;
        // }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool InVortexRange(in float3 position)
        {
            return math.lengthsq(position - C0) <= DisParams.x;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool4 InVortexRange(in float3x4 position)
        {
            float3x4 dis = position - new float3x4(C0, C0, C0, C0);
            return new bool4(math.lengthsq(dis.c0) <= DisParams.x,
                             math.lengthsq(dis.c1) <= DisParams.x,
                             math.lengthsq(dis.c2) <= DisParams.x,
                             math.lengthsq(dis.c3) <= DisParams.x);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float3 CalculateVortexForce(in float3 position, in float3 velocity)
        {
            float3 toCenter = position - C0;
            float  distance = math.lengthsq(toCenter);

            if (distance > DisParams.x || distance < 0.01f)
                return float3.zero;

            distance = math.sqrt(distance);

            float3 horizontalDir = new float3(toCenter.x, 0, toCenter.z);

            float3 tangent = math.cross(toCenter, new float3(0, 1, 0));
            tangent = math.normalize(tangent);

            float3 suctionDir = -math.normalize(horizontalDir);
            // float3 verticalDir = new float3(0, math.sign(-toCenter.y), 0);
            float3 verticalDir = new float3(0, 1, 0);

            float3 forceFactor = Force * (1 - distance * DisParams.y);
            //x切向 y垂直 z向心
            return forceFactor.x * tangent + forceFactor.y * verticalDir + forceFactor.z * suctionDir;
        }
        
        //不知道谁发明的 我祝他x福
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float3x4 CalculateVortexForce(in float3x4 position, in float3x4 velocity)
        {
            float3x4 toCenter = position - new float3x4(C0, C0, C0, C0);

            float4 distance = new(math.length(toCenter.c0),
                                  math.length(toCenter.c1),
                                  math.length(toCenter.c2),
                                  math.length(toCenter.c3));

            float4 inRangeMask = math.select(1f, 0f, (distance > DisParams.x));

            float3x4 horizontalDir = new(toCenter.c0.x, 0, toCenter.c0.z,
                                         toCenter.c1.x, 0, toCenter.c1.z,
                                         toCenter.c2.x, 0, toCenter.c2.z,
                                         toCenter.c3.x, 0, toCenter.c3.z),
                     
                     tangent = new(math.normalize(math.cross(toCenter.c0, MathematicsUtil.up)),
                                   math.normalize(math.cross(toCenter.c0, MathematicsUtil.up)),
                                   math.normalize(math.cross(toCenter.c0, MathematicsUtil.up)),
                                   math.normalize(math.cross(toCenter.c0, MathematicsUtil.up))),
                     
                     suctionDir = new(-math.normalize(horizontalDir.c0),
                                      -math.normalize(horizontalDir.c0),
                                      -math.normalize(horizontalDir.c0),
                                      -math.normalize(horizontalDir.c0)),
                     
                     forceFactor = new(Force * (1 - distance.x * DisParams.y),
                                       Force * (1 - distance.y * DisParams.y),
                                       Force * (1 - distance.z * DisParams.y),
                                       Force * (1 - distance.w * DisParams.y));

            return new(inRangeMask.x * forceFactor.c0 * new float3(tangent.c0 + MathematicsUtil.up + suctionDir.c0),
                       inRangeMask.y * forceFactor.c1 * new float3(tangent.c1 + MathematicsUtil.up + suctionDir.c1),
                       inRangeMask.z * forceFactor.c2 * new float3(tangent.c2 + MathematicsUtil.up + suctionDir.c2),
                       inRangeMask.w * forceFactor.c3 * new float3(tangent.c3 + MathematicsUtil.up + suctionDir.c3));
            
            // float3 toCenter = position - C0;
            // float  distance = math.lengthsq(toCenter);

            // if (distance > DisParams.x || distance < 0.01f)
            //     return float3.zero;

            // distance = math.sqrt(distance);

            // float3 horizontalDir = new float3(toCenter.x, 0, toCenter.z);

            // float3 tangent = math.cross(toCenter, new float3(0, 1, 0));
            // tangent = math.normalize(tangent);

            // float3 suctionDir = -math.normalize(horizontalDir);
                     
            // float3 verticalDir = new float3(0, 1, 0);

            // float3 forceFactor = Force * (1 - distance * DisParams.y);
            //x切向 y垂直 z向心
            // return forceFactor.x * tangent + forceFactor.y * verticalDir + forceFactor.z * suctionDir;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsRepulsionRange(in float3 position)
        {
            return math.lengthsq(position - C0) <= DisParams.x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float3 CalculateRepulsionForce(in float3 position, in float3 velocity)
        {
            float3 toCenter = position - C0;
            float  distance = math.lengthsq(toCenter);

            if (distance > DisParams.x || distance < 0.01f)
                return float3.zero;

            distance = math.sqrt(distance);

            float3 direction = math.normalize(toCenter);

            float forceFactor = DisParams.z * (1 - distance * DisParams.y);
            return direction * forceFactor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsViscosityRange(float3 position)
        {
            return math.lengthsq(position - C0) <= DisParams.x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float3 CalculateViscosityForce(in float3 position, in float3 velocity)
        {
            float3 toCenter = position - C0;
            float  distance = math.lengthsq(toCenter);

            if (distance > DisParams.x || distance < 0.01f)
                return float3.zero;

            distance = math.sqrt(distance);

            float resistanceFactor = DisParams.z * (1 - distance * DisParams.y);
            // float resistanceFactor = DisParams.z;
            resistanceFactor = math.clamp(resistanceFactor, 0, 1);
            return -velocity * resistanceFactor;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsViscosityHeartRange(float3 position)
        {
            return math.lengthsq(position - C0) <= DisParams.x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float3 CalculateViscosityHeartForce(in float3 position, in float3 velocity)
        {
            float dis = GetHeartDis(in position);

            if (dis > math.EPSILON)
                return float3.zero;

            float distance         = math.pow(-dis, 0.33f);
            float resistanceFactor = DisParams.z * (1 - distance);
            resistanceFactor = math.clamp(resistanceFactor, 0, 1);
            return -velocity * resistanceFactor;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetHeartDis(in float3 position)
        {
            float3 p = (position - C0) * DisParams.y * 2f;
            float
                x2 = p.x * p.x,
                y2 = 9   * p.z * p.z,
                z3 = p.y * p.y * p.y;
            float a  = x2 + y2 * 0.25f + p.y * p.y - 1;
            float a3 = a * a * a;
            float fx = a3 - x2 * z3 - y2 * z3 / 80;
            return fx;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsBernoulliForceRange(float3 position)
        {
            //假设俩不远
            return (math.lengthsq(position - C0) <= DisParams.x ||
                    math.lengthsq(position - C1) <= DisParams.x) &&
                   math.lengthsq(MoveDir) > 1e-5f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float3 CaculateBernoulliForceForce(in float3 position, in float3 velocity)
        {
            float3
                toCenter0 = position - C0,
                toCenter1 = position - C1;
            float
                distance0 = math.lengthsq(toCenter0),
                distance1 = math.lengthsq(toCenter1);

            if ((distance0 > DisParams.x) || distance0 < 1e-5f || DisParams.w < 0.01f)
                return float3.zero;
            // if ((distance0 > DisParams.x && distance1 > DisParams.x) || distance0 < 1e-5f || 
            //     distance1 < 1e-5f || DisParams.w < 0.01f)
            //     return float3.zero;

            distance0 = math.sqrt(distance0);
            distance1 = math.sqrt(distance1);

            toCenter0 /= distance0;
            // toCenter1 /= distance1;

            float3 horizontalDir = new float3(toCenter0.x, 0, toCenter0.z);

            float3 suctionDir = MathematicsUtil.Normalize(horizontalDir);
            // float3 verticalDir = new float3(0, math.mad(toCenter.y * DisParams.y, -0.5f, 0.5f), 0);
            float3 verticalDir = new float3(0, 1, 0);

            // float area     = math.dot(-MoveDir1, toCenter1);
            // float areaMask = math.max(area * 4 - 3, 0);

            // float3 disFactor = new float3(
            //                               (1 - distance1 * DisParams.y) /* areaMask*/,
            //                               (1 - math.min(distance0, distance1) * DisParams.y),
            //                               1 - math.min(distance0, distance1) * DisParams.y);

            // float disFactor = 1 - distance0 * DisParams.y;
            float disFactor = 1;

            float3 tangent = MoveDir1;

            float3 forceFactor = Force * disFactor * DisParams.w; //运动强度控制一下,最好及时移除force并去掉这个w
            return forceFactor.x * tangent + forceFactor.y * verticalDir + forceFactor.z * suctionDir;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsDelayBernoulliForceRange(float3 position)
        {
            float3 toCenter = position - C1;
            return math.lengthsq(toCenter)      <= DisParams.x &&
                   math.lengthsq(MoveDir)       > 1e-4         &&
                   math.dot(-MoveDir, toCenter) > 0.8f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float3 CaculateDelayBernoulliForceForce(in float3 position, in float3 velocity)
        {
            float3 toCenter = position - C1;
            float  distance = math.lengthsq(toCenter);

            if (distance > DisParams.x || distance < 1e-5f || DisParams.w < 1e-4f)
                return float3.zero;

            distance = math.sqrt(distance);

            float3 horizontalDir = new float3(toCenter.x, 0, toCenter.z);

            float3 suctionDir = MathematicsUtil.Normalize(horizontalDir);
            // float3 verticalDir = new float3(0, math.mad(toCenter.y * DisParams.y, -0.5f, 0.5f), 0);

            float area = math.dot(-MoveDir, suctionDir);

            if (area <= 0.5f)
                return float3.zero;

            float disFactor = 1 - distance * DisParams.y,
                  areaMask  = math.max(area * 5 - 4, 0);

            float3 tangent     = MoveDir;
            float3 verticalDir = new float3(0, 1, 0);


            float3 forceFactor = Force * disFactor * areaMask * DisParams.w; //运动强度控制一下,最好及时移除force并去掉这个w
            return forceFactor.x * tangent + forceFactor.y * verticalDir + forceFactor.z * suctionDir;
        }
    }
}