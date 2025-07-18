using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class GTAO : ScriptableRendererFeature
{
    [Serializable]
    public class GTAOSetting
    {
        public Material material;
        
        [Header("--- GTAO Setting ---")]
        [Range(2, 4)] public int DirSampler = 2;

        [Range(2, 8)] public int SliceSampler = 2;

        [Range(0.1f, 8)] public float Radius = 2;

        [Range(0, 8)] public float Power = 2;

        [Range(0, 1)] public float Intensity = 0.8f;

        [Range(0, 1)] public float Sharpeness = 0.25f;

        public CopyPackType AOBlurType = CopyPackType.DepthAO;

        [Header("Filtter Property")]
        public bool TemporalFiltter = false;

        [Range(1, 5)] public float TemporalScale = 1;

        [Range(0, 1)] public float TemporalResponse = 1;

        [Header("Debug")]
        public OutSource OutPass = OutSource.Combien;

        public Vector4 GetResolveParams => new Vector4(DirSampler, SliceSampler, Radius, Power);
        public Vector4 GetPostParams    => new Vector4(Intensity, Sharpeness, TemporalScale, TemporalResponse);
    }
    
    public enum DepthSource
    {
        Depth        = 0,
        DepthNormals = 1
    }
    
    public enum CopyPackType
    {
        DepthAO,
        NormalAO,
    }

    public enum OutSource
    {
        Combien    = 6,
        BentNormal = 7,
        AO         = 8,
        RO         = 9,
        SSR        = 10,
    }

    public GTAOSetting Settings;

    GTAOResolvePass m_GTAOPass;

    /// <inheritdoc/>
    public override void Create()
    {
        Dispose(true);
        
        if(!isActive)
            return;

        m_GTAOPass = new GTAOResolvePass(Settings, name)
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents - 1,
        };
    }


    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.isPreviewCamera || Settings.material == null)
            return;

        if (m_GTAOPass.SetUp(in Settings))
            renderer.EnqueuePass(m_GTAOPass);
    }
    
    protected override void Dispose(bool isDisposing)
    {
        if (m_GTAOPass != null)
        {
            m_GTAOPass.Dispose();
            m_GTAOPass = null;
        }
    }
    
    
    class GTAOResolvePass : ScriptableRenderPass
    {
        private readonly ProfilingSampler m_ProfilingSampler;

        private readonly Material m_Material;

        private RenderTextureDescriptor m_Desc;

        private RTHandle m_ResolveRT;
        
        private RTHandle m_TempRT;

        private RTHandle m_Spatial;

        private RTHandle m_NormalRT;

        private RTHandle m_MoveVectorRT;

        private RTHandle m_TemporalRTA;

        private RTHandle m_TemporalRTB;

        private bool aIsCurrRT;

        private GTAOSetting m_CurrentSettings;

        private RTHandle src;
        private RTHandle dest;

        private const string k_PACKNORMALAO    = "_PACKNORMALAO";
        
        
        private                 uint    m_sampleStep        = 0;
        private static readonly float[] m_temporalRotations = {60, 300, 180, 240, 120, 0};
        private static readonly float[] m_spatialOffsets    = {0, 0.5f, 0.25f, 0.75f};

        private ref RTHandle _CurrRT => ref aIsCurrRT ? ref m_TemporalRTA : ref m_TemporalRTB;
        private ref RTHandle _PrevRT => ref aIsCurrRT ? ref m_TemporalRTB : ref m_TemporalRTA;
        
        private void SwapTemporalRT()
        {
            aIsCurrRT = !aIsCurrRT;
        }

        public GTAOResolvePass(GTAOSetting setting, in string passName)
        {
            m_Material = setting.material;

            m_ProfilingSampler = new ProfilingSampler(passName);
        }

        public bool SetUp(in GTAOSetting setting)
        {
            m_CurrentSettings = setting;
            
            //不开msaa的话，可以不需要CopyPass

            var inputConfig = ScriptableRenderPassInput.Normal | ScriptableRenderPassInput.Depth;
            
            if(m_CurrentSettings.TemporalFiltter) 
                inputConfig |= ScriptableRenderPassInput.Motion;
            
            ConfigureInput(inputConfig);
            
            m_Material.SetVector("_AO_ResolveParams", setting.GetResolveParams);
            m_Material.SetVector("_AO_PostParams", setting.GetPostParams);

            CoreUtils.SetKeyword(m_Material, k_PACKNORMALAO, m_CurrentSettings.AOBlurType == CopyPackType.NormalAO);

            return m_CurrentSettings.Power > 0.01;
        }
        

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var cameraData = renderingData.cameraData;
            var render     = cameraData.renderer;
            
            m_Desc  = renderingData.cameraData.cameraTargetDescriptor;

            m_Desc.sRGB = false;

            m_Desc.depthBufferBits = -1;
            
            m_Desc.msaaSamples = 1;

            m_Desc.colorFormat = RenderTextureFormat.ARGB32;
            
            
            RenderingUtils.ReAllocateIfNeeded(ref m_ResolveRT, m_Desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_BentNormalTexture");
            RenderingUtils.ReAllocateIfNeeded(ref m_TempRT, m_Desc, FilterMode.Point, TextureWrapMode.Clamp, name: "GTAO_BlurTemp");
            RenderingUtils.ReAllocateIfNeeded(ref m_Spatial, m_Desc, FilterMode.Point, TextureWrapMode.Clamp, name: "GTAO_Spatial");

            m_Desc.colorFormat = RenderTextureFormat.RGB111110Float; //

            if (m_CurrentSettings.TemporalFiltter)
            {
                RenderingUtils.ReAllocateIfNeeded(ref m_TemporalRTA, m_Desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_TemporalRTA");
                RenderingUtils.ReAllocateIfNeeded(ref m_TemporalRTB, m_Desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_TemporalRTB");
                m_MoveVectorRT = render.GetCopyMoveVector();
                m_Material.SetTexture("_PrevRT", _PrevRT);
                m_Material.SetTexture("_CurrRT", _CurrRT);
                m_Material.SetTexture(m_MoveVectorRT.name, m_MoveVectorRT);
            }

            m_NormalRT = render.GetCopyNormal();
            m_Material.SetTexture(m_ResolveRT.name, m_ResolveRT);
            m_Material.SetTexture(m_NormalRT.name, m_NormalRT);
            
            var camera = cameraData.camera;

            var cameraPrej = camera.projectionMatrix;

            // float cotTanHalfFov = 1 / MathF.Tan(camera.fieldOfView * Mathf.Deg2Rad); //cot(θ/2)
            float cotTanHalfFov = cameraPrej.m11; //cot(θ/2)
            
            // Vector2 focalLen = new Vector2(cameraPrej.m00, cotTanHalfFov);
            Vector2 focalLen = new Vector2(cotTanHalfFov / camera.aspect, cotTanHalfFov);

            Vector2 invFocalLen = new Vector2(1 / focalLen.x, 1 / focalLen.y);

            m_Material.SetVector("_AO_UVToView", new Vector4(2 * invFocalLen.x, 2 * invFocalLen.y, -invFocalLen.x, -invFocalLen.y));

            var V  = camera.worldToCameraMatrix;
            var VP = GL.GetGPUProjectionMatrix(cameraPrej, false) * V;

            m_Material.SetMatrix("_Inverse_View_ProjectionMatrix", VP.inverse);

            float projScale = (float)m_Desc.height * cotTanHalfFov * 0.25f;// H/2  * cot(θ/2) * 0.5
            m_Material.SetFloat("_AO_HalfProjScale", projScale);
            
            float temporalRotation = m_temporalRotations[m_sampleStep    % 6];
            float temporalOffset   = m_spatialOffsets[(m_sampleStep / 6) & 3];
            m_Material.SetFloat("_AO_TemporalDirections", temporalRotation / 360);
            m_Material.SetFloat("_AO_TemporalOffsets", temporalOffset);
            
            m_sampleStep++;
            m_sampleStep %= 23;
        }


        //dest
        private const RenderBufferLoadAction  LoadAction = RenderBufferLoadAction.DontCare;
        private const RenderBufferStoreAction StoreAction = RenderBufferStoreAction.Store;
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                var render = renderingData.cameraData.renderer;

                var colorSrc = render.GetCameraColorBackBuffer(cmd);
                var colordest = render.GetCameraColorFrontBuffer(cmd);

                var depthRT  = render.GetCopyDepth();

                m_Material.SetTexture("_CameraColorTexture", colorSrc);
                m_Material.SetTexture(depthRT.name, depthRT);

                // if (m_CurrentSettings.Source == DepthSource.Depth)
                // {
                //     Blitter.BlitCameraTexture(cmd, depthRT, m_NormalRT, LoadAction, StoreAction, m_Material, 0);
                // }

                (src, dest) = (m_TempRT, m_ResolveRT); //Resolve
                Blitter.BlitCameraTexture(cmd, src, dest, LoadAction, StoreAction, m_Material, 1);

                (src, dest) = (m_ResolveRT, m_Spatial);//copy
                Blitter.BlitCameraTexture(cmd, src, dest, LoadAction, StoreAction, m_Material, 2);

                (src, dest) = (m_Spatial, m_TempRT); //blurX
                Blitter.BlitCameraTexture(cmd, src, dest, LoadAction, StoreAction, m_Material, 3);

                (src, dest) = (m_TempRT, m_Spatial); //blurY
                Blitter.BlitCameraTexture(cmd, src, dest, LoadAction, StoreAction, m_Material, 4);
                
                if(m_CurrentSettings.TemporalFiltter)
                {
                    (src, dest) = (m_Spatial, _CurrRT); //Temporal
                    Blitter.BlitCameraTexture(cmd, src, dest, LoadAction, StoreAction, m_Material, 5);

                    (src, dest) = (_CurrRT, colordest); 
                }
                else
                {
                    (src, dest) = (m_Spatial, colordest); 
                }
                
                Blitter.BlitCameraTexture(cmd, src, dest, LoadAction, StoreAction, m_Material, (int)m_CurrentSettings.OutPass);
                
                render.SwapColorBuffer(cmd);
            }

            context.ExecuteCommandBuffer(cmd);
            // cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        // Cleanup any allocated resources that were created during the execution of this render pass.
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            SwapTemporalRT();
        }

        public void Dispose()
        {
            m_ResolveRT?.Release();

            m_TempRT?.Release();
            m_Spatial?.Release();

            m_NormalRT?.Release();

            m_TemporalRTA?.Release();
            m_TemporalRTB?.Release();
        }
    }
}


