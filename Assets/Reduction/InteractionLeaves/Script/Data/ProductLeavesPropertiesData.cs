using System;
using Unity.Collections;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace UnityEngine.PBD
{
    public enum MeshUvNormalFormat
    {
        UNorm16x2,
        UNorm8x4,
    }

    public enum ExtForceMode
    {
        Simple,
        PreFilter,
        WindField,
    }
    
    public enum ParticlesCollisionMode
    {
        Off,
        GridBasedHash,
        CompactHash,
        CompactHashReverseSearch,
    }

    public enum RigibodyFilterMode
    {
        SimpleAABB,
        Voxel,
        Octree,
    }

    [Serializable]
    public class ProductLeavesPropertiesData
    {

        [Range(1, 20)] public int DesignJobThreadNum = 2;

        [Range(2, 20)] public int SortSegmentCount = 4;

        [_ReadOnlyInPlayMode, SerializeField] 
        private Vector2Int leavesTexSplit = new Vector2Int(2, 2);

        [_ReadOnlyInPlayMode] public IndexFormat indexFormat = IndexFormat.UInt16;

        [_ReadOnlyInPlayMode] public MeshUvNormalFormat uvNormalFormat = MeshUvNormalFormat.UNorm16x2;

        [_ReadOnlyInPlayMode, SerializeField, Range(1, int.MaxValue / 6)]
        private int maxLeavesCount = 1000;

        [Range(0, ushort.MaxValue / 6)] public float productNumPerSecond = 50;

        [_ExponentialSlider(1e-10f, 1e-1f, 4f, true)]
        public float staticVelocityThreshold = 5e-3f;

        [Header("Product Settings"), Space, _MinMaxSliderVector2(.01f, 10f), SerializeField]
        private Vector2 massRange = new Vector2(1f, 2f);

        [_MinMaxSliderVector2(-90, 90), Tooltip("quad对折角度范围"), SerializeField]
        private Vector2 foldAngleRange = new Vector2(-20, 20); //对折范围

        [_MinMaxSliderVector2(.01f, 0.5f), SerializeField]
        private Vector2 scaleRange = new Vector2(0.1f, 0.15f);

        [_MinMaxSliderVector2(-50, 50), SerializeField]
        private Vector2 areaX = new Vector2(-3, 3);

        [_MinMaxSliderVector2(-10, 10), SerializeField]
        private Vector2 areaY = new Vector2(-1, 1);

        [_MinMaxSliderVector2(-50, 50), SerializeField]
        private Vector2 areaZ = new Vector2(-3, 3);

        [Range(0, 1)] public float m_Damping            = 0.1f;
        [Range(0, 1)] public float m_Friction           = 0.1f;
        [Range(0, 1)] public float m_Elasticity         = 0.2f;
        [Range(0, 1)] public float m_CompressStiffness  = 0.4f;
        [Range(0, 1)] public float m_StretchStiffness   = 0.2f;
        [Range(0, 1)] public float m_BendStiffness      = 0.2f;
        [Range(0, 1)] public float m_CollisionStiffness = 0.1f;

        public Vector3 m_WindFroce = new Vector3(0, 0,     0);
        public Vector3 m_Gravity   = new Vector3(0, -9.8f, 0);

        [_ReadOnlyInPlayMode] public ExtForceMode extForceMode = ExtForceMode.Simple;

        [Header("Collision Settings"), Space, _ReadOnlyInPlayMode]
        public ParticlesCollisionMode particlesCollisionMode = ParticlesCollisionMode.CompactHash;

        [Range(1, 50)] public float particleCollisonCullingRadius = 10f;

        public bool collisionByQuad = true;

        [_ReadOnlyInPlayMode] public RigibodyFilterMode rigibodyFilterMode = RigibodyFilterMode.SimpleAABB;

        [Range(1, 16)] public int octSplitThresholdNums = 2;

        // [Range(2, 16), _ReadOnlyInPlayMode]
        // public int octCapacityLimitedPerNode = 8;

        [Range(1, 12)] public int octMaxDepth = 6;

        [Min(0.1f), Space] public float rigiVoxelSize = 1;

        public int rigiVoxelCount = 10000;

        public int RigiVoxelBucketMask => Mathf.NextPowerOfTwo(rigiVoxelCount) - 1;

        [Range(0.0f, 1f), SerializeField] private float particle2RigiCollisionRadius = 1f;

        [_ReadOnlyInPlayMode, Tooltip("更新mesh方式差异,不剔除是增量更新全量提交，剔除是视锥内全量更新提交")]
        public bool frustumCulling = false;

        public Camera focusCamera;
        
        
        

        public bool IsFrustumCullingOn => frustumCulling && focusCamera;

        public Bounds Area => new Bounds(
                                         new Vector3(math.csum(areaX),  math.csum(areaY),  math.csum(areaZ) * 0.5f),
                                         new Vector3(areaX.y - areaX.x, areaY.y - areaY.x, areaZ.y - areaZ.x));

        public float3 FoldAngleRange => new float3(foldAngleRange * Mathf.Deg2Rad, 0);

        public float3 ScaleRange => new float3(scaleRange, 0);

        public float3 MassRange => new float3(massRange, 0);

        // public Vector2Int LeavesTexSplit => leavesTexSplit;
        public int LeavesTypeCount => leavesTexSplit.x * leavesTexSplit.y;

        public int MaxLeavesCount
        {
            get => maxLeavesCount;
            set => maxLeavesCount = value;
        }

        public int VerticesCount  => Constants.VerticesLimit * maxLeavesCount;
        public int TrianglesCount => Constants.TrainglesLimit * maxLeavesCount;

        //--------particle-particle---------
        public float4 P_FilterParams
        {
            get
            {
                if (focusCamera != null)
                    return new float4(
                                      focusCamera.transform.position + focusCamera.transform.forward * particleCollisonCullingRadius,
                                      particleCollisonCullingRadius);
                else
                    return new float4(float3.zero, particleCollisonCullingRadius);
            }
        }

        public float P_ParticlesRadius => scaleRange.y * math.select(0.25f, 0.5f, collisionByQuad);
        public float P_CellRadius      => scaleRange.y * math.select(0.5f,  1f,   collisionByQuad);


        //--------particle-rigibody----------
        public float R_Particle2RigiRadius => particle2RigiCollisionRadius;

        public float R_QuadRadius => scaleRange.y * 0.5f;

        public float StaticVelocityThreshold => staticVelocityThreshold * staticVelocityThreshold;

        private float4 leaveTexSplitParams
            => new float4(
                          1f   / leavesTexSplit.x,
                          1f   / leavesTexSplit.y,
                          0.5f / leavesTexSplit.x,
                          0.5f / leavesTexSplit.y);

        private int hashBucketCapacityMask;

        public int HashBucketCapacityMask => hashBucketCapacityMask;

        public void InitialBucketCapacityMask()
        {
            hashBucketCapacityMask = Mathf.NextPowerOfTwo(VerticesCount) - 1;
        }


        public float4 GetSkinParams(int skinType)
        {
            int2 gridXY = GetGridXY(skinType, leavesTexSplit);
            return GetGridST(gridXY, leaveTexSplitParams);
        }

        private int2 GetGridXY(int skinType, Vector2Int texSplit)
        {
            return new int2(skinType % texSplit.x, Mathf.FloorToInt((float)skinType / texSplit.x));
        }

        private float4 GetGridST(int2 gridXY, float4 texParams)
        {
            return new float4(
                              texParams.x,
                              texParams.y,
                              gridXY.x * texParams.z * 2,
                              gridXY.y * texParams.z * 2);
        }

        public void InitProductParams(ref NativeArray<float3> productParams)
        {
            if (!productParams.IsCreated /*&& productParams.Length == 5*/)
                return;
            Bounds area = Area;

            float3
                min = area.min,
                max = area.max;
            productParams[0] = min;
            productParams[1] = max;
            productParams[2] = FoldAngleRange;
            productParams[3] = ScaleRange;
            productParams[4] = MassRange;
        }

        public void InitSkinParams(ref NativeArray<float4> @params)
        {
            var leaveTpyeCount = LeavesTypeCount;
            if (!@params.IsCreated || @params.Length != leaveTpyeCount)
                return;

            for (int i = 0; i < leaveTpyeCount; i++)
            {
                @params[i] = GetSkinParams(i);
            }
        }


        public void Dispose()
        {
        }
    }
}