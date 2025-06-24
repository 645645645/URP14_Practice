#define USE_BATCH

using System;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Properties;
using UnityEngine.Jobs;
using UnityEngine.Rendering;

#if USE_BATCH
using For = Unity.Jobs.IJobParallelForBatchExtensions;
#else
using For = Unity.Jobs.IJobParallelForExtensions;
#endif

namespace UnityEngine.PBD
{
    public class InteractionOfLeavesManager : CreateSingleton<InteractionOfLeavesManager>
    {
        public enum PhysicsUpdateMode
        {
            RealTimeFrameRate,
            FixedTimeFrameRate,
        }

        const MeshUpdateFlags UPDATE_FLAGS_SILENT =
            MeshUpdateFlags.DontValidateIndices   |
            MeshUpdateFlags.DontNotifyMeshUsers   |
            MeshUpdateFlags.DontRecalculateBounds |
            MeshUpdateFlags.DontResetBoneBounds;

        static readonly VertexAttributeDescriptor[] MESH_BUFFER_PARAMS =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position,  VertexAttributeFormat.Float32, 3, 0),
            new VertexAttributeDescriptor(VertexAttribute.Normal,    VertexAttributeFormat.UNorm16, 2, 1),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.UNorm16, 2, 2),
        };
        
        static readonly VertexAttributeDescriptor[] MESH_BUFFER_PARAMS_UNORM8 =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position,  VertexAttributeFormat.Float32, 3, 0),
            new VertexAttributeDescriptor(VertexAttribute.Normal,    VertexAttributeFormat.UNorm8, 4, 1),
        };

        public int targetFrameRate = -1;

        public PhysicsUpdateMode physicsUpdateMode = PhysicsUpdateMode.RealTimeFrameRate;

        public ProductLeavesPropertiesData m_propertiesData;

        [_ReadOnlyInPlayMode] public WindField  m_windField;

        private ProductNativeData m_nativeData;

#if UNITY_EDITOR
        [DontCreateProperty, Space(20)] public PBDDebugDrawer debugDrawer;
#endif
        
        private float m_deltaTime;
        private float m_productNumSumByFrame = 0;
        private int   m_checkIndex           = 0;
        private int   m_quadCount            = 0;
        private int   m_updateCount;
        private int   m_staticCount;
        private int   m_extDynamicCount;
        private int   m_extPostDynamicCount;
        private int   m_renderCount;
        private int   m_quadBatchCount;
        private int   m_updateQuadBatchCount;
        private int   m_staticQuadBatchCount;
        private int   m_extDynamicBatchCount;
        private int   m_extPostDynamicBatchCount;

        private Mesh mesh;

        private JobHandle _lastJobHandle = default;
        

        //=========================================================================================
        /// <summary>
        /// Reload Domain 対策
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            InitMember();
        }


        protected override void InitSingleton()
        {
            Initial();
        }

        private void OnDisable()
        {
            Dispose();
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (Application.isPlaying)
            {
                _clearHashMapJobHandleSPH.Complete();
                _clearHashMapJobHandle.Complete();
                _lastJobHandle.Complete();

                debugDrawer.DrawDebugGizmos(m_updateCount, m_propertiesData.P_ParticlesRadius, ref m_nativeData.m_UpdateList);
                debugDrawer.DrawDebugGizmosAll(m_quadCount, m_propertiesData.P_ParticlesRadius);
                debugDrawer.DrawOctree(in m_nativeData.m_RigibodyOctree, m_nativeData.m_rigibodyColliders.Length);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Bounds area = m_propertiesData.Area;

            float3
                center = area.center + transform.position,
                extend = area.extents;

            float3
                ex1 = transform.TransformVector(new float3(-1, -1, -1) * extend),
                ex2 = transform.TransformVector(new float3(-1, 1,  -1) * extend),
                ex3 = transform.TransformVector(new float3(1,  1,  -1) * extend),
                ex4 = transform.TransformVector(new float3(1,  -1, -1) * extend);

            float3
                a = center + ex1,
                b = center + ex2,
                c = center + ex3,
                d = center + ex4,
                e = center - ex3,
                f = center - ex4,
                g = center - ex1,
                h = center - ex2;

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, d);
            Gizmos.DrawLine(a, d);
            Gizmos.DrawLine(e, f);
            Gizmos.DrawLine(f, g);
            Gizmos.DrawLine(g, h);
            Gizmos.DrawLine(h, e);
            Gizmos.DrawLine(a, e);
            Gizmos.DrawLine(b, f);
            Gizmos.DrawLine(c, g);
            Gizmos.DrawLine(d, h);
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                _lastJobHandle.Complete();
                ReflushParams();
            }

            ref var propertiesData = ref m_propertiesData;

            switch (propertiesData.indexFormat)
            {
                case IndexFormat.UInt16:
                    propertiesData.MaxLeavesCount = Mathf.Min(propertiesData.MaxLeavesCount, ushort.MaxValue / 6);
                    break;
                case IndexFormat.UInt32:
                    propertiesData.MaxLeavesCount = (int)Mathf.Min(propertiesData.MaxLeavesCount, uint.MaxValue / 6);
                    break;
            }
        }
