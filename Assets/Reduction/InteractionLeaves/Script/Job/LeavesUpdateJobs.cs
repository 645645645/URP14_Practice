using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;

namespace UnityEngine.PBD
{
    [GenerateTestsForBurstCompatibility]
    public struct LeavesUpdateJobs
    {
        [BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
        public struct FillTrianglesJobUShort : IJobParallelForBatch
        {
            //常驻 只生成一次
            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<ushort> triangles;
        
            public void Execute(int start, int count)
            {
                for (int index = start; index < start + count; index++)
                {
                    int4 indices   = math.mad(index, 4, new int4(0, 1, 2, 3));
                    int3 traingesA = math.mad(index, 6, new int3(0, 1, 2));
                    int3 traingesB = math.mad(index, 6, new int3(3, 4, 5));

                    triangles[traingesA.x] = (ushort)indices[0];
                    triangles[traingesA.y] = (ushort)indices[2];
                    triangles[traingesA.z] = (ushort)indices[1];
                    triangles[traingesB.x] = (ushort)indices[1];
                    triangles[traingesB.y] = (ushort)indices[2];
                    triangles[traingesB.z] = (ushort)indices[3];
                }
            }
        }

        [BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
        public struct FillTrianglesJobUInt : IJobParallelForBatch
        {
            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<uint> triangles;
        
            public void Execute(int start, int count)
            {
                for (int index = start; index < start + count; index++)
                {
                    int4 indices   = math.mad(index, 4, new int4(0, 1, 2, 3));
                    int3 traingesA = math.mad(index, 6, new int3(0, 1, 2));
                    int3 traingesB = math.mad(index, 6, new int3(3, 4, 5));

                    triangles[traingesA.x] = (uint)indices[0];
                    triangles[traingesA.y] = (uint)indices[2];
                    triangles[traingesA.z] = (uint)indices[1];
                    triangles[traingesB.x] = (uint)indices[1];
                    triangles[traingesB.y] = (uint)indices[2];
                    triangles[traingesB.z] = (uint)indices[3];
                }
            }
        }

        [BurstCompile(
            OptimizeFor = OptimizeFor.Performance,
            FloatMode = FloatMode.Fast,          CompileSynchronously = true,
            FloatPrecision = FloatPrecision.Low, DisableSafetyChecks = true)]
        public struct CreatQuadMeshDataUNorm16Job : IJobParallelForBatch
        {
            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<DistanceConstraint> distanceConstraints;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<BendConstraint> bendConstraints;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<PredictedPositions> PredictedPositions;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<Velocity> Velocities;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<Normal> Normals;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<Radius> Radius;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<InvMass> InvMasses;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<QuadInvMass> QuadInvMasses;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<Area> Areas;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<IsNeedUpdate> IsNeedUpdates;

            //mesh ===============
            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<float3> vertices;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<UNorm16x2> normal;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<UNorm16x2> uvs;
            //===================

            [ReadOnly, NativeDisableParallelForRestriction]
            public NativeArray<float4>.ReadOnly skinParams;

            [ReadOnly, NativeDisableParallelForRestriction]
            public NativeArray<float3>.ReadOnly productMinMax;

            [ReadOnly] public float4x4 local2World;

            [ReadOnly] public int offset; //生成

            [ReadOnly] public float Radius2Rigibody;

            public void Execute(int startIndex, int count)
            {
                for (int index = startIndex; index < startIndex + count; index++)
                {
                    int quadID = index + offset;
                    quadID %= IsNeedUpdates.Length;

                    int4 indices = math.mad(quadID, 4, new int4(0, 1, 2, 3));

                    var random = Unity.Mathematics.Random.CreateFromIndex((uint)indices[0]);

                    int    type = random.NextInt(0, skinParams.Length);
                    float4 uvST = skinParams[type];

                    float3 pos = random.NextFloat3(productMinMax[0], productMinMax[1]);

                    // quaternion rot = quaternion.Euler(pos * 30);
                    quaternion rot = random.NextQuaternionRotation();

                    float3 foldAngleRange = productMinMax[2];
                    float2 scaleRange     = productMinMax[3].xy;
                    float2 massRange      = productMinMax[4].xy;
                    float  scale          = random.NextFloat(scaleRange.x,     scaleRange.y);
                    float  angle          = random.NextFloat(foldAngleRange.x, foldAngleRange.y);
                    float  mass           = random.NextFloat(massRange.x,      massRange.y);
                    float  radius         = random.NextFloat(scaleRange.x,     scaleRange.y);
                    radius *= (0.25f * Radius2Rigibody);

                    var quadPos = CaculateQuadPos(local2World, pos, rot, scale, angle);

                    float3
                        a = (quadPos.c0 - quadPos.c1),
                        b = (quadPos.c2 - quadPos.c1),
                        c = (quadPos.c3 - quadPos.c1),
                        d = (quadPos.c0 - quadPos.c2),
                        e = (quadPos.c3 - quadPos.c2);

                    CaculateNormalAndArea(a, b, c, out float3 na, out float3 nb, out float3 np, out var area);

                    CreatDistanceConstraint(ref distanceConstraints, quadID, a, b, c, d, e);
                    CreatBendConstraint(ref bendConstraints, quadID, na, nb);

                    UpdateMeshPos(ref vertices, indices, quadPos);
                    UpdateMeshNormal(indices, na, nb, np);
                    UpdateMeshUv(indices, uvST);

                    UpdateSimulationData(ref QuadInvMasses, ref Radius,  ref IsNeedUpdates, ref PredictedPositions,
                                         ref Velocities,    ref Normals, ref InvMasses,     ref Areas,
                                         quadID,            indices,     quadPos,
                                         na,                nb,          area, radius, mass, type);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void UpdateMeshNormal(in int4 indices, in float3 na, in float3 nb, in float3 np)
            {
                UNorm16x2
                    octNA = new(MathematicsUtil.UnitVectorToOctahedron(na), true),
                    octNB = new(MathematicsUtil.UnitVectorToOctahedron(nb), true),
                    octNP = new(MathematicsUtil.UnitVectorToOctahedron(np), true);

                normal[indices[0]] = octNA;
                normal[indices[1]] = octNP;
                normal[indices[2]] = octNP;
                normal[indices[3]] = octNB;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void UpdateMeshUv(in int4 indices, in float4 uvST)
            {
                uvs[indices[0]] = new UNorm16x2(math.mad(new float2(0, 0), uvST.xy, uvST.zw));
                uvs[indices[1]] = new UNorm16x2(math.mad(new float2(1, 0), uvST.xy, uvST.zw));
                uvs[indices[2]] = new UNorm16x2(math.mad(new float2(0, 1), uvST.xy, uvST.zw));
                uvs[indices[3]] = new UNorm16x2(math.mad(new float2(1, 1), uvST.xy, uvST.zw));
            }
        }


        [BurstCompile(
            OptimizeFor = OptimizeFor.Performance,
            FloatMode = FloatMode.Fast,          CompileSynchronously = true,
            FloatPrecision = FloatPrecision.Low, DisableSafetyChecks = true)]
        public struct CreatQuadMeshDataUNorm8Job : IJobParallelForBatch
        {
            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<DistanceConstraint> distanceConstraints;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<BendConstraint> bendConstraints;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<PredictedPositions> PredictedPositions;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<Velocity> Velocities;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<Normal> Normals;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<Radius> Radius;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<InvMass> InvMasses;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<QuadInvMass> QuadInvMasses;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<Area> Areas;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<IsNeedUpdate> IsNeedUpdates;

            //mesh ===============
            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<float3> vertices;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<UNorm8x4> uvAndNormal;
            //===================

            [ReadOnly, NativeDisableParallelForRestriction]
            public NativeArray<float4>.ReadOnly skinParams;

            [ReadOnly, NativeDisableParallelForRestriction]
            public NativeArray<float3>.ReadOnly productMinMax;

            [ReadOnly] public float4x4 local2World;

            [ReadOnly] public int offset; //生成

            [ReadOnly] public float Radius2Rigibody;

            public void Execute(int startIndex, int count)
            {
                for (int index = startIndex; index < startIndex + count; index++)
                {
                    int quadID = index + offset;
                    quadID %= IsNeedUpdates.Length;

                    int4 indices = math.mad(quadID, 4, new int4(0, 1, 2, 3));

                    var random = Unity.Mathematics.Random.CreateFromIndex((uint)indices[0]);

                    int    type = random.NextInt(0, skinParams.Length);
                    float4 uvST = skinParams[type];

                    float3 pos = random.NextFloat3(productMinMax[0], productMinMax[1]);

                    // quaternion rot = quaternion.Euler(pos * 30);
                    quaternion rot = random.NextQuaternionRotation();

                    float3 foldAngleRange = productMinMax[2];
                    float2 scaleRange     = productMinMax[3].xy;
                    float2 massRange      = productMinMax[4].xy;
                    float  scale          = random.NextFloat(scaleRange.x,     scaleRange.y);
                    float  angle          = random.NextFloat(foldAngleRange.x, foldAngleRange.y);
                    float  mass           = random.NextFloat(massRange.x,      massRange.y);
                    float  radius         = random.NextFloat(scaleRange.x,     scaleRange.y);
                    radius *= (0.25f * Radius2Rigibody);

                    var quadPos = CaculateQuadPos(local2World, pos, rot, scale, angle);

                    float3
                        a = (quadPos.c0 - quadPos.c1),
                        b = (quadPos.c2 - quadPos.c1),
                        c = (quadPos.c3 - quadPos.c1),
                        d = (quadPos.c0 - quadPos.c2),
                        e = (quadPos.c3 - quadPos.c2);

                    CaculateNormalAndArea(a, b, c, out float3 na, out float3 nb, out float3 np, out var area);

                    CreatDistanceConstraint(ref distanceConstraints, quadID, a, b, c, d, e);
                    CreatBendConstraint(ref bendConstraints, quadID, na, nb);

                    UpdateMeshPos(ref vertices, indices, quadPos);
                    UpdateMeshUvAndNormal(indices, na, nb, np, uvST);

                    UpdateSimulationData(ref QuadInvMasses, ref Radius,  ref IsNeedUpdates, ref PredictedPositions,
                                         ref Velocities,    ref Normals, ref InvMasses,     ref Areas,
                                         quadID,            indices,     quadPos,
                                         na,                nb,          area, radius, mass, type);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void UpdateMeshUvAndNormal(in int4 indices, in float3 na, in float3 nb, in float3 np, in float4 uvST)
            {
                float2
                    octNA = MathematicsUtil.UnitVectorToOctahedron(na),
                    octNB = MathematicsUtil.UnitVectorToOctahedron(nb),
                    octNP = MathematicsUtil.UnitVectorToOctahedron(np);

                UNorm8x4
                    nv0 = new(octNA, math.mad(new float2(0, 0), uvST.xy, uvST.zw)),
                    nv1 = new(octNP, math.mad(new float2(1, 0), uvST.xy, uvST.zw)),
                    nv2 = new(octNP, math.mad(new float2(0, 1), uvST.xy, uvST.zw)),
                    nv3 = new(octNB, math.mad(new float2(1, 1), uvST.xy, uvST.zw));

                uvAndNormal[indices[0]] = nv0;
                uvAndNormal[indices[1]] = nv1;
                uvAndNormal[indices[2]] = nv2;
                uvAndNormal[indices[3]] = nv3;
            }
        }

#region LastUpdateData

        [BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
        public struct UpdateVelocityJob : IJobParallelForBatch
        {
            [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
            public NativeArray<int>.ReadOnly UpdateList;

            [ReadOnly, NativeDisableParallelForRestriction]
            public NativeArray<PredictedPositions>.ReadOnly PredictedPositions;

            [ReadOnly, NativeDisableParallelForRestriction]
            public NativeArray<Position>.ReadOnly Positions;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<Velocity> Velocities;

            [ReadOnly] public float InvDeltaTime;

            public unsafe void Execute(int start, int count)
            {
                var size = UnsafeUtility.SizeOf<float3x4>();
                
                var updatePtr    = (int*)UpdateList.GetUnsafeReadOnlyPtr();
                var predictedPtr = (PredictedPositions*)PredictedPositions.GetUnsafeReadOnlyPtr();
                var positionPtr  = (Position*)Positions.GetUnsafeReadOnlyPtr();
                var dest         = (Velocity*)Velocities.GetUnsafePtr();
                
                for (int index = start; index < start + count; index++)
                {
                    int quadID = updatePtr[index];

                    int4 indices = math.mad(quadID, 4, new int4(0, 1, 2, 3));

                    float3x4 predictedPositions = new float3x4(
                        predictedPtr[indices.x].Value,
                        predictedPtr[indices.y].Value,
                        predictedPtr[indices.z].Value,
                        predictedPtr[indices.w].Value
                    );

                    float3x4 oldPositions = new float3x4(
                        positionPtr[indices.x].Value,
                        positionPtr[indices.y].Value,
                        positionPtr[indices.z].Value,
                        positionPtr[indices.w].Value
                    );

                    float3x4 velocities = (predictedPositions - oldPositions) * InvDeltaTime;
                    
                    UnsafeUtility.MemCpy(dest + indices[0], &velocities, size);
                }
            }
        }


        [BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
        public struct UpdateNormalAreaJob : IJobParallelForBatch
        {
            [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
            public NativeArray<int>.ReadOnly UpdateList;

            [ReadOnly, NativeDisableParallelForRestriction]
            public NativeArray<PredictedPositions>.ReadOnly PredictedPositions;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<Normal> Normals;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<Area> Areas;
        
            public unsafe void Execute(int startIndex, int count)
            {
                var sizeArea   = UnsafeUtility.SizeOf<Area>()   * 4;
                var sizeNormal = UnsafeUtility.SizeOf<Normal>() * 4;
                
                var updatePtr  = (int*)UpdateList.GetUnsafeReadOnlyPtr();
                var source     = (PredictedPositions*)PredictedPositions.GetUnsafeReadOnlyPtr();
                var destArea   = (Area*)Areas.GetUnsafePtr();
                var destNormal = (Normal*)Normals.GetUnsafePtr();
                var tempArea   = stackalloc Area[4];
                var tempNormal = stackalloc Normal[4];
                
                for (int index = startIndex; index < startIndex + count; index++)
                {
                    int quadID = updatePtr[index];

                    int4 indices = math.mad(quadID, 4, new int4(0, 1, 2, 3));

                    float3
                        p0 = source[indices.x].Value,
                        p1 = source[indices.y].Value,
                        p2 = source[indices.z].Value,
                        p3 = source[indices.w].Value;

                    float3
                        a = (p0 - p1),
                        b = (p2 - p1),
                        c = (p3 - p1);

                    CaculateNormalAndArea(a, b, c, out var na, out var nb, out var area);

                    tempArea[0] = new Area(){ Value = area.x };
                    tempArea[1] = new Area(){ Value = area.z };
                    tempArea[2] = new Area(){ Value = area.z };
                    tempArea[3] = new Area(){ Value = area.y };

                    tempNormal[0] = new Normal() { Value = na };
                    tempNormal[1] = new Normal() { Value = na };
                    tempNormal[2] = new Normal() { Value = nb };
                    tempNormal[3] = new Normal() { Value = nb };
                    
                    UnsafeUtility.MemCpy(destArea   + indices[0], tempArea,   sizeArea);
                    UnsafeUtility.MemCpy(destNormal + indices[0], tempNormal, sizeNormal);
                }
            }
        }

        [BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
        public struct UpdateMeshPosJob : IJobParallelForBatch
        {
            [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
            public NativeArray<int>.ReadOnly UpdateList;

            [ReadOnly, NativeDisableParallelForRestriction]
            public NativeArray<PredictedPositions>.ReadOnly PredictedPositions;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<float3> pos;
            
            public unsafe void Execute(int startIndex, int count)
            {
                var size   = UnsafeUtility.SizeOf<PredictedPositions>() * 4;

                var updatePtr = (int*)UpdateList.GetUnsafeReadOnlyPtr();
                var source    = (PredictedPositions*)PredictedPositions.GetUnsafeReadOnlyPtr();
                var dest      = (float3*)pos.GetUnsafePtr();

                for (int index = startIndex; index < startIndex + count; index++)
                {
                    int offset = updatePtr[index] * 4;

                    UnsafeUtility.MemCpy(dest + offset, source + offset, size);
                }
            }
        }

        [BurstCompile(
            OptimizeFor = OptimizeFor.Performance,
            FloatMode = FloatMode.Fast,          CompileSynchronously = true,
            FloatPrecision = FloatPrecision.Low, DisableSafetyChecks = true)]
        public struct UpdateMeshNormalUNorm16Job : IJobParallelForBatch
        {
            [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
            public NativeArray<int>.ReadOnly UpdateList;

            [ReadOnly, NativeDisableParallelForRestriction]
            public NativeArray<Normal>.ReadOnly Normals;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<UNorm16x2> normal;
        
            public unsafe void Execute(int startIndex, int count)
            {
                var size   = UnsafeUtility.SizeOf<UNorm16x2>() * 4;

                var updatePtr = (int*)UpdateList.GetUnsafeReadOnlyPtr();
                var source    = (Normal*)Normals.GetUnsafeReadOnlyPtr();
                var dest      = (UNorm16x2*)normal.GetUnsafePtr();
                var tempArray = stackalloc UNorm16x2[4];
                
                for (int index = startIndex; index < startIndex + count; index++)
                {
                    int quadID = updatePtr[index];

                    int4 indices = math.mad(quadID, 4, new int4(0, 1, 2, 3));

                    float3
                        na = source[indices[0]].Value,
                        nb = source[indices[3]].Value;

                    float3 np = math.normalize(na + nb);

                    UNorm16x2
                        octNA = new(MathematicsUtil.UnitVectorToOctahedron(na), true),
                        octNB = new(MathematicsUtil.UnitVectorToOctahedron(nb), true),
                        octNP = new(MathematicsUtil.UnitVectorToOctahedron(np), true);

                    tempArray[0] = octNA;
                    tempArray[1] = octNP;
                    tempArray[2] = octNP;
                    tempArray[3] = octNB;

                    UnsafeUtility.MemCpy(dest + indices[0], tempArray, size);
                }
            }
        }


        [BurstCompile(
            OptimizeFor = OptimizeFor.Performance,
            FloatMode = FloatMode.Fast,          CompileSynchronously = true,
            FloatPrecision = FloatPrecision.Low, DisableSafetyChecks = true)]
        public struct UpdateMeshNormalUNorm8Job : IJobParallelForBatch
        {
            [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
            public NativeArray<int>.ReadOnly UpdateList;

            [ReadOnly, NativeDisableParallelForRestriction]
            public NativeArray<Normal>.ReadOnly Normals;

            [NativeDisableParallelForRestriction] public NativeArray<UNorm8x4> uvAndNormal;
        
            public unsafe void Execute(int startIndex, int count)
            {
                var size = UnsafeUtility.SizeOf<UNorm8x4>() * 4;

                var updatePtr = (int*)UpdateList.GetUnsafeReadOnlyPtr();
                var source    = (Normal*)Normals.GetUnsafeReadOnlyPtr();
                var dest      = (UNorm8x4*)uvAndNormal.GetUnsafePtr();
                var tempArray = stackalloc UNorm8x4[4];
                
                for (int index = startIndex; index < startIndex + count; index++)
                {
                    int quadID = updatePtr[index];

                    int4 indices = math.mad(quadID, 4, new int4(0, 1, 2, 3));

                    float3
                        na = source[indices[0]].Value,
                        nb = source[indices[3]].Value;

                    float3 np = math.normalize(na + nb);

                    UNorm8x4
                        nv0 = dest[indices[0]],
                        nv1 = dest[indices[1]],
                        nv2 = dest[indices[2]],
                        nv3 = dest[indices[3]];

                    float2
                        octNA = MathematicsUtil.UnitVectorToOctahedron(na),
                        octNB = MathematicsUtil.UnitVectorToOctahedron(nb),
                        octNP = MathematicsUtil.UnitVectorToOctahedron(np);
                    
                    tempArray[0] = new UNorm8x4(octNA, nv0.z, nv0.w);
                    tempArray[1] = new UNorm8x4(octNP, nv1.z, nv1.w);
                    tempArray[2] = new UNorm8x4(octNP, nv2.z, nv2.w);
                    tempArray[3] = new UNorm8x4(octNB, nv3.z, nv3.w);
                    
                    UnsafeUtility.MemCpy(dest + indices[0], tempArray, size);
                }
            }
        }


        [BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
        public struct UpdateFrustumMeshPosJob : IJobParallelForBatch
        {
            [ReadOnly, NativeDisableParallelForRestriction]
            public NativeArray<int> RenderList;

            [ReadOnly, NativeDisableParallelForRestriction]
            public NativeArray<PredictedPositions>.ReadOnly PredictedPositions;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<float3> pos;

            [ReadOnly] public int JobNum;

            public unsafe void Execute(int startIndex, int count)
            {
                int length = RenderList.Length;
//                if (length < 1 || JobNum < 1)
//                    return;

                if (!(length >= 1 && JobNum >= 1))
                    return;

                int actualJobNum = math.min(JobNum, length);

                int jobLength = (length + JobNum - 1) / JobNum;
                
                var size   = UnsafeUtility.SizeOf<PredictedPositions>() * 4;

                var renderPtr = (int*)RenderList.GetUnsafeReadOnlyPtr();
                var source    = (PredictedPositions*)PredictedPositions.GetUnsafeReadOnlyPtr();
                var dest      = (float3*)pos.GetUnsafePtr();

                int endIndedx   = math.min(startIndex + count, actualJobNum);
                
                var tempResults = stackalloc PredictedPositions[Constants.MAX_STACK_ALLOC * 4];
                
                for (int index = startIndex; index < endIndedx; index++)
                {
                    int start = jobLength * index,
                        end   = math.min(math.mad(jobLength, index, jobLength), length);
                    
                    var (currentStart, remaining) = (start, end - start);

                    while (remaining > 0)
                    {
                        int batchSize   = math.min(remaining, Constants.MAX_STACK_ALLOC);
                        
                        for (int i = 0; i < batchSize; i++)
                        {
                            int destIndex   = currentStart + i;
                            int sourceIndex = renderPtr[destIndex];

                            UnsafeUtility.MemCpy(tempResults + i * 4, source + sourceIndex * 4, size);
                        }
                        
                        UnsafeUtility.MemCpy(dest + currentStart * 4, tempResults, batchSize * size);

                        remaining    -= batchSize;
                        currentStart += batchSize;
                    }
                    
//                    for (int i = start; i < end; i++)
//                    {
//                        int quadID = renderPtr[i];
//
//                        int sourceOffset = quadID * 4,
//                            destOffset   = i      * 4;
//
//                        UnsafeUtility.MemCpy(dest + destOffset, source + sourceOffset, size);
//                    }
                }
            }
        }


        [BurstCompile(
            OptimizeFor = OptimizeFor.Performance,
            FloatMode = FloatMode.Fast,          CompileSynchronously = true,
            FloatPrecision = FloatPrecision.Low, DisableSafetyChecks = true)]
        public struct UpdateFrustumMeshNormalUNorm16Job : IJobParallelForBatch
        {
            [ReadOnly, NativeDisableParallelForRestriction]
            public NativeArray<int> RenderList;

            [ReadOnly, NativeDisableParallelForRestriction]
            public NativeArray<Normal>.ReadOnly Normals;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<UNorm16x2> normal;

            [ReadOnly] public int JobNum;
        
            public unsafe void Execute(int startIndex, int count)
            {
                int length = RenderList.Length;
//                if (length < 1 || JobNum < 1)
//                    return;

                if (!(length >= 1 && JobNum >= 1))
                    return;

                int actualJobNum = math.min(JobNum, length);

                int jobLength = (length + JobNum - 1) / JobNum;
                
                var size   = UnsafeUtility.SizeOf<UNorm16x2>() * 4;

                var renderPtr = (int*)RenderList.GetUnsafeReadOnlyPtr();
                var source    = (Normal*)Normals.GetUnsafeReadOnlyPtr();
                var dest      = (UNorm16x2*)normal.GetUnsafePtr();
                var tempArray = stackalloc UNorm16x2[4];
                
                int endIndedx = math.min(startIndex + count, actualJobNum);
                
                for (int index = startIndex; index < endIndedx; index++)
                {
                    int start = jobLength * index,
                        end   = math.min(math.mad(jobLength, index, jobLength), length);

                    for (int i = start; i < end; i++)
                    {
                        int quadID = renderPtr[i];

                        int
                            sourceOffset = quadID * 4,
                            destOffset   = i      * 4;
                        
                        float3
                            na = source[sourceOffset].Value,
                            nb = source[sourceOffset + 1].Value;

                        float3 np = math.normalize(na + nb);

                        UNorm16x2
                            octNA = new(MathematicsUtil.UnitVectorToOctahedron(na), true),
                            octNB = new(MathematicsUtil.UnitVectorToOctahedron(nb), true),
                            octNP = new(MathematicsUtil.UnitVectorToOctahedron(np), true);

                        tempArray[0] = octNA;
                        tempArray[1] = octNP;
                        tempArray[2] = octNP;
                        tempArray[3] = octNB;

                        UnsafeUtility.MemCpy(dest + destOffset, tempArray, size);
                    }
                }
            }
        }

        [BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
        public struct UpdateFrustumMeshUVUNorm16Job : IJobParallelForBatch
        {
            [ReadOnly, NativeDisableParallelForRestriction]
            public NativeArray<int> RenderList;

            [ReadOnly, NativeDisableParallelForRestriction]
            public NativeArray<UNorm16x2>.ReadOnly uvs;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<UNorm16x2> uvsForRendering;

            [ReadOnly] public int JobNum;

            public unsafe void Execute(int startIndex, int count)
            {
                int length = RenderList.Length;
//                if (length < 1 || JobNum < 1)
//                    return;

                if (!(length >= 1 && JobNum >= 1))
                    return;
                
                int actualJobNum = math.min(JobNum, length);
                int jobLength    = (length + JobNum - 1) / JobNum;
                
                var size   = UnsafeUtility.SizeOf<UNorm16x2>() * 4;
                
                var renderPtr = (int*)RenderList.GetUnsafeReadOnlyPtr();
                var source    = (UNorm16x2*)uvs.GetUnsafeReadOnlyPtr();
                var dest      = (UNorm16x2*)uvsForRendering.GetUnsafePtr();

                int endIndedx = math.min(startIndex + count, actualJobNum);
                
                for (int index = startIndex; index < endIndedx; index++)
                {
                    int start = jobLength * index,
                        end   = math.min(math.mad(jobLength, index, jobLength), length);

                    for (int i = start; i < end; i++)
                    {
                        int quadID = renderPtr[i];

                        int sourceOffset = quadID * 4,
                            destOffset   = i      * 4;
                        
                        UnsafeUtility.MemCpy(dest + destOffset, source + sourceOffset, size);
                    }
                }
            }
        }

        [BurstCompile(
            OptimizeFor = OptimizeFor.Performance,
            FloatMode = FloatMode.Fast,          CompileSynchronously = true,
            FloatPrecision = FloatPrecision.Low, DisableSafetyChecks = true)]
        public struct UpdateFrustumMeshUVNormalUNorm8Job : IJobParallelForBatch
        {
            [ReadOnly, NativeDisableParallelForRestriction]
             public NativeArray<int> RenderList;

            [ReadOnly, NativeDisableParallelForRestriction]
            public NativeArray<Normal>.ReadOnly Normals;

            [ReadOnly, NativeDisableParallelForRestriction]
            public NativeArray<UNorm8x4>.ReadOnly uvAndNormal;

            [WriteOnly, NativeDisableParallelForRestriction]
            public NativeArray<UNorm8x4> uvAndNormalForRendering;

            [ReadOnly] public int JobNum;
        
            public void Execute(int startIndex, int count)
            {
                int length = RenderList.Length;
//                if (length < 1 || JobNum < 1)
//                    return;

                if (!(length >= 1 && JobNum >= 1))
                    return;

                int actualJobNum = math.min(JobNum, length);

                int jobLength = (length + JobNum - 1) / JobNum;

                int endIndedx = math.min(startIndex + count, actualJobNum);
                
                for (int index = startIndex; index < endIndedx; index++)
                {
                    int start = jobLength * index,
                        end   = math.min(math.mad(jobLength, index, jobLength), length);

                    for (int i = start; i < end; i++)
                    {
                        int quadID = RenderList[i];

                        int4 indiceStep = new int4(0, 1, 2, 3);

                        int4
                            indices     = math.mad(i,      4, indiceStep),
                            quadIndices = math.mad(quadID, 4, indiceStep);

                        float3
                            na = Normals[quadIndices.x].Value,
                            nb = Normals[quadIndices.w].Value;

                        float3 np = math.normalize(na + nb);

                        float2
                            octNA = MathematicsUtil.UnitVectorToOctahedron(na),
                            octNB = MathematicsUtil.UnitVectorToOctahedron(nb),
                            octNP = MathematicsUtil.UnitVectorToOctahedron(np);

                        UNorm8x4
                            nv0 = uvAndNormal[quadIndices[0]],
                            nv1 = uvAndNormal[quadIndices[1]],
                            nv2 = uvAndNormal[quadIndices[2]],
                            nv3 = uvAndNormal[quadIndices[3]];

                        uvAndNormalForRendering[indices.x] = new UNorm8x4(octNA, nv0.z, nv0.w);
                        uvAndNormalForRendering[indices.y] = new UNorm8x4(octNP, nv1.z, nv1.w);
                        uvAndNormalForRendering[indices.z] = new UNorm8x4(octNP, nv2.z, nv2.w);
                        uvAndNormalForRendering[indices.w] = new UNorm8x4(octNB, nv3.z, nv3.w);
                    }
                }
            }
        }

#endregion


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float3x4 CaculateQuadPos(in float4x4 local2World,
                                        in float3   pos,   in quaternion rot,
                                        float       scale, float         curveAngele)
        {
            float3x4 quadPos = new float3x4(
                new float3(0, 0, 0),
                new float3(1, 0, 0),
                new float3(0, 0, 1),
                new float3(1, 0, 1)
            );
            quadPos.c3 = MathematicsUtil.RotatePointAroundAxis(
                quadPos.c3, quadPos.c1, quadPos.c1 - quadPos.c2, curveAngele);

            float4x4 trs = math.mul(local2World, float4x4.TRS(pos, rot, scale));

            quadPos.c0 = MathematicsUtil.MatrixMultiplyPoint3x4(trs, quadPos.c0, 1);
            quadPos.c1 = MathematicsUtil.MatrixMultiplyPoint3x4(trs, quadPos.c1, 1);
            quadPos.c2 = MathematicsUtil.MatrixMultiplyPoint3x4(trs, quadPos.c2, 1);
            quadPos.c3 = MathematicsUtil.MatrixMultiplyPoint3x4(trs, quadPos.c3, 1);

            return quadPos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CaculateNormalAndArea(in  float3 a,  in  float3 b,  in  float3 c,
                                          out float3 na, out float3 nb, out float3 np,
                                          out float3 area)
        {
            float3 perpA    = math.cross(a, b);
            float3 perpB    = math.cross(b, c);
            float  perpLenA = math.length(perpA);
            float  perpLenB = math.length(perpB);
            float  areaA    = perpLenA        * 0.5f;
            float  areaB    = perpLenB        * 0.5f;
            float  areaP    = (areaA + areaB) * 0.5f;

            na = perpA / perpLenA;
            nb = perpB / perpLenB;
            np = math.normalize(na + nb);

            area = new float3(areaA, areaB, areaP);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CaculateNormalAndArea(in  float3 a,  in  float3 b, in float3 c,
                                          out float3 na, out float3 nb,
                                          out float3 area)
        {
            float3 perpA    = math.cross(a, b);
            float3 perpB    = math.cross(b, c);
            float  perpLenA = math.length(perpA);
            float  perpLenB = math.length(perpB);
            float  areaA    = perpLenA        * 0.5f;
            float  areaB    = perpLenB        * 0.5f;
            float  areaP    = (areaA + areaB) * 0.5f;

            na = perpA / perpLenA;
            nb = perpB / perpLenB;

            area = new float3(areaA, areaB, areaP);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CreatDistanceConstraint(ref NativeArray<DistanceConstraint> distanceConstraints,
                                            int                                 quadID, in float3 a, in float3 b, in float3 c, in float3 d, in float3 e)
        {
            int dStart = quadID * 5;
            distanceConstraints[dStart]     = new DistanceConstraint() { restLength = math.length(a) };
            distanceConstraints[dStart + 1] = new DistanceConstraint() { restLength = math.length(b) };
            distanceConstraints[dStart + 2] = new DistanceConstraint() { restLength = math.length(c) };
            distanceConstraints[dStart + 3] = new DistanceConstraint() { restLength = math.length(d) };
            distanceConstraints[dStart + 4] = new DistanceConstraint() { restLength = math.length(e) };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CreatBendConstraint(ref NativeArray<BendConstraint> bendConstraints,
                                        int                             quadID, in float3 na, in float3 nb)
        {
            float dot   = math.clamp(math.dot(na, nb), -1, 1);
            float angle = math.acos(dot);

            bendConstraints[quadID] = new BendConstraint()
            {
                // index0 = index0,
                // index1 = index1,
                // index2 = index2,
                // index3 = index3,
                restAngle = angle, // rad
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void UpdateSimulationData(ref NativeArray<QuadInvMass>        QuadInvMasses,
                                         ref NativeArray<Radius>             Radius,
                                         ref NativeArray<IsNeedUpdate>       IsNeedUpdates,
                                         ref NativeArray<PredictedPositions> PredictedPositions,
                                         ref NativeArray<Velocity>           Velocities,
                                         ref NativeArray<Normal>             Normals,
                                         ref NativeArray<InvMass>            InvMasses,
                                         ref NativeArray<Area>               Areas,
                                         //
                                         int       quadID,
                                         in int4   indices, in float3x4 quadPos,
                                         in float3 na,      in float3   nb,
                                         in float3 area,    float       radius,
                                         float     mass,    int         type)
        {
            float density;

            switch (type)
            {
                case 0:
                    density = 1.2f;
                    break;
                case 1:
                    density = 1;
                    break;
                case 2:
                    density = 1.4f;
                    break;
                case 3:
                    density = 1.2f;
                    break;
                default:
                    density = 1.5f;
                    break;
            }

            mass = mass * density * 0.008f;
            // mass = mass * density * areaP;   

            float
                invMass0 = 2.4f / mass,
                invMass1 = 2.9f / mass,
                invMass2 = 1.6f / mass,
                invMass3 = 2f   / mass;

            // float quadInvMass = 1.0f / (mass * (1.0f / 2.4f + 1.0f / 2.9f + 1.0f / 1.6f + 1.0f / 2.0f));
            float quadInvMass = 1.0f / (mass * 1.886494f);

            QuadInvMasses[quadID] = new QuadInvMass() { Value  = quadInvMass };
            Radius[quadID]        = new Radius() { Value       = radius };
            IsNeedUpdates[quadID] = new IsNeedUpdate() { Value = true };

            CreatParticleDataAppend(ref PredictedPositions, ref Velocities, ref Normals, ref InvMasses, ref Areas,
                                    indices[0],             quadPos.c0,     na,          invMass0,      area.x);

            CreatParticleDataAppend(ref PredictedPositions, ref Velocities, ref Normals, ref InvMasses, ref Areas,
                                    indices[1],             quadPos.c1,     na,          invMass1,      area.z);

            CreatParticleDataAppend(ref PredictedPositions, ref Velocities, ref Normals, ref InvMasses, ref Areas,
                                    indices[2],             quadPos.c2,     nb,          invMass2,      area.z);

            CreatParticleDataAppend(ref PredictedPositions, ref Velocities, ref Normals, ref InvMasses, ref Areas,
                                    indices[3],             quadPos.c3,     nb,          invMass3,      area.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CreatParticleDataAppend(ref NativeArray<PredictedPositions> PredictedPositions,
                                            ref NativeArray<Velocity>           Velocities,
                                            ref NativeArray<Normal>             Normals,
                                            ref NativeArray<InvMass>            InvMasses,
                                            ref NativeArray<Area>               Areas,
                                            int                                 index,   in float3 pos, in float3 nor,
                                            float                               invMass, float     area)
        {
            PredictedPositions[index] = new PredictedPositions() { Value = pos };
            Velocities[index]         = new Velocity() { Value           = float3.zero };
            Normals[index]            = new Normal() { Value             = nor };
            InvMasses[index]          = new InvMass() { Value            = invMass };
            Areas[index]              = new Area() { Value               = area };
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void UpdateMeshPos(ref NativeArray<float3> vertices, in int4 indices, in float3x4 quadPos)
        {
            vertices[indices[0]] = quadPos.c0;
            vertices[indices[1]] = quadPos.c1;
            vertices[indices[2]] = quadPos.c2;
            vertices[indices[3]] = quadPos.c3;
        }
    }
}