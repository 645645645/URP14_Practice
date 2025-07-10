using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class HiZMipmapCreater : ScriptableRendererFeature
{
    public enum SSRType
    {
        Simple,
        HiZ,
        HiZ_UE4
    }

    [Serializable]
    public class HiZSetting
    {
        [Header("---Mipmap Create Pass---")] [_ReadOnly]
        public ComputeShader hizComputeShader;

        [_ReadOnly] public Material hizLegacyMaterial;

        public RenderPassEvent mipCreateEvent = RenderPassEvent.AfterRenderingOpaques;
        [Range(-10, 10)] public int mipCreateOffset = 10;

        [Space(10)] [Header("---SSR Setting---")]
        public SSRType ssrType;

        [Range(0, 5)] public float ssrIntensity = 1;

        [_RangeStep(8, 64, 1)] public float ssrStepNums = 16;

        [Range(0.1f, 1f)] public float ssrThreshold = 0.1f;

        [Range(0, 1)] public float ssrDithering = 1;
    }


    HiZMipmapRenderPass m_HiZMipmapCreatePass;

    public HiZSetting m_Settings;


    public override void Create()
    {
        Dispose(true);

        //-------
        if (!isActive) return;
        //------


        m_HiZMipmapCreatePass = new HiZMipmapRenderPass(m_Settings)
        {
            renderPassEvent = m_Settings.mipCreateEvent + m_Settings.mipCreateOffset
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.isPreviewCamera)
            return;
        if (m_Settings.hizComputeShader == null || m_Settings.hizLegacyMaterial == null)
        {
            Debug.LogError($"SSR Render Feature: {nameof(HiZMipmapCreater)} .<ConmputeShader> or <Material> is null");
            return;
        }
        
        renderer.EnqueuePass(m_HiZMipmapCreatePass);
    }

    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        if (renderingData.cameraData.isPreviewCamera)
            return;
        m_HiZMipmapCreatePass.SetUp(renderer.cameraDepthTargetHandle);
    }

    protected override void Dispose(bool isDisposing)
    {
        if (m_HiZMipmapCreatePass != null)
        {
            m_HiZMipmapCreatePass.Dispose();
            m_HiZMipmapCreatePass = null;
        }
    }

    class HiZMipmapRenderPass : ScriptableRenderPass
    {
        private static class HiZConstans
        {
            public static readonly GlobalKeyword _ssrHizKeyword = GlobalKeyword.Create("_SSRHIZ");
            public static readonly GlobalKeyword _ssrHizUE4Keyword = GlobalKeyword.Create("_SSRHIZUE4");

            public static readonly int _HZBMipmapDepthTexID = Shader.PropertyToID("_HZBDepthTexture");

            // public static readonly int _HZBOffsetParamsID = Shader.PropertyToID("_HZBOffsetParams");
            public static readonly int _InputViewportMaxBoundID = Shader.PropertyToID("_InputViewportMaxBound");
            public static readonly int _DispatchThreadIdToBufferUVID = Shader.PropertyToID("_DispatchThreadIdToBufferUV");
            public static readonly int _HZBUvFactorAndInvFactor = Shader.PropertyToID("_HZBUvFactorAndInvFactor");
            public static readonly int _CurrentMipBatchCountID = Shader.PropertyToID("_CurrentMipBatchCount");
            public static readonly int _SSRParams = Shader.PropertyToID("_SSRParams");

            public static readonly int _maxMipmapLevelID = Shader.PropertyToID("_MaxMipLevel");
            public static readonly int _parentTextureMipID = Shader.PropertyToID("_ParentTextureMip");

            public static RenderTextureFormat CustomDepthFormat
            {
                get
                {
                    var format = RenderTextureFormat.R16;
                    if (SystemInfo.SupportsRenderTextureFormat(format)
                        && SystemInfo.SupportsRandomWriteOnRenderTextureFormat(format))
                        return format;
                    format = RenderTextureFormat.RHalf;
                    if (SystemInfo.SupportsRenderTextureFormat(format)
                        && SystemInfo.SupportsRandomWriteOnRenderTextureFormat(format))
                        return format;
                    format = RenderTextureFormat.RFloat;
                    // if (SystemInfo.SupportsRenderTextureFormat(format)
                    //     && SystemInfo.SupportsRandomWriteOnRenderTextureFormat(format))
                    return format;
                }
            }

            public static readonly RenderTextureDescriptor tempRTDesc = new(1, 1, CustomDepthFormat, -1, -1)
            {
                msaaSamples = 1,
                sRGB = false,
                useMipMap = false,
                autoGenerateMips = false,
                enableRandomWrite = false
            };
#if true
            public static readonly bool SupportComputeShaders = SystemInfo.supportsComputeShaders &&
                                                                SystemInfo.SupportsRandomWriteOnRenderTextureFormat(CustomDepthFormat);
#else
            public static readonly bool SupportComputeShaders = false;
#endif

            //metal dont know
            // SystemInfo.supportsMemoryBarriers
            public static readonly bool SupportsMemoryBarriers = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12 ||
                                                                 SystemInfo.graphicsDeviceType == GraphicsDeviceType.XboxOneD3D12 ||
                                                                 SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan ||
                                                                 SystemInfo.graphicsDeviceType == GraphicsDeviceType.PlayStation5 ||
                                                                 SystemInfo.graphicsDeviceType == GraphicsDeviceType.PlayStation5NGGC ||
                                                                 SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3;
        }

        private ProfilingSampler m_ProfilingSampler;

        private RTHandle _mipRT;
        private RTHandle[] _tempRTs;
        private bool[] _tempRTsIsCreated;
        private const int MaxMipmapLevelOutBatchCount = 4;
        private readonly string _passTag = "HiZ_MipmapCreatorPass";

        private readonly ComputeShader _hizComputeShader;
        private readonly Material _hizMaterial;
        private readonly int[] _kernelHandle;

        private RTHandle _depthRT;

        private Vector2Int[] _MipLevelSize;

        private int _numMips;
        private RenderTextureDescriptor _camDescriptor;

        private readonly SSRType _ssrType;
        private readonly float _ssrIntensity;
        private readonly float _ssrStepNums;
        private readonly float _ssrThreshold;
        private readonly float _ssrDithering;

        private bool mipIsCreated = false;

        private bool bUseCompute;
        private bool bUseTemp;
        private bool useMSAA;


        public HiZMipmapRenderPass(HiZSetting setting)
        {
            _hizComputeShader = setting.hizComputeShader;
            _hizMaterial = setting.hizLegacyMaterial;
            if (_hizComputeShader != null)
            {
                _kernelHandle = new int[4];
                for (int i = 0; i < 4; i++)
                {
                    _kernelHandle[i] = _hizComputeShader.FindKernel($"KHZBCreator{i+1}");
                }
            }

            _ssrType = setting.ssrType;
            _ssrIntensity = setting.ssrIntensity;
            _ssrStepNums = setting.ssrStepNums;
            _ssrThreshold = setting.ssrThreshold;
            _ssrDithering = setting.ssrDithering;
            bUseCompute = HiZConstans.SupportComputeShaders;
            bUseTemp = HiZConstans.SupportComputeShaders && !HiZConstans.SupportsMemoryBarriers;
            // Debug.Log("supportedRandomWriteTargetCount = " +  SystemInfo.supportedRandomWriteTargetCount);
            // Debug.Log("R8 = " +  SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.R8));
            // Debug.Log("R16 = " +  SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.R16));
            // Debug.Log("RHalf = " +  SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RHalf));
            // Debug.Log("RFloat = " +  SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RFloat));

            m_ProfilingSampler = new ProfilingSampler(_passTag);
        }

        public void SetUp(in RTHandle cameraDepth)
        {
            _depthRT = cameraDepth;
            
            useMSAA  = cameraDepth.isMSAAEnabled;
            
            //默认需要color (with downSample)
            var inputConfig = ScriptableRenderPassInput.Color;

            if (useMSAA)
                inputConfig |= ScriptableRenderPassInput.Depth;
            
            ConfigureInput(inputConfig);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            cmd.SetGlobalVector(HiZConstans._SSRParams, new Vector4(_ssrIntensity, _ssrStepNums, _ssrThreshold, _ssrDithering));

            switch (_ssrType)
            {
                case SSRType.HiZ:
                    cmd.EnableKeyword(HiZConstans._ssrHizKeyword);
                    cmd.DisableKeyword(HiZConstans._ssrHizUE4Keyword);
                    break;
                case SSRType.HiZ_UE4:
                    cmd.EnableKeyword(HiZConstans._ssrHizUE4Keyword);
                    cmd.DisableKeyword(HiZConstans._ssrHizKeyword);
                    break;
                case SSRType.Simple:
                default:
                    cmd.DisableKeyword(HiZConstans._ssrHizUE4Keyword);
                    cmd.DisableKeyword(HiZConstans._ssrHizKeyword);
                    return;
            }
            
            var cameraDescriptor = renderingData.cameraData.cameraTargetDescriptor;

            CheckScreenResize(cameraDescriptor);

            if (!mipIsCreated)
            {
#if UNITY_EDITOR
                Dispose();
#endif
                _camDescriptor = cameraDescriptor;
                int width = _camDescriptor.width;
                int height = _camDescriptor.height;

                width = Mathf.Max(Mathf.CeilToInt(Mathf.Log(width, 2)) - 1, 1);
                height = Mathf.Max(Mathf.CeilToInt(Mathf.Log(height, 2)) - 1, 1);
                _numMips = Mathf.Max(width, height);

                width = 1 << width;
                height = 1 << height;
                // Debug.Log($"To2Pow:width = {width}, height = {height}");

                var mipDescriptor = new RenderTextureDescriptor(width, height, HiZConstans.CustomDepthFormat, -1, _numMips)
                {
                    msaaSamples = 1,
                    sRGB = false, // linear
                    useMipMap = true,
                    autoGenerateMips = false,
                    enableRandomWrite = HiZConstans.SupportComputeShaders
                };

                _mipRT = RTHandles.Alloc(HiZConstans._HZBMipmapDepthTexID, "_HZBDepthTexture");
                RenderingUtils.ReAllocateIfNeeded(ref _mipRT, mipDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_HZBDepthTexture");
                _tempRTs = new RTHandle[_numMips];
                _tempRTsIsCreated = new bool[_numMips];
                _MipLevelSize = new Vector2Int[_numMips];

                for (int i = 0; i < _numMips; i++)
                {
                    _MipLevelSize[i] = new Vector2Int(width, height);
                    width = Mathf.Max(width >> 1, 1);
                    height = Mathf.Max(height >> 1, 1);
                }

                mipIsCreated = true;
            }


            // ConfigureTarget(renderer.cameraColorTargetHandle);
            // ConfigureClear(ClearFlag.None, Color.clear);
        }

        /// <summary>
        /// GameDisPaly改输出分辨率。没有事件接口
        /// </summary>
        /// <param name="cameraDescriptor"></param>
        private void CheckScreenResize(RenderTextureDescriptor cameraDescriptor)
        {
            if ((cameraDescriptor.width != _camDescriptor.width) ||
                (cameraDescriptor.height != _camDescriptor.height))
            {
                mipIsCreated = false;
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_ssrType == SSRType.Simple)
                return;

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {

                if (bUseCompute)
                {
                    // Reduce first mips
                    {
                        Vector2Int srcSize          = new Vector2Int(_camDescriptor.width, _camDescriptor.height);
                        Vector2Int destSize         = _MipLevelSize[0];
                        RTHandle   parentTextureMip = useMSAA ? renderingData.cameraData.renderer.GetCopyDepth() : _depthRT;
                        Vector4 dispatchThreadIdToBufferUV = new Vector4()
                        {
                            x = 2.0f / (float)srcSize.x,
                            y = 2.0f / (float)srcSize.y,
                            z = 0.0f,
                            w = 0.0f
                            // w = (srcSize.y - (destSize.y << 1)) / (float)destSize.y
                        };
                        Vector4 inputViewportMaxBound = new Vector4()
                        {
                            x = 1.0f / (float)srcSize.x,
                            y = 1.0f / (float)srcSize.y,
                            z = ((float)srcSize.x - 0.5f) / (float)srcSize.x,
                            w = ((float)srcSize.y - 0.5f) / (float)srcSize.y
                        };
                        ReduceMips(cmd, parentTextureMip, srcSize, 0, destSize,
                            0, dispatchThreadIdToBufferUV, inputViewportMaxBound, false);


                        Vector2 _hzbUvFactor = new Vector2(srcSize.x / (float)(destSize.x * 2),
                            srcSize.y / (float)(destSize.y * 2));
                        Vector4 _hzbUvFactorAndInvFactor = new Vector4()
                        {
                            x = _hzbUvFactor.x,
                            y = _hzbUvFactor.y,
                            z = 1 / _hzbUvFactor.x,
                            w = 1 / _hzbUvFactor.y
                        };

                        cmd.SetGlobalVector(HiZConstans._HZBUvFactorAndInvFactor, _hzbUvFactorAndInvFactor);
                    }

                    // bUseTemp |= !HiZConstans.SupportsMemoryBarriers;
                    // Reduce the next mips
                    for (int start = MaxMipmapLevelOutBatchCount; start < _numMips; start += MaxMipmapLevelOutBatchCount)
                    {
                        Vector2Int srcSize = _MipLevelSize[start - 1];
                        Vector2Int dstSize = _MipLevelSize[start];
                        RTHandle parentTextureMip = _mipRT;

                        Vector4 dispatchThreadIdToBufferUV = new Vector4()
                        {
                            x = 2.0f / (float)srcSize.x,
                            y = 2.0f / (float)srcSize.y,
                            z = 0.0f / (float)srcSize.x,
                            w = 0.0f / (float)srcSize.y
                        };
                        Vector4 inputViewportMaxBound = new Vector4(1 / (float)srcSize.x, 1 / (float)srcSize.y, 1, 1);

                        ReduceMips(cmd, parentTextureMip, srcSize, start - 1, dstSize,
                            start, dispatchThreadIdToBufferUV, inputViewportMaxBound, bUseTemp);
                    }
                }
                else
                {
                    //save cost: depth to temp0
                    //first depth to mip0 -> to temp1 -> copy to mip1
                    Vector2Int srcSize = new Vector2Int(_camDescriptor.width, _camDescriptor.height);
                    Vector2Int destSize = _MipLevelSize[0];

                    RTHandle src  = useMSAA ? renderingData.cameraData.renderer.GetCopyDepth() : _depthRT;
                    RTHandle dest = _mipRT;

                    Rect viewPort = new Rect(Vector2.zero, srcSize / 2);


                    CoreUtils.SetRenderTarget(cmd, dest, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                        ClearFlag.None, Color.clear, 0, CubemapFace.Unknown, -1);
                    cmd.SetViewport(viewPort);
                    Blitter.BlitTexture(cmd, src, Vector2.one, _hizMaterial, 0);

                    Vector2 _hzbUvFactor = new Vector2(srcSize.x / (float)(destSize.x * 2),
                        srcSize.y / (float)(destSize.y * 2));
                    Vector4 _hzbUvFactorAndInvFactor = new Vector4()
                    {
                        x = _hzbUvFactor.x,
                        y = _hzbUvFactor.y,
                        z = 1 / _hzbUvFactor.x,
                        w = 1 / _hzbUvFactor.y
                    };
                    cmd.SetGlobalVector(HiZConstans._HZBUvFactorAndInvFactor, _hzbUvFactorAndInvFactor);


                    for (int i = 1; i < _numMips; i++)
                    {
                        src = (i == 1 ? _mipRT : _tempRTs[i - 1]);
                        destSize = _MipLevelSize[i];
                        var tempDesc = GetCompatibleDescriptor(HiZConstans.tempRTDesc, destSize.x, destSize.y);
                        dest = reAllocateTempRTIfNeeded(i, tempDesc, FilterMode.Point, TextureWrapMode.Clamp);
                        CoreUtils.SetRenderTarget(cmd, dest, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                            ClearFlag.None, Color.clear, 0, CubemapFace.Unknown, -1);
                        // Blitter.BlitCameraTexture(cmd, src, dest, _hizMaterial, 0);
                        Blitter.BlitTexture(cmd, src, Vector2.one, _hizMaterial, 0);

                        cmd.CopyTexture(dest, 0, 0, _mipRT, 0, i);
                    }
                }

                Vector4 mipParams = new Vector4(_numMips - 1, Time.frameCount & 7, 0, 0);
                cmd.SetGlobalVector(HiZConstans._maxMipmapLevelID, mipParams);
                cmd.SetGlobalTexture(HiZConstans._HZBMipmapDepthTexID, _mipRT);
            }

            context.ExecuteCommandBuffer(cmd);
            // cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        private void ReduceMips(CommandBuffer cmd, RTHandle parent,
            Vector2Int parentSize, int parentMipLevel,
            Vector2Int targetSize, int startMipLevel,
            Vector4 dispatchThreadIdToBufferUV, Vector4 inputViewportMaxBound,
            bool bUseTemp)
        {
            if (parentMipLevel >= _numMips)
                return;

            int end = Mathf.Min(startMipLevel + MaxMipmapLevelOutBatchCount, _numMips);

            //注 这个方式在dx11下不太稳定，
            //todo：mip改图集方式 先搁置
            if (bUseTemp)
            {
                // 读写同一个RT的不同mipLevel,问题是资源没被上一个cs释放,造成parent纹理绑定失败,
                // DX12/Vulkan自带ResourceBarriers没问题,ue有RHI
                // unity只有不太可靠的cmd, 而GraphicsFence对cs无效
                // 这里blit到临时RT 做个双层汉堡 cs/blit/cs/blit/cs
                var tempDesc = GetCompatibleDescriptor(HiZConstans.tempRTDesc, parentSize.x, parentSize.y);
                reAllocateTempRTIfNeeded(parentMipLevel, tempDesc, FilterMode.Point, TextureWrapMode.Clamp);

                CoreUtils.SetRenderTarget(cmd, _tempRTs[parentMipLevel],
                    RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                    ClearFlag.None, Color.clear, 0, CubemapFace.Unknown, -1);

                Blitter.BlitTexture(cmd, parent, Vector2.one, parentMipLevel, false);

                parent = _tempRTs[parentMipLevel];
                parentMipLevel = 0;
            }

            int kernelIndex = end - startMipLevel - 1;

            for (int i = startMipLevel; i < end; i++)
            {
                cmd.SetComputeTextureParam(_hizComputeShader, _kernelHandle[kernelIndex], "_HiZMip" + (i - startMipLevel), _mipRT, i);
            }

            cmd.SetComputeVectorParam(_hizComputeShader, HiZConstans._InputViewportMaxBoundID, inputViewportMaxBound);
            cmd.SetComputeVectorParam(_hizComputeShader, HiZConstans._DispatchThreadIdToBufferUVID, dispatchThreadIdToBufferUV);
            cmd.SetComputeIntParam(_hizComputeShader, HiZConstans._CurrentMipBatchCountID, end - startMipLevel);

            // new renderb
            cmd.SetComputeTextureParam(_hizComputeShader, _kernelHandle[kernelIndex], HiZConstans._parentTextureMipID, parent, parentMipLevel);
            int threadGroupX = Mathf.CeilToInt(targetSize.x / 8f);
            int threadGroupY = Mathf.CeilToInt(targetSize.y / 8f);
            cmd.DispatchCompute(_hizComputeShader, _kernelHandle[kernelIndex], threadGroupX, threadGroupY, 1);
        }

        private RTHandle reAllocateTempRTIfNeeded(int index, RenderTextureDescriptor descriptor, FilterMode filterMode, TextureWrapMode wrapMode)
        {
            if (index >= _numMips || index < 0)
            {
                Debug.LogError(" tempRTsArray : out of index.");
                return null;
            }

            if (!_tempRTsIsCreated[index])
            {
                RenderingUtils.ReAllocateIfNeeded(ref _tempRTs[index], descriptor, filterMode, wrapMode, name: $"_HiZ_Temp{index}");
                _tempRTsIsCreated[index] = true;
            }


            return _tempRTs[index];
        }

        private static RenderTextureDescriptor GetCompatibleDescriptor(RenderTextureDescriptor descriptor, int width, int height)
        {
            descriptor.width = width;
            descriptor.height = height;
            return descriptor;
        }

        public void Dispose()
        {
            // if (_ssrType == SSRType.Simple)
            //     return;
            _mipRT?.Release();
            mipIsCreated = false;
            if (_tempRTs != null)
                foreach (var tempRT in _tempRTs)
                    tempRT?.Release();

            _tempRTsIsCreated = null;
            Shader.DisableKeyword(HiZConstans._ssrHizKeyword);
            Shader.DisableKeyword(HiZConstans._ssrHizUE4Keyword);
        }
    }
}