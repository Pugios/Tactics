Shader "Vision/VisionMaskDraw"
{
    // Assigned directly to the VisionCone mesh as its material.
    // The only pass is tagged LightMode=VisionMask, which URP's normal passes never draw,
    // so the cone is invisible in the game view. VisionMaskRenderPass draws it into the
    // mask render texture, writing R=1 wherever the cone covers the screen.
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "VisionMask"
            Tags { "LightMode" = "VisionMask" }

            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0; // x = edge softness from VisionSystem (0 = sharp apex edge, 1 = interior)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float softness : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.softness = input.uv.x;
                return output;
            }

            // R = coverage, G = how much blur this area may receive
            half2 Frag(Varyings input) : SV_Target
            {
                return half2(1.0h, (half)input.softness);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
