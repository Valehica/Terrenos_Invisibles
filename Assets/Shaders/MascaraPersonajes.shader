// Mascara de personajes para RenderShaderURP: pinta 1 donde se ve un renderer de la
// capa de render "Personajes". El pase de estilo la usa para no aplicarles la
// cuantizacion de luz ni las bandas de color, que estan calibradas para el paisaje:
// la banda de arena de Norte desaturaba y quemaba el pelaje canela de la vicuña, la
// del cielo azulaba la panza blanca en sombra, y la cuantizacion dibujaba una linea
// gris justo donde el canela pasa a blanco.
// No usa el depth buffer de la camara como attachment (tendria que coincidir en MSAA
// con la mascara): compara a mano contra _CameraDepthTexture para no marcar lo tapado.
Shader "Hidden/Terrenos/MascaraPersonajes"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Mascara"
            ZWrite Off ZTest Always Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.positionCS.xy / _ScaledScreenParams.xy;
                float escena = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float propia = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                // Margen relativo: la profundidad de la camara y la de este draw salen
                // de la misma malla, pero no son bit a bit iguales.
                clip(escena - propia + 0.02 + propia * 0.01);
                return 1;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
