using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class KuwaharaFilter : ScriptableRendererFeature
{
    public enum SampleMode
    {
        Base = 0,
        Bilinear = 1,
        Sobel = 2,
        BilinearSobel,
        // Blit,
    }
    
    [Serializable]
    public class Settings
    {

        public Material blurMaterial;
        
        public RenderPassEvent @event = RenderPassEvent.BeforeRenderingPostProcessing;

        [Range(-10, 10)] public int eventOffset = 0;

        [Range(0, 10)] public float blurRadius = 3;
        
        public SampleMode mode = SampleMode.Sobel;
    }


    public Settings settings = new Settings();

    KuwaharaFilterPass m_Pass;

    /// <inheritdoc/>
    public override void Create()
    {
        Dispose(true);
        m_Pass = new KuwaharaFilterPass(name,
                                        settings.blurMaterial,
                                        settings.@event + settings.eventOffset,
                                        settings.blurRadius,
                                        settings.mode);
    }
    
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.isPreviewCamera || !renderingData.postProcessingEnabled)
            return;
        if (settings.blurMaterial == null)
            return;
        renderer.EnqueuePass(m_Pass);
    }

    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        m_Pass.SetUp(renderer.cameraColorTargetHandle);
    }

    protected override void Dispose(bool disposing)
    {
        if (m_Pass != null)
        {
            m_Pass.Dispose();
            m_Pass = null;
        }
    }
    
    
    class KuwaharaFilterPass : ScriptableRenderPass
    {
        private static readonly int _KuwaharaFilterParams = Shader.PropertyToID("_KuwaharaFilterParams");

        private readonly Material m_Material;

        private readonly ProfilingSampler m_ProfilingSampler;

        private readonly float m_BlurRadius;

        private SampleMode m_SampleMode;

        private RenderTextureDescriptor m_CameraBufferDescriptor;

        private Vector2Int m_LastFrameScreenSize;

        private RTHandle m_CameraColor;

        private RTHandle m_TempColor;

        public KuwaharaFilterPass(string passTag, Material material, RenderPassEvent evt, float blurRadius, SampleMode mode)
        {
            m_ProfilingSampler = new ProfilingSampler(passTag);
            m_Material         = material;
            renderPassEvent    = evt;
            m_BlurRadius       = blurRadius;
            m_SampleMode       = mode;
        }

        public void SetUp(RTHandle rtHandle)
        {
            m_CameraColor = rtHandle;
        }
        
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            m_CameraBufferDescriptor = renderingData.cameraData.cameraTargetDescriptor;

            m_CameraBufferDescriptor.useMipMap        = false;
            m_CameraBufferDescriptor.autoGenerateMips = false;
            m_CameraBufferDescriptor.depthBufferBits  = 0;
            m_CameraBufferDescriptor.msaaSamples      = 1;
            m_CameraBufferDescriptor.sRGB             = false;

            CheckScreenResize();
        }

        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get(m_ProfilingSampler.name);

            // cmd.SetGlobalVector(_KuwaharaFilterParams, new Vector4(m_BlurRadius, 0, 0, 0));
            m_Material.SetVector(_KuwaharaFilterParams, new Vector4(m_BlurRadius, 0, 0, 0));

            //写进packages里可以省掉这个临时copy，   swap
            Blitter.BlitCameraTexture(cmd, m_CameraColor, m_TempColor,
                                      RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                                      Blitter.GetBlitMaterial(TextureDimension.Unknown), 0);

            Blitter.BlitCameraTexture(cmd, m_TempColor, m_CameraColor,
                                      RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, 
                                      m_Material, (int)m_SampleMode);
            
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }
        
        
        private void CheckScreenResize()
        {
            if ((m_CameraBufferDescriptor.width  != m_LastFrameScreenSize.x) ||
                (m_CameraBufferDescriptor.height != m_LastFrameScreenSize.y))
            {
                m_LastFrameScreenSize = new Vector2Int(m_CameraBufferDescriptor.width, m_CameraBufferDescriptor.height);
                
                
#if UNITY_EDITOR
                Dispose();
#endif
                m_TempColor = RTHandles.Alloc(m_TempColor, "_KuwaharaTemp");
                RenderingUtils.ReAllocateIfNeeded(ref m_TempColor, m_CameraBufferDescriptor, 
                                                  FilterMode.Bilinear, TextureWrapMode.Clamp,
                                                  false, 1, 0, name: "_KuwaharaTemp");
            }
        }
        
        public void Dispose()
        {
            m_TempColor?.Release();
        }
    }
}


