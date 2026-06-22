Shader "Tactics/VisionFootprintReveal"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "VisionFootprintReveal"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl"

            TEXTURE2D(_PreFogTex);
            SAMPLER(sampler_PreFogTex);

            TEXTURE2D(_FootprintMaskTex);
            SAMPLER(sampler_FootprintMaskTex);

            TEXTURE2D_X(_VisionSceneDepth);
            SAMPLER(sampler_VisionSceneDepth);

            float3 _VisionMaskOrigin;
            float _VisionMaskHalfExtent;

            half SampleWorldFootprintMask(float3 worldPosition)
            {
                if (_VisionMaskHalfExtent <= 0.0001h)
                    return 0.0h;

                float2 offset = worldPosition.xz - _VisionMaskOrigin.xz;
                float2 maskUv = offset / (_VisionMaskHalfExtent * 2.0h) + 0.5h;

                if (maskUv.x < 0.0h || maskUv.x > 1.0h || maskUv.y < 0.0h || maskUv.y > 1.0h)
                    return 0.0h;

                return SAMPLE_TEXTURE2D(_FootprintMaskTex, sampler_FootprintMaskTex, maskUv).r;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 fogged = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half4 preFog = SAMPLE_TEXTURE2D(_PreFogTex, sampler_PreFogTex, uv);

                float deviceDepth = SAMPLE_TEXTURE2D_X(_VisionSceneDepth, sampler_VisionSceneDepth, uv).r;
                float3 worldPosition = ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
                half mask = SampleWorldFootprintMask(worldPosition);

                return lerp(fogged, preFog, mask);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
