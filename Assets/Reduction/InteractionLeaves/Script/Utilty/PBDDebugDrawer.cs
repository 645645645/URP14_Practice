using System;
using NUnit.Framework;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace UnityEngine.PBD
{
#if UNITY_EDITOR
    [Serializable]
    public struct PBDDebugDrawer
    {
        public bool vertices;
        public bool normal;
        public bool bend;
        public bool hitNormal;
        public bool hitConcatDelta;
        public bool hitConcatNormal;
        public bool particle;
        public bool voxel;
        public bool octree;

        private NativeSlice<PredictedPositions> __DebugPostition;
        private NativeSlice<Normal>             __DebugQuadNormal;
        private NativeArray<float3>             __DebugBendDir;
        private NativeArray<PBDCollisionHit>    __DebugCollisionHit;
        private NativeList<float3>              __DebugVoxel;

        private NativeArray<int> __DebugHashCounter;

        private NativeSlice<IsNeedUpdate> __DebugIsUpdate;

        private NativeSlice<ParticleCollisionConstraint> __DebugParticleCollison;

        public bool bDrawDebug => vertices        ||
                                  normal          ||
                                  bend            ||
                                  hitNormal       ||
                                  hitConcatDelta  ||
                                  hitConcatNormal ||
                                  particle        ||
                                  voxel;

        public NativeArray<float3> __BendDirArray => __DebugBendDir;

        public NativeArray<PBDCollisionHit> __HitArray => __DebugCollisionHit;

        public NativeList<float3> __Voxel => __DebugVoxel;

        public NativeArray<int> __HashCounter => __DebugHashCounter;

        public void Init(int                  vertexCount,
                         in ProductNativeData nativeData)
        {
            __DebugBendDir      = new NativeArray<float3>(vertexCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            __DebugCollisionHit = new NativeArray<PBDCollisionHit>(vertexCount, Allocator.Persistent);

            __DebugPostition        = nativeData.m_predictedPositions.Slice();
            __DebugQuadNormal       = nativeData.m_normals.Slice();
            __DebugIsUpdate         = nativeData.m_isNeedUpdates.Slice();
            __DebugParticleCollison = nativeData.m_particleCollisionConstraints.Slice();

            __DebugVoxel  = new NativeList<float3>(2048 << 5, Allocator.Persistent);

            __DebugHashCounter = new NativeArray<int>(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        }

        public void Dispose()
        {
            if (__DebugBendDir.IsCreated) __DebugBendDir.Dispose();
            if (__DebugCollisionHit.IsCreated) __DebugCollisionHit.Dispose();
            if (__DebugVoxel.IsCreated) __DebugVoxel.Dispose();
            if (__DebugHashCounter.IsCreated) __DebugHashCounter.Dispose();
        }

        public unsafe void DrawDebugGizmos(int quadCount, float particleRadius, ref NativeList<int> updateList)
        {
            if (bDrawDebug)
            {
                for (int i = 0; i < updateList.Length; i++)
                {
                    var index    = updateList[i];
                    var isUpdate = __DebugIsUpdate[index].Value;
                    var start    = index * 4;
                    for (int j = 0; j < 4; j++)
                    {
                        int pIndex = j + start;
                        var pos    = __DebugPostition[pIndex].Value;
                        // if (vertices)
                        // {
                        //     Gizmos.color = Color.yellow;
                        //     Gizmos.DrawWireSphere(pos, 0.01f);
                        // }
                        //
                        // if (normal)
                        // {
                        //     Gizmos.color = Color.blue;
                        //     var normal = __DebugQuadNormal[pIndex].Value;
                        //     Gizmos.DrawLine(pos, pos + normal * 0.1f);
                        // }

                        if (particle && isUpdate)
                        {
                            var pCnst = __DebugParticleCollison[pIndex];
                            if (pCnst.ConstraintsCount > 0)
                            {
                                Gizmos.color = Color.red;
                                Gizmos.DrawLine(pos, pos + pCnst.Delta * 500 / pCnst.ConstraintsCount);
                                Gizmos.color = Color.yellow;
                                Gizmos.DrawWireSphere(pos, particleRadius);
                            }
                        }

                        if (bend && isUpdate)
                        {
                            Gizmos.color = Color.cyan;
                            var bendDir = __DebugBendDir[pIndex];
                            Gizmos.DrawLine(pos, pos + bendDir * 10);
                        }

                        var hit = __DebugCollisionHit[pIndex];

                        if (hitNormal && isUpdate && hit.hitCount > 0)
                        {
                            Gizmos.color = Color.green;
                            Gizmos.DrawLine(hit.hitSurfacePos, hit.hitSurfacePos + hit.hitNormal * 0.1f);
                        }

                        if (hitConcatDelta && isUpdate && hit.hitCount > 0)
                        {
                            Gizmos.color = Color.red;
                            var delta = hit.hitConcatDelta;
                            Gizmos.DrawLine(pos, pos + delta * 1f);
                        }

                        if (hitConcatNormal && isUpdate && hit.hitCount > 0)
                        {
                            Gizmos.color = Color.magenta;
                            // var center = hit.hitActorCenter;
                            var concatNormal = hit.hitConcatNormal;
                            // Gizmos.DrawLine(center, center + concatNormal * 1f);
                            Gizmos.DrawLine(pos, pos + concatNormal * 0.1f);
                        }
                    }
                }
            }
        }

        public void DrawDebugGizmosAll(int quadCount, float particleRadius)
        {
            if (bDrawDebug)
            {
                for (int i = 0; i < quadCount; i++)
                {
                    var start = i * 4;
                    // var isUpdate = __DebugIsUpdate[i].Value;
                    for (int j = start; j < start + 4; j++)
                    {
                        var pos = __DebugPostition[j].Value;
                        if (vertices)
                        {
                            Gizmos.color = Color.yellow;
                            Gizmos.DrawWireSphere(pos, 0.01f);
                        }

                        if (normal)
                        {
                            Gizmos.color = Color.blue;
                            var normal = __DebugQuadNormal[j].Value;
                            Gizmos.DrawLine(pos, pos + normal * 0.1f);
                        }
                    }
                }

                if (voxel)
                {
                    Gizmos.color = Color.cyan;
                    for (int i = 0; i < __DebugVoxel.Length; i++)
                        Gizmos.DrawWireSphere(__DebugVoxel[i], 0.05f);
                }
            }
        }

        public void DrawOctree(in RigidbodyOctree oct, int rigibodyNum)
        {
            if (octree && oct is { IsCreated: true, NodeLength: > 0 })
                DrawOctree(in oct, oct.Nodes[0], rigibodyNum);
        }

        private void DrawOctree(in RigidbodyOctree oct, in OctreeNode node, int rigibodyNum)
        {
            int rigiCount = node.RigidbodyCount;
            if (rigiCount > 0)
            {
                Gizmos.color = Color.Lerp(Color.clear, Color.green, Mathf.Sqrt(Mathf.Clamp01(rigiCount / (float)rigibodyNum)));
                Gizmos.DrawWireCube(node.Bounds.Center, node.Bounds.Size);
            }

            if (!node.IsLeaf)
            {
                for (int i = 0; i < 8; i++)
                {
                    DrawOctree(in oct, oct.Nodes[node.FirstChild + i], rigibodyNum);
                }
            }
        }


    }
#endif
}