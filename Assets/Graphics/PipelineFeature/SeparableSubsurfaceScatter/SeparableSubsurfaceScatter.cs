using System;

namespace UnityEngine.Rendering.Universal
{
    [Serializable, VolumeComponentMenu("Post-processing/SeparableSubsurfaceScatter")]
    public sealed class SeparableSubsurfaceScatter : VolumeComponent, IPostProcessComponent
    {
        public ClampedFloatParameter SubsurfaceWidth = new ClampedFloatParameter(0f, 0f, 5f);
        
        public ColorParameter SubsurfaceColor = new ColorParameter(Color.red, false, false, false);
        public ColorParameter SubsurfaceFalloff = new ColorParameter(new Color32(255, 208, 136, 255), false, false, false);

        public BoolParameter FollowSurfaceDepth = new BoolParameter(true);

        public ClampedFloatParameter SurfaceDepthFalloff = new ClampedFloatParameter(1f, 0f, 3f);

        public IntParameter StencilRefValue = new IntParameter(2);

        public bool IsActive() => SubsurfaceWidth.value > 0;

        public bool IsTileCompatible() => false;
    }
}