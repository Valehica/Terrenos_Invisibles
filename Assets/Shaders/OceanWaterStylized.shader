Shader "Terrenos/OceanWaterStylized"
{
    Properties
    {
        [Header(Aguada  tres tonos continuos)]
        _ShallowColor ("Shallow Tint", Color) = (0.45, 0.88, 0.84, 1)
        _MidColor ("Mid Tone", Color) = (0.14, 0.52, 0.60, 1)
        _DeepColor ("Deep Color", Color) = (0.05, 0.25, 0.38, 1)
        _DepthMaxDistance ("Depth Fade Distance", Float) = 10
        _Desaturation ("Desaturacion", Range(0, 1)) = 0.12
        _SceneTint ("Translucidez (0 pintado  1 vidrio)", Range(0, 1)) = 0.35

        [Header(Papel  variacion de aguada)]
        _WashStrength ("Intensidad de aguada", Range(0, 0.5)) = 0.14
        _WashScale ("Escala de aguada (por metro)", Float) = 0.035

        [Header(Surface Detail)]
        [Normal] _NormalMap ("Normal Map (olas rodantes)", 2D) = "bump" {}
        [Normal] _NormalMap2 ("Normal Map 2 (ondas calmas)", 2D) = "bump" {}
        _NormalScale ("Normal Strength", Range(0, 2)) = 0.35
        _NormalTiling ("Normal Tiling (per meter)", Float) = 0.035
        _NormalSpeed ("Normal Scroll Speed", Float) = 0.03
        _Refraction ("Refraction", Range(0, 0.2)) = 0.03

        [Header(Olas  largos por encima del paso de la malla  8.13 m por quad)]
        _WaveA ("Wave A", Vector) = (-0.707, 0.707, 0.0314, 220)
        _WaveB ("Wave B", Vector) = (-0.940, 0.342, 0.0293, 150)
        _WaveC ("Wave C", Vector) = (-0.985, -0.174, 0.0251, 100)
        _WaveD ("Wave D", Vector) = (-0.819, -0.574, 0.0203, 68)
        _WaveSpeed ("Wave Speed", Float) = 0.65
        _WarpScale ("Escala de la deformacion", Float) = 0.004
        _WarpStrength ("Fuerza de la deformacion (m)", Float) = 26

        [Header(Resaca  el lavado de cada ola sobre la playa)]
        _ResacaAmplitud ("Amplitud (m de profundidad)", Float) = 0.45
        _ResacaVelocidad ("Ciclos por segundo", Float) = 0.11
        _ResacaAlcance ("Hasta que profundidad llega (m)", Float) = 3.0
        _ResacaVariacion ("Variacion a lo largo de la costa", Float) = 0.012

        [Header(Espuma  bordes blandos)]
        _FoamColor ("Foam Color", Color) = (1, 1, 1, 0.9)
        _FoamTex ("Foam Mask (RGB = patrones)", 2D) = "white" {}
        _ShoreFoamDistance ("Shore Foam Distance", Float) = 1.2
        _FoamNoiseScale ("Foam Noise Scale", Float) = 0.5
        _FoamSoftness ("Suavidad del borde", Range(0.02, 0.6)) = 0.32
        _CrestFoamHeight ("Crest Foam Height", Float) = 2.05
        _PigmentEdge ("Acumulacion de pigmento", Range(0, 1)) = 0.45

        [Header(Causticas)]
        _CausticTex ("Caustic Frames", 2DArray) = "" {}
        _CausticFrames ("Cantidad de cuadros", Float) = 32
        _CausticFPS ("Cuadros por segundo", Float) = 20
        _CausticScale ("Escala (por metro)", Float) = 0.25
        _CausticStrength ("Intensidad", Float) = 0.7
        _CausticMaxDepth ("Profundidad maxima visible", Float) = 4

        [Header(Reflection  tenue)]
        _Smoothness ("Smoothness", Range(0, 1)) = 0.72
        _FresnelPower ("Fresnel Power", Float) = 4
        _ReflectionStrength ("Reflection Strength", Range(0, 1)) = 0.35
        _SpecularStrength ("Sun Sheen", Float) = 0.35
        _SpecularBroad ("Ancho del brillo", Range(8, 400)) = 48

        [Header(Shore  bakeado del terreno)]
        _ShoreHeightMap ("Terrain Height Map (R = metros)", 2D) = "black" {}
        _ShoreMapOrigin ("Terrain Origin XZ", Vector) = (0, 0, 0, 0)
        _ShoreMapSize ("Terrain Size XZ", Vector) = (1000, 800, 0, 0)
        _WaveShoreFade ("Profundidad para ola completa", Float) = 3.5
        _EdgeFade ("Suavizado del borde de agua", Float) = 0.6
        _FarDepth ("Profundidad fuera del terreno", Float) = 30
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "OceanForward"
            Tags { "LightMode"="UniversalForward" }
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_NormalMap2); SAMPLER(sampler_NormalMap2);
            TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);
            TEXTURE2D_ARRAY(_CausticTex); SAMPLER(sampler_CausticTex);
            TEXTURE2D(_ShoreHeightMap); SAMPLER(sampler_ShoreHeightMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _MidColor, _DeepColor, _FoamColor;
                float _DepthMaxDistance, _Desaturation, _SceneTint;
                float _WashStrength, _WashScale;
                float4 _NormalMap_ST;
                float _NormalScale, _NormalTiling, _NormalSpeed, _Refraction;
                float4 _WaveA, _WaveB, _WaveC, _WaveD;
                float _WaveSpeed, _WarpScale, _WarpStrength;
                float _ResacaAmplitud, _ResacaVelocidad, _ResacaAlcance, _ResacaVariacion;
                float _ShoreFoamDistance, _FoamNoiseScale, _FoamSoftness, _CrestFoamHeight, _PigmentEdge;
                float _Smoothness, _FresnelPower, _ReflectionStrength, _SpecularStrength, _SpecularBroad;
                float4 _ShoreMapOrigin, _ShoreMapSize;
                float _WaveShoreFade, _EdgeFade, _FarDepth;
                float4 _FoamTex_ST;
                float _CausticFrames, _CausticFPS, _CausticScale, _CausticStrength, _CausticMaxDepth;
            CBUFFER_END

            // Globales que publica RenderShaderURP. Van fuera de UnityPerMaterial
            // a proposito: son del pase de estilo, no del material. Si el feature no
            // esta, quedan en cero y el bloque de abajo no modifica nada.
            float4 _AireColorGlobal;
            float4 _AireParamsGlobal;   // x = dist inicio, y = dist plena, z = tinte, w = desaturacion

            // Profundidad del agua en un punto: nivel del mar (origen del objeto)
            // menos la altura del terreno bakeada en el mapa.
            float WaterDepthAt(float2 worldXZ, float seaLevel)
            {
                float2 uv = (worldXZ - _ShoreMapOrigin.xy) / max(_ShoreMapSize.xy, 1e-4);
                float terrainH = SAMPLE_TEXTURE2D_LOD(_ShoreHeightMap, sampler_ShoreHeightMap, saturate(uv), 0).r;
                float dentro = seaLevel - terrainH;
                // Fuera del terreno no hay dato de altura. Saltar de golpe a _FarDepth
                // dibujaba una costura recta justo sobre el borde del mapa, asi que la
                // profundidad se mezcla en una franja de borde.
                float2 dBorde = min(uv, 1 - uv);
                float borde = saturate(min(dBorde.x, dBorde.y) / 0.03);
                return lerp(_FarDepth, dentro, borde);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 tangentWS  : TEXCOORD2;
                float3 bitangentWS: TEXCOORD3;
                float  eyeDepth   : TEXCOORD4;
                float  waveHeight : TEXCOORD5;
                float  fogFactor  : TEXCOORD6;
                float  shoreDepth : TEXCOORD7;
            };

            float3 Gerstner(float4 wave, float3 p, inout float3 tangent, inout float3 binormal)
            {
                float steepness = wave.z;
                float k = TWO_PI / max(wave.w, 0.01);
                float c = sqrt(9.8 / k);
                float2 d = normalize(wave.xy);
                float f = k * (dot(d, p.xz) - c * _Time.y * _WaveSpeed);
                float a = steepness / k;
                float s = sin(f), co = cos(f);

                tangent  += float3(-d.x * d.x * steepness * s, d.x * steepness * co, -d.x * d.y * steepness * s);
                binormal += float3(-d.x * d.y * steepness * s, d.y * steepness * co, -d.y * d.y * steepness * s);
                return float3(d.x * a * co, a * s, d.y * a * co);
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float ValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + 1), u.x), u.y);
            }

            // Resaca: la lengua de agua sube por la playa y se retira, cada pocos
            // segundos. No es la marea (que tarda horas) sino el lavado de cada ola,
            // y es lo que hace que una costa se vea viva. Solo mueve la franja somera:
            // mar adentro el nivel queda donde esta.
            float ResacaOffset(float2 worldXZ, float depth)
            {
                // La fase se corre a lo largo de la costa con ruido: si fuera igual en
                // todos lados la lengua entraria como una linea recta, que no pasa nunca.
                float fase = _Time.y * _ResacaVelocidad + ValueNoise(worldXZ * _ResacaVariacion);
                float f = frac(fase);

                // Asimetrica en el tiempo: entra rapido y se retira despacio, como el
                // agua real escurriendo por la arena.
                float onda = f < 0.3 ? smoothstep(0.0, 0.3, f) : 1.0 - smoothstep(0.3, 1.0, f);
                onda = onda * 2.0 - 1.0;

                // Se apaga con la profundidad, al cuadrado para que no se note mar adentro.
                float cerca = saturate(1.0 - depth / max(_ResacaAlcance, 0.01));
                return onda * _ResacaAmplitud * cerca * cerca;
            }

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 gridWS = TransformObjectToWorld(input.positionOS.xyz);

                // Las olas se aplanan al llegar a lo poco profundo: sin esto, en una
                // playa tendida la cresta inunda metros de arena y deja parches sueltos.
                float seaLevel = unity_ObjectToWorld._m13;
                float depth = WaterDepthAt(gridWS.xz, seaLevel);
                // La resaca corre la profundidad efectiva, asi que arrastra sola el borde
                // del agua, la espuma de orilla y el aplanado de olas en lo bajo.
                depth += ResacaOffset(gridWS.xz, depth);
                float waveFade = saturate((depth - 0.1) / max(_WaveShoreFade, 0.01));
                waveFade *= waveFade;

                // Sumar cuatro senoides da una celosia regular, y el ojo la lee como
                // cuadricula. Se corre el punto donde se evaluan las olas con un ruido
                // lento (deformacion de dominio): las crestas dejan de estar alineadas
                // sin que cambie ni su largo ni su altura.
                // Dos octavas: la primera mueve regiones enteras, la segunda rompe las
                // crestas entre si. Con una sola, los maximos de interferencia seguian
                // alineados y la espuma de cresta los delataba como manchas en retícula.
                float2 warpA = float2(ValueNoise(gridWS.xz * _WarpScale),
                                      ValueNoise(gridWS.xz * _WarpScale + 37.1)) - 0.5;
                float2 warpB = float2(ValueNoise(gridWS.xz * _WarpScale * 4.3),
                                      ValueNoise(gridWS.xz * _WarpScale * 4.3 + 11.7)) - 0.5;
                float3 muestraWS = gridWS;
                muestraWS.xz += warpA * _WarpStrength + warpB * (_WarpStrength * 0.42);

                float3 tangent = float3(1, 0, 0);
                float3 binormal = float3(0, 0, 1);
                float3 offset = 0;
                offset += Gerstner(_WaveA, muestraWS, tangent, binormal);
                offset += Gerstner(_WaveB, muestraWS, tangent, binormal);
                offset += Gerstner(_WaveC, muestraWS, tangent, binormal);
                offset += Gerstner(_WaveD, muestraWS, tangent, binormal);

                float3 p = gridWS + offset * waveFade;
                tangent = lerp(float3(1, 0, 0), tangent, waveFade);
                binormal = lerp(float3(0, 0, 1), binormal, waveFade);
                o.shoreDepth = depth;

                o.positionWS = p;
                o.normalWS = normalize(cross(binormal, tangent));
                o.tangentWS = normalize(tangent);
                o.bitangentWS = normalize(binormal);
                o.waveHeight = offset.y * waveFade;
                o.positionCS = TransformWorldToHClip(p);
                o.eyeDepth = -TransformWorldToView(p).z;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                float t = _Time.y;
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);

                // Detail normals: two scrolling layers
                float2 uv1 = i.positionWS.xz * _NormalTiling + t * _NormalSpeed * float2(0.8, 0.6);
                // Rotada ~35 grados y con relacion de escala no entera: si las dos capas
                // repiten en el mismo eje y a multiplo exacto, el tiling se ve.
                const float2 ejeB = float2(0.819, 0.574);
                float2 xzB = float2(dot(i.positionWS.xz, ejeB), dot(i.positionWS.xz, float2(-ejeB.y, ejeB.x)));
                float2 uv2 = xzB * _NormalTiling * 2.37 + t * _NormalSpeed * float2(-0.5, 0.9);
                float3 n1 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv1), _NormalScale);
                float3 n2 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap2, sampler_NormalMap2, uv2), _NormalScale);
                float3 nTS = normalize(float3(n1.xy + n2.xy, n1.z * n2.z));
                float3 N = normalize(i.tangentWS * nTS.x + i.bitangentWS * nTS.y + i.normalWS * nTS.z);
                if (!isFrontFace) N = -N;

                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);

                // Water depth behind this pixel
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float depth = max(sceneEye - i.eyeDepth, 0);

                // Refraction (reject samples from objects in front of the water)
                float2 refrUV = screenUV + nTS.xy * _Refraction * saturate(depth * 0.5);
                float refrEye = LinearEyeDepth(SampleSceneDepth(refrUV), _ZBufferParams);
                if (refrEye < i.eyeDepth) { refrUV = screenUV; refrEye = sceneEye; }
                float refrDepth = max(refrEye - i.eyeDepth, 0);
                half3 sceneColor = SampleSceneColor(refrUV);

                // Cáusticas sobre el fondo: se reconstruye el punto del fondo a lo largo
                // del rayo de vista y se proyecta la animación en su posición XZ.
                float3 camFwd = -UNITY_MATRIX_V[2].xyz;
                float rayScale = 1.0 / max(dot(-V, camFwd), 0.1);
                float3 floorWS = i.positionWS - V * (refrDepth * rayScale);
                float frame = floor(fmod(_Time.y * _CausticFPS, max(_CausticFrames, 1)));
                float2 cuv = floorWS.xz * _CausticScale + nTS.xy * 0.05;
                float caustic = SAMPLE_TEXTURE2D_ARRAY(_CausticTex, sampler_CausticTex, cuv, frame).r;
                caustic *= SAMPLE_TEXTURE2D_ARRAY(_CausticTex, sampler_CausticTex, cuv * 0.63 + 0.37, fmod(frame + 11, max(_CausticFrames, 1))).r * 1.6;
                float causticFade = saturate(1 - refrDepth / max(_CausticMaxDepth, 0.01)) * saturate(refrDepth * 4);
                Light sun = GetMainLight();
                sceneColor += caustic * causticFade * _CausticStrength * sun.color * saturate(sun.direction.y);

                // Aguada de tres tonos. La profundidad recorre shallow -> mid -> deep en
                // un degradé continuo: lo que da el aire pintado no es cortar el color en
                // bandas sino la desaturación y la variación de pigmento de más abajo.
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float NdotL = saturate(dot(N, mainLight.direction));
                half3 lit = mainLight.color * (0.45 + 0.55 * NdotL) + SampleSH(N);
                float absorb = 1 - exp(-refrDepth / max(_DepthMaxDistance, 0.01) * 3);
                half3 wash = absorb < 0.5
                    ? lerp(_ShallowColor.rgb, _MidColor.rgb, smoothstep(0, 0.5, absorb))
                    : lerp(_MidColor.rgb, _DeepColor.rgb, smoothstep(0.5, 1, absorb));
                wash *= lit;

                // El fondo refractado entra apenas: un agua pintada no es un vidrio, y
                // dejar pasar el terreno entero delata el 3D bajo la ilusión.
                half3 col = lerp(wash, sceneColor * _ShallowColor.rgb, (1 - absorb) * _SceneTint);

                // Reflection + fresnel (tenue: el espejo perfecto lee como realista)
                float3 R = reflect(-V, N);
                half3 refl = GlossyEnvironmentReflection(R, 1 - _Smoothness, 1);
                float fresnel = 0.02 + 0.98 * pow(1 - saturate(dot(N, V)), _FresnelPower);
                col = lerp(col, refl, fresnel * _ReflectionStrength);

                // Brillo ancho en lugar del punto especular: el sol sobre una acuarela
                // es una zona de luz difusa, no un destello puntual.
                float3 H = normalize(mainLight.direction + V);
                col += pow(saturate(dot(N, H)), _SpecularBroad) * mainLight.color * _SpecularStrength * mainLight.shadowAttenuation;

                // Luces extra (por ejemplo la linterna del jugador)
                #ifdef _ADDITIONAL_LIGHTS
                uint lightCount = GetAdditionalLightsCount();
                for (uint li = 0u; li < lightCount; li++)
                {
                    Light extra = GetAdditionalLight(li, i.positionWS);
                    float atten = extra.distanceAttenuation;
                    float3 Hx = normalize(extra.direction + V);
                    col += extra.color * atten * (saturate(dot(N, extra.direction)) * 0.25
                         + pow(saturate(dot(N, Hx)), _SpecularBroad * 1.5) * 0.4);
                }
                #endif

                // Variación de aguada: manchas grandes y lentas de valor, como pigmento
                // asentándose desparejo sobre el papel. Es lo que rompe el color plano.
                float wash1 = ValueNoise(i.positionWS.xz * _WashScale + t * 0.008);
                float wash2 = ValueNoise(i.positionWS.xz * _WashScale * 2.3 - t * 0.005);
                float washVar = (wash1 * 0.65 + wash2 * 0.35 - 0.5) * 2;
                col *= 1 + washVar * _WashStrength;

                // Desaturación global, antes de la espuma para que el blanco quede limpio.
                float lum = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(col, lum.xxx, _Desaturation);

                // Espuma de borde blando. Se fue la modulación senoidal de la versión
                // realista: esas bandas rítmicas leen como gráfico, no como pintura.
                float2 foamUV = i.positionWS.xz * _FoamNoiseScale * 0.12;
                float foamA = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, foamUV + t * float2(0.012, 0.02)).r;
                float foamB = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, foamUV * 1.9 - t * float2(0.018, 0.009)).g;
                // Tercera octava de ruido de valor: rompe la repetición de la textura y
                // da el contorno irregular de una mancha de pintura.
                float foamC = ValueNoise(i.positionWS.xz * 0.6 + t * 0.03);
                float noise = 1 - saturate(foamA * 0.5 + foamB * 0.5 + (foamC - 0.5) * 0.4);
                float shore = 1 - saturate(depth / max(_ShoreFoamDistance, 0.01));
                shore = smoothstep(0, 1, shore);
                float shoreFoam = smoothstep(noise - _FoamSoftness, noise + _FoamSoftness, shore);
                shoreFoam *= saturate(i.shoreDepth / 0.12);   // sin espuma sobre la arena seca
                float crest = saturate((i.waveHeight - _CrestFoamHeight) * 1.2);
                float crestFoam = smoothstep(0.45, 0.85, noise) * crest;
                float foam = saturate(max(shoreFoam, crestFoam));

                // Acumulación de pigmento: en una acuarela el color se concentra en el
                // borde de la mancha al secarse. Se oscurece la franja de transición,
                // que es donde foam*(1-foam) tiene su máximo.
                float edge = saturate(foam * (1 - foam) * 4);
                col *= 1 - edge * _PigmentEdge * 0.5;

                half3 foamCol = _FoamColor.rgb * (mainLight.color * (0.6 + 0.4 * NdotL) + SampleSH(float3(0, 1, 0)));
                col = lerp(col, foamCol, foam * _FoamColor.a);

                col = MixFog(col, i.fogFactor);

                // Perspectiva aerea, la misma que aplica el pase de estilo general a
                // lo opaco. Aca va a mano porque el agua no escribe profundidad.
                float tAire = saturate((i.eyeDepth - _AireParamsGlobal.x)
                            / max(_AireParamsGlobal.y - _AireParamsGlobal.x, 0.01));
                tAire = tAire * tAire * (3.0 - 2.0 * tAire);
                float lAire = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(col, lAire.xxx, tAire * _AireParamsGlobal.w);
                col = lerp(col, _AireColorGlobal.rgb, tAire * _AireParamsGlobal.z);

                // El borde del agua se desvanece en los últimos centímetros de
                // profundidad: así la línea de costa no muestra el borde de los
                // triángulos ni parches sueltos al subir la marea.
                float alpha = saturate(i.shoreDepth / max(_EdgeFade, 0.01));
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
