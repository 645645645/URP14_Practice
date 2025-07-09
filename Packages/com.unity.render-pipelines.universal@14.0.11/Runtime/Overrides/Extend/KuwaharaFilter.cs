using System;

namespace UnityEngine.Rendering.Universal
{
    public enum KuwaharaFilterSampleMode
    {
        Base     = 0,
        Bilinear = 1,
        Sobel    = 2,
        
        BilinearSobel = 3,
    }
    
    [Serializable, VolumeComponentMenuForRenderPipeline("Post-processing/Kuwahara Filter", typeof(UniversalRenderPipeline))]
    public class KuwaharaFilter : VolumeComponent, IPostProcessComponent
    {
        public KuwaharaFilterParameter mode = new KuwaharaFilterParameter(KuwaharaFilterSampleMode.Sobel);

        public ClampedFloatParameter blurRadius = new ClampedFloatParameter(0, 0, 10);
        
        public bool IsActive()
        {
            return active && blurRadius.value > 0;
        }

        public bool IsTileCompatible() => false;

        public Vector4 GetParams => new Vector4(blurRadius.value, 0, 0, 0);
    }
    
    [Serializable]
    public sealed class KuwaharaFilterParameter : VolumeParameter<KuwaharaFilterSampleMode>
    {
        public KuwaharaFilterParameter(KuwaharaFilterSampleMode value, bool overrideState = false) : base(value, overrideState) { }
    }
}