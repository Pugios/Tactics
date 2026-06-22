Shader "Tactics/VisionUniformFog"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "VisionUniformFog"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _FogStrength;

            half4 Frag(Varyings input) : SV_Target
            {
                half4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                half brightness = 1.0h - (half)_FogStrength;
                return half4(sceneColor.rgb * brightness, sceneColor.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
