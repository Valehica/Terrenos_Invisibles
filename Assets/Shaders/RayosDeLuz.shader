// Rayos de luz (god rays) en espacio de pantalla para URP.
// Pasada 0: máscara con el cielo brillante cerca del sol.
// Pasada 1: desenfoque radial hacia la posición del sol en pantalla.
// Pasada 2: suma aditiva sobre la imagen de la cámara.
Shader "Hidden/Terrenos/RayosDeLuz"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        float4 _SunScreenPos;   // xy = posición del sol (UV), z = cuánto mira la cámara hacia el sol
        float4 _RayParams;      // x = muestras, y = densidad, z = decaimiento, w = peso
        float4 _RayTint;
        float  _Threshold;
        float  _Intensity;
        float  _SunRadius;
        ENDHLSL

        Pass
        {
            Name "Mascara"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half3 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

                // Solo el cielo emite rayos: lo que tapa el cielo (cerro, pasto,
                // personaje) queda negro y por eso aparecen los haces entre ellos.
                float depth = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                    float sky = step(depth, 0.000001);
                #else
                    float sky = step(0.999999, depth);
                #endif

                float lum = dot(col, float3(0.2126, 0.7152, 0.0722));
                float bright = saturate((lum - _Threshold) / max(1.0 - _Threshold, 0.001));

                float2 d = uv - _SunScreenPos.xy;
                d.x *= _ScreenParams.x / _ScreenParams.y;
                float nearSun = saturate(1.0 - length(d) / max(_SunRadius, 0.001));

                return half4(col * bright * sky * nearSun * nearSun, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Radial"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                int samples = (int)_RayParams.x;
                float2 delta = (uv - _SunScreenPos.xy) * (_RayParams.y / max(_RayParams.x, 1.0));
                float illum = 1.0;
                half3 acc = 0;

                [loop]
                for (int s = 0; s < 96; s++)
                {
                    if (s >= samples) break;
                    uv -= delta;
                    acc += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb * illum * _RayParams.w;
                    illum *= _RayParams.z;
                }
                return half4(acc, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Suma"
            Blend One One
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings input) : SV_Target
            {
                half3 rays = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord).rgb;
                return half4(rays * _RayTint.rgb * _Intensity * _SunScreenPos.z, 0);
            }
            ENDHLSL
        }
    }
}
