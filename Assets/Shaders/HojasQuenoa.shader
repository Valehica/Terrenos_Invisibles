// Follaje de la Queñoa (escena Norte).
//
// Las hojas vienen de Blender con el color horneado en el color de vertice
// ("Color_Unity", ver Tools/Blender/hojas_quenoa.py). URP/Lit ignora el color de
// vertice, por eso este shader propio.
//
// Las normales de la malla combinada no son las de cada hoja: el menu
// Terrenos > Norte > Actualizar Queñoa las mezcla con la direccion desde el
// centro de cada mancha de follaje, asi la copa se ilumina como un volumen
// (sombra suave de un lado, luz del otro) y no como miles de planos sueltos.
// Por eso no se dan vuelta en la cara trasera.
Shader "Terrenos/HojasQuenoa"
{
    Properties
    {
        _Tinte        ("Tinte", Color) = (1, 1, 1, 1)
        _Brillo       ("Brillo", Range(0, 2)) = 1
        _Wrap         ("Wrap de la luz", Range(0, 1)) = 0.45
        _Translucidez ("Translucidez a contraluz", Range(0, 1)) = 0.35
        _ColorTrans   ("Color a contraluz", Color) = (0.85, 0.95, 0.45, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Tinte;
            half  _Brillo;
            half  _Wrap;
            half  _Translucidez;
            half4 _ColorTrans;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half3  color      : TEXCOORD2;
                half   fog        : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS   = TransformObjectToWorldNormal(v.normalOS);
                // Blender guarda el color de vertice en sRGB y Unity no lo convierte.
                o.color      = SRGBToLinear(v.color.rgb);
                o.fog        = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS);
                half3 albedo = i.color * _Tinte.rgb * _Brillo;

                Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half sombra = L.shadowAttenuation * L.distanceAttenuation;

                half difusa = saturate((dot(n, L.direction) + _Wrap) / (1.0 + _Wrap));
                half3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half contraluz = pow(saturate(dot(-V, L.direction)), 4.0) * _Translucidez;
                half3 ambiente = SampleSH(n);

                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS));
                    ambiente *= ao.indirectAmbientOcclusion;
                    difusa   *= ao.directAmbientOcclusion;
                #endif

                half3 col = albedo * (L.color * (difusa * sombra) + ambiente)
                          + albedo * _ColorTrans.rgb * L.color * (contraluz * sombra);
                col = MixFog(col, i.fog);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vertS
            #pragma fragment fragS
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };

            V vertS(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float3 nws = TransformObjectToWorldNormal(v.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 dirLuz = normalize(_LightPosition - ws);
                #else
                    float3 dirLuz = _LightDirection;
                #endif
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(ws, nws, dirLuz));
                #if UNITY_REVERSED_Z
                    o.positionCS.z = min(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    o.positionCS.z = max(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return o;
            }

            half4 fragS(V i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vertD
            #pragma fragment fragD
            #pragma multi_compile_instancing

            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };

            V vertD(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half fragD(V i) : SV_Target { return 0; }
            ENDHLSL
        }

        // Lo necesita el SSAO: sin este pase la copa no aparece en la textura de
        // normales y el oclusor queda calculado contra el suelo de atras.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vertN
            #pragma fragment fragN
            #pragma multi_compile_instancing

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            V vertN(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 fragN(V i) : SV_Target { return half4(normalize(i.normalWS), 0.0); }
            ENDHLSL
        }
    }
    Fallback Off
}
