Shader "Tactics/VisionEyeDepthCopy"
{
    // Copies the eye camera's device depth into an R32F color target so it can persist
    // across cameras and be sampled by the fog composite pass.
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "VisionEyeDepthCopy"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #define USE_FULL_PRECISION_BLIT_TEXTURE 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 Frag(Varyings input) : SV_Target
            {
                float deviceDepth = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.texcoord, 0).r;
                return float4(deviceDepth, 0.0, 0.0, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
