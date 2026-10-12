// Billboards del bosque lejano (BosqueLejano.cs). Cada arbol son dos cuadros cruzados con su foto, horneada
// sin luz por Assets/Editor/ArbolesLodSur.cs; aca se ilumina con el sol y el ambiente.
//
// TEXCOORD1 = (punto de referencia del LODGroup del arbol en mundo, distancia a la que ese LODGroup se apaga),
// calculado por BosqueLejano. Mas cerca que eso los cuatro vertices del cuadro se juntan en un punto y no se
// dibuja: ahi el Terrain dibuja el arbol de verdad. Los arboles que solo existen como billboard (lejos de la
// zona que recorre el jugador) llevan distancia 0: siempre se dibujan.
//
// La normal es hacia arriba (la pone BosqueLejano): con la del cuadro, la cara que no mira al sol sale negra.
// Por eso la luz va con wrap, como el follaje de la Queñoa.
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

        // Posicion de clip, o un punto degenerado si el arbol de verdad todavia se dibuja.
        float4 PosicionBillboard(float3 positionOS, float4 refCorte)
        {
            float3 ws = TransformObjectToWorld(positionOS);
            float d = distance(refCorte.xyz, _WorldSpaceCameraPos.xyz);
            return d < refCorte.w ? float4(0, 0, 0, 1) : TransformWorldToHClip(ws);
        }

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
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 pie        : TEXCOORD1;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half3  color      : TEXCOORD2;
                half   fog        : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                o.positionCS = PosicionBillboard(v.positionOS.xyz, v.pie);
                o.uv = v.uv;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color.rgb;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                clip(tex.a - _Cutoff);
                float3 n = normalize(i.normalWS);
                half3 albedo = tex.rgb * i.color * _Tinte.rgb * _Brillo;

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

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 pie : TEXCOORD1; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            V vertD(A v)
            {
                V o;
                o.positionCS = PosicionBillboard(v.positionOS.xyz, v.pie);
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

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 pie : TEXCOORD1; };
            struct V { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float2 uv : TEXCOORD1; };

            V vertN(A v)
            {
                V o;
                o.positionCS = PosicionBillboard(v.positionOS.xyz, v.pie);
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
