Shader "Tactics/VisionFogComposite"
{
    // Fullscreen fog pass using the vision depth map ("shadow mapping" with the player's eye as the light).
    // For every screen pixel: reconstruct the world position, then test whether any part of a standing
    // enemy body at that spot (head/chest/knees/feet sample heights) has a clear line to the eye.
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "VisionFogComposite"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ VISION_DEBUG_SHOW_MASK

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl"

            TEXTURE2D_X(_VisionSceneDepth);
            SAMPLER(sampler_VisionSceneDepth);

            // Vision depth map: raw device depth rendered from the player's eye, copied to R32F.
            TEXTURE2D_FLOAT(_VisionEyeDepth);

            float _FogStrength;
            float _VisionHasValidMask;

            float4x4 _VisionEyeVP;
            float4x4 _VisionEyeView;
            float4 _VisionEyePosWS;
            float4 _VisionEyeForwardWS;
            float4 _VisionEyeZBufferParams;
            float4 _VisionEyeDepthParams;
            float4 _VisionSampleHeights;

            bool IsBackgroundDepth(float rawDepth)
            {
#if UNITY_REVERSED_Z
                return rawDepth < 1e-4;
#else
                return rawDepth > 1.0 - 1e-4;
#endif
            }

            float EyeLinearDepth(float rawDepth)
            {
                return rcp(_VisionEyeZBufferParams.z * rawDepth + _VisionEyeZBufferParams.w);
            }

            // Positive view-space distance in front of the eye (matches Unity's linear eye depth).
            float SampleEyeLinearDepth(float3 worldPosWS)
            {
                float4 viewPos = mul(_VisionEyeView, float4(worldPosWS, 1.0));
                return -viewPos.z;
            }

            half SamplePointVisibility(float3 samplePosWS)
            {
                float4 clipPos = mul(_VisionEyeVP, float4(samplePosWS, 1.0));
                if (clipPos.w <= 0.0)
                    return 0.0h;

                float3 ndc = clipPos.xyz / clipPos.w;
                if (max(abs(ndc.x), abs(ndc.y)) >= 1.0)
                    return 0.0h;

                float2 uv = ndc.xy * 0.5 + 0.5;
#if UNITY_UV_STARTS_AT_TOP
                uv.y = 1.0 - uv.y;
#endif

                float sampleDepth = SampleEyeLinearDepth(samplePosWS);
                float requiredClearance = sampleDepth - _VisionEyeDepthParams.y;
                float texelSize = _VisionEyeDepthParams.x;

                half lit = 0.0h;

                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float rawDepth = SAMPLE_TEXTURE2D_LOD(
                            _VisionEyeDepth,
                            sampler_PointClamp,
                            uv + float2(x, y) * texelSize,
                            0).r;

                        // Visible when the nearest occluder along this direction is at or beyond the sample point.
                        lit += EyeLinearDepth(rawDepth) >= requiredClearance ? (1.0h / 9.0h) : 0.0h;
                    }
                }

                return lit;
            }

#if defined(VISION_DEBUG_SHOW_MASK)
            // Color-coded diagnostic for the head sample:
            //   purple = no valid mask, red = behind eye, orange = outside FoV,
            //   green  = visible, blue = occluded (brightness = occluder / sample depth ratio).
            half4 DebugVisionState(float3 worldPosition)
            {
                if (_VisionHasValidMask < 0.5h)
                    return half4(0.4h, 0.0h, 0.6h, 1.0h);

                float3 samplePos = worldPosition + float3(0.0, _VisionSampleHeights.x, 0.0);
                float4 clipPos = mul(_VisionEyeVP, float4(samplePos, 1.0));
                if (clipPos.w <= 0.0)
                    return half4(1.0h, 0.0h, 0.0h, 1.0h);

                float3 ndc = clipPos.xyz / clipPos.w;
                if (max(abs(ndc.x), abs(ndc.y)) >= 1.0)
                    return half4(1.0h, 0.5h, 0.0h, 1.0h);

                float2 uv = ndc.xy * 0.5 + 0.5;
#if UNITY_UV_STARTS_AT_TOP
                uv.y = 1.0 - uv.y;
#endif

                float rawDepth = SAMPLE_TEXTURE2D_LOD(_VisionEyeDepth, sampler_PointClamp, uv, 0).r;
                float occluderDistance = EyeLinearDepth(rawDepth);
                float sampleDistance = SampleEyeLinearDepth(samplePos);

                if (occluderDistance >= sampleDistance - _VisionEyeDepthParams.y)
                    return half4(0.0h, 1.0h, 0.0h, 1.0h);

                return half4(0.0h, 0.0h, (half)saturate(occluderDistance / max(sampleDistance, 0.001)), 1.0h);
            }
#endif

            half SampleWorldVisionMask(float3 worldPosition)
            {
                if (_VisionHasValidMask < 0.5h)
                    return 0.0h;

                half mask = 0.0h;

                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    float3 samplePos = worldPosition + float3(0.0, _VisionSampleHeights[i], 0.0);
                    mask = max(mask, SamplePointVisibility(samplePos));
                }

                return mask;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float deviceDepth = SAMPLE_TEXTURE2D_X(_VisionSceneDepth, sampler_VisionSceneDepth, uv).r;

#if defined(VISION_DEBUG_SHOW_MASK)
                if (IsBackgroundDepth(deviceDepth))
                    return half4(0.25h, 0.25h, 0.25h, 1.0h);

                return DebugVisionState(ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP));
#endif

                half mask = 0.0h;
                if (!IsBackgroundDepth(deviceDepth))
                {
                    float3 worldPosition = ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
                    mask = SampleWorldVisionMask(worldPosition);
                }

                half4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half visibility = 1.0h - (half)_FogStrength * (1.0h - mask);
                return half4(sceneColor.rgb * visibility, sceneColor.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
