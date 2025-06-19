using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Jobs;

namespace UnityEngine.PBD
{
    //记录模拟开始前位置
    [BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
    public struct InitializeJob : IJobParallelForBatch
    {
        [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
        public NativeArray<int>.ReadOnly UpdateList;

        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<Position> Positions;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PredictedPositions>.ReadOnly PredictedPositions;

        public unsafe void Execute(int start, int count)
        {
            var size = UnsafeUtility.SizeOf<Position>() * 4;
            
            var updatePtr = (int*)UpdateList.GetUnsafeReadOnlyPtr();
            var source    = (PredictedPositions*)PredictedPositions.GetUnsafeReadOnlyPtr();
            var dest      = (Position*)Positions.GetUnsafePtr();
            for (int index = start; index < start + count; index++)
            {
                int offset = updatePtr[index] * 4;

                UnsafeUtility.MemCpy(destination: dest + offset,
                                     source: source    + offset,
                                     size: size);
            }
        }
    }


    #region ExtForce

    [BurstCompile(FloatPrecision.Low, FloatMode.Default)]
    public struct ReadForceTransformJob : IJobParallelForTransform
    {
        [NativeDisableParallelForRestriction] 
        public NativeList<PBDForceField> ForceFields;

        [ReadOnly] public float DeltaTime;

        public void Execute(int index, TransformAccess transform)
        {
            if (transform.isValid)
            {
                ref var force = ref ForceFields.ElementAt(index);
                force.loacl2World = transform.localToWorldMatrix;
                force.DeltaTime   = DeltaTime;
                force.Prepare();
            }
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance,
                  FloatMode = FloatMode.Fast, CompileSynchronously = true,
                  FloatPrecision = FloatPrecision.Low, DisableSafetyChecks = true)]
    public struct ExtForceJob : IJobParallelForBatch
    {
        [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
        public NativeArray<int>.ReadOnly UpdateList;

        [NativeDisableParallelForRestriction] 
        public NativeArray<PredictedPositions> PredictedPositions;

        [NativeDisableParallelForRestriction] 
        public NativeArray<Velocity> Velocities;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<Normal>.ReadOnly Normals;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<InvMass>.ReadOnly InvMasses;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<Area>.ReadOnly Areas;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PBDForceField>.ReadOnly ForceFields;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PBDForceField>.ReadOnly PostForceFields;

        [ReadOnly] public float3 gravity;
        [ReadOnly] public float3 wind;
        [ReadOnly] public float damping;
        [ReadOnly] public float deltaTime;

        public unsafe void Execute(int start, int count)
        {
            var sizeF1 = UnsafeUtility.SizeOf<float>();
            int sizeF4  = sizeF1 * 4,
                sizeF34 = sizeF1 * 12;
            
            var updatePtr  = (int*)UpdateList.GetUnsafeReadOnlyPtr();
            var posPtr     = (PredictedPositions*)PredictedPositions.GetUnsafeReadOnlyPtr();
            var veloPtr    = (Velocity*)Velocities.GetUnsafePtr();
            var normalPtr  = (Normal*)Normals.GetUnsafeReadOnlyPtr();
            var invMassPtr = (InvMass*)InvMasses.GetUnsafeReadOnlyPtr();
            var areaPtr    = (Area*)Areas.GetUnsafeReadOnlyPtr();
            var forcePtr    = (PBDForceField*)ForceFields.GetUnsafeReadOnlyPtr();
            var pForcePtr    = (PBDForceField*)PostForceFields.GetUnsafeReadOnlyPtr();
            
            
            for (int index = start; index < start + count; index++)
            {            
                int quadID  = updatePtr[index];
                int offset = quadID * 4;

                float3x4
                    positons,
                    normals,
                    velocities;
                    
                float4 areaes,
                       invMasses;

                UnsafeUtility.MemCpy(&positons, posPtr    + offset, sizeF34);
                UnsafeUtility.MemCpy(&normals, normalPtr  + offset, sizeF34);
                UnsafeUtility.MemCpy(&velocities, veloPtr + offset, sizeF34);

                UnsafeUtility.MemCpy(&areaes, areaPtr       + offset, sizeF4);
                UnsafeUtility.MemCpy(&invMasses, invMassPtr + offset, sizeF4);

                float4 winDotN = new float4()
                {
                    x = math.dot(velocities[0] - wind, normals[0]),
                    y = math.dot(velocities[1] - wind, normals[1]),
                    z = math.dot(velocities[2] - wind, normals[2]),
                    w = math.dot(velocities[3] - wind, normals[3]),
                };

                float4 tempF = winDotN * areaes;

                float3x4 forces = new float3x4(normals.c0 * tempF[0],
                                               normals.c1 * tempF[1],
                                               normals.c2 * tempF[2],
                                               normals.c3 * tempF[3]);

                for (int i = 0; i < ForceFields.Length; i++)
                {
                    for (int j = 0; j < 4; j++)
                        forces[j] += forcePtr[i].CaculateForce(in positons[j], in velocities[j]);
                }

                for (int i = 0; i < 4; i++)
                {
                    velocities[i] += (forces[i] * invMasses[i] + gravity) * deltaTime;
                    velocities[i] *= math.max(-damping * invMasses[i] * deltaTime + 1, 0);
                }
                
                for (int i = 0; i < PostForceFields.Length; i++)
                {
                    for (int j = 0; j < 4; j++)
                        velocities[j] += pForcePtr[i].CaculateForce(in positons[j], in velocities[j]);
                }

                for (int i = 0; i < 4; i++)
                {
                    positons[i] += velocities[i] * deltaTime;
                }

                UnsafeUtility.MemCpy(posPtr + offset, &positons, sizeF34);
                UnsafeUtility.MemCpy(veloPtr + offset, &velocities, sizeF34);

                // int4 indices = math.mad(quadID, 4, new int4(0, 1, 2, 3));
                // for (int i = 0; i < 4; i++)
                // {
                //     int pIndex = indices[i];
                //
                //     float3
                //         position = PredictedPositions[pIndex].Value,
                //         normal   = Normals[pIndex].Value,
                //         velocity = Velocities[pIndex].Value;
                //
                //     float
                //         area     = Areas[pIndex].Value,
                //         invMass  = InvMasses[pIndex].Value,
                //         windDotN = math.dot(velocity - wind, normal);
                //
                //     if (windDotN < 0)
                //     {
                //         //好像做了个屁事
                //         normal   *= -1;
                //         windDotN *= -1;
                //     }
                //
                //     float3 force = windDotN * normal * area;
                //     for (int j = 0; j < ForceFields.Length; j++)
                //         force += ForceFields[j].CaculateForce(in position, in velocity);
                //
                //     //重力应该分到PreDynamic
                //     velocity += (force * invMass + gravity) * deltaTime;
                //     velocity *= math.max(-damping * invMass * deltaTime + 1, 0);
                //     // velocity *= math.exp(-damping * invMass * deltaTime);
                //
                //     for (int j = 0; j < PostForceFields.Length; j++)
                //         velocity += PostForceFields[j].CaculateForce(in position, in velocity);
                //
                //     position = velocity * deltaTime + position;
                //
                //     Velocities[pIndex]         = new Velocity() { Value           = velocity };
                //     PredictedPositions[pIndex] = new PredictedPositions() { Value = position };
                // }
            }
        }
    }
    
//    力场激活static部分, 朴素ext模式
//    开了byQuad就(划掉) 用上一帧碰撞前的quadPos
    [BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
    public struct ExtForceSetUpdateJob : IJobParallelForBatch
    {
        [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
        public NativeArray<int>.ReadOnly StaticList;
        
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<QuadPredictedPositions>.ReadOnly QuadPredictedPositions;

        
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PBDForceField>.ReadOnly ForceFields;
        
        //现在这个不需要激活static 
        // [ReadOnly, NativeDisableParallelForRestriction]
        // public NativeArray<PBDForceField>.ReadOnly PostForceFields;
        
        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<IsNeedUpdate> IsNeedUpdates;

        public void Execute(int start, int count)
        {

            for (int index = start; index < start + count; index++)
            {
                int quadID = StaticList[index];

                float3 pos = QuadPredictedPositions[quadID].Value;
                
                
                for (int i = 0; i < ForceFields.Length; i++)
                {
                    if (ForceFields[i].IsInRange(pos))
                    {
                        IsNeedUpdates[quadID] = new IsNeedUpdate() { Value = true };
                        break;
                    }
                }
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void IsSetUpdate(int index, in float3 pos)
        {
            for (int i = 0; i < ForceFields.Length; i++)
            {
                if (ForceFields[i].IsInRange(pos))
                {
                    IsNeedUpdates[index] = new IsNeedUpdate() { Value = true };
                    return;
                }
            }

            // for (int i = 0; i < PostForceFields.Length; i++)
            // {
            //     if (PostForceFields[i].IsInRange(pos))
            //     {
            //         IsNeedUpdates[index] = new IsNeedUpdate() { Value = true };
            //         return;
            //     }
            // }
        }
    }

    
    //开了byQuad就用上一帧碰撞前的quadPos
    [BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
    public struct ExtDynamicForceSetUpdateJob : IJob
    {
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PredictedPositions>.ReadOnly PredictedPositions;
        
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<QuadPredictedPositions>.ReadOnly QuadPredictedPositions;
        
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PBDForceField>.ReadOnly ForceFields;
        
        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<IsNeedUpdate> IsNeedUpdates;
        
        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeList<int>.ParallelWriter ExtDynamicForceList;

        [ReadOnly] public int QuadCount;
        
        [ReadOnly] public bool ByQuad;
        
        public void Execute()
        {
//            if(ForceFields.Length < 1)
//                return;

            if (ByQuad)
                SetUpdateByQuad();
            else
                SetUpdateByParticle();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetUpdateByQuad()
        {
            for (int index = 0; index < QuadCount; index++)
            {
                float3 pos = QuadPredictedPositions[index].Value;

                for (int i = 0; i < ForceFields.Length; i++)
                {
                    if (ForceFields[i].IsInRange(pos))
                    {
                        IsNeedUpdates[index] = new IsNeedUpdate() { Value = true };
                        ExtDynamicForceList.AddNoResize(index);
                        break;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetUpdateByParticle()
        {
            for (int index = 0; index < QuadCount; index++)
            {
                int    start = index * 4;
                float3 pos   = float3.zero;
                for (int i = start; i < start + 4; i++)
                {
                    pos += PredictedPositions[i].Value;
                }

                pos *= 0.25f;

                for (int i = 0; i < ForceFields.Length; i++)
                {
                    if (ForceFields[i].IsInRange(pos))
                    {
                        IsNeedUpdates[index] = new IsNeedUpdate() { Value = true };
                        ExtDynamicForceList.AddNoResize(index);
                        break;
                    }
                }
            }
        }
    }
    
    [BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
    public struct ExtPostDynamicForceSetUpdateJob : IJob
    {
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PredictedPositions>.ReadOnly PredictedPositions;
        
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<QuadPredictedPositions>.ReadOnly QuadPredictedPositions;
        
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PBDForceField>.ReadOnly PostForceFields;
        
        // [WriteOnly, NativeDisableParallelForRestriction]
        // public NativeArray<IsNeedUpdate> IsNeedUpdates;
        
        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeList<int>.ParallelWriter ExtPostDynamicForceList;

        [ReadOnly] public int QuadCount;
        
        [ReadOnly] public bool ByQuad;
        
        public void Execute()
        {
//            if(PostForceFields.Length < 1)
//                return;

            if (ByQuad)
                SetUpdateByQuad();
            else
                SetUpdateByParticle();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetUpdateByQuad()
        {
            for (int index = 0; index < QuadCount; index++)
            {
                float3 pos = QuadPredictedPositions[index].Value;

                for (int i = 0; i < PostForceFields.Length; i++)
                {
                    if (PostForceFields[i].IsInRange(pos))
                    {
                        // IsNeedUpdates[index] = new IsNeedUpdate() { Value = true };
                        ExtPostDynamicForceList.AddNoResize(index);
                        break;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetUpdateByParticle()
        {
            for (int index = 0; index < QuadCount; index++)
            {
                int    start = index * 4;
                float3 pos   = float3.zero;
                for (int i = start; i < start + 4; i++)
                {
                    pos += PredictedPositions[i].Value;
                }

                pos *= 0.25f;

                for (int i = 0; i < PostForceFields.Length; i++)
                {
                    if (PostForceFields[i].IsInRange(pos))
                    {
                        // IsNeedUpdates[index] = new IsNeedUpdate() { Value = true };
                        ExtPostDynamicForceList.AddNoResize(index);
                        break;
                    }
                }
            }
        }
        
    }

    //仅计算合力
    [BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
    public struct ExtPreDynamicForceJob : IJobParallelFor, IJobParallelForBatch
    {
        [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
        public NativeArray<int>.ReadOnly UpdateList;

        [ReadOnly, NativeDisableParallelForRestriction] 
        public NativeArray<Velocity>.ReadOnly Velocities;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<Normal>.ReadOnly Normals;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<Area>.ReadOnly Areas;
        
        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<ExtForce> ExtForces;

        // 不传mass 重力挪到下阶段
        // [ReadOnly] public float3 gravity;
        [ReadOnly] public float3 wind;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Execute(int index)
        {
            int  quadID  = UpdateList[index];
            int4 indices = math.mad(quadID, 4, new int4(0, 1, 2, 3));
            for (int i = 0; i < 4; i++)
            {
                int pIndex = indices[i];

                float3
                    normal   = Normals[pIndex].Value,
                    velocity = Velocities[pIndex].Value,
                    area     = Areas[pIndex].Value;

                float
                    // invMass = InvMasses[i].Value,
                    windDotN = math.dot(velocity - wind, normal);

                if (windDotN < 0)
                {
                    normal   *= -1;
                    windDotN *= -1;
                }

                float3 force = windDotN * normal * area;

                ExtForces[pIndex] = new ExtForce() { Value = force };
            }
        }

        public void Execute(int start, int count)
        {
            for (int index = start; index < start + count; index++)
            {
                Execute(index);
            }
        }
    }

    
    [BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
    public struct ExtDynamicForceJob : IJobParallelFor, IJobParallelForBatch
    {
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<int>.ReadOnly ExtForceList;
        
        [ReadOnly, NativeDisableParallelForRestriction] 
        public NativeArray<PredictedPositions>.ReadOnly PredictedPositions;

        [ReadOnly, NativeDisableParallelForRestriction] 
        public NativeArray<Velocity>.ReadOnly Velocities;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PBDForceField>.ReadOnly ForceFields;
        
        [NativeDisableParallelForRestriction]
        public NativeArray<ExtForce> ExtForces;

        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Execute(int index)
        {
            int  quadID  = ExtForceList[index];
            int4 indices = math.mad(quadID, 4, new int4(0, 1, 2, 3));
            for (int i = 0; i < 4; i++)
            {
                int pIndex = indices[i];
                    
                float3
                    position = PredictedPositions[pIndex].Value,
                    velocity = Velocities[pIndex].Value,
                    force    = ExtForces[pIndex].Value;

                for (int j = 0; j < ForceFields.Length; j++)
                    force += ForceFields[j].CaculateForce(in position, in velocity);

                ExtForces[pIndex] = new ExtForce() { Value = force };
            }
            
        }

        public void Execute(int start, int count)
        {
            for (int index = start; index < start + count; index++)
            {
                Execute(index);
            }
        }
    }

    [BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
    public struct ExtVelocityJob : IJobParallelFor, IJobParallelForBatch
    {
        [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
        public NativeArray<int>.ReadOnly UpdateList;

        [NativeDisableParallelForRestriction] 
        public NativeArray<Velocity> Velocities;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<InvMass>.ReadOnly InvMasses;
        
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<ExtForce>.ReadOnly ExtForces;

        [ReadOnly] public float3 gravity;
        [ReadOnly] public float damping;
        [ReadOnly] public float deltaTime;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Execute(int index)
        {
            int  quadID  = UpdateList[index];
            int4 indices = math.mad(quadID, 4, new int4(0, 1, 2, 3));
            for (int i = 0; i < 4; i++)
            {
                int pIndex = indices[i];

                float3
                    velocity = Velocities[pIndex].Value,
                    force    = ExtForces[pIndex].Value;

                float
                    invMass = InvMasses[pIndex].Value;

                velocity += (force * invMass + gravity) * deltaTime;
                velocity *= math.max(-damping * invMass * deltaTime + 1, 0);
                // velocity *= math.exp(-damping * invMass * deltaTime);


                Velocities[pIndex] = new Velocity() { Value = velocity };
            }
        }

        public void Execute(int start, int count)
        {
            for (int index = start; index < start + count; index++)
            {
                Execute(index);
            }
        }
    }
    
    
    [BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
    public struct ExtPostDynamicForceJob : IJobParallelFor, IJobParallelForBatch
    {
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<int>.ReadOnly ExtForceList;

        [ReadOnly, NativeDisableParallelForRestriction] 
        public NativeArray<PredictedPositions>.ReadOnly PredictedPositions;
        
        [NativeDisableParallelForRestriction] 
        public NativeArray<Velocity> Velocities;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PBDForceField>.ReadOnly PostForceFields;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Execute(int index)
        {
            int  quadID  = ExtForceList[index];
            int4 indices = math.mad(quadID, 4, new int4(0, 1, 2, 3));
            for (int i = 0; i < 4; i++)
            {
                int pIndex = indices[i];
                    
                float3
                    position = PredictedPositions[pIndex].Value,
                    velocity = Velocities[pIndex].Value;

                for (int j = 0; j < PostForceFields.Length; j++)
                    velocity += PostForceFields[j].CaculateForce(in position, in velocity);

                Velocities[pIndex] = new Velocity() { Value = velocity };
            }
        }

        public void Execute(int start, int count)
        {
            for (int index = start; index < start + count; index++)
            {
                Execute(index);
            }
        }
    }


    [BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
    public struct ExtPredictedUpdateJob : IJobParallelFor, IJobParallelForBatch
    {
        [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
        public NativeArray<int>.ReadOnly UpdateList;

        [NativeDisableParallelForRestriction] public NativeArray<PredictedPositions> PredictedPositions;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<Velocity>.ReadOnly Velocities;

        [ReadOnly] public float deltaTime;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Execute(int index)
        {
            int  quadID  = UpdateList[index];
            int4 indices = math.mad(quadID, 4, new int4(0, 1, 2, 3));
            for (int i = 0; i < 4; i++)
            {
                int pIndex = indices[i];

                float3
                    position = PredictedPositions[pIndex].Value,
                    velocity = Velocities[pIndex].Value;

                position += velocity * deltaTime;

                PredictedPositions[pIndex] = new PredictedPositions() { Value = position };
            }
        }

        public void Execute(int start, int count)
        {
            for (int index = start; index < start + count; index++)
            {
                Execute(index);
            }
        }
    }



    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Default, 
                  FloatPrecision = FloatPrecision.High, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    public unsafe struct ExtWindFieldJob : IJobParallelForBatch
    {
        [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
        public NativeArray<int>.ReadOnly UpdateList;
        
        [NativeDisableParallelForRestriction] 
        public NativeArray<PredictedPositions> PredictedPositions;

        [NativeDisableParallelForRestriction] 
        public NativeArray<Velocity> Velocities;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<Normal>.ReadOnly Normals;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<InvMass>.ReadOnly InvMasses;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<Area>.ReadOnly Areas;

        [ReadOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        public NativeArray<float>.ReadOnly WindFieldX;
        
        [ReadOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        public NativeArray<float>.ReadOnly WindFieldY;
        
        [ReadOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        public NativeArray<float>.ReadOnly WindFieldZ;
        
        // 你怎么还在
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PBDForceField>.ReadOnly PostForceFields;
        
        [ReadOnly] public float3    gravity;
        [ReadOnly] public float3    wind;//常驻
        [ReadOnly] public PBDBounds windFieldBounds;
        [ReadOnly] public float3    windFieldOri;
        [ReadOnly] public float3    windFieldMoveDelta;
        [ReadOnly] public int3      dim;
        [ReadOnly] public int3      N;
        [ReadOnly] public float     damping;
        [ReadOnly] public float     deltaTime;
        
        public unsafe void Execute(int start, int count)
        {
            var sizeF1 = UnsafeUtility.SizeOf<float>();
            int sizeF4  = sizeF1 * 4,
                sizeF34 = sizeF1 * 12;
            
            var updatePtr  = (int*)UpdateList.GetUnsafeReadOnlyPtr();
            var posPtr     = (PredictedPositions*)PredictedPositions.GetUnsafeReadOnlyPtr();
            var veloPtr    = (Velocity*)Velocities.GetUnsafePtr();
            var normalPtr  = (Normal*)Normals.GetUnsafeReadOnlyPtr();
            var invMassPtr = (InvMass*)InvMasses.GetUnsafeReadOnlyPtr();
            var areaPtr    = (Area*)Areas.GetUnsafeReadOnlyPtr();
            // var forcePtr   = (PBDForceField*)ForceFields.GetUnsafeReadOnlyPtr();
            var pForcePtr  = (PBDForceField*)PostForceFields.GetUnsafeReadOnlyPtr();
            
            float* X = (float*)WindFieldX.GetUnsafeReadOnlyPtr(),
                   Y = (float*)WindFieldY.GetUnsafeReadOnlyPtr(),
                   Z = (float*)WindFieldZ.GetUnsafeReadOnlyPtr();
            
            for (int index = start; index < start + count; index++)
            {
                int quadID = updatePtr[index];
                int offset = quadID * 4;

                float3x4
                    positons,
                    normals,
                    velocities;
                    
                float4 areaes,
                       invMasses;

                UnsafeUtility.MemCpy(&positons, posPtr    + offset, sizeF34);
                UnsafeUtility.MemCpy(&normals, normalPtr  + offset, sizeF34);
                UnsafeUtility.MemCpy(&velocities, veloPtr + offset, sizeF34);

                UnsafeUtility.MemCpy(&areaes, areaPtr       + offset, sizeF4);
                UnsafeUtility.MemCpy(&invMasses, invMassPtr + offset, sizeF4);
                
                
                float4 winDotN = new float4()
                {
                    x = math.dot(velocities[0] - wind, normals[0]),
                    y = math.dot(velocities[1] - wind, normals[1]),
                    z = math.dot(velocities[2] - wind, normals[2]),
                    w = math.dot(velocities[3] - wind, normals[3]),
                };

                float4 tempF = winDotN * areaes;

                float3x4 forces = new float3x4(normals.c0 * tempF[0],
                                               normals.c1 * tempF[1],
                                               normals.c2 * tempF[2],
                                               normals.c3 * tempF[3]);

                int i;
                for (i = 0; i < 4; i++)
                {
                    float3 p = positons[i] + windFieldMoveDelta;
                    if(!MathematicsUtil.AABBContains(p, windFieldBounds))
                        continue;
                    float3 plocal = (p - windFieldOri);

                    forces[i] += GridUtils.TrilinearStandard(plocal, ref X, ref Y, ref Z, dim);
                }
                

                for (i = 0; i < 4; i++)
                {
                    velocities[i] += (forces[i] * invMasses[i] + gravity) * deltaTime;
                    velocities[i] *= math.max(-damping * invMasses[i] * deltaTime + 1, 0);
                }
                
                for (i = 0; i < PostForceFields.Length; i++)
                {
                    for (int j = 0; j < 4; j++)
                        velocities[j] += pForcePtr[i].CaculateForce(in positons[j], in velocities[j]);
                }
                
                
                for (i = 0; i < 4; i++)
                {
                    positons[i] += velocities[i] * deltaTime;
                }
                

                UnsafeUtility.MemCpy(posPtr  + offset, &positons, sizeF34);
                UnsafeUtility.MemCpy(veloPtr + offset, &velocities, sizeF34);
            }
        }
    }



    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Default, 
                  FloatPrecision = FloatPrecision.High, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    public unsafe struct ExtWindFieldSetUpdateJob : IJobParallelForBatch
    {
        [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
        public NativeArray<int>.ReadOnly StaticList;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<QuadPredictedPositions>.ReadOnly QuadPredictedPositions;
        
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<QuadInvMass>.ReadOnly QuadInvMasses;

        [ReadOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        public NativeArray<float>.ReadOnly WindFieldX;
        
        [ReadOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        public NativeArray<float>.ReadOnly WindFieldY;
        
        [ReadOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        public NativeArray<float>.ReadOnly WindFieldZ;

        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<IsNeedUpdate> IsNeedUpdates;

        [ReadOnly] public PBDBounds windFieldBounds;
        [ReadOnly] public float3    windFieldOri;
        [ReadOnly] public float3    windFieldMoveDelta;
        [ReadOnly] public int3      dim;
        [ReadOnly] public int3      N;

        [ReadOnly] public float activeVelocityThreshold;

        public void Execute(int start, int count)
        {
            var updatePtr  = (int*)StaticList.GetUnsafeReadOnlyPtr();

            float* X = (float*)WindFieldX.GetUnsafeReadOnlyPtr(),
                   Y = (float*)WindFieldY.GetUnsafeReadOnlyPtr(),
                   Z = (float*)WindFieldZ.GetUnsafeReadOnlyPtr();

            for (int index = start; index < start + count; index++)
            {
                int quadID = updatePtr[index];

                float3 pos = QuadPredictedPositions[quadID].Value + windFieldMoveDelta;
                
                if(!MathematicsUtil.AABBContains(pos, windFieldBounds))
                    continue;
                
                float3 plocal = (pos - windFieldOri);

                float3 wind = GridUtils.TrilinearStandard(plocal, ref X, ref Y, ref Z, dim);

                //速度场当成力场
                if (math.length(wind) * QuadInvMasses[quadID].Value * 0.01f > activeVelocityThreshold)
                    IsNeedUpdates[quadID] = new IsNeedUpdate() { Value = true };
            }
        }
    }

#endregion
    
    
    
    #region Distance


    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    public struct DistanceConstraintJob : IJobParallelForBatch
    {
        [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
        public NativeArray<int>.ReadOnly UpdateList;

        [NativeDisableParallelForRestriction] public NativeArray<PredictedPositions> PredictedPositions;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<InvMass>.ReadOnly InvMasses;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<DistanceConstraint>.ReadOnly DistanceConstraints;

        [ReadOnly, NativeDisableParallelForRestriction, NativeFixedLength(5)]
        public NativeArray<int2>.ReadOnly DisContraintIndexes;

        [ReadOnly] public float ComppressStiffness;
        [ReadOnly] public float StretchStiffness;

        public unsafe void Execute(int start, int count)
        {
            var size = UnsafeUtility.SizeOf<PredictedPositions>() * 4;
            
            var updatePtr   = (int*)UpdateList.GetUnsafeReadOnlyPtr();
            var dest        = (PredictedPositions*)PredictedPositions.GetUnsafePtr();
            var invMassPtr  = (InvMass*)InvMasses.GetUnsafeReadOnlyPtr();
            var disCnstPtr  = (DistanceConstraint*)DistanceConstraints.GetUnsafeReadOnlyPtr();
            var disIndexPtr = (int2*)DisContraintIndexes.GetUnsafeReadOnlyPtr();
            
            QuadDistanceConstraints quadDistanceConstraints = new QuadDistanceConstraints();
            
            for (int index = start; index < start + count; index++)
            {
                int  quadID  = updatePtr[index];

                int pOffset = quadID * 4,
                    dOffset = quadID * 5;

                float3x4 quadPos;
                
                UnsafeUtility.MemCpy(&quadPos, dest + pOffset, size);
                
                for (int i = 0; i < 5; i++)
                {
                    int                dIndex = i + dOffset;
                    DistanceConstraint cnstr  = disCnstPtr[dIndex];

                    (int indexA, int indexB) = (disIndexPtr[i].x, disIndexPtr[i].y);

                    float3 predPosA = quadPos[indexA],
                           predPosB = quadPos[indexB];

                    float invMassA = invMassPtr[indexA].Value,
                          invMassB = invMassPtr[indexB].Value;

                    float restLen = cnstr.restLength;

                    float3 dir     = predPosB - predPosA;
                    float  length  = math.length(dir);
                    float  invMass = invMassA + invMassB;
                    if (invMass <= math.EPSILON || length <= math.EPSILON)
                        continue;

                    dir /= length;

                    float3 dP = float3.zero;
                    if (length <= restLen) //compress
                        dP = ComppressStiffness * dir * (length - restLen) / invMass;
                    else //stretch
                        dP = StretchStiffness * dir * (length - restLen) / invMass;

                    quadDistanceConstraints.AddConstraint(indexA, dP  * invMassA);
                    quadDistanceConstraints.AddConstraint(indexB, -dP * invMassB);
                }

                quadPos += quadDistanceConstraints.GetDelta();

                UnsafeUtility.MemCpy(dest + pOffset, &quadPos, size);

                quadDistanceConstraints.Clear();
            }
        }
    }


#endregion
    
    

    #region Bending

    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    public struct BendConstraintJob : IJobParallelForBatch
    {
        [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
        public NativeArray<int>.ReadOnly UpdateList;

        [NativeDisableParallelForRestriction] 
        public NativeArray<PredictedPositions> PredictedPositions;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<InvMass>.ReadOnly InvMasses;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<BendConstraint>.ReadOnly BendConstraints;

        [ReadOnly] public float BendStiffness;

#if UNITY_EDITOR
        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<float3> DebugArray;
#endif

        public unsafe void Execute(int start, int count)
        {
            int size     = UnsafeUtility.SizeOf<PredictedPositions>() * 4,
                massSize = UnsafeUtility.SizeOf<InvMass>()            * 4;
            
            var updatePtr   = (int*)UpdateList.GetUnsafeReadOnlyPtr();
            var dest        = (PredictedPositions*)PredictedPositions.GetUnsafePtr();
            var invMassPtr  = (InvMass*)InvMasses.GetUnsafeReadOnlyPtr();
            var bendCnstPtr = (BendConstraint*)BendConstraints.GetUnsafeReadOnlyPtr();
            
#if UNITY_EDITOR
            var debugPtr = (float3*)DebugArray.GetUnsafePtr();
#endif
            
            for (int index = start; index < start + count; index++)
            {
                int  quadID  = updatePtr[index];
                int  offset  = quadID * 4;

                BendConstraint cnstr = bendCnstPtr[quadID];

                float3x4 positions;
                UnsafeUtility.MemCpy(&positions, dest + offset, size);

                float4 invMasses;
                
                UnsafeUtility.MemCpy(&invMasses, invMassPtr + offset, massSize);
                

                float3
                    crr0 = float3.zero,
                    crr1 = float3.zero,
                    crr2 = float3.zero,
                    crr3 = float3.zero;

                // if (solve_BendConstraint_matthias(
                //         positions[1], invMasses[1],
                //         positions[0], invMasses[0],
                //         positions[2], invMasses[2],
                //         positions[3], invMasses[3],
                //         cnstr.restAngle,
                //         BendStiffness,
                //         ref crr1, ref crr0, ref crr2, ref crr3
                if (solve_BendConstraint_rbridson(
                    positions[0], invMasses[0],
                    positions[3], invMasses[3],
                    positions[2], invMasses[2],
                    positions[1], invMasses[1],
                    cnstr.restAngle,
                    BendStiffness,
                    ref crr0, ref crr3, ref crr2, ref crr1
                ))
                {
                    float3x4 crr = new float3x4(crr0, crr1, crr2, crr3);
                    positions += crr;

                    UnsafeUtility.MemCpy(dest + offset, &positions, size);
#if UNITY_EDITOR
                    
                    UnsafeUtility.MemCpy(debugPtr + offset, &crr, size);
#endif
                }
            }
        }

        //https://matthias-research.github.io/pages/publications/posBasedDyn.pdf
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool solve_BendConstraint_matthias(
            float3 p0, float invMass0,
            float3 p1, float invMass1,
            float3 p2, float invMass2,
            float3 p3, float invMass3,
            float restAngle,
            float stiffness,
            ref float3 crr0, ref float3 crr1, ref float3 crr2, ref float3 crr3)
        {
            float3
                a = (p1 - p0),
                b = (p2 - p0),
                c = (p3 - p0);

            float3
                n1 = math.cross(a, b),
                n2 = math.cross(a, c);

            float invLen1 = 1.0f / math.length(n1);
            float invLen2 = 1.0f / math.length(n2);

            n1 *= invLen1;
            n2 *= invLen2;
            // n1 = math.normalize(n1);
            // n2 = math.normalize(n2);

            float d = math.dot(n1, n2);

            d = math.clamp(d, -1, 1);

            float currentAngle = math.acos(d);

            float constraint = currentAngle - restAngle;

            if (math.abs(constraint) < 1e-5f)
                return false;

            float3 dp3 = (math.cross(a, n1) + math.cross(n2, a) * d) * invLen2;
            float3 dp2 = (math.cross(a, n2) + math.cross(n1, a) * d) * invLen1;
            float3 dp1 = -(math.cross(b, n2) + math.cross(n1, b) * d) * invLen1
                         - (math.cross(c, n1) + math.cross(n2, c) * d) * invLen2;
            float3 dp0 = -dp1 - dp2 - dp3;

            float w_sum =
                invMass0 * math.dot(dp0, dp0) +
                invMass1 * math.dot(dp1, dp1) +
                invMass2 * math.dot(dp2, dp2) +
                invMass3 * math.dot(dp3, dp3);

            if (w_sum < 1e-10f)
                return false;

            float lambda = -constraint * math.sqrt(1 - d * d) / w_sum;

            lambda *= stiffness;

            // if (math.dot(math.cross(n1, n2), a) > 0)
            //     lambda = -lambda;

            crr0 = lambda * invMass0 * dp0;
            crr1 = lambda * invMass1 * dp1;
            crr2 = lambda * invMass2 * dp2;
            crr3 = lambda * invMass3 * dp3;

            return true;
        }

        //https://www.cs.ubc.ca/~rbridson/docs/cloth2003.pdf
        //顶点序号与文内的不一至
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        bool solve_BendConstraint_rbridson(
            float3 p0, float invMass0,
            float3 p1, float invMass1,
            float3 p2, float invMass2,
            float3 p3, float invMass3,
            float restAngle,
            float stiffness,
            ref float3 crr0, ref float3 crr1, ref float3 crr2, ref float3 crr3)
        {
//            if (invMass0 < math.EPSILON && invMass1 < math.EPSILON)
//                return false;

            float3 e = p3 - p2;
            float elen = math.length(e);

//            if (elen < math.EPSILON)
//                return false;

            float invElen = 1.0f / elen;

            float3 n1 = math.cross(p2 - p0, p3 - p0),
                n2 = math.cross(p3 - p1, p2 - p1);
            n1 /= math.lengthsq(n1);
            n2 /= math.lengthsq(n2);

            float3 d0 = elen * n1;
            float3 d1 = elen * n2;
            float3 d2 = math.dot(p0 - p3, e) * invElen * n1 + math.dot(p1 - p3, e) * invElen * n2;
            float3 d3 = math.dot(p2 - p0, e) * invElen * n1 + math.dot(p2 - p1, e) * invElen * n2;

            n1 = math.normalize(n1);
            n2 = math.normalize(n2);

            float dot = math.dot(n1, n2);
            dot = math.clamp(dot, -1, 1);

            float phi = math.acos(dot);
            // float phi = (-0.6981317f * dot * dot - 0.8726646f) * dot + 1.570796f;

            float lambda =
                invMass0 * math.lengthsq(d0) +
                invMass1 * math.lengthsq(d1) +
                invMass2 * math.lengthsq(d2) +
                invMass3 * math.lengthsq(d3);

//            if (lambda == 0)
//                return false;
            lambda = math.max(lambda, math.EPSILON);

            lambda = (phi - restAngle) / lambda * stiffness;

            if (math.dot(math.cross(n1, n2), e) > 0)
                lambda = -lambda;

            crr0 = -invMass0 * lambda * d0;
            crr1 = -invMass0 * lambda * d1;
            crr2 = -invMass0 * lambda * d2;
            crr3 = -invMass0 * lambda * d3;
            return true;
        }
    }

    #endregion
    
    
    #region Particle2ParticleCollision

    /// <summary>
    /// 粒子-粒子
    /// </summary>

    
    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Default, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    public unsafe struct SPHOptimizedCollisionDetectionReverseSearchJob : IJobParallelFor
    {
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<int>.ReadOnly UpdateList;
        
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PredictedPositions>.ReadOnly PredictedPositions;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<Velocity>.ReadOnly Velocities;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<InvMass>.ReadOnly InvMasses;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<QuadPredictedPositions> QuadPredictedPositions;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<QuadVelocity>.ReadOnly QuadVelocities;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<QuadInvMass>.ReadOnly QuadInvMasses;

        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<ParticleCollisionConstraint> ParticleCollisionConstraints;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<BaseHash> SortedHashes;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<int> Hashes;
        
//        [ReadOnly, NativeDisableParallelForRestriction]
//        public UnsafeParallelHashMap<PrecomputedHashKey, HashRange>.ReadOnly HashRanges;

        [ReadOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        public SimpleHashArray<HashRange> HashRanges;

        [ReadOnly, NativeDisableParallelForRestriction, NativeFixedLength(27)]
        public NativeArray<int3>.ReadOnly neighborOffsets;

        [ReadOnly] public float4 filterParams; //pos radius

        [ReadOnly] public float radius;

        [ReadOnly] public float cellRadius;

        [ReadOnly] public float CollisionStiffness;

        [ReadOnly] public int JobNum;

        [ReadOnly] public int bucketCapacityMask;

        [ReadOnly] public bool collisionByQuad;

        public void Execute(int index)
        {
            int length = Hashes.Length;

            if (length < 1 || JobNum < 1)
                return;

            int actualJobNum  = math.min(JobNum, length);
            int segmentLength = (length + actualJobNum - 1) / actualJobNum;

            float radiusSum   = radius + radius;
            float radiusSumSq = radiusSum * radiusSum;

            int start = segmentLength * index,
                end   = math.min(math.mad(segmentLength, index, segmentLength), length);

            if (collisionByQuad)
                CollisionIntersectionByQuad(start, end, radiusSum, radiusSumSq);
            else
                CollisionIntersectionByParticles(start, end, radiusSum, radiusSumSq);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void CollisionIntersectionByQuad(int start, int end, float radiusSum, float radiusSumSq)
        {
            var keyPtr       = (int*)Hashes.GetUnsafeReadOnlyPtr();
            var hashIndexPtr = (BaseHash*)SortedHashes.GetUnsafeReadOnlyPtr();
            var posPtr       = (QuadPredictedPositions*)QuadPredictedPositions.GetUnsafeReadOnlyPtr();
            var massPtr      = (QuadInvMass*)QuadInvMasses.GetUnsafeReadOnlyPtr();
            var neighberPtr  = (int3*)neighborOffsets.GetUnsafeReadOnlyPtr();
            var cnstrPtr     = (ParticleCollisionConstraint*)ParticleCollisionConstraints.GetUnsafePtr();
            var size         = UnsafeUtility.SizeOf<ParticleCollisionConstraint>();
            
            int neighberLength = neighborOffsets.Length;
            int hashMask       = MathematicsUtil.NextPowerOfTwo(UpdateList.Length)- 1;

            for (int i = start; i < end; i++)
            {
                var hash  = keyPtr[i];
                HashRanges.TryGetValue(hash, out HashRange range);
                for (int A = range.Start; A <= range.End; A++)
                {
                    int indexA = hashIndexPtr[A].Index;

                    float3 posA = posPtr[indexA].Value;

                    if (!MathematicsUtil.InSphereSpacial(in posA, cellRadius, in filterParams))
                        continue;

                    float invMassA = massPtr[indexA].Value;
                    float3 /*velocity = QuadVelocities[indexA].Value,*/
                        delta = float3.zero;

                    int cnstrsCount = 0;

                    int3 cellPos = HashUtility.PosToGrid(posA, cellRadius);

                    for (int offset = 0; offset < neighberLength; offset++)
                    {
                        int3 offsetStep = neighberPtr[offset];

//                            if (math.dot(offsetStep, velocity) < 0)
//                                continue;

                        int3 offsetPos = cellPos + offsetStep;
                        int  nextHash  = HashUtility.GridToHash(offsetPos, hashMask);

                        if (HashRanges.TryGetValue(nextHash, out var hashRange))
                        {
                            for (int B = hashRange.Start; B <= hashRange.End; B++)
                            {
                                int indexB = hashIndexPtr[B].Index;

                                if (indexB == indexA)
                                    continue;

                                float3 posB = posPtr[indexB].Value;

                                float3 dir = posA - posB;

                                float disSq = math.lengthsq(dir);

                                if (disSq > radiusSumSq || disSq <= math.EPSILON)
                                    continue;


                                float dis = math.sqrt(disSq);

                                float invMassB = massPtr[indexB].Value;
                                float invMass  = invMassA + invMassB;

                                float3 dP = CollisionStiffness * (dis - radiusSum) * (dir / dis) / invMass;

                                delta -= dP * invMassA;
                                cnstrsCount++;
                            }
                        }
                    }

                    if (cnstrsCount > 0)
                    {
                        var deltaPosition = new ParticleCollisionConstraint()
                        {
                            Delta            = delta,
                            ConstraintsCount = cnstrsCount,
                        };
                        UnsafeUtility.MemCpyReplicate(cnstrPtr + indexA * 4, &deltaPosition, size, 4);
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void CollisionIntersectionByParticles(int start, int end, float radiusSum, float radiusSumSq)
        {
            var keyPtr       = (int*)Hashes.GetUnsafeReadOnlyPtr();
            var hashIndexPtr = (BaseHash*)SortedHashes.GetUnsafeReadOnlyPtr();
            var posPtr       = (PredictedPositions*)PredictedPositions.GetUnsafeReadOnlyPtr();
            var massPtr      = (InvMass*)InvMasses.GetUnsafeReadOnlyPtr();
            var neighberPtr  = (int3*)neighborOffsets.GetUnsafeReadOnlyPtr();
            var cnstrPtr     = (ParticleCollisionConstraint*)ParticleCollisionConstraints.GetUnsafePtr();
            
            int neighberLength = neighborOffsets.Length;
            int hashMask       = (MathematicsUtil.NextPowerOfTwo(UpdateList.Length) << 2) - 1;
            
            for (int i = start; i < end; i++)
            {
                var hash  = keyPtr[i];
                HashRanges.TryGetValue(hash, out HashRange range);
                for (int A = range.Start; A <= range.End; A++)
                {
                    int indexA = hashIndexPtr[A].Index;

                    float3 posA = posPtr[indexA].Value;

                    if (!MathematicsUtil.InSphereSpacial(in posA, cellRadius, in filterParams))
                        continue;

                    float invMassA = massPtr[indexA].Value;
                    int3  cellPos  = HashUtility.PosToGrid(posA, cellRadius);
                    float3 /*velocity = Velocities[indexA].Value,*/
                        delta = float3.zero;

                    int cnstrsCount = 0;
                    for (int offset = 0; offset < neighberLength; offset++)
                    {
                        int3 offsetStep = neighberPtr[offset];

//                            if (math.dot(offsetStep, velocity) < 0)
//                                continue;

                        int3 offsetPos = cellPos + offsetStep;
                        int  nextHash  = HashUtility.GridToHash(offsetPos, hashMask);

                        if (HashRanges.TryGetValue(nextHash, out var hashRange))
                        {
                            for (int B = hashRange.Start; B <= hashRange.End; B++)
                            {
                                int indexB = hashIndexPtr[B].Index;

                                if (indexB == indexA || (indexB / 4) == (indexA / 4))
                                    continue;

                                float3 posB = posPtr[indexB].Value;

                                float3 dir = posA - posB;

                                float disSq = math.lengthsq(dir);

                                if (disSq > radiusSumSq || disSq <= math.EPSILON)
                                    continue;


                                float dis = math.sqrt(disSq);

                                float invMassB = massPtr[indexB].Value;
                                float invMass  = invMassA + invMassB;

                                float3 dP = CollisionStiffness * (dis - radiusSum) * (dir / dis) / invMass;

                                delta -= dP * invMassA;
                                cnstrsCount++;
                            }
                        }
                    }

                    if (cnstrsCount > 0)
                    {
                        cnstrPtr[indexA] = new ParticleCollisionConstraint()
                        {
                            Delta = delta,
                            ConstraintsCount = cnstrsCount,
                        };
                    }
                }
            }
        }
    }
    
    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Default, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    public struct SPHOptimizedCollisionDetectionJob : IJobParallelForBatch
    {
        [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
        public NativeArray<int>.ReadOnly UpdateList;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PredictedPositions>.ReadOnly PredictedPositions;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<Velocity>.ReadOnly Velocities;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<InvMass>.ReadOnly InvMasses;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<QuadPredictedPositions> QuadPredictedPositions;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<QuadVelocity>.ReadOnly QuadVelocities;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<QuadInvMass>.ReadOnly QuadInvMasses;

        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<ParticleCollisionConstraint> ParticleCollisionConstraints;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<BaseHash> SortedHashes;

        [ReadOnly, NativeDisableParallelForRestriction]
        public UnsafeParallelHashMap<PrecomputedHashKey, HashRange>.ReadOnly hashRanges;

        [ReadOnly, NativeDisableParallelForRestriction, NativeFixedLength(27)]
        public NativeArray<int3>.ReadOnly neighborOffsets;

        [ReadOnly] public float4 filterParams; //pos radius

        [ReadOnly] public float radius;

        [ReadOnly] public float cellRadius;

        [ReadOnly] public float CollisionStiffness;

        [ReadOnly] public int bucketCapacityMask;

        [ReadOnly] public bool collisionByQuad;

        public void Execute(int start, int count)
        {
            float radiusSum   = radius + radius;
            float radiusSumSq = radiusSum * radiusSum;

            if (collisionByQuad)
            {
                int hashMask = MathematicsUtil.NextPowerOfTwo(UpdateList.Length) - 1;
                for (int index = start; index < start + count; index++)
                {
                    int quadID = UpdateList[index];

                    CollisionIntersectionByQuad(quadID, radiusSum, radiusSumSq, hashMask);
                }
            }
            else
            {
                int hashMask = (MathematicsUtil.NextPowerOfTwo(UpdateList.Length) << 2) - 1;
                for (int index = start; index < start + count; index++)
                {
                    int quadID = UpdateList[index];

                    CollisionIntersectionByParticles(quadID, radiusSum, radiusSumSq, hashMask);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void CollisionIntersectionByQuad(int index, float radiusSum, float radiusSumSq, int hashMask)
        {
            float3
                posA     = QuadPredictedPositions[index].Value,
                velocity = QuadVelocities[index].Value,
                delta    = float3.zero;

            if (!MathematicsUtil.InSphereSpacial(in posA, cellRadius, in filterParams))
                return;

            float
                invMassA = QuadInvMasses[index].Value;

            int cnstrsCount = 0;

            int3 cellPos = HashUtility.PosToGrid(posA, cellRadius);

            for (int offset = 0; offset < neighborOffsets.Length; offset++)
            {
                int3 offsetStep = neighborOffsets[offset];
                if (math.dot(offsetStep, velocity) < 0)
                    continue;
                int3 offsetPos = cellPos + offsetStep;
                int  nextHash  = HashUtility.GridToHash(offsetPos, hashMask);

                if (hashRanges.TryGetValue(nextHash, out var hashRange))
                {
                    for (int i = hashRange.Start; i <= hashRange.End; i++)
                    {
                        int indexB = SortedHashes[i].Index;

                        if (indexB == index)
                            continue;

                        float3 posB     = QuadPredictedPositions[indexB].Value;
                        float  invMassB = QuadInvMasses[indexB].Value;

                        float3 dir = posA - posB;

                        float disSq = math.lengthsq(dir);

                        if (disSq > radiusSumSq || disSq <= math.EPSILON)
                            continue;


                        float dis     = math.sqrt(disSq);
                        float invMass = invMassA + invMassB;

                        float3 dP = CollisionStiffness * (dis - radiusSum) * (dir / dis) / invMass;

                        delta -= dP * invMassA;
                        cnstrsCount++;
                    }
                }
            }

            if (cnstrsCount > 0)
            {
                var deltaPosition = new ParticleCollisionConstraint()
                {
                    Delta            = delta,
                    ConstraintsCount = cnstrsCount,
                };

                int4 indices = math.mad(index, 4, new int4(0, 1, 2, 3));
                ParticleCollisionConstraints[indices.x] = deltaPosition;
                ParticleCollisionConstraints[indices.y] = deltaPosition;
                ParticleCollisionConstraints[indices.z] = deltaPosition;
                ParticleCollisionConstraints[indices.w] = deltaPosition;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void CollisionIntersectionByParticles(int index, float radiusSum, float radiusSumSq, int hashMask)
        {
            int start = index * 4;

            for (int indexA = start; indexA < start + 4; indexA++)
            {
                float3
                    posA     = PredictedPositions[indexA].Value,
//                    velocity = Velocities[indexA].Value,
                    delta    = float3.zero;

                if (!MathematicsUtil.InSphereSpacial(in posA, cellRadius, in filterParams))
                    continue;

                float invMassA = InvMasses[indexA].Value;

                int3 cellPos = HashUtility.PosToGrid(posA, cellRadius);
                // int hash = HashUtility.GridToHash(cellPos, numCells);

                int cnstrsCount = 0;
                for (int offset = 0; offset < neighborOffsets.Length; offset++)
                {
                    int3 offsetStep = neighborOffsets[offset];
//                    if (math.dot(offsetStep, velocity) < 0)
//                        continue;
                    int3 offsetPos = cellPos + offsetStep;
                    int  nextHash  = HashUtility.GridToHash(offsetPos, hashMask);

                    if (hashRanges.TryGetValue(nextHash, out var hashRange))
                    {
                        for (int i = hashRange.Start; i <= hashRange.End; i++)
                        {
                            int indexB = SortedHashes[i].Index;

                            if (indexB == indexA || (indexB / 4) == index)
                                continue;

                            float3 posB = PredictedPositions[indexB].Value;

                            float3 dir = posA - posB;

                            float disSq = math.lengthsq(dir);

                            if (disSq > radiusSumSq || disSq <= math.EPSILON)
                                continue;


                            float dis = math.sqrt(disSq);

                            float invMassB = InvMasses[indexB].Value;
                            float invMass  = invMassA + invMassB;

                            float3 dP = CollisionStiffness * (dis - radiusSum) * (dir / dis) / invMass;

                            delta -= dP * invMassA;
                            cnstrsCount++;
                        }
                    }

                }

                if (cnstrsCount > 0)
                {
                    ParticleCollisionConstraints[indexA] = new ParticleCollisionConstraint()
                    {
                        Delta = delta,
                        //
                        ConstraintsCount = cnstrsCount,
                    };
                }
            }
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Default, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    public struct InterParticlesCollisions : IJobParallelForBatch
    {
        [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
        public NativeArray<int>.ReadOnly UpdateList;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PredictedPositions>.ReadOnly PredictedPositions;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<InvMass>.ReadOnly InvMasses;
        
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<QuadPredictedPositions> QuadPredictedPositions;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<QuadInvMass>.ReadOnly QuadInvMasses;

        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<ParticleCollisionConstraint> ParticleCollisionConstraints;

        [ReadOnly] public NativeParallelMultiHashMap<int, int>.ReadOnly hashMap;

        [ReadOnly] public float4 filterParams; //pos radius

        [ReadOnly] public float radius;

        [ReadOnly] public float cellRadius;

        [ReadOnly] public float CollisionStiffness;

        [ReadOnly] public bool collisionByQuad;

        [ReadOnly] public int bucketCapacityMask;

        public void Execute(int start, int count)
        {
            float radiusSum   = radius + radius;
            float radiusSumSq = radiusSum * radiusSum;
            if (collisionByQuad)
            {
                for (int index = start; index < start + count; index++)
                {
                    int quadID = UpdateList[index];

                    CollisionIntersectionByQuad(quadID, radiusSum, radiusSumSq);
                }
            }
            else
            {
                for (int index = start; index < start + count; index++)
                {
                    int quadID = UpdateList[index];

                    CollisionIntersectionByParticles(quadID, radiusSum, radiusSumSq);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void CollisionIntersectionByQuad(int index, float radiusSum, float radiusSumSq)
        {
            float3 posA = QuadPredictedPositions[index].Value;

            if (!MathematicsUtil.InSphereSpacial(in posA, cellRadius, in filterParams))
                return;

            float invMassA = QuadInvMasses[index].Value;

            int3 cellPos = HashUtility.PosToGrid(posA, cellRadius);
            int  hash    = HashUtility.GridToHash(cellPos, bucketCapacityMask);

            bool found = hashMap.TryGetFirstValue(hash, out int indexB, out var iterator);

            float3 delta       = float3.zero;
            int    cnstrsCount = 0;

            while (found)
            {
                if (index == indexB)
                {
                    found = hashMap.TryGetNextValue(out indexB, ref iterator);
                    continue;
                }

                float3 posB     = QuadPredictedPositions[indexB].Value;
                float  invMassB = QuadInvMasses[indexB].Value;

                float3 dir = posA - posB;

                float disSq = math.lengthsq(dir);

                if (disSq > radiusSumSq || disSq <= math.EPSILON)
                {
                    found = hashMap.TryGetNextValue(out indexB, ref iterator);
                    continue;
                }

                float dis = math.sqrt(disSq);

                float invMass = invMassA + invMassB;

                float3 dP = CollisionStiffness * (dis - radiusSum) * (dir / dis) / invMass;

                delta -= dP * invMassA;
                cnstrsCount++;

                found = hashMap.TryGetNextValue(out indexB, ref iterator);
            }

            if (cnstrsCount > 0)
            {
                var deltaPosition = new ParticleCollisionConstraint()
                {
                    Delta            = delta,
                    ConstraintsCount = cnstrsCount,
                };

                int4 indices = math.mad(index, 4, new int4(0, 1, 2, 3));
                ParticleCollisionConstraints[indices[0]] = deltaPosition;
                ParticleCollisionConstraints[indices[1]] = deltaPosition;
                ParticleCollisionConstraints[indices[2]] = deltaPosition;
                ParticleCollisionConstraints[indices[3]] = deltaPosition;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void CollisionIntersectionByParticles(int index, float radiusSum, float radiusSumSq)
        {
            int start = index * 4;

            for (int indexA = start; indexA < start + 4; indexA++)
            {
                float3 posA = PredictedPositions[indexA].Value;

                if (!MathematicsUtil.InSphereSpacial(in posA, cellRadius, in filterParams))
                    continue;

                float invMassA = InvMasses[indexA].Value;

                int3 cellPos = HashUtility.PosToGrid(posA, cellRadius);
                int  hash    = HashUtility.GridToHash(cellPos, bucketCapacityMask);

                bool found = hashMap.TryGetFirstValue(hash, out int indexB, out var iterator);

                float3 delta       = float3.zero;
                int    cnstrsCount = 0;
                while (found)
                {
                    if (indexB == indexA || (indexB / 4) == index)
                    {
                        found = hashMap.TryGetNextValue(out indexB, ref iterator);
                        continue;
                    }

                    float3 posB = PredictedPositions[indexB].Value;

                    float3 dir = posA - posB;

                    float disSq = math.lengthsq(dir);

                    if (disSq > radiusSumSq || disSq <= math.EPSILON)
                    {
                        found = hashMap.TryGetNextValue(out indexB, ref iterator);
                        continue;
                    }

                    float dis = math.sqrt(disSq);

                    float invMassB = InvMasses[indexB].Value;
                    float invMass  = invMassA + invMassB;

                    float3 dP = CollisionStiffness * (dis - radiusSum) * (dir / dis) / invMass;

                    delta -= dP * invMassA;
                    cnstrsCount++;

                    found = hashMap.TryGetNextValue(out indexB, ref iterator);
                }

                if (cnstrsCount > 0)
                {
                    ParticleCollisionConstraints[indexA] = new ParticleCollisionConstraint()
                    {
                        Delta = delta,
                        //
                        ConstraintsCount = cnstrsCount,
                    };
                }
            }
        }
    }

    [BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
    public struct ReCaculateQuadVelocityJob : IJobParallelForBatch
    {
        [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
        public NativeArray<int>.ReadOnly UpdateList;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<Velocity>.ReadOnly Velocities;

        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<QuadVelocity> QuadVelocities;
        
        [ReadOnly]
        private static readonly float4 weightMatrix = new float4(
            0.25f, 0.25f, 0.25f, 0.25f
        );

        public unsafe void Execute(int start, int count)
        {
            var size = UnsafeUtility.SizeOf<Velocity>() * 4;
            
            var updatePtr = (int*)UpdateList.GetUnsafeReadOnlyPtr();
            var source = (Velocity*)Velocities.GetUnsafeReadOnlyPtr();
            var dest   = (QuadVelocity*)QuadVelocities.GetUnsafePtr();
            
            for (int index = start; index < start + count; index++)
            {
                int quadID = updatePtr[index];
                
                float3x4 velocites;
                
                UnsafeUtility.MemCpy(&velocites, source + quadID * 4, size);

//                float3 velocity = (velocites.c0 + velocites.c1 + velocites.c2 + velocites.c3) * 0.25f;

                float3 velocity = math.mul(velocites, weightMatrix);
                
                dest[quadID] = new QuadVelocity() { Value = velocity };
            }
        }
    }

    [BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
    public struct AddParticleCollisionConstraintToPosition : IJobParallelForBatch
    {
        [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
        public NativeArray<int>.ReadOnly UpdateList;

        [NativeDisableParallelForRestriction] public NativeArray<PredictedPositions> PredictedPositions;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<ParticleCollisionConstraint> ParticleCollisionConstraints;

        public unsafe void Execute(int start, int count)
        {
            var size     = UnsafeUtility.SizeOf<float3x4>();
            var stride   = UnsafeUtility.SizeOf<float3>();
            var cnstSize = UnsafeUtility.SizeOf<ParticleCollisionConstraint>();

            var updatePtr = (int*)UpdateList.GetUnsafeReadOnlyPtr();
            var source    = (ParticleCollisionConstraint*)ParticleCollisionConstraints.GetUnsafeReadOnlyPtr();
            var dest      = (PredictedPositions*)PredictedPositions.GetUnsafePtr();
            
            for (int index = start; index < start + count; index++)
            {
                int offset = updatePtr[index] * 4;
                
                float3x4 positions, delta;

                //读4个pos
                UnsafeUtility.MemCpy(&positions, dest + offset, size);

                //读4个Constraints.Delta
                UnsafeUtility.MemCpyStride(&delta, stride, source + offset, cnstSize , stride, 4);

                //计算,以每帧清空Constraint为前提
                float3x4 predictedPositions = positions + delta;
                
                //回写
                UnsafeUtility.MemCpy(dest + offset, &predictedPositions, size);
            }
        }
    }

#endregion
    
    
    

    #region RigiBodyCollision
    

    [BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
    public struct AddRigiCollisionConstraintToPositionByDivi : IJobParallelForBatch
    {
        [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
        public NativeArray<int>.ReadOnly UpdateList;

        // [ReadOnly, NativeDisableParallelForRestriction]
        // public NativeArray<Position>.ReadOnly Positions;

        [NativeDisableParallelForRestriction] 
        public NativeArray<PredictedPositions> PredictedPositions;

        [NativeDisableParallelForRestriction] 
        public NativeArray<Velocity> Velocities;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<RigiCollisionConstraint> RigiCollisionConstraints;

        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<IsNeedUpdate> IsNeedUpdates;

        [ReadOnly] public float Threshold;
        
        public void Execute(int start, int count)
        {
            float thresholdSQ  = Threshold;
            for (int index = start; index < start + count; index++)
            {
                bool4 beUpdates    = true;
                bool  hasCollision = false;

                int  quadID  = UpdateList[index];
                
                int4 indices = math.mad(quadID, 4, new int4(0, 1, 2, 3));
                for (int i = 0; i < 4; i++)
                {
                    int pIndex      = indices[i];
                    var rigiCnst    = RigiCollisionConstraints[pIndex];
                    int cnstrsCount = rigiCnst.ConstraintsCount;
                    if (cnstrsCount > 0)
                    {
                        var position = PredictedPositions[pIndex].Value;

                        var delta    = rigiCnst.Delta;
                        var velocity = rigiCnst.Velocity;

                        Velocities[pIndex] = new Velocity()
                        {
                            Value = velocity
                        };
                        PredictedPositions[pIndex] = new PredictedPositions()
                        {
                            Value = position + delta / rigiCnst.ConstraintsCount
                        };

                        if ((math.lengthsq(velocity) < thresholdSQ))
                            beUpdates[i] = false;

                        hasCollision = true;
                    }
                }

                if (hasCollision)
                {
                    int countbits = math.countbits((uint)MathematicsUtil.bitmask(beUpdates));
                    IsNeedUpdates[quadID] = new IsNeedUpdate() { Value = countbits > 1 };
                }
            }
        }
    }
    
    [BurstCompile]
    public struct ReadRigibodyColliderTransformJob : IJobParallelForTransform
    {
        [NativeDisableParallelForRestriction] 
        public NativeList<PBDCustomColliderInfo> collider;

        [ReadOnly] public float DeltaTimeInv;

        public void Execute(int index, TransformAccess transform)
        {
            if (transform.isValid)
            {
                ref var data = ref collider.ElementAt(index);
                if (data.bStatic)
                    return;
                data.Position = transform.position;
                data.Rotation = transform.rotation;
                data.Scale = MathematicsUtil.GetLossyScale(transform);
                data.Prepare(DeltaTimeInv);
            }
        }
    }

    //to 并行
    [BurstCompile]
    public struct RigibodySpatiaHashingJob : IJob
    {
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PBDCustomColliderInfo>.ReadOnly Colliders;
        
        [WriteOnly] public NativeList<BaseHash> RigidBodyHashes;

#if UNITY_EDITOR
        [WriteOnly] public NativeList<float3> VoxelDebug;

        [ReadOnly] public bool Debug;
#endif

        [ReadOnly] public float cellRadius; //不同于粒子格子尺寸
        [ReadOnly] public int   bucketCapacityMask;

        public void Execute()
        {
            NativeList<int> bodyHashes = new NativeList<int>(2048 << 4, Allocator.Temp);

            for (int i = 0; i < Colliders.Length; i++)
            {
                var c = Colliders[i];
                float3
                    min = c.boundsMin,
                    max = c.boundsMax;

                bodyHashes.Clear();

                if (math.lengthsq(max - min) > 25)
                {
                    HashUtility.CalculateAABBCellHashes(min, max, cellRadius,bucketCapacityMask, bodyHashes
#if UNITY_EDITOR
                                                      , VoxelDebug, Debug
#endif
                    );
                }
                else
                {
                    HashUtility.CalculateAABBCellHashesFast(min, max, cellRadius,bucketCapacityMask, bodyHashes
#if UNITY_EDITOR
                                                          , VoxelDebug, Debug
#endif
                    );
                }

                for (int j = 0; j < bodyHashes.Length; j++)
                {
                    RigidBodyHashes.AddNoResize(new BaseHash()
                    {
                        Index = i,
                        Hash  = bodyHashes[j],
                    });
                }
            }

            bodyHashes.Dispose();
        }
    }


    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Default, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    public unsafe struct SPHRigibody2ParticleHashearchJob : IJobParallelForBatch
    {
        [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
        public NativeArray<int>.ReadOnly UpdateList;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PBDCustomColliderInfo>.ReadOnly Colliders;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PredictedPositions>.ReadOnly PredictedPositions;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<Radius>.ReadOnly Radius;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<Velocity>.ReadOnly Velocities;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<QuadPredictedPositions>.ReadOnly QuadPredictedPositions;

        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<RigiCollisionConstraint> RigiCollisionConstraints;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<BaseHash> RigidBodyHashes;

        [ReadOnly, NativeDisableParallelForRestriction]
        public UnsafeParallelHashMap<PrecomputedHashKey, HashRange>.ReadOnly RigidBodyHashRanges;

        [ReadOnly, NativeDisableParallelForRestriction, NativeFixedLength(27)]
        public NativeArray<int3>.ReadOnly neighborOffsets;

        [ReadOnly, NativeDisableParallelForRestriction, NativeFixedLength(1)]
        public NativeArray<PBDBounds>.ReadOnly SceneBounds;

        [ReadOnly] public float QuadRadius;
        [ReadOnly] public float Friction;
        [ReadOnly] public float Elasticity;
        [ReadOnly] public float cellRadius; //不同于粒子格子尺寸
        [ReadOnly] public int   bucketCapacityMask;

#if UNITY_EDITOR
        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<PBDCollisionHit> DebugArray;
#endif

        public void Execute(int start, int count)
        {
//            if (RigidBodyHashes.Length < 1 || Colliders.Length < 1)
//                return;

            float qRadius = QuadRadius;
            var   scence  = SceneBounds[0];
            var   pBounds = new PBDBounds();

            QuadCollisionHitInfos quadHitInfos = new QuadCollisionHitInfos();


            int MAX_STACK_DEPTH = math.min(Constants.MAX_STACK_ALLOC, Colliders.Length);

//            var hashSet = new NativeHashSet<int>(Colliders.Length, Allocator.Temp);
            var hashSet = stackalloc ushort[MAX_STACK_DEPTH];
            int counter = 0;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            bool Add(ushort rbIndex)
            {
                for (int i = 0; i < counter; i++)
                    if (hashSet[i] == rbIndex)
                        return false;

                hashSet[counter++] = rbIndex;
                return true;
            }


            for (int index = start; index < start + count; index++)
            {
                int quadID = UpdateList[index];

                float3 quadPos = QuadPredictedPositions[quadID].Value;

                pBounds.Min = quadPos - qRadius;
                pBounds.Max = quadPos + qRadius;

                if (!MathematicsUtil.AABBOverlap(in scence, in pBounds))
                    continue;

                float pRadius = Radius[quadID].Value;

                int4 indices = math.mad(quadID, 4, new int4(0, 1, 2, 3));

                quadHitInfos.Clear();

                int3 cellPos = HashUtility.PosToGrid(quadPos, cellRadius);
                for (int offset = 0; offset < neighborOffsets.Length; offset++)
                {
                    int3 offsetPos = cellPos + neighborOffsets[offset];
                    int  hash      = HashUtility.GridToHash(offsetPos, bucketCapacityMask);

                    if (RigidBodyHashRanges.TryGetValue(hash, out var cell))
                    {
                        for (int j = cell.Start; j <= cell.End; j++)
                        {
                            var rigi      = RigidBodyHashes[j];
                            int rigiIndex = rigi.Index;

                            if (!Add((ushort)rigiIndex))
                                continue;
//                            if (!hashSet.Add(rigiIndex))
//                                continue;

                            var c = Colliders[rigiIndex];

                            PBDBounds rbBounds = c.Bounds;

                            if (!MathematicsUtil.AABBOverlap(in rbBounds, in pBounds))
                                continue;

                            for (int i = 0; i < 4; i++)
                            {
                                int pIndex = indices[i];
                                float3 position = PredictedPositions[pIndex].Value,
                                       velocity = Velocities[pIndex].Value;

                                PBDCollisionHit hit = quadHitInfos.GetHit(i);

                                if (c.Collide(in position, pRadius, Elasticity, Friction, ref velocity, ref hit))
                                {
                                    quadHitInfos.SetHit(i, hit);
                                }
                            }
                        }
                    }
                }

                if (counter == 0)
                    return;
//                if(hashSet.IsEmpty )
//                    continue;

                for (int i = 0; i < 4; i++)
                {
                    int pIndex   = indices[i];
                    var hit      = quadHitInfos.GetHit(i);
                    int hitCount = hit.hitCount;
                    if (hitCount > 0)
                    {
                        RigiCollisionConstraints[pIndex] = new RigiCollisionConstraint()
                        {
                            Delta    = hit.hitDelta,
                            Velocity = hit.hitConcatDelta,
                            // Normal = hit.hitNormal,
                            // InsertDepth = hit.insertDepth,
                            ConstraintsCount = hitCount,
                        };
                    }
#if UNITY_EDITOR
                    DebugArray[pIndex] = hit;
#endif
                }

//                hashSet.Clear();
                counter = 0;
            }

//            hashSet.Dispose();
        }
    }


    [BurstCompile]
    public struct UpdateSenceBounds : IJob
    {
        [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
        public NativeArray<PBDCustomColliderInfo>.ReadOnly Colliders;

        [WriteOnly, NativeDisableParallelForRestriction, NativeFixedLength(1)]
        public NativeArray<PBDBounds> SceneBounds;

        public void Execute()
        {
//            if (Colliders.Length < 1)
//                return;

            float3 min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < Colliders.Length; i++)
            {
                var c = Colliders[i];
                min = math.min(min, c.boundsMin);
                max = math.max(max, c.boundsMax);
            }

            SceneBounds[0] = new PBDBounds()
            {
                Min = min,
                Max = max,
            };
        }
    }

    [BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
    public struct ReCaculateQuadPosition : IJobParallelForBatch
    {
        [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
        public NativeArray<int>.ReadOnly UpdateList;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PredictedPositions>.ReadOnly PredictedPositions;

        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<QuadPredictedPositions> QuadPredictedPositions;
        
        [ReadOnly]
        private static readonly float4 weightMatrix = new float4(
            0.25f, 0.25f, 0.25f, 0.25f
        );

        public unsafe void Execute(int start, int count)
        {
            var size = UnsafeUtility.SizeOf<PredictedPositions>() * 4;
            
            var updatePtr = (int*)UpdateList.GetUnsafeReadOnlyPtr();
            var source    = (PredictedPositions*)PredictedPositions.GetUnsafeReadOnlyPtr();
            var dest      = (QuadPredictedPositions*)QuadPredictedPositions.GetUnsafePtr();
            
            for (int index = start; index < start + count; index++)
            {
                int quadID = updatePtr[index];

                float3x4 positions;
                
                UnsafeUtility.MemCpy(&positions, source + quadID * 4, size);

                float3 pos = math.mul(positions, weightMatrix);
                
//                dest[quadID]       = new QuadPredictedPositions() { Value = pos };
                dest[quadID].Value = pos;
            }
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    public unsafe struct OctreeRigidbodyCollisionJob : IJobParallelForBatch
    {
        [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
        public NativeArray<int>.ReadOnly UpdateList;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PBDCustomColliderInfo>.ReadOnly Colliders;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PredictedPositions>.ReadOnly PredictedPositions;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<Radius>.ReadOnly Radius;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<Velocity>.ReadOnly Velocities;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<QuadPredictedPositions>.ReadOnly QuadPredictedPositions;

        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<RigiCollisionConstraint> RigiCollisionConstraints;

        [ReadOnly, NativeDisableParallelForRestriction, NativeFixedLength(1)]
        public NativeArray<PBDBounds>.ReadOnly SceneBounds;

        [ReadOnly, NativeDisableParallelForRestriction]
        public RigidbodyOctree RigidbodyOctree;


        [ReadOnly] public float QuadRadius;
        [ReadOnly] public float Friction;
        [ReadOnly] public float Elasticity;

#if UNITY_EDITOR
        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<PBDCollisionHit> DebugArray;
#endif

        public void Execute(int start, int count)
        {
//            if (RigidbodyOctree.NodeLength < 1 || RigidbodyOctree.Nodes[0].IsEmpty)
//                return;

            int MAX_STACK_DEPTH = math.min(Constants.MAX_STACK_ALLOC, RigidbodyOctree.NodeLength);
            
            ushort* stack           = stackalloc ushort[MAX_STACK_DEPTH];
            int     stackPtr        = 0;

//            var stack = new NativeQueue<int>(Allocator.Temp);

            QuadCollisionHitInfos quadHitInfos = new QuadCollisionHitInfos();

            var   scence     = SceneBounds[0];
            float qRadius    = QuadRadius;
            int   nodeLength = RigidbodyOctree.NodeLength;
            var   pBounds    = new PBDBounds();

            for (int index = start; index < start + count; index++)
            {
                int    quadID  = UpdateList[index];
                float3 quadPos = QuadPredictedPositions[quadID].Value;

                pBounds.Min = quadPos - qRadius;
                pBounds.Max = quadPos + qRadius;

                if (!MathematicsUtil.AABBOverlap(in scence, in pBounds))
                    continue;

                float pRadius = Radius[quadID].Value;
                int4  indices = math.mad(quadID, 4, new int4(0, 1, 2, 3));

//                stack.Enqueue(0);
                stack[stackPtr++] = 0;

                quadHitInfos.Clear();

//                while (stack.TryDequeue(out var nodeIndex))
                while (stackPtr > 0)
                {
                    stackPtr--;
                    var nodeIndex = stack[stackPtr];
                    var node = RigidbodyOctree.Nodes[nodeIndex];

                    for (int i = 0; i < node.RigidbodyCount; i++)
                    {
                        var rbIndex = RigidbodyOctree.RigidbodyIndices[node.RigidbodyStart + i];

                        var c = Colliders[rbIndex];

                        PBDBounds rbBounds = c.Bounds;

                        if (!MathematicsUtil.AABBOverlap(in rbBounds, in pBounds))
                            continue;

                        for (int j = 0; j < 4; j++)
                        {
                            int pIndex = indices[j];

                            float3
                                position = PredictedPositions[pIndex].Value,
                                // velocity = float3.zero;
                                velocity = Velocities[pIndex].Value;

                            PBDCollisionHit hit = quadHitInfos.GetHit(j);

                            if (c.Collide(in position, pRadius, Elasticity, Friction, ref velocity, ref hit))
                            {
                                quadHitInfos.SetHit(j, hit);
                            }
                        }
                    }

                    if (!node.IsLeaf)
                    {
                        int firstChild = node.FirstChild;
                        for (int i = 0; i < 8; i++)
                        {
                            int childIndex = firstChild + i;
                            if (childIndex >= nodeLength) continue;

                            var childNode = RigidbodyOctree.Nodes[childIndex];
                            if (childNode.IsEmpty) continue;

                            if (MathematicsUtil.AABBOverlap(childNode.Bounds, pBounds))
//                                stack.Enqueue((ushort)childIndex);
                                if(stackPtr < MAX_STACK_DEPTH)
                                    stack[stackPtr++] = (ushort)childIndex;
                        }
                    }
                }

                // to unsafe MemCpy
                for (int i = 0; i < 4; i++)
                {
                    int pIndex   = indices[i];
                    var hit      = quadHitInfos.GetHit(i);
                    int hitCount = hit.hitCount;
                    if (hitCount > 0)
                    {
                        RigiCollisionConstraints[pIndex] = new RigiCollisionConstraint()
                        {
                            Delta    = hit.hitDelta,
                            Velocity = hit.hitConcatDelta,
                            // Normal = hit.hitNormal,
                            // InsertDepth = hit.insertDepth,
                            ConstraintsCount = hitCount,
                        };
                    }
#if UNITY_EDITOR
                    DebugArray[pIndex] = hit;
#endif
                }
            }

//            stack.Dispose();
        }
    }

    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    public struct RigibodyCollisionJob : IJobParallelForBatch
    {
        [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
        public NativeArray<int>.ReadOnly UpdateList;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PBDCustomColliderInfo>.ReadOnly Colliders;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PredictedPositions>.ReadOnly PredictedPositions;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<Radius>.ReadOnly Radius;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<Velocity>.ReadOnly Velocities;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<QuadPredictedPositions>.ReadOnly QuadPredictedPositions;

        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<RigiCollisionConstraint> RigiCollisionConstraints;

        [ReadOnly, NativeDisableParallelForRestriction, NativeFixedLength(1)]
        public NativeArray<PBDBounds>.ReadOnly SceneBounds;

        [ReadOnly] public float QuadRadius;
        [ReadOnly] public float Friction;
        [ReadOnly] public float Elasticity;

#if UNITY_EDITOR
        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<PBDCollisionHit> DebugArray;
#endif

        public void Execute(int start, int count)
        {
            float qRadius = QuadRadius;

            PBDBounds scence = SceneBounds[0];

            QuadCollisionHitInfos quadHitInfos = new QuadCollisionHitInfos();

            for (int index = start; index < start + count; index++)
            {
                int quadID = UpdateList[index];

                float3 quadPos = QuadPredictedPositions[quadID].Value;

                if (!MathematicsUtil.AABBOverlap(in quadPos, in qRadius, in scence.Min, in scence.Max))
                    continue;

                int4  indices = math.mad(quadID, 4, new int4(0, 1, 2, 3));
                float pRadius = Radius[quadID].Value;

                quadHitInfos.Clear();

                //没考虑质量
                for (int j = 0; j < Colliders.Length; j++)
                {
                    var c = Colliders[j];
                    if (!MathematicsUtil.AABBOverlap(in quadPos, in qRadius, in c.boundsMin, in c.boundsMax))
                        continue;
                    for (int i = 0; i < 4; i++)
                    {
                        int pIndex = indices[i];
                        var hit    = quadHitInfos.GetHit(i);

                        float3
                            position = PredictedPositions[pIndex].Value,
                            velocity = Velocities[pIndex].Value;
                        // velocity = float3.zero;
                        if (c.Collide(in position, pRadius, Elasticity, Friction, ref velocity, ref hit))
                        {
                            quadHitInfos.SetHit(i, hit);
                        }
                    }
                }

                for (int i = 0; i < 4; i++)
                {
                    int pIndex   = indices[i];
                    var hit      = quadHitInfos.GetHit(i);
                    int hitCount = hit.hitCount;
                    if (hitCount > 0)
                    {
                        RigiCollisionConstraints[pIndex] = new RigiCollisionConstraint()
                        {
                            Delta    = hit.hitDelta,
                            Velocity = hit.hitConcatDelta,
                            // Normal = hit.hitNormal,
                            // InsertDepth = hit.insertDepth,
                            ConstraintsCount = hitCount,
                        };
                    }
#if UNITY_EDITOR
                    DebugArray[pIndex] = hit;
#endif
                }
            }

        }
    }

#endregion

    
    
#region Clear
    
    
    [BurstCompile]
    public struct ClearList<T> : IJob where T : unmanaged
    {
        [WriteOnly] public NativeList<T> List;

        public void Execute()
        {
            List.Clear();
        }
    }
    

    [BurstCompile]
    public struct ClearHashNeighbours : IJob
    {
        [WriteOnly] public NativeParallelMultiHashMap<int, int> hashMap;
        public void Execute()
        {
            hashMap.Clear();
        }
    }

    [BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
    public struct ClearConstraintsArray<T> : IJobParallelForBatch where T : unmanaged
    {
        [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
        public NativeArray<int>.ReadOnly UpdateList;

        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<T> Constraints;

        public unsafe void Execute(int start, int count)
        {
            var clearCnstr = default(T);
            var stride     = UnsafeUtility.SizeOf<T>();

            var updatePtr = (int*)UpdateList.GetUnsafeReadOnlyPtr();
            var destPtr   = (T*)Constraints.GetUnsafePtr();

            for (int index = start; index < start + count; index++)
            {
                int offset = updatePtr[index] * 4;

                UnsafeUtility.MemCpyReplicate(destPtr + offset, &clearCnstr, stride, 4);
            }
        }
    }


    [BurstCompile]
    public struct ClearHashMapJob<T> : IJob where T : unmanaged
    {
        [WriteOnly] public UnsafeParallelHashMap<PrecomputedHashKey, T> hashRanges;
        
        public void Execute()
        {
            hashRanges.Clear();
        }
    }
    
    [BurstCompile]
    public struct ClearSimpleHashMapJob<T> : IJob where T : unmanaged
    {
        [WriteOnly, NativeDisableUnsafePtrRestriction]
        public SimpleHashArray<T> hashRanges;
        
        public void Execute()
        {
            hashRanges.Clear();
        }
    }
    
#endregion


    //多线程标记在视锥内
    [BurstCompile(OptimizeFor = OptimizeFor.Performance, FloatMode = FloatMode.Fast, 
                  FloatPrecision = FloatPrecision.Low, CompileSynchronously = true,
                  /*Debug = true,*/
                  DisableSafetyChecks = true)]
    public struct CaculateInFrustumJob : IJobParallelForBatch
    {
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PredictedPositions>.ReadOnly PredictedPositions;

        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<IsNeedRender> IsNeedRenders;

        [ReadOnly] public float4x4 CullingMatrix;
        
        public unsafe void Execute(int start, int count)
        {
            var (currentStart, remaining) = (start, count);
            
            var tempResults  = stackalloc bool[Constants.MAX_STACK_ALLOC];
            var renderPtr    = (IsNeedRender*)IsNeedRenders.GetUnsafePtr();
            var stride       = UnsafeUtility.SizeOf<IsNeedRender>();
            
            while (remaining > 0)
            {
                int batchSize   = math.min(remaining, Constants.MAX_STACK_ALLOC);

                for (int i = 0; i < batchSize; i++)
                {
                    int  index   = currentStart + i;
                    int4 indices = math.mad(index, 4, new int4(0, 1, 2, 3));
                    
                    bool needRender = false;
                    for (int j = 0; j < 4; j++)
                    {
                        if (MathematicsUtil.InFrustum(in CullingMatrix, PredictedPositions[indices[j]].Value))
                        {
                            needRender = true;
                            break;
                        }
                    }

                    tempResults[i] = needRender;
                }

                UnsafeUtility.MemCpy(renderPtr + currentStart, tempResults, stride * batchSize);
                remaining    -= batchSize;
                currentStart += batchSize;
            }
        }
    }
    
    [BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
    public struct InFrustumFilterJob : IJobFilterRange
    {
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<IsNeedRender>.ReadOnly IsNeedRenders;
        
        public bool Execute(int index)
        {
            return IsNeedRenders[index].Value;
        }
    }
    
    [BurstCompile]
    public struct UpdateQuadFilter : IJobFilterRange
    {
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<IsNeedUpdate>.ReadOnly IsNeedUpdates;

        public bool Execute(int index)
        {
            return IsNeedUpdates[index].Value;
        }
    }
    
    [BurstCompile]
    public struct StaticQuadFilter : IJobFilterRange
    {
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<IsNeedUpdate>.ReadOnly IsNeedUpdates;

        public bool Execute(int index)
        {
            return !IsNeedUpdates[index].Value;
        }
    }
}