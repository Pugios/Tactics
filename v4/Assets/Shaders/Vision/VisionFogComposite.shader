Shader "Vision/VisionFogComposite"
{
    // Fullscreen pass driven by VisionFogRenderPass.
    // Darkens the camera image wherever the vision mask is black.
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_VisionMaskTex);
            float4 _VisionMaskTex_TexelSize; // auto-filled by Unity (x = 1/width, y = 1/height)

            float _FogStrength;
            float _FogSoftness; // blur radius in mask texels; 0 = hard edge everywhere

            // Mask channels: R = coverage, G = blur allowance (0 near the cone's
            // apex side edges, 1 in the interior). The blur is applied selectively:
            // side edges stay sharp, the cone base and interior slivers are diffused.
            // Purely visual: gameplay visibility comes from VisionSystem raycasts.
            half2 BlurMask(float2 uv)
            {
                float2 o = _VisionMaskTex_TexelSize.xy * _FogSoftness;

                half2 m = SAMPLE_TEXTURE2D(_VisionMaskTex, sampler_LinearClamp, uv).rg * 4.0h;
                m += SAMPLE_TEXTURE2D(_VisionMaskTex, sampler_LinearClamp, uv + float2( o.x, 0)).rg * 2.0h;
                m += SAMPLE_TEXTURE2D(_VisionMaskTex, sampler_LinearClamp, uv + float2(-o.x, 0)).rg * 2.0h;
                m += SAMPLE_TEXTURE2D(_VisionMaskTex, sampler_LinearClamp, uv + float2(0,  o.y)).rg * 2.0h;
                m += SAMPLE_TEXTURE2D(_VisionMaskTex, sampler_LinearClamp, uv + float2(0, -o.y)).rg * 2.0h;
                m += SAMPLE_TEXTURE2D(_VisionMaskTex, sampler_LinearClamp, uv + float2( o.x,  o.y)).rg;
                m += SAMPLE_TEXTURE2D(_VisionMaskTex, sampler_LinearClamp, uv + float2(-o.x,  o.y)).rg;
                m += SAMPLE_TEXTURE2D(_VisionMaskTex, sampler_LinearClamp, uv + float2( o.x, -o.y)).rg;
                m += SAMPLE_TEXTURE2D(_VisionMaskTex, sampler_LinearClamp, uv + float2(-o.x, -o.y)).rg;

                return m / 16.0h;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                half sharpMask = SAMPLE_TEXTURE2D(_VisionMaskTex, sampler_LinearClamp, uv).r;
                half mask = sharpMask;

                if (_FogSoftness > 0.0)
                {
                    half2 blurred = BlurMask(uv);

                    // How "blurrable" is this neighborhood? Normalize the blurred
                    // allowance by the blurred coverage so the base edge (interior
                    // G=1 next to outside G=0) still counts as fully blurrable.
                    half allowance = saturate(blurred.g / max(blurred.r, 0.004h));
                    mask = lerp(sharpMask, blurred.r, allowance);
                }

                // mask = 1 inside the cone (full brightness), 0 outside (darkened).
                half brightness = lerp(1.0h - _FogStrength, 1.0h, mask);
                return half4(sceneColor.rgb * brightness, sceneColor.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
