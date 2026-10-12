// Arboles lejanos de Sur (Arbol_*_Lejos.prefab): dos cuadros cruzados con la foto del arbol de frente y de
// costado, horneada sin luz por Assets/Editor/ArbolesLejanosSur.cs; aca se ilumina con el sol y el ambiente.
//
// La normal es hacia arriba (la pone ArbolesLejanosSur): con la del cuadro, la cara que no mira al sol sale
// negra. Por eso la luz va con wrap, como el follaje de la Queñoa.
Shader "Terrenos/ArbolBillboard"
{
    Properties
    {
        _BaseMap ("Atlas", 2D) = "white" {}
        _Cutoff  ("Corte alfa", Range(0, 1)) = 0.5
        _Tinte   ("Tinte", Color) = (1, 1, 1, 1)
        _Brillo  ("Brillo", Range(0, 2)) = 1
        _Wrap    ("Wrap de la luz", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half   _Cutoff;
            half4  _Tinte;
            half   _Brillo;
            half   _Wrap;
        CBUFFER_END

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

        void Recorte(float2 uv)
        {
            clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a - _Cutoff);
        }
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
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half   fog        : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                clip(tex.a - _Cutoff);
                float3 n = normalize(i.normalWS);
                half3 albedo = tex.rgb * _Tinte.rgb * _Brillo;

                Light L = GetMainLight();
                half difusa = saturate((dot(n, L.direction) + _Wrap) / (1.0 + _Wrap));
                half3 ambiente = SampleSH(n);
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS));
                    ambiente *= ao.indirectAmbientOcclusion;
                    difusa   *= ao.directAmbientOcclusion;
                #endif
                half3 col = albedo * (L.color * difusa + ambiente);
                col = MixFog(col, i.fog);
                return half4(col, 1.0);
            }
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

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            V vertD(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half fragD(V i) : SV_Target { Recorte(i.uv); return 0; }
            ENDHLSL
        }

        // Lo necesitan el SSAO y el pase de estilo (leen profundidad/normales): sin este pase el bosque lejano
        // no existe para ellos.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vertN
            #pragma fragment fragN
            #pragma multi_compile_instancing

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float2 uv : TEXCOORD1; };

            V vertN(A v)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                return o;
            }

            half4 fragN(V i) : SV_Target { Recorte(i.uv); return half4(normalize(i.normalWS), 0.0); }
            ENDHLSL
        }
    }
    Fallback Off
}
