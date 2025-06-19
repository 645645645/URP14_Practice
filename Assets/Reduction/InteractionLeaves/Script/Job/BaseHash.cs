using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace UnityEngine.PBD
{
    
    [GenerateTestsForBurstCompatibility]
    public struct BaseHash : IComparable<BaseHash>
    {
        public int Index; //在原数组的索引
        public int Hash;

        public BaseHash(int index, int hash)
        {
            Index = index;
            Hash  = hash;
        }

        public int CompareTo(BaseHash other)
        {
            return Hash.CompareTo(other.Hash);
        }
    }
    
    public struct HashRange
    {
        public int Start;
        public int End;
    }

    public readonly struct HashComparer : IComparer<BaseHash>
    {
        public int Compare(BaseHash x, BaseHash y)
        {
            return x.CompareTo(y);
        }
    }
    
    //线程数多考虑转Tag+Filter
    //Parallel(hash：toArray  + IFilter(inRange：toList) ) + IJob（collect hash use FilterList）
    [BurstCompile(OptimizeFor = OptimizeFor.Performance,
                  FloatMode = FloatMode.Default, CompileSynchronously = true,
                  FloatPrecision = FloatPrecision.Low, DisableSafetyChecks = true)]
    // fast下 算哈希容易出事
    public struct CalculateParticleHashesJob: IJobParallelForBatch, IJob
    {
        [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
        public NativeArray<int>.ReadOnly UpdateList;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PredictedPositions>.ReadOnly PredictedPositions;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<QuadPredictedPositions>.ReadOnly QuadPredictedPositions;

        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeList<BaseHash> ParticleHashes;

        [ReadOnly] public float4 filterParams; //pos radius

        [ReadOnly] public float cellRadius;

        [ReadOnly] public int bucketCapacityMask;

        [ReadOnly] public bool collisionByQuad;

#if UNITY_EDITOR
        public NativeArray<int> debugCounter;
#endif

        public void Execute()
        {

#if UNITY_EDITOR
            debugCounter[0] = 0;
#endif
            if (collisionByQuad)
            {
                int hashMask = MathematicsUtil.NextPowerOfTwo(UpdateList.Length) - 1;
                for (int index = 0; index < UpdateList.Length; index++)
                {
                    int quad = UpdateList[index];
                    AddHashByQuad(quad, hashMask);
                }
            }
            else
            {
                int hashMask = (MathematicsUtil.NextPowerOfTwo(UpdateList.Length) << 2) - 1;
                for (int index = 0; index < UpdateList.Length; index++)
                {
                    int quad = UpdateList[index];
                    AddHashByParticle(quad, hashMask);
                }
            }
        }

        public void Execute(int start, int count)
        {
            if (collisionByQuad)
            {
                int hashMask = MathematicsUtil.NextPowerOfTwo(UpdateList.Length) - 1;
                for (int index = start; index < start + count; index++)
                {
                    int quad = UpdateList[index];

                    AddHashByQuad(quad, hashMask);
                }
            }
            else
            {
                int hashMask = (MathematicsUtil.NextPowerOfTwo(UpdateList.Length) << 2) - 1;
                for (int index = start; index < start + count; index++)
                {
                    int quad = UpdateList[index];

                    AddHashByParticle(quad, hashMask);
                }
            }
        }

        [GenerateTestsForBurstCompatibility]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void AddHashByQuad(int quad, int hashMask)
        {
            float3 pos = QuadPredictedPositions[quad].Value;

            if (!MathematicsUtil.InSphereSpacial(in pos, cellRadius, in filterParams))
                return;

            AddToHashList(quad, pos, hashMask);
        }

        [GenerateTestsForBurstCompatibility]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void AddHashByParticle(int quad, int hashMask)
        {
            int start = quad * 4;
            for (int i = start; i < start + 4; i++)
            {
                float3 pos = PredictedPositions[i].Value;

                if (!MathematicsUtil.InSphereSpacial(in pos, cellRadius, in filterParams))
                    continue;

                AddToHashList(i, pos, hashMask);
            }
        }

        [GenerateTestsForBurstCompatibility]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void AddToHashList(int index, float3 pos, int hashMask)
        {
            int hash = HashUtility.Hash(pos, cellRadius, hashMask);
            ParticleHashes.AddNoResize(new BaseHash(index, hash));

#if UNITY_EDITOR
            if (hash == 0)
                debugCounter[0]++;
#endif
        }
    }

    [BurstCompile]
    public struct BuildGridLookup : IJob
    {
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<BaseHash> SortedHashes;

        [WriteOnly, NativeDisableParallelForRestriction]
        public UnsafeParallelHashMap<PrecomputedHashKey, HashRange> HashRanges;

        public void Execute()
        {
            if (SortedHashes.Length < 1)
                return;

            int currentHash = SortedHashes[0].Hash,
                start       = 0,
                end         = SortedHashes.Length;
            
            for (int i = 1; i < end; i++)
            {
                int nextHash = SortedHashes[i].Hash;
                if (nextHash != currentHash)
                {
                    HashRanges.TryAdd(new (currentHash), new HashRange()
                    {
                        Start = start,
                        End   = i - 1,
                    });

                    currentHash = nextHash;
                    start       = i;
                }
            }
            
            HashRanges.TryAdd(new (currentHash), new HashRange()
            {
                Start = start,
                End   = end - 1,
            });
        }
    }
    
    [BurstCompile]
    public struct BuildGridLookupArray : IJob
    {
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<BaseHash> SortedHashes;

//        [WriteOnly, NativeDisableParallelForRestriction]
//        public UnsafeParallelHashMap<PrecomputedHashKey, HashRange> HashRanges;

        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeList<int> Hash;
        
        [WriteOnly, NativeDisableParallelForRestriction, NativeDisableUnsafePtrRestriction]
        public SimpleHashArray<HashRange> HashRanges;

        public void Execute()
        {
            if (SortedHashes.Length < 1)
                return;

            int currentHash = SortedHashes[0].Hash,
                start       = 0,
                end         = SortedHashes.Length;
            Hash.ResizeUninitialized(end);
            unsafe
            {
                int* outputPtr   = Hash.GetUnsafePtr();
                int  outputIndex = 0;

                for (int i = 1; i < end; i++)
                {
                    int nextHash = SortedHashes[i].Hash;
                    if (nextHash != currentHash)
                    {
                        outputPtr[outputIndex] = currentHash;
                        outputIndex++;
                        HashRanges.TryAdd(currentHash, new HashRange()
                        {
                            Start = start,
                            End   = i - 1,
                        });

                        currentHash = nextHash;
                        start       = i;
                    }
                }

                outputPtr[outputIndex] = currentHash;
                outputIndex++;
                HashRanges.TryAdd(currentHash, new HashRange()
                {
                    Start = start,
                    End   = end - 1,
                });
                

                Hash.ResizeUninitialized(outputIndex);
            }
        }
    }
    
    [BurstCompile(OptimizeFor = OptimizeFor.Performance, 
                  FloatMode = FloatMode.Default, CompileSynchronously = true, 
                  FloatPrecision = FloatPrecision.Low, DisableSafetyChecks = true)]
    public struct BuildHashNeighbours : IJobParallelForBatch
    {
        [ReadOnly, NativeDisableParallelForRestriction, NativeMatchesParallelForLength]
        public NativeArray<int>.ReadOnly UpdateList;

        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<PredictedPositions>.ReadOnly PredictedPositions;
        
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<QuadPredictedPositions> QuadPredictedPositions;

        [WriteOnly] public NativeParallelMultiHashMap<int, int>.ParallelWriter hashMap;
        
        [ReadOnly, NativeDisableParallelForRestriction]
        public NativeArray<int3>.ReadOnly neighborOffsets;
        
        [ReadOnly] public float4 filterParams;//pos radius
        
        [ReadOnly] public float cellRadius;

        [ReadOnly] public int bucketCapacityMask;

        [ReadOnly] public bool collisionByQuad;

        public void Execute(int start, int count)
        {
            if (collisionByQuad)
            {
                for (int index = start; index < start + count; index++)
                {
                    int quadID = UpdateList[index];
                    AddNeighboursByQuad(quadID);
                }
            }
            else
            {
                for (int index = start; index < start + count; index++)
                {
                    int quadID = UpdateList[index];
                    AddNeighboursByParticles(quadID);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void AddNeighboursByQuad(int index)
        {
            float3 position = QuadPredictedPositions[index].Value;
            if (MathematicsUtil.InSphereSpacial(in position, cellRadius, in filterParams))
            {
                CreatNeighbours(index, position);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void AddNeighboursByParticles(int index)
        {
            int start = index * 4;
            for (int i = start; i < start + 4; i++)
            {
                var position = PredictedPositions[i].Value;
                if (MathematicsUtil.InSphereSpacial(in position, cellRadius, in filterParams))
                {
                    CreatNeighbours(i, position);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void CreatNeighbours(int index, float3 pos)
        {
            int3 cellPos = HashUtility.PosToGrid(pos, cellRadius);

            for (int offset = 0; offset < neighborOffsets.Length; offset++)
            {
                int3 offsetPos = cellPos + neighborOffsets[offset];
                int  hash      = HashUtility.GridToHash(offsetPos, bucketCapacityMask);
                hashMap.Add(hash, index);
            }
        }
    }
}