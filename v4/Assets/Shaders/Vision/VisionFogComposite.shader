Shader "Tactics/VisionFogComposite"
{
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

            float _FogStrength;
            float _VisionHasValidMask;

            int _VisionFootprintVertCount;
            float4 _VisionFootprintVerts[64];

            bool IsBackgroundDepth(float rawDepth)
            {
#if UNITY_REVERSED_Z
                return rawDepth < 1e-4;
#else
                return rawDepth > 1.0 - 1e-4;
#endif
            }

            bool PointInPolygonXZ(float2 p)
            {
                int count = _VisionFootprintVertCount;
                if (count < 3)
                    return false;

                bool inside = false;

                [loop]
                for (int i = 0; i < count; i++)
                {
                    int j = i - 1;
                    if (j < 0)
                        j = count - 1;

                    float2 vi = _VisionFootprintVerts[i].xy;
                    float2 vj = _VisionFootprintVerts[j].xy;

                    bool yStraddle = (vi.y > p.y) != (vj.y > p.y);
                    if (!yStraddle)
                        continue;

                    float t = (p.y - vi.y) / (vj.y - vi.y);
                    float xIntersect = vi.x + t * (vj.x - vi.x);
                    if (p.x < xIntersect)
                        inside = !inside;
                }

                return inside;
            }

            half SampleWorldFootprintMask(float3 worldPosition)
            {
                if (_VisionHasValidMask < 0.5h)
                    return 0.0h;

                return PointInPolygonXZ(worldPosition.xz) ? 1.0h : 0.0h;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float deviceDepth = SAMPLE_TEXTURE2D_X(_VisionSceneDepth, sampler_VisionSceneDepth, uv).r;

                half mask = 0.0h;
                if (!IsBackgroundDepth(deviceDepth))
                {
                    float3 worldPosition = ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
                    mask = SampleWorldFootprintMask(worldPosition);
                }

#if defined(VISION_DEBUG_SHOW_MASK)
                return half4(mask, mask, mask, 1.0h);
#endif

                half4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half visibility = 1.0h - (half)_FogStrength * (1.0h - mask);
                return half4(sceneColor.rgb * visibility, sceneColor.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
