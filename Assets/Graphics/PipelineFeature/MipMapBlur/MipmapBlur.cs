// https://github.com/jacktheLad/UnityMipmapBlur/blob/main/Assets/MipmapBlur/MipmapBlur.cs

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class MipmapBlur : ScriptableRendererFeature
{
    [Serializable]
    public class Settings
    {
        public string passTag = "Mipmap Blur";

        [_ReadOnly] public Material blurMaterial;
        
        public RenderPassEvent @event = RenderPassEvent.BeforeRenderingPostProcessing;
        
        [Range(0, 50)] public float blurLevel = 25;

        [Space(10)] public bool outPutToUIBackGround = false;

        [Tooltip("毛玻璃UI可以开着降更新频率")] [Range(1, 3)]
        public int redutionFrameRatio = 1;
    }

    public Settings settings = new Settings();
    private MipmapBlurPass m_Pass;

    public override void Create()
    {
        //sb行为 OnValidate触发Create，不管回收
        Dispose(true);
        m_Pass = new MipmapBlurPass
        (
            settings.passTag,
            settings.blurMaterial,
            settings.@event,
            settings.blurLevel,
            settings.redutionFrameRatio,
            settings.outPutToUIBackGround
        );
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.isSceneViewCamera || renderingData.cameraData.isPreviewCamera)
            return;
        if (settings.blurMaterial == null)
            return;
        renderer.EnqueuePass(m_Pass);
    }

    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        if (renderingData.cameraData.isSceneViewCamera || renderingData.cameraData.isPreviewCamera)
            return;
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
}

public class MipmapBlurPass : ScriptableRenderPass
{
    private static class PID
    {
        public static RTHandle _TextureWithMips;
        public static readonly int _MBlurParams = Shader.PropertyToID("_MBParams");
        public static readonly int _TempMipTexture = Shader.PropertyToID("_TempMipTexture");
        public static readonly int _KawaseBlurRT = Shader.PropertyToID("_KawaseBlurRT");
    }

    private readonly Material m_BlurMaterial;
    private readonly ProfilingSampler m_ProfilingSampler;
    private readonly float m_BlurLevel;
    private RenderTextureDescriptor m_CameraBufferDescriptor;
    private RTHandle m_CameraColor;
    private RTHandle[] m_MipIDs;
    private Vector3[] m_Weights; //x:weight y:continue z:miplevel
    private bool m_mipTempIsCreated = false;
    private int m_MipCount; // exclude mip0;
    private int m_RedutionFrameRatio;
    private int m_selfFrame;
    private bool m_outToUIBackground;
    private bool resultIsCreated = false;
    private RTHandle resultRT; //毛玻璃UI背景

    private Vector2Int m_LastFrameScreenSize;

    public MipmapBlurPass(string passTag, Material blurMaterial, RenderPassEvent evt, float blurLevel, int redutionFrequencyRatio = 2, bool outToUIBackground = false)
    {
        m_ProfilingSampler = new ProfilingSampler(passTag);
        m_BlurMaterial = blurMaterial;
        renderPassEvent = evt;
        m_BlurLevel = blurLevel;
        m_RedutionFrameRatio = redutionFrequencyRatio;
        m_selfFrame = redutionFrequencyRatio - 1;
        m_outToUIBackground = outToUIBackground;
    }

    public void SetUp(RTHandle rtHandle)
    {
        m_CameraColor = rtHandle;
    }

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        {
            m_selfFrame++;
            if (m_selfFrame < m_RedutionFrameRatio) return;
        }
        m_CameraBufferDescriptor = renderingData.cameraData.cameraTargetDescriptor;
        m_CameraBufferDescriptor.useMipMap = false;
        m_CameraBufferDescriptor.autoGenerateMips = false;
        m_CameraBufferDescriptor.depthBufferBits = 0;
        m_CameraBufferDescriptor.msaaSamples = 1;
        m_CameraBufferDescriptor.sRGB = false;
        CheckScreenResize();

        if (!m_mipTempIsCreated)
        {
#if UNITY_EDITOR
            Dispose();
#endif
            RenderTextureDescriptor desc = m_CameraBufferDescriptor;
            desc.useMipMap = true;
            desc.mipCount = m_MipCount;
            //为了降带宽这里分辨率可以降2/4倍宽高，损失高频细节
            //别问为什么模糊半径开0也糊,不降分辨率自然就清楚了
            desc.width = Mathf.Max(desc.width >> 1, 1);
            desc.height = Mathf.Max(desc.height >> 1, 1);
            PID._TextureWithMips = RTHandles.Alloc(PID._TextureWithMips, "_TextureWithMips");
            RenderingUtils.ReAllocateIfNeeded(ref PID._TextureWithMips, desc, FilterMode.Bilinear, TextureWrapMode.Clamp,
                false, 1, 0, name: "_TextureWithMips");
            desc.useMipMap = false;
            desc.mipCount = -1;
            Vector2Int screenSize = m_LastFrameScreenSize;
            int width = screenSize.x, height = screenSize.y;
            for (int i = 0; i < m_MipCount; i++)
            {
                width = Mathf.Max(width >> 1, 1);
                height = Mathf.Max(height >> 1, 1);
                desc.width = width;
                desc.height = height;

                string names = "_TempMip" + (i + 1);
                m_MipIDs[i] = RTHandles.Alloc(Shader.PropertyToID(names), name: names);
                RenderingUtils.ReAllocateIfNeeded(ref m_MipIDs[i], desc, FilterMode.Bilinear,
                    TextureWrapMode.Clamp, name: names);
            }

            float blurLevel = m_BlurLevel;
            m_Weights = new Vector3[m_MipCount + 1];
            //找第一个大于0.01的，保留前一位，再前面全跳过
            int jump = 0;
            for (int i = m_MipCount; i >= 0; i--)
            {
                //降2倍这里level+1
                float weight = GetMipBlendWeight(blurLevel, i + 1);
                bool useful = weight >= 0.01f;
                if (jump == 0 && useful)
                {
                    jump = Mathf.Min(i + 1, m_MipCount);
                    Vector3 front = m_Weights[jump];
                    front.y = 1;
                    m_Weights[jump] = front;
                }

                if (jump != 0)
                {
                    useful = true;
                }

                m_Weights[i] = new Vector3(weight, useful ? 1 : 0, i);
            }

            m_mipTempIsCreated = true;
        }
        

