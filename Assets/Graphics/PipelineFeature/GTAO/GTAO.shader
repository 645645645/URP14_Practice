Shader "Hidden/Custom/GTAO"
{

    HLSLINCLUDE
    
#pragma enable_d3d11_debug_symbols enable_vulkan_debug_symbols
    #include "Assets/Graphics/PipelineFeature/GTAO/GTAOPass.hlsl"

    
    ENDHLSL
            

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"
        }
        ZTest Always
        ZWrite Off
        Blend Off
        Cull Off

        //0
        Pass
        {
            Name "GTAO ReduceNormal"

            HLSLPROGRAM
            #pragma vertex Vert_GTAO
            #pragma fragment Fragment_ReduceNormal
            ENDHLSL
        }

        //1
        Pass
        {
            Name "GTAO Resolve"

            HLSLPROGRAM
            #pragma vertex Vert_GTAO
            #pragma fragment Fragment_ResolveGTAO

            ENDHLSL
        }

        //2
        Pass
        {
            Name "GTAO CopyPackAO"

            HLSLPROGRAM
            #pragma vertex Vert_GTAO
            #pragma fragment Fragment_CopyAONormal
            #pragma multi_compile_local_fragment _ _PACKNORMALAO
            ENDHLSL
        }
        
        //3
        Pass
        {
            Name "GTAO BlurX"

            HLSLPROGRAM
            #pragma vertex Vert_GTAO
            #pragma fragment Fragment_HBlur
            #pragma multi_compile_local_fragment _ _PACKNORMALAO
            ENDHLSL
        }
        
        //4
        Pass
        {
            Name "GTAO BlurY"

            HLSLPROGRAM
            #pragma vertex Vert_GTAO
            #pragma fragment Fragment_VBlur
            #pragma multi_compile_local_fragment _ _PACKNORMALAO

            ENDHLSL
        }

        //5
        Pass
        {
            Name "GTAO Temporal"

            HLSLPROGRAM
            #pragma vertex Vert_GTAO
            #pragma fragment Fragmen_Temporal
            ENDHLSL
        }

        //6
        Pass
        {
            Name "GTAO Combine"

            HLSLPROGRAM
            #pragma vertex Vert_GTAO
            #pragma fragment Fragmen_Combine
            ENDHLSL
        }

        //7
        Pass
        {
            Name "GTAO Debug"
            
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex Vert_GTAO
            #pragma fragment Fragment_DebugBentNormal
            ENDHLSL
        }

        //8
        Pass
        {
            Name "GTAO Debug"
            
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex Vert_GTAO
            #pragma fragment Fragment_DebugAO
            ENDHLSL
        }

        //9
        Pass
        {
            Name "GTAO Debug"
            
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex Vert_GTAO
            #pragma fragment Fragment_DebugRO
            ENDHLSL
        }        

        //10
        Pass
        {
            Name "GTAO Debug"
            
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex Vert_GTAO
            #pragma fragment Fragment_DebugReflections
            ENDHLSL
        }
    }
}