#endif

        void ReflushParams()
        {
            m_propertiesData?.InitProductParams(ref m_nativeData.productParams);
        }
        

        void Dispose()
        {
            _lastJobHandle.Complete();
            // _windFiledJobHandle.Complete();
            _clearHashMapJobHandle.Complete();
            _clearHashMapJobHandleSPH.Complete();
            m_nativeData.Dispose();
            Destroy(mesh);

            m_windField?.Dispose();

#if UNITY_EDITOR
            debugDrawer.Dispose();
#endif

            // trianglesBuffer?.Dispose();
            // trianglesBuffer = null;
            // posBuffer?.Dispose();
            // posBuffer = null;
            // normalBuffer?.Dispose();
            // normalBuffer = null;
            // uvBuffer?.Dispose();
            // uvBuffer = null;
            // commandBuf?.Dispose();
            // commandBuf = null;
        }

        void Initial()
        {
            Application.targetFrameRate = targetFrameRate;
            ref var nativeData = ref m_nativeData;
            ref var properties = ref m_propertiesData;

            if (!nativeData.Initialize(properties))
                return;

            if (properties.extForceMode == ExtForceMode.WindField)
                m_windField.Initialize();

            IndexFormat format = properties.indexFormat;

            var maxLeavesCount = properties.MaxLeavesCount;
            var verticesCount  = properties.VerticesCount;
            // var trianglesCount = properties.TrianglesCount;

            switch (format)
            {
                case IndexFormat.UInt16:
                    var fillUvAndTranglesUShort = new LeavesUpdateJobs.FillTrianglesJobUShort()
                    {
                        triangles = nativeData.trianglesArrayUShort,
                    };
                    _lastJobHandle = fillUvAndTranglesUShort.ScheduleByRef(maxLeavesCount, (maxLeavesCount + 1) / 2, _lastJobHandle);
                    break;
                case IndexFormat.UInt32:
                    var fillUvAndTranglesUInt = new LeavesUpdateJobs.FillTrianglesJobUInt()
                    {
                        triangles = nativeData.trianglesArrayUInt,
                    };
                    _lastJobHandle = fillUvAndTranglesUInt.ScheduleByRef(maxLeavesCount, (maxLeavesCount + 1) / 2, _lastJobHandle);
                    break;
            }

            ReflushParams();

            mesh = new Mesh()
            {
                name = "leavesRoot",
            };
            GetComponent<MeshFilter>().mesh = mesh;
            var render = GetComponent<MeshRenderer>();
            render.enabled = true;
            m_material = render.sharedMaterial;

            switch (properties.uvNormalFormat)
            {
                case MeshUvNormalFormat.UNorm16x2:
                    m_material.DisableKeyword("_COMPACTNORMALUV");
                    mesh.SetVertexBufferParams(verticesCount, MESH_BUFFER_PARAMS);
                    break;
                case MeshUvNormalFormat.UNorm8x4:
                    m_material.EnableKeyword("_COMPACTNORMALUV");
                    mesh.SetVertexBufferParams(verticesCount, MESH_BUFFER_PARAMS_UNORM8);
                    break;
            }

#if UNITY_EDITOR
            debugDrawer.Init(verticesCount, in nativeData);
#endif
            // ReflushMesh();
            int trianglesCount = maxLeavesCount * 6;

            mesh.SetIndexBufferParams(trianglesCount, format);

            _lastJobHandle.Complete();

            switch (format)
            {
                case IndexFormat.UInt16:
                    mesh.SetIndexBufferData(nativeData.trianglesArrayUShort, 0, 0, trianglesCount, UPDATE_FLAGS_SILENT);
                    break;
                case IndexFormat.UInt32:
                    mesh.SetIndexBufferData(nativeData.trianglesArrayUInt, 0, 0, trianglesCount, UPDATE_FLAGS_SILENT);
                    break;
            }

            // trianglesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 6, sizeof(uint));//只存一个quad
            // posBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, verticesCount, 3 * sizeof(float));
            // normalBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, verticesCount, 3 * sizeof(float));
            // // uvBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, verticesCount, UnsafeUtility.SizeOf<half2>());
            // uvBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, verticesCount, 2 * sizeof(float));
            // mpb = new MaterialPropertyBlock();
            // commandBuf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, commandCount, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            // commandData = new GraphicsBuffer.IndirectDrawIndexedArgs[commandCount];
        }

        private Material m_material;
        // private MaterialPropertyBlock mpb;
        // private GraphicsBuffer trianglesBuffer;
        // private GraphicsBuffer posBuffer;
        // private GraphicsBuffer normalBuffer;
        // private GraphicsBuffer uvBuffer;
        // private GraphicsBuffer commandBuf;
        // private GraphicsBuffer.IndirectDrawIndexedArgs[] commandData;
        // private const int commandCount = 3;


        //----缓存队列
        public void RegistRigibody<T>(in T pbdCollider) where T : PBDColliderBase
        {
            m_nativeData.AddCollider(in pbdCollider);
        }

        public void UnRegistRigibody<T>(in T pbdCollider) where T : PBDColliderBase
        {
            m_nativeData.RemoveCollider(in pbdCollider);
        }

        public void UpdateColliderInfo<T>(in T pbdCollider) where T : PBDColliderBase
        {
            m_nativeData.UpdateCollider(in pbdCollider);
        }

        public void UpdateSceneBounds()
        {
            m_UpdateScenceBounds = true;
        }

        private bool m_UpdateScenceBounds = false;


        public void RegistForce<T>(in T force) where T : PBDForce
        {
            m_nativeData.AddForce(force);
        }

        public void UnRegistForce<T>(in T force) where T : PBDForce
        {
            m_nativeData.RemoveForce(force);
        }

        public void UpdateForceInfo<T>(in T force) where T : PBDForce
        {
            m_nativeData.UpdateForce(force);
        }


        //-----------

        private bool m_quadHasFull = false;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReflushMesh()
        {
            if (m_quadCount < 1) return;
            ref var properties = ref m_propertiesData;
            ref var nativeData = ref m_nativeData;

            Vector3 size = properties.Area.size;

            // IndexFormat format = properties.indexFormat;

            if (properties.IsFrustumCullingOn && m_renderCount > 0)
            {
                int vertexCount    = m_renderCount * Constants.VerticesLimit;
                int trianglesCount = m_renderCount * Constants.TrainglesLimit;

                mesh.SetVertexBufferData(nativeData.verticesArray, 0, 0, vertexCount, 0, UPDATE_FLAGS_SILENT);
                
                switch (properties.uvNormalFormat)
                {
                    case MeshUvNormalFormat.UNorm16x2:
                        mesh.SetVertexBufferData(nativeData.normalArray,   0, 0, vertexCount, 1, UPDATE_FLAGS_SILENT);
                        mesh.SetVertexBufferData(nativeData.uvsForRendering, 0, 0, vertexCount, 2, UPDATE_FLAGS_SILENT);
                        break;
                    
                    case MeshUvNormalFormat.UNorm8x4:
                        mesh.SetVertexBufferData(nativeData.uvsAndNormalForRendering,   0, 0, vertexCount, 1, UPDATE_FLAGS_SILENT);
                        break;
                }

                var subMesh = new SubMeshDescriptor(0, trianglesCount, MeshTopology.Triangles);
                mesh.SetSubMesh(0, subMesh, UPDATE_FLAGS_SILENT);

                subMesh.bounds = new Bounds(transform.position + Vector3.down * size.y, 4 * size);
                mesh.bounds    = subMesh.bounds;
            }
            else
            {
                // m_material.DisableKeyword("_INDIRECTDRAWON");
                int vertexCount    = m_quadCount * Constants.VerticesLimit;
                int trianglesCount = m_quadCount * Constants.TrainglesLimit;
                
                mesh.SetVertexBufferData(nativeData.verticesArray, 0, 0, vertexCount, 0, UPDATE_FLAGS_SILENT);
                
                switch (properties.uvNormalFormat)
                {
                    case MeshUvNormalFormat.UNorm16x2:
                        mesh.SetVertexBufferData(nativeData.normalArray,   0, 0, vertexCount, 1, UPDATE_FLAGS_SILENT);

                        if (!m_quadHasFull)
                            mesh.SetVertexBufferData(nativeData.meshUVsArray, 0, 0, vertexCount, 2, UPDATE_FLAGS_SILENT);
                        break;
                    
                    case MeshUvNormalFormat.UNorm8x4:
                        mesh.SetVertexBufferData(nativeData.uvsAndNormal,   0, 0, vertexCount, 1, UPDATE_FLAGS_SILENT);
                        break;
                }
                

                if (!m_quadHasFull)
                {
                    var subMesh = new SubMeshDescriptor(0, trianglesCount, MeshTopology.Triangles);
                    mesh.SetSubMesh(0, subMesh, UPDATE_FLAGS_SILENT);

                    subMesh.bounds = new Bounds(transform.position + Vector3.down * size.y, 4 * size);
                    mesh.bounds    = subMesh.bounds;

                    if (m_quadCount == properties.MaxLeavesCount)
                    {
                        m_quadHasFull = true;
                    }
                }
                
                // Camera cam;
                // #if UNITY_EDITOR
                //     cam = null;
                // #else
                //     cam = properties.focusCamera;
                // #endif
                // m_material.EnableKeyword("_INDIRECTDRAWON");
                // trianglesBuffer.SetData(nativeData.trianglesArrayUInt.Slice(0,6).ToArray());
                // posBuffer.SetData(nativeData.m_predictedPositions);
                // normalBuffer.SetData(nativeData.m_normals);
                // // mpb.SetBuffer("_Triangles", trianglesBuffer);
                // mpb.SetBuffer("_Positions", posBuffer);
                // mpb.SetBuffer("_Normals", normalBuffer);
                //
                //
                // if (!m_quadHasFull)
                // {
                //     uvBuffer.SetData(nativeData.meshUVsArray);
                //     mpb.SetBuffer("_UVs", uvBuffer);
                //     
                //     if (m_quadCount == properties.MaxLeavesCount)
                //     {
                //         m_quadHasFull = true;
                //     }
                // }
                //
                // for (int i = 0; i < commandCount; i++)
                // {
                //     commandData[i].indexCountPerInstance = 6;
                //     commandData[i].instanceCount = (uint)m_quadCount;
                //     commandData[i].startInstance = 0;
                //     commandData[i].startIndex = 0;
                //     commandData[i].baseVertexIndex = 0;
                // }
                //
                // commandBuf.SetData(commandData);
                // //
                // Graphics.DrawProceduralIndirect(
                //     m_material,
                //     new Bounds(transform.position + Vector3.down * size.y, 4 * size),
                //     MeshTopology.Triangles,
                //     trianglesBuffer,
                //     commandBuf,
                //     0,
                //     cam,
                //     mpb,
                //     ShadowCastingMode.Off,
                //     false,
                //     0
                // );
                // Graphics.DrawProcedural(
                //     m_material,
                //     new Bounds(transform.position + Vector3.down * size.y, 4 * size),
                //     MeshTopology.Triangles,
                //     trianglesBuffer,
                //     6,
                //     m_quadCount,
                //     cam,
                //     mpb,
                //     ShadowCastingMode.Off,
                //     false,
                //     0
                // );
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining), GenerateTestsForBurstCompatibility]
        JobHandle CreatNewQuad(JobHandle dep, float deltaTime)
        {
            ref var properties = ref m_propertiesData;
            ref var nativeData = ref m_nativeData;

            m_productNumSumByFrame += deltaTime * properties.productNumPerSecond;
            if (m_productNumSumByFrame > 1)
            {
                switch (properties.uvNormalFormat)
                {
                    case MeshUvNormalFormat.UNorm16x2:
                    {
                        var creatJob = new LeavesUpdateJobs.CreatQuadMeshDataUNorm16Job()
                        {
                            distanceConstraints = nativeData.m_distanceConstraints,
                            bendConstraints     = nativeData.m_bendConstraints,
                            PredictedPositions  = nativeData.m_predictedPositions,
                            Velocities          = nativeData.m_velocities,
                            Normals             = nativeData.m_normals,
                            Radius              = nativeData.m_radius,
                            InvMasses           = nativeData.m_invmasses,
                            QuadInvMasses       = nativeData.m_quadInvMass,
                            Areas               = nativeData.m_areas,
                            IsNeedUpdates       = nativeData.m_isNeedUpdates,
                            vertices            = nativeData.verticesArray,
                            normal              = nativeData.normalArray,
                            uvs                 = nativeData.meshUVsArray,
                            skinParams          = nativeData.skinSplitParams.AsReadOnly(),
                            productMinMax       = nativeData.productParams.AsReadOnly(),
                            local2World         = transform.localToWorldMatrix,
                            offset              = m_checkIndex,
                            Radius2Rigibody     = properties.R_Particle2RigiRadius,
                        };
                        //每帧生成新的
                        int curFrameProductNum = (int)m_productNumSumByFrame;
                        m_checkIndex           += curFrameProductNum;
                        m_checkIndex           %= properties.MaxLeavesCount;
                        m_productNumSumByFrame -= curFrameProductNum;
                        m_quadCount            += curFrameProductNum;
                        m_quadCount            =  math.min(properties.MaxLeavesCount, m_quadCount);

                        dep = creatJob.ScheduleByRef(curFrameProductNum, Mathf.Max((curFrameProductNum + 1) / 2, 1), dep);
                    }
                        break;
                    case MeshUvNormalFormat.UNorm8x4:
                    {
                        var creatJob = new LeavesUpdateJobs.CreatQuadMeshDataUNorm8Job()
                        {
                            distanceConstraints = nativeData.m_distanceConstraints,
                            bendConstraints     = nativeData.m_bendConstraints,
                            PredictedPositions  = nativeData.m_predictedPositions,
                            Velocities          = nativeData.m_velocities,
                            Normals             = nativeData.m_normals,
                            Radius              = nativeData.m_radius,
                            InvMasses           = nativeData.m_invmasses,
                            QuadInvMasses       = nativeData.m_quadInvMass,
                            Areas               = nativeData.m_areas,
                            IsNeedUpdates       = nativeData.m_isNeedUpdates,
                            vertices            = nativeData.verticesArray,
                            uvAndNormal         = nativeData.uvsAndNormal,
                            skinParams          = nativeData.skinSplitParams.AsReadOnly(),
                            productMinMax       = nativeData.productParams.AsReadOnly(),
                            local2World         = transform.localToWorldMatrix,
                            offset              = m_checkIndex,
                            Radius2Rigibody     = properties.R_Particle2RigiRadius,
                        };
                        //每帧生成新的
                        int curFrameProductNum = (int)m_productNumSumByFrame;
                        m_checkIndex           += curFrameProductNum;
                        m_checkIndex           %= properties.MaxLeavesCount;
                        m_productNumSumByFrame -= curFrameProductNum;
                        m_quadCount            += curFrameProductNum;
                        m_quadCount            =  math.min(properties.MaxLeavesCount, m_quadCount);

                        dep = creatJob.ScheduleByRef(curFrameProductNum, Mathf.Max((curFrameProductNum + 1) / 2, 1), dep);
                    }
                        break;
                }
            }

            return dep;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining), GenerateTestsForBurstCompatibility]
        JobHandle PrepareClear(JobHandle dep)
        {
            ref var properties = ref m_propertiesData;
            ref var nativeData = ref m_nativeData;

            var clearClearParticleCollisionConstraintJob = new ClearConstraintsArray<ParticleCollisionConstraint>()
            {
                UpdateList  = nativeData.m_UpdateList.AsParallelReader(),
                Constraints = nativeData.m_particleCollisionConstraints,
            };

            var clearRigiCollisionConstraintJob = new ClearConstraintsArray<RigiCollisionConstraint>()
            {
                UpdateList  = nativeData.m_UpdateList.AsParallelReader(),
                Constraints = nativeData.m_rigiCollisionConstraints,
            };

            var clearParticleJobHandle = clearClearParticleCollisionConstraintJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, dep);

            var clearRigiJobHandle = clearRigiCollisionConstraintJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, dep);


            switch (properties.particlesCollisionMode)
            {
                case ParticlesCollisionMode.CompactHash:
                    nativeData.m_particleHashes.Clear();
                    break;
                case ParticlesCollisionMode.CompactHashReverseSearch:
                    nativeData.m_particleHashes.Clear();
//                    nativeData.m_particle_Hashes.Clear();
                    break;
            }
            
            
            