        if (!resultIsCreated && m_outToUIBackground)
        {
            resultRT = RTHandles.Alloc(PID._KawaseBlurRT, "_KawaseBlurRT");
            RenderingUtils.ReAllocateIfNeeded(ref resultRT, m_CameraBufferDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_KawaseBlurRT");
            cmd.SetGlobalTexture(PID._KawaseBlurRT, resultRT);
            resultIsCreated = true;
        }
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        {
            if (m_selfFrame < m_RedutionFrameRatio) return;
        }
        var cmd = CommandBufferPool.Get(m_ProfilingSampler.name);
        // using (new ProfilingScope(cmd, m_ProfilingSampler))
        {
            cmd.SetGlobalTexture(PID._TempMipTexture, m_CameraColor);
            Blitter.BlitCameraTexture(cmd, m_CameraColor, PID._TextureWithMips,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                m_BlurMaterial, 0);
            cmd.GenerateMips(PID._TextureWithMips);

            // float blurLevel = m_BlurLevel;
            int start = Mathf.Max(m_MipCount, 1);
            bool isFrist = true;
            for (var i = start; i >= 0; i--)
            {
                Vector3 w = m_Weights[i];
                bool jump = w.y < 0.001f;
                if (jump)
                    continue;
                float weight = w.x;
                cmd.SetGlobalVector(PID._MBlurParams, new Vector4(i, weight, w.y));

                RTHandle destination;
                if (i == 0)
                {
                    destination = m_outToUIBackground ? resultRT : m_CameraColor;
                    cmd.SetGlobalTexture(PID._TempMipTexture, m_MipIDs[i]);
                }
                else
                {
                    destination = m_MipIDs[i - 1];
                    // cmd.SetGlobalTexture(PID._TempMipTexture, i == start ? PID._TextureWithMips : m_MipIDs[i]);
                    cmd.SetGlobalTexture(PID._TempMipTexture, isFrist ? PID._TextureWithMips : m_MipIDs[i]);
                }

                Blitter.BlitCameraTexture(cmd, PID._TextureWithMips, destination,
                    RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                    m_BlurMaterial, (isFrist || weight < 0.01f) ? 0 : 1);
                isFrist = false;
            }
            
            // var camera = renderingData.cameraData.camera;
            // cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);
        }

        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
        CommandBufferPool.Release(cmd);
    }

    private float GetMipBlendWeight(float blurLevel, int curMipLevel)
    {
        float sigma2 = blurLevel * blurLevel;
        float c = 2.0f * Mathf.PI * sigma2;
        float numerator = (1 << (curMipLevel << 2)) * Mathf.Log(4.0f);
        float denominator = c * ((1 << (curMipLevel << 1)) + c);
        return Mathf.Clamp01(numerator / denominator);
    }

    private void CheckScreenResize()
    {
        if ((m_CameraBufferDescriptor.width != m_LastFrameScreenSize.x) ||
            (m_CameraBufferDescriptor.height != m_LastFrameScreenSize.y))
        {
            m_LastFrameScreenSize = new Vector2Int(m_CameraBufferDescriptor.width, m_CameraBufferDescriptor.height);

            var maxLen = Mathf.Max(m_LastFrameScreenSize.x, m_LastFrameScreenSize.y);
            m_MipCount = Mathf.FloorToInt(Mathf.Log(maxLen, 2));
            m_MipCount = Mathf.Max(m_MipCount, 2) - 1;
            m_MipIDs = new RTHandle[m_MipCount];

            m_mipTempIsCreated = false;
            resultIsCreated = false;
        }
    }

    public override void OnCameraCleanup(CommandBuffer cmd)
    {
        {
            if (m_selfFrame < m_RedutionFrameRatio) return;
            m_selfFrame &= ((1 << m_RedutionFrameRatio) - 1);
        }
    }

    public void Dispose()
    {
        PID._TextureWithMips?.Release();
        for (var i = 0; i < m_MipCount; i++)
        {
            m_MipIDs[i]?.Release();
        }

        m_mipTempIsCreated = false;

        resultRT?.Release();
    }
}