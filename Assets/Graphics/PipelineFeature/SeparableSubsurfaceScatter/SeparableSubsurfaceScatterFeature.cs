using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal
{
    public class SeparableSubsurfaceScatterFeature : ScriptableRendererFeature
    {
        [Serializable]
        public class Settings
        {
            public Shader sssssPS;
            public RenderPassEvent m_event = RenderPassEvent.AfterRenderingTransparents;
        }

        SeparableSubsurfaceScatterPass m_sssssPass;

        public Settings settings = new Settings();

        /// <inheritdoc/>
        public override void Create()
        {
            Dispose(true);

            if (!isActive) return;

            if (settings.sssssPS == null)
                settings.sssssPS = Shader.Find("Hidden/Universal Render Pipeline/SeparableSubsurfaceScatter");

            m_sssssPass = new SeparableSubsurfaceScatterPass(settings.sssssPS)
            {
                renderPassEvent = settings.m_event,
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if(renderingData.cameraData.isPreviewCamera)
                return;
            
            renderer.EnqueuePass(m_sssssPass);
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            m_sssssPass.SetUp(renderer.cameraColorTargetHandle, renderer.cameraDepthTargetHandle);
        }

        protected override void Dispose(bool disposing)
        {
            if (m_sssssPass != null)
            {
                m_sssssPass.Dispose();
                m_sssssPass = null;
            }
        }

        class SeparableSubsurfaceScatterPass : ScriptableRenderPass
        {
            SeparableSubsurfaceScatter m_SSSS = null;
            const string k_RenderTag = "Separable SubsurfaceScatter";


            Material ssssMaterial = null;
            RTHandle currentColorTarget;
            RTHandle currentDepthTarget;
            RenderTextureDescriptor m_CameraBufferDescriptor;
            Vector2Int m_LastFrameScreenSize;
            bool m_rtIsCreated = false;

            RTHandle m_TempTargetId;

            bool m_isSceneView = false;

            bool m_UseMsaa;
            const int nSamples = 7;
            private Vector4[] kernel = new Vector4[nSamples];

            public SeparableSubsurfaceScatterPass(Shader ssssPS)
            {
                if (ssssPS == null)
                {
                    Debug.LogError("Shader not found.");
                    return;
                }

                ssssMaterial = CoreUtils.CreateEngineMaterial(ssssPS);
            }

            public void SetUp(in RTHandle colorHandle, in RTHandle depthHandle)
            {
                currentColorTarget = colorHandle;
                currentDepthTarget = depthHandle;//stencel
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                if (!renderingData.cameraData.postProcessEnabled)
                    return;

                m_isSceneView = renderingData.cameraData.isSceneViewCamera;

                m_CameraBufferDescriptor = renderingData.cameraData.cameraTargetDescriptor;

                m_UseMsaa = m_CameraBufferDescriptor.msaaSamples > 1;

                m_CameraBufferDescriptor.depthBufferBits = 0;
                m_CameraBufferDescriptor.autoGenerateMips = false;
                m_CameraBufferDescriptor.memoryless = RenderTextureMemoryless.MSAA | RenderTextureMemoryless.Depth;
                m_CameraBufferDescriptor.msaaSamples = 1;

                CheckScreenResize();
                if (!m_rtIsCreated)
                {
#if UNITY_EDITOR
                    Dispose();
#endif 
                    // m_TempTargetId = RTHandles.Alloc(m_TempTargetId, "_Destination");
                    RenderingUtils.ReAllocateIfNeeded(ref m_TempTargetId, m_CameraBufferDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp,
                        false, 1, 0, name: "_Destination");
                }

                //要透明之后的color
                if (m_UseMsaa)
                    ConfigureInput(ScriptableRenderPassInput.Depth); //read
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (!renderingData.cameraData.postProcessEnabled)
                    return;

                var stack = VolumeManager.instance.stack;
                m_SSSS = stack.GetComponent<SeparableSubsurfaceScatter>();

                if (m_SSSS == null || !m_SSSS.IsActive() || ssssMaterial == null)
                    return;
                var cmd = CommandBufferPool.Get(k_RenderTag);

                var renderer = renderingData.cameraData.renderer;

                var      material    = ssssMaterial;
                RTHandle sourceColor = currentColorTarget;
                RTHandle destDepth   = currentDepthTarget;
                var      tempColor   = m_TempTargetId;

                Vector3 SSSColor = new Vector3(m_SSSS.SubsurfaceColor.value.r, m_SSSS.SubsurfaceColor.value.g, m_SSSS.SubsurfaceColor.value.b);
                Vector3 SSSFalloff = new Vector3(m_SSSS.SubsurfaceFalloff.value.r, m_SSSS.SubsurfaceFalloff.value.g, m_SSSS.SubsurfaceFalloff.value.b);

                CalculateKernel(SSSColor, SSSFalloff, material);

                material.SetFloat("_SSSSDepthFalloff", m_SSSS.SurfaceDepthFalloff.value);

                float distanceToProjectionWindow = 1.0F / Mathf.Tan(0.5F * Mathf.Deg2Rad * (renderingData.cameraData.camera.fieldOfView) * 0.333F);

                material.SetFloat("_DistanceToProjectionWindow", distanceToProjectionWindow);

                if (m_SSSS.FollowSurfaceDepth.value)
                    material.EnableKeyword("SSSS_FOLLOW_SURFACE");
                else
                    material.DisableKeyword("SSSS_FOLLOW_SURFACE");

                material.SetFloat("_RefValue", m_SSSS.StencilRefValue.value);

                material.SetTexture("_CameraDepthTexture", m_UseMsaa ? renderer.GetCopyDepth() : destDepth);

                //开启msaa, 如果使用内置copy depth pass会丢掉stencil，而 硬解 msaa很酸爽，所以这玩意定位就是跟msaa不熟
                {
                    cmd.SetGlobalVector("_SSSSDirection", new Vector4(m_SSSS.SubsurfaceWidth.value, 0f, 0f, 0f));
                    CoreUtils.SetRenderTarget(cmd, tempColor, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                        destDepth, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare);
                    Blitter.BlitTexture(cmd, sourceColor, Vector2.one, material, 0);
                    
                    cmd.SetGlobalVector("_SSSSDirection", new Vector4(0f, m_SSSS.SubsurfaceWidth.value, 0f, 0f));
                    CoreUtils.SetRenderTarget(cmd, sourceColor, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                        destDepth, RenderBufferLoadAction.Load, RenderBufferStoreAction.DontCare);
                    Blitter.BlitTexture(cmd, tempColor, Vector2.one, material, 0);
                }
                
                context.ExecuteCommandBuffer(cmd);
                // cmd.Clear();
                CommandBufferPool.Release(cmd);
            }
            
            private Vector3 Gaussian(float variance, float r, Vector3 falloff)
            {
                /**
                 * We use a falloff to modulate the shape of the profile. Big falloffs
                 * spreads the shape making it wider, while small falloffs make it
                 * narrower.
                 */
                Vector3 g = Vector3.zero;

                for (int i = 0; i < 3; i++)
                {
                    float rr = r / (0.0001f + falloff[i]);
                    g[i] = Mathf.Exp((-(rr * rr)) / (2.0f * variance)) / (2.0f * Mathf.PI * variance);
                }

                return g;
            }


            private Vector3 Profile(float r, Vector3 falloff)
            {
                /**
                 * We used the red channel of the original skin profile defined in
                 * [d'Eon07] for all three channels. We noticed it can be used for green
                 * and blue channels (scaled using the falloff parameter) without
                 * introducing noticeable differences and allowing for total control over
                 * the profile. For example, it allows to create blue SSS gradients, which
                 * could be useful in case of rendering blue creatures.
                 */
                return // 0.233f * gaussian(0.0064f, r) + /* We consider this one to be directly bounced light, accounted by the strength parameter (see @STRENGTH) */
                    0.100f * Gaussian(0.0484f, r, falloff) +
                    0.118f * Gaussian(0.187f, r, falloff) +
                    0.113f * Gaussian(0.567f, r, falloff) +
                    0.358f * Gaussian(1.99f, r, falloff) +
                    0.078f * Gaussian(7.41f, r, falloff);
            }

            public void CalculateKernel(Vector3 strength, Vector3 falloff, Material material)
            {
                const float RANGE = nSamples > 20 ? 3.0f : 2.0f;
                const float EXPONENT = 2.0f;

                // Calculate the offsets:

                float step = 2.0f * RANGE / (nSamples - 1);
                for (int i = 0; i < nSamples; i++)
                {
                    float o = -RANGE + i * step;
                    float sign = o < 0.0f ? -1.0f : 1.0f;
                    kernel[i].w = RANGE * sign * Mathf.Abs(Mathf.Pow(o, EXPONENT)) / Mathf.Pow(RANGE, EXPONENT);
                }

                // Calculate the weights:
                for (int i = 0; i < nSamples; i++)
                {
                    float w0 = i > 0 ? Mathf.Abs(kernel[i].w - kernel[i - 1].w) : 0.0f;
                    float w1 = i < nSamples - 1 ? Mathf.Abs(kernel[i].w - kernel[i + 1].w) : 0.0f;
                    float area = (w0 + w1) / 2.0f;
                    Vector3 tt = area * Profile(kernel[i].w, falloff);
                    kernel[i] = new Vector4(tt.x, tt.y, tt.z, kernel[i].w);
                }

                // We want the offset 0.0 to come first:
                Vector4 t = kernel[nSamples / 2];
                for (int i = nSamples / 2; i > 0; i--)
                    kernel[i] = kernel[i - 1];
                kernel[0] = t;

                // Calculate the sum of the weights, we will need to normalize them below:
                Vector3 sum = Vector3.zero;
                for (int i = 0; i < nSamples; i++)
                {
                    sum.x += kernel[i].x;
                    sum.y += kernel[i].y;
                    sum.z += kernel[i].z;
                }

                // Normalize the weights:
                for (int i = 0; i < nSamples; i++)
                {
                    Vector4 vecx = kernel[i];
                    vecx.x /= sum.x;
                    vecx.y /= sum.y;
                    vecx.z /= sum.z;
                    kernel[i] = vecx;
                }

                // Tweak them using the desired strength. The first one is:
                //     lerp(1.0, kernel[0].rgb, strength)
                Vector4 vec = kernel[0];
                vec.x = (1.0f - strength.x) * 1.0f + strength.x * kernel[0].x;
                vec.y = (1.0f - strength.y) * 1.0f + strength.y * kernel[0].y;
                vec.z = (1.0f - strength.z) * 1.0f + strength.z * kernel[0].z;
                kernel[0] = vec;

                for (int i = 1; i < nSamples; i++)
                {
                    var vect = kernel[i];
                    vect.x *= strength.x;
                    vect.y *= strength.y;
                    vect.z *= strength.z;
                    kernel[i] = vect;
                }

                material.SetVectorArray("_Kernel", kernel);
            }

            void CheckScreenResize()
            {
                if ((m_CameraBufferDescriptor.width != m_LastFrameScreenSize.x) ||
                    (m_CameraBufferDescriptor.height != m_LastFrameScreenSize.y))
                {
                    m_LastFrameScreenSize = new Vector2Int(m_CameraBufferDescriptor.width, m_CameraBufferDescriptor.height);

                    m_rtIsCreated = false;
                }
            }

            public void Dispose()
            {
                m_TempTargetId?.Release();
                m_rtIsCreated = false;
            }
        }
    }
}