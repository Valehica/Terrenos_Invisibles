// Agua del mar estilo Zelda: Breath of the Wild.
//
// Lo somero es transparente y turquesa (se ve el fondo con causticas), lo hondo
// es azul saturado, el cielo se refleja por Fresnel con un degradado propio, el
// sol deja una estela de destellos y la orilla tiene espuma blanca de borde neto
// con lineas que entran hacia la playa.
//
// La profundidad sale del depth buffer (vertical, en metros), no de un bake: la
// espuma aparece sola contra rocas, la canoa o cualquier cosa opaca, y la marea
// (MareaController mueve el transform) corre la orilla sin tocar nada.
//
// Las olas grandes son tres Gerstner por vertice. OleajeMuestreo.cs replica la
// misma cuenta en C# para que la canoa flote: si se cambia aca, cambiarla alla.
// Largo de onda minimo ~65 m: la OceanGrid de Centro tiene 8 m por quad.
//
// El agua no escribe profundidad, asi que el pase de estilo (RenderShaderURP) la
// trata como cielo; la perspectiva aerea se aplica aca con sus globales.
Shader "Terrenos/Mar"
{
    Properties
    {
        [Header(Color del agua)]
        _ColorSomero ("Somero", Color) = (0.30, 0.86, 0.80, 1)
        _ColorMedio ("Medio", Color) = (0.06, 0.56, 0.76, 1)
        _ColorProfundo ("Profundo", Color) = (0.02, 0.22, 0.50, 1)
        _ProfundidadMedia ("Profundidad del color medio (m)", Float) = 2.5
        _ProfundidadPlena ("Profundidad del color profundo (m)", Float) = 16
        _TinteFondo ("Tinte del fondo visto a traves", Color) = (0.55, 0.95, 0.90, 1)
        _Claridad ("Claridad (m de agua que se ven)", Float) = 3
        _BrilloCuerpo ("Brillo del cuerpo", Range(0, 2)) = 0.85
        _Refraccion ("Refraccion", Range(0, 0.1)) = 0.025

        [Header(Reflejo del cielo)]
        _ColorHorizonte ("Horizonte", Color) = (0.78, 0.90, 0.96, 1)
        _ColorCenit ("Cenit", Color) = (0.36, 0.62, 0.92, 1)
        _MezclaCieloSH ("Mezcla con el cielo real (SH)", Range(0, 1)) = 0.3
        _FresnelMin ("Fresnel minimo", Range(0, 1)) = 0.03
        _FresnelMax ("Fresnel maximo", Range(0, 1)) = 0.8
        _FresnelPotencia ("Fresnel potencia", Range(1, 10)) = 4

        [Header(Ondas chicas)]
        [NoScaleOffset][Normal] _OndasN ("Normal de ondas", 2D) = "bump" {}
        _OndasEscala ("Escala (1/m)", Float) = 0.07
        _OndasFuerza ("Fuerza", Range(0, 2)) = 0.55
        _OndasVelocidad ("Velocidad (m/s)", Float) = 0.7
        _OndasDistancia ("Distancia donde se calman (m)", Float) = 350

        [Header(Olas grandes Gerstner  xy direccion  z pendiente  w largo)]
        _OlaA ("Ola A", Vector) = (0.25, 1, 0.020, 140)
        _OlaB ("Ola B", Vector) = (-0.45, 1, 0.018, 96)
        _OlaC ("Ola C", Vector) = (0.70, 1, 0.012, 70)
        _VelocidadOlas ("Velocidad de las olas", Float) = 1

        [Header(Sol)]
        _Brillo ("Tamano del reflejo (potencia)", Float) = 350
        _BrilloIntensidad ("Intensidad del reflejo", Float) = 2.5
        _DestellosEscala ("Escala de destellos (1/m)", Float) = 0.35
        _DestellosPotencia ("Abanico de destellos (potencia)", Float) = 24
        _DestellosIntensidad ("Intensidad de destellos", Float) = 7

        [Header(Espuma)]
        [NoScaleOffset] _Ruido ("Ruido (R causticas, G espuma, B destellos)", 2D) = "gray" {}
        _ColorEspuma ("Color (a = opacidad)", Color) = (1, 1, 1, 0.95)
        _EspumaAncho ("Orilla: profundidad cubierta (m)", Float) = 0.35
        _EspumaRuidoEscala ("Escala del ruido (1/m)", Float) = 0.09
        _LineasAlcance ("Lineas: hasta que profundidad (m)", Float) = 1.4
        _LineasFrecuencia ("Lineas por metro de profundidad", Float) = 1.6
        _LineasVelocidad ("Velocidad de las lineas", Float) = 0.22
        _LineasGrosor ("Grosor de las lineas", Range(0.02, 0.5)) = 0.14

        [Header(Causticas)]
        [Toggle(_CAUSTICAS)] _Causticas ("Causticas", Float) = 1
        _CausticasEscala ("Escala (1/m)", Float) = 0.28
        _CausticasIntensidad ("Intensidad", Float) = 1.3
        _CausticasProfundidad ("Se apagan a (m)", Float) = 4
        _CausticasVelocidad ("Velocidad", Float) = 0.04

        [Header(Borde)]
        _BordeSuave ("Fundido en la orilla (m)", Float) = 0.06
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Mar"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma shader_feature_local_fragment _CAUSTICAS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorSomero, _ColorMedio, _ColorProfundo, _TinteFondo;
                float _ProfundidadMedia, _ProfundidadPlena, _Claridad, _BrilloCuerpo, _Refraccion;
                half4 _ColorHorizonte, _ColorCenit;
                float _MezclaCieloSH, _FresnelMin, _FresnelMax, _FresnelPotencia;
                float _OndasEscala, _OndasFuerza, _OndasVelocidad, _OndasDistancia;
                float4 _OlaA, _OlaB, _OlaC;
                float _VelocidadOlas;
                float _Brillo, _BrilloIntensidad, _DestellosEscala, _DestellosPotencia, _DestellosIntensidad;
                half4 _ColorEspuma;
                float _EspumaAncho, _EspumaRuidoEscala, _LineasAlcance, _LineasFrecuencia, _LineasVelocidad, _LineasGrosor;
                float _Causticas, _CausticasEscala, _CausticasIntensidad, _CausticasProfundidad, _CausticasVelocidad;
                float _BordeSuave;
            CBUFFER_END

            TEXTURE2D(_OndasN); SAMPLER(sampler_OndasN);
            TEXTURE2D(_Ruido);  SAMPLER(sampler_Ruido);

            // Los publica RenderShaderURP (el pase de estilo).
            float4 _AireColorGlobal;
            float4 _AireParamsGlobal;   // x = dist inicio, y = dist plena, z = tinte, w = desaturacion

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half   fogFactor  : TEXCOORD2;
            };

            // Ola de Gerstner (formulacion de Catlike Coding). Acumula las derivadas
            // para sacar la normal exacta de la superficie desplazada.
            float3 Gerstner(float4 ola, float2 p, inout float3 tangente, inout float3 binormal)
            {
                float k = TWO_PI / max(ola.w, 0.01);
                float c = sqrt(9.8 / k) * _VelocidadOlas;
                float2 d = normalize(ola.xy);
                float f = k * (dot(d, p) - c * _Time.y);
                float a = ola.z / k;
                float s, co;
                sincos(f, s, co);
                tangente += float3(-d.x * d.x * ola.z * s, d.x * ola.z * co, -d.x * d.y * ola.z * s);
                binormal += float3(-d.x * d.y * ola.z * s, d.y * ola.z * co, -d.y * d.y * ola.z * s);
                return float3(d.x * a * co, a * s, d.y * a * co);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 pos = TransformObjectToWorld(v.positionOS.xyz);
                float3 tangente = float3(1, 0, 0), binormal = float3(0, 0, 1);
                float3 desp = Gerstner(_OlaA, pos.xz, tangente, binormal)
                            + Gerstner(_OlaB, pos.xz, tangente, binormal)
                            + Gerstner(_OlaC, pos.xz, tangente, binormal);
                pos += desp;
                o.positionWS = pos;
                o.normalWS = normalize(cross(binormal, tangente));
                o.positionCS = TransformWorldToHClip(pos);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 pos = i.positionWS;
                float2 uvPantalla = GetNormalizedScreenSpaceUV(i.positionCS);
                float3 V = GetWorldSpaceNormalizeViewDir(pos);
                float distCam = length(_WorldSpaceCameraPos - pos);
                float eyeAgua = max(-TransformWorldToView(pos).z, 1e-3);
                float t = _Time.y;

                // ---- normal: Gerstner + dos capas de ondas chicas ----
                // La segunda capa va girada 35 grados y con escala no entera: dos capas
                // alineadas dibujan una reticula.
                float2 pA = pos.xz + float2(1.0, 0.55) * (t * _OndasVelocidad);
                float2 pB = float2(0.819 * pos.x - 0.574 * pos.z, 0.574 * pos.x + 0.819 * pos.z) * 1.73
                          + float2(-0.7, 1.0) * (t * _OndasVelocidad * 1.3);
                half3 n1 = UnpackNormal(SAMPLE_TEXTURE2D(_OndasN, sampler_OndasN, pA * _OndasEscala));
                half3 n2 = UnpackNormal(SAMPLE_TEXTURE2D(_OndasN, sampler_OndasN, pB * _OndasEscala));
                half2 ondas = (n1.xy + n2.xy) * _OndasFuerza;
                // De lejos las ondas chicas solo hacen ruido: se calman.
                float calma = saturate(distCam / _OndasDistancia);
                ondas *= lerp(1.0, 0.2, calma * calma);
                float3 N = normalize(normalize(i.normalWS) + float3(ondas.x, 0, ondas.y));

                Light luz = GetMainLight(TransformWorldToShadowCoord(pos));
                half sombra = luz.shadowAttenuation * luz.distanceAttenuation;
                half3 ambiente = SampleSH(half3(0, 1, 0));

                // ---- profundidad vertical bajo este punto ----
                float rawFondo = SampleSceneDepth(uvPantalla);
                float3 fondo = ComputeWorldSpacePosition(uvPantalla, rawFondo, UNITY_MATRIX_I_VP);
                float prof = max(pos.y - fondo.y, 0);

                // ---- refraccion: se desvia la vista del fondo, salvo que caiga sobre
                // algo que esta delante del agua (eso daria halos sobre la canoa) ----
                float2 uvR = uvPantalla + N.xz * (_Refraccion * saturate(prof * 0.5));
                float rawR = SampleSceneDepth(uvR);
                if (LinearEyeDepth(rawR, _ZBufferParams) < eyeAgua)
                {
                    uvR = uvPantalla;
                    rawR = rawFondo;
                }
                float3 fondoR = ComputeWorldSpacePosition(uvR, rawR, UNITY_MATRIX_I_VP);
                float profR = max(pos.y - fondoR.y, 0);
                // Largo del rayo dentro del agua: mirando de canto se ve menos fondo.
                float camino = length(fondoR - pos);
                half3 colFondo = SampleSceneColor(uvR);

                #if defined(_CAUSTICAS)
                {
                    float tc = t * _CausticasVelocidad;
                    float2 uc = fondoR.xz * _CausticasEscala;
                    half c1 = SAMPLE_TEXTURE2D(_Ruido, sampler_Ruido, uc + float2(tc, tc * 0.6)).r;
                    half c2 = SAMPLE_TEXTURE2D(_Ruido, sampler_Ruido, uc * 1.37 + float2(-tc * 0.8, tc * 0.45) + 0.37).r;
                    half caus = min(c1, c2) * _CausticasIntensidad
                              * saturate(1 - profR / _CausticasProfundidad) * saturate(profR * 3);
                    colFondo += colFondo * caus * luz.color * sombra * 2;
                }
                #endif

                // ---- cuerpo del agua ----
                half3 colAgua = lerp(_ColorSomero.rgb, _ColorMedio.rgb, saturate(prof / _ProfundidadMedia));
                colAgua = lerp(colAgua, _ColorProfundo.rgb, smoothstep(_ProfundidadMedia, _ProfundidadPlena, prof));
                half3 luzCuerpo = luz.color * saturate(luz.direction.y * 1.5 + 0.2) * lerp(0.55, 1.0, sombra) + ambiente;
                colAgua *= luzCuerpo * _BrilloCuerpo;

                half visible = exp(-camino / max(_Claridad, 0.01));
                half3 fondoTenido = colFondo * lerp(half3(1, 1, 1), _TinteFondo.rgb, saturate(profR / _ProfundidadMedia + 0.25));
                half3 col = lerp(colAgua, fondoTenido, visible);

                // ---- reflejo del cielo ----
                float3 R = reflect(-V, N);
                half3 cielo = lerp(_ColorHorizonte.rgb, _ColorCenit.rgb, sqrt(saturate(R.y)));
                cielo = lerp(cielo, SampleSH(R), _MezclaCieloSH);
                float fres = _FresnelMin + (_FresnelMax - _FresnelMin) * pow(1 - saturate(dot(N, V)), _FresnelPotencia);
                col = lerp(col, cielo, fres);

                // ---- sol: reflejo de borde neto + destellos alrededor ----
                // Con la normal completa el reflejo se rompe en manchas por todo el mar
                // (parecen espuma); con la ondulacion atenuada queda una estela.
                float3 H = normalize(luz.direction + V);
                float3 Nsol = normalize(normalize(i.normalWS) + float3(ondas.x, 0, ondas.y) * 0.3);
                half lobulo = pow(saturate(dot(Nsol, H)), _Brillo);
                lobulo = smoothstep(0.02, 0.6, lobulo);
                // Destellos: puntos de la textura que titilan con la ondulacion, solo
                // donde la normal completa mira al sol.
                float2 ud = pos.xz * _DestellosEscala;
                half d1 = SAMPLE_TEXTURE2D(_Ruido, sampler_Ruido, ud + N.xz * 0.8 + t * 0.03).b;
                half d2 = SAMPLE_TEXTURE2D(_Ruido, sampler_Ruido, ud * 0.71 - N.xz * 0.5 - t * 0.021).b;
                half destellos = saturate(d1 * d2 * 8) * pow(saturate(dot(N, H)), _DestellosPotencia) * _DestellosIntensidad;
                col += (lobulo * _BrilloIntensidad + destellos) * luz.color * sombra;

                // ---- espuma ----
                half ruido = SAMPLE_TEXTURE2D(_Ruido, sampler_Ruido, pos.xz * _EspumaRuidoEscala + float2(t * 0.020, t * 0.013)).g;
                half ruido2 = SAMPLE_TEXTURE2D(_Ruido, sampler_Ruido, pos.xz * (_EspumaRuidoEscala * 2.3) - float2(t * 0.011, t * 0.017)).g;
                // Orilla: banda de borde neto, cortada por el ruido.
                half orilla = 1 - saturate(prof / _EspumaAncho);
                half umbral = 0.25 + 0.55 * ruido;
                half espuma = smoothstep(umbral - 0.04, umbral + 0.04, orilla);
                // Lineas que entran hacia la playa y se desarman al llegar.
                half alcance = 1 - saturate(prof / _LineasAlcance);
                half fase = frac(prof * _LineasFrecuencia + t * _LineasVelocidad + ruido * 0.4);
                half linea = smoothstep(1 - _LineasGrosor, 1 - _LineasGrosor + 0.04, fase) * (1 - smoothstep(0.96, 1.0, fase));
                linea *= smoothstep(0.3, 0.42, ruido2 * alcance + alcance * 0.35) * alcance;
                espuma = saturate(espuma + linea);
                half3 luzEspuma = luz.color * saturate(luz.direction.y + 0.35) * lerp(0.6, 1.0, sombra) + ambiente;
                col = lerp(col, _ColorEspuma.rgb * luzEspuma, espuma * _ColorEspuma.a);

                col = MixFog(col, i.fogFactor);

                // Perspectiva aerea, igual que la del pase de estilo sobre lo opaco.
                float tAire = saturate((eyeAgua - _AireParamsGlobal.x) / max(_AireParamsGlobal.y - _AireParamsGlobal.x, 0.01));
                tAire = tAire * tAire * (3.0 - 2.0 * tAire);
                half lAire = dot(col, half3(0.299, 0.587, 0.114));
                col = lerp(col, lAire.xxx, tAire * _AireParamsGlobal.w);
                col = lerp(col, _AireColorGlobal.rgb, tAire * _AireParamsGlobal.z);

                // Los ultimos centimetros se funden con la arena: sin esto se ve el
                // borde de los triangulos sobre la playa.
                half alfa = saturate(prof / max(_BordeSuave, 0.001));
                return half4(col, alfa);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