#if UNITY_EDITOR
            
            if (debugDrawer.bDrawDebug)
            {
                var clearDebugHitJob = new ClearConstraintsArray<PBDCollisionHit>()
                {
                    UpdateList  = nativeData.m_UpdateList.AsParallelReader(),
                    Constraints = debugDrawer.__HitArray,
                };
                var clearDebugHitJobHandle = clearDebugHitJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, dep);
                
                return JobHandle.CombineDependencies(clearParticleJobHandle, clearRigiJobHandle, clearDebugHitJobHandle);
            }

#endif
            

            dep = JobHandle.CombineDependencies(clearParticleJobHandle, clearRigiJobHandle);
            
            return dep;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining), GenerateTestsForBurstCompatibility]
        JobHandle PrepareReadCollider(JobHandle dep, float deltaTime)
        {
            ref var properties = ref m_propertiesData;
            ref var nativeData = ref m_nativeData;

            var readColliderJob = new ReadRigibodyColliderTransformJob()
            {
                collider = nativeData.m_rigibodyColliders,
                DeltaTimeInv = 1 / deltaTime,
            };

            dep = readColliderJob.ScheduleReadOnlyByRef(nativeData.m_rigibodyColliderTrasnforms, 16, dep);

            
            //
            if (m_UpdateScenceBounds)
            {
                m_UpdateScenceBounds = false;

                var updateSceneBounds = new UpdateSenceBounds()
                {
                    Colliders   = nativeData.m_rigibodyColliders.AsParallelReader(),
                    SceneBounds = nativeData.m_sceneBounds,
                };
                dep = updateSceneBounds.ScheduleByRef(dep);

                switch (properties.rigibodyFilterMode)
                {
                    case RigibodyFilterMode.SimpleAABB:
                        break;
                    case RigibodyFilterMode.Voxel:
#if UNITY_EDITOR
                        debugDrawer.__Voxel.Clear();
#endif
                        nativeData.m_rigibodyHashes.Clear();
                        
                        var clearRigiHashMapJob = new ClearHashMapJob<HashRange>()
                        {
                            hashRanges = nativeData.m_rigibodyHashRanges,
                        };

                        var clearRigiHashJobHandle = clearRigiHashMapJob.ScheduleByRef(dep);

                        var rigibodySpatiaHashJob = new RigibodySpatiaHashingJob()
                        {
                            Colliders          = nativeData.m_rigibodyColliders.AsParallelReader(),
                            RigidBodyHashes    = nativeData.m_rigibodyHashes,
                            cellRadius         = properties.rigiVoxelSize,
                            bucketCapacityMask = properties.RigiVoxelBucketMask,
#if UNITY_EDITOR
                            VoxelDebug = debugDrawer.__Voxel,
                            Debug      = debugDrawer.voxel,
#endif
                        };

                        var sortJob = nativeData.m_rigibodyHashes.SortJobDeferByRef(new HashComparer());

                        var buildLookupJob = new BuildGridLookup()
                        {
                            SortedHashes = nativeData.m_rigibodyHashes.AsDeferredJobArray(),
                            HashRanges   = nativeData.m_rigibodyHashRanges,
                        };


                        var rigibodySpatiaJobHandle = rigibodySpatiaHashJob.ScheduleByRef(clearRigiHashJobHandle);

                        var sortJobHandle = sortJob.ScheduleDeferredByRef(rigibodySpatiaJobHandle);
                        dep = buildLookupJob.ScheduleByRef(sortJobHandle);

                        break;
                    case RigibodyFilterMode.Octree:

                        var buildRigibodyOctJob = new RigidbodyOctree.BuildRigidBodyOctreeJob()
                        {
                            Colliders               = nativeData.m_rigibodyColliders.AsParallelReader(),
                            SceneBounds             = nativeData.m_sceneBounds.AsReadOnly(),
                            Octree                  = nativeData.m_RigibodyOctree,
                            OctreeSplitThresholdNum = properties.octSplitThresholdNums,
                            MaxDepth                = properties.octMaxDepth,
                        };

                        nativeData.m_RigibodyOctree.Clear();
                        dep = buildRigibodyOctJob.ScheduleByRef(dep);

                        break;
                }
            }

            return dep;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining), GenerateTestsForBurstCompatibility]
        JobHandle PreparReadForce(JobHandle dep, float deltaTime)
        {
            ref var nativeData = ref m_nativeData;

            var readPreForceTrasnformJob = new ReadForceTransformJob()
            {
                ForceFields = nativeData.m_preForceFields,
                DeltaTime   = deltaTime,
            };

            var readForceTrasnformJob = new ReadForceTransformJob()
            {
                ForceFields = nativeData.m_forceFields,
                DeltaTime   = deltaTime,
            };

            var readPostForceTrasnformJob = new ReadForceTransformJob()
            {
                ForceFields = nativeData.m_postForceFields,
                DeltaTime   = deltaTime,
            };

            var readPreForceJobHandle  = readPreForceTrasnformJob.ScheduleReadOnlyByRef(nativeData.m_preForceTransforms, 8, dep);
            var readForceJobHandle     = readForceTrasnformJob.ScheduleReadOnlyByRef(nativeData.m_forceTransforms, 8, dep);
            var readPostForceJobHandle = readPostForceTrasnformJob.ScheduleReadOnlyByRef(nativeData.m_postForceTransforms, 8, dep);

            return JobHandle.CombineDependencies(readPreForceJobHandle, readForceJobHandle, readPostForceJobHandle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining), GenerateTestsForBurstCompatibility]
        JobHandle InitExtDistanceBending(JobHandle dep, float deltaTime)
        {
            ref var properties = ref m_propertiesData;
            ref var nativeData = ref m_nativeData;

            var initJob = new InitializeJob()
            {
                UpdateList         = nativeData.m_UpdateList.AsParallelReader(),
                Positions          = nativeData.m_positions,
                PredictedPositions = nativeData.m_predictedPositions.AsReadOnly(),
            };

            var distanceJob = new DistanceConstraintJob()
            {
                UpdateList          = nativeData.m_UpdateList.AsParallelReader(),
                PredictedPositions  = nativeData.m_predictedPositions,
                InvMasses           = nativeData.m_invmasses.AsReadOnly(),
                DistanceConstraints = nativeData.m_distanceConstraints.AsReadOnly(),
                DisContraintIndexes = nativeData.m_disContraintIndexes.AsReadOnly(),
                ComppressStiffness  = properties.m_CompressStiffness,
                StretchStiffness    = properties.m_StretchStiffness,
            };

            var bendJob = new BendConstraintJob()
            {
                UpdateList         = nativeData.m_UpdateList.AsParallelReader(),
                PredictedPositions = nativeData.m_predictedPositions,
                InvMasses          = nativeData.m_invmasses.AsReadOnly(),
                BendConstraints    = nativeData.m_bendConstraints.AsReadOnly(),
                BendStiffness      = properties.m_BendStiffness,

#if UNITY_EDITOR
                DebugArray = debugDrawer.__BendDirArray,
#endif
            };

            dep = initJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, dep);

            switch (properties.extForceMode)
            {
                case ExtForceMode.Simple:
                {
                    var extJob = new ExtForceJob()
                    {
                        UpdateList         = nativeData.m_UpdateList.AsParallelReader(),
                        PredictedPositions = nativeData.m_predictedPositions,
                        Velocities         = nativeData.m_velocities,
                        Normals            = nativeData.m_normals.AsReadOnly(),
                        InvMasses          = nativeData.m_invmasses.AsReadOnly(),
                        Areas              = nativeData.m_areas.AsReadOnly(),
                        ForceFields        = nativeData.m_forceFields.AsParallelReader(),
                        PostForceFields    = nativeData.m_postForceFields.AsParallelReader(),
                        damping            = properties.m_Damping,
                        wind               = properties.m_WindFroce,
                        gravity            = properties.m_Gravity,
                        deltaTime          = deltaTime,
                    };

                    dep = extJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, dep);
                }
                    break;
                
                case ExtForceMode.PreFilter:
                {
                    var extPreDynamicForceJob = new ExtPreDynamicForceJob()
                    {
                        UpdateList = nativeData.m_UpdateList.AsParallelReader(),
                        Velocities = nativeData.m_velocities.AsReadOnly(),
                        Normals    = nativeData.m_normals.AsReadOnly(),
                        Areas      = nativeData.m_areas.AsReadOnly(),
                        ExtForces  = nativeData.m_extForces,
                        wind       = properties.m_WindFroce,
                    };

                    var extDynamicForeceJob = new ExtDynamicForceJob()
                    {
                        ExtForceList       = nativeData.m_ExtDynamicForceList.AsParallelReader(),
                        PredictedPositions = nativeData.m_predictedPositions.AsReadOnly(),
                        Velocities         = nativeData.m_velocities.AsReadOnly(),
                        ForceFields        = nativeData.m_forceFields.AsParallelReader(),
                        ExtForces          = nativeData.m_extForces,
                    };

                    var extVelocityJob = new ExtVelocityJob()
                    {
                        UpdateList = nativeData.m_UpdateList.AsParallelReader(),
                        Velocities = nativeData.m_velocities,
                        InvMasses  = nativeData.m_invmasses.AsReadOnly(),
                        ExtForces  = nativeData.m_extForces.AsReadOnly(),
                        gravity    = properties.m_Gravity,
                        damping    = properties.m_Damping,
                        deltaTime  = deltaTime,
                    };

                    var extPostDynamicForceJob = new ExtPostDynamicForceJob()
                    {
                        ExtForceList       = nativeData.m_ExtPostDynamicForceList.AsParallelReader(),
                        PredictedPositions = nativeData.m_predictedPositions.AsReadOnly(),
                        Velocities         = nativeData.m_velocities,
                        PostForceFields    = nativeData.m_postForceFields.AsParallelReader(),
                    };

                    var extPredectedUpdateJob = new ExtPredictedUpdateJob()
                    {
                        UpdateList         = nativeData.m_UpdateList.AsParallelReader(),
                        PredictedPositions = nativeData.m_predictedPositions,
                        Velocities         = nativeData.m_velocities.AsReadOnly(),
                        deltaTime          = deltaTime,
                    };

                    dep = For.ScheduleByRef(ref extPreDynamicForceJob, m_updateCount, m_updateQuadBatchCount, dep);
                    dep = For.ScheduleByRef(ref extDynamicForeceJob, m_extDynamicCount, m_extDynamicBatchCount, dep);
                    dep = For.ScheduleByRef(ref extVelocityJob, m_updateCount, m_updateQuadBatchCount, dep);
                    dep = For.ScheduleByRef(ref extPostDynamicForceJob, m_extPostDynamicCount, m_extPostDynamicBatchCount, dep);
                    dep = For.ScheduleByRef(ref extPredectedUpdateJob, m_updateCount, m_updateQuadBatchCount, dep);
                }
                    break;
                case ExtForceMode.WindField:
                    // unsafe
                    {
                        if (m_windField.IsCreated)
                        {

                            var extJob = new ExtWindFieldJob()
                            {
                                UpdateList         = nativeData.m_UpdateList.AsParallelReader(),
                                PredictedPositions = nativeData.m_predictedPositions,
                                Velocities         = nativeData.m_velocities,
                                Normals            = nativeData.m_normals.AsReadOnly(),
                                InvMasses          = nativeData.m_invmasses.AsReadOnly(),
                                Areas              = nativeData.m_areas.AsReadOnly(),
                                WindFieldX         = m_windField.Exchange_VelocityX.AsReadOnly(),
                                WindFieldY         = m_windField.Exchange_VelocityY.AsReadOnly(),
                                WindFieldZ         = m_windField.Exchange_VelocityZ.AsReadOnly(),
                                PostForceFields    = nativeData.m_postForceFields.AsParallelReader(),
                                damping            = properties.m_Damping,
                                wind               = properties.m_WindFroce,
                                windFieldBounds    = m_windField.WindFieldBounds,
                                windFieldOri       = m_windField.WindFieldOri,
                                windFieldMoveDelta = m_windField.windFieldMoveDelta,
                                dim                = m_windField.Dim,
                                N                  = m_windField.PublicN,
                                gravity            = properties.m_Gravity,
                                deltaTime          = deltaTime,
                            };

                            dep = extJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, dep);
                        }
                    }
                    break;
            }

            dep = distanceJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, dep);
            dep = bendJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, dep);

            return dep;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining), GenerateTestsForBurstCompatibility]
        JobHandle Particle2ParticleCollision(JobHandle dep)
        {
            ref var properties = ref m_propertiesData;
            ref var nativeData = ref m_nativeData;

            bool byQuad = properties.collisionByQuad;

            var addDeltaJob = new AddParticleCollisionConstraintToPosition()
            {
                UpdateList                   = nativeData.m_UpdateList.AsParallelReader(),
                PredictedPositions           = nativeData.m_predictedPositions,
                ParticleCollisionConstraints = nativeData.m_particleCollisionConstraints,
            };

            var caculateQuadVelocityJob = new ReCaculateQuadVelocityJob()
            {
                UpdateList     = nativeData.m_UpdateList.AsParallelReader(),
                Velocities     = nativeData.m_velocities.AsReadOnly(),
                QuadVelocities = nativeData.m_quadVelocity,
            };

            var caculateQuadPosJob = new ReCaculateQuadPosition()
            {
                UpdateList             = nativeData.m_UpdateList.AsParallelReader(),
                PredictedPositions     = nativeData.m_predictedPositions.AsReadOnly(),
                QuadPredictedPositions = nativeData.m_quadPredictedPosition,
            };

            switch (properties.particlesCollisionMode)
            {
                case ParticlesCollisionMode.GridBasedHash:
                {
                    var buildCollisionHashJob = new BuildHashNeighbours()
                    {
                        UpdateList             = nativeData.m_UpdateList.AsParallelReader(),
                        PredictedPositions     = nativeData.m_predictedPositions.AsReadOnly(),
                        QuadPredictedPositions = nativeData.m_quadPredictedPosition,
                        hashMap                = nativeData.m_collisionNeighours.AsParallelWriter(),
                        neighborOffsets        = nativeData.m_neighborOffsets.AsReadOnly(),
                        filterParams           = properties.P_FilterParams,
                        cellRadius             = properties.P_CellRadius,
                        bucketCapacityMask     = properties.HashBucketCapacityMask,
                        collisionByQuad        = byQuad,
                    };

                    var p2pcollisionJob = new InterParticlesCollisions()
                    {
                        UpdateList                   = nativeData.m_UpdateList.AsParallelReader(),
                        PredictedPositions           = nativeData.m_predictedPositions.AsReadOnly(),
                        InvMasses                    = nativeData.m_invmasses.AsReadOnly(),
                        QuadPredictedPositions       = nativeData.m_quadPredictedPosition,
                        QuadInvMasses                = nativeData.m_quadInvMass.AsReadOnly(),
                        ParticleCollisionConstraints = nativeData.m_particleCollisionConstraints,
                        hashMap                      = nativeData.m_collisionNeighours.AsReadOnly(),
                        filterParams                 = properties.P_FilterParams,
                        radius                       = properties.P_ParticlesRadius,
                        cellRadius                   = properties.P_CellRadius,
                        CollisionStiffness           = properties.m_CollisionStiffness,
                        bucketCapacityMask           = properties.HashBucketCapacityMask,
                        collisionByQuad              = byQuad,
                    };

                    var clearHashNeighboursJob = new ClearHashNeighbours()
                    {
                        hashMap = m_nativeData.m_collisionNeighours,
                    };

                    if (byQuad)
                        dep = caculateQuadPosJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, dep);

                    dep = buildCollisionHashJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, dep);
                    dep = p2pcollisionJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, dep);

                    _clearHashMapJobHandle = clearHashNeighboursJob.ScheduleByRef(dep);

                    dep = addDeltaJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, dep);
                    break;
                }
                
                case ParticlesCollisionMode.CompactHash:
                {
                    var buildHashArrayJob = new CalculateParticleHashesJob()
                    {
                        UpdateList             = nativeData.m_UpdateList.AsParallelReader(),
                        PredictedPositions     = nativeData.m_predictedPositions.AsReadOnly(),
                        QuadPredictedPositions = nativeData.m_quadPredictedPosition.AsReadOnly(),
                        ParticleHashes         = nativeData.m_particleHashes,
                        filterParams           = properties.P_FilterParams,
                        cellRadius             = properties.P_CellRadius,
                        bucketCapacityMask     = properties.HashBucketCapacityMask,
                        collisionByQuad        = byQuad,
#if UNITY_EDITOR
                        debugCounter           = debugDrawer.__HashCounter,
#endif
                    };
                    
                    var sortJob = nativeData.m_particleHashes.SortJobDeferByRef(new HashComparer());

                    var buildHashRangeJob = new BuildGridLookup()
                    {
                        SortedHashes = nativeData.m_particleHashes.AsDeferredJobArray(),
                        HashRanges   = nativeData.m_particleHashRanges,
                    };
                    var p2pcollisionSPHJob = new SPHOptimizedCollisionDetectionJob()
                    {
                        UpdateList                   = nativeData.m_UpdateList.AsParallelReader(),
                        PredictedPositions           = nativeData.m_predictedPositions.AsReadOnly(),
                        Velocities                   = nativeData.m_velocities.AsReadOnly(),
                        InvMasses                    = nativeData.m_invmasses.AsReadOnly(),
                        QuadPredictedPositions       = nativeData.m_quadPredictedPosition,
                        QuadVelocities               = nativeData.m_quadVelocity.AsReadOnly(),
                        QuadInvMasses                = nativeData.m_quadInvMass.AsReadOnly(),
                        ParticleCollisionConstraints = nativeData.m_particleCollisionConstraints,
                        SortedHashes                 = nativeData.m_particleHashes.AsDeferredJobArray(),
                        hashRanges                   = nativeData.m_particleHashRanges.AsReadOnly(),
                        neighborOffsets              = nativeData.m_neighborOffsets.AsReadOnly(),
                        filterParams                 = properties.P_FilterParams,
                        radius                       = properties.P_ParticlesRadius,
                        cellRadius                   = properties.P_CellRadius,
                        CollisionStiffness           = properties.m_CollisionStiffness,
                        bucketCapacityMask           = properties.HashBucketCapacityMask,
                        collisionByQuad              = byQuad,
                    };
                    
                    var clearHashMap = new ClearHashMapJob<HashRange>()
                    {
                        hashRanges = nativeData.m_particleHashRanges,
                    };

                    JobHandle caculateQuadPosJobHandle      = dep,
                              caculateQuadVelocityJobHandle = dep;
                    if (byQuad)
                    {
                        caculateQuadPosJobHandle      = caculateQuadPosJob.ScheduleByRef( m_updateCount, m_updateQuadBatchCount, caculateQuadPosJobHandle);
                        caculateQuadVelocityJobHandle = caculateQuadVelocityJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, caculateQuadVelocityJobHandle);
                    }

                    if (byQuad)
                        dep = caculateQuadPosJobHandle;

                    // dep = properties.DesignJobThreadNum > 4 ? 
                    //     buildHashArrayJob.ScheduleByRef(dep) : 
                    //     buildHashArrayJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, dep);
                    dep = buildHashArrayJob.ScheduleByRef(dep);
                    
                    dep = sortJob.ScheduleDeferredByRef(dep, threadNum: properties.SortSegmentCount);
                    dep = buildHashRangeJob.ScheduleByRef(dep);

                    if (byQuad)
                        dep = JobHandle.CombineDependencies(dep, caculateQuadVelocityJobHandle);

                    var particleCollisionJobHandle = p2pcollisionSPHJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, dep);
                    dep = addDeltaJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, particleCollisionJobHandle);

                    _clearHashMapJobHandleSPH = clearHashMap.ScheduleByRef(particleCollisionJobHandle);
                    break;
                }
                
                case ParticlesCollisionMode.CompactHashReverseSearch:
                {
                    var buildHashArrayJob = new CalculateParticleHashesJob()
                    {
                        UpdateList             = nativeData.m_UpdateList.AsParallelReader(),
                        PredictedPositions     = nativeData.m_predictedPositions.AsReadOnly(),
                        QuadPredictedPositions = nativeData.m_quadPredictedPosition.AsReadOnly(),
                        ParticleHashes         = nativeData.m_particleHashes,
                        filterParams           = properties.P_FilterParams,
                        cellRadius             = properties.P_CellRadius,
                        bucketCapacityMask     = properties.HashBucketCapacityMask,
                        collisionByQuad        = byQuad,
#if UNITY_EDITOR
                        debugCounter           = debugDrawer.__HashCounter,
#endif
                    };
                    
                    var sortJob = nativeData.m_particleHashes.SortJobDeferByRef(new HashComparer());
                    
                    var buildHashRangeJob = new BuildGridLookupArray()
                    {
                        SortedHashes = nativeData.m_particleHashes.AsDeferredJobArray(),
//                        HashRanges   = nativeData.m_particleHashRanges,
                        HashRanges   = nativeData.m_particleSimpleHashRanges,
                        Hash         = nativeData.m_particle_Hashes,
                    };

                    var p2pcollisionSPHJob = new SPHOptimizedCollisionDetectionReverseSearchJob()
                    {
                        UpdateList                   = nativeData.m_UpdateList.AsParallelReader(),
                        PredictedPositions           = nativeData.m_predictedPositions.AsReadOnly(),
                        Velocities                   = nativeData.m_velocities.AsReadOnly(),
                        InvMasses                    = nativeData.m_invmasses.AsReadOnly(),
                        QuadPredictedPositions       = nativeData.m_quadPredictedPosition,
                        QuadVelocities               = nativeData.m_quadVelocity.AsReadOnly(),
                        QuadInvMasses                = nativeData.m_quadInvMass.AsReadOnly(),
                        ParticleCollisionConstraints = nativeData.m_particleCollisionConstraints,
                        SortedHashes                 = nativeData.m_particleHashes.AsDeferredJobArray(),
                        Hashes                       = nativeData.m_particle_Hashes.AsDeferredJobArray(),
//                        HashRanges                   = nativeData.m_particleHashRanges.AsReadOnly(),
                        HashRanges         = nativeData.m_particleSimpleHashRanges,
                        neighborOffsets    = nativeData.m_neighborOffsets.AsReadOnly(),
                        filterParams       = properties.P_FilterParams,
                        radius             = properties.P_ParticlesRadius,
                        cellRadius         = properties.P_CellRadius,
                        CollisionStiffness = properties.m_CollisionStiffness,
                        JobNum             = properties.DesignJobThreadNum,
                        bucketCapacityMask = properties.HashBucketCapacityMask,
                        collisionByQuad    = byQuad,
                    };
                    
                    
//                    var clearHashRanges = new ClearHashMapJob<HashRange>()
//                    {
//                        hashRanges = nativeData.m_particleHashRanges,
//                    };
                    var clearHashRanges = new ClearSimpleHashMapJob<HashRange>()
                    {
                        hashRanges = nativeData.m_particleSimpleHashRanges,
                    };

                    JobHandle caculateQuadPosJobHandle      = dep,
                              caculateQuadVelocityJobHandle = dep;
                    if (byQuad)
                    {
                        caculateQuadPosJobHandle      = caculateQuadPosJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, caculateQuadPosJobHandle);
                        caculateQuadVelocityJobHandle = caculateQuadVelocityJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, caculateQuadVelocityJobHandle);
                    }

                    if (byQuad)
                        dep = caculateQuadPosJobHandle;

                    // dep = properties.DesignJobThreadNum > 4 ? 
                    //     buildHashArrayJob.ScheduleByRef(dep) : 
                    //     buildHashArrayJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, dep);
                    dep = buildHashArrayJob.ScheduleByRef(dep);
                    
                    dep = sortJob.ScheduleDeferredByRef(dep, threadNum: properties.SortSegmentCount);
                    dep = buildHashRangeJob.ScheduleByRef(dep);
                    
                    if (byQuad)
                        dep = JobHandle.CombineDependencies(dep, caculateQuadVelocityJobHandle);

                    var particleCollisionJobHandle = p2pcollisionSPHJob.ScheduleByRef(properties.DesignJobThreadNum, 1, dep);
                    dep = addDeltaJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, particleCollisionJobHandle);

                    _clearHashMapJobHandleSPH = clearHashRanges.ScheduleByRef(particleCollisionJobHandle);
                    break;
                }
            }

            return dep;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining), GenerateTestsForBurstCompatibility]
        JobHandle Particle2RigibodyCollision(JobHandle dep)
        {
            ref var properties = ref m_propertiesData;
            ref var nativeData = ref m_nativeData;

            var caculateQuadPosJob = new ReCaculateQuadPosition()
            {
                UpdateList             = nativeData.m_UpdateList.AsParallelReader(),
                PredictedPositions     = nativeData.m_predictedPositions.AsReadOnly(),
                QuadPredictedPositions = nativeData.m_quadPredictedPosition,
            };

            dep = caculateQuadPosJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, dep);

            switch (properties.rigibodyFilterMode)
            {
                case RigibodyFilterMode.SimpleAABB:
                    var p2RigiCollisionJob = new RigibodyCollisionJob()
                    {
                        UpdateList               = nativeData.m_UpdateList.AsParallelReader(),
                        Colliders                = nativeData.m_rigibodyColliders.AsParallelReader(),
                        PredictedPositions       = nativeData.m_predictedPositions.AsReadOnly(),
                        Radius                   = nativeData.m_radius.AsReadOnly(),
                        Velocities               = nativeData.m_velocities.AsReadOnly(),
                        QuadPredictedPositions   = nativeData.m_quadPredictedPosition.AsReadOnly(),
                        RigiCollisionConstraints = nativeData.m_rigiCollisionConstraints,
                        SceneBounds              = nativeData.m_sceneBounds.AsReadOnly(),
                        QuadRadius               = properties.R_QuadRadius,
                        Elasticity               = properties.m_Elasticity,
                        Friction                 = properties.m_Friction,
#if UNITY_EDITOR
                        DebugArray = debugDrawer.__HitArray,
#endif
                    };
                    dep = p2RigiCollisionJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, dep);
                    break;

                case RigibodyFilterMode.Voxel:
                    var rigiCollisionJob = new SPHRigibody2ParticleHashearchJob()
                    {
                        UpdateList               = nativeData.m_UpdateList.AsParallelReader(),
                        Colliders                = nativeData.m_rigibodyColliders.AsParallelReader(),
                        PredictedPositions       = nativeData.m_predictedPositions.AsReadOnly(),
                        Radius                   = nativeData.m_radius.AsReadOnly(),
                        Velocities               = nativeData.m_velocities.AsReadOnly(),
                        QuadPredictedPositions   = nativeData.m_quadPredictedPosition.AsReadOnly(),
                        RigiCollisionConstraints = nativeData.m_rigiCollisionConstraints,
                        RigidBodyHashes          = nativeData.m_rigibodyHashes.AsDeferredJobArray(),
                        RigidBodyHashRanges      = nativeData.m_rigibodyHashRanges.AsReadOnly(),
                        neighborOffsets          = nativeData.m_neighborOffsets.AsReadOnly(),
                        SceneBounds              = nativeData.m_sceneBounds.AsReadOnly(),
                        QuadRadius               = properties.R_QuadRadius,
                        Friction                 = properties.m_Friction,
                        Elasticity               = properties.m_Elasticity,
                        cellRadius               = properties.rigiVoxelSize,
                        bucketCapacityMask       = properties.RigiVoxelBucketMask,
#if UNITY_EDITOR
                        DebugArray               = debugDrawer.__HitArray,
#endif
                    };

                    dep = rigiCollisionJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, dep);

                    break;

                case RigibodyFilterMode.Octree:

                    var octRigibodyCollision = new OctreeRigidbodyCollisionJob()
                    {
                        UpdateList               = nativeData.m_UpdateList.AsParallelReader(),
                        Colliders                = nativeData.m_rigibodyColliders.AsParallelReader(),
                        PredictedPositions       = nativeData.m_predictedPositions.AsReadOnly(),
                        Radius                   = nativeData.m_radius.AsReadOnly(),
                        Velocities               = nativeData.m_velocities.AsReadOnly(),
                        QuadPredictedPositions   = nativeData.m_quadPredictedPosition.AsReadOnly(),
                        RigiCollisionConstraints = nativeData.m_rigiCollisionConstraints,
                        SceneBounds              = nativeData.m_sceneBounds.AsReadOnly(),
                        RigidbodyOctree          = nativeData.m_RigibodyOctree,
                        QuadRadius               = properties.R_QuadRadius,
                        Friction                 = properties.m_Friction,
                        Elasticity               = properties.m_Elasticity,
#if UNITY_EDITOR
                        DebugArray = debugDrawer.__HitArray,
#endif
                    };
                    
                    dep = octRigibodyCollision.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, dep);

                    break;
            }

            var addDeltaByDivi = new AddRigiCollisionConstraintToPositionByDivi()
            {
                UpdateList               = nativeData.m_UpdateList.AsParallelReader(),
                PredictedPositions       = nativeData.m_predictedPositions,
                RigiCollisionConstraints = nativeData.m_rigiCollisionConstraints,
                Velocities               = nativeData.m_velocities,
                IsNeedUpdates            = nativeData.m_isNeedUpdates,
                Threshold                = properties.StaticVelocityThreshold,
            };

            dep = addDeltaByDivi.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, dep);

            return dep;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining), GenerateTestsForBurstCompatibility]
        JobHandle UpdateDataCombine(JobHandle dep, float deltaTime)
        {
            ref var nativeData = ref m_nativeData;
            ref var properties = ref m_propertiesData;

            var updateVelocityJob = new LeavesUpdateJobs.UpdateVelocityJob()
            {
                UpdateList         = nativeData.m_UpdateList.AsParallelReader(),
                PredictedPositions = nativeData.m_predictedPositions.AsReadOnly(),
                Positions          = nativeData.m_positions.AsReadOnly(),
                Velocities         = nativeData.m_velocities,
                InvDeltaTime       = 1f / deltaTime,
            };
            var updateNormalAreaJob = new LeavesUpdateJobs.UpdateNormalAreaJob()
            {
                UpdateList         = nativeData.m_UpdateList.AsParallelReader(),
                PredictedPositions = nativeData.m_predictedPositions.AsReadOnly(),
                Normals            = nativeData.m_normals,
                Areas              = nativeData.m_areas,
            };

            var updateNormalAreaJobHandle = updateNormalAreaJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, dep);

            var updateVelocityJobHandle = updateVelocityJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, dep);

            if (properties.IsFrustumCullingOn)
            {
                var caculateInFrustumJob = new CaculateInFrustumJob
                {
                    PredictedPositions = nativeData.m_predictedPositions.AsReadOnly(),
                    IsNeedRenders      = nativeData.m_isNeedRenders,
                    CullingMatrix      = properties.focusCamera.cullingMatrix,
                };
                var collectInFrustumJob = new InFrustumFilterJob()
                {
                    IsNeedRenders = nativeData.m_isNeedRenders.AsReadOnly(),
                };

                int threadNum = properties.DesignJobThreadNum;

                var updateMeshPosJob = new LeavesUpdateJobs.UpdateFrustumMeshPosJob()
                {
                    RenderList         = nativeData.m_RenderList.AsDeferredJobArray(),
                    PredictedPositions = nativeData.m_predictedPositions.AsReadOnly(),
                    pos                = nativeData.verticesArray,
                    JobNum             = threadNum,
                };

                var caculateInFrustumHandle = caculateInFrustumJob.ScheduleByRef(m_quadCount, m_quadBatchCount, dep);

                var collectRenderHandle = collectInFrustumJob.ScheduleOverwriteByRef(nativeData.m_RenderList, m_quadCount, caculateInFrustumHandle);

                var canUpdateMeshNormal = JobHandle.CombineDependencies(collectRenderHandle, updateNormalAreaJobHandle);

                var updateMeshPosJobHandle = updateMeshPosJob.ScheduleByRef(threadNum, 1, collectRenderHandle);

                JobHandle combine = default;
                switch (properties.uvNormalFormat)
                {
                    case MeshUvNormalFormat.UNorm16x2:
                    {
                        var updateMeshNormalJob = new LeavesUpdateJobs.UpdateFrustumMeshNormalUNorm16Job()
                        {
                            RenderList = nativeData.m_RenderList.AsDeferredJobArray(),
                            Normals    = nativeData.m_normals.AsReadOnly(),
                            // normal = nativeData.normalForRendering,
                            normal = nativeData.normalArray,
                            JobNum = threadNum,
                        };

                        var updateMeshUvJob = new LeavesUpdateJobs.UpdateFrustumMeshUVUNorm16Job()
                        {
                            RenderList      = nativeData.m_RenderList.AsDeferredJobArray(),
                            uvs             = nativeData.meshUVsArray.AsReadOnly(),
                            uvsForRendering = nativeData.uvsForRendering,
                            JobNum          = threadNum,
                        };
                        var updateMeshNormalJobHandle = updateMeshNormalJob.ScheduleByRef(threadNum, 1, canUpdateMeshNormal);
                        var updateMeshUvJobHandle     = updateMeshUvJob.ScheduleByRef(threadNum, 1, collectRenderHandle);

                        combine = JobHandle.CombineDependencies(updateMeshPosJobHandle, updateMeshNormalJobHandle, updateMeshUvJobHandle);
                    }
                        break;
                    
                    case MeshUvNormalFormat.UNorm8x4:
                    {
                        var updateMeshUvNormalJob = new LeavesUpdateJobs.UpdateFrustumMeshUVNormalUNorm8Job()
                        {
                            RenderList              = nativeData.m_RenderList.AsDeferredJobArray(),
                            Normals                 = nativeData.m_normals.AsReadOnly(),
                            uvAndNormal             = nativeData.uvsAndNormal.AsReadOnly(),
                            uvAndNormalForRendering = nativeData.uvsAndNormalForRendering,
                            JobNum                  = threadNum,
                        };
                        var updateMeshUvNormalJobHandle = updateMeshUvNormalJob.ScheduleByRef(threadNum, 1, canUpdateMeshNormal);
                        
                        combine = JobHandle.CombineDependencies(updateMeshPosJobHandle, updateMeshUvNormalJobHandle);
                    }
                        break;
                }

                dep = JobHandle.CombineDependencies(updateVelocityJobHandle, combine);
            }
            else
            {
                var updateMeshPosJob = new LeavesUpdateJobs.UpdateMeshPosJob()
                {
                    UpdateList         = nativeData.m_UpdateList.AsParallelReader(),
                    PredictedPositions = nativeData.m_predictedPositions.AsReadOnly(),
                    pos                = nativeData.verticesArray,
                };
                var updateMeshPosJobHandle    = updateMeshPosJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, dep);

                switch (properties.uvNormalFormat)
                {
                    case MeshUvNormalFormat.UNorm16x2:
                
                        var updateMeshNormalJob = new LeavesUpdateJobs.UpdateMeshNormalUNorm16Job()
                        {
                            UpdateList = nativeData.m_UpdateList.AsParallelReader(),
                            Normals    = nativeData.m_normals.AsReadOnly(),
                            normal     = nativeData.normalArray,
                        };
                        var updateMeshNormalJobHandle = updateMeshNormalJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, updateNormalAreaJobHandle);

                        dep = JobHandle.CombineDependencies(updateMeshPosJobHandle, updateMeshNormalJobHandle, updateVelocityJobHandle);
                        
                        break;
                    
                    case MeshUvNormalFormat.UNorm8x4:

                        var updateMeshUvNormalJob = new LeavesUpdateJobs.UpdateMeshNormalUNorm8Job()
                        {
                            UpdateList  = nativeData.m_UpdateList.AsParallelReader(),
                            Normals     = nativeData.m_normals.AsReadOnly(),
                            uvAndNormal = nativeData.uvsAndNormal,
                        };
                        var updateMeshUvNormalJobHandle = updateMeshUvNormalJob.ScheduleByRef(m_updateCount, m_updateQuadBatchCount, updateNormalAreaJobHandle);
                        
                        dep = JobHandle.CombineDependencies(updateMeshPosJobHandle, updateMeshUvNormalJobHandle, updateVelocityJobHandle);
                        
                        break;
                }
            }

            return dep;
        }


        //臃肿 不想改了
        [MethodImpl(MethodImplOptions.AggressiveInlining), GenerateTestsForBurstCompatibility]
        JobHandle SimulateEndClearAndCollect(JobHandle dep, float deltaTime)
        {
            ref var nativeData     = ref m_nativeData;
            ref var propertiesData = ref m_propertiesData;

            //end for clear and collect


            var collectNeedUpdateJob = new UpdateQuadFilter()
            {
                IsNeedUpdates = nativeData.m_isNeedUpdates.AsReadOnly(),
            };

            switch (propertiesData.extForceMode)
            {
                case ExtForceMode.Simple:
                {
                    var extSetUpdateJob = new ExtForceSetUpdateJob()
                    {
                        StaticList             = nativeData.m_StaticList.AsParallelReader(),
                        QuadPredictedPositions = nativeData.m_quadPredictedPosition.AsReadOnly(),
                        QuadInvMasses          = nativeData.m_quadInvMass.AsReadOnly(),
                        ForceFields            = nativeData.m_forceFields.AsParallelReader(),
                        // PostForceFields = nativeData.m_postForceFields.AsParallelReader(),
                        IsNeedUpdates = nativeData.m_isNeedUpdates,
                    };


                    var collectStaticJob = new StaticQuadFilter()
                    {
                        IsNeedUpdates = nativeData.m_isNeedUpdates.AsReadOnly(),
                    };

                    var creatNewQuadJobHandle = CreatNewQuad(dep, deltaTime); // <----------Creat

                    var extSetUpdateJobHandle = extSetUpdateJob.ScheduleByRef(m_staticCount, m_staticQuadBatchCount, creatNewQuadJobHandle);

                    var collectStaticJobHandle = collectStaticJob.ScheduleOverwriteByRef(nativeData.m_StaticList, m_quadCount, extSetUpdateJobHandle);

                    var collectNeedUpdateJobHandle = collectNeedUpdateJob.ScheduleOverwriteByRef(nativeData.m_UpdateList, m_quadCount, extSetUpdateJobHandle);

                    dep = JobHandle.CombineDependencies(collectNeedUpdateJobHandle, collectStaticJobHandle);
                }
                    break;
                case ExtForceMode.PreFilter:
                {
                    var clearList = new ClearList<int>() { List = nativeData.m_ExtDynamicForceList };
                    var clearPostList = new ClearList<int>() { List = nativeData.m_ExtPostDynamicForceList };

                    var extDynamicSetUpdateJob = new ExtDynamicForceSetUpdateJob()
                    {
                        PredictedPositions     = nativeData.m_predictedPositions.AsReadOnly(),
                        QuadPredictedPositions = nativeData.m_quadPredictedPosition.AsReadOnly(),
                        ForceFields            = nativeData.m_forceFields.AsParallelReader(),
                        IsNeedUpdates          = nativeData.m_isNeedUpdates,
                        ExtDynamicForceList    = nativeData.m_ExtDynamicForceList.AsParallelWriter(),
                        QuadCount              = m_quadCount,
                        ByQuad                 = m_propertiesData.collisionByQuad,
                    };

                    var extPostDynamicSetUpdateJob = new ExtPostDynamicForceSetUpdateJob()
                    {
                        PredictedPositions      = nativeData.m_predictedPositions.AsReadOnly(),
                        QuadPredictedPositions  = nativeData.m_quadPredictedPosition.AsReadOnly(),
                        PostForceFields         = nativeData.m_postForceFields.AsParallelReader(),
                        ExtPostDynamicForceList = nativeData.m_ExtPostDynamicForceList.AsParallelWriter(),
                        QuadCount               = m_quadCount,
                        ByQuad                  = m_propertiesData.collisionByQuad,
                    };


                    var creatNewQuadJobHandle = CreatNewQuad(dep, deltaTime); // <----------Creat

                    var canCollect = JobHandle.CombineDependencies(creatNewQuadJobHandle, clearList.ScheduleByRef(dep), clearPostList.ScheduleByRef(dep));

                    var extSetUpdateJobHandle = extDynamicSetUpdateJob.ScheduleByRef(canCollect);

                    var extPostSetUpdateJobJobHandle = extPostDynamicSetUpdateJob.ScheduleByRef(canCollect);

                    var collectNeedUpdateJobHandle = collectNeedUpdateJob.ScheduleOverwriteByRef(nativeData.m_UpdateList, m_quadCount, extSetUpdateJobHandle);

                    dep = JobHandle.CombineDependencies(collectNeedUpdateJobHandle, extPostSetUpdateJobJobHandle);
                }
                    break;
                case ExtForceMode.WindField:
                    {
                        var extSetUpdateJob = new ExtWindFieldSetUpdateJob()
                        {
                            StaticList              = nativeData.m_StaticList.AsParallelReader(),
                            QuadPredictedPositions  = nativeData.m_quadPredictedPosition.AsReadOnly(),
                            QuadInvMasses           = nativeData.m_quadInvMass.AsReadOnly(),
                            WindFieldX              = m_windField.Exchange_VelocityX.AsReadOnly(),
                            WindFieldY              = m_windField.Exchange_VelocityY.AsReadOnly(),
                            WindFieldZ              = m_windField.Exchange_VelocityZ.AsReadOnly(),
                            IsNeedUpdates           = nativeData.m_isNeedUpdates,
                            windFieldBounds         = m_windField.WindFieldBounds,
                            windFieldOri            = m_windField.WindFieldOri,
                            windFieldMoveDelta      = m_windField.windFieldMoveDelta,
                            dim                     = m_windField.Dim,
                            N                       = m_windField.PublicN,
                            activeVelocityThreshold = m_windField.activeVelocityThreshold,
                        };


                        var collectStaticJob = new StaticQuadFilter()
                        {
                            IsNeedUpdates = nativeData.m_isNeedUpdates.AsReadOnly(),
                        };

                        var creatNewQuadJobHandle = CreatNewQuad(dep, deltaTime); // <----------Creat

                        var extSetUpdateJobHandle = extSetUpdateJob.ScheduleByRef(m_staticCount, m_staticQuadBatchCount, creatNewQuadJobHandle);

                        var collectStaticJobHandle = collectStaticJob.ScheduleOverwriteByRef(nativeData.m_StaticList, m_quadCount, extSetUpdateJobHandle);

                        var collectNeedUpdateJobHandle = collectNeedUpdateJob.ScheduleOverwriteByRef(nativeData.m_UpdateList, m_quadCount, extSetUpdateJobHandle);

                        dep = JobHandle.CombineDependencies(collectNeedUpdateJobHandle, collectStaticJobHandle);
                    }

                    break;
            }
            

            return dep;
        }

        public void AfterEarlyUpdate()
        {
        }

        private void FixedUpdate()
        {
            if (physicsUpdateMode == PhysicsUpdateMode.FixedTimeFrameRate)
            {
                m_deltaTime = Time.fixedDeltaTime;
                SchedulePhysics(m_deltaTime);
            }
        }

        public void AfterFixedUpdate()
        {
        }

        public void AfterUpdate()
        {
        }

        public void BeforeLateUpdate()
        {
        }

        public void LateUpdate()
        {
        }

        public void AfterLateUpdate()
        {
        }

        public void PostLateUpdate()
        {
        }

        private float m_Accumulator;

        public void AfterRendering()
        {
            switch (physicsUpdateMode)
            {
                case PhysicsUpdateMode.RealTimeFrameRate:
                    m_deltaTime = Time.deltaTime;
                    SchedulePhysics(m_deltaTime);
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void SchedulePhysics(float deltaTime)
        {
            _lastJobHandle.Complete();
            //-------主线程可以读写job数据的空间


            ref var properties = ref m_propertiesData;
            ref var nativeData = ref m_nativeData;

            int threadNum = m_propertiesData.DesignJobThreadNum;

            m_quadBatchCount = (m_quadCount + threadNum - 1) / threadNum;

            m_updateCount          = nativeData.m_UpdateList.Length;
            m_updateQuadBatchCount = (m_updateCount + threadNum - 1) / threadNum;

            if (properties.frustumCulling)
                m_renderCount = nativeData.m_RenderList.Length;

            switch (properties.extForceMode)
            {
                case ExtForceMode.Simple:
                    m_staticCount          = nativeData.m_StaticList.Length;
                    m_staticQuadBatchCount = (m_staticCount + 1) / threadNum;
                    break;
                
                case ExtForceMode.PreFilter:
                        
                    m_extDynamicCount          = nativeData.m_ExtDynamicForceList.Length;
                    m_extPostDynamicBatchCount = (m_extDynamicCount + threadNum - 1) / threadNum;

                    m_extPostDynamicCount      = nativeData.m_ExtPostDynamicForceList.Length;
                    m_extPostDynamicBatchCount = (m_extPostDynamicCount + threadNum - 1) / threadNum;
                    break;
                
                case ExtForceMode.WindField:
                    m_staticCount          = nativeData.m_StaticList.Length;
                    m_staticQuadBatchCount = (m_staticCount + 1) / threadNum;
                    break;
            }

            nativeData.ProcessRigiBodyQueue();
            nativeData.ProcessForceQueue();
            ReflushMesh();

#if UNITY_EDITOR
            // Debug.LogFormat("hash = 0 count : {0}", debugDrawer.__HashCounter[0]);
#endif
            
            //------------
            ScheduleAllJob(deltaTime);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining), GenerateTestsForBurstCompatibility]
        void ScheduleAllJob(float deltaTime)
        {
            ref var properties = ref m_propertiesData;
            ref var nativeData = ref m_nativeData;
            
            if (m_updateCount > 0)
            {
                var prepareReadForceJobHandle = PreparReadForce(_lastJobHandle, deltaTime);

                var prepareReadHandle = PrepareReadCollider(_lastJobHandle, deltaTime);
                
                if(m_windField.IsCreated && properties.extForceMode == ExtForceMode.WindField)
                {
                    var canWriteToFront = JobHandle.CombineDependencies(prepareReadForceJobHandle, prepareReadHandle);
                    
                    m_windField.UpdateFieldParams();
                            
                    var windSimlateJobHandle = m_windField.Simulate(_lastJobHandle, deltaTime);
                
                    var writeSoureceJobhandle = m_windField.WriteToFrontBuffer(canWriteToFront, ref nativeData.m_forceFields, ref nativeData.m_rigibodyColliders);
                    
                    _windFiledJobHandle = JobHandle.CombineDependencies(writeSoureceJobhandle, windSimlateJobHandle);
                }

                var initExtDistanceBending = InitExtDistanceBending(prepareReadForceJobHandle, deltaTime);

                var prepareClearJobHandle = PrepareClear(_lastJobHandle);
                _lastJobHandle = JobHandle.CombineDependencies(initExtDistanceBending, prepareReadHandle, prepareClearJobHandle);

                // _lastJobHandle = JobHandle.CombineDependencies(/*clearRenderCounterJobHandle,*/ _lastJobHandle, prepareReadForceJobHandle);

                _lastJobHandle = JobHandle.CombineDependencies(_clearHashMapJobHandle, _lastJobHandle);


                _lastJobHandle = Particle2ParticleCollision(_lastJobHandle);

                _lastJobHandle = Particle2RigibodyCollision(_lastJobHandle);

                _lastJobHandle = UpdateDataCombine(_lastJobHandle, deltaTime);
            }

            _lastJobHandle = SimulateEndClearAndCollect(_lastJobHandle, deltaTime);

            //用了sortJob必须过一次compelete
            _lastJobHandle = JobHandle.CombineDependencies(_clearHashMapJobHandleSPH, _lastJobHandle, _windFiledJobHandle);


            if (m_windField.IsCreated && properties.extForceMode == ExtForceMode.WindField)
                _lastJobHandle = m_windField.SaveToBack(_lastJobHandle);//fron和back拆开写入
            
            JobHandle.ScheduleBatchedJobs();
        }

        JobHandle _clearHashMapJobHandle    = default; //这个clear耗时巨大
        JobHandle _clearHashMapJobHandleSPH = default;

        JobHandle _windFiledJobHandle = default;
    }
